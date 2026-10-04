using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.History;

/// <summary>An ordinary spatial History change, separate from non-History connector geometry.</summary>
public sealed record SpatialRegionHeightHistoryDelta
{
    public SpatialRegionHeightHistoryDelta(DocumentScopeId scopeId, Canvas2DSpatialRegionId regionId,
        double beforeHeight, double afterHeight)
    {
        ArgumentNullException.ThrowIfNull(scopeId); ArgumentNullException.ThrowIfNull(regionId);
        if (!double.IsFinite(beforeHeight) || beforeHeight <= 0d) { throw new ArgumentOutOfRangeException(nameof(beforeHeight)); }
        if (!double.IsFinite(afterHeight) || afterHeight <= 0d) { throw new ArgumentOutOfRangeException(nameof(afterHeight)); }
        if (beforeHeight == afterHeight) { throw new ArgumentException("A height delta requires different values.", nameof(afterHeight)); }
        ScopeId = scopeId; RegionId = regionId; BeforeHeight = beforeHeight; AfterHeight = afterHeight;
    }
    public DocumentScopeId ScopeId { get; }
    public Canvas2DSpatialRegionId RegionId { get; }
    public double BeforeHeight { get; }
    public double AfterHeight { get; }
}
