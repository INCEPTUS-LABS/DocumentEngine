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

public sealed class MoveVisualStatesHistoryTests
{
    private static readonly DocumentId DocumentId = new("test:atomic-move-history");
    private static readonly VisualStateId AlphaVisualId = new("test:visual:alpha");
    private static readonly VisualStateId BetaVisualId = new("test:visual:beta");
    private static readonly VisualStateId GammaVisualId = new("test:visual:gamma");

    [Fact]
    public async Task OneEntryUndoAndRedoRestoreEveryPositionAndPlacementMode()
    {
        var document = CreateDocument();
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();
        var original = State(document);

        var move = await history.ExecuteAsync(
            processor,
            new MoveVisualStatesCommand(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new VisualStateMove(
                        AlphaVisualId,
                        new PointD(70d, 80d),
                        VisualPlacementMode.Pinned),
                    new VisualStateMove(
                        BetaVisualId,
                        new PointD(180d, 100d),
                        VisualPlacementMode.Pinned),
                    new VisualStateMove(
                        GammaVisualId,
                        new PointD(290d, 130d),
                        VisualPlacementMode.Pinned),
                ]));
        var moved = State(document);

        Assert.True(move.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false), history.CaptureStatus());
        Assert.Equal(
            [new PointD(70d, 80d), new PointD(180d, 100d), new PointD(290d, 130d)],
            moved.Select(state => state.Position));
        Assert.All(moved, state => Assert.Equal(VisualPlacementMode.Pinned, state.PlacementMode));

        var undo = await history.UndoAsync(processor);
        var restored = State(document);

        Assert.True(undo.IsCommitted);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        Assert.Equal(
            original.Select(state => (state.Position, state.PlacementMode)),
            restored.Select(state => (state.Position, state.PlacementMode)));
        Assert.Equal(new HistoryStatus(1, canUndo: false, canRedo: true), history.CaptureStatus());

        var redo = await history.RedoAsync(processor);
        var redone = State(document);

        Assert.True(redo.IsCommitted);
        Assert.Equal(new DocumentRevision(3), document.Revision);
        Assert.Equal(
            moved.Select(state => (state.Position, state.PlacementMode)),
            redone.Select(state => (state.Position, state.PlacementMode)));
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false), history.CaptureStatus());
    }

    [Fact]
    public void PolicyRejectsACommittedSnapshotMissingAnyTarget()
    {
        var before = CreateDocument().CaptureSnapshot();
        var command = new MoveVisualStatesCommand(
            DocumentId,
            DocumentRevision.Zero,
            [
                new VisualStateMove(AlphaVisualId, new PointD(70d, 80d)),
                new VisualStateMove(BetaVisualId, new PointD(180d, 100d)),
            ]);
        var committed = new DocumentSnapshot(
            before.SemanticModel,
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                before.VisualModel.VisualStates.Where(state => state.Id != BetaVisualId)),
            before.Metadata);

        var preparation = new MoveVisualStatesHistoryPolicy().Prepare(
            command,
            before,
            committed);

        Assert.False(preparation.Succeeded);
        Assert.Contains(preparation.Diagnostics, diagnostic =>
            diagnostic.Code == HistoryDiagnosticCodes.InvalidPreparation);
    }

    private static Document CreateDocument()
    {
        var elementIds = new[]
        {
            new SemanticElementId("test:element:alpha"),
            new SemanticElementId("test:element:beta"),
            new SemanticElementId("test:element:gamma"),
        };
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                elementIds.Select(id => new SemanticElementSnapshot(
                    id,
                    new SemanticTypeId("test:type")))),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new VisualStateSnapshot(
                        AlphaVisualId,
                        elementIds[0],
                        new PointD(10d, 20d),
                        new SizeD(50d, 30d),
                        VisualPlacementMode.Automatic),
                    new VisualStateSnapshot(
                        BetaVisualId,
                        elementIds[1],
                        new PointD(120d, 40d),
                        new SizeD(60d, 35d),
                        VisualPlacementMode.Manual),
                    new VisualStateSnapshot(
                        GammaVisualId,
                        elementIds[2],
                        new PointD(230d, 70d),
                        new SizeD(70d, 40d),
                        VisualPlacementMode.Pinned),
                ]),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
        return Assert.IsType<Document>(DocumentFactory.Create(snapshot).Document);
    }

    private static VisualStateSnapshot[] State(Document document) =>
        document.VisualModel.VisualStates
            .Where(state =>
                state.Id == AlphaVisualId ||
                state.Id == BetaVisualId ||
                state.Id == GammaVisualId)
            .OrderBy(state => state.Id.Value, StringComparer.Ordinal)
            .ToArray();
}
