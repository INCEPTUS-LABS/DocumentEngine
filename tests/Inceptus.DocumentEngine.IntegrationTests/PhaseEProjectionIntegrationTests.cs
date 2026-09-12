using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.IntegrationTests.Fixtures;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.EditorState;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseEProjectionIntegrationTests
{
    private static readonly SemanticTypeId NodeTypeId = new("test:node");
    private static readonly SemanticTypeId EdgeTypeId = new("test:edge");
    private static readonly ProjectionRuleId NodeRuleId = new("test:projection:node");
    private static readonly ProjectionRuleId EdgeRuleId = new("test:projection:edge");

    [Fact]
    public void ReconstructedNeutralDocumentProjectsDeterministicallyWithoutAuthoritativeEffects()
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
        var engine = new ProjectionEngine(
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

        var first = engine.Project(documentBefore);
        var second = engine.Project(documentBefore);

        Assert.True(first.IsSuccessful);
        Assert.True(second.IsSuccessful);
        Assert.Equal(first, second);
        var graph = Assert.IsType<ProjectedGraph>(first.Graph);
        Assert.Equal(fixture.DocumentId, graph.DocumentId);
        Assert.Equal(DocumentRevision.Zero, graph.SourceRevision);
        Assert.Equal(3, graph.NodeCount);
        Assert.Equal(2, graph.EdgeCount);
        Assert.Empty(graph.Groups);
        Assert.Empty(graph.Ports);
        Assert.Empty(graph.Labels);

        AssertTraceability(graph, fixture);
        Assert.Equal(revisionBefore, document.Revision);
        Assert.Equal(documentBefore, document.CaptureSnapshot());
        Assert.Equal(historyBefore, history.CaptureStatus());
        Assert.Equal(editorStateBefore, editorState.CaptureSnapshot());
        Assert.Equal(0, subscriber.EventCount);

        GC.KeepAlive(commandProcessor);
    }

    private static void AssertTraceability(
        ProjectedGraph graph,
        PluginNeutralDocumentFixture fixture)
    {
        var alpha = graph.Nodes.Single(node =>
            node.Source.SemanticElementId == fixture.AlphaId);
        var beta = graph.Nodes.Single(node =>
            node.Source.SemanticElementId == fixture.BetaId);
        var gamma = graph.Nodes.Single(node =>
            node.Source.SemanticElementId == fixture.GammaId);
        var alphaBeta = graph.Edges.Single(edge =>
            edge.Source.SemanticElementId == fixture.AlphaToBetaId);
        var betaGamma = graph.Edges.Single(edge =>
            edge.Source.SemanticElementId == fixture.BetaToGammaId);

        Assert.Equal(new VisualStateId("test:visual-alpha"), alpha.Source.VisualStateId);
        Assert.Equal(new VisualStateId("test:visual-beta"), beta.Source.VisualStateId);
        Assert.Null(gamma.Source.VisualStateId);
        Assert.Equal(new VisualStateId("test:visual-alpha-beta"), alphaBeta.Source.VisualStateId);
        Assert.Null(betaGamma.Source.VisualStateId);
        Assert.Equal(NodeTypeId, gamma.Source.SemanticTypeId);
        Assert.Equal(EdgeTypeId, betaGamma.Source.SemanticTypeId);
        Assert.Equal(ProjectionSourceKind.SemanticElement, alpha.Source.SourceKind);
        Assert.Equal(ProjectionSourceKind.SemanticRelationship, alphaBeta.Source.SourceKind);
        Assert.Equal(alpha.Id, alphaBeta.SourceNodeId);
        Assert.Equal(beta.Id, alphaBeta.TargetNodeId);
        Assert.Equal(beta.Id, betaGamma.SourceNodeId);
        Assert.Equal(gamma.Id, betaGamma.TargetNodeId);
        Assert.Equal(3, alphaBeta.PersistentRoute.Length);
        Assert.Empty(betaGamma.PersistentRoute);
    }

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
            var node = new ProjectedNode(
                source,
                placementHint,
                semanticProperties: elementInput.Element.Properties);

            return ProjectionRuleResult.Success(
                new ProjectionRuleContribution(nodes: [node]));
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
