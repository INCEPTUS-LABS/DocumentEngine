using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>Transient mapping of an expanded region envelope into a compact Scene.</summary>
public sealed record Canvas2DDerivedRegionMapping
{
    public Canvas2DDerivedRegionMapping(RectD logicalBounds, Canvas2DSpatialCoordinateMap coordinateMap)
    {
        ArgumentNullException.ThrowIfNull(coordinateMap);
        LogicalBounds = logicalBounds;
        CoordinateMap = coordinateMap;
    }

    public RectD LogicalBounds { get; }
    public Canvas2DSpatialCoordinateMap CoordinateMap { get; }

    public RectD MapBounds(RectD bounds)
    {
        var top = CoordinateMap.MapLogicalToScene(bounds.TopLeft);
        var bottom = CoordinateMap.MapLogicalToScene(new PointD(bounds.Right, bounds.Bottom));
        return new(top.X, top.Y, bottom.X - top.X, bottom.Y - top.Y);
    }
}
