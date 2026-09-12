using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.UnitTests.Commands;

namespace Inceptus.DocumentEngine.UnitTests.History;

public sealed class UpdateNodeLabelVisualOverrideHistoryTests
{
    private static readonly NodeLabelVisualOverride Manual =
        new(34d, -18d, 150d, 42d);

    [Fact]
    public void PolicyBuildsExactAutomaticInverseAndManualRedo()
    {
        var before = UpdateNodeLabelVisualOverrideCommandHandlerTests.Snapshot(
            DocumentRevision.Zero);
        var committed = UpdateNodeLabelVisualOverrideCommandHandlerTests.Snapshot(
            new DocumentRevision(1),
            Manual);
        var store = new HistoryStore();
        var coordinator = new HistoryCoordinator(
            [UpdateNodeLabelVisualOverrideHistoryPolicy.Registration]);
        var command = UpdateNodeLabelVisualOverrideCommandHandlerTests.Command(
            DocumentRevision.Zero,
            Manual);

        Install(store, coordinator.PrepareRecord(store, command, before, committed));

        var undo = store.PrepareUndo(before.DocumentId, new DocumentRevision(1));
        var inverse = Assert.IsType<UpdateNodeLabelVisualOverrideCommand>(
            undo.Mutation!.RestorationCommand);
        Assert.Null(inverse.TargetOverride);
        Assert.Equal(new DocumentRevision(1), inverse.ExpectedRevision);
        Install(store, undo);

        var redo = store.PrepareRedo(before.DocumentId, new DocumentRevision(2));
        var replay = Assert.IsType<UpdateNodeLabelVisualOverrideCommand>(
            redo.Mutation!.RestorationCommand);
        Assert.Equal(Manual, replay.TargetOverride);
        Assert.Equal(new DocumentRevision(2), replay.ExpectedRevision);
    }

    [Fact]
    public async Task BuiltInHistoryRestoresAutomaticAndExactManualState()
    {
        var document = CreateDocument(visualOverride: null);
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();

        var updated = await history.ExecuteAsync(
            processor,
            UpdateNodeLabelVisualOverrideCommandHandlerTests.Command(
                document.Revision,
                Manual));

        Assert.True(updated.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        AssertOverride(document, Manual, isExplicit: true);
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        AssertOverride(document, expected: null, isExplicit: false);
        Assert.Equal(new HistoryStatus(1, false, true), history.CaptureStatus());

        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.Equal(new DocumentRevision(3), document.Revision);
        AssertOverride(document, Manual, isExplicit: true);
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());
    }

    [Fact]
    public async Task BuiltInHistoryResetUndoRedoRestoresExactManualThenAutomaticState()
    {
        var document = CreateDocument(Manual);
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();

        var reset = await history.ExecuteAsync(
            processor,
            UpdateNodeLabelVisualOverrideCommandHandlerTests.Command(
                document.Revision,
                targetOverride: null));

        Assert.True(reset.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        AssertOverride(document, expected: null, isExplicit: false);
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        AssertOverride(document, Manual, isExplicit: true);
        Assert.Equal(new HistoryStatus(1, false, true), history.CaptureStatus());

        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.Equal(new DocumentRevision(3), document.Revision);
        AssertOverride(document, expected: null, isExplicit: false);
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());
    }

    [Fact]
    public void PolicyRejectsMissingOrMismatchedCommittedOverride()
    {
        var before = UpdateNodeLabelVisualOverrideCommandHandlerTests.Snapshot(
            DocumentRevision.Zero);
        var unexpected = UpdateNodeLabelVisualOverrideCommandHandlerTests.Snapshot(
            new DocumentRevision(1),
            new NodeLabelVisualOverride(1d, 2d, 80d, 20d));
        var missing = UpdateNodeLabelVisualOverrideCommandHandlerTests.Snapshot(
            new DocumentRevision(1),
            visualStateOverride: new VisualStateSnapshot(
                new VisualStateId("test:other-visual"),
                UpdateNodeLabelVisualOverrideCommandHandlerTests.ElementId,
                default,
                default,
                VisualPlacementMode.Manual));
        var command = UpdateNodeLabelVisualOverrideCommandHandlerTests.Command(
            DocumentRevision.Zero,
            Manual);
        var policy = new UpdateNodeLabelVisualOverrideHistoryPolicy();

        var mismatched = policy.Prepare(command, before, unexpected);
        var absent = policy.Prepare(command, before, missing);

        Assert.False(mismatched.Succeeded);
        Assert.Contains(mismatched.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.InvalidPreparation);
        Assert.False(absent.Succeeded);
        Assert.Contains(absent.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.InvalidPreparation);
    }

    private static Document CreateDocument(NodeLabelVisualOverride? visualOverride) =>
        Assert.IsType<Document>(DocumentFactory.Create(
            UpdateNodeLabelVisualOverrideCommandHandlerTests.Snapshot(
                DocumentRevision.Zero,
                visualOverride)).Document);

    private static void AssertOverride(
        Document document,
        NodeLabelVisualOverride? expected,
        bool isExplicit)
    {
        var snapshot = document.CaptureSnapshot();
        var visual = Assert.Single(snapshot.VisualModel.VisualStates);
        Assert.Equal(
            isExplicit,
            NodeLabelVisualOverride.TryRead(visual.Properties, out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal("preserved", visual.Properties["test:style"].TextValue);
    }

    private static void Install(
        HistoryStore store,
        HistoryMutationPreparationResult preparation)
    {
        Assert.True(preparation.Succeeded);
        Assert.True(store.TryInstallPrepared(preparation.Mutation!));
    }
}
