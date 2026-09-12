using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Bpmn.Scene;

/// <summary>
/// Places every specialized Task marker coherently inside the upper-left Task body.
/// </summary>
internal static class BpmnTaskMarkerPlacementPolicy
{
    private const double MarginRatio = 0.09d;
    private const double ExtentRatio = 0.28d;
    private const double StrokeWidthRatio = 0.055d;

    internal static RectD ResolveBounds(RectD taskBounds)
    {
        var minimumDimension = Math.Min(taskBounds.Width, taskBounds.Height);
        var margin = minimumDimension * MarginRatio;
        var extent = minimumDimension * ExtentRatio;
        return new RectD(
            taskBounds.Left + margin,
            taskBounds.Top + margin,
            extent,
            extent);
    }

    internal static double ResolveStrokeWidth(RectD markerBounds) =>
        Math.Min(markerBounds.Width, markerBounds.Height) * StrokeWidthRatio;
}
