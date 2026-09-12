namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

/// <summary>
/// Defines the deterministic version-one zoom choices exposed by the modeler toolbar.
/// </summary>
internal static class DocumentCanvasZoomPolicy
{
    internal const int MinimumPercentage = 25;
    internal const int MaximumPercentage = 400;
    internal const int StepPercentage = 10;
    internal const int ActualSizePercentage = 100;

    internal static double ZoomIn(double currentZoom)
    {
        Validate(currentZoom);
        if (currentZoom >= ToZoom(MaximumPercentage))
        {
            return ToZoom(MaximumPercentage);
        }

        return ToZoom(Math.Clamp(
            checked(ToPercentage(currentZoom) + StepPercentage),
            MinimumPercentage,
            MaximumPercentage));
    }

    internal static double ZoomOut(double currentZoom)
    {
        Validate(currentZoom);
        if (currentZoom <= ToZoom(MinimumPercentage))
        {
            return ToZoom(MinimumPercentage);
        }

        if (currentZoom > ToZoom(MaximumPercentage))
        {
            return ToZoom(MaximumPercentage);
        }

        return ToZoom(Math.Clamp(
            checked(ToPercentage(currentZoom) - StepPercentage),
            MinimumPercentage,
            MaximumPercentage));
    }

    internal static double ActualSize => ToZoom(ActualSizePercentage);

    internal static bool CanZoomIn(double currentZoom) =>
        currentZoom < ToZoom(MaximumPercentage);

    internal static bool CanZoomOut(double currentZoom) =>
        currentZoom > ToZoom(MinimumPercentage);

    internal static bool IsActualSize(double currentZoom) =>
        currentZoom.Equals(ActualSize);

    internal static int ToPercentage(double zoom)
    {
        Validate(zoom);

        return checked((int)Math.Round(
            zoom * ActualSizePercentage,
            MidpointRounding.AwayFromZero));
    }

    private static double ToZoom(int percentage) =>
        percentage / (double)ActualSizePercentage;

    private static void Validate(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom <= 0d ||
            zoom > int.MaxValue / (double)ActualSizePercentage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(zoom),
                zoom,
                "Zoom must be finite, positive, and representable as an integer percentage.");
        }
    }
}
