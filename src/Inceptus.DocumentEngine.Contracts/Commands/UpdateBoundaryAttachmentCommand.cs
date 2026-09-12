using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Replaces one attached visual's authoritative placement on its existing owner boundary.
/// The structural attachment owner is intentionally not changed by this Command.
/// </summary>
public sealed class UpdateBoundaryAttachmentCommand :
    IEquatable<UpdateBoundaryAttachmentCommand>,
    ICommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/update-boundary-attachment");

    public UpdateBoundaryAttachmentCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        VisualStateId targetVisualStateId,
        BoundaryAttachmentPlacement targetAttachment,
        RectD effectiveOwnerBounds)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        ArgumentNullException.ThrowIfNull(targetAttachment);

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        TargetVisualStateId = targetVisualStateId;
        TargetAttachment = targetAttachment;
        EffectiveOwnerBounds = effectiveOwnerBounds;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Visual;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.VisualModel;

    public VisualStateId TargetVisualStateId { get; }

    public BoundaryAttachmentPlacement TargetAttachment { get; }

    /// <summary>
    /// Gets the owner bounds used to resolve the requested placement. For a non-pinned
    /// owner these are the effective Layout bounds captured by the initiating editor;
    /// persistent owner geometry remains unchanged.
    /// </summary>
    public RectD EffectiveOwnerBounds { get; }

    public bool Equals(UpdateBoundaryAttachmentCommand? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        TargetDocumentId == other.TargetDocumentId &&
        ExpectedRevision == other.ExpectedRevision &&
        TargetVisualStateId == other.TargetVisualStateId &&
        TargetAttachment.Equals(other.TargetAttachment) &&
        EffectiveOwnerBounds == other.EffectiveOwnerBounds;

    public override bool Equals(object? obj) =>
        Equals(obj as UpdateBoundaryAttachmentCommand);

    public override int GetHashCode() => HashCode.Combine(
        TargetDocumentId,
        ExpectedRevision,
        TargetVisualStateId,
        TargetAttachment,
        EffectiveOwnerBounds);

    public static bool operator ==(
        UpdateBoundaryAttachmentCommand? left,
        UpdateBoundaryAttachmentCommand? right) =>
        EqualityComparer<UpdateBoundaryAttachmentCommand>.Default.Equals(left, right);

    public static bool operator !=(
        UpdateBoundaryAttachmentCommand? left,
        UpdateBoundaryAttachmentCommand? right) =>
        !(left == right);
}
