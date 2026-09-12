using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.History;

public sealed class HistoryCoordinatorTests
{
    private static readonly DocumentId DocumentId = new("test:history-policy-document");
    private static readonly SemanticElementId ElementId = new("test:history-element");
    private static readonly VisualStateId VisualId = new("test:history-visual");

    [Fact]
    public void MissingPolicyFailsAndExplicitNotUndoablePolicyRecordsNothing()
    {
        var before = Snapshot(DocumentRevision.Zero, new PointD(1d, 2d));
        var committed = Snapshot(new DocumentRevision(1), new PointD(3d, 4d));
        var command = Move(before.Revision, new PointD(3d, 4d));
        var store = new HistoryStore();

        var missing = new HistoryCoordinator([])
            .PrepareRecord(store, command, before, committed);
        var excluded = new HistoryCoordinator(
            [
                new CommandHistoryPolicyRegistration(
                    MoveVisualStateCommand.KnownTypeId,
                    new NotUndoablePolicy()),
            ])
            .PrepareRecord(store, command, before, committed);

        Assert.False(missing.Succeeded);
        Assert.Contains(missing.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.MissingPolicy);
        Assert.True(excluded.Succeeded);
        Assert.Null(excluded.Mutation);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void MovePolicyBuildsInverseAndRedoCommandsFromMinimalVisualData()
    {
        var before = Snapshot(
            DocumentRevision.Zero,
            new PointD(10d, 20d),
            VisualPlacementMode.Pinned);
        var committed = Snapshot(
            new DocumentRevision(1),
            new PointD(40d, 50d),
            VisualPlacementMode.Manual);
        var store = new HistoryStore();
        var coordinator = new HistoryCoordinator([MoveVisualStateHistoryPolicy.Registration]);

        var record = coordinator.PrepareRecord(
            store,
            Move(before.Revision, committed.VisualModel.VisualStates[0].Position),
            before,
            committed);
        Install(store, record);

        var undo = store.PrepareUndo(DocumentId, new DocumentRevision(1));
        var inverse = Assert.IsType<MoveVisualStateCommand>(undo.Mutation!.RestorationCommand);
        Assert.Equal(new PointD(10d, 20d), inverse.TargetPosition);
        Assert.Equal(VisualPlacementMode.Pinned, inverse.RequestedPlacementMode);
        Assert.Equal(new DocumentRevision(1), inverse.ExpectedRevision);
        Install(store, undo);

        var redo = store.PrepareRedo(DocumentId, new DocumentRevision(2));
        var replay = Assert.IsType<MoveVisualStateCommand>(redo.Mutation!.RestorationCommand);
        Assert.Equal(new PointD(40d, 50d), replay.TargetPosition);
        Assert.Equal(VisualPlacementMode.Manual, replay.RequestedPlacementMode);
        Assert.Equal(new DocumentRevision(2), replay.ExpectedRevision);
    }

    [Fact]
    public void DuplicatePolicyRegistrationFailsDeterministically()
    {
        var registration = MoveVisualStateHistoryPolicy.Registration;

        var exception = Assert.Throws<ArgumentException>(() =>
            new HistoryCoordinator([registration, registration]));

        Assert.Contains(
            HistoryDiagnosticCodes.DuplicatePolicyRegistration,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CommandsContainNeitherHistoryNorDocumentSnapshots()
    {
        var fields = typeof(MoveVisualStateCommand).GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);

        Assert.DoesNotContain(fields, field =>
            field.FieldType == typeof(DocumentSnapshot) ||
            field.FieldType.Namespace?.Contains("History", StringComparison.Ordinal) == true);
    }

    private static MoveVisualStateCommand Move(
        DocumentRevision revision,
        PointD position) =>
        new(DocumentId, revision, VisualId, position, VisualPlacementMode.Manual);

    private static DocumentSnapshot Snapshot(
        DocumentRevision revision,
        PointD position,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                [new SemanticElementSnapshot(ElementId, new SemanticTypeId("test:type"))]),
            new VisualModelSnapshot(
                DocumentId,
                revision,
                [
                    new VisualStateSnapshot(
                        VisualId,
                        ElementId,
                        position,
                        new SizeD(30d, 20d),
                        placementMode),
                ]),
            new DocumentMetadataSnapshot(DocumentId, revision));

    private static void Install(
        HistoryStore store,
        HistoryMutationPreparationResult preparation)
    {
        Assert.True(preparation.Succeeded);
        Assert.True(store.TryInstallPrepared(preparation.Mutation!));
    }

    private sealed class NotUndoablePolicy : ICommandHistoryPolicy
    {
        public CommandHistoryPreparationResult Prepare(
            ICommand command,
            DocumentSnapshot before,
            DocumentSnapshot committed) =>
            CommandHistoryPreparationResult.NotUndoable(
            [
                new Diagnostic(
                    "test:not-undoable",
                    DiagnosticSeverity.Information,
                    "The test policy explicitly excludes this Command."),
            ]);
    }
}
