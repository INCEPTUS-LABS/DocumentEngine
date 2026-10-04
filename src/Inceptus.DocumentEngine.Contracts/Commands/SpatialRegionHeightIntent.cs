using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>Authorizes one scoped authored height change, independently of connector routing.</summary>
public sealed record SpatialRegionHeightIntent
{
    public SpatialRegionHeightIntent(DocumentScopeId scopeId, Canvas2DSpatialRegionId regionId, double expandedHeight)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        ArgumentNullException.ThrowIfNull(regionId);
        if (!double.IsFinite(expandedHeight) || expandedHeight <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(expandedHeight));
        }
        ScopeId = scopeId; RegionId = regionId; ExpandedHeight = expandedHeight;
    }
    public DocumentScopeId ScopeId { get; }
    public Canvas2DSpatialRegionId RegionId { get; }
    public double ExpandedHeight { get; }
}
