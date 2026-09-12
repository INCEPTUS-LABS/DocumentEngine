using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.History;

public sealed class ConnectorAnchorHistoryTests
{
    private static readonly DocumentId DocumentId = new("test:anchor-history");
    private static readonly SemanticElementId SourceId = new("test:source");
    private static readonly SemanticElementId TargetId = new("test:target");
    private static readonly SemanticElementId RelationshipId = new("test:relationship");
    private static readonly VisualStateId SourceVisualId = new("test:source-visual");
    private static readonly VisualStateId TargetVisualId = new("test:target-visual");
    private static readonly VisualStateId ConnectorVisualId = new("test:connector-visual");

    [Fact]
    public async Task AddIsOneHistoryEntryAndUndoRedoRestoreSameIdentityAndOrder()
    {
        var document = CreateDocument(
        [
            Anchor("a1", ConnectorAnchorRole.Source, 0),
            Anchor("a2", ConnectorAnchorRole.Target, 1),
        ]);
        var semanticBefore = document.CaptureSnapshot().SemanticModel;
        var subscriber = new CountingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);
        var history = new HistoryManager(document);
        var newId = new ConnectorAnchorId("new");

        var add = await history.ExecuteAsync(processor, new AddConnectorAnchorCommand(
            DocumentId,
            document.Revision,
            SourceVisualId,
            newId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Target,
            1));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(add.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
        Assert.Equal(1, subscriber.Count);
        AssertAnchors(document, ("a1", 0), ("new", 1), ("a2", 2));
        Assert.Equal(
            semanticBefore.Elements.AsEnumerable(),
            document.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            semanticBefore.Relationships.AsEnumerable(),
            document.SemanticModel.Relationships.AsEnumerable());

        var undo = await history.UndoAsync(processor);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(undo.IsCommitted);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
        AssertAnchors(document, ("a1", 0), ("a2", 1));

        var redo = await history.RedoAsync(processor);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(redo.IsCommitted);
        Assert.Equal(new DocumentRevision(3), document.Revision);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
        AssertAnchors(document, ("a1", 0), ("new", 1), ("a2", 2));
        Assert.Equal(newId, SourceVisual(document).ConnectorAnchors[1].Id);
        Assert.Equal(3, subscriber.Count);
    }

    [Fact]
    public async Task RemoveIsOneHistoryEntryAndUndoRedoRestoreExactAnchor()
    {
        var document = CreateDocument(
        [
            Anchor("a1", ConnectorAnchorRole.Source, 0),
            Anchor("a2", ConnectorAnchorRole.Target, 1),
            Anchor("a3", ConnectorAnchorRole.Source, 2),
        ]);
        var processor = new CommandProcessor();
        var history = new HistoryManager(document);

        var remove = await history.ExecuteAsync(processor, new RemoveConnectorAnchorCommand(
            DocumentId,
            document.Revision,
            SourceVisualId,
            new ConnectorAnchorId("a2")));

        Assert.True(remove.IsCommitted);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
        AssertAnchors(document, ("a1", 0), ("a3", 1));

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        AssertAnchors(document, ("a1", 0), ("a2", 1), ("a3", 2));
        var restored = SourceVisual(document).ConnectorAnchors[1];
        Assert.Equal(ConnectorAnchorRole.Target, restored.Role);
        Assert.Equal(ConnectorAnchorSide.Right, restored.Side);

        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        AssertAnchors(document, ("a1", 0), ("a3", 1));
        Assert.Equal(1, history.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task UsedAnchorRemovalDoesNotCommitOrEnterHistory()
    {
        var usedId = new ConnectorAnchorId("used");
        var document = CreateDocument(
            [new ConnectorAnchor(
                usedId,
                ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Source,
                0)],
            connectorSourceAnchorId: usedId);
        var processor = new CommandProcessor();
        var history = new HistoryManager(document);
        var before = document.CaptureSnapshot();

        var result = await history.ExecuteAsync(processor, new RemoveConnectorAnchorCommand(
            DocumentId,
            document.Revision,
            SourceVisualId,
            usedId));

        Assert.False(result.IsCommitted);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Equal(new HistoryStatus(0, false, false), history.CaptureStatus());
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.ConnectorAnchorInUse);
    }

    [Fact]
    public async Task PolicyRejectedAddDoesNotCommitEnterHistoryOrPublishAnEvent()
    {
        var policy = Uniform(EdgeConnectorAnchorPolicy.Disabled);
        var provider = Provider(policy);
        var document = CreateDocument([], connectorAnchorPolicyProvider: provider);
        var subscriber = new CountingSubscriber();
        var processor = new CommandProcessor(
            subscribers: [subscriber],
            connectorAnchorPolicyProvider: provider);
        var history = new HistoryManager(document);
        var before = document.CaptureSnapshot();

        var result = await history.ExecuteAsync(processor, new AddConnectorAnchorCommand(
            DocumentId,
            document.Revision,
            SourceVisualId,
            new ConnectorAnchorId("rejected"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.False(result.IsCommitted);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Equal(new HistoryStatus(0, false, false), history.CaptureStatus());
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(0, subscriber.Count);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.ConnectorAnchorPolicyViolation);
    }

    [Fact]
    public async Task DynamicSingleAddUndoAndDifferentRoleAddRemainAtomic()
    {
        var policy = Uniform(EdgeConnectorAnchorPolicy.DynamicSingle());
        var provider = Provider(policy);
        var document = CreateDocument([], connectorAnchorPolicyProvider: provider);
        var subscriber = new CountingSubscriber();
        var processor = new CommandProcessor(
            subscribers: [subscriber],
            connectorAnchorPolicyProvider: provider);
        var history = new HistoryManager(document);

        var first = await history.ExecuteAsync(processor, new AddConnectorAnchorCommand(
            DocumentId,
            document.Revision,
            SourceVisualId,
            new ConnectorAnchorId("source"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0));
        var rejected = await history.ExecuteAsync(processor, new AddConnectorAnchorCommand(
            DocumentId,
            document.Revision,
            SourceVisualId,
            new ConnectorAnchorId("target-before-undo"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Target,
            1));
        var undo = await history.UndoAsync(processor);
        var replacement = await history.ExecuteAsync(processor, new AddConnectorAnchorCommand(
            DocumentId,
            document.Revision,
            SourceVisualId,
            new ConnectorAnchorId("target"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Target,
            0));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(first.IsCommitted);
        Assert.False(rejected.IsCommitted);
        Assert.True(undo.IsCommitted);
        Assert.True(replacement.IsCommitted);
        Assert.Equal(new DocumentRevision(3), document.Revision);
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());
        var anchor = Assert.Single(SourceVisual(document).ConnectorAnchors);
        Assert.Equal(new ConnectorAnchorId("target"), anchor.Id);
        Assert.Equal(ConnectorAnchorRole.Target, anchor.Role);
        Assert.Equal(3, subscriber.Count);
    }

    private static Document CreateDocument(
        IEnumerable<ConnectorAnchor> sourceAnchors,
        ConnectorAnchorId? connectorSourceAnchorId = null,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null)
    {
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [Element(SourceId), Element(TargetId)],
                [new SemanticRelationshipSnapshot(
                    RelationshipId,
                    new SemanticTypeId("test:relationship-type"),
                    SourceId,
                    TargetId)]),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    ElementVisual(SourceVisualId, SourceId, sourceAnchors),
                    ElementVisual(TargetVisualId, TargetId, []),
                    new VisualStateSnapshot(
                        ConnectorVisualId,
                        RelationshipId,
                        default,
                        default,
                        VisualPlacementMode.Automatic,
                        sourceAnchorId: connectorSourceAnchorId),
                ]),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
        return Assert.IsType<Document>(DocumentFactory.Create(
            snapshot,
            connectorAnchorPolicyProvider).Document);
    }

    private static ElementConnectorAnchorPolicy Uniform(EdgeConnectorAnchorPolicy edge) =>
        new(edge, edge, edge, edge);

    private static ElementConnectorAnchorPolicyRegistry Provider(
        ElementConnectorAnchorPolicy policy) =>
        new(
        [
            new ElementConnectorAnchorPolicyRegistration(
                new SemanticTypeId("test:element-type"),
                policy),
        ]);

    private static SemanticElementSnapshot Element(SemanticElementId id) =>
        new(id, new SemanticTypeId("test:element-type"));

    private static VisualStateSnapshot ElementVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        IEnumerable<ConnectorAnchor> anchors) =>
        new(
            visualStateId,
            semanticElementId,
            new PointD(10d, 20d),
            new SizeD(100d, 60d),
            VisualPlacementMode.Manual,
            connectorAnchors: anchors);

    private static ConnectorAnchor Anchor(
        string id,
        ConnectorAnchorRole role,
        int order) =>
        new(
            new ConnectorAnchorId(id),
            ConnectorAnchorSide.Right,
            role,
            order);

    private static VisualStateSnapshot SourceVisual(Document document)
    {
        Assert.True(document.VisualModel.TryGetVisualState(SourceVisualId, out var visual));
        return Assert.IsType<VisualStateSnapshot>(visual);
    }

    private static void AssertAnchors(
        Document document,
        params (string Id, int Order)[] expected)
    {
        Assert.Equal(expected.Select(item => item.Id),
            SourceVisual(document).ConnectorAnchors.Select(anchor => anchor.Id.Value));
        Assert.Equal(expected.Select(item => item.Order),
            SourceVisual(document).ConnectorAnchors.Select(anchor => anchor.Order));
    }

    private sealed class CountingSubscriber : IDocumentChangedSubscriber
    {
        private int _count;

        internal int Count => Volatile.Read(ref _count);

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.CompletedTask;
        }
    }
}
