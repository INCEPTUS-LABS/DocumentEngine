using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.History;

public sealed class N100GenericModelHistoryTests
{
    private static readonly DocumentId DocumentId = new("test:n100:generic");
    private static readonly ModelProfileId Organizational = new("test:profile:organizational");
    private static readonly ModelProfileId Stage = new("test:profile:stage");

    [Fact]
    public async Task AvailabilityBatchIsOnePersistentRevisionAndOneHistoryEntry()
    {
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(DocumentId).Document);
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();
        var command = new SetModelProfileAvailabilityCommand(
            document.DocumentId,
            document.Revision,
            [
                new(Organizational, true),
                new(Stage, true),
            ]);

        var applied = await history.ExecuteAsync(processor, command);

        Assert.True(applied.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.True(document.SemanticModel.ModelProfiles.IsAvailable(Organizational));
        Assert.True(document.SemanticModel.ModelProfiles.IsAvailable(Stage));
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());
        var roundtrip = DocumentReconstructor.Reconstruct(document.CaptureSnapshot());
        Assert.True(roundtrip.Succeeded);
        Assert.Equal(
            document.CaptureSnapshot(),
            Assert.IsType<Document>(roundtrip.Document).CaptureSnapshot());

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.False(document.SemanticModel.ModelProfiles.IsAvailable(Organizational));
        Assert.False(document.SemanticModel.ModelProfiles.IsAvailable(Stage));
        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.True(document.SemanticModel.ModelProfiles.IsAvailable(Organizational));
        Assert.True(document.SemanticModel.ModelProfiles.IsAvailable(Stage));
    }

    [Fact]
    public async Task TopLevelScopeCreationUsesExplicitIdentityAndUndoRestoresForestExactly()
    {
        var document = Assert.IsType<Document>(
            DocumentFactory.CreateEmpty(new DocumentId("test:n100:scope-command")).Document);
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();
        var peerId = new DocumentScopeId("test:process:B");

        var applied = await history.ExecuteAsync(
            processor,
            new CreateTopLevelDocumentScopeCommand(
                document.DocumentId,
                document.Revision,
                peerId));

        Assert.True(applied.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.True(document.SemanticModel.IsExplicitPeerRoot(peerId));
        var peer = Assert.Single(document.SemanticModel.NestedScopes);
        Assert.Empty(document.SemanticModel.Elements);
        Assert.Empty(document.SemanticModel.ScopeMemberships);
        Assert.Null(peer.ParentScopeId);
        Assert.Null(peer.OwnerSemanticElementId);
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Empty(document.SemanticModel.NestedScopes);
        Assert.Equal(document.SemanticModel.RootScopeId, new DocumentScopeId(document.DocumentId.Value));
        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.True(document.SemanticModel.IsExplicitPeerRoot(peerId));
    }

    [Fact]
    public async Task DuplicateScopeAndNoOpAvailabilityAreRejectedWithoutRevisionOrHistory()
    {
        var document = Assert.IsType<Document>(
            DocumentFactory.CreateEmpty(new DocumentId("test:n100:failures")).Document);
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();
        var peerId = new DocumentScopeId("test:process:B");
        Assert.True((await history.ExecuteAsync(
            processor,
            new CreateTopLevelDocumentScopeCommand(
                document.DocumentId,
                document.Revision,
                peerId))).IsCommitted);
        var beforeRevision = document.Revision;
        var beforeStatus = history.CaptureStatus();

        var duplicate = await history.ExecuteAsync(
            processor,
            new CreateTopLevelDocumentScopeCommand(
                document.DocumentId,
                document.Revision,
                peerId));
        var noOp = await history.ExecuteAsync(
            processor,
            new SetModelProfileAvailabilityCommand(
                document.DocumentId,
                document.Revision,
                [new(Organizational, false)]));

        Assert.False(duplicate.IsCommitted);
        Assert.False(noOp.IsCommitted);
        Assert.Equal(beforeRevision, document.Revision);
        Assert.Equal(beforeStatus, history.CaptureStatus());
    }
}
