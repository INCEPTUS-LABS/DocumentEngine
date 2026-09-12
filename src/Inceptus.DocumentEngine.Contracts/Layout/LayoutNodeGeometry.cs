using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Layout;

/// <summary>
/// Immutable document-coordinate geometry calculated for one projected node.
/// </summary>
public sealed class LayoutNodeGeometry : IEquatable<LayoutNodeGeometry>
{
    public LayoutNodeGeometry(
        ProjectedObjectId projectedObjectId,
        RectD bounds,
        Matrix2D transform)
    {
        ArgumentNullException.ThrowIfNull(projectedObjectId);

        ProjectedObjectId = projectedObjectId;
        Bounds = bounds;
        Transform = transform;
    }

    public ProjectedObjectId ProjectedObjectId { get; }

    public RectD Bounds { get; }

    public Matrix2D Transform { get; }

    public PointD Position => Bounds.TopLeft;

    public SizeD Size => Bounds.Size;

    public bool Equals(LayoutNodeGeometry? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        ProjectedObjectId == other.ProjectedObjectId &&
        Bounds == other.Bounds &&
        Transform == other.Transform;

    public override bool Equals(object? obj) => Equals(obj as LayoutNodeGeometry);

    public override int GetHashCode() => HashCode.Combine(ProjectedObjectId, Bounds, Transform);
}
