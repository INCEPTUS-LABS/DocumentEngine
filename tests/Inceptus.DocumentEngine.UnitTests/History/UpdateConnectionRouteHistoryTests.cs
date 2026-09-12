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

public sealed class UpdateConnectionRouteHistoryTests
{
    private static readonly DocumentId DocumentId = new("test:route-history");
    private static readonly SemanticElementId SourceId = new("test:source");
    private static readonly SemanticElementId TargetId = new("test:target");
    private static readonly SemanticElementId RelationshipId = new("test:relationship");
    private static readonly VisualStateId VisualId = new("test:relationship-visual");
    private static readonly PointD[] OriginalRoute =
        [new(10d, 20d), new(30d, 40d), new(80d, 90d)];
    private static readonly PointD[] UpdatedRoute =
        [new(10d, 20d), new(45d, 55d), new(60d, 70d), new(80d, 90d)];

    [Fact]
    public void PolicyBuildsExactFullInverseAndRedoRoutes()
    {
        var before = Snapshot(DocumentRevision.Zero, OriginalRoute);
        var committed = Snapshot(new DocumentRevision(1), UpdatedRoute);
        var store = new HistoryStore();
        var coordinator = new HistoryCoordinator(
            [UpdateConnectionRouteHistoryPolicy.Registration]);
        var command = Update(DocumentRevision.Zero, UpdatedRoute);

        Install(store, coordinator.PrepareRecord(store, command, before, committed));

        var undo = store.PrepareUndo(DocumentId, new DocumentRevision(1));
        var inverse = Assert.IsType<UpdateConnectionRouteCommand>(
            undo.Mutation!.RestorationCommand);
        Assert.Equal(OriginalRoute, inverse.TargetRoute.AsEnumerable());
        Assert.Equal(new DocumentRevision(1), inverse.ExpectedRevision);
        Install(store, undo);

        var redo = store.PrepareRedo(DocumentId, new DocumentRevision(2));
        var replay = Assert.IsType<UpdateConnectionRouteCommand>(
            redo.Mutation!.RestorationCommand);
        Assert.Equal(UpdatedRoute, replay.TargetRoute.AsEnumerable());
        Assert.Equal(new DocumentRevision(2), replay.ExpectedRevision);
    }

    [Fact]
    public async Task BuiltInHistoryRecordsOneRouteEditAndUndoRedoRestoreExactRoutes()
    {
        var document = CreateDocument();
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();

        var update = await history.ExecuteAsync(
            processor,
            Update(document.Revision, UpdatedRoute));
        Assert.True(update.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        AssertRoute(document, UpdatedRoute);
        Assert.Equal(
            new HistoryStatus(1, canUndo: true, canRedo: false),
            history.CaptureStatus());

        var undo = await history.UndoAsync(processor);
        Assert.True(undo.IsCommitted);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        AssertRoute(document, OriginalRoute);
        Assert.Equal(
            new HistoryStatus(1, canUndo: false, canRedo: true),
            history.CaptureStatus());

        var redo = await history.RedoAsync(processor);
        Assert.True(redo.IsCommitted);
        Assert.Equal(new DocumentRevision(3), document.Revision);
        AssertRoute(document, UpdatedRoute);
        Assert.Equal(
            new HistoryStatus(1, canUndo: true, canRedo: false),
            history.CaptureStatus());
    }

    [Fact]
    public async Task FirstPersistentRouteIsOneHistoryEditAndUndoRedoRestoresAutomaticRoutingState()
    {
        var document = Assert.IsType<Document>(
            DocumentFactory.Create(Snapshot(DocumentRevision.Zero, [])).Document);
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();

        var insert = await history.ExecuteAsync(
            processor,
            Update(document.Revision, UpdatedRoute));

        Assert.True(insert.IsCommitted);
        AssertRoute(document, UpdatedRoute);
        Assert.Equal(1, history.CaptureStatus().EntryCount);

        var undo = await history.UndoAsync(processor);

        Assert.True(undo.IsCommitted);
        AssertRoute(document, []);
        Assert.Equal(1, history.CaptureStatus().EntryCount);

        var redo = await history.RedoAsync(processor);

        Assert.True(redo.IsCommitted);
        AssertRoute(document, UpdatedRoute);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task ClearingPersistentRouteIsOneHistoryEditAndUndoRedoRestoresExactRoute()
    {
        var document = CreateDocument();
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();

        var clear = await history.ExecuteAsync(
            processor,
            Update(document.Revision, []));

        Assert.True(clear.IsCommitted);
        AssertRoute(document, []);
        Assert.Equal(1, history.CaptureStatus().EntryCount);

        var undo = await history.UndoAsync(processor);

        Assert.True(undo.IsCommitted);
        AssertRoute(document, OriginalRoute);
        Assert.Equal(1, history.CaptureStatus().EntryCount);

        var redo = await history.RedoAsync(processor);

        Assert.True(redo.IsCommitted);
        AssertRoute(document, []);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
    }

    [Fact]
    public void PolicyAcceptsAutomaticRoutingStateButRejectsMissingOrMalformedPriorRoute()
    {
        var policy = new UpdateConnectionRouteHistoryPolicy();
        var missingTarget = new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new SemanticElementSnapshot(SourceId, new SemanticTypeId("test:type")),
                    new SemanticElementSnapshot(TargetId, new SemanticTypeId("test:type")),
                ],
                [
                    new SemanticRelationshipSnapshot(
                        RelationshipId,
                        new SemanticTypeId("test:relationship-type"),
                        SourceId,
                        TargetId),
                ]),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
        var automatic = Snapshot(DocumentRevision.Zero, []);
        var nonRestorable = Snapshot(DocumentRevision.Zero, [new PointD(1d, 2d)]);
        var committed = Snapshot(new DocumentRevision(1), UpdatedRoute);

        var missing = policy.Prepare(
            Update(DocumentRevision.Zero, UpdatedRoute),
            missingTarget,
            committed);
        var automaticRoute = policy.Prepare(
            Update(DocumentRevision.Zero, UpdatedRoute),
            automatic,
            committed);
        var invalidRoute = policy.Prepare(
            Update(DocumentRevision.Zero, UpdatedRoute),
            nonRestorable,
            committed);

        Assert.False(missing.Succeeded);
        Assert.Contains(missing.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.InvalidPreparation);
        Assert.True(automaticRoute.Succeeded);
        Assert.False(invalidRoute.Succeeded);
        Assert.Contains(invalidRoute.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.InvalidPreparation);
    }

    private static UpdateConnectionRouteCommand Update(
        DocumentRevision revision,
        IEnumerable<PointD> route) =>
        new(DocumentId, revision, VisualId, route);

    private static DocumentSnapshot Snapshot(
        DocumentRevision revision,
        IEnumerable<PointD> route) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                [
                    new SemanticElementSnapshot(SourceId, new SemanticTypeId("test:type")),
                    new SemanticElementSnapshot(TargetId, new SemanticTypeId("test:type")),
                ],
                [
                    new SemanticRelationshipSnapshot(
                        RelationshipId,
                        new SemanticTypeId("test:relationship-type"),
                        SourceId,
                        TargetId),
                ]),
            new VisualModelSnapshot(
                DocumentId,
                revision,
                [
                    new VisualStateSnapshot(
                        VisualId,
                        RelationshipId,
                        new PointD(5d, 6d),
                        new SizeD(7d, 8d),
                        VisualPlacementMode.Manual,
                        route),
                ]),
            new DocumentMetadataSnapshot(DocumentId, revision));

    private static Document CreateDocument() =>
        Assert.IsType<Document>(
            DocumentFactory.Create(Snapshot(DocumentRevision.Zero, OriginalRoute)).Document);

    private static void AssertRoute(Document document, IEnumerable<PointD> expectedRoute)
    {
        var visual = Assert.Single(document.VisualModel.VisualStates);
        Assert.Equal(expectedRoute, visual.Route.AsEnumerable());
        Assert.Equal(RelationshipId, visual.SemanticElementId);
        Assert.Equal(new PointD(5d, 6d), visual.Position);
        Assert.Equal(new SizeD(7d, 8d), visual.Size);
        Assert.Equal(VisualPlacementMode.Manual, visual.PlacementMode);
    }

    private static void Install(
        HistoryStore store,
        HistoryMutationPreparationResult preparation)
    {
        Assert.True(preparation.Succeeded);
        Assert.True(store.TryInstallPrepared(preparation.Mutation!));
    }
}
