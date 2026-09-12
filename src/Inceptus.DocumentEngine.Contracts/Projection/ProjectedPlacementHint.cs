using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Describes persistent placement information exposed as a read-only projection hint.
/// </summary>
public sealed class ProjectedPlacementHint : IEquatable<ProjectedPlacementHint>
{
    public ProjectedPlacementHint(
        PointD position,
        SizeD size,
        VisualPlacementMode placementMode,
        ProjectedBoundaryAttachment? boundaryAttachment = null)
    {
        if (!Enum.IsDefined(placementMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(placementMode),
                placementMode,
                "The placement mode must be defined.");
        }

        Position = position;
        Size = size;
        PlacementMode = placementMode;
        BoundaryAttachment = boundaryAttachment;
    }

    public PointD Position { get; }

    public SizeD Size { get; }

    public VisualPlacementMode PlacementMode { get; }

    public ProjectedBoundaryAttachment? BoundaryAttachment { get; }

    public bool Equals(ProjectedPlacementHint? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Position == other.Position &&
        Size == other.Size &&
        PlacementMode == other.PlacementMode &&
        Equals(BoundaryAttachment, other.BoundaryAttachment);

    public override bool Equals(object? obj) => Equals(obj as ProjectedPlacementHint);

    public override int GetHashCode() =>
        HashCode.Combine(Position, Size, PlacementMode, BoundaryAttachment);
}
