using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.Export;

internal static class GraphicalSceneBounds
{
    internal static RectD Union(RectD a, RectD b) => new(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top),
        Math.Max(a.Right, b.Right) - Math.Min(a.Left, b.Left), Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Top, b.Top));

    internal static RectD? Calculate(Canvas2DSceneItem item)
    {
        var geometry = item.Geometry;
        var b = geometry.Bounds;
        var m = item.Transform;
        var half = item.Style.Stroke is null || geometry.Kind == Canvas2DSceneGeometryKind.Image ? 0 : item.Style.StrokeWidth / 2;
        RectD bounds;
        if (geometry.Kind == Canvas2DSceneGeometryKind.Path)
        {
            var points = geometry.Points;
            var painted = new List<PointD>(points);
            var count = geometry.IsClosed ? points.Length : points.Length - 1;
            for (var i = 0; i < count; i++)
            {
                var a = points[i];
                var c = points[(i + 1) % points.Length];
                var dx = c.X - a.X;
                var dy = c.Y - a.Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length == 0) continue;
                var nx = -dy / length * half;
                var ny = dx / length * half;
                painted.Add(new(a.X + nx, a.Y + ny));
                painted.Add(new(a.X - nx, a.Y - ny));
                painted.Add(new(c.X + nx, c.Y + ny));
                painted.Add(new(c.X - nx, c.Y - ny));
                if (!geometry.IsClosed && i + 1 == count) continue;
                var d = points[(i + 2) % points.Length];
                var ex = d.X - c.X;
                var ey = d.Y - c.Y;
                var nextLength = Math.Sqrt(ex * ex + ey * ey);
                if (nextLength == 0) continue;
                var ux = dx / length;
                var uy = dy / length;
                var vx = ex / nextLength;
                var vy = ey / nextLength;
                var denominator = 1 + ux * vx + uy * vy;
                if (denominator <= 0) continue;
                var mx = (-uy - vy) * half / denominator;
                var my = (ux + vx) * half / denominator;
                // Canvas and SVG both use miterLimit=10 and bevel joins above that limit.
                if (mx * mx + my * my <= 100 * half * half)
                {
                    painted.Add(new(c.X + mx, c.Y + my));
                    painted.Add(new(c.X - mx, c.Y - my));
                }
            }
            bounds = FromPoints(painted.Select(m.TransformPoint));
        }
        else if (geometry.Kind == Canvas2DSceneGeometryKind.Ellipse)
        {
            var center = m.TransformPoint(new(b.X + b.Width / 2, b.Y + b.Height / 2));
            var rx = b.Width / 2;
            var ry = b.Height / 2;
            var x = Math.Sqrt(m.M11 * m.M11 * rx * rx + m.M21 * m.M21 * ry * ry) + half * Math.Sqrt(m.M11 * m.M11 + m.M21 * m.M21);
            var y = Math.Sqrt(m.M12 * m.M12 * rx * rx + m.M22 * m.M22 * ry * ry) + half * Math.Sqrt(m.M12 * m.M12 + m.M22 * m.M22);
            bounds = new(center.X - x, center.Y - y, x * 2, y * 2);
        }
        else
        {
            // Text geometry carries the measured ink rectangle from the captured Scene.
            bounds = FromPoints(new[]
            {
                new PointD(b.Left - half, b.Top - half), new(b.Right + half, b.Top - half),
                new(b.Right + half, b.Bottom + half), new(b.Left - half, b.Bottom + half),
            }.Select(m.TransformPoint));
        }
        return item.Clip is { } clip ? bounds.Intersection(clip) : bounds;
    }

    private static RectD FromPoints(IEnumerable<PointD> points)
    {
        var copy = points.ToArray();
        var left = copy.Min(p => p.X);
        var top = copy.Min(p => p.Y);
        return new(left, top, copy.Max(p => p.X) - left, copy.Max(p => p.Y) - top);
    }
}
