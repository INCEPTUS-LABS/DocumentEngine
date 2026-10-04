using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

internal enum Canvas2DSegmentOrientation
{
    Degenerate,
    Horizontal,
    Vertical,
    Diagonal,
}

internal static class Canvas2DSegmentGeometry
{
    // Logical geometry tolerance, independent of viewport scale and device pixels.
    internal const double Tolerance = 1e-9;

    internal static Canvas2DSegmentOrientation Orientation(PointD start, PointD end)
    {
        var sameX = Math.Abs(end.X - start.X) <= Tolerance;
        var sameY = Math.Abs(end.Y - start.Y) <= Tolerance;
        return (sameX, sameY) switch
        {
            (true, true) => Canvas2DSegmentOrientation.Degenerate,
            (false, true) => Canvas2DSegmentOrientation.Horizontal,
            (true, false) => Canvas2DSegmentOrientation.Vertical,
            _ => Canvas2DSegmentOrientation.Diagonal,
        };
    }
}
