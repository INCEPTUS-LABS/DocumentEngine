namespace Inceptus.DocumentEngine.Canvas2D.Rendering;

/// <summary>
/// Describes one Canvas2D surface in CSS pixels and its backing-store scale.
/// </summary>
public readonly record struct Canvas2DSurfaceSize
{
    public Canvas2DSurfaceSize(
        double cssWidth,
        double cssHeight,
        double devicePixelRatio)
    {
        if (!double.IsFinite(cssWidth) || cssWidth <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cssWidth), cssWidth, "CSS width must be finite and greater than zero.");
        }

        if (!double.IsFinite(cssHeight) || cssHeight <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cssHeight), cssHeight, "CSS height must be finite and greater than zero.");
        }

        if (!double.IsFinite(devicePixelRatio) || devicePixelRatio <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(devicePixelRatio),
                devicePixelRatio,
                "Device-pixel ratio must be finite and greater than zero.");
        }

        CssWidth = cssWidth;
        CssHeight = cssHeight;
        DevicePixelRatio = devicePixelRatio;
    }

    public double CssWidth { get; }

    public double CssHeight { get; }

    public double DevicePixelRatio { get; }
}
