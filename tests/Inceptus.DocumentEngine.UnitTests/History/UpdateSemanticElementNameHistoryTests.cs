using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.UnitTests.Commands;

namespace Inceptus.DocumentEngine.UnitTests.History;

public sealed class UpdateSemanticElementNameHistoryTests
{
    [Fact]
    public void PolicyBuildsExactInverseAndRedoNames()
    {
        var before = UpdateSemanticElementNameCommandProcessorTests.Snapshot(
            DocumentRevision.Zero,
            "Original");
        var committed = UpdateSemanticElementNameCommandProcessorTests.Snapshot(
            new DocumentRevision(1),
            "Renamed");
        var store = new HistoryStore();
        var coordinator = new HistoryCoordinator(
            [UpdateSemanticElementNameHistoryPolicy.Registration]);
        var command = UpdateSemanticElementNameCommandProcessorTests.Update(
            DocumentRevision.Zero,
            "Renamed");

        Install(store, coordinator.PrepareRecord(store, command, before, committed));

        var undo = store.PrepareUndo(before.DocumentId, new DocumentRevision(1));
        var inverse = Assert.IsType<UpdateSemanticElementNameCommand>(
            undo.Mutation!.RestorationCommand);
        Assert.Equal("Original", inverse.TargetName);
        Assert.Equal("test:name", inverse.NamePropertyKey);
        Assert.Equal(new DocumentRevision(1), inverse.ExpectedRevision);
        Install(store, undo);

        var redo = store.PrepareRedo(before.DocumentId, new DocumentRevision(2));
        var replay = Assert.IsType<UpdateSemanticElementNameCommand>(
            redo.Mutation!.RestorationCommand);
        Assert.Equal("Renamed", replay.TargetName);
        Assert.Equal(new DocumentRevision(2), replay.ExpectedRevision);
    }

    [Fact]
    public async Task BuiltInHistoryRecordsOneNameEditAndUndoRedoRestoreExactNames()
    {
        var document = UpdateSemanticElementNameCommandProcessorTests.CreateDocument();
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();

        var update = await history.ExecuteAsync(
            processor,
            UpdateSemanticElementNameCommandProcessorTests.Update(
                document.Revision,
                "Renamed"));
        Assert.True(update.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal("Renamed", Name());
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());

        var undo = await history.UndoAsync(processor);
        Assert.True(undo.IsCommitted);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        Assert.Equal("Original", Name());
        Assert.Equal(new HistoryStatus(1, false, true), history.CaptureStatus());

        var redo = await history.RedoAsync(processor);
        Assert.True(redo.IsCommitted);
        Assert.Equal(new DocumentRevision(3), document.Revision);
        Assert.Equal("Renamed", Name());
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());

        string Name() => UpdateSemanticElementNameCommandProcessorTests.Name(
            document.CaptureSnapshot());
    }

    [Fact]
    public void PolicyRejectsMissingOrNonTextNameState()
    {
        var validBefore = UpdateSemanticElementNameCommandProcessorTests.Snapshot(
            DocumentRevision.Zero,
            "Original");
        var validCommitted = UpdateSemanticElementNameCommandProcessorTests.Snapshot(
            new DocumentRevision(1),
            "Renamed");
        var missingCommand = new UpdateSemanticElementNameCommand(
            validBefore.DocumentId,
            DocumentRevision.Zero,
            new SemanticElementId("test:missing"),
            "test:name",
            "Renamed");

        var result = new UpdateSemanticElementNameHistoryPolicy().Prepare(
            missingCommand,
            validBefore,
            validCommitted);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.InvalidPreparation);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void PolicyRejectsAnOldNameThatTheInverseCommandCannotRestore(string oldName)
    {
        var before = UpdateSemanticElementNameCommandProcessorTests.Snapshot(
            DocumentRevision.Zero,
            oldName);
        var committed = UpdateSemanticElementNameCommandProcessorTests.Snapshot(
            new DocumentRevision(1),
            "Renamed");
        var command = UpdateSemanticElementNameCommandProcessorTests.Update(
            DocumentRevision.Zero,
            "Renamed");

        var result = new UpdateSemanticElementNameHistoryPolicy().Prepare(
            command,
            before,
            committed);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.InvalidPreparation);
    }

    private static void Install(
        HistoryStore store,
        HistoryMutationPreparationResult preparation)
    {
        Assert.True(preparation.Succeeded);
        Assert.True(store.TryInstallPrepared(preparation.Mutation!));
    }
}
