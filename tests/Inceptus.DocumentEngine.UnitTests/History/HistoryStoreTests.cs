using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.History;

public sealed class HistoryStoreTests
{
    private static readonly DocumentId DocumentId = new("test:history-document");

    [Fact]
    public void PreparedRecordIsInvisibleUntilInstalledAndStalePreparationCannotOverwrite()
    {
        var store = new HistoryStore();
        var first = store.PrepareRecord(Entry("test:first"));
        var stale = store.PrepareRecord(Entry("test:stale"));

        Assert.True(first.Succeeded);
        Assert.Equal(0, store.Count);
        Assert.True(store.TryInstallPrepared(first.Mutation!));
        Assert.False(store.TryInstallPrepared(stale.Mutation!));

        Assert.Equal(1, store.Count);
        Assert.Equal(1, store.Cursor);
        Assert.True(store.CanUndo);
        Assert.False(store.CanRedo);
    }

    [Fact]
    public void UndoRedoUseCurrentRevisionAndNewRecordTruncatesRedoLinearly()
    {
        var store = new HistoryStore();
        Install(store, store.PrepareRecord(Entry("test:first")));
        Install(store, store.PrepareRecord(Entry("test:second")));

        var undo = store.PrepareUndo(DocumentId, new DocumentRevision(8));
        var undoCommand = Assert.IsType<TestCommand>(undo.Mutation!.RestorationCommand);
        Assert.Equal(new DocumentRevision(8), undoCommand.ExpectedRevision);
        Assert.Equal(DocumentId, undoCommand.TargetDocumentId);
        Install(store, undo);

        Assert.Equal(1, store.Cursor);
        Assert.True(store.CanRedo);

        var redo = store.PrepareRedo(DocumentId, new DocumentRevision(9));
        var redoCommand = Assert.IsType<TestCommand>(redo.Mutation!.RestorationCommand);
        Assert.Equal(new DocumentRevision(9), redoCommand.ExpectedRevision);
        Install(store, redo);

        var secondUndo = store.PrepareUndo(DocumentId, new DocumentRevision(10));
        Install(store, secondUndo);
        Install(store, store.PrepareRecord(Entry("test:replacement")));

        Assert.Equal(2, store.Count);
        Assert.Equal(2, store.Cursor);
        Assert.False(store.CanRedo);
        Assert.Equal(
            "test:replacement/undo",
            store.PrepareUndo(DocumentId, new DocumentRevision(11))
                .Mutation!.RestorationCommand!.TypeId.Value);
    }

    [Fact]
    public void UnavailableNavigationReturnsDiagnosticsWithoutChangingCursor()
    {
        var store = new HistoryStore();

        var undo = store.PrepareUndo(DocumentId, DocumentRevision.Zero);
        var redo = store.PrepareRedo(DocumentId, DocumentRevision.Zero);

        Assert.False(undo.Succeeded);
        Assert.Contains(undo.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.UndoUnavailable);
        Assert.False(redo.Succeeded);
        Assert.Contains(redo.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.RedoUnavailable);
        Assert.Equal(0, store.Cursor);
    }

    [Fact]
    public void DocumentAndScopeNavigationEntriesShareOneLinearCursorAndRedoSuffix()
    {
        var rootScopeId = new DocumentScopeId("test:history:root");
        var childScopeId = new DocumentScopeId("test:history:child");
        var siblingScopeId = new DocumentScopeId("test:history:sibling");
        var store = new HistoryStore();
        Install(store, store.PrepareRecord(Entry("test:document-mutation")));
        Install(store, store.PrepareRecord(new HistoryEntry(
            new ScopeNavigationHistoryEntry(rootScopeId, childScopeId))));

        var undoNavigation = store.PrepareUndo(DocumentId, new DocumentRevision(8));
        Assert.Equal(
            new ScopeNavigationHistoryEntry(rootScopeId, childScopeId),
            undoNavigation.Mutation!.ScopeNavigation);
        Assert.Null(undoNavigation.Mutation.RestorationCommand);
        Install(store, undoNavigation);

        var undoDocument = store.PrepareUndo(DocumentId, new DocumentRevision(8));
        Assert.NotNull(undoDocument.Mutation!.RestorationCommand);
        Assert.Null(undoDocument.Mutation.ScopeNavigation);
        Install(store, undoDocument);

        var redoDocument = store.PrepareRedo(DocumentId, new DocumentRevision(9));
        Assert.NotNull(redoDocument.Mutation!.RestorationCommand);
        Install(store, redoDocument);
        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: true), store.CaptureStatus());

        Install(store, store.PrepareRecord(new HistoryEntry(
            new ScopeNavigationHistoryEntry(rootScopeId, siblingScopeId))));

        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: false), store.CaptureStatus());
        Assert.False(store.PrepareRedo(DocumentId, new DocumentRevision(10)).Succeeded);
        var latest = store.PrepareUndo(DocumentId, new DocumentRevision(10));
        Assert.Equal(
            new ScopeNavigationHistoryEntry(rootScopeId, siblingScopeId),
            latest.Mutation!.ScopeNavigation);
    }

    [Fact]
    public void ScopeNavigationEntryRequiresDistinctExactScopeIds()
    {
        var scopeId = new DocumentScopeId("test:history:scope");

        Assert.Throws<ArgumentException>(() =>
            new ScopeNavigationHistoryEntry(scopeId, scopeId));
        Assert.Throws<ArgumentNullException>(() =>
            new ScopeNavigationHistoryEntry(null!, scopeId));
        Assert.Throws<ArgumentNullException>(() =>
            new ScopeNavigationHistoryEntry(scopeId, null!));
    }

    private static HistoryEntry Entry(string type) =>
        new(
            new CommandTypeId(type),
            new TestFactory(new CommandTypeId($"{type}/undo")),
            new TestFactory(new CommandTypeId($"{type}/redo")));

    private static void Install(
        HistoryStore store,
        HistoryMutationPreparationResult preparation)
    {
        Assert.True(preparation.Succeeded);
        Assert.True(store.TryInstallPrepared(preparation.Mutation!));
    }

    private sealed class TestFactory(CommandTypeId typeId) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new TestCommand(typeId, documentId, expectedRevision);
    }

    private sealed class TestCommand(
        CommandTypeId typeId,
        DocumentId documentId,
        DocumentRevision revision) : ICommand
    {
        public CommandTypeId TypeId { get; } = typeId;

        public DocumentId TargetDocumentId { get; } = documentId;

        public DocumentRevision ExpectedRevision { get; } = revision;

        public CommandCategory Category => CommandCategory.Visual;

        public AuthoritativeDocumentComponent AffectedComponents =>
            AuthoritativeDocumentComponent.VisualModel;
    }
}
