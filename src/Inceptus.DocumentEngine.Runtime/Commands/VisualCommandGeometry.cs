using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal static class VisualCommandGeometry
{
    private static readonly double MaximumRenderableMagnitude =
        Math.Sqrt(double.MaxValue) / 16d;

    internal static bool HasRenderableBounds(PointD position, SizeD size)
    {
        if (!IsRenderable(position) ||
            !DocumentGeometryBoundary.Contains(position) ||
            size.Width <= 0d ||
            size.Height <= 0d ||
            size.Width > MaximumRenderableMagnitude ||
            size.Height > MaximumRenderableMagnitude)
        {
            return false;
        }

        try
        {
            _ = new RectD(position.X, position.Y, size.Width, size.Height);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    internal static bool HasRenderableRoute(IReadOnlyList<PointD> route)
    {
        if (route.Count < 2 || route.Any(static point =>
                !IsRenderable(point) || !DocumentGeometryBoundary.Contains(point)))
        {
            return false;
        }

        var minimumX = route[0].X;
        var maximumX = minimumX;
        var minimumY = route[0].Y;
        var maximumY = minimumY;
        for (var index = 1; index < route.Count; index++)
        {
            minimumX = Math.Min(minimumX, route[index].X);
            maximumX = Math.Max(maximumX, route[index].X);
            minimumY = Math.Min(minimumY, route[index].Y);
            maximumY = Math.Max(maximumY, route[index].Y);
        }

        try
        {
            _ = new RectD(
                minimumX,
                minimumY,
                maximumX - minimumX,
                maximumY - minimumY);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool IsRenderable(PointD point) =>
        Math.Abs(point.X) <= MaximumRenderableMagnitude &&
        Math.Abs(point.Y) <= MaximumRenderableMagnitude;
}
