using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>Exact local node geometry, retaining Layout's bounds and transform semantics.</summary>
public sealed record ScopeNodeGeometrySnapshot
{
    public ScopeNodeGeometrySnapshot(
        VisualStateId visualStateId,
        RectD localBounds,
        Matrix2D transform,
        Canvas2DSpatialRegionId? regionId)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        if (!transform.TryInvert(out _))
            throw new ArgumentException("Node transforms must be invertible.", nameof(transform));
        VisualStateId = visualStateId;
        LocalBounds = localBounds;
        Transform = transform;
        RegionId = regionId;
    }

    public VisualStateId VisualStateId { get; }
    public RectD LocalBounds { get; }
    public Matrix2D Transform { get; }
    public Canvas2DSpatialRegionId? RegionId { get; }
}
