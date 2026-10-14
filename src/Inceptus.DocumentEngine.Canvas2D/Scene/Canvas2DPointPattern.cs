using Inceptus.DocumentEngine.Contracts.EditorState;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

/// <summary>
/// Compact, non-interactive background primitive. All culling and sampling are managed;
/// the browser only paints the supplied rows and columns through the Scene transform.
/// </summary>
internal sealed record Canvas2DPointPattern(double X, double Y, double Step, int Columns, int Rows,
    double Radius, string Fill)
{
    internal const int MaximumPoints = 8192;

    internal static Canvas2DPointPattern? Create(GridSettings? settings, ViewportSnapshot viewport)
    {
        if (settings is not { ShowGrid: true } || viewport.VisibleDocumentRegion is not { } visible)
            return null;
        var step = settings.PointDistance;
        // Use an integer power-of-two multiple, anchored at zero. This is presentation
        // sampling only; GridGeometry always uses the original PointDistance.
        var minimumStep = Math.Max(8d / viewport.Zoom,
            Math.Max(visible.Width, visible.Height) / 80d);
        if (step < minimumStep)
        {
            var exponent = Math.Ceiling(Math.Log2(minimumStep) - Math.Log2(step));
            step = Math.ScaleB(step, (int)Math.Min(2097, exponent));
        }
        if (!double.IsFinite(step) || step <= 0d) return null;
        var firstX = Math.Ceiling(visible.Left / step);
        var firstY = Math.Ceiling(visible.Top / step);
        var lastX = Math.Floor(visible.Right / step);
        var lastY = Math.Floor(visible.Bottom / step);
        var columns = lastX - firstX + 1d;
        var rows = lastY - firstY + 1d;
        if (!double.IsFinite(columns) || !double.IsFinite(rows) || columns <= 0 || rows <= 0 ||
            columns * rows > MaximumPoints || !double.IsFinite(firstX * step) || !double.IsFinite(firstY * step))
            return null;
        return new(firstX * step, firstY * step, step, (int)columns, (int)rows,
            0.85d / viewport.Zoom, "#94a3b8");
    }
}
