using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Routing;

public sealed class RoutingEngineTests
{
    private static readonly DocumentId DocumentId = new("test:routing-document");
    private static readonly DocumentRevision Revision = new(23);
    private static readonly AlgorithmId LayoutAlgorithmId = new("test:layout:neutral");
    private static readonly AlgorithmId AlgorithmAId = new("test:routing:a");
    private static readonly AlgorithmId AlgorithmZId = new("test:routing:z");
    private static readonly ProjectionRuleId NodeRuleId = new("test:projection:node");
    private static readonly ProjectionRuleId EdgeRuleId = new("test:projection:edge");
    private static readonly ProjectionRuleId PortRuleId = new("test:projection:port");
    private static readonly SemanticTypeId NodeTypeId = new("test:node");
    private static readonly SemanticTypeId EdgeTypeId = new("test:edge");

    [Fact]
    public void RoutePassesExactImmutableInputsAndBuildsCompleteProvenanceBearingResult()
    {
        var graph = Graph();
        var layout = Layout(graph);
        var context = new RoutingContext(
            [new("test:clearance", PropertyValue.FromNumber(12d))]);
        ProjectedGraph? observedGraph = null;
        LayoutResult? observedLayout = null;
        RoutingContext? observedContext = null;
        CancellationToken observedToken = default;
        using var cancellation = new CancellationTokenSource();
        var algorithm = new DelegateAlgorithm((inputGraph, inputLayout, options, token) =>
        {
            observedGraph = inputGraph;
            observedLayout = inputLayout;
            observedContext = options;
            observedToken = token;
            return Complete(inputGraph, inputLayout, reverse: true);
        });

        var execution = Engine((AlgorithmAId, algorithm)).Route(
            graph,
            layout,
            AlgorithmAId,
            context,
            cancellation.Token);

        Assert.True(execution.IsSuccessful);
        Assert.Same(graph, observedGraph);
        Assert.Same(layout, observedLayout);
        Assert.Same(context, observedContext);
        Assert.Equal(cancellation.Token, observedToken);
        var result = Assert.IsType<RoutingResult>(execution.Result);
        Assert.Equal(DocumentId, result.DocumentId);
        Assert.Equal(Revision, result.SourceRevision);
        Assert.Equal(LayoutAlgorithmId, result.LayoutAlgorithmId);
        Assert.Equal(AlgorithmAId, result.RoutingAlgorithmId);
        Assert.Equal(
            graph.Edges.Select(static edge => edge.Id),
            result.Routes.Select(static route => route.ProjectedEdgeId));
        Assert.Equal(graph.EdgeCount, result.RouteCount);
    }

    [Fact]
    public void ExplicitSelectionIsDeterministicAndRegistriesAreInstanceLocal()
    {
        var graph = Graph();
        var layout = Layout(graph);
        var calls = new List<string>();
        var algorithmA = new DelegateAlgorithm((input, geometry, _, _) =>
        {
            calls.Add("a");
            return Complete(input, geometry, metadataValue: "a");
        });
        var algorithmZ = new DelegateAlgorithm((input, geometry, _, _) =>
        {
            calls.Add("z");
            return Complete(input, geometry, metadataValue: "z");
        });
        var first = Engine((AlgorithmZId, algorithmZ), (AlgorithmAId, algorithmA));
        var second = Engine((AlgorithmAId, algorithmA), (AlgorithmZId, algorithmZ));

        var firstResult = first.Route(graph, layout, AlgorithmAId);
        var secondResult = second.Route(graph, layout, AlgorithmAId);
        var isolated = new RoutingEngine().Route(graph, layout, AlgorithmAId);

        Assert.Equal(["a", "a"], calls);
        Assert.Equal(firstResult, secondResult);
        Assert.Equal("a", Assert.IsType<RoutingResult>(firstResult.Result)
            .Metadata["test:algorithm"].TextValue);
        AssertFailure(isolated, RoutingDiagnosticCodes.MissingAlgorithm);
    }

    [Fact]
    public void RegistryRejectsDuplicatesAndDefensivelyCopiesRegistrations()
    {
        var graph = Graph();
        var layout = Layout(graph);
        var algorithm = new DelegateAlgorithm(CompleteWithContext);
        var registration = new RoutingAlgorithmRegistration(AlgorithmAId, algorithm);
        var registrations = new List<RoutingAlgorithmRegistration> { registration };
        var engine = new RoutingEngine(registrations);
        registrations.Clear();

        Assert.True(engine.Route(graph, layout, AlgorithmAId).IsSuccessful);
        var exception = Assert.Throws<ArgumentException>(() => new RoutingEngine(
        [
            registration,
            new RoutingAlgorithmRegistration(new AlgorithmId(AlgorithmAId.Value), algorithm),
        ]));
        Assert.Contains(RoutingDiagnosticCodes.DuplicateAlgorithmRegistration, exception.Message);
        Assert.Contains(AlgorithmAId.Value, exception.Message);
        Assert.Throws<ArgumentException>(() => new RoutingEngine([null!]));
    }

    [Fact]
    public void StructurallyEquivalentAllocatedLayoutIsAccepted()
    {
        var graph = Graph();
        var firstLayout = Layout(graph);
        var equivalentLayout = Layout(Graph());
        var engine = Engine((AlgorithmAId, new DelegateAlgorithm(CompleteWithContext)));

        var first = engine.Route(graph, firstLayout, AlgorithmAId);
        var second = engine.Route(graph, equivalentLayout, AlgorithmAId);

        Assert.True(first.IsSuccessful);
        Assert.True(second.IsSuccessful);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(CompatibilityFault.ForeignDocument)]
    [InlineData(CompatibilityFault.StaleRevision)]
    [InlineData(CompatibilityFault.MissingNodeGeometry)]
    [InlineData(CompatibilityFault.UnexpectedNodeGeometry)]
    [InlineData(CompatibilityFault.WrongCategoryGeometry)]
    [InlineData(CompatibilityFault.InvalidGeometry)]
    public void IncompatibleLayoutFailsBeforeAlgorithmExecution(CompatibilityFault fault)
    {
        var graph = Graph(includeGroup: true);
        var layout = IncompatibleLayout(graph, fault);
        var calls = 0;
        var engine = Engine((AlgorithmAId, new DelegateAlgorithm((input, geometry, options, token) =>
        {
            calls++;
            return CompleteWithContext(input, geometry, options, token);
        })));

        var result = engine.Route(graph, layout, AlgorithmAId);

        Assert.Equal(0, calls);
        AssertFailure(result, RoutingDiagnosticCodes.IncompatibleLayoutResult);
    }

    [Fact]
    public void MalformedContextFailsBeforeAlgorithmExecution()
    {
        var graph = Graph();
        var layout = Layout(graph);
        var context = new RoutingContext();
        typeof(RoutingContext).GetField(
            "<Options>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, null);
        var calls = 0;
        var engine = Engine((AlgorithmAId, new DelegateAlgorithm((input, geometry, _, _) =>
        {
            calls++;
            return Complete(input, geometry);
        })));

        var result = engine.Route(graph, layout, AlgorithmAId, context);

        Assert.Equal(0, calls);
        AssertFailure(result, RoutingDiagnosticCodes.InvalidInput);
    }

    [Fact]
    public void EqualInputsAndEquivalentContributionOrdersProduceEqualCanonicalResults()
    {
        var firstGraph = Graph();
        var secondGraph = Graph();
        var firstLayout = Layout(firstGraph);
        var secondLayout = Layout(secondGraph);
        var firstEngine = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, _) =>
                Complete(input, geometry, reverse: false))));
        var secondEngine = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, _) =>
                Complete(input, geometry, reverse: true))));

        var first = firstEngine.Route(firstGraph, firstLayout, AlgorithmAId, RoutingContext.Empty);
        var second = secondEngine.Route(secondGraph, secondLayout, AlgorithmAId, new RoutingContext());
        var third = firstEngine.Route(firstGraph, firstLayout, AlgorithmAId);

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void PersistentRoutesAndRoutingHintsAreVisibleUnchangedButNotEngineEnforced()
    {
        var graph = Graph();
        var layout = Layout(graph);
        var hintedEdge = graph.Edges.Single(edge => edge.PersistentRoute.Length > 0);
        var graphBefore = Graph();
        ImmutableArray<PointD> observedPersistentRoute = default;
        PropertyMap? observedHints = null;
        var engine = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, _) =>
            {
                var observed = input.Edges.Single(edge => edge.Id == hintedEdge.Id);
                observedPersistentRoute = observed.PersistentRoute;
                observedHints = observed.RoutingHints;
                return Complete(
                    input,
                    geometry,
                    overrideRoute: edge => edge.Id == hintedEdge.Id
                        ? new RoutedConnectorGeometry(
                            edge.Id,
                            geometry.Nodes.Single(node =>
                                node.ProjectedObjectId == edge.SourceNodeId).Bounds.TopLeft,
                            geometry.Nodes.Single(node =>
                                node.ProjectedObjectId == edge.TargetNodeId).Bounds.TopLeft,
                            sourcePortId: edge.SourcePortId,
                            targetPortId: edge.TargetPortId)
                        : null);
            })));

        var result = engine.Route(graph, layout, AlgorithmAId);

        Assert.True(result.IsSuccessful);
        Assert.Equal(hintedEdge.PersistentRoute, observedPersistentRoute);
        Assert.Same(hintedEdge.RoutingHints, observedHints);
        Assert.Equal("orthogonal", observedHints!["test:style"].TextValue);
        Assert.Equal(graphBefore, graph);
        var expectedSourceAnchor = layout.Nodes.Single(node =>
            node.ProjectedObjectId == hintedEdge.SourceNodeId).Bounds.TopLeft;
        Assert.Equal(expectedSourceAnchor, Assert.IsType<RoutingResult>(result.Result)
            .Routes.Single(route => route.ProjectedEdgeId == hintedEdge.Id).SourceAnchor);
    }

    [Fact]
    public void MissingExtraWrongCategoryAndDuplicateRoutesAreRejected()
    {
        var graph = Graph();
        var layout = Layout(graph);
        var missing = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, _) =>
                RoutingAlgorithmResult.Success(new RoutingComputation(
                    input.Edges.Skip(1).Select(edge => Route(edge, geometry)))))))
            .Route(graph, layout, AlgorithmAId);
        var extra = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, _) =>
                RoutingAlgorithmResult.Success(new RoutingComputation(
                    input.Edges.Select(edge => Route(edge, geometry)).Append(
                        new RoutedConnectorGeometry(
                            new ProjectedObjectId("test:foreign-edge"),
                            new PointD(0d, 0d),
                            new PointD(1d, 1d))))))))
            .Route(graph, layout, AlgorithmAId);
        var wrongCategory = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, _) =>
                RoutingAlgorithmResult.Success(new RoutingComputation(
                    input.Edges.Skip(1).Select(edge => Route(edge, geometry)).Append(
                        new RoutedConnectorGeometry(
                            input.Nodes[0].Id,
                            new PointD(0d, 0d),
                            new PointD(1d, 1d))))))))
            .Route(graph, layout, AlgorithmAId);
        var duplicateComputation = new RoutingComputation(
            graph.Edges.Select(edge => Route(edge, layout)));
        var duplicate = duplicateComputation.Routes[0];
        typeof(RoutingComputation).GetField(
            "<Routes>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(duplicateComputation, ImmutableArray.Create(duplicate, duplicate));
        var duplicateResult = Engine((AlgorithmAId,
            new DelegateAlgorithm((_, _, _, _) =>
                RoutingAlgorithmResult.Success(duplicateComputation))))
            .Route(graph, layout, AlgorithmAId);

        AssertFailure(missing, RoutingDiagnosticCodes.MissingRoute);
        AssertFailure(extra, RoutingDiagnosticCodes.UnexpectedRoute);
        AssertFailure(wrongCategory, RoutingDiagnosticCodes.InvalidProjectedIdentity);
        AssertFailure(duplicateResult, RoutingDiagnosticCodes.DuplicateRoute);
    }

    [Fact]
    public void ExplicitNoRouteOutcomeCompletesCoverageAndPreservesWarning()
    {
        var graph = Graph();
        var layout = Layout(graph);
        var noRouteEdge = graph.Edges[^1];
        var warning = new Diagnostic(
            "TEST_NO_ROUTE",
            DiagnosticSeverity.Warning,
            "No route exists for this connector.",
            noRouteEdge.Id.Value);
        var result = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, _) =>
                RoutingAlgorithmResult.Success(
                    new RoutingComputation(
                        routes: input.Edges
                            .Where(edge => edge.Id != noRouteEdge.Id)
                            .Select(edge => Route(edge, geometry)),
                        noRouteEdgeIds: [noRouteEdge.Id]),
                    [warning]))))
            .Route(graph, layout, AlgorithmAId);

        Assert.True(result.IsSuccessful);
        var routing = Assert.IsType<RoutingResult>(result.Result);
        Assert.Single(routing.Routes);
        Assert.Equal(noRouteEdge.Id, Assert.Single(routing.NoRouteEdgeIds));
        Assert.Equal(warning, Assert.Single(routing.Diagnostics));
    }

    [Fact]
    public void ConflictingForeignAndWrongCategoryNoRouteOutcomesAreRejected()
    {
        var graph = Graph();
        var layout = Layout(graph);
        var routes = graph.Edges.Select(edge => Route(edge, layout)).ToArray();
        var conflict = Engine((AlgorithmAId,
            new DelegateAlgorithm((_, _, _, _) => RoutingAlgorithmResult.Success(
                new RoutingComputation(
                    routes,
                    noRouteEdgeIds: [graph.Edges[0].Id])))))
            .Route(graph, layout, AlgorithmAId);
        var foreign = Engine((AlgorithmAId,
            new DelegateAlgorithm((_, _, _, _) => RoutingAlgorithmResult.Success(
                new RoutingComputation(
                    routes,
                    noRouteEdgeIds: [new ProjectedObjectId("test:foreign-edge")])))))
            .Route(graph, layout, AlgorithmAId);
        var wrongCategory = Engine((AlgorithmAId,
            new DelegateAlgorithm((_, _, _, _) => RoutingAlgorithmResult.Success(
                new RoutingComputation(
                    routes,
                    noRouteEdgeIds: [graph.Nodes[0].Id])))))
            .Route(graph, layout, AlgorithmAId);

        AssertFailure(conflict, RoutingDiagnosticCodes.ConflictingRouteOutcome);
        AssertFailure(foreign, RoutingDiagnosticCodes.UnexpectedRoute);
        AssertFailure(wrongCategory, RoutingDiagnosticCodes.InvalidProjectedIdentity);
    }

    [Fact]
    public void PortReferencesMustExactlyMatchProjectedEdgeEndpoints()
    {
        var graph = Graph();
        var layout = Layout(graph);
        var portedEdge = graph.Edges.Single(edge => edge.SourcePortId is not null);
        var otherPort = graph.Ports.Single(port =>
            port.OwnerNodeId != portedEdge.SourceNodeId &&
            port.OwnerNodeId != portedEdge.TargetNodeId);
        var missingPort = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, _) => Complete(
                input,
                geometry,
                overrideRoute: edge => edge.Id == portedEdge.Id
                    ? new RoutedConnectorGeometry(
                        edge.Id,
                        new PointD(0d, 0d),
                        new PointD(1d, 1d))
                    : null))))
            .Route(graph, layout, AlgorithmAId);
        var wrongPort = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, _) => Complete(
                input,
                geometry,
                overrideRoute: edge => edge.Id == portedEdge.Id
                    ? new RoutedConnectorGeometry(
                        edge.Id,
                        new PointD(0d, 0d),
                        new PointD(1d, 1d),
                        sourcePortId: otherPort.Id,
                        targetPortId: edge.TargetPortId)
                    : null))))
            .Route(graph, layout, AlgorithmAId);

        AssertFailure(missingPort, RoutingDiagnosticCodes.InvalidPortReference);
        AssertFailure(wrongPort, RoutingDiagnosticCodes.InvalidPortReference);
    }

    [Fact]
    public void AnchorsMustAttachToTheCorrespondingLayoutNodeBounds()
    {
        var graph = Graph();
        var layout = Layout(graph);
        var edge = graph.Edges[0];
        var result = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, _) => Complete(
                input,
                geometry,
                overrideRoute: candidate => candidate.Id == edge.Id
                    ? new RoutedConnectorGeometry(
                        candidate.Id,
                        new PointD(-1000d, -1000d),
                        new PointD(1000d, 1000d),
                        sourcePortId: candidate.SourcePortId,
                        targetPortId: candidate.TargetPortId)
                    : null))))
            .Route(graph, layout, AlgorithmAId);

        AssertFailure(result, RoutingDiagnosticCodes.InvalidAttachmentPoint);
    }

    [Theory]
    [InlineData(PathFault.NonFiniteAnchor)]
    [InlineData(PathFault.NonFiniteBend)]
    [InlineData(PathFault.IncompletePath)]
    [InlineData(PathFault.MismatchedPath)]
    public void MalformedPathIsIndependentlyRejected(PathFault fault)
    {
        var graph = Graph();
        var layout = Layout(graph);
        var computation = Assert.IsType<RoutingComputation>(Complete(graph, layout).Computation);
        var route = computation.Routes[0];
        CorruptPath(route, fault);

        var result = Engine((AlgorithmAId,
            new DelegateAlgorithm((_, _, _, _) => RoutingAlgorithmResult.Success(computation))))
            .Route(graph, layout, AlgorithmAId);

        AssertFailure(
            result,
            fault is PathFault.NonFiniteAnchor or PathFault.NonFiniteBend
                ? RoutingDiagnosticCodes.NonFiniteGeometry
                : RoutingDiagnosticCodes.InvalidPath);
    }

    [Theory]
    [InlineData(-1d, 40d)]
    [InlineData(120d, -1d)]
    public void NegativeDerivedRoutePointViolatesTheDocumentBoundary(
        double bendX,
        double bendY)
    {
        var graph = Graph();
        var layout = Layout(graph);
        var targetEdge = graph.Edges[0];
        var valid = Route(targetEdge, layout);
        var result = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, _) => Complete(
                input,
                geometry,
                overrideRoute: edge => edge.Id == targetEdge.Id
                    ? new RoutedConnectorGeometry(
                        edge.Id,
                        valid.SourceAnchor,
                        valid.DestinationAnchor,
                        [new PointD(bendX, bendY)],
                        edge.SourcePortId,
                        edge.TargetPortId)
                    : null))))
            .Route(graph, layout, AlgorithmAId);

        AssertFailure(result, RoutingDiagnosticCodes.RoutingConstraintViolation);
    }

    [Fact]
    public void WarningsArePreservedButFailuresAndFaultsExposeNoResult()
    {
        var graph = Graph();
        var layout = Layout(graph);
        var warning = new Diagnostic("TEST_ROUTING_WARNING", DiagnosticSeverity.Warning, "warning");
        var successful = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, _) =>
                Complete(input, geometry, diagnostics: [warning]))))
            .Route(graph, layout, AlgorithmAId);
        var reported = Engine((AlgorithmAId,
            new DelegateAlgorithm((_, _, _, _) => RoutingAlgorithmResult.Failure(
                [new Diagnostic("TEST_FAILURE", DiagnosticSeverity.Error, "failed")]))))
            .Route(graph, layout, AlgorithmAId);
        var thrown = Engine((AlgorithmAId,
            new DelegateAlgorithm((_, _, _, _) =>
                throw new InvalidOperationException("private plugin detail"))))
            .Route(graph, layout, AlgorithmAId);
        var nullResult = Engine((AlgorithmAId,
            new DelegateAlgorithm((_, _, _, _) => null!)))
            .Route(graph, layout, AlgorithmAId);

        Assert.True(successful.IsSuccessful);
        Assert.Equal(warning.Code, Assert.Single(successful.Diagnostics).Code);
        AssertFailure(reported, "TEST_FAILURE");
        AssertFailure(thrown, RoutingDiagnosticCodes.AlgorithmFailure);
        Assert.DoesNotContain(thrown.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("private plugin detail", StringComparison.Ordinal));
        AssertFailure(nullResult, RoutingDiagnosticCodes.InvalidAlgorithmResult);
    }

    [Fact]
    public void CancellationBeforeDuringAndAfterAlgorithmExecutionPublishesNoResult()
    {
        var graph = Graph();
        var layout = Layout(graph);
        var calls = 0;
        using var before = new CancellationTokenSource();
        before.Cancel();
        var beforeResult = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, _) =>
            {
                calls++;
                return Complete(input, geometry);
            }))).Route(graph, layout, AlgorithmAId, cancellationToken: before.Token);

        using var during = new CancellationTokenSource();
        var duringResult = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, token) =>
            {
                calls++;
                during.Cancel();
                token.ThrowIfCancellationRequested();
                return Complete(input, geometry);
            }))).Route(graph, layout, AlgorithmAId, cancellationToken: during.Token);

        using var after = new CancellationTokenSource();
        var afterResult = Engine((AlgorithmAId,
            new DelegateAlgorithm((input, geometry, _, _) =>
            {
                calls++;
                var result = Complete(input, geometry);
                after.Cancel();
                return result;
            }))).Route(graph, layout, AlgorithmAId, cancellationToken: after.Token);

        Assert.Equal(2, calls);
        AssertCancelled(beforeResult);
        AssertCancelled(duringResult);
        AssertCancelled(afterResult);
    }

    [Fact]
    public void EngineOwnedValidationTraversalObservesCancellation()
    {
        var graph = Graph();
        var computation = Assert.IsType<RoutingComputation>(Complete(graph, Layout(graph)).Computation);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var validate = typeof(RoutingEngine).GetMethod(
            "ValidateComputation",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(validate);

        var exception = Assert.Throws<TargetInvocationException>(() => validate.Invoke(
            null,
            [graph, Layout(graph), computation, new List<Diagnostic>(), cancellation.Token]));

        Assert.IsType<OperationCanceledException>(exception.InnerException);
    }

    [Fact]
    public void EmptyEdgeGraphProducesCompleteEmptyRouting()
    {
        var graph = Graph(includeEdges: false);
        var layout = Layout(graph);
        var result = Engine((AlgorithmAId,
            new DelegateAlgorithm((_, _, _, _) =>
                RoutingAlgorithmResult.Success(RoutingComputation.Empty))))
            .Route(graph, layout, AlgorithmAId);

        Assert.True(result.IsSuccessful);
        Assert.Empty(Assert.IsType<RoutingResult>(result.Result).Routes);
    }

    private static RoutingEngine Engine(
        params (AlgorithmId Id, IRoutingAlgorithm Algorithm)[] algorithms) =>
        new(algorithms.Select(static item =>
            new RoutingAlgorithmRegistration(item.Id, item.Algorithm)));

    private static ProjectedGraph Graph(bool includeGroup = false, bool includeEdges = true)
    {
        var nodeA = Node("a");
        var nodeB = Node("b");
        var nodeC = Node("c");
        var sourcePort = Port("source-a", nodeA.Id);
        var targetPort = Port("target-b", nodeB.Id);
        var otherPort = Port("other-c", nodeC.Id);
        var edges = includeEdges
            ?
            [
                Edge("ab", nodeA.Id, nodeB.Id, sourcePort.Id, targetPort.Id, true),
                Edge("bc", nodeB.Id, nodeC.Id),
            ]
            : Array.Empty<ProjectedEdge>();
        var groups = includeGroup
            ? [new ProjectedGroup(Source("group", ProjectionSourceKind.SemanticElement, NodeRuleId),
                [nodeA.Id, nodeB.Id, nodeC.Id])]
            : Array.Empty<ProjectedGroup>();

        return new ProjectedGraph(
            DocumentId,
            Revision,
            [nodeC, nodeA, nodeB],
            edges,
            groups,
            [otherPort, sourcePort, targetPort]);
    }

    private static ProjectedNode Node(string key) =>
        new(Source(key, ProjectionSourceKind.SemanticElement, NodeRuleId));

    private static ProjectedPort Port(string key, ProjectedObjectId ownerNodeId) =>
        new(Source(key, ProjectionSourceKind.SemanticElement, PortRuleId), ownerNodeId);

    private static ProjectedEdge Edge(
        string key,
        ProjectedObjectId sourceNodeId,
        ProjectedObjectId targetNodeId,
        ProjectedObjectId? sourcePortId = null,
        ProjectedObjectId? targetPortId = null,
        bool withHints = false) =>
        new(
            Source(key, ProjectionSourceKind.SemanticRelationship, EdgeRuleId),
            sourceNodeId,
            targetNodeId,
            sourcePortId,
            targetPortId,
            withHints
                ? [new PointD(10d, 20d), new PointD(50d, 20d), new PointD(50d, 80d)]
                : null,
            routingHints: withHints
                ? [new("test:style", PropertyValue.FromText("orthogonal"))]
                : null);

    private static ProjectionSourceTrace Source(
        string key,
        ProjectionSourceKind sourceKind,
        ProjectionRuleId ruleId) =>
        new(
            DocumentId,
            ruleId,
            sourceKind,
            new SemanticElementId($"test:semantic:{key}"),
            sourceKind == ProjectionSourceKind.SemanticRelationship ? EdgeTypeId : NodeTypeId,
            key);

    private static LayoutResult Layout(ProjectedGraph graph) =>
        new(
            graph.DocumentId,
            graph.SourceRevision,
            LayoutAlgorithmId,
            new LayoutComputation(
                graph.Nodes.Select((node, index) =>
                {
                    var bounds = new RectD(index * 160d, 40d, 100d, 50d);
                    return new LayoutNodeGeometry(
                        node.Id,
                        bounds,
                        Matrix2D.CreateTranslation(bounds.X, bounds.Y));
                }),
                graph.Groups.Select(group =>
                    new LayoutGroupGeometry(group.Id, new RectD(0d, 0d, 500d, 180d)))));

    private static LayoutResult IncompatibleLayout(ProjectedGraph graph, CompatibilityFault fault)
    {
        if (fault == CompatibilityFault.ForeignDocument)
        {
            return new LayoutResult(
                new DocumentId("test:foreign-document"),
                graph.SourceRevision,
                LayoutAlgorithmId,
                Layout(graph).Computation);
        }

        if (fault == CompatibilityFault.StaleRevision)
        {
            return new LayoutResult(
                graph.DocumentId,
                graph.SourceRevision.Increment(),
                LayoutAlgorithmId,
                Layout(graph).Computation);
        }

        var valid = Layout(graph);
        IEnumerable<LayoutNodeGeometry> nodes = valid.Nodes;
        IEnumerable<LayoutGroupGeometry> groups = valid.Groups;
        if (fault == CompatibilityFault.MissingNodeGeometry)
        {
            nodes = nodes.Skip(1);
        }
        else if (fault == CompatibilityFault.UnexpectedNodeGeometry)
        {
            nodes = nodes.Append(new LayoutNodeGeometry(
                new ProjectedObjectId("test:foreign-node"),
                new RectD(0d, 0d, 10d, 10d),
                Matrix2D.Identity));
        }
        else if (fault == CompatibilityFault.WrongCategoryGeometry)
        {
            var groupId = graph.Groups[0].Id;
            nodes = nodes.Skip(1).Append(new LayoutNodeGeometry(
                groupId,
                new RectD(0d, 0d, 10d, 10d),
                Matrix2D.Identity));
            groups = [];
        }

        var result = new LayoutResult(
            graph.DocumentId,
            graph.SourceRevision,
            LayoutAlgorithmId,
            new LayoutComputation(nodes, groups));
        if (fault == CompatibilityFault.InvalidGeometry)
        {
            var geometry = result.Nodes[0];
            object boxedBounds = geometry.Bounds;
            typeof(RectD).GetField(
                "<Width>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(boxedBounds, double.NaN);
            typeof(LayoutNodeGeometry).GetField(
                "<Bounds>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(geometry, (RectD)boxedBounds);
        }

        return result;
    }

    private static RoutingAlgorithmResult CompleteWithContext(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default) =>
        Complete(graph, layout, false, null, null, null);

    private static RoutingAlgorithmResult Complete(
        ProjectedGraph graph,
        LayoutResult layout,
        bool reverse = false,
        string? metadataValue = null,
        IEnumerable<Diagnostic>? diagnostics = null,
        Func<ProjectedEdge, RoutedConnectorGeometry?>? overrideRoute = null)
    {
        var routes = graph.Edges
            .Select(edge => overrideRoute?.Invoke(edge) ?? Route(edge, layout))
            .ToArray();
        if (reverse)
        {
            Array.Reverse(routes);
        }

        return RoutingAlgorithmResult.Success(
            new RoutingComputation(
                routes,
                metadataValue is null
                    ? null
                    : [new("test:algorithm", PropertyValue.FromText(metadataValue))]),
            diagnostics);
    }

    private static RoutedConnectorGeometry Route(ProjectedEdge edge, LayoutResult layout)
    {
        var source = layout.Nodes.Single(node => node.ProjectedObjectId == edge.SourceNodeId).Bounds;
        var target = layout.Nodes.Single(node => node.ProjectedObjectId == edge.TargetNodeId).Bounds;
        var sourceAnchor = new PointD(source.Right, source.Y + (source.Height / 2d));
        var destinationAnchor = new PointD(target.Left, target.Y + (target.Height / 2d));
        return new RoutedConnectorGeometry(
            edge.Id,
            sourceAnchor,
            destinationAnchor,
            [new PointD((sourceAnchor.X + destinationAnchor.X) / 2d, sourceAnchor.Y)],
            edge.SourcePortId,
            edge.TargetPortId);
    }

    private static void CorruptPath(RoutedConnectorGeometry route, PathFault fault)
    {
        if (fault == PathFault.NonFiniteAnchor)
        {
            object boxed = route.SourceAnchor;
            typeof(PointD).GetField("<X>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(boxed, double.NaN);
            typeof(RoutedConnectorGeometry).GetField(
                "<SourceAnchor>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(route, (PointD)boxed);
            return;
        }

        if (fault == PathFault.NonFiniteBend)
        {
            object boxed = route.BendPoints[0];
            typeof(PointD).GetField("<Y>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(boxed, double.PositiveInfinity);
            typeof(RoutedConnectorGeometry).GetField(
                "<BendPoints>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(route, ImmutableArray.Create((PointD)boxed));
            return;
        }

        var path = fault == PathFault.IncompletePath
            ? ImmutableArray.Create(route.SourceAnchor)
            : ImmutableArray.Create(route.DestinationAnchor, route.SourceAnchor);
        typeof(RoutedConnectorGeometry).GetField(
            "<Path>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(route, path);
    }

    private static void AssertFailure(RoutingExecutionResult result, string diagnosticCode)
    {
        Assert.Equal(RoutingExecutionStatus.Failed, result.Status);
        Assert.Null(result.Result);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == diagnosticCode);
    }

    private static void AssertCancelled(RoutingExecutionResult result)
    {
        Assert.Equal(RoutingExecutionStatus.Cancelled, result.Status);
        Assert.Null(result.Result);
        Assert.Equal(RoutingDiagnosticCodes.Cancelled, Assert.Single(result.Diagnostics).Code);
    }

    public enum CompatibilityFault
    {
        ForeignDocument,
        StaleRevision,
        MissingNodeGeometry,
        UnexpectedNodeGeometry,
        WrongCategoryGeometry,
        InvalidGeometry,
    }

    public enum PathFault
    {
        NonFiniteAnchor,
        NonFiniteBend,
        IncompletePath,
        MismatchedPath,
    }

    private sealed class DelegateAlgorithm(
        Func<ProjectedGraph, LayoutResult, RoutingContext, CancellationToken, RoutingAlgorithmResult> route)
        : IRoutingAlgorithm
    {
        public RoutingAlgorithmResult Route(
            ProjectedGraph graph,
            LayoutResult layout,
            RoutingContext context,
            CancellationToken cancellationToken) =>
            route(graph, layout, context, cancellationToken);
    }
}
