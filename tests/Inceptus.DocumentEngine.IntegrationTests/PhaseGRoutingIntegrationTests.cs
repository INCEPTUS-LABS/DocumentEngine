using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.IntegrationTests.Fixtures;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.EditorState;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseGRoutingIntegrationTests
{
    private static readonly SemanticTypeId NodeTypeId = new("test:node");
    private static readonly SemanticTypeId EdgeTypeId = new("test:edge");
    private static readonly ProjectionRuleId NodeRuleId = new("test:projection:node");
    private static readonly ProjectionRuleId EdgeRuleId = new("test:projection:edge");
    private static readonly AlgorithmId LayoutAlgorithmId = new("test:layout:neutral");
    private static readonly AlgorithmId RoutingAlgorithmId = new("test:routing:neutral");

    [Fact]
    public void ReconstructedNeutralDocumentProjectsLaysOutAndRoutesWithoutAuthoritativeEffects()
    {
        var fixture = new PluginNeutralDocumentFixture();
        var creation = DocumentFactory.Create(fixture.CreateSnapshot(DocumentRevision.Zero));
        var createdDocument = Assert.IsType<Document>(creation.Document);
        Assert.True(creation.Succeeded);

        var persistentSnapshot = createdDocument.CaptureSnapshot();
        var reconstruction = DocumentReconstructor.Reconstruct(persistentSnapshot);
        var document = Assert.IsType<Document>(reconstruction.Document);
        Assert.True(reconstruction.Succeeded);
        Assert.Equal(persistentSnapshot, document.CaptureSnapshot());

        var history = new HistoryManager(document);
        var editorState = new EditorStateStore();
        var subscriber = new RecordingSubscriber();
        var commandProcessor = new CommandProcessor(subscribers: [subscriber]);
        var historyBefore = history.CaptureStatus();
        var editorStateBefore = editorState.CaptureSnapshot();
        var documentBefore = document.CaptureSnapshot();
        var revisionBefore = document.Revision;
        var projectionEngine = CreateProjectionEngine();
        var layoutEngine = new LayoutEngine(
        [
            new LayoutAlgorithmRegistration(LayoutAlgorithmId, new NeutralLayoutAlgorithm()),
        ]);
        var routingEngine = new RoutingEngine(
        [
            new RoutingAlgorithmRegistration(RoutingAlgorithmId, new NeutralRoutingAlgorithm()),
        ]);

        var firstProjection = projectionEngine.Project(documentBefore);
        var secondProjection = projectionEngine.Project(documentBefore);
        var firstGraph = Assert.IsType<ProjectedGraph>(firstProjection.Graph);
        var secondGraph = Assert.IsType<ProjectedGraph>(secondProjection.Graph);
        var firstLayoutExecution = layoutEngine.Layout(firstGraph, LayoutAlgorithmId);
        var secondLayoutExecution = layoutEngine.Layout(secondGraph, LayoutAlgorithmId);
        var firstLayout = Assert.IsType<LayoutResult>(firstLayoutExecution.Result);
        var secondLayout = Assert.IsType<LayoutResult>(secondLayoutExecution.Result);
        var firstRouting = routingEngine.Route(firstGraph, firstLayout, RoutingAlgorithmId);
        var secondRouting = routingEngine.Route(secondGraph, secondLayout, RoutingAlgorithmId);

        var secondReconstruction = DocumentReconstructor.Reconstruct(persistentSnapshot);
        var secondDocument = Assert.IsType<Document>(secondReconstruction.Document);
        var reconstructedProjection = projectionEngine.Project(secondDocument.CaptureSnapshot());
        var reconstructedGraph = Assert.IsType<ProjectedGraph>(reconstructedProjection.Graph);
        var reconstructedLayoutExecution = layoutEngine.Layout(
            reconstructedGraph,
            LayoutAlgorithmId);
        var reconstructedLayout = Assert.IsType<LayoutResult>(reconstructedLayoutExecution.Result);
        var reconstructedRouting = routingEngine.Route(
            reconstructedGraph,
            reconstructedLayout,
            RoutingAlgorithmId);

        Assert.True(firstProjection.IsSuccessful);
        Assert.True(secondProjection.IsSuccessful);
        Assert.True(firstLayoutExecution.IsSuccessful);
        Assert.True(secondLayoutExecution.IsSuccessful);
        Assert.True(firstRouting.IsSuccessful);
        Assert.True(secondRouting.IsSuccessful);
        Assert.True(reconstructedRouting.IsSuccessful);
        Assert.Equal(firstGraph, secondGraph);
        Assert.Equal(firstGraph, reconstructedGraph);
        Assert.Equal(firstLayout, secondLayout);
        Assert.Equal(firstLayout, reconstructedLayout);
        Assert.Equal(firstRouting, secondRouting);
        Assert.Equal(firstRouting, reconstructedRouting);

        var routing = Assert.IsType<RoutingResult>(firstRouting.Result);
        Assert.Equal(fixture.DocumentId, routing.DocumentId);
        Assert.Equal(DocumentRevision.Zero, routing.SourceRevision);
        Assert.Equal(LayoutAlgorithmId, routing.LayoutAlgorithmId);
        Assert.Equal(RoutingAlgorithmId, routing.RoutingAlgorithmId);
        Assert.Equal(2, routing.RouteCount);
        Assert.Equal(
            firstGraph.Edges.Select(static edge => edge.Id),
            routing.Routes.Select(static route => route.ProjectedEdgeId));
        Assert.Equal("neutral-polyline", routing.Metadata["test:style"].TextValue);

        var persistentEdge = firstGraph.Edges.Single(edge => edge.PersistentRoute.Length > 0);
        var persistentRoute = routing.Routes.Single(route =>
            route.ProjectedEdgeId == persistentEdge.Id);
        Assert.Equal(
            persistentEdge.PersistentRoute.AsEnumerable(),
            persistentRoute.Path.AsEnumerable());
        var calculatedEdge = firstGraph.Edges.Single(edge => edge.PersistentRoute.IsEmpty);
        var calculatedRoute = routing.Routes.Single(route =>
            route.ProjectedEdgeId == calculatedEdge.Id);
        Assert.Equal(calculatedEdge.SourcePortId, calculatedRoute.SourcePortId);
        Assert.Equal(calculatedEdge.TargetPortId, calculatedRoute.TargetPortId);
        Assert.Equal(2, calculatedRoute.Path.Length);

        Assert.Equal(revisionBefore, document.Revision);
        Assert.Equal(documentBefore, document.CaptureSnapshot());
        Assert.Equal(historyBefore, history.CaptureStatus());
        Assert.Equal(editorStateBefore, editorState.CaptureSnapshot());
        Assert.Equal(0, subscriber.EventCount);

        GC.KeepAlive(commandProcessor);
    }

    private static ProjectionEngine CreateProjectionEngine() =>
        new(
        [
            new ProjectionRuleRegistration(
                NodeRuleId,
                ProjectionSourceKind.SemanticElement,
                NodeTypeId,
                new NeutralNodeRule()),
            new ProjectionRuleRegistration(
                EdgeRuleId,
                ProjectionSourceKind.SemanticRelationship,
                EdgeTypeId,
                new NeutralEdgeRule()),
        ]);

    private sealed class NeutralNodeRule : IProjectionRule
    {
        public ProjectionRuleResult Project(
            ProjectionRuleInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elementInput = Assert.IsType<ElementProjectionRuleInput>(input);
            var visualState = elementInput.VisualStates.FirstOrDefault();
            var source = new ProjectionSourceTrace(
                input.DocumentId,
                NodeRuleId,
                input.SourceKind,
                input.SemanticId,
                input.SemanticTypeId,
                "node",
                visualState?.Id);
            var placementHint = visualState is null
                ? null
                : new ProjectedPlacementHint(
                    visualState.Position,
                    visualState.Size,
                    visualState.PlacementMode);

            return ProjectionRuleResult.Success(
                new ProjectionRuleContribution(
                    nodes:
                    [
                        new ProjectedNode(
                            source,
                            placementHint,
                            semanticProperties: elementInput.Element.Properties),
                    ]));
        }
    }

    private sealed class NeutralEdgeRule : IProjectionRule
    {
        public ProjectionRuleResult Project(
            ProjectionRuleInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relationshipInput = Assert.IsType<RelationshipProjectionRuleInput>(input);
            var visualState = relationshipInput.VisualStates.FirstOrDefault();
            var source = new ProjectionSourceTrace(
                input.DocumentId,
                EdgeRuleId,
                input.SourceKind,
                input.SemanticId,
                input.SemanticTypeId,
                "edge",
                visualState?.Id);
            var edge = new ProjectedEdge(
                source,
                NodeId(input.DocumentId, relationshipInput.SourceElement.Id),
                NodeId(input.DocumentId, relationshipInput.TargetElement.Id),
                persistentRoute: visualState?.Route,
                semanticProperties: relationshipInput.Relationship.Properties);

            return ProjectionRuleResult.Success(new ProjectionRuleContribution(edges: [edge]));
        }

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
    }

    private sealed class NeutralLayoutAlgorithm : ILayoutAlgorithm
    {
        public LayoutAlgorithmResult Compute(
            ProjectedGraph graph,
            LayoutContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(context);
            var geometries = new List<LayoutNodeGeometry>(graph.NodeCount);
            for (var index = 0; index < graph.Nodes.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = graph.Nodes[index];
                var bounds = node.PlacementHint is null
                    ? new RectD(400d + (index * 140d), 100d, 80d, 40d)
                    : new RectD(
                        node.PlacementHint.Position.X,
                        node.PlacementHint.Position.Y,
                        node.PlacementHint.Size.Width,
                        node.PlacementHint.Size.Height);
                geometries.Add(new LayoutNodeGeometry(
                    node.Id,
                    bounds,
                    Matrix2D.CreateTranslation(bounds.X, bounds.Y)));
            }

            return LayoutAlgorithmResult.Success(new LayoutComputation(geometries));
        }
    }

    private sealed class NeutralRoutingAlgorithm : IRoutingAlgorithm
    {
        public RoutingAlgorithmResult Route(
            ProjectedGraph graph,
            LayoutResult layout,
            RoutingContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(layout);
            ArgumentNullException.ThrowIfNull(context);
            var routes = new List<RoutedConnectorGeometry>(graph.EdgeCount);
            foreach (var edge in graph.Edges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (edge.PersistentRoute.Length >= 2)
                {
                    routes.Add(new RoutedConnectorGeometry(
                        edge.Id,
                        edge.PersistentRoute[0],
                        edge.PersistentRoute[^1],
                        edge.PersistentRoute.Skip(1).SkipLast(1),
                        edge.SourcePortId,
                        edge.TargetPortId));
                    continue;
                }

                var source = layout.Nodes.Single(node =>
                    node.ProjectedObjectId == edge.SourceNodeId).Bounds;
                var target = layout.Nodes.Single(node =>
                    node.ProjectedObjectId == edge.TargetNodeId).Bounds;
                routes.Add(new RoutedConnectorGeometry(
                    edge.Id,
                    new PointD(source.Right, source.Y + (source.Height / 2d)),
                    new PointD(target.Left, target.Y + (target.Height / 2d)),
                    sourcePortId: edge.SourcePortId,
                    targetPortId: edge.TargetPortId));
            }

            return RoutingAlgorithmResult.Success(
                new RoutingComputation(
                    routes,
                    [new("test:style", PropertyValue.FromText("neutral-polyline"))]));
        }
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        private int _eventCount;

        internal int EventCount => Volatile.Read(ref _eventCount);

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            ArgumentNullException.ThrowIfNull(change);
            Interlocked.Increment(ref _eventCount);
            return ValueTask.CompletedTask;
        }
    }
}
