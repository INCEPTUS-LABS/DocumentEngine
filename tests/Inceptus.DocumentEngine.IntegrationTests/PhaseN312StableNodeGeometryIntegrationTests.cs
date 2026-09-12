using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Layout;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN312StableNodeGeometryIntegrationTests
{
    [Fact]
    public async Task FreshSelectionThenRepeatedSingleMovesChangeOnlyTheirExplicitTargets()
    {
        var context = await CreateContextAsync();
        var current = context.InitialArtifacts;
        var initialBounds = SceneNodeBounds(context.InitialScene, current.ProjectedGraph);

        var selected = await context.Pipeline.RebuildSceneAsync(
            current,
            context.Composition.Document.CaptureSnapshot().VisualModel,
            new EditorStateSnapshot([BpmnDemoPipeline.TaskVisualId]),
            CancellationToken.None);
        var selectedScene = Assert.IsType<Canvas2DScene>(selected.Scene);
        Assert.Equal(initialBounds, SceneNodeBounds(selectedScene, current.ProjectedGraph));
        Assert.Equal(1, context.LayoutProbe.InvocationCount);

        VisualStateId[] sequence =
        [
            BpmnDemoPipeline.TaskVisualId,
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            BpmnDemoPipeline.ApprovedTaskVisualId,
            BpmnDemoPipeline.StartEventVisualId,
            BpmnDemoPipeline.TaskVisualId,
        ];
        var history = ImmutableArray.CreateBuilder<EditingSessionPipelineArtifacts>();
        history.Add(current);
        for (var index = 0; index < sequence.Length; index++)
        {
            var target = sequence[index];
            var beforeScene = SceneNodeBounds(
                Assert.IsType<Canvas2DScene>(index == 0 ? selected.Scene : context.LastScene),
                current.ProjectedGraph);
            var beforeVisuals = context.Composition.Document.VisualModel.VisualStates
                .ToDictionary(static visual => visual.Id);
            var targetPosition = beforeScene[target].TopLeft +
                new VectorD(35d + (index * 4d), 20d + (index * 3d));
            var committed = await context.Processor.ExecuteAsync(
                context.Composition.Document,
                new MoveVisualStateCommand(
                    context.Composition.Document.DocumentId,
                    context.Composition.Document.Revision,
                    target,
                    targetPosition,
                    VisualPlacementMode.Pinned));
            Assert.True(committed.IsCommitted);

            var pipeline = await context.Pipeline.RunPreservingNodeLayoutAsync(
                context.Composition.Document.CaptureSnapshot(),
                current,
                history.ToImmutable(),
                Assert.IsType<NodeGeometryPipelineImpact>(
                    committed.CommittedEvent?.NodeGeometryImpact),
                context.Configuration.InitialEditorState,
                CancellationToken.None);
            current = Assert.IsType<EditingSessionPipelineArtifacts>(pipeline.Artifacts);
            context.LastScene = Assert.IsType<Canvas2DScene>(pipeline.Scene);
            history.Add(current);
            var afterScene = SceneNodeBounds(context.LastScene, current.ProjectedGraph);

            Assert.Equal(targetPosition, afterScene[target].TopLeft);
            AssertUnchangedExcept(beforeScene, afterScene, target);
            AssertPersistentVisualsUnchangedExcept(
                beforeVisuals,
                context.Composition.Document.VisualModel.VisualStates,
                target);
            Assert.Equal(1, context.LayoutProbe.InvocationCount);
        }
    }

    [Fact]
    public async Task ConnectedTaskResizePreservesOtherSceneNodesAndReroutesBothSides()
    {
        var context = await CreateContextAsync();
        var before = context.InitialArtifacts;
        var beforeScene = SceneNodeBounds(context.InitialScene, before.ProjectedGraph);
        var beforeVisuals = context.Composition.Document.VisualModel.VisualStates
            .ToDictionary(static visual => visual.Id);
        var beforeAnchors = beforeVisuals[BpmnDemoPipeline.TaskVisualId].ConnectorAnchors;
        var incomingBefore = Route(before, BpmnDemoPipeline.FirstSequenceFlowId);
        var outgoingBefore = Route(before, BpmnDemoPipeline.SecondSequenceFlowId);
        var original = beforeScene[BpmnDemoPipeline.TaskVisualId];
        var resizedBounds = new RectD(
            original.X - 12d,
            original.Y - 8d,
            original.Width + 45d,
            original.Height + 26d);

        var committed = await context.Processor.ExecuteAsync(
            context.Composition.Document,
            new ResizeVisualStateCommand(
                context.Composition.Document.DocumentId,
                context.Composition.Document.Revision,
                BpmnDemoPipeline.TaskVisualId,
                resizedBounds,
                VisualPlacementMode.Pinned));
        Assert.True(committed.IsCommitted);
        var pipeline = await context.Pipeline.RunPreservingNodeLayoutAsync(
            context.Composition.Document.CaptureSnapshot(),
            before,
            [before],
            Assert.IsType<NodeGeometryPipelineImpact>(
                committed.CommittedEvent?.NodeGeometryImpact),
            context.Configuration.InitialEditorState,
            CancellationToken.None);
        var after = Assert.IsType<EditingSessionPipelineArtifacts>(pipeline.Artifacts);
        var afterScene = SceneNodeBounds(
            Assert.IsType<Canvas2DScene>(pipeline.Scene),
            after.ProjectedGraph);

        Assert.Equal(resizedBounds, afterScene[BpmnDemoPipeline.TaskVisualId]);
        AssertUnchangedExcept(beforeScene, afterScene, BpmnDemoPipeline.TaskVisualId);
        AssertPersistentVisualsUnchangedExcept(
            beforeVisuals,
            context.Composition.Document.VisualModel.VisualStates,
            BpmnDemoPipeline.TaskVisualId);
        Assert.Equal(
            beforeAnchors.AsEnumerable(),
            context.Composition.Document.VisualModel.VisualStates
                .Single(visual => visual.Id == BpmnDemoPipeline.TaskVisualId)
                .ConnectorAnchors.AsEnumerable());
        Assert.False(incomingBefore.AsSpan().SequenceEqual(
            Route(after, BpmnDemoPipeline.FirstSequenceFlowId).AsSpan()));
        Assert.False(outgoingBefore.AsSpan().SequenceEqual(
            Route(after, BpmnDemoPipeline.SecondSequenceFlowId).AsSpan()));
        Assert.Equal(1, context.LayoutProbe.InvocationCount);
    }

    [Fact]
    public async Task AtomicMultiMoveChangesOnlyDeclaredNodesAndSkipsLayout()
    {
        var context = await CreateContextAsync();
        var before = context.InitialArtifacts;
        var beforeScene = SceneNodeBounds(context.InitialScene, before.ProjectedGraph);
        var delta = new VectorD(42d, 31d);
        var approved = BpmnDemoPipeline.ApprovedTaskVisualId;
        var rejected = BpmnDemoPipeline.RejectedTaskVisualId;

        var committed = await context.Processor.ExecuteAsync(
            context.Composition.Document,
            new MoveVisualStatesCommand(
                context.Composition.Document.DocumentId,
                context.Composition.Document.Revision,
                [
                    new VisualStateMove(
                        approved,
                        beforeScene[approved].TopLeft + delta,
                        VisualPlacementMode.Pinned),
                    new VisualStateMove(
                        rejected,
                        beforeScene[rejected].TopLeft + delta,
                        VisualPlacementMode.Pinned),
                ]));
        Assert.True(committed.IsCommitted);
        var pipeline = await context.Pipeline.RunPreservingNodeLayoutAsync(
            context.Composition.Document.CaptureSnapshot(),
            before,
            [before],
            Assert.IsType<NodeGeometryPipelineImpact>(
                committed.CommittedEvent?.NodeGeometryImpact),
            context.Configuration.InitialEditorState,
            CancellationToken.None);
        var after = Assert.IsType<EditingSessionPipelineArtifacts>(pipeline.Artifacts);
        var afterScene = SceneNodeBounds(
            Assert.IsType<Canvas2DScene>(pipeline.Scene),
            after.ProjectedGraph);

        Assert.Equal(beforeScene[approved].Translate(delta), afterScene[approved]);
        Assert.Equal(beforeScene[rejected].Translate(delta), afterScene[rejected]);
        AssertUnchangedExcept(beforeScene, afterScene, approved, rejected);
        Assert.Equal(1, context.LayoutProbe.InvocationCount);
    }

    private static async Task<TestContext> CreateContextAsync()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var probe = new CountingLayoutAlgorithm(new BpmnLayoutAlgorithm());
        var configuration = WithLayoutProbe(composition.Configuration, probe);
        var pipeline = new EditingSessionPipeline(configuration);
        var initial = await pipeline.RunFullAsync(
            composition.Document.CaptureSnapshot(),
            configuration.InitialEditorState,
            CancellationToken.None);
        return new TestContext(
            composition,
            configuration,
            pipeline,
            Processor(configuration),
            probe,
            Assert.IsType<EditingSessionPipelineArtifacts>(initial.Artifacts),
            Assert.IsType<Canvas2DScene>(initial.Scene));
    }

    private static Dictionary<VisualStateId, RectD> SceneNodeBounds(
        Canvas2DScene scene,
        ProjectedGraph graph) =>
        graph.Nodes.ToDictionary(
            static node => node.Source.VisualStateId!,
            node => Assert.Single(scene.Items, item =>
                item.Id == Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node"))
                .Bounds);

    private static ImmutableArray<PointD> Route(
        EditingSessionPipelineArtifacts artifacts,
        SemanticElementId relationshipId)
    {
        var edge = Assert.Single(artifacts.ProjectedGraph.Edges, candidate =>
            candidate.Source.SemanticElementId == relationshipId);
        return Assert.Single(artifacts.RoutingResult.Routes, route =>
            route.ProjectedEdgeId == edge.Id).Path;
    }

    private static void AssertUnchangedExcept(
        IReadOnlyDictionary<VisualStateId, RectD> before,
        IReadOnlyDictionary<VisualStateId, RectD> after,
        params VisualStateId[] changedIds)
    {
        var changed = changedIds.ToHashSet();
        Assert.Equal(before.Keys.OrderBy(static id => id.Value),
            after.Keys.OrderBy(static id => id.Value));
        foreach (var (id, bounds) in before)
        {
            if (!changed.Contains(id))
            {
                Assert.Equal(bounds, after[id]);
            }
        }
    }

    private static void AssertPersistentVisualsUnchangedExcept(
        Dictionary<VisualStateId, VisualStateSnapshot> before,
        IEnumerable<VisualStateSnapshot> after,
        params VisualStateId[] changedIds)
    {
        var changed = changedIds.ToHashSet();
        foreach (var visual in after)
        {
            if (!changed.Contains(visual.Id))
            {
                Assert.Equal(before[visual.Id], visual);
            }
        }
    }

    private static EditingSessionConfiguration WithLayoutProbe(
        EditingSessionConfiguration source,
        ILayoutAlgorithm probe) =>
        new(
            source.ProjectionEngine,
            new LayoutEngine(
                [new LayoutAlgorithmRegistration(source.LayoutAlgorithmId, probe)]),
            source.LayoutAlgorithmId,
            source.RoutingEngine,
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

    private static CommandProcessor Processor(EditingSessionConfiguration configuration) =>
        new(
            configuration.CommandHandlers,
            configuration.CommandValidators,
            historyPolicies: configuration.HistoryPolicies,
            connectorAnchorPolicyProvider: configuration.ConnectorAnchorPolicyProvider);

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

    private sealed class TestContext(
        DocumentCanvasComposition composition,
        EditingSessionConfiguration configuration,
        EditingSessionPipeline pipeline,
        CommandProcessor processor,
        CountingLayoutAlgorithm layoutProbe,
        EditingSessionPipelineArtifacts initialArtifacts,
        Canvas2DScene initialScene)
    {
        internal DocumentCanvasComposition Composition { get; } = composition;

        internal EditingSessionConfiguration Configuration { get; } = configuration;

        internal EditingSessionPipeline Pipeline { get; } = pipeline;

        internal CommandProcessor Processor { get; } = processor;

        internal CountingLayoutAlgorithm LayoutProbe { get; } = layoutProbe;

        internal EditingSessionPipelineArtifacts InitialArtifacts { get; } = initialArtifacts;

        internal Canvas2DScene InitialScene { get; } = initialScene;

        internal Canvas2DScene LastScene { get; set; } = initialScene;
    }
}
