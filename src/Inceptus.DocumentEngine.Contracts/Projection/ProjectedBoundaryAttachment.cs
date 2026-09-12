using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Carries one projected node's notation-neutral owner-boundary dependency.
/// </summary>
public sealed class ProjectedBoundaryAttachment : IEquatable<ProjectedBoundaryAttachment>
{
    public ProjectedBoundaryAttachment(
        SemanticElementId attachedToElementId,
        BoundaryAttachmentPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(attachedToElementId);
        ArgumentNullException.ThrowIfNull(placement);

        AttachedToElementId = attachedToElementId;
        Placement = placement;
    }

    public SemanticElementId AttachedToElementId { get; }

    public BoundaryAttachmentPlacement Placement { get; }

    public bool Equals(ProjectedBoundaryAttachment? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        AttachedToElementId == other.AttachedToElementId &&
        Placement.Equals(other.Placement);

    public override bool Equals(object? obj) => Equals(obj as ProjectedBoundaryAttachment);

    public override int GetHashCode() => HashCode.Combine(AttachedToElementId, Placement);
}
