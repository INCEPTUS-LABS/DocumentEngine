using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

/// <summary>
/// Resolves connector labels against the supplied effective route. Owns no persistent
/// geometry and never changes the route. Callers supply the projected automatic intent.
/// </summary>
internal static class Canvas2DConnectorLabelResolver
{
    internal static PointD Resolve(
        ProjectedLabel label,
        IReadOnlyList<PointD> path,
        ConnectorLabelPlacement? manualPlacement,
        SizeD textSize)
    {
        if (manualPlacement is not null || label.ConnectorPlacement is null)
        {
            var placement = manualPlacement ?? ConnectorLabelPlacement.Default;
            return Canvas2DConnectorPathGeometry.ResolvePoint(path, placement.PathPosition) +
                placement.Offset;
        }

        var intent = label.ConnectorPlacement;
        var (point, tangent) = Canvas2DConnectorPathGeometry.ResolveSourceFrame(
            path, intent.DistanceFromSource);
        var normal = new VectorD(tangent.Y, -tangent.X);
        // Follow the branch's net transverse direction when it turns away from
        // the source tangent. Collinear routes retain the clockwise normal.
        var towardTarget = path[^1] - point;
        if ((towardTarget.X * normal.X) + (towardTarget.Y * normal.Y) < -1e-9)
        {
            normal = new VectorD(-normal.X, -normal.Y);
        }
        var halfWidth = textSize.Width / 2d;
        var halfHeight = textSize.Height / 2d;
        var clearance = (Math.Abs(normal.X) * halfWidth) +
            (Math.Abs(normal.Y) * halfHeight) + intent.PerpendicularGap;
        var preferred = BoundarySafe(point, normal, clearance, halfWidth, halfHeight);
        var opposite = BoundarySafe(
            point, new VectorD(-normal.X, -normal.Y), clearance, halfWidth, halfHeight);
        return preferred is { } first &&
            (opposite is null || first.Adjustment <= opposite.Value.Adjustment)
                ? first.Point
                : opposite!.Value.Point;
    }

    // The center of the composed line bounds is the shared resolver's final anchor,
    // including spatial composition. Interaction must start here, not at a separately
    // reconstructed canonical midpoint or at the center of whichever line was hit.
    internal static RectD PresentedBounds(IEnumerable<Canvas2DSceneItem> lines)
    {
        var bounds = lines.Select(static item => item.Bounds).ToArray();
        var left = bounds.Min(static item => item.Left);
        var top = bounds.Min(static item => item.Top);
        return new RectD(left, top,
            bounds.Max(static item => item.Right) - left,
            bounds.Max(static item => item.Bottom) - top);
    }

    private static (PointD Point, double Adjustment)? BoundarySafe(
        PointD routePoint, VectorD normal, double clearance, double halfWidth, double halfHeight)
    {
        var requested = new PointD(
            routePoint.X + (normal.X * clearance),
            routePoint.Y + (normal.Y * clearance));
        var x = Math.Max(halfWidth, requested.X);
        var y = Math.Max(halfHeight, requested.Y);
        var missingClearance = clearance -
            (((x - routePoint.X) * normal.X) + ((y - routePoint.Y) * normal.Y));
        if (missingClearance > 1e-9)
        {
            var positiveX = Math.Max(0d, normal.X);
            var positiveY = Math.Max(0d, normal.Y);
            var lengthSquared = (positiveX * positiveX) + (positiveY * positiveY);
            if (lengthSquared == 0d)
            {
                return null;
            }

            x += positiveX * missingClearance / lengthSquared;
            y += positiveY * missingClearance / lengthSquared;
        }

        var dx = x - requested.X;
        var dy = y - requested.Y;
        return (new PointD(x, y), (dx * dx) + (dy * dy));
    }
}
