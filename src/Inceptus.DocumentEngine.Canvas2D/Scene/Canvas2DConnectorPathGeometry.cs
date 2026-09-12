using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

/// <summary>
/// Resolves route-relative connector geometry in logical document coordinates.
/// </summary>
internal static class Canvas2DConnectorPathGeometry
{
    private const double DistanceTieTolerance = 1e-12;

    internal static PointD ResolvePoint(
        IReadOnlyList<PointD> path,
        double pathPosition)
    {
        ValidatePath(path);
        if (!double.IsFinite(pathPosition))
        {
            throw new ArgumentOutOfRangeException(nameof(pathPosition));
        }

        var lengths = SegmentLengths(path, out var totalLength);
        if (totalLength == 0d)
        {
            return path[0];
        }

        var targetLength = Math.Clamp(pathPosition, 0d, 1d) * totalLength;
        var traversed = 0d;
        for (var index = 0; index < lengths.Length; index++)
        {
            var segmentLength = lengths[index];
            if (segmentLength == 0d)
            {
                continue;
            }

            if (targetLength <= traversed + segmentLength)
            {
                var ratio = Math.Clamp(
                    (targetLength - traversed) / segmentLength,
                    0d,
                    1d);
                return Interpolate(path[index], path[index + 1], ratio);
            }

            traversed += segmentLength;
        }

        return path[^1];
    }

    internal static Canvas2DConnectorPathProjection FindNearest(
        IReadOnlyList<PointD> path,
        PointD point)
    {
        ValidatePath(path);
        var lengths = SegmentLengths(path, out var totalLength);
        if (totalLength == 0d)
        {
            return new Canvas2DConnectorPathProjection(
                0d,
                path[0],
                point - path[0],
                0);
        }

        var bestDistanceSquared = double.PositiveInfinity;
        var bestLength = 0d;
        var bestPoint = path[0];
        var bestSegmentIndex = 0;
        var traversed = 0d;
        for (var index = 0; index < lengths.Length; index++)
        {
            var start = path[index];
            var end = path[index + 1];
            var segmentLength = lengths[index];
            if (segmentLength == 0d)
            {
                continue;
            }

            var segment = end - start;
            var ratio = Math.Clamp(
                (((point.X - start.X) * segment.X) +
                 ((point.Y - start.Y) * segment.Y)) /
                (segmentLength * segmentLength),
                0d,
                1d);
            var candidate = Interpolate(start, end, ratio);
            var delta = point - candidate;
            var distanceSquared = (delta.X * delta.X) + (delta.Y * delta.Y);
            var candidateLength = traversed + (ratio * segmentLength);
            if (distanceSquared + DistanceTieTolerance < bestDistanceSquared ||
                Math.Abs(distanceSquared - bestDistanceSquared) <= DistanceTieTolerance &&
                candidateLength < bestLength)
            {
                bestDistanceSquared = distanceSquared;
                bestLength = candidateLength;
                bestPoint = candidate;
                bestSegmentIndex = index;
            }

            traversed += segmentLength;
        }

        return new Canvas2DConnectorPathProjection(
            Math.Clamp(bestLength / totalLength, 0d, 1d),
            bestPoint,
            point - bestPoint,
            bestSegmentIndex);
    }

    private static double[] SegmentLengths(
        IReadOnlyList<PointD> path,
        out double totalLength)
    {
        var lengths = new double[path.Count - 1];
        totalLength = 0d;
        for (var index = 0; index < lengths.Length; index++)
        {
            var delta = path[index + 1] - path[index];
            lengths[index] = Length(delta);
            totalLength += lengths[index];
        }

        return lengths;
    }

    private static double Length(VectorD vector)
    {
        var scale = Math.Max(Math.Abs(vector.X), Math.Abs(vector.Y));
        if (scale == 0d)
        {
            return 0d;
        }

        var x = vector.X / scale;
        var y = vector.Y / scale;
        return scale * Math.Sqrt((x * x) + (y * y));
    }

    private static PointD Interpolate(PointD start, PointD end, double ratio) =>
        new(
            start.X + ((end.X - start.X) * ratio),
            start.Y + ((end.Y - start.Y) * ratio));

    private static void ValidatePath(IReadOnlyList<PointD> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count < 2)
        {
            throw new ArgumentException(
                "A connector path requires at least two points.",
                nameof(path));
        }
    }
}

internal sealed record Canvas2DConnectorPathProjection(
    double PathPosition,
    PointD RoutePoint,
    VectorD Offset,
    int SegmentIndex);
