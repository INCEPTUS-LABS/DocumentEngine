using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.UnitTests.Commands;

namespace Inceptus.DocumentEngine.UnitTests.History;

public sealed class MoveLabelHistoryTests
{
    private static readonly ConnectorLabelPlacement Moved =
        new(0.85d, new(4d, -6d));

    [Fact]
    public void PolicyBuildsExactDefaultRemovalAndRedoPlacement()
    {
        var before = MoveLabelCommandHandlerTests.Snapshot(DocumentRevision.Zero);
        var committed = MoveLabelCommandHandlerTests.Snapshot(
            new DocumentRevision(1),
            Moved);
        var store = new HistoryStore();
        var coordinator = new HistoryCoordinator([MoveLabelHistoryPolicy.Registration]);
        var command = MoveLabelCommandHandlerTests.Command(DocumentRevision.Zero, Moved);

        Install(store, coordinator.PrepareRecord(store, command, before, committed));

        var undo = store.PrepareUndo(before.DocumentId, new DocumentRevision(1));
        var inverse = Assert.IsType<MoveLabelCommand>(undo.Mutation!.RestorationCommand);
        Assert.Null(inverse.TargetPlacement);
        Assert.Equal(new DocumentRevision(1), inverse.ExpectedRevision);
        Install(store, undo);

        var redo = store.PrepareRedo(before.DocumentId, new DocumentRevision(2));
        var replay = Assert.IsType<MoveLabelCommand>(redo.Mutation!.RestorationCommand);
        Assert.Equal(Moved, replay.TargetPlacement);
        Assert.Equal(new DocumentRevision(2), replay.ExpectedRevision);
    }

    [Fact]
    public async Task BuiltInHistoryMovesOneLabelAndUndoRedoRestoreExactPlacement()
    {
        var document = Assert.IsType<Document>(DocumentFactory.Create(
            MoveLabelCommandHandlerTests.Snapshot(DocumentRevision.Zero)).Document);
        var original = document.CaptureSnapshot();
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();

        var moved = await history.ExecuteAsync(
            processor,
            MoveLabelCommandHandlerTests.Command(document.Revision, Moved));

        Assert.True(moved.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        AssertPlacement(Moved, isExplicit: true);
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        AssertPlacement(ConnectorLabelPlacement.Default, isExplicit: false);
        Assert.Equal(new HistoryStatus(1, false, true), history.CaptureStatus());

        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.Equal(new DocumentRevision(3), document.Revision);
        AssertPlacement(Moved, isExplicit: true);
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());

        void AssertPlacement(ConnectorLabelPlacement expected, bool isExplicit)
        {
            var snapshot = document.CaptureSnapshot();
            var visual = Assert.Single(snapshot.VisualModel.VisualStates);
            Assert.Equal(expected, ConnectorLabelPlacement.Resolve(visual.Properties));
            Assert.Equal(
                isExplicit,
                ConnectorLabelPlacement.TryRead(visual.Properties, out _));
            Assert.Equal(
                original.SemanticModel.Relationships.AsEnumerable(),
                snapshot.SemanticModel.Relationships.AsEnumerable());
            Assert.Equal(
                original.SemanticModel.Elements.AsEnumerable(),
                snapshot.SemanticModel.Elements.AsEnumerable());
            Assert.Equal(
                original.VisualModel.VisualStates[0].Route.AsEnumerable(),
                visual.Route.AsEnumerable());
        }
    }

    [Fact]
    public void PolicyRejectsMissingOrMismatchedCommittedPlacement()
    {
        var before = MoveLabelCommandHandlerTests.Snapshot(DocumentRevision.Zero);
        var unexpected = MoveLabelCommandHandlerTests.Snapshot(
            new DocumentRevision(1),
            new ConnectorLabelPlacement(0.2d, default));
        var missing = MoveLabelCommandHandlerTests.Snapshot(
            new DocumentRevision(1),
            visualOverride: new VisualStateSnapshot(
                new VisualStateId("test:other"),
                MoveLabelCommandHandlerTests.RelationshipId,
                default,
                default,
                VisualPlacementMode.Manual));
        var command = MoveLabelCommandHandlerTests.Command(DocumentRevision.Zero, Moved);
        var policy = new MoveLabelHistoryPolicy();

        var mismatched = policy.Prepare(command, before, unexpected);
        var absent = policy.Prepare(command, before, missing);

        Assert.False(mismatched.Succeeded);
        Assert.Contains(mismatched.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.InvalidPreparation);
        Assert.False(absent.Succeeded);
        Assert.Contains(absent.Diagnostics, diagnostic =>
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
