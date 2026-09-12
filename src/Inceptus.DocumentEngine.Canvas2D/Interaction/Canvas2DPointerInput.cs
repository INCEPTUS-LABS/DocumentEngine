using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

/// <summary>
/// One normalized browser-pointer sample expressed in canvas-relative CSS coordinates.
/// </summary>
public readonly record struct Canvas2DPointerInput
{
    public Canvas2DPointerInput(
        long pointerId,
        PointD cssPoint,
        bool isPrimary = true,
        int button = 0,
        int buttons = 0,
        bool controlKey = false)
    {
        if (pointerId < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pointerId),
                pointerId,
                "The pointer identity must be non-negative.");
        }

        if (button < -1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(button),
                button,
                "The pointer button must use a browser PointerEvent button value.");
        }

        if (buttons < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(buttons),
                buttons,
                "The pointer buttons mask must be non-negative.");
        }

        PointerId = pointerId;
        CssPoint = cssPoint;
        IsPrimary = isPrimary;
        Button = button;
        Buttons = buttons;
        ControlKey = controlKey;
    }

    public long PointerId { get; }

    public PointD CssPoint { get; }

    public bool IsPrimary { get; }

    public int Button { get; }

    public int Buttons { get; }

    public bool ControlKey { get; }
}
