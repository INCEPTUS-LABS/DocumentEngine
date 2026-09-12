using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.HitTesting;

/// <summary>
/// Performs deterministic, browser-independent hit testing over immutable Canvas2D scene data.
/// </summary>
public sealed class Canvas2DSceneHitTestService
{
    private const int EllipseSampleCount = 96;
    private const int EllipseRefinementCount = 48;
    private const double BoundaryTolerance = 1e-10;

    public Canvas2DSceneHitTestService(
        Canvas2DSceneHitTestConfiguration? configuration = null) =>
        Configuration = configuration ?? Canvas2DSceneHitTestConfiguration.Default;

    public Canvas2DSceneHitTestConfiguration Configuration { get; }

    public Canvas2DSceneHitTestResult? HitTest(
        Canvas2DScene scene,
        PointD documentPoint)
    {
        ArgumentNullException.ThrowIfNull(scene);

        for (var index = scene.Items.Length - 1; index >= 0; index--)
        {
            var item = scene.Items[index];
            if (IsHit(item, documentPoint))
            {
                if (Canvas2DConnectorLineJumpMetadata.TryResolveHitTarget(
                        item,
                        out var hitTargetId))
                {
                    var hitTarget = scene.Items.FirstOrDefault(candidate =>
                        candidate.Id == hitTargetId);
                    if (hitTarget is not null &&
                        item.Origin.RelatedSceneObjectIds.Contains(hitTarget.Id) &&
                        item.Origin.VisualStateId == hitTarget.Origin.VisualStateId &&
                        item.Origin.ProjectedObjectId == hitTarget.Origin.ProjectedObjectId &&
                        hitTarget.IsVisible &&
                        hitTarget.HitTestPolicy.Mode != Canvas2DHitTestMode.None)
                    {
                        return new Canvas2DSceneHitTestResult(hitTarget, documentPoint);
                    }

                    continue;
                }

                return new Canvas2DSceneHitTestResult(item, documentPoint);
            }
        }

        return null;
    }

    private bool IsHit(Canvas2DSceneItem item, PointD point)
    {
        var mode = item.HitTestPolicy.Mode;
        if (!item.IsVisible || mode == Canvas2DHitTestMode.None ||
            item.Clip is { } clip && !clip.Contains(point))
        {
            return false;
        }

        if (mode == Canvas2DHitTestMode.Bounds)
        {
            return item.Bounds.Contains(point);
        }

        var halfStrokeWidth = item.Style.StrokeWidth / 2d;
        var tolerance = Math.Max(
            Configuration.MinimumStrokeTolerance,
            item.HitTestPolicy.StrokeTolerance);
        var requiresStroke = mode is Canvas2DHitTestMode.Stroke or Canvas2DHitTestMode.FillOrStroke;
        var expansion = requiresStroke
            ? (halfStrokeWidth * MaximumScale(item.Transform)) + tolerance
            : 0d;
        if (!ContainsExpanded(item.Bounds, point, expansion))
        {
            return false;
        }

        var fillHit = (mode is Canvas2DHitTestMode.Fill or Canvas2DHitTestMode.FillOrStroke) &&
            IsFillHit(item, point);
        if (fillHit)
        {
            return true;
        }

        return requiresStroke && IsStrokeHit(item, point, halfStrokeWidth, tolerance);
    }

    private static bool IsFillHit(Canvas2DSceneItem item, PointD point) =>
        item.Geometry.Kind switch
        {
            Canvas2DSceneGeometryKind.Rectangle or
            Canvas2DSceneGeometryKind.Text or
            Canvas2DSceneGeometryKind.Image => IsPolygonFillHit(
                TransformRectangle(item.Geometry.Bounds, item.Transform),
                point),
            Canvas2DSceneGeometryKind.Ellipse => IsEllipseFillHit(item, point),
            Canvas2DSceneGeometryKind.Path => IsPolygonFillHit(
                TransformPoints(item.Geometry.Points, item.Transform),
                point),
            _ => false,
        };

    private static bool IsStrokeHit(
        Canvas2DSceneItem item,
        PointD point,
        double halfStrokeWidth,
        double tolerance) =>
        item.Geometry.Kind switch
        {
            Canvas2DSceneGeometryKind.Rectangle or
            Canvas2DSceneGeometryKind.Text or
            Canvas2DSceneGeometryKind.Image => IsPolylineStrokeHit(
                RectanglePoints(item.Geometry.Bounds),
                item.Transform,
                point,
                halfStrokeWidth,
                tolerance,
                close: true),
            Canvas2DSceneGeometryKind.Ellipse => IsEllipseStrokeHit(
                item,
                point,
                halfStrokeWidth,
                tolerance),
            Canvas2DSceneGeometryKind.Path => IsPolylineStrokeHit(
                item.Geometry.Points,
                item.Transform,
                point,
                halfStrokeWidth,
                tolerance,
                item.Geometry.IsClosed),
            _ => false,
        };

    private static bool IsEllipseFillHit(Canvas2DSceneItem item, PointD point)
    {
        var bounds = item.Geometry.Bounds;
        var radiusX = bounds.Width / 2d;
        var radiusY = bounds.Height / 2d;
        if (radiusX <= 0d || radiusY <= 0d || !item.Transform.TryInvert(out var inverse))
        {
            return false;
        }

        var local = inverse.TransformPoint(point);
        var normalizedX = (local.X - (bounds.X + radiusX)) / radiusX;
        var normalizedY = (local.Y - (bounds.Y + radiusY)) / radiusY;
        return (normalizedX * normalizedX) + (normalizedY * normalizedY) <=
            1d + BoundaryTolerance;
    }

    private static bool IsEllipseStrokeHit(
        Canvas2DSceneItem item,
        PointD point,
        double halfStrokeWidth,
        double tolerance)
    {
        var bounds = item.Geometry.Bounds;
        var centerX = bounds.X + (bounds.Width / 2d);
        var centerY = bounds.Y + (bounds.Height / 2d);
        var radiusX = bounds.Width / 2d;
        var radiusY = bounds.Height / 2d;
        if (radiusX == 0d && radiusY == 0d)
        {
            var degenerateReach = (halfStrokeWidth * MaximumScale(item.Transform)) + tolerance;
            return IsWithin(
                point,
                item.Transform.TransformPoint(new PointD(centerX, centerY)),
                degenerateReach);
        }

        var step = (Math.PI * 2d) / EllipseSampleCount;
        var bestAngle = 0d;
        var bestDistanceSquared = double.PositiveInfinity;
        for (var index = 0; index < EllipseSampleCount; index++)
        {
            var angle = index * step;
            var candidate = EllipsePoint(
                centerX,
                centerY,
                radiusX,
                radiusY,
                item.Transform,
                angle);
            var distanceSquared = DistanceSquared(point, candidate);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestAngle = angle;
            }
        }

        var left = bestAngle - step;
        var right = bestAngle + step;
        for (var iteration = 0; iteration < EllipseRefinementCount; iteration++)
        {
            var first = left + ((right - left) / 3d);
            var second = right - ((right - left) / 3d);
            if (EllipseDistanceSquared(item, point, centerX, centerY, radiusX, radiusY, first) <=
                EllipseDistanceSquared(item, point, centerX, centerY, radiusX, radiusY, second))
            {
                right = second;
            }
            else
            {
                left = first;
            }
        }

        var refinedAngle = (left + right) / 2d;
        var refinedDistanceSquared = EllipseDistanceSquared(
            item,
            point,
            centerX,
            centerY,
            radiusX,
            radiusY,
            refinedAngle);
        var tangent = new VectorD(
            -radiusX * Math.Sin(refinedAngle),
            radiusY * Math.Cos(refinedAngle));
        var reach = TransformedPerpendicularReach(
            item.Transform,
            tangent,
            halfStrokeWidth) + tolerance;
        return refinedDistanceSquared <= reach * reach;
    }

    private static double EllipseDistanceSquared(
        Canvas2DSceneItem item,
        PointD point,
        double centerX,
        double centerY,
        double radiusX,
        double radiusY,
        double angle) =>
        DistanceSquared(
            point,
            EllipsePoint(centerX, centerY, radiusX, radiusY, item.Transform, angle));

    private static PointD EllipsePoint(
        double centerX,
        double centerY,
        double radiusX,
        double radiusY,
        Matrix2D transform,
        double angle) =>
        transform.TransformPoint(new PointD(
            centerX + (radiusX * Math.Cos(angle)),
            centerY + (radiusY * Math.Sin(angle))));

    private static bool IsPolygonFillHit(PointD[] points, PointD point)
    {
        if (points.Length < 3 || !HasArea(points))
        {
            return false;
        }

        var windingNumber = 0;
        for (int current = 0, previous = points.Length - 1;
             current < points.Length;
             previous = current++)
        {
            var start = points[previous];
            var end = points[current];
            if (DistanceToSegmentSquared(point, start, end) <= BoundaryTolerance * BoundaryTolerance)
            {
                return true;
            }

            var side = Cross(start, end, point);
            if (start.Y <= point.Y && end.Y > point.Y && side > 0d)
            {
                windingNumber++;
            }
            else if (start.Y > point.Y && end.Y <= point.Y && side < 0d)
            {
                windingNumber--;
            }
        }

        return windingNumber != 0;
    }

    private static bool HasArea(PointD[] points)
    {
        var origin = points[0];
        var secondIndex = 1;
        while (secondIndex < points.Length && points[secondIndex] == origin)
        {
            secondIndex++;
        }

        if (secondIndex == points.Length)
        {
            return false;
        }

        var second = points[secondIndex];
        for (var index = secondIndex + 1; index < points.Length; index++)
        {
            if (Math.Abs(Cross(origin, second, points[index])) > BoundaryTolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static double Cross(PointD start, PointD end, PointD point) =>
        ((end.X - start.X) * (point.Y - start.Y)) -
        ((point.X - start.X) * (end.Y - start.Y));

    private static bool IsPolylineStrokeHit(
        IReadOnlyList<PointD> localPoints,
        Matrix2D transform,
        PointD point,
        double halfStrokeWidth,
        double tolerance,
        bool close)
    {
        if (localPoints.Count == 0)
        {
            return false;
        }

        for (var index = 1; index < localPoints.Count; index++)
        {
            if (IsTransformedSegmentStrokeHit(
                    localPoints[index - 1],
                    localPoints[index],
                    transform,
                    point,
                    halfStrokeWidth,
                    tolerance))
            {
                return true;
            }
        }

        return close && IsTransformedSegmentStrokeHit(
            localPoints[^1],
            localPoints[0],
            transform,
            point,
            halfStrokeWidth,
            tolerance);
    }

    private static bool IsTransformedSegmentStrokeHit(
        PointD localStart,
        PointD localEnd,
        Matrix2D transform,
        PointD point,
        double halfStrokeWidth,
        double tolerance)
    {
        var start = transform.TransformPoint(localStart);
        var end = transform.TransformPoint(localEnd);
        var localTangent = new VectorD(
            localEnd.X - localStart.X,
            localEnd.Y - localStart.Y);
        var reach = TransformedPerpendicularReach(
            transform,
            localTangent,
            halfStrokeWidth) + tolerance;
        return DistanceToSegmentSquared(point, start, end) <= reach * reach;
    }

    private static double TransformedPerpendicularReach(
        Matrix2D transform,
        VectorD localTangent,
        double halfStrokeWidth)
    {
        if (halfStrokeWidth == 0d)
        {
            return 0d;
        }

        var localLength = Length(localTangent.X, localTangent.Y);
        if (localLength == 0d)
        {
            return halfStrokeWidth * MaximumScale(transform);
        }

        var transformedTangent = transform.TransformVector(localTangent);
        var transformedLength = Length(transformedTangent.X, transformedTangent.Y);
        if (transformedLength == 0d)
        {
            var localNormal = new VectorD(
                -localTangent.Y / localLength,
                localTangent.X / localLength);
            var transformedNormal = transform.TransformVector(localNormal);
            return halfStrokeWidth * Length(
                transformedNormal.X,
                transformedNormal.Y);
        }

        var determinant = Math.Abs(
            (transform.M11 * transform.M22) - (transform.M12 * transform.M21));
        return halfStrokeWidth * determinant * localLength / transformedLength;
    }

    private static double MaximumScale(Matrix2D transform)
    {
        var scale = Math.Max(
            Math.Max(Math.Abs(transform.M11), Math.Abs(transform.M12)),
            Math.Max(Math.Abs(transform.M21), Math.Abs(transform.M22)));
        if (scale == 0d)
        {
            return 0d;
        }

        var m11 = transform.M11 / scale;
        var m12 = transform.M12 / scale;
        var m21 = transform.M21 / scale;
        var m22 = transform.M22 / scale;
        var first = (m11 * m11) + (m12 * m12);
        var second = (m21 * m21) + (m22 * m22);
        var cross = (m11 * m21) + (m12 * m22);
        var discriminant = Math.Sqrt(
            Math.Max(0d, ((first - second) * (first - second)) + (4d * cross * cross)));
        return scale * Math.Sqrt((first + second + discriminant) / 2d);
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

    private static double DistanceToSegmentSquared(PointD point, PointD start, PointD end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared == 0d)
        {
            return DistanceSquared(point, start);
        }

        var projection = Math.Clamp(
            (((point.X - start.X) * deltaX) + ((point.Y - start.Y) * deltaY)) /
                lengthSquared,
            0d,
            1d);
        var closestX = start.X + (projection * deltaX);
        var closestY = start.Y + (projection * deltaY);
        var distanceX = point.X - closestX;
        var distanceY = point.Y - closestY;
        return (distanceX * distanceX) + (distanceY * distanceY);
    }

    private static bool IsWithin(PointD point, PointD target, double reach) =>
        DistanceSquared(point, target) <= reach * reach;

    private static double DistanceSquared(PointD left, PointD right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        return (x * x) + (y * y);
    }

    private static PointD[] TransformRectangle(RectD bounds, Matrix2D transform) =>
        TransformPoints(RectanglePoints(bounds), transform);

    private static PointD[] RectanglePoints(RectD bounds) =>
    [
        new PointD(bounds.Left, bounds.Top),
        new PointD(bounds.Right, bounds.Top),
        new PointD(bounds.Right, bounds.Bottom),
        new PointD(bounds.Left, bounds.Bottom),
    ];

    private static PointD[] TransformPoints(IEnumerable<PointD> points, Matrix2D transform) =>
        points.Select(transform.TransformPoint).ToArray();

    private static bool ContainsExpanded(RectD bounds, PointD point, double expansion) =>
        point.X >= bounds.Left - expansion && point.X <= bounds.Right + expansion &&
        point.Y >= bounds.Top - expansion && point.Y <= bounds.Bottom + expansion;
}
