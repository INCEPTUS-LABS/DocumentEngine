using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN313StableLabelGeometryIntegrationTests
{
    [Fact]
    public async Task RepeatedExclusiveGatewayLabelEditsResetUndoAndRedoPreserveDiagramGeometry()
    {
        await using var context = await TestContext.CreateAsync();
        await SelectAsync(context, BpmnDemoPipeline.ExclusiveGatewayVisualId);
        var baseline = CaptureDiagram(context.Session.CaptureState());
        var initialLayoutCount = context.LayoutProbe.InvocationCount;
        var initialRoutingCount = context.RoutingProbe.InvocationCount;
        var overrides = new NodeLabelVisualOverride?[]
        {
            new(200d, 96d, 300d, 120d),
            new(240d, 131d, 300d, 120d),
            new(240d, 131d, 356d, 146d),
            new(180d, 72d, 356d, 146d),
            null,
        };

        foreach (var targetOverride in overrides)
        {
            var beforeDocument = context.Composition.Document.CaptureSnapshot();
            var beforeHistory = context.Session.CaptureState().HistoryStatus.EntryCount;
            var result = await context.Session.ExecuteAsync(
                new UpdateNodeLabelVisualOverrideCommand(
                    beforeDocument.DocumentId,
                    beforeDocument.Revision,
                    BpmnDemoPipeline.ExclusiveGatewayVisualId,
                    targetOverride));
            await context.Session.WaitForIdleAsync();

            Assert.True(result.IsCommitted);
            Assert.Equal(beforeHistory + 1,
                context.Session.CaptureState().HistoryStatus.EntryCount);
            AssertOnlyLabelOverrideChanged(
                beforeDocument.VisualModel.VisualStates,
                context.Composition.Document.VisualModel.VisualStates,
                BpmnDemoPipeline.ExclusiveGatewayVisualId);
            AssertDiagramPreserved(context.Session.CaptureState(), baseline);
            AssertLabelScene(context.Session.CaptureState(), targetOverride);
        }

        for (var index = 0; index < 2; index++)
        {
            Assert.True((await context.Session.UndoAsync()).IsCommitted);
            await context.Session.WaitForIdleAsync();
            AssertDiagramPreserved(context.Session.CaptureState(), baseline);
        }

        for (var index = 0; index < 2; index++)
        {
            Assert.True((await context.Session.RedoAsync()).IsCommitted);
            await context.Session.WaitForIdleAsync();
            AssertDiagramPreserved(context.Session.CaptureState(), baseline);
        }

        Assert.Equal(initialLayoutCount, context.LayoutProbe.InvocationCount);
        Assert.Equal(initialRoutingCount, context.RoutingProbe.InvocationCount);
        Assert.Equal(1, initialLayoutCount);
        Assert.Equal(1, initialRoutingCount);
    }

    [Fact]
    public async Task ExclusiveParallelAndInclusiveGatewayOverridesAllUseSceneOnlyPipeline()
    {
        await using var context = await TestContext.CreateAsync();
        var baseline = CaptureDiagram(context.Session.CaptureState());
        VisualStateId[] gateways =
        [
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            BpmnDemoPipeline.ParallelSplitGatewayVisualId,
            BpmnDemoPipeline.InclusiveSplitGatewayVisualId,
        ];

        for (var index = 0; index < gateways.Length; index++)
        {
            var target = gateways[index];
            await SelectAsync(context, target);
            var state = context.Session.CaptureState();
            var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
            var node = Assert.Single(graph.Nodes, candidate =>
                candidate.Source.VisualStateId == target);
            var label = Assert.Single(graph.Labels, candidate =>
                candidate.OwnerId == node.Id);
            Assert.Equal(NodeLabelInteractionPolicy.MoveAndResize,
                label.NodeInteractionPolicy);
            var targetOverride = new NodeLabelVisualOverride(
                160d + (index * 15d),
                88d + (index * 10d),
                240d + (index * 30d),
                90d + (index * 15d));

            var committed = await context.Session.ExecuteAsync(
                new UpdateNodeLabelVisualOverrideCommand(
                    context.Composition.Document.DocumentId,
                    context.Composition.Document.Revision,
                    target,
                    targetOverride));
            await context.Session.WaitForIdleAsync();

            Assert.True(committed.IsCommitted);
            AssertDiagramPreserved(context.Session.CaptureState(), baseline);
            AssertLabelScene(context.Session.CaptureState(), targetOverride, target);
        }

        Assert.Equal(1, context.LayoutProbe.InvocationCount);
        Assert.Equal(1, context.RoutingProbe.InvocationCount);
    }

    [Fact]
    public async Task GatewayNameEditKeepsManualLabelBoxAndDiagramGeometryValuesStable()
    {
        await using var context = await TestContext.CreateAsync();
        await SelectAsync(context, BpmnDemoPipeline.ExclusiveGatewayVisualId);
        var manualOverride = new NodeLabelVisualOverride(200d, 104d, 260d, 84d);
        Assert.True((await context.Session.ExecuteAsync(
            new UpdateNodeLabelVisualOverrideCommand(
                context.Composition.Document.DocumentId,
                context.Composition.Document.Revision,
                BpmnDemoPipeline.ExclusiveGatewayVisualId,
                manualOverride))).IsCommitted);
        await context.Session.WaitForIdleAsync();
        var before = CaptureDiagram(context.Session.CaptureState());
        var beforeLabelBounds = LabelInteractionBounds(
            context.Session.CaptureState(),
            BpmnDemoPipeline.ExclusiveGatewayVisualId);

        var renamed = await context.Session.ExecuteAsync(
            new UpdateSemanticElementNameCommand(
                context.Composition.Document.DocumentId,
                context.Composition.Document.Revision,
                BpmnDemoPipeline.ExclusiveGatewayId,
                "BPMN.Name",
                "Order approved by customer?"));
        await context.Session.WaitForIdleAsync();
        var afterState = context.Session.CaptureState();
        var after = CaptureDiagram(afterState);

        Assert.True(renamed.IsCommitted);
        Assert.Equal(before.Nodes.AsEnumerable(), after.Nodes.AsEnumerable());
        Assert.Equal(before.Groups.AsEnumerable(), after.Groups.AsEnumerable());
        Assert.Equal(before.Routes.AsEnumerable(), after.Routes.AsEnumerable());
        Assert.Equal(before.AnchorPoints, after.AnchorPoints);
        Assert.Equal(before.StableSceneGeometry.AsEnumerable(),
            after.StableSceneGeometry.AsEnumerable());
        Assert.Equal(beforeLabelBounds, LabelInteractionBounds(
            afterState,
            BpmnDemoPipeline.ExclusiveGatewayVisualId));
        Assert.True(NodeLabelVisualOverride.TryRead(
            context.Composition.Document.VisualModel.VisualStates.Single(visual =>
                visual.Id == BpmnDemoPipeline.ExclusiveGatewayVisualId).Properties,
            out var retainedOverride));
        Assert.Equal(manualOverride, retainedOverride);
    }

    private static async Task SelectAsync(TestContext context, VisualStateId visualStateId)
    {
        var state = context.Session.CaptureState();
        var result = await context.Session.UpdateEditorStateAsync(
            new Contracts.EditorState.EditorStateSnapshot(
                selection: [visualStateId],
                hoveredObjectId: state.EditorState.HoveredObjectId,
                activeToolId: state.EditorState.ActiveToolId,
                viewport: state.EditorState.Viewport));
        Assert.True(result.Succeeded);
    }

    private static DiagramCapture CaptureDiagram(EditingSessionState state)
    {
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var layout = Assert.IsType<LayoutResult>(state.LayoutResult);
        var routing = Assert.IsType<RoutingResult>(state.RoutingResult);
        var scene = Assert.IsType<Canvas2DScene>(state.CurrentScene);
        var layoutById = layout.Nodes.ToDictionary(static node => node.ProjectedObjectId);
        var anchorPoints = graph.Ports
            .Select(port => (Port: port, Decoded: Decode(port)))
            .ToDictionary(
                static entry => entry.Decoded.Id,
                entry => ConnectorAnchorGeometryResolver.ResolvePoint(
                    layoutById[entry.Port.OwnerNodeId].Bounds,
                    entry.Decoded.Side,
                    entry.Decoded.Order,
                    entry.Decoded.SideCount));
        var stableSceneGeometry = scene.Items
            .Where(static item =>
                item.Layer is Canvas2DSceneLayer.Content or
                    Canvas2DSceneLayer.Connector or
                    Canvas2DSceneLayer.Decoration &&
                (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0)
            .OrderBy(static item => item.Id.Value, StringComparer.Ordinal)
            .Select(static item => new SceneGeometry(
                item.Id,
                item.Bounds,
                item.Geometry,
                item.Transform,
                item.Clip))
            .ToImmutableArray();
        return new DiagramCapture(
            layout.Nodes,
            layout.Groups,
            layout.Computation,
            routing.Routes,
            routing.Computation,
            anchorPoints,
            stableSceneGeometry);
    }

    private static void AssertDiagramPreserved(
        EditingSessionState state,
        DiagramCapture expected)
    {
        var actual = CaptureDiagram(state);
        Assert.Equal(expected.Nodes.AsEnumerable(), actual.Nodes.AsEnumerable());
        Assert.Equal(expected.Groups.AsEnumerable(), actual.Groups.AsEnumerable());
        Assert.Same(expected.LayoutComputation, actual.LayoutComputation);
        Assert.Equal(expected.Routes.AsEnumerable(), actual.Routes.AsEnumerable());
        Assert.Same(expected.RoutingComputation, actual.RoutingComputation);
        Assert.Equal(expected.AnchorPoints, actual.AnchorPoints);
        Assert.Equal(expected.StableSceneGeometry.AsEnumerable(),
            actual.StableSceneGeometry.AsEnumerable());
    }

    private static void AssertLabelScene(
        EditingSessionState state,
        NodeLabelVisualOverride? targetOverride,
        VisualStateId? targetVisualStateId = null)
    {
        targetVisualStateId ??= BpmnDemoPipeline.ExclusiveGatewayVisualId;
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var layout = Assert.IsType<LayoutResult>(state.LayoutResult);
        var scene = Assert.IsType<Canvas2DScene>(state.CurrentScene);
        var node = Assert.Single(graph.Nodes, candidate =>
            candidate.Source.VisualStateId == targetVisualStateId);
        var label = Assert.Single(graph.Labels, candidate => candidate.OwnerId == node.Id);
        var nodeBounds = Assert.Single(layout.Nodes, candidate =>
            candidate.ProjectedObjectId == node.Id).Bounds;
        var interaction = Assert.Single(scene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                label.Id,
                "label-interaction"));
        if (targetOverride is not null)
        {
            var expectedBounds = new RectD(
                nodeBounds.Left + (nodeBounds.Width / 2d) + targetOverride.OffsetX -
                    (targetOverride.Width / 2d),
                nodeBounds.Top + (nodeBounds.Height / 2d) + targetOverride.OffsetY -
                    (targetOverride.Height / 2d),
                targetOverride.Width,
                targetOverride.Height);
            Assert.Equal(expectedBounds, interaction.Bounds);
        }

        var zones = scene.Items.Where(item =>
            item.Origin.VisualStateId == targetVisualStateId &&
            item.Origin.StableSourceKey?.StartsWith(
                "node-label-resize-zone:",
                StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(8, zones.Length);
        Assert.All(zones, zone => Assert.Contains(interaction.Id,
            zone.Origin.RelatedSceneObjectIds));
    }

    private static RectD LabelInteractionBounds(
        EditingSessionState state,
        VisualStateId visualStateId)
    {
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var scene = Assert.IsType<Canvas2DScene>(state.CurrentScene);
        var node = Assert.Single(graph.Nodes, candidate =>
            candidate.Source.VisualStateId == visualStateId);
        var label = Assert.Single(graph.Labels, candidate => candidate.OwnerId == node.Id);
        return Assert.Single(scene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                label.Id,
                "label-interaction")).Bounds;
    }

    private static ProjectedConnectorAnchor Decode(ProjectedPort port)
    {
        Assert.True(ProjectedConnectorAnchorMetadata.TryDecode(port, out var anchor));
        return Assert.IsType<ProjectedConnectorAnchor>(anchor);
    }

    private static void AssertOnlyLabelOverrideChanged(
        IEnumerable<VisualStateSnapshot> before,
        IEnumerable<VisualStateSnapshot> after,
        VisualStateId targetVisualStateId)
    {
        var beforeById = before.ToDictionary(static visual => visual.Id);
        foreach (var current in after)
        {
            var previous = beforeById[current.Id];
            if (current.Id != targetVisualStateId)
            {
                Assert.Equal(previous, current);
                continue;
            }

            Assert.Equal(previous.SemanticElementId, current.SemanticElementId);
            Assert.Equal(previous.Position, current.Position);
            Assert.Equal(previous.Size, current.Size);
            Assert.Equal(previous.PlacementMode, current.PlacementMode);
            Assert.Equal(previous.Route.AsEnumerable(), current.Route.AsEnumerable());
            Assert.Equal(previous.ConnectorAnchors.AsEnumerable(),
                current.ConnectorAnchors.AsEnumerable());
            Assert.Equal(previous.SourceAnchorId, current.SourceAnchorId);
            Assert.Equal(previous.TargetAnchorId, current.TargetAnchorId);
        }
    }

    private sealed class CountingLayoutAlgorithm(ILayoutAlgorithm inner) : ILayoutAlgorithm
    {
        internal int InvocationCount { get; private set; }

        public LayoutAlgorithmResult Compute(
            ProjectedGraph graph,
            LayoutContext context,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return inner.Compute(graph, context, cancellationToken);
        }
    }

    private sealed class CountingRoutingAlgorithm(IRoutingAlgorithm inner) : IRoutingAlgorithm
    {
        internal int InvocationCount { get; private set; }

        public RoutingAlgorithmResult Route(
            ProjectedGraph graph,
            LayoutResult layout,
            RoutingContext context,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return inner.Route(graph, layout, context, cancellationToken);
        }
    }

    private sealed class TestContext : IAsyncDisposable
    {
        private readonly Canvas2DRenderer _renderer;

        private TestContext(
            DocumentCanvasComposition composition,
            EditingSession session,
            Canvas2DRenderer renderer,
            CountingLayoutAlgorithm layoutProbe,
            CountingRoutingAlgorithm routingProbe)
        {
            Composition = composition;
            Session = session;
            _renderer = renderer;
            LayoutProbe = layoutProbe;
            RoutingProbe = routingProbe;
        }

        internal DocumentCanvasComposition Composition { get; }

        internal EditingSession Session { get; }

        internal CountingLayoutAlgorithm LayoutProbe { get; }

        internal CountingRoutingAlgorithm RoutingProbe { get; }

        internal static async Task<TestContext> CreateAsync()
        {
            var composition = await BpmnModelerTestComposition.CreateDemoAsync();
            var source = composition.Configuration;
            var layoutProbe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
            var routingProbe = new CountingRoutingAlgorithm(new BpmnRoutingAlgorithm());
            var configuration = new EditingSessionConfiguration(
                source.ProjectionEngine,
                new LayoutEngine(
                    [new LayoutAlgorithmRegistration(source.LayoutAlgorithmId, layoutProbe)]),
                source.LayoutAlgorithmId,
                new RoutingEngine(
                    [new RoutingAlgorithmRegistration(source.RoutingAlgorithmId, routingProbe)]),
                source.RoutingAlgorithmId,
                source.SceneBuilder,
                source.ProjectionContext,
                source.LayoutContext,
                source.RoutingContext,
                source.InitialEditorState,
                source.CommandHandlers,
                source.CommandValidators,
                source.HistoryPolicies,
                source.DocumentChangedSubscribers,
                source.ConnectorAnchorPolicyProvider);
            var renderer = new Canvas2DRenderer(
                new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
                new Canvas2DRendererConfiguration(
                    fontResources:
                    [
                        new Canvas2DFontResource(
                            "org.dejavu.DejaVuSans",
                            "2.37",
                            "DejaVu Sans",
                            "fonts/DejaVuSans-2.37.ttf"),
                    ],
                    defaultFontFamily: "DejaVu Sans"));
            Assert.True((await renderer.InitializeAsync(
                "phase-n313-canvas",
                new Canvas2DSurfaceSize(900d, 600d, 1.25d))).Succeeded);
            var attachment = await EditingSession.AttachAsync(
                composition.Document,
                renderer,
                configuration);
            return new TestContext(
                composition,
                Assert.IsType<EditingSession>(attachment.Session),
                renderer,
                layoutProbe,
                routingProbe);
        }

        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            await _renderer.DisposeAsync();
        }
    }

    private sealed record DiagramCapture(
        ImmutableArray<LayoutNodeGeometry> Nodes,
        ImmutableArray<LayoutGroupGeometry> Groups,
        LayoutComputation LayoutComputation,
        ImmutableArray<RoutedConnectorGeometry> Routes,
        RoutingComputation RoutingComputation,
        IReadOnlyDictionary<ConnectorAnchorId, PointD> AnchorPoints,
        ImmutableArray<SceneGeometry> StableSceneGeometry);

    private sealed record SceneGeometry(
        SceneObjectId Id,
        RectD Bounds,
        Canvas2DSceneGeometry Geometry,
        Matrix2D Transform,
        RectD? Clip);
}
