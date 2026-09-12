using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Bpmn.Scene;

/// <summary>
/// Supplies one centered marker coordinate box for every supported BPMN intermediate event.
/// Marker-specific geometry is derived inside this box and never changes owner-node bounds.
/// </summary>
internal static class BpmnIntermediateEventMarkerPlacementPolicy
{
    private const double MarkerExtentRatio = 0.46d;
    private const double EnvelopeHeightRatio = 0.70d;
    private const double ClockInsetRatio = 0.087d;

    internal static RectD ResolveMarkerBounds(RectD eventBounds)
    {
        var extent = Math.Min(eventBounds.Width, eventBounds.Height) * MarkerExtentRatio;
        return new RectD(
            eventBounds.X + ((eventBounds.Width - extent) / 2d),
            eventBounds.Y + ((eventBounds.Height - extent) / 2d),
            extent,
            extent);
    }

    internal static RectD ResolveEnvelopeBounds(RectD markerBounds)
    {
        var height = markerBounds.Height * EnvelopeHeightRatio;
        return new RectD(
            markerBounds.X,
            markerBounds.Y + ((markerBounds.Height - height) / 2d),
            markerBounds.Width,
            height);
    }

    internal static RectD ResolveClockBounds(RectD markerBounds)
    {
        var inset = Math.Min(markerBounds.Width, markerBounds.Height) * ClockInsetRatio;
        return new RectD(
            markerBounds.X + inset,
            markerBounds.Y + inset,
            markerBounds.Width - (2d * inset),
            markerBounds.Height - (2d * inset));
    }

    internal static PointD[] ResolveSignalTriangle(RectD markerBounds) =>
    [
        new PointD(markerBounds.X + (markerBounds.Width / 2d), markerBounds.Top),
        new PointD(markerBounds.Right, markerBounds.Bottom),
        new PointD(markerBounds.Left, markerBounds.Bottom),
    ];
}
