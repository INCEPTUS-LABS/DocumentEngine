using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

/// <summary>
/// Immutable logical document-space geometry used for directed connector target arrows.
/// </summary>
internal sealed class Canvas2DConnectorArrowConfiguration :
    IEquatable<Canvas2DConnectorArrowConfiguration>
{
    internal Canvas2DConnectorArrowConfiguration(
        double length = 10d,
        double halfWidth = 4d)
    {
        if (!double.IsFinite(length) || length <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                length,
                "The connector arrow length must be finite and greater than zero.");
        }

        if (!double.IsFinite(halfWidth) || halfWidth <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(halfWidth),
                halfWidth,
                "The connector arrow half-width must be finite and greater than zero.");
        }

        Length = length;
        HalfWidth = halfWidth;
    }

    internal static Canvas2DConnectorArrowConfiguration Default { get; } = new();

    internal double Length { get; }

    internal double HalfWidth { get; }

    public bool Equals(Canvas2DConnectorArrowConfiguration? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Length.Equals(other.Length) &&
        HalfWidth.Equals(other.HalfWidth);

    public override bool Equals(object? obj) =>
        Equals(obj as Canvas2DConnectorArrowConfiguration);

    public override int GetHashCode() => HashCode.Combine(Length, HalfWidth);
}

/// <summary>
/// Resolves a target arrow from the last non-degenerate segment of an already routed path.
/// </summary>
internal static class Canvas2DConnectorArrowGeometry
{
    internal static Canvas2DSceneGeometry? Create(
        IReadOnlyList<PointD> routedPath,
        Canvas2DConnectorArrowConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(routedPath);
        if (routedPath.Count < 2)
        {
            return null;
        }

        configuration ??= Canvas2DConnectorArrowConfiguration.Default;
        var tip = routedPath[^1];
        if (!IsFinite(tip))
        {
            return null;
        }

        PointD? previous = null;
        double segmentLength = 0d;
        for (var index = routedPath.Count - 2; index >= 0; index--)
        {
            var candidate = routedPath[index];
            if (!IsFinite(candidate))
            {
                return null;
            }

            segmentLength = Length(tip.X - candidate.X, tip.Y - candidate.Y);
            if (segmentLength > 0d && double.IsFinite(segmentLength))
            {
                previous = candidate;
                break;
            }
        }

        if (previous is null)
        {
            return null;
        }

        var directionX = (tip.X - previous.Value.X) / segmentLength;
        var directionY = (tip.Y - previous.Value.Y) / segmentLength;
        var baseCenter = new PointD(
            tip.X - (directionX * configuration.Length),
            tip.Y - (directionY * configuration.Length));
        var perpendicularX = -directionY * configuration.HalfWidth;
        var perpendicularY = directionX * configuration.HalfWidth;
        var firstWing = new PointD(
            baseCenter.X + perpendicularX,
            baseCenter.Y + perpendicularY);
        var secondWing = new PointD(
            baseCenter.X - perpendicularX,
            baseCenter.Y - perpendicularY);
        if (!IsFinite(baseCenter) || !IsFinite(firstWing) || !IsFinite(secondWing))
        {
            return null;
        }

        return Canvas2DSceneGeometry.Path(
            [tip, firstWing, secondWing],
            isClosed: true);
    }

    private static double Length(double x, double y)
    {
        var scale = Math.Max(Math.Abs(x), Math.Abs(y));
        if (scale == 0d)
        {
            return 0d;
        }

        var normalizedX = x / scale;
        var normalizedY = y / scale;
        return scale * Math.Sqrt((normalizedX * normalizedX) + (normalizedY * normalizedY));
    }

    private static bool IsFinite(PointD point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y);
}
