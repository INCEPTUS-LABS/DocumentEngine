using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Layout;

/// <summary>
/// Immutable document-coordinate geometry calculated for one projected group.
/// </summary>
public sealed class LayoutGroupGeometry : IEquatable<LayoutGroupGeometry>
{
    public LayoutGroupGeometry(ProjectedObjectId projectedObjectId, RectD bounds)
    {
        ArgumentNullException.ThrowIfNull(projectedObjectId);

        ProjectedObjectId = projectedObjectId;
        Bounds = bounds;
    }

    public ProjectedObjectId ProjectedObjectId { get; }

    public RectD Bounds { get; }

    public bool Equals(LayoutGroupGeometry? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        ProjectedObjectId == other.ProjectedObjectId &&
        Bounds == other.Bounds;

    public override bool Equals(object? obj) => Equals(obj as LayoutGroupGeometry);

    public override int GetHashCode() => HashCode.Combine(ProjectedObjectId, Bounds);
}
