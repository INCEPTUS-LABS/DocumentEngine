using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Bpmn.Scene;

/// <summary>
/// Places the compact SubProcess notation marker at the bottom center of its
/// Activity body. The marker is derived Scene geometry only.
/// </summary>
internal static class BpmnSubProcessMarkerPlacementPolicy
{
    private const double ExtentRatio = 0.18d;
    private const double BottomMarginRatio = 0.075d;
    private const double StrokeWidthRatio = 0.08d;
    private const double PlusInsetRatio = 0.22d;
    private const double PlusHalfThicknessRatio = 0.075d;

    internal static RectD ResolveBounds(RectD activityBounds)
    {
        var minimumDimension = Math.Min(activityBounds.Width, activityBounds.Height);
        var extent = minimumDimension * ExtentRatio;
        var bottomMargin = minimumDimension * BottomMarginRatio;
        return new RectD(
            activityBounds.Left + ((activityBounds.Width - extent) / 2d),
            activityBounds.Bottom - bottomMargin - extent,
            extent,
            extent);
    }

    internal static double ResolveStrokeWidth(RectD markerBounds) =>
        Math.Min(markerBounds.Width, markerBounds.Height) * StrokeWidthRatio;

    internal static PointD[] ResolvePlusPoints(RectD markerBounds)
    {
        var minimumDimension = Math.Min(markerBounds.Width, markerBounds.Height);
        var inset = minimumDimension * PlusInsetRatio;
        var halfThickness = minimumDimension * PlusHalfThicknessRatio;
        var centerX = markerBounds.Left + (markerBounds.Width / 2d);
        var centerY = markerBounds.Top + (markerBounds.Height / 2d);
        var left = markerBounds.Left + inset;
        var right = markerBounds.Right - inset;
        var top = markerBounds.Top + inset;
        var bottom = markerBounds.Bottom - inset;

        return
        [
            new PointD(centerX - halfThickness, top),
            new PointD(centerX + halfThickness, top),
            new PointD(centerX + halfThickness, centerY - halfThickness),
            new PointD(right, centerY - halfThickness),
            new PointD(right, centerY + halfThickness),
            new PointD(centerX + halfThickness, centerY + halfThickness),
            new PointD(centerX + halfThickness, bottom),
            new PointD(centerX - halfThickness, bottom),
            new PointD(centerX - halfThickness, centerY + halfThickness),
            new PointD(left, centerY + halfThickness),
            new PointD(left, centerY - halfThickness),
            new PointD(centerX - halfThickness, centerY - halfThickness),
        ];
    }
}
