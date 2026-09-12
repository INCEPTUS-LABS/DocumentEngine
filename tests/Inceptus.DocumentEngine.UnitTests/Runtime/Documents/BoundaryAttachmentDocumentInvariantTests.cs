using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Runtime.Documents;

public sealed class BoundaryAttachmentDocumentInvariantTests
{
    private static readonly DocumentId DocumentId = new("test:attachment-document");
    private static readonly DocumentRevision Revision = new(9);
    private static readonly SemanticTypeId TypeId = new("test:node");
    private static readonly SemanticElementId OwnerId = new("test:owner");
    private static readonly SemanticElementId AttachedId = new("test:attached");
    private static readonly VisualStateId OwnerVisualId = new("test:owner-visual");
    private static readonly VisualStateId AttachedVisualId = new("test:attached-visual");

    [Fact]
    public void StructuralAttachmentRequiresAnExistingSameScopeOwnerWithoutCycles()
    {
        var missing = Snapshot(
            elements:
            [
                new SemanticElementSnapshot(
                    AttachedId,
                    TypeId,
                    attachedToElementId: new SemanticElementId("test:missing")),
            ]);
        var cycle = Snapshot(
            elements:
            [
                new SemanticElementSnapshot(OwnerId, TypeId, attachedToElementId: AttachedId),
                new SemanticElementSnapshot(AttachedId, TypeId, attachedToElementId: OwnerId),
            ]);
        var childScope = new DocumentScopeSnapshot(
            new DocumentScopeId("test:child"),
            new DocumentScopeId(DocumentId.Value));
        var crossScope = Snapshot(
            elements:
            [
                new SemanticElementSnapshot(OwnerId, TypeId),
                new SemanticElementSnapshot(AttachedId, TypeId, attachedToElementId: OwnerId),
            ],
            nestedScopes: [childScope],
            memberships:
            [
                new SemanticElementScopeMembershipSnapshot(AttachedId, childScope.Id),
            ]);

        Assert.Contains(
            DocumentReconstructor.Reconstruct(missing).Diagnostics,
            diagnostic => diagnostic.Code ==
                DocumentInvariantValidator.SemanticAttachmentReferenceMissingCode);
        Assert.Equal(
            [AttachedId.Value, OwnerId.Value],
            DocumentReconstructor.Reconstruct(cycle).Diagnostics
                .Where(diagnostic => diagnostic.Code ==
                    DocumentInvariantValidator.SemanticAttachmentCycleCode)
                .Select(static diagnostic => diagnostic.SourceIdentity)
                .Order(StringComparer.Ordinal));
        Assert.Contains(
            DocumentReconstructor.Reconstruct(crossScope).Diagnostics,
            diagnostic => diagnostic.Code ==
                DocumentInvariantValidator.SemanticAttachmentScopeMismatchCode);
    }

    [Fact]
    public void VisualAttachmentRequiresStructuralAuthorityAndSynchronizedFallbackBounds()
    {
        var placement = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Bottom,
            0.5d);
        var withoutStructuralOwner = Snapshot(
            elements: [new SemanticElementSnapshot(AttachedId, TypeId)],
            visualStates:
            [
                Visual(
                    AttachedVisualId,
                    AttachedId,
                    new PointD(132d, 142d),
                    new SizeD(36d, 36d),
                    placement),
            ]);
        var withoutVisualPlacement = Snapshot(
            elements:
            [
                new SemanticElementSnapshot(OwnerId, TypeId),
                new SemanticElementSnapshot(AttachedId, TypeId, attachedToElementId: OwnerId),
            ],
            visualStates:
            [
                Visual(OwnerVisualId, OwnerId, new PointD(100d, 100d), new SizeD(100d, 60d)),
                Visual(AttachedVisualId, AttachedId, new PointD(132d, 142d), new SizeD(36d, 36d)),
            ]);
        var staleFallback = ValidSnapshot(
            attachedPosition: new PointD(133d, 142d),
            ownerPlacementMode: VisualPlacementMode.Pinned);

        AssertBoundaryAttachmentRejected(withoutStructuralOwner);
        AssertBoundaryAttachmentRejected(withoutVisualPlacement);
        AssertBoundaryAttachmentRejected(staleFallback);
    }

    [Fact]
    public void ValidAttachmentReconstructsAndPreservesExactStructuralAndVisualState()
    {
        var snapshot = ValidSnapshot(new PointD(132d, 142d));

        var result = DocumentReconstructor.Reconstruct(snapshot);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        var captured = Assert.IsType<Document>(result.Document).CaptureSnapshot();
        Assert.Equal(snapshot, captured);
        Assert.Equal(
            OwnerId,
            captured.SemanticModel.Elements
                .Single(element => element.Id == AttachedId)
                .AttachedToElementId);
        Assert.Equal(
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.5d),
            captured.VisualModel.VisualStates
                .Single(visual => visual.Id == AttachedVisualId)
                .BoundaryAttachment);
    }

    [Theory]
    [InlineData(VisualPlacementMode.Automatic)]
    [InlineData(VisualPlacementMode.Manual)]
    public void NonPinnedOwnerAllowsLegalFallbackDerivedFromEffectiveLayout(
        VisualPlacementMode ownerPlacementMode)
    {
        var snapshot = ValidSnapshot(
            new PointD(482d, 222d),
            ownerPlacementMode);

        var result = DocumentReconstructor.Reconstruct(snapshot);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(snapshot, Assert.IsType<Document>(result.Document).CaptureSnapshot());
    }

    private static void AssertBoundaryAttachmentRejected(DocumentSnapshot snapshot) =>
        Assert.Contains(
            DocumentReconstructor.Reconstruct(snapshot).Diagnostics,
            diagnostic => diagnostic.Code ==
                DocumentInvariantValidator.VisualBoundaryAttachmentInvalidCode);

    private static DocumentSnapshot ValidSnapshot(
        PointD attachedPosition,
        VisualPlacementMode ownerPlacementMode = VisualPlacementMode.Manual) =>
        Snapshot(
            elements:
            [
                new SemanticElementSnapshot(OwnerId, TypeId),
                new SemanticElementSnapshot(AttachedId, TypeId, attachedToElementId: OwnerId),
            ],
            visualStates:
            [
                Visual(
                    OwnerVisualId,
                    OwnerId,
                    new PointD(100d, 100d),
                    new SizeD(100d, 60d),
                    placementMode: ownerPlacementMode),
                Visual(
                    AttachedVisualId,
                    AttachedId,
                    attachedPosition,
                    new SizeD(36d, 36d),
                    new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.5d)),
            ]);

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot>? elements = null,
        IEnumerable<VisualStateSnapshot>? visualStates = null,
        IEnumerable<DocumentScopeSnapshot>? nestedScopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                Revision,
                elements,
                nestedScopes: nestedScopes,
                scopeMemberships: memberships),
            new VisualModelSnapshot(DocumentId, Revision, visualStates),
            new DocumentMetadataSnapshot(DocumentId, Revision));

    private static VisualStateSnapshot Visual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        PointD position,
        SizeD size,
        BoundaryAttachmentPlacement? attachment = null,
        VisualPlacementMode placementMode = VisualPlacementMode.Manual) =>
        new(
            visualStateId,
            semanticElementId,
            position,
            size,
            placementMode,
            boundaryAttachment: attachment);
}
