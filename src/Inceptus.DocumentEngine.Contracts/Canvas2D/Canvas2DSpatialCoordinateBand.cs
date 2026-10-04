namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Associates one expanded logical row with its current displayed row. The mapping is transient;
/// a compact row never changes the saved region height or its logical contents.
/// </summary>
public sealed class Canvas2DSpatialCoordinateBand : IEquatable<Canvas2DSpatialCoordinateBand>
{
    public Canvas2DSpatialCoordinateBand(
        Canvas2DSpatialRegionId regionId,
        double logicalTop,
        double logicalBottom,
        double displayedTop,
        double displayedBottom)
    {
        ArgumentNullException.ThrowIfNull(regionId);
        if (!double.IsFinite(logicalTop) || !double.IsFinite(logicalBottom) ||
            !double.IsFinite(displayedTop) || !double.IsFinite(displayedBottom) ||
            !double.IsFinite(logicalBottom - logicalTop) ||
            !double.IsFinite(displayedBottom - displayedTop) ||
            logicalBottom <= logicalTop || displayedBottom <= displayedTop ||
            (displayedBottom - displayedTop > logicalBottom - logicalTop &&
             !Canvas2DSpatialCoordinateMap.EquivalentBoundary(
                 displayedBottom, displayedTop + (logicalBottom - logicalTop))))
        {
            throw new ArgumentException(
                "A coordinate band requires finite positive heights and cannot expand its logical row.");
        }

        RegionId = regionId;
        LogicalTop = logicalTop;
        LogicalBottom = logicalBottom;
        DisplayedTop = displayedTop;
        DisplayedBottom = displayedBottom;
    }

    public Canvas2DSpatialRegionId RegionId { get; }
    public double LogicalTop { get; }
    public double LogicalBottom { get; }
    public double DisplayedTop { get; }
    public double DisplayedBottom { get; }
    public double LogicalHeight => LogicalBottom - LogicalTop;
    public double DisplayedHeight => DisplayedBottom - DisplayedTop;

    public bool Equals(Canvas2DSpatialCoordinateBand? other) =>
        ReferenceEquals(this, other) ||
        other is not null && RegionId == other.RegionId &&
        LogicalTop == other.LogicalTop && LogicalBottom == other.LogicalBottom &&
        DisplayedTop == other.DisplayedTop && DisplayedBottom == other.DisplayedBottom;

    public override bool Equals(object? obj) => Equals(obj as Canvas2DSpatialCoordinateBand);

    public override int GetHashCode() => HashCode.Combine(
        RegionId, LogicalTop, LogicalBottom, DisplayedTop, DisplayedBottom);
}
