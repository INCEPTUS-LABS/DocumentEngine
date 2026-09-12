namespace Inceptus.DocumentEngine.Contracts.Geometry;

/// <summary>
/// Defines the notation-neutral persistent Document geometry boundary.
/// Viewport presentation remains unbounded and may expose coordinates outside this region.
/// </summary>
public static class DocumentGeometryBoundary
{
    public const double MinimumX = 0d;

    public const double MinimumY = 0d;

    public static bool Contains(PointD point) =>
        point.X >= MinimumX && point.Y >= MinimumY;

    public static bool Contains(RectD bounds) =>
        bounds.Left >= MinimumX && bounds.Top >= MinimumY;

    public static PointD Clamp(PointD point) => new(
        Math.Max(MinimumX, point.X),
        Math.Max(MinimumY, point.Y));

    /// <summary>
    /// Translates a complete rectangle into the legal region without changing its size.
    /// </summary>
    public static RectD Clamp(RectD bounds) => bounds.Translate(new VectorD(
        Math.Max(0d, MinimumX - bounds.Left),
        Math.Max(0d, MinimumY - bounds.Top)));

    /// <summary>
    /// Clamps one shared translation so every supplied rectangle remains legal.
    /// </summary>
    public static VectorD ClampTranslation(
        IEnumerable<RectD> bounds,
        VectorD requestedTranslation)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        var hasBounds = false;
        var minimumLeft = double.MaxValue;
        var minimumTop = double.MaxValue;
        foreach (var current in bounds)
        {
            hasBounds = true;
            minimumLeft = Math.Min(minimumLeft, current.Left);
            minimumTop = Math.Min(minimumTop, current.Top);
        }

        return !hasBounds
            ? requestedTranslation
            : new VectorD(
                Math.Max(requestedTranslation.X, MinimumX - minimumLeft),
                Math.Max(requestedTranslation.Y, MinimumY - minimumTop));
    }
}
