using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Layout;

namespace Inceptus.DocumentEngine.UnitTests.Layout;

public sealed class LayoutEngineTests
{
    private static readonly DocumentId DocumentId = new("test:layout-document");
    private static readonly DocumentRevision Revision = new(23);
    private static readonly AlgorithmId AlgorithmAId = new("test:layout:a");
    private static readonly AlgorithmId AlgorithmZId = new("test:layout:z");
    private static readonly ProjectionRuleId ProjectionRuleId = new("test:projection:node");
    private static readonly SemanticTypeId NodeTypeId = new("test:node");

    [Fact]
    public void LayoutPassesTheExactImmutableInputsAndBuildsACompleteProvenanceBearingResult()
    {
        var graph = Graph();
        var context = new LayoutContext(
            [new("test:spacing", PropertyValue.FromNumber(25d))]);
        ProjectedGraph? observedGraph = null;
        LayoutContext? observedContext = null;
        CancellationToken observedToken = default;
        using var cancellation = new CancellationTokenSource();
        var algorithm = new DelegateAlgorithm((input, options, token) =>
        {
            observedGraph = input;
            observedContext = options;
            observedToken = token;
            return Complete(input, reverse: true);
        });
        var engine = Engine((AlgorithmAId, algorithm));

        var result = engine.Layout(graph, AlgorithmAId, context, cancellation.Token);

        Assert.True(result.IsSuccessful);
        Assert.Same(graph, observedGraph);
        Assert.Same(context, observedContext);
        Assert.Equal(cancellation.Token, observedToken);
        var layout = Assert.IsType<LayoutResult>(result.Result);
        Assert.Equal(DocumentId, layout.DocumentId);
        Assert.Equal(Revision, layout.SourceRevision);
        Assert.Equal(AlgorithmAId, layout.AlgorithmId);
        Assert.Equal(graph.Nodes.Select(static node => node.Id),
            layout.Nodes.Select(static geometry => geometry.ProjectedObjectId));
        Assert.Equal(graph.Groups.Select(static group => group.Id),
            layout.Groups.Select(static geometry => geometry.ProjectedObjectId));
        Assert.Equal(graph.Nodes.Length, layout.NodeCount);
        Assert.Equal(graph.Groups.Length, layout.GroupCount);
    }

    [Fact]
    public void ExplicitSelectionIsIndependentOfRegistrationOrderAndRegistriesAreInstanceLocal()
    {
        var graph = Graph(includeGroup: false);
        var calls = new List<string>();
        var algorithmA = new DelegateAlgorithm((input, _, _) =>
        {
            calls.Add("a");
            return Complete(input, metadataValue: "a");
        });
        var algorithmZ = new DelegateAlgorithm((input, _, _) =>
        {
            calls.Add("z");
            return Complete(input, metadataValue: "z");
        });
        var first = Engine((AlgorithmZId, algorithmZ), (AlgorithmAId, algorithmA));
        var second = Engine((AlgorithmAId, algorithmA), (AlgorithmZId, algorithmZ));

        var firstResult = first.Layout(graph, AlgorithmAId);
        var secondResult = second.Layout(graph, AlgorithmAId);
        var isolated = new LayoutEngine().Layout(graph, AlgorithmAId);

        Assert.Equal(["a", "a"], calls);
        Assert.Equal(firstResult, secondResult);
        Assert.Equal("a", Assert.IsType<LayoutResult>(firstResult.Result)
            .Metadata["test:algorithm"].TextValue);
        Assert.Equal(LayoutExecutionStatus.Failed, isolated.Status);
        Assert.Equal(
            LayoutDiagnosticCodes.MissingAlgorithm,
            Assert.Single(isolated.Diagnostics).Code);
    }

    [Fact]
    public void MissingAlgorithmInvokesNothingAndReturnsNoPartialResult()
    {
        var graph = Graph(includeGroup: false);
        var calls = 0;
        var registered = new DelegateAlgorithm((input, _, _) =>
        {
            calls++;
            return Complete(input);
        });

        var result = Engine((AlgorithmAId, registered)).Layout(graph, AlgorithmZId);

        Assert.Equal(0, calls);
        Assert.Equal(LayoutExecutionStatus.Failed, result.Status);
        Assert.Null(result.Result);
        Assert.Equal(LayoutDiagnosticCodes.MissingAlgorithm,
            Assert.Single(result.Diagnostics).Code);
        Assert.Equal(DocumentId, result.DocumentId);
        Assert.Equal(Revision, result.SourceRevision);
        Assert.Equal(AlgorithmZId, result.AlgorithmId);
    }

    [Fact]
    public void MalformedLayoutContextFailsInputValidationBeforeAlgorithmExecution()
    {
        var graph = Graph(includeGroup: false);
        var context = new LayoutContext();
        var optionsField = typeof(LayoutContext).GetField(
            "<Options>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(optionsField);
        optionsField.SetValue(context, null);
        var calls = 0;
        var engine = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, _) =>
            {
                calls++;
                return Complete(input);
            })));

        var result = engine.Layout(graph, AlgorithmAId, context);

        Assert.Equal(0, calls);
        Assert.Equal(LayoutExecutionStatus.Failed, result.Status);
        Assert.Null(result.Result);
        Assert.Equal(LayoutDiagnosticCodes.InvalidInput,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void DuplicateAlgorithmRegistrationIsRejectedDeterministically()
    {
        var algorithm = new DelegateAlgorithm((input, _, _) => Complete(input));
        var first = new LayoutAlgorithmRegistration(AlgorithmAId, algorithm);
        var second = new LayoutAlgorithmRegistration(
            new AlgorithmId(AlgorithmAId.Value),
            algorithm);

        var exception = Assert.Throws<ArgumentException>(() =>
            new LayoutEngine([first, second]));

        Assert.Contains(AlgorithmAId.Value, exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            LayoutDiagnosticCodes.DuplicateAlgorithmRegistration,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryDefensivelyCopiesConfigurationAndRejectsNullEntries()
    {
        var graph = Graph(includeGroup: false);
        var registration = new LayoutAlgorithmRegistration(
            AlgorithmAId,
            new DelegateAlgorithm((input, _, _) => Complete(input)));
        var registrations = new List<LayoutAlgorithmRegistration> { registration };
        var engine = new LayoutEngine(registrations);
        registrations.Clear();

        var result = engine.Layout(graph, AlgorithmAId);

        Assert.True(result.IsSuccessful);
        Assert.Throws<ArgumentException>(() =>
            new LayoutEngine([null!]));
    }

    [Fact]
    public void EqualInputsAndEquivalentContributionOrdersProduceEqualCanonicalResults()
    {
        var firstGraph = Graph();
        var secondGraph = Graph();
        var firstEngine = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, _) => Complete(input, reverse: false))));
        var secondEngine = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, _) => Complete(input, reverse: true))));

        var first = firstEngine.Layout(firstGraph, AlgorithmAId, LayoutContext.Empty);
        var second = secondEngine.Layout(secondGraph, AlgorithmAId, new LayoutContext());
        var third = firstEngine.Layout(firstGraph, AlgorithmAId, LayoutContext.Empty);

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void WarningsArePreservedButReportedAlgorithmFailureExposesNoResult()
    {
        var graph = Graph(includeGroup: false);
        var warning = new Diagnostic(
            "TEST_LAYOUT_WARNING",
            DiagnosticSeverity.Warning,
            "warning");
        var successful = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, _) => Complete(input, diagnostics: [warning]))))
            .Layout(graph, AlgorithmAId);
        var failed = Engine((AlgorithmAId,
            new DelegateAlgorithm((_, _, _) => LayoutAlgorithmResult.Failure(
                [new Diagnostic("TEST_FAILURE", DiagnosticSeverity.Error, "failed")]))))
            .Layout(graph, AlgorithmAId);

        Assert.True(successful.IsSuccessful);
        Assert.Equal(warning.Code, Assert.Single(successful.Diagnostics).Code);
        Assert.Equal(warning.Code,
            Assert.Single(Assert.IsType<LayoutResult>(successful.Result).Diagnostics).Code);
        Assert.Equal(LayoutExecutionStatus.Failed, failed.Status);
        Assert.Null(failed.Result);
        Assert.Equal("TEST_FAILURE", Assert.Single(failed.Diagnostics).Code);
    }

    [Theory]
    [InlineData(AlgorithmFault.Throw)]
    [InlineData(AlgorithmFault.NullResult)]
    [InlineData(AlgorithmFault.UnrelatedCancellation)]
    public void AlgorithmFaultsBecomeDiagnosticsWithoutPartialResults(AlgorithmFault fault)
    {
        var graph = Graph(includeGroup: false);
        var algorithm = new DelegateAlgorithm((input, _, _) => fault switch
        {
            AlgorithmFault.Throw => throw new InvalidOperationException("plugin fault"),
            AlgorithmFault.NullResult => null!,
            AlgorithmFault.UnrelatedCancellation => throw new OperationCanceledException(),
            _ => Complete(input),
        });

        var result = Engine((AlgorithmAId, algorithm)).Layout(graph, AlgorithmAId);

        Assert.Equal(LayoutExecutionStatus.Failed, result.Status);
        Assert.Null(result.Result);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code is LayoutDiagnosticCodes.AlgorithmFailure or
                LayoutDiagnosticCodes.InvalidAlgorithmResult);
    }

    [Fact]
    public void IncompleteAndUnexpectedGeometryAreRejectedWithoutAPartialResult()
    {
        var graph = Graph();
        var foreignId = new ProjectedObjectId("test:projected:foreign");
        var incomplete = Engine((AlgorithmAId,
            new DelegateAlgorithm((_, _, _) => LayoutAlgorithmResult.Success(
                new LayoutComputation()))))
            .Layout(graph, AlgorithmAId);
        var unexpected = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, _) => LayoutAlgorithmResult.Success(
                new LayoutComputation(
                    input.Nodes.Select(NodeGeometry)
                        .Append(new LayoutNodeGeometry(
                            foreignId,
                            new RectD(0d, 0d, 10d, 10d),
                            Matrix2D.Identity)),
                    input.Groups.Select(GroupGeometry))))))
            .Layout(graph, AlgorithmAId);

        Assert.Equal(LayoutExecutionStatus.Failed, incomplete.Status);
        Assert.Null(incomplete.Result);
        Assert.Contains(incomplete.Diagnostics,
            diagnostic => diagnostic.Code == LayoutDiagnosticCodes.MissingGeometry);
        Assert.Equal(LayoutExecutionStatus.Failed, unexpected.Status);
        Assert.Null(unexpected.Result);
        Assert.Contains(unexpected.Diagnostics,
            diagnostic => diagnostic.Code == LayoutDiagnosticCodes.UnexpectedGeometry);
    }

    [Fact]
    public void GeometryForTheWrongProjectedCategoryIsRejected()
    {
        var graph = Graph();
        var nodeId = graph.Nodes[0].Id;
        var groupId = graph.Groups[0].Id;
        var result = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, _) => LayoutAlgorithmResult.Success(
                new LayoutComputation(
                    input.Nodes.Skip(1).Select(NodeGeometry)
                        .Append(new LayoutNodeGeometry(
                            groupId,
                            new RectD(0d, 0d, 10d, 10d),
                            Matrix2D.Identity)),
                    [new LayoutGroupGeometry(nodeId, new RectD(0d, 0d, 10d, 10d))])))))
            .Layout(graph, AlgorithmAId);

        Assert.Equal(LayoutExecutionStatus.Failed, result.Status);
        Assert.Null(result.Result);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == LayoutDiagnosticCodes.UnexpectedGeometry);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == LayoutDiagnosticCodes.MissingGeometry);
    }

    [Fact]
    public void MalformedAlgorithmGeometryIsIndependentlyRejectedByTheEngine()
    {
        var graph = Graph(includeGroup: false);
        var computation = Assert.IsType<LayoutComputation>(Complete(graph).Computation);
        var geometry = computation.Nodes[0];
        object boxedBounds = geometry.Bounds;
        var widthField = typeof(RectD).GetField(
            "<Width>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(widthField);
        widthField.SetValue(boxedBounds, double.NaN);
        var boundsField = typeof(LayoutNodeGeometry).GetField(
            "<Bounds>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(boundsField);
        boundsField.SetValue(geometry, (RectD)boxedBounds);
        var engine = Engine((AlgorithmAId,
            new DelegateAlgorithm((_, _, _) =>
                LayoutAlgorithmResult.Success(computation))));

        var result = engine.Layout(graph, AlgorithmAId);

        Assert.Equal(LayoutExecutionStatus.Failed, result.Status);
        Assert.Null(result.Result);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == LayoutDiagnosticCodes.InvalidGeometry);
    }

    [Fact]
    public void NegativeEffectiveNodeGeometryIsRejectedByTheGenericLayoutBoundary()
    {
        var graph = Graph(includeGroup: false);
        var target = graph.Nodes.First(node =>
            node.PlacementHint?.PlacementMode != VisualPlacementMode.Pinned);
        var result = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, _) => Complete(
                input,
                overrideBounds: id => id == target.Id
                    ? new RectD(-1d, 20d, 100d, 50d)
                    : null))))
            .Layout(graph, AlgorithmAId);

        Assert.Equal(LayoutExecutionStatus.Failed, result.Status);
        Assert.Null(result.Result);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == LayoutDiagnosticCodes.InvalidGeometry);
    }

    [Fact]
    public void ZeroExtentEffectiveNodeGeometryIsRejected()
    {
        var graph = Graph(includeGroup: false);
        var target = graph.Nodes.First(node =>
            node.PlacementHint?.PlacementMode != VisualPlacementMode.Pinned);
        var result = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, _) => Complete(
                input,
                overrideBounds: id => id == target.Id
                    ? new RectD(0d, 20d, 0d, 50d)
                    : null))))
            .Layout(graph, AlgorithmAId);

        Assert.Equal(LayoutExecutionStatus.Failed, result.Status);
        Assert.Null(result.Result);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == LayoutDiagnosticCodes.InvalidGeometry);
    }

    [Fact]
    public void PinnedPlacementIsFixedWhileManualAndAutomaticHintsRemainAlgorithmGuidance()
    {
        var graph = Graph(includeGroup: false);
        var pinned = graph.Nodes.Single(node =>
            node.PlacementHint?.PlacementMode == VisualPlacementMode.Pinned);
        var manual = graph.Nodes.Single(node =>
            node.PlacementHint?.PlacementMode == VisualPlacementMode.Manual);
        var automatic = graph.Nodes.Single(node =>
            node.PlacementHint?.PlacementMode == VisualPlacementMode.Automatic);
        ProjectedPlacementHint? observedPinnedHint = null;
        var invalidPinned = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, _) =>
            {
                observedPinnedHint = input.Nodes.Single(node => node.Id == pinned.Id).PlacementHint;
                return Complete(
                    input,
                    overrideBounds: id => id == pinned.Id
                        ? new RectD(999d, 999d, 10d, 10d)
                        : null);
            }))).Layout(graph, AlgorithmAId);
        var valid = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, _) => Complete(
                input,
                overrideBounds: id => id == manual.Id
                    ? new RectD(500d, 500d, 80d, 30d)
                    : id == automatic.Id
                        ? new RectD(700d, 500d, 90d, 35d)
                        : null))))
            .Layout(graph, AlgorithmAId);

        Assert.Same(pinned.PlacementHint, observedPinnedHint);
        Assert.Equal(LayoutExecutionStatus.Failed, invalidPinned.Status);
        Assert.Null(invalidPinned.Result);
        Assert.Contains(invalidPinned.Diagnostics,
            diagnostic => diagnostic.Code == LayoutDiagnosticCodes.PinnedPlacementViolation);
        Assert.True(valid.IsSuccessful);
        var layout = Assert.IsType<LayoutResult>(valid.Result);
        Assert.Equal(new RectD(500d, 500d, 80d, 30d),
            layout.Nodes.Single(geometry => geometry.ProjectedObjectId == manual.Id).Bounds);
        Assert.Equal(new RectD(700d, 500d, 90d, 35d),
            layout.Nodes.Single(geometry => geometry.ProjectedObjectId == automatic.Id).Bounds);
        Assert.Equal(graph, Graph(includeGroup: false));
    }

    [Fact]
    public void BoundaryAttachmentPostPassUsesFinalOwnerBoundsAndPreservesTransformBasis()
    {
        var ownerSemanticId = new SemanticElementId("test:semantic:owner");
        var attachedSemanticId = new SemanticElementId("test:semantic:attached");
        var owner = new ProjectedNode(
            SourceForSemantic("owner", ownerSemanticId),
            new ProjectedPlacementHint(
                new PointD(10d, 20d),
                new SizeD(100d, 60d),
                VisualPlacementMode.Automatic));
        var attached = new ProjectedNode(
            SourceForSemantic("attached", attachedSemanticId),
            new ProjectedPlacementHint(
                new PointD(42d, 62d),
                new SizeD(36d, 36d),
                VisualPlacementMode.Manual,
                new ProjectedBoundaryAttachment(
                    ownerSemanticId,
                    new BoundaryAttachmentPlacement(
                        BoundaryAttachmentSide.Bottom,
                        0.25d))),
            geometryInteractionPolicy:
                NodeGeometryInteractionPolicy.AttachedBoundaryMoveFixedSize);
        var graph = new ProjectedGraph(DocumentId, Revision, [owner, attached]);
        var result = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, _) => Complete(
                input,
                overrideBounds: id => id == owner.Id
                    ? new RectD(200d, 100d, 120d, 80d)
                    : id == attached.Id
                        ? new RectD(700d, 500d, 36d, 36d)
                        : null))))
            .Layout(graph, AlgorithmAId);

        Assert.True(result.IsSuccessful);
        var layout = Assert.IsType<LayoutResult>(result.Result);
        var ownerGeometry = layout.Nodes.Single(geometry =>
            geometry.ProjectedObjectId == owner.Id);
        var attachedGeometry = layout.Nodes.Single(geometry =>
            geometry.ProjectedObjectId == attached.Id);
        Assert.Equal(new RectD(200d, 100d, 120d, 80d), ownerGeometry.Bounds);
        Assert.Equal(new RectD(212d, 162d, 36d, 36d), attachedGeometry.Bounds);
        Assert.Equal(1d, attachedGeometry.Transform.M11);
        Assert.Equal(1d, attachedGeometry.Transform.M22);
        Assert.Equal(212d, attachedGeometry.Transform.OffsetX);
        Assert.Equal(162d, attachedGeometry.Transform.OffsetY);
    }

    [Fact]
    public void BoundaryAttachmentPostPassRejectsDuplicateAlgorithmGeometryWithoutThrowing()
    {
        var ownerSemanticId = new SemanticElementId("test:semantic:duplicate-owner");
        var owner = new ProjectedNode(
            SourceForSemantic("duplicate-owner", ownerSemanticId),
            new ProjectedPlacementHint(
                new PointD(100d, 100d),
                new SizeD(100d, 60d),
                VisualPlacementMode.Automatic));
        var attached = new ProjectedNode(
            SourceForSemantic(
                "duplicate-attached",
                new SemanticElementId("test:semantic:duplicate-attached")),
            new ProjectedPlacementHint(
                new PointD(132d, 142d),
                new SizeD(36d, 36d),
                VisualPlacementMode.Manual,
                new ProjectedBoundaryAttachment(
                    ownerSemanticId,
                    new BoundaryAttachmentPlacement(
                        BoundaryAttachmentSide.Bottom,
                        0.5d))));
        var graph = new ProjectedGraph(DocumentId, Revision, [owner, attached]);
        var computation = Assert.IsType<LayoutComputation>(Complete(graph).Computation);
        var nodesField = typeof(LayoutComputation).GetField(
            "<Nodes>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(nodesField);
        nodesField.SetValue(computation, computation.Nodes.Add(computation.Nodes[0]));
        var result = Engine((AlgorithmAId,
            new DelegateAlgorithm((_, _, _) =>
                LayoutAlgorithmResult.Success(computation))))
            .Layout(graph, AlgorithmAId);

        Assert.Equal(LayoutExecutionStatus.Failed, result.Status);
        Assert.Null(result.Result);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == LayoutDiagnosticCodes.DuplicateGeometry);
    }

    [Fact]
    public void BoundaryAttachmentPostPassRejectsDerivedNegativeGeometry()
    {
        var ownerSemanticId = new SemanticElementId("test:semantic:owner-near-boundary");
        var owner = new ProjectedNode(
            SourceForSemantic("owner-near-boundary", ownerSemanticId),
            new ProjectedPlacementHint(
                new PointD(0d, 20d),
                new SizeD(100d, 60d),
                VisualPlacementMode.Automatic));
        var attached = new ProjectedNode(
            SourceForSemantic(
                "attached-near-boundary",
                new SemanticElementId("test:semantic:attached-near-boundary")),
            new ProjectedPlacementHint(
                new PointD(0d, 0d),
                new SizeD(36d, 36d),
                VisualPlacementMode.Manual,
                new ProjectedBoundaryAttachment(
                    ownerSemanticId,
                    new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Left, 0.5d))));
        var graph = new ProjectedGraph(DocumentId, Revision, [owner, attached]);

        var result = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, _) => Complete(input))))
            .Layout(graph, AlgorithmAId);

        Assert.Equal(LayoutExecutionStatus.Failed, result.Status);
        Assert.Null(result.Result);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == LayoutDiagnosticCodes.InvalidGeometry);
    }

    [Fact]
    public void CancellationBeforeDuringAndImmediatelyAfterAlgorithmExecutionReturnsNoResult()
    {
        var graph = Graph(includeGroup: false);
        var calls = 0;
        using var before = new CancellationTokenSource();
        before.Cancel();
        var beforeResult = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, _) =>
            {
                calls++;
                return Complete(input);
            }))).Layout(graph, AlgorithmAId, cancellationToken: before.Token);

        using var during = new CancellationTokenSource();
        var duringResult = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, _) =>
            {
                calls++;
                var result = Complete(input);
                during.Cancel();
                return result;
            }))).Layout(graph, AlgorithmAId, cancellationToken: during.Token);

        using var observed = new CancellationTokenSource();
        var observedResult = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, _, token) =>
            {
                calls++;
                observed.Cancel();
                token.ThrowIfCancellationRequested();
                return Complete(input);
            }))).Layout(graph, AlgorithmAId, cancellationToken: observed.Token);

        Assert.Equal(2, calls);
        AssertCancelled(beforeResult);
        AssertCancelled(duringResult);
        AssertCancelled(observedResult);
    }

    [Fact]
    public void EngineOwnedValidationTraversalObservesCancellation()
    {
        var graph = Graph(includeGroup: false);
        var computation = Assert.IsType<LayoutComputation>(Complete(graph).Computation);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var validate = typeof(LayoutEngine).GetMethod(
            "ValidateComputation",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(validate);

        var exception = Assert.Throws<TargetInvocationException>(() => validate.Invoke(
            null,
            [graph, computation, new List<Diagnostic>(), cancellation.Token]));

        Assert.IsType<OperationCanceledException>(exception.InnerException);
    }

    [Fact]
    public void EmptyGraphProducesACompleteEmptyLayout()
    {
        var graph = new ProjectedGraph(DocumentId, Revision);
        var result = Engine((AlgorithmAId,
            new DelegateAlgorithm((_, _, _) =>
                LayoutAlgorithmResult.Success(LayoutComputation.Empty))))
            .Layout(graph, AlgorithmAId);

        Assert.True(result.IsSuccessful);
        Assert.Empty(Assert.IsType<LayoutResult>(result.Result).Nodes);
        Assert.Empty(Assert.IsType<LayoutResult>(result.Result).Groups);
    }

    private static LayoutEngine Engine(
        params (AlgorithmId Id, ILayoutAlgorithm Algorithm)[] algorithms) =>
        new(algorithms.Select(static item =>
            new LayoutAlgorithmRegistration(item.Id, item.Algorithm)));

    private static ProjectedGraph Graph(bool includeGroup = true)
    {
        var automatic = Node(
            "automatic",
            new ProjectedPlacementHint(
                new PointD(10d, 20d),
                new SizeD(80d, 30d),
                VisualPlacementMode.Automatic));
        var manual = Node(
            "manual",
            new ProjectedPlacementHint(
                new PointD(120d, 20d),
                new SizeD(90d, 35d),
                VisualPlacementMode.Manual));
        var pinned = Node(
            "pinned",
            new ProjectedPlacementHint(
                new PointD(240d, 20d),
                new SizeD(100d, 40d),
                VisualPlacementMode.Pinned));
        var nodes = new[] { pinned, automatic, manual };
        var groups = includeGroup
            ? [new ProjectedGroup(Source("group", ProjectedObjectKind.Group), nodes.Select(n => n.Id))]
            : Array.Empty<ProjectedGroup>();

        return new ProjectedGraph(DocumentId, Revision, nodes, groups: groups);
    }

    private static ProjectedNode Node(
        string localKey,
        ProjectedPlacementHint placementHint) =>
        new(Source(localKey, ProjectedObjectKind.Node), placementHint);

    private static ProjectionSourceTrace Source(
        string localKey,
        ProjectedObjectKind kind) =>
        new(
            DocumentId,
            ProjectionRuleId,
            ProjectionSourceKind.SemanticElement,
            new SemanticElementId($"test:semantic:{kind.ToString().ToLowerInvariant()}:{localKey}"),
            NodeTypeId,
            localKey);

    private static ProjectionSourceTrace SourceForSemantic(
        string localKey,
        SemanticElementId semanticElementId) =>
        new(
            DocumentId,
            ProjectionRuleId,
            ProjectionSourceKind.SemanticElement,
            semanticElementId,
            NodeTypeId,
            localKey);

    private static LayoutAlgorithmResult Complete(
        ProjectedGraph graph,
        bool reverse = false,
        string? metadataValue = null,
        IEnumerable<Diagnostic>? diagnostics = null,
        Func<ProjectedObjectId, RectD?>? overrideBounds = null)
    {
        var nodes = graph.Nodes
            .Select(node => NodeGeometry(node, overrideBounds?.Invoke(node.Id)))
            .ToArray();
        var groups = graph.Groups.Select(GroupGeometry).ToArray();
        if (reverse)
        {
            Array.Reverse(nodes);
            Array.Reverse(groups);
        }

        return LayoutAlgorithmResult.Success(
            new LayoutComputation(
                nodes,
                groups,
                metadataValue is null
                    ? null
                    : [new("test:algorithm", PropertyValue.FromText(metadataValue))]),
            diagnostics);
    }

    private static LayoutNodeGeometry NodeGeometry(ProjectedNode node) =>
        NodeGeometry(node, null);

    private static LayoutNodeGeometry NodeGeometry(
        ProjectedNode node,
        RectD? overrideBounds)
    {
        var bounds = overrideBounds ?? (node.PlacementHint is null
            ? new RectD(0d, 0d, 80d, 30d)
            : new RectD(
                node.PlacementHint.Position.X,
                node.PlacementHint.Position.Y,
                node.PlacementHint.Size.Width,
                node.PlacementHint.Size.Height));
        return new LayoutNodeGeometry(
            node.Id,
            bounds,
            Matrix2D.CreateTranslation(bounds.X, bounds.Y));
    }

    private static LayoutGroupGeometry GroupGeometry(ProjectedGroup group) =>
        new(group.Id, new RectD(0d, 0d, 400d, 200d));

    private static void AssertCancelled(LayoutExecutionResult result)
    {
        Assert.Equal(LayoutExecutionStatus.Cancelled, result.Status);
        Assert.Null(result.Result);
        Assert.Equal(LayoutDiagnosticCodes.Cancelled,
            Assert.Single(result.Diagnostics).Code);
    }

    public enum AlgorithmFault
    {
        Throw,
        NullResult,
        UnrelatedCancellation,
    }

    private sealed class DelegateAlgorithm(
        Func<ProjectedGraph, LayoutContext, CancellationToken, LayoutAlgorithmResult> compute)
        : ILayoutAlgorithm
    {
        public LayoutAlgorithmResult Compute(
            ProjectedGraph graph,
            LayoutContext context,
            CancellationToken cancellationToken) =>
            compute(graph, context, cancellationToken);
    }
}
