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

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class BoundaryAttachmentCommandTests
{
    private static readonly DocumentId DocumentId = new("test:attachment-commands");
    private static readonly SemanticTypeId TypeId = new("test:node");
    private static readonly SemanticElementId OwnerId = new("test:owner");
    private static readonly SemanticElementId AttachedId = new("test:attached");
    private static readonly SemanticElementId UnrelatedId = new("test:unrelated");
    private static readonly VisualStateId OwnerVisualId = new("test:owner-visual");
    private static readonly VisualStateId AttachedVisualId = new("test:attached-visual");
    private static readonly VisualStateId UnrelatedVisualId = new("test:unrelated-visual");

    [Fact]
    public async Task OwnerMoveSynchronizesDerivedFallbackAndReportsDependentImpact()
    {
        var snapshot = Snapshot();
        var result = await new MoveVisualStateCommandHandler().HandleAsync(
            new MoveVisualStateCommand(
                DocumentId,
                snapshot.Revision,
                OwnerVisualId,
                new PointD(200d, 220d),
                VisualPlacementMode.Pinned),
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        AssertVisual(proposed, OwnerVisualId, new PointD(200d, 220d), new SizeD(100d, 60d));
        AssertVisual(proposed, AttachedVisualId, new PointD(232d, 262d), new SizeD(36d, 36d));
        AssertVisual(proposed, UnrelatedVisualId, new PointD(400d, 100d), new SizeD(80d, 40d));
        Assert.Equal(
            [AttachedVisualId, OwnerVisualId],
            Assert.IsType<NodeGeometryPipelineImpact>(result.NodeGeometryImpact)
                .ChangedVisualStateIds
                .ToArray());
    }

    [Fact]
    public async Task AtomicMultiMoveSynchronizesDependentsAndPreservesUnrelatedTargets()
    {
        var snapshot = Snapshot();
        var result = await new MoveVisualStatesCommandHandler().HandleAsync(
            new MoveVisualStatesCommand(
                DocumentId,
                snapshot.Revision,
                [
                    new VisualStateMove(OwnerVisualId, new PointD(150d, 160d)),
                    new VisualStateMove(UnrelatedVisualId, new PointD(500d, 300d)),
                ]),
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        AssertVisual(proposed, OwnerVisualId, new PointD(150d, 160d), new SizeD(100d, 60d));
        AssertVisual(proposed, AttachedVisualId, new PointD(182d, 202d), new SizeD(36d, 36d));
        AssertVisual(proposed, UnrelatedVisualId, new PointD(500d, 300d), new SizeD(80d, 40d));
        Assert.Equal(
            [AttachedVisualId, OwnerVisualId, UnrelatedVisualId],
            Assert.IsType<NodeGeometryPipelineImpact>(result.NodeGeometryImpact)
                .ChangedVisualStateIds
                .ToArray());
    }

    [Fact]
    public async Task OwnerResizeRecomputesAttachmentFromUnchangedSideAndPosition()
    {
        var snapshot = Snapshot();
        var result = await new ResizeVisualStateCommandHandler().HandleAsync(
            new ResizeVisualStateCommand(
                DocumentId,
                snapshot.Revision,
                OwnerVisualId,
                new RectD(100d, 100d, 200d, 80d),
                VisualPlacementMode.Pinned),
            snapshot,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        var attached = AssertVisual(
            proposed,
            AttachedVisualId,
            new PointD(182d, 162d),
            new SizeD(36d, 36d));
        Assert.Equal(
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.5d),
            attached.BoundaryAttachment);
    }

    [Fact]
    public async Task FreeMoveAndResizeCannotCreateIndependentAttachedGeometryAuthority()
    {
        var snapshot = Snapshot();
        var moved = await new MoveVisualStateCommandHandler().HandleAsync(
            new MoveVisualStateCommand(
                DocumentId,
                snapshot.Revision,
                AttachedVisualId,
                new PointD(300d, 300d)),
            snapshot,
            CancellationToken.None);
        var resized = await new ResizeVisualStateCommandHandler().HandleAsync(
            new ResizeVisualStateCommand(
                DocumentId,
                snapshot.Revision,
                AttachedVisualId,
                new RectD(132d, 142d, 50d, 50d)),
            snapshot,
            CancellationToken.None);

        Assert.False(moved.Succeeded);
        Assert.False(resized.Succeeded);
        Assert.All(
            moved.Diagnostics.Concat(resized.Diagnostics),
            diagnostic => Assert.Equal(
                CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportPosition,
                diagnostic.Code));
    }

    [Fact]
    public async Task DependentGeometryCrossingTheDocumentBoundaryRejectsOwnerMoveAtomically()
    {
        var snapshot = Snapshot(
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Left, 0.5d));

        var result = await new MoveVisualStateCommandHandler().HandleAsync(
            new MoveVisualStateCommand(
                DocumentId,
                snapshot.Revision,
                OwnerVisualId,
                new PointD(10d, 100d)),
            snapshot,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid);
    }

    [Fact]
    public async Task BoundaryAttachmentCommandCommitsOneDerivedEditAndUndoRedoRestoresExactState()
    {
        var document = Assert.IsType<Document>(DocumentFactory.Create(Snapshot()).Document);
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();
        var target = new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Right, 0.25d);

        var update = await history.ExecuteAsync(
            processor,
            new UpdateBoundaryAttachmentCommand(
                DocumentId,
                document.Revision,
                AttachedVisualId,
                target,
                new RectD(100d, 100d, 100d, 60d)));

        Assert.True(update.IsCommitted);
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());
        AssertAttachment(document, target, new PointD(182d, 97d));

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        AssertAttachment(
            document,
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.5d),
            new PointD(132d, 142d));

        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        AssertAttachment(document, target, new PointD(182d, 97d));
        Assert.Equal(new HistoryStatus(1, true, false), history.CaptureStatus());
    }

    [Fact]
    public async Task AutomaticOwnerHistoryRestoresExactDerivedFallbackWithoutPublicOverride()
    {
        var document = Assert.IsType<Document>(DocumentFactory.Create(Snapshot(
            ownerPlacementMode: VisualPlacementMode.Automatic)).Document);
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();
        var before = document.CaptureSnapshot();
        var effectiveOwnerBounds = new RectD(280d, 240d, 180d, 90d);
        var target = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Right,
            0.25d);

        var update = await history.ExecuteAsync(
            processor,
            new UpdateBoundaryAttachmentCommand(
                DocumentId,
                document.Revision,
                AttachedVisualId,
                target,
                effectiveOwnerBounds));

        Assert.True(update.IsCommitted);
        var committed = document.CaptureSnapshot();
        AssertAttachment(
            document,
            target,
            target.ResolveBounds(effectiveOwnerBounds, new SizeD(36d, 36d)).TopLeft);

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Equal(
            before.VisualModel.VisualStates.AsEnumerable(),
            document.CaptureSnapshot().VisualModel.VisualStates.AsEnumerable());

        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.Equal(
            committed.VisualModel.VisualStates.AsEnumerable(),
            document.CaptureSnapshot().VisualModel.VisualStates.AsEnumerable());

        Assert.Null(typeof(UpdateBoundaryAttachmentCommand).GetProperty(
            "TargetFallbackBounds"));
    }

    [Fact]
    public async Task BoundaryAttachmentValidatorRejectsNoOpWithoutRevisionOrHistory()
    {
        var document = Assert.IsType<Document>(DocumentFactory.Create(Snapshot()).Document);
        var history = new HistoryManager(document);
        var processor = new CommandProcessor();

        var result = await history.ExecuteAsync(
            processor,
            new UpdateBoundaryAttachmentCommand(
                DocumentId,
                document.Revision,
                AttachedVisualId,
                new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.5d),
                new RectD(100d, 100d, 100d, 60d)));

        Assert.False(result.IsCommitted);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Equal(0, history.CaptureStatus().EntryCount);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.BoundaryAttachmentUnchanged);
    }

    private static void AssertAttachment(
        Document document,
        BoundaryAttachmentPlacement expected,
        PointD expectedPosition)
    {
        var visual = document.CaptureSnapshot().VisualModel.VisualStates
            .Single(candidate => candidate.Id == AttachedVisualId);
        Assert.Equal(expected, visual.BoundaryAttachment);
        Assert.Equal(expectedPosition, visual.Position);
        Assert.Equal(new SizeD(36d, 36d), visual.Size);
    }

    private static VisualStateSnapshot AssertVisual(
        DocumentSnapshot snapshot,
        VisualStateId id,
        PointD position,
        SizeD size)
    {
        var visual = snapshot.VisualModel.VisualStates.Single(candidate => candidate.Id == id);
        Assert.Equal(position, visual.Position);
        Assert.Equal(size, visual.Size);
        return visual;
    }

    private static DocumentSnapshot Snapshot(
        BoundaryAttachmentPlacement? placement = null,
        VisualPlacementMode ownerPlacementMode = VisualPlacementMode.Manual)
    {
        placement ??= new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.5d);
        var ownerBounds = new RectD(100d, 100d, 100d, 60d);
        var attachedSize = new SizeD(36d, 36d);
        var attachedBounds = placement.ResolveBounds(ownerBounds, attachedSize);
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new SemanticElementSnapshot(OwnerId, TypeId),
                    new SemanticElementSnapshot(
                        AttachedId,
                        TypeId,
                        attachedToElementId: OwnerId),
                    new SemanticElementSnapshot(UnrelatedId, TypeId),
                ]),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new VisualStateSnapshot(
                        OwnerVisualId,
                        OwnerId,
                        ownerBounds.TopLeft,
                        ownerBounds.Size,
                        ownerPlacementMode),
                    new VisualStateSnapshot(
                        AttachedVisualId,
                        AttachedId,
                        attachedBounds.TopLeft,
                        attachedSize,
                        VisualPlacementMode.Manual,
                        boundaryAttachment: placement),
                    new VisualStateSnapshot(
                        UnrelatedVisualId,
                        UnrelatedId,
                        new PointD(400d, 100d),
                        new SizeD(80d, 40d),
                        VisualPlacementMode.Manual),
                ]),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
    }
}
