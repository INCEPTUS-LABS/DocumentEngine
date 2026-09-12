using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.IntegrationTests.Fixtures;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.EditorState;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseHCanvas2DSceneIntegrationTests
{
    private static readonly SemanticTypeId NodeTypeId = new("test:node");
    private static readonly SemanticTypeId EdgeTypeId = new("test:edge");
    private static readonly ProjectionRuleId NodeRuleId = new("test:projection:node");
    private static readonly ProjectionRuleId EdgeRuleId = new("test:projection:edge");
    private static readonly AlgorithmId LayoutAlgorithmId = new("test:layout:neutral");
    private static readonly AlgorithmId RoutingAlgorithmId = new("test:routing:neutral");

    internal static (Document Document, Canvas2DScene Scene, EditorStateSnapshot EditorState)
        CreatePhaseIRendererInput()
    {
        var fixture = new PluginNeutralDocumentFixture();
        var alpha = fixture.VisualStates[0];
        fixture.VisualStates[0] = new VisualStateSnapshot(
            alpha.Id,
            alpha.SemanticElementId,
            alpha.Position,
            alpha.Size,
            alpha.PlacementMode,
            alpha.Route,
            [new("test:fill", PropertyValue.FromText("#dbeafe"))]);
        var creation = DocumentFactory.Create(fixture.CreateSnapshot(DocumentRevision.Zero));
        var createdDocument = Assert.IsType<Document>(creation.Document);
        var reconstruction = DocumentReconstructor.Reconstruct(createdDocument.CaptureSnapshot());
        var document = Assert.IsType<Document>(reconstruction.Document);
        var snapshot = document.CaptureSnapshot();
        var counts = new StageCounts();
        var projection = new ProjectionEngine(
        [
            new ProjectionRuleRegistration(
                NodeRuleId,
                ProjectionSourceKind.SemanticElement,
                NodeTypeId,
                new NeutralNodeRule(counts)),
            new ProjectionRuleRegistration(
                EdgeRuleId,
                ProjectionSourceKind.SemanticRelationship,
                EdgeTypeId,
                new NeutralEdgeRule(counts)),
        ]).Project(snapshot);
        var graph = Assert.IsType<ProjectedGraph>(projection.Graph);
        var layout = Assert.IsType<LayoutResult>(new LayoutEngine(
        [
            new LayoutAlgorithmRegistration(LayoutAlgorithmId, new NeutralLayoutAlgorithm(counts)),
        ]).Layout(graph, LayoutAlgorithmId).Result);
        var routing = Assert.IsType<RoutingResult>(new RoutingEngine(
        [
            new RoutingAlgorithmRegistration(RoutingAlgorithmId, new NeutralRoutingAlgorithm(counts)),
        ]).Route(graph, layout, RoutingAlgorithmId).Result);
        var sceneBuilder = new Canvas2DSceneBuilder(
            contributors:
            [
                new Canvas2DSceneContributorRegistration(
                    new Canvas2DSceneContributorDescriptor(
                        new Canvas2DSceneContributorId("test:phase-i-neutral-scene"),
                        "1"),
                    new NeutralSceneContributor()),
            ]);
        var baseScene = Assert.IsType<Canvas2DScene>(sceneBuilder.Build(
            graph,
            layout,
            routing,
            snapshot.VisualModel,
            EditorStateSnapshot.Empty).Scene);
        var alphaNode = baseScene.Items.Single(item =>
            item.Origin.SemanticElementId == fixture.AlphaId &&
            item.Layer == Canvas2DSceneLayer.Content);
        var editorState = new EditorStateSnapshot(
            selection: [alphaNode.Origin.VisualStateId!],
            viewport: new ViewportSnapshot(1.5d, new VectorD(25d, 15d)));
        var scene = Assert.IsType<Canvas2DScene>(sceneBuilder.Build(
            graph,
            layout,
            routing,
            snapshot.VisualModel,
            editorState).Scene);
        return (document, scene, editorState);
    }

    [Fact]
    public void ReconstructedNeutralPipelineBuildsDeterministicSceneAndSupportsSceneOnlyRebuild()
    {
        var fixture = new PluginNeutralDocumentFixture();
        var alpha = fixture.VisualStates[0];
        fixture.VisualStates[0] = new VisualStateSnapshot(
            alpha.Id,
            alpha.SemanticElementId,
            alpha.Position,
            alpha.Size,
            alpha.PlacementMode,
            alpha.Route,
            [new("test:fill", PropertyValue.FromText("#dbeafe"))]);
        var creation = DocumentFactory.Create(fixture.CreateSnapshot(DocumentRevision.Zero));
        var createdDocument = Assert.IsType<Document>(creation.Document);
        Assert.True(creation.Succeeded);
        var reconstruction = DocumentReconstructor.Reconstruct(createdDocument.CaptureSnapshot());
        var document = Assert.IsType<Document>(reconstruction.Document);
        Assert.True(reconstruction.Succeeded);

        var history = new HistoryManager(document);
        var editorStore = new EditorStateStore();
        var subscriber = new RecordingSubscriber();
        var commandProcessor = new CommandProcessor(subscribers: [subscriber]);
        var documentBefore = document.CaptureSnapshot();
        var revisionBefore = document.Revision;
        var historyBefore = history.CaptureStatus();
        var editorBefore = editorStore.CaptureSnapshot();
        var counts = new StageCounts();
        var projectionEngine = new ProjectionEngine(
        [
            new ProjectionRuleRegistration(
                NodeRuleId,
                ProjectionSourceKind.SemanticElement,
                NodeTypeId,
                new NeutralNodeRule(counts)),
            new ProjectionRuleRegistration(
                EdgeRuleId,
                ProjectionSourceKind.SemanticRelationship,
                EdgeTypeId,
                new NeutralEdgeRule(counts)),
        ]);
        var layoutEngine = new LayoutEngine(
        [
            new LayoutAlgorithmRegistration(LayoutAlgorithmId, new NeutralLayoutAlgorithm(counts)),
        ]);
        var routingEngine = new RoutingEngine(
        [
            new RoutingAlgorithmRegistration(RoutingAlgorithmId, new NeutralRoutingAlgorithm(counts)),
        ]);
        var sceneContributor = new NeutralSceneContributor();
        var sceneBuilder = new Canvas2DSceneBuilder(
            contributors:
            [
                new Canvas2DSceneContributorRegistration(
                    new Canvas2DSceneContributorDescriptor(
                        new Canvas2DSceneContributorId("test:neutral-scene"),
                        "1"),
                    sceneContributor),
            ]);

        var first = RunPipeline(
            documentBefore,
            projectionEngine,
            layoutEngine,
            routingEngine,
            sceneBuilder,
            EditorStateSnapshot.Empty);
        var second = RunPipeline(
            documentBefore,
            projectionEngine,
            layoutEngine,
            routingEngine,
            sceneBuilder,
            EditorStateSnapshot.Empty);

        Assert.Equal(first.Graph, second.Graph);
        Assert.Equal(first.Layout, second.Layout);
        Assert.Equal(first.Routing, second.Routing);
        Assert.Equal(first.Scene, second.Scene);
        Assert.Equal(3, first.Graph.NodeCount);
        Assert.Equal(2, first.Graph.EdgeCount);
        Assert.Equal(7, first.Scene.Items.Count(item => item.Origin.ProjectedObjectId is not null));
        Assert.All(
            first.Scene.Items.Where(item => item.Origin.ProjectedObjectId is not null),
            item =>
            {
                Assert.NotNull(item.Origin.SemanticElementId);
                Assert.True(item.Origin.Categories.HasFlag(
                    Canvas2DSceneOriginCategory.ProjectedRuntimeObject));
            });
        var alphaNode = first.Scene.Items.Single(item =>
            item.Origin.SemanticElementId == fixture.AlphaId &&
            item.Layer == Canvas2DSceneLayer.Content);
        Assert.Equal("#dbeafe", alphaNode.PersistentAppearance["test:fill"].TextValue);
        Assert.All(first.Routing.Routes, route =>
        {
            var sceneRoute = first.Scene.Items.Single(item =>
                item.Origin.ProjectedObjectId == route.ProjectedEdgeId &&
                !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));
            Assert.Equal(route.Path.AsEnumerable(), sceneRoute.Geometry.Points.AsEnumerable());
        });

        var countsBeforeSceneOnlyRebuild = counts.Capture();
        var editorOnly = new EditorStateSnapshot(
            selection: [alphaNode.Origin.VisualStateId!],
            hoveredObjectId: alphaNode.Id,
            viewport: new ViewportSnapshot(1.5d, new VectorD(25d, 15d)),
            temporaryFeedback:
            [
                new EditorFeedbackSnapshot(
                    "test:selection-rectangle",
                    "selection-rectangle",
                    new RectD(0d, 0d, 300d, 180d)),
            ]);
        var rebuilt = sceneBuilder.Build(
            first.Graph,
            first.Layout,
            first.Routing,
            documentBefore.VisualModel,
            editorOnly);
        var rebuiltScene = Assert.IsType<Canvas2DScene>(rebuilt.Scene);

        Assert.True(rebuilt.Succeeded);
        Assert.Equal(countsBeforeSceneOnlyRebuild, counts.Capture());
        Assert.Same(first.Graph, sceneContributor.LastGraph);
        Assert.Same(first.Layout, sceneContributor.LastLayout);
        Assert.Same(first.Routing, sceneContributor.LastRouting);
        Assert.Same(documentBefore.VisualModel, sceneContributor.LastVisualModel);
        Assert.Same(editorOnly, sceneContributor.LastEditorState);
        Assert.Equal(11, rebuiltScene.Items.Count(item =>
            item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.EditorState)));
        Assert.Equal(editorOnly.Viewport, rebuiltScene.Viewport);
        Assert.Equal(
            first.Scene.Items.Where(item =>
                !item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.EditorState)),
            rebuiltScene.Items.Where(item =>
                !item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.EditorState)));

        Assert.Equal(revisionBefore, document.Revision);
        Assert.Equal(documentBefore, document.CaptureSnapshot());
        Assert.Equal(historyBefore, history.CaptureStatus());
        Assert.Equal(editorBefore, editorStore.CaptureSnapshot());
        Assert.Equal(0, subscriber.EventCount);
        GC.KeepAlive(commandProcessor);
    }

    private static PipelineOutput RunPipeline(
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot snapshot,
        ProjectionEngine projectionEngine,
        LayoutEngine layoutEngine,
        RoutingEngine routingEngine,
        Canvas2DSceneBuilder sceneBuilder,
        EditorStateSnapshot editorState)
    {
        var projection = projectionEngine.Project(snapshot);
        var graph = Assert.IsType<ProjectedGraph>(projection.Graph);
        var layoutExecution = layoutEngine.Layout(graph, LayoutAlgorithmId);
        var layout = Assert.IsType<LayoutResult>(layoutExecution.Result);
        var routingExecution = routingEngine.Route(graph, layout, RoutingAlgorithmId);
        var routing = Assert.IsType<RoutingResult>(routingExecution.Result);
        var sceneExecution = sceneBuilder.Build(
            graph,
            layout,
            routing,
            snapshot.VisualModel,
            editorState);

        Assert.True(projection.IsSuccessful);
        Assert.True(layoutExecution.IsSuccessful);
        Assert.True(routingExecution.IsSuccessful);
        Assert.True(sceneExecution.Succeeded);
        return new PipelineOutput(
            graph,
            layout,
            routing,
            Assert.IsType<Canvas2DScene>(sceneExecution.Scene));
    }

    private sealed record PipelineOutput(
        ProjectedGraph Graph,
        LayoutResult Layout,
        RoutingResult Routing,
        Canvas2DScene Scene);

    private sealed class StageCounts
    {
        internal int ProjectionRules;
        internal int LayoutRuns;
        internal int RoutingRuns;

        internal (int ProjectionRules, int LayoutRuns, int RoutingRuns) Capture() =>
            (ProjectionRules, LayoutRuns, RoutingRuns);
    }

    private sealed class NeutralNodeRule(StageCounts counts) : IProjectionRule
    {
        public ProjectionRuleResult Project(
            ProjectionRuleInput input,
            CancellationToken cancellationToken)
        {
            counts.ProjectionRules++;
            cancellationToken.ThrowIfCancellationRequested();
            var elementInput = Assert.IsType<ElementProjectionRuleInput>(input);
            var visual = elementInput.VisualStates.FirstOrDefault();
            return ProjectionRuleResult.Success(new ProjectionRuleContribution(
                nodes:
                [
                    new ProjectedNode(
                        Source(input, NodeRuleId, visual?.Id, "node"),
                        visual is null
                            ? null
                            : new ProjectedPlacementHint(
                                visual.Position,
                                visual.Size,
                                visual.PlacementMode),
                        semanticProperties: elementInput.Element.Properties),
                ]));
        }
    }

    private sealed class NeutralEdgeRule(StageCounts counts) : IProjectionRule
    {
        public ProjectionRuleResult Project(
            ProjectionRuleInput input,
            CancellationToken cancellationToken)
        {
            counts.ProjectionRules++;
            cancellationToken.ThrowIfCancellationRequested();
            var relationship = Assert.IsType<RelationshipProjectionRuleInput>(input);
            var visual = relationship.VisualStates.FirstOrDefault();
            return ProjectionRuleResult.Success(new ProjectionRuleContribution(
                edges:
                [
                    new ProjectedEdge(
                        Source(input, EdgeRuleId, visual?.Id, "edge"),
                        NodeId(input.DocumentId, relationship.SourceElement.Id),
                        NodeId(input.DocumentId, relationship.TargetElement.Id),
                        persistentRoute: visual?.Route,
                        semanticProperties: relationship.Relationship.Properties),
                ]));
        }
    }

    private sealed class NeutralLayoutAlgorithm(StageCounts counts) : ILayoutAlgorithm
    {
        public LayoutAlgorithmResult Compute(
            ProjectedGraph graph,
            LayoutContext context,
            CancellationToken cancellationToken)
        {
            counts.LayoutRuns++;
            var nodes = graph.Nodes.Select((node, index) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bounds = node.PlacementHint is null
                    ? new RectD(400d + (index * 140d), 100d, 80d, 40d)
                    : new RectD(
                        node.PlacementHint.Position.X,
                        node.PlacementHint.Position.Y,
                        node.PlacementHint.Size.Width,
                        node.PlacementHint.Size.Height);
                return new LayoutNodeGeometry(
                    node.Id,
                    bounds,
                    Matrix2D.CreateTranslation(bounds.X, bounds.Y));
            });
            return LayoutAlgorithmResult.Success(new LayoutComputation(nodes));
        }
    }

    private sealed class NeutralRoutingAlgorithm(StageCounts counts) : IRoutingAlgorithm
    {
        public RoutingAlgorithmResult Route(
            ProjectedGraph graph,
            LayoutResult layout,
            RoutingContext context,
            CancellationToken cancellationToken)
        {
            counts.RoutingRuns++;
            var routes = graph.Edges.Select(edge =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (edge.PersistentRoute.Length >= 2)
                {
                    return new RoutedConnectorGeometry(
                        edge.Id,
                        edge.PersistentRoute[0],
                        edge.PersistentRoute[^1],
                        edge.PersistentRoute.Skip(1).SkipLast(1));
                }

                var source = layout.Nodes.Single(node =>
                    node.ProjectedObjectId == edge.SourceNodeId).Bounds;
                var target = layout.Nodes.Single(node =>
                    node.ProjectedObjectId == edge.TargetNodeId).Bounds;
                return new RoutedConnectorGeometry(
                    edge.Id,
                    new PointD(source.Right, source.Y + (source.Height / 2d)),
                    new PointD(target.Left, target.Y + (target.Height / 2d)));
            });
            return RoutingAlgorithmResult.Success(new RoutingComputation(routes));
        }
    }

    private sealed class NeutralSceneContributor : ICanvas2DSceneContributor
    {
        internal ProjectedGraph? LastGraph { get; private set; }
        internal LayoutResult? LastLayout { get; private set; }
        internal RoutingResult? LastRouting { get; private set; }
        internal VisualModelSnapshot? LastVisualModel { get; private set; }
        internal EditorStateSnapshot? LastEditorState { get; private set; }

        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            LastGraph = context.ProjectedGraph;
            LastLayout = context.LayoutResult;
            LastRouting = context.RoutingResult;
            LastVisualModel = context.VisualModel;
            LastEditorState = context.EditorState;
            var id = context.Contributor.ContributorId;
            var item = new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForExtension(id, "watermark"),
                Canvas2DSceneLayer.Overlay,
                -10,
                Canvas2DSceneGeometry.Text(new RectD(0d, 0d, 80d, 20d), "neutral"),
                new Canvas2DSceneOriginTrace(
                    Canvas2DSceneOriginCategory.RegisteredExtension,
                    stableSourceKey: "watermark"),
                style: new Canvas2DSceneStyle(fill: "#64748b"));
            return Canvas2DSceneContributionResult.Success(
                new Canvas2DSceneContribution([item]));
        }
    }

    private static ProjectionSourceTrace Source(
        ProjectionRuleInput input,
        ProjectionRuleId ruleId,
        VisualStateId? visualStateId,
        string localKey) =>
        new(
            input.DocumentId,
            ruleId,
            input.SourceKind,
            input.SemanticId,
            input.SemanticTypeId,
            localKey,
            visualStateId);

    private static ProjectedObjectId NodeId(
        DocumentId documentId,
        SemanticElementId semanticElementId) =>
        ProjectedObjectIdentity.Create(
            documentId,
            NodeRuleId,
            ProjectionSourceKind.SemanticElement,
            semanticElementId,
            ProjectedObjectKind.Node,
            "node");

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        private int _eventCount;

        internal int EventCount => Volatile.Read(ref _eventCount);

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Interlocked.Increment(ref _eventCount);
            return ValueTask.CompletedTask;
        }
    }
}
