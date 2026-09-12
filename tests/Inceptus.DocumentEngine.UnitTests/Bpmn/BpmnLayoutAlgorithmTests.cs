using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Layout;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnLayoutAlgorithmTests
{
    private const string ProjectedSemanticTypeProperty = "BPMN.ProjectedSemanticType";

    private static readonly DocumentId DocumentId = new("bpmn:m2-layout-unit-document");
    private static readonly DocumentRevision Revision = new(31);
    private static readonly ProjectionRuleId NodeRuleId = new("bpmn:m2-test/node");
    private static readonly ProjectionRuleId EdgeRuleId = new("bpmn:m2-test/edge");

    [Fact]
    public void LinearFlowIsLeftToRightIdentityPreservingAndExactlyDeterministic()
    {
        var start = Node("linear-start", BpmnSemanticTypes.StartEvent);
        var task = Node("linear-task", BpmnSemanticTypes.Task);
        var end = Node("linear-end", BpmnSemanticTypes.EndEvent);
        var graph = Graph(
            [task, end, start],
            [Flow("linear-2", task, end), Flow("linear-1", start, task)]);
        var algorithm = new BpmnLayoutAlgorithm();

        var first = algorithm.Compute(graph, LayoutContext.Empty, CancellationToken.None);
        var second = algorithm.Compute(graph, LayoutContext.Empty, CancellationToken.None);

        Assert.Equal(first, second);
        var computation = Successful(first);
        Assert.Equal(
            graph.Nodes.Select(static node => node.Id),
            computation.Nodes.Select(static geometry => geometry.ProjectedObjectId));

        var startGeometry = Geometry(computation, start);
        var taskGeometry = Geometry(computation, task);
        var endGeometry = Geometry(computation, end);
        Assert.True(startGeometry.Bounds.Right < taskGeometry.Bounds.Left);
        Assert.True(taskGeometry.Bounds.Right < endGeometry.Bounds.Left);
        Assert.Equal(CenterY(startGeometry.Bounds), CenterY(taskGeometry.Bounds));
        Assert.Equal(CenterY(taskGeometry.Bounds), CenterY(endGeometry.Bounds));
        Assert.Equal(start.PlacementHint!.Size, startGeometry.Size);
        Assert.Equal(task.PlacementHint!.Size, taskGeometry.Size);
        Assert.Equal(end.PlacementHint!.Size, endGeometry.Size);
    }

    [Fact]
    public void LongerLinearFlowUsesTopologyInsteadOfInputOrIdentityOrder()
    {
        var start = Node("z-start", BpmnSemanticTypes.StartEvent);
        var taskA = Node("y-task-a", BpmnSemanticTypes.Task);
        var taskB = Node(
            "a-task-b",
            BpmnSemanticTypes.Task,
            Automatic(new SizeD(150d, 60d)));
        var end = Node("b-end", BpmnSemanticTypes.EndEvent);
        var graph = Graph(
            [taskB, end, taskA, start],
            [
                Flow("z-flow-3", taskB, end),
                Flow("a-flow-1", start, taskA),
                Flow("m-flow-2", taskA, taskB),
            ]);

        var computation = Run(graph);
        LayoutNodeGeometry[] ordered =
        [
            Geometry(computation, start),
            Geometry(computation, taskA),
            Geometry(computation, taskB),
            Geometry(computation, end),
        ];

        for (var index = 1; index < ordered.Length; index++)
        {
            Assert.True(ordered[index - 1].Bounds.Right < ordered[index].Bounds.Left);
        }

        Assert.Equal(new SizeD(150d, 60d), ordered[2].Size);
    }

    [Fact]
    public void ExclusiveGatewayWithoutPlacementHintUsesCentralizedLogicalFallbackSize()
    {
        var gateway = Node(
            "gateway-fallback",
            BpmnSemanticTypes.ExclusiveGateway,
            includePlacementHint: false);

        var computation = Run(Graph([gateway]));

        Assert.Equal(new SizeD(48d, 48d), Geometry(computation, gateway).Size);
    }

    [Fact]
    public void BranchTargetsShareAStableDepthColumnAndUseSeparateSiblingRows()
    {
        var start = Node("branch-start", BpmnSemanticTypes.StartEvent);
        var gateway = Node("branch-gateway", BpmnSemanticTypes.ExclusiveGateway);
        var taskA = Node("branch-task-a", BpmnSemanticTypes.Task);
        var taskB = Node("branch-task-b", BpmnSemanticTypes.Task);
        ProjectedNode[] nodes = [taskB, gateway, start, taskA];
        ProjectedEdge[] edges =
        [
            Flow("branch-start-gateway", start, gateway),
            Flow("branch-gateway-b", gateway, taskB),
            Flow("branch-gateway-a", gateway, taskA),
        ];
        var graph = Graph(nodes, edges);
        var reversed = Graph(nodes.Reverse(), edges.Reverse());

        var first = Run(graph);
        var second = Run(reversed);

        Assert.Equal(graph, reversed);
        Assert.Equal(first, second);
        var startBounds = Geometry(first, start).Bounds;
        var gatewayBounds = Geometry(first, gateway).Bounds;
        var taskABounds = Geometry(first, taskA).Bounds;
        var taskBBounds = Geometry(first, taskB).Bounds;
        Assert.True(startBounds.Right < gatewayBounds.Left);
        Assert.True(gatewayBounds.Right < taskABounds.Left);
        Assert.True(gatewayBounds.Right < taskBBounds.Left);
        Assert.Equal(taskABounds.Left, taskBBounds.Left);
        Assert.NotEqual(CenterY(taskABounds), CenterY(taskBBounds));
        Assert.False(taskABounds.Intersects(taskBBounds));
        Assert.Equal(CenterY(gatewayBounds),
            (CenterY(taskABounds) + CenterY(taskBBounds)) / 2d);
    }

    [Fact]
    public void PinnedTaskKeepsItsExactPersistentBoundsWhileOtherNodesRemainAutomatic()
    {
        var start = Node("pinned-task-start", BpmnSemanticTypes.StartEvent);
        var pinnedBounds = new RectD(410d, 275d, 150d, 95d);
        var task = Node(
            "pinned-task",
            BpmnSemanticTypes.Task,
            Pinned(pinnedBounds));
        var end = Node("pinned-task-end", BpmnSemanticTypes.EndEvent);
        var graph = Graph(
            [end, task, start],
            [Flow("pinned-task-1", start, task), Flow("pinned-task-2", task, end)]);
        var before = Graph(
            [end, task, start],
            [Flow("pinned-task-1", start, task), Flow("pinned-task-2", task, end)]);

        var first = Run(graph);
        var second = Run(graph);

        Assert.Equal(first, second);
        Assert.Equal(pinnedBounds, Geometry(first, task).Bounds);
        Assert.NotEqual(HintBounds(start), Geometry(first, start).Bounds);
        Assert.NotEqual(HintBounds(end), Geometry(first, end).Bounds);
        Assert.Equal(before, graph);
    }

    [Fact]
    public void MixedPinnedComponentNeverOffsetsAutomaticNodesOutsideDocument()
    {
        var start = Node("boundary-pinned-start", BpmnSemanticTypes.StartEvent);
        var pinnedBounds = new RectD(20d, 30d, 120d, 80d);
        var task = Node(
            "boundary-pinned-task",
            BpmnSemanticTypes.Task,
            Pinned(pinnedBounds));
        var end = Node("boundary-pinned-end", BpmnSemanticTypes.EndEvent);
        var graph = Graph(
            [end, task, start],
            [
                Flow("boundary-pinned-1", start, task),
                Flow("boundary-pinned-2", task, end),
            ]);

        var computation = Run(graph);

        Assert.Equal(pinnedBounds, Geometry(computation, task).Bounds);
        Assert.All(computation.Nodes, geometry =>
        {
            Assert.True(geometry.Bounds.Left >= 0d);
            Assert.True(geometry.Bounds.Top >= 0d);
        });
    }

    [Fact]
    public void PinnedExclusiveGatewayKeepsExactBoundsWhileBranchRowsRemainDeterministic()
    {
        var task = Node("pinned-gateway-source", BpmnSemanticTypes.Task);
        var pinnedBounds = new RectD(410d, 275d, 48d, 48d);
        var gateway = Node(
            "pinned-gateway",
            BpmnSemanticTypes.ExclusiveGateway,
            Pinned(pinnedBounds));
        var taskA = Node("pinned-gateway-target-a", BpmnSemanticTypes.Task);
        var taskB = Node("pinned-gateway-target-b", BpmnSemanticTypes.Task);
        var graph = Graph(
            [taskB, gateway, taskA, task],
            [
                Flow("pinned-gateway-incoming", task, gateway),
                Flow("pinned-gateway-outgoing-a", gateway, taskA),
                Flow("pinned-gateway-outgoing-b", gateway, taskB),
            ]);

        var first = Run(graph);
        var second = Run(graph);

        Assert.Equal(first, second);
        Assert.Equal(pinnedBounds, Geometry(first, gateway).Bounds);
        var taskABounds = Geometry(first, taskA).Bounds;
        var taskBBounds = Geometry(first, taskB).Bounds;
        Assert.Equal(taskABounds.Left, taskBBounds.Left);
        Assert.False(taskABounds.Intersects(taskBBounds));
        AssertFinite(Geometry(first, task).Bounds);
        AssertFinite(taskABounds);
        AssertFinite(taskBBounds);
    }

    [Fact]
    public void PinnedStartAndEndBoundaryNodesKeepTheirExactPersistentBounds()
    {
        var pinnedStartBounds = new RectD(175d, 90d, 44d, 44d);
        var pinnedEndBounds = new RectD(925d, 310d, 52d, 52d);
        var start = Node(
            "pinned-boundary-start",
            BpmnSemanticTypes.StartEvent,
            Pinned(pinnedStartBounds));
        var task = Node("pinned-boundary-task", BpmnSemanticTypes.Task);
        var end = Node(
            "pinned-boundary-end",
            BpmnSemanticTypes.EndEvent,
            Pinned(pinnedEndBounds));
        var graph = Graph(
            [task, start, end],
            [Flow("pinned-boundary-1", start, task), Flow("pinned-boundary-2", task, end)]);

        var computation = Run(graph);

        Assert.Equal(pinnedStartBounds, Geometry(computation, start).Bounds);
        Assert.Equal(pinnedEndBounds, Geometry(computation, end).Bounds);
        AssertFinite(Geometry(computation, task).Bounds);
    }

    [Fact]
    public void DisconnectedSupportedNodeUsesASeparateDeterministicNonOverlappingRow()
    {
        var start = Node("disconnected-start", BpmnSemanticTypes.StartEvent);
        var task = Node("disconnected-main-task", BpmnSemanticTypes.Task);
        var end = Node("disconnected-end", BpmnSemanticTypes.EndEvent);
        var disconnected = Node("disconnected-secondary-task", BpmnSemanticTypes.Task);
        ProjectedNode[] nodes = [disconnected, end, task, start];
        ProjectedEdge[] edges =
        [
            Flow("disconnected-2", task, end),
            Flow("disconnected-1", start, task),
        ];
        var graph = Graph(nodes, edges);
        var graphWithReversedInputs = Graph(nodes.Reverse(), edges.Reverse());

        var first = Run(graph);
        var second = Run(graphWithReversedInputs);

        Assert.Equal(graph, graphWithReversedInputs);
        Assert.Equal(first, second);
        var disconnectedBounds = Geometry(first, disconnected).Bounds;
        var mainBounds = new[]
        {
            Geometry(first, start).Bounds,
            Geometry(first, task).Bounds,
            Geometry(first, end).Bounds,
        };
        Assert.All(mainBounds, bounds => Assert.False(disconnectedBounds.Intersects(bounds)));
        Assert.True(
            disconnectedBounds.Bottom <= mainBounds.Min(static bounds => bounds.Top) ||
            disconnectedBounds.Top >= mainBounds.Max(static bounds => bounds.Bottom));
    }

    [Fact]
    public void MultipleRootsHaveAStableNonOverlappingOrderBeforeTheirSharedTarget()
    {
        var firstRoot = Node("multiple-root-z", BpmnSemanticTypes.StartEvent);
        var secondRoot = Node("multiple-root-a", BpmnSemanticTypes.StartEvent);
        var task = Node("multiple-root-task", BpmnSemanticTypes.Task);
        var end = Node("multiple-root-end", BpmnSemanticTypes.EndEvent);
        var graph = Graph(
            [task, firstRoot, end, secondRoot],
            [
                Flow("multiple-root-3", task, end),
                Flow("multiple-root-2", secondRoot, task),
                Flow("multiple-root-1", firstRoot, task),
            ]);

        var first = Run(graph);
        var second = Run(graph);

        Assert.Equal(first, second);
        var firstRootBounds = Geometry(first, firstRoot).Bounds;
        var secondRootBounds = Geometry(first, secondRoot).Bounds;
        var taskBounds = Geometry(first, task).Bounds;
        Assert.False(firstRootBounds.Intersects(secondRootBounds));
        Assert.True(taskBounds.Left > firstRootBounds.Right);
        Assert.True(taskBounds.Left > secondRootBounds.Right);
    }

    [Fact]
    public void CycleCompletesWithDeterministicStableProjectedIdentityFallback()
    {
        var taskZ = Node("cycle-z", BpmnSemanticTypes.Task);
        var taskA = Node("cycle-a", BpmnSemanticTypes.Task);
        var graph = Graph(
            [taskZ, taskA],
            [Flow("cycle-z-to-a", taskZ, taskA), Flow("cycle-a-to-z", taskA, taskZ)]);
        var algorithm = new BpmnLayoutAlgorithm();

        var firstResult = algorithm.Compute(graph, LayoutContext.Empty, CancellationToken.None);
        var secondResult = algorithm.Compute(graph, LayoutContext.Empty, CancellationToken.None);

        Assert.Equal(firstResult, secondResult);
        var computation = Successful(firstResult);
        var stableOrder = graph.Nodes.OrderBy(static node => node.Id.Value, StringComparer.Ordinal).ToArray();
        var firstBounds = Geometry(computation, stableOrder[0]).Bounds;
        var secondBounds = Geometry(computation, stableOrder[1]).Bounds;
        Assert.True(firstBounds.Right < secondBounds.Left);
        Assert.False(firstBounds.Intersects(secondBounds));
    }

    [Fact]
    public void MissingOrUnsupportedNodeTypeMetadataFailsWithoutAComputation()
    {
        var missingMarker = Node(
            "missing-node-marker",
            BpmnSemanticTypes.Task,
            includeTypeMarker: false);
        var unsupportedMarker = Node(
            "unsupported-node-marker",
            BpmnSemanticTypes.Task,
            projectedType: "BPMN.Gateway");
        var algorithm = new BpmnLayoutAlgorithm();

        var missingResult = algorithm.Compute(
            Graph([missingMarker]),
            LayoutContext.Empty,
            CancellationToken.None);
        var unsupportedResult = algorithm.Compute(
            Graph([unsupportedMarker]),
            LayoutContext.Empty,
            CancellationToken.None);

        AssertFailure(missingResult);
        AssertFailure(unsupportedResult);
    }

    [Fact]
    public void UnsupportedEdgeTypeMetadataFailsWithoutReinterpretingItAsSequenceFlow()
    {
        var source = Node("unsupported-edge-source", BpmnSemanticTypes.Task);
        var target = Node("unsupported-edge-target", BpmnSemanticTypes.EndEvent);
        var unsupported = Flow(
            "unsupported-edge",
            source,
            target,
            projectedType: "BPMN.MessageFlow");

        var result = new BpmnLayoutAlgorithm().Compute(
            Graph([source, target], [unsupported]),
            LayoutContext.Empty,
            CancellationToken.None);

        AssertFailure(result);
    }

    [Fact]
    public void CancellationIsObservedDirectlyAndThroughTheFrameworkLayoutEngine()
    {
        var start = Node("cancel-start", BpmnSemanticTypes.StartEvent);
        var end = Node("cancel-end", BpmnSemanticTypes.EndEvent);
        var graph = Graph([start, end], [Flow("cancel-flow", start, end)]);
        var algorithm = new BpmnLayoutAlgorithm();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = Assert.Throws<OperationCanceledException>(() => algorithm.Compute(
            graph,
            LayoutContext.Empty,
            cancellation.Token));
        var engineResult = new LayoutEngine(
        [
            new LayoutAlgorithmRegistration(BpmnAlgorithmIds.DefaultLayout, algorithm),
        ]).Layout(
            graph,
            BpmnAlgorithmIds.DefaultLayout,
            cancellationToken: cancellation.Token);

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(LayoutExecutionStatus.Cancelled, engineResult.Status);
        Assert.Null(engineResult.Result);
        Assert.Equal(
            LayoutDiagnosticCodes.Cancelled,
            Assert.Single(engineResult.Diagnostics).Code);
    }

    private static LayoutComputation Run(ProjectedGraph graph) =>
        Successful(new BpmnLayoutAlgorithm().Compute(
            graph,
            LayoutContext.Empty,
            CancellationToken.None));

    private static LayoutComputation Successful(LayoutAlgorithmResult result)
    {
        Assert.True(
            result.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.DoesNotContain(
            result.Diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return Assert.IsType<LayoutComputation>(result.Computation);
    }

    private static void AssertFailure(LayoutAlgorithmResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Null(result.Computation);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static LayoutNodeGeometry Geometry(
        LayoutComputation computation,
        ProjectedNode node) =>
        Assert.Single(computation.Nodes, geometry => geometry.ProjectedObjectId == node.Id);

    private static ProjectedGraph Graph(
        IEnumerable<ProjectedNode> nodes,
        IEnumerable<ProjectedEdge>? edges = null) =>
        new(DocumentId, Revision, nodes, edges);

    private static ProjectedNode Node(
        string localKey,
        SemanticTypeId semanticTypeId,
        ProjectedPlacementHint? placementHint = null,
        string? projectedType = null,
        bool includeTypeMarker = true,
        bool includePlacementHint = true)
    {
        IEnumerable<KeyValuePair<string, PropertyValue>>? projectedProperties = includeTypeMarker
            ?
            [
                new(
                    ProjectedSemanticTypeProperty,
                    PropertyValue.FromText(projectedType ?? semanticTypeId.Value)),
            ]
            : null;
        return new ProjectedNode(
            new ProjectionSourceTrace(
                DocumentId,
                NodeRuleId,
                ProjectionSourceKind.SemanticElement,
                new SemanticElementId($"bpmn:m2-layout:{localKey}"),
                semanticTypeId,
                $"node:{localKey}"),
            includePlacementHint
                ? placementHint ?? Automatic(DefaultSize(semanticTypeId))
                : null,
            projectedProperties: projectedProperties);
    }

    private static ProjectedEdge Flow(
        string localKey,
        ProjectedNode source,
        ProjectedNode target,
        string? projectedType = null) =>
        new(
            new ProjectionSourceTrace(
                DocumentId,
                EdgeRuleId,
                ProjectionSourceKind.SemanticRelationship,
                new SemanticElementId($"bpmn:m2-layout:{localKey}"),
                BpmnSemanticTypes.SequenceFlow,
                $"edge:{localKey}"),
            source.Id,
            target.Id,
            projectedProperties:
            [
                new(
                    ProjectedSemanticTypeProperty,
                    PropertyValue.FromText(projectedType ?? BpmnSemanticTypes.SequenceFlow.Value)),
            ]);

    private static ProjectedPlacementHint Automatic(SizeD size) =>
        new(new PointD(900d, 700d), size, VisualPlacementMode.Automatic);

    private static ProjectedPlacementHint Pinned(RectD bounds) =>
        new(bounds.TopLeft, bounds.Size, VisualPlacementMode.Pinned);

    private static SizeD DefaultSize(SemanticTypeId semanticTypeId) =>
        semanticTypeId == BpmnSemanticTypes.Task
            ? new SizeD(120d, 80d)
            : semanticTypeId == BpmnSemanticTypes.ExclusiveGateway
                ? new SizeD(48d, 48d)
                : new SizeD(36d, 36d);

    private static RectD HintBounds(ProjectedNode node)
    {
        var hint = Assert.IsType<ProjectedPlacementHint>(node.PlacementHint);
        return new RectD(hint.Position.X, hint.Position.Y, hint.Size.Width, hint.Size.Height);
    }

    private static double CenterY(RectD bounds) => bounds.Top + (bounds.Height / 2d);

    private static void AssertFinite(RectD bounds)
    {
        Assert.True(double.IsFinite(bounds.X));
        Assert.True(double.IsFinite(bounds.Y));
        Assert.True(double.IsFinite(bounds.Width));
        Assert.True(double.IsFinite(bounds.Height));
    }
}
