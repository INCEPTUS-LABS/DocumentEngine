using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Immutable reversible vertical compaction from expanded scope coordinates to displayed
/// document coordinates. Gaps and expanded rows translate without changing their size.
/// </summary>
public sealed class Canvas2DSpatialCoordinateMap : IEquatable<Canvas2DSpatialCoordinateMap>
{
    public Canvas2DSpatialCoordinateMap(IEnumerable<Canvas2DSpatialCoordinateBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);
        var copy = bands.ToArray();
        if (Array.Exists(copy, static band => band is null))
        {
            throw new ArgumentException("Coordinate bands cannot contain null values.", nameof(bands));
        }

        Array.Sort(copy, static (left, right) => left.LogicalTop.CompareTo(right.LogicalTop));
        var regionIds = new HashSet<Canvas2DSpatialRegionId>();
        for (var index = 0; index < copy.Length; index++)
        {
            var band = copy[index];
            if (!regionIds.Add(band.RegionId) ||
                !double.IsFinite(band.DisplayedHeight / band.LogicalHeight) ||
                band.DisplayedHeight / band.LogicalHeight <= 0d)
            {
                throw new ArgumentException("Coordinate bands require unique regions and positive slopes.", nameof(bands));
            }

            if (index == 0)
            {
                if (band.LogicalTop != band.DisplayedTop)
                {
                    throw new ArgumentException("The map must be identity above its first row.", nameof(bands));
                }
            }
            else
            {
                var previous = copy[index - 1];
                var expectedTop = previous.DisplayedBottom + (band.LogicalTop - previous.LogicalBottom);
                if (band.LogicalTop < previous.LogicalBottom ||
                    band.DisplayedTop < previous.DisplayedBottom ||
                    !double.IsFinite(expectedTop) ||
                    !EquivalentBoundary(band.DisplayedTop, expectedTop))
                {
                    throw new ArgumentException(
                        "Coordinate bands must not overlap and their gaps must remain continuous translations.",
                        nameof(bands));
                }
            }
        }

        Bands = [.. copy];
        IsIdentity = Bands.All(static band =>
            band.LogicalTop == band.DisplayedTop && band.LogicalBottom == band.DisplayedBottom);
    }

    public static Canvas2DSpatialCoordinateMap Identity { get; } = new([]);
    public ImmutableArray<Canvas2DSpatialCoordinateBand> Bands { get; }
    public bool IsIdentity { get; }

    public PointD MapLogicalToScene(PointD point) =>
        IsIdentity ? point : new PointD(point.X, MapY(point.Y, inverse: false));

    public PointD MapSceneToLogical(PointD point) =>
        IsIdentity ? point : new PointD(point.X, MapY(point.Y, inverse: true));

    /// <summary>
    /// Maps the complete image of each segment, retaining supplied vertices and adding only
    /// transient vertices at crossed band boundaries. Manual and Straight presentations map
    /// their authored vertices/endpoints directly instead of calling this method.
    /// </summary>
    public ImmutableArray<PointD> MapPath(IEnumerable<PointD> logicalPath)
    {
        ArgumentNullException.ThrowIfNull(logicalPath);
        var source = logicalPath.ToImmutableArray();
        if (source.Length < 2)
        {
            throw new ArgumentException("A path requires at least two points.", nameof(logicalPath));
        }

        if (IsIdentity)
        {
            return source;
        }

        var boundaries = Bands.SelectMany(static band => new[] { band.LogicalTop, band.LogicalBottom })
            .Distinct().Order().ToArray();
        var mapped = ImmutableArray.CreateBuilder<PointD>();
        mapped.Add(MapLogicalToScene(source[0]));
        for (var index = 1; index < source.Length; index++)
        {
            var start = source[index - 1];
            var end = source[index];
            if (start.Y != end.Y)
            {
                var crossed = boundaries.Where(y => y > Math.Min(start.Y, end.Y) && y < Math.Max(start.Y, end.Y));
                if (end.Y < start.Y)
                {
                    crossed = crossed.Reverse();
                }

                foreach (var y in crossed)
                {
                    var fraction = (y - start.Y) / (end.Y - start.Y);
                    mapped.Add(MapLogicalToScene(new PointD(start.X + (fraction * (end.X - start.X)), y)));
                }
            }

            mapped.Add(MapLogicalToScene(end));
        }

        return mapped.ToImmutable();
    }

    public bool TryGetTranslation(out Matrix2D transform)
    {
        transform = Matrix2D.Identity;
        return IsIdentity;
    }

    public bool Equals(Canvas2DSpatialCoordinateMap? other) =>
        ReferenceEquals(this, other) || other is not null && Bands.AsSpan().SequenceEqual(other.Bands.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as Canvas2DSpatialCoordinateMap);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var band in Bands)
        {
            hash.Add(band);
        }
        return hash.ToHashCode();
    }

    private double MapY(double value, bool inverse)
    {
        var offset = 0d;
        foreach (var band in Bands)
        {
            var sourceTop = inverse ? band.DisplayedTop : band.LogicalTop;
            var sourceBottom = inverse ? band.DisplayedBottom : band.LogicalBottom;
            var targetTop = inverse ? band.LogicalTop : band.DisplayedTop;
            var targetBottom = inverse ? band.LogicalBottom : band.DisplayedBottom;
            if (value < sourceTop)
            {
                return value + offset;
            }
            if (value <= sourceBottom)
            {
                if (value == sourceTop)
                {
                    return targetTop;
                }
                if (value == sourceBottom)
                {
                    return targetBottom;
                }
                if (EquivalentBoundary(targetBottom, targetTop + (sourceBottom - sourceTop)))
                {
                    return value + (targetTop - sourceTop);
                }
                return targetTop + ((value - sourceTop) / (sourceBottom - sourceTop) * (targetBottom - targetTop));
            }
            offset = targetBottom - sourceBottom;
        }
        return value + offset;
    }

    internal static bool EquivalentBoundary(double left, double right)
    {
        if (left == right)
        {
            return true;
        }
        var lower = right;
        var upper = right;
        for (var index = 0; index < 4; index++)
        {
            lower = Math.BitDecrement(lower);
            upper = Math.BitIncrement(upper);
        }
        return left >= lower && left <= upper;
    }
}
