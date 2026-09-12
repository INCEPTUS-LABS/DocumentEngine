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

public sealed class ResizeVisualStateHistoryTests
{
    private static readonly DocumentId DocumentId = new("test:resize-history");
    private static readonly SemanticElementId ElementId = new("test:element");
    private static readonly VisualStateId VisualId = new("test:visual");
    private static readonly RectD OriginalBounds = new(10d, 20d, 30d, 40d);
    private static readonly RectD ResizedBounds = new(70d, 80d, 110d, 65d);

    [Fact]
    public void PolicyBuildsExactInverseAndRedoBoundsWithPlacementIntent()
    {
        var before = Snapshot(
            DocumentRevision.Zero,
            OriginalBounds,
            VisualPlacementMode.Manual);
        var committed = Snapshot(
            new DocumentRevision(1),
            ResizedBounds,
            VisualPlacementMode.Pinned);
        var store = new HistoryStore();
        var coordinator = new HistoryCoordinator(
            [ResizeVisualStateHistoryPolicy.Registration]);
        var command = Resize(
            before.Revision,
            ResizedBounds,
            VisualPlacementMode.Pinned);

        Install(store, coordinator.PrepareRecord(store, command, before, committed));

        var undo = store.PrepareUndo(DocumentId, new DocumentRevision(1));
        var inverse = Assert.IsType<ResizeVisualStateCommand>(
            undo.Mutation!.RestorationCommand);
        Assert.Equal(OriginalBounds, inverse.TargetBounds);
        Assert.Equal(VisualPlacementMode.Manual, inverse.RequestedPlacementMode);
        Assert.Equal(new DocumentRevision(1), inverse.ExpectedRevision);
        Install(store, undo);

        var redo = store.PrepareRedo(DocumentId, new DocumentRevision(2));
        var replay = Assert.IsType<ResizeVisualStateCommand>(
            redo.Mutation!.RestorationCommand);
        Assert.Equal(ResizedBounds, replay.TargetBounds);
        Assert.Equal(VisualPlacementMode.Pinned, replay.RequestedPlacementMode);
        Assert.Equal(new DocumentRevision(2), replay.ExpectedRevision);
    }

    [Fact]
    public async Task BuiltInHistoryRecordsOneResizeAndUndoRedoRestoreExactState()
    {
        var document = CreateDocument();
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();

        var resize = await history.ExecuteAsync(
            processor,
            Resize(
                document.Revision,
                ResizedBounds,
                VisualPlacementMode.Pinned));
        Assert.True(resize.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        AssertVisual(document, ResizedBounds, VisualPlacementMode.Pinned);
        Assert.Equal(
            new HistoryStatus(1, canUndo: true, canRedo: false),
            history.CaptureStatus());

        var undo = await history.UndoAsync(processor);
        Assert.True(undo.IsCommitted);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        AssertVisual(document, OriginalBounds, VisualPlacementMode.Manual);
        Assert.Equal(
            new HistoryStatus(1, canUndo: false, canRedo: true),
            history.CaptureStatus());

        var redo = await history.RedoAsync(processor);
        Assert.True(redo.IsCommitted);
        Assert.Equal(new DocumentRevision(3), document.Revision);
        AssertVisual(document, ResizedBounds, VisualPlacementMode.Pinned);
        Assert.Equal(
            new HistoryStatus(1, canUndo: true, canRedo: false),
            history.CaptureStatus());
    }

    [Fact]
    public void PolicyRejectsPreparationWithoutTheTargetVisualState()
    {
        var before = Snapshot(DocumentRevision.Zero, OriginalBounds);
        var committed = new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                new DocumentRevision(1),
                [new SemanticElementSnapshot(ElementId, new SemanticTypeId("test:type"))]),
            new VisualModelSnapshot(DocumentId, new DocumentRevision(1)),
            new DocumentMetadataSnapshot(DocumentId, new DocumentRevision(1)));

        var result = new ResizeVisualStateHistoryPolicy().Prepare(
            Resize(DocumentRevision.Zero, ResizedBounds),
            before,
            committed);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.InvalidPreparation);
    }

    private static ResizeVisualStateCommand Resize(
        DocumentRevision revision,
        RectD bounds,
        VisualPlacementMode? placementMode = null) =>
        new(DocumentId, revision, VisualId, bounds, placementMode);

    private static DocumentSnapshot Snapshot(
        DocumentRevision revision,
        RectD bounds,
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
                        bounds.TopLeft,
                        bounds.Size,
                        placementMode,
                        [new PointD(1d, 2d), new PointD(3d, 4d)]),
                ]),
            new DocumentMetadataSnapshot(DocumentId, revision));

    private static Document CreateDocument() =>
        Assert.IsType<Document>(
            DocumentFactory.Create(Snapshot(DocumentRevision.Zero, OriginalBounds)).Document);

    private static void AssertVisual(
        Document document,
        RectD expectedBounds,
        VisualPlacementMode expectedPlacementMode)
    {
        var visual = Assert.Single(document.VisualModel.VisualStates);
        Assert.Equal(expectedBounds.TopLeft, visual.Position);
        Assert.Equal(expectedBounds.Size, visual.Size);
        Assert.Equal(expectedPlacementMode, visual.PlacementMode);
        Assert.Equal(
            new[] { new PointD(1d, 2d), new PointD(3d, 4d) },
            visual.Route.AsEnumerable());
    }

    private static void Install(
        HistoryStore store,
        HistoryMutationPreparationResult preparation)
    {
        Assert.True(preparation.Succeeded);
        Assert.True(store.TryInstallPrepared(preparation.Mutation!));
    }
}
