using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.IntegrationTests.Fixtures;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.EditorState;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseFLayoutIntegrationTests
{
    private static readonly SemanticTypeId NodeTypeId = new("test:node");
    private static readonly SemanticTypeId EdgeTypeId = new("test:edge");
    private static readonly ProjectionRuleId NodeRuleId = new("test:projection:node");
    private static readonly ProjectionRuleId EdgeRuleId = new("test:projection:edge");
    private static readonly AlgorithmId LayoutAlgorithmId = new("test:layout:neutral");

    [Fact]
    public void ReconstructedNeutralDocumentProjectsAndLaysOutDeterministicallyWithoutAuthoritativeEffects()
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
        var projectionEngine = ProjectionEngine();
        var layoutEngine = new LayoutEngine(
        [
            new LayoutAlgorithmRegistration(
                LayoutAlgorithmId,
                new NeutralLayoutAlgorithm()),
        ]);

        var firstProjection = projectionEngine.Project(documentBefore);
        var secondProjection = projectionEngine.Project(documentBefore);
        var firstGraph = Assert.IsType<ProjectedGraph>(firstProjection.Graph);
        var secondGraph = Assert.IsType<ProjectedGraph>(secondProjection.Graph);
        var firstLayout = layoutEngine.Layout(firstGraph, LayoutAlgorithmId);
        var secondLayout = layoutEngine.Layout(secondGraph, LayoutAlgorithmId);

        var secondReconstruction = DocumentReconstructor.Reconstruct(persistentSnapshot);
        var secondDocument = Assert.IsType<Document>(secondReconstruction.Document);
        var reconstructedProjection = projectionEngine.Project(secondDocument.CaptureSnapshot());
        var reconstructedGraph = Assert.IsType<ProjectedGraph>(reconstructedProjection.Graph);
        var reconstructedLayout = layoutEngine.Layout(
            reconstructedGraph,
            LayoutAlgorithmId);

        Assert.True(firstProjection.IsSuccessful);
        Assert.True(secondProjection.IsSuccessful);
        Assert.Equal(firstGraph, secondGraph);
        Assert.Equal(firstGraph, reconstructedGraph);
        Assert.True(firstLayout.IsSuccessful);
        Assert.Equal(firstLayout, secondLayout);
        Assert.Equal(firstLayout, reconstructedLayout);

        var result = Assert.IsType<LayoutResult>(firstLayout.Result);
        Assert.Equal(fixture.DocumentId, result.DocumentId);
        Assert.Equal(DocumentRevision.Zero, result.SourceRevision);
        Assert.Equal(LayoutAlgorithmId, result.AlgorithmId);
        Assert.Equal(3, result.NodeCount);
        Assert.Empty(result.Groups);
        Assert.Equal(
            firstGraph.Nodes.Select(static node => node.Id),
            result.Nodes.Select(static geometry => geometry.ProjectedObjectId));
        Assert.Equal("left-to-right", result.Metadata["test:direction"].TextValue);

        AssertPlacementInterpretation(firstGraph, result);
        Assert.Equal(revisionBefore, document.Revision);
        Assert.Equal(documentBefore, document.CaptureSnapshot());
        Assert.Equal(historyBefore, history.CaptureStatus());
        Assert.Equal(editorStateBefore, editorState.CaptureSnapshot());
        Assert.Equal(0, subscriber.EventCount);

        GC.KeepAlive(commandProcessor);
    }

    private static void AssertPlacementInterpretation(
        ProjectedGraph graph,
        LayoutResult result)
    {
        foreach (var node in graph.Nodes)
        {
            var geometry = result.Nodes.Single(item =>
                item.ProjectedObjectId == node.Id);
            if (node.PlacementHint is not null)
            {
                Assert.Equal(node.PlacementHint.Position, geometry.Position);
                Assert.Equal(node.PlacementHint.Size, geometry.Size);
            }
            else
            {
                Assert.True(geometry.Position.X >= 400d);
                Assert.Equal(new SizeD(80d, 40d), geometry.Size);
            }
        }
    }

    private static ProjectionEngine ProjectionEngine() =>
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

            return ProjectionRuleResult.Success(
                new ProjectionRuleContribution(edges: [edge]));
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

            return LayoutAlgorithmResult.Success(
                new LayoutComputation(
                    geometries,
                    metadata:
                    [
                        new("test:direction", PropertyValue.FromText("left-to-right")),
                    ]));
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
