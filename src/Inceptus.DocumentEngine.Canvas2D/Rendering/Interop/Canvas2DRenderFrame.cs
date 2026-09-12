using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;

/// <summary>
/// Private, complete-frame transport data. It is not a public rendering model.
/// </summary>
internal sealed class Canvas2DRenderFrame
{
    internal Canvas2DRenderFrame(
        Matrix2D viewportTransform,
        IEnumerable<Canvas2DRenderItem> items)
    {
        ViewportTransform = Canvas2DMatrixData.From(viewportTransform);
        Items = items.ToArray();
    }

    public Canvas2DMatrixData ViewportTransform { get; }

    public Canvas2DRenderItem[] Items { get; }
}

internal sealed class Canvas2DRenderItem
{
    internal Canvas2DRenderItem(Canvas2DSceneItem item)
    {
        Id = item.Id.Value;
        Layer = (int)item.Layer;
        ZIndex = item.ZIndex;
        GeometryKind = (int)item.Geometry.Kind;
        GeometryBounds = Canvas2DRectData.From(item.Geometry.Bounds);
        Points = item.Geometry.Points
            .Select(static point => Canvas2DPointData.From(point))
            .ToArray();
        Content = item.Geometry.Content;
        IsClosed = item.Geometry.IsClosed;
        TextAnchor = Canvas2DPointData.From(item.Geometry.TextAnchor);
        TextAlignment = (int)item.Geometry.TextAlignment;
        TextBaseline = (int)item.Geometry.TextBaseline;
        Transform = Canvas2DMatrixData.From(item.Transform);
        Clip = item.Clip is null ? null : Canvas2DRectData.From(item.Clip.Value);
        Fill = item.Style.Fill;
        Stroke = item.Style.Stroke;
        StrokeWidth = item.Style.StrokeWidth;
        DashPattern = item.Style.DashPattern.ToArray();
        Opacity = item.Style.Opacity;
        FontFamily = item.Style.FontFamily;
        FontSize = item.Style.FontSize;
        IsVisible = item.IsVisible;
    }

    public string Id { get; }

    public int Layer { get; }

    public int ZIndex { get; }

    public int GeometryKind { get; }

    public Canvas2DRectData GeometryBounds { get; }

    public Canvas2DPointData[] Points { get; }

    public string? Content { get; }

    public bool IsClosed { get; }

    public Canvas2DPointData TextAnchor { get; }

    public int TextAlignment { get; }

    public int TextBaseline { get; }

    public Canvas2DMatrixData Transform { get; }

    public Canvas2DRectData? Clip { get; }

    public string? Fill { get; }

    public string? Stroke { get; }

    public double StrokeWidth { get; }

    public double[] DashPattern { get; }

    public double Opacity { get; }

    public string? FontFamily { get; }

    public double FontSize { get; }

    public bool IsVisible { get; }
}

internal sealed record Canvas2DPointData(double X, double Y)
{
    internal static Canvas2DPointData From(PointD point) => new(point.X, point.Y);
}

internal sealed record Canvas2DRectData(double X, double Y, double Width, double Height)
{
    internal static Canvas2DRectData From(RectD bounds) =>
        new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
}

internal sealed record Canvas2DMatrixData(
    double M11,
    double M12,
    double M21,
    double M22,
    double OffsetX,
    double OffsetY)
{
    internal static Canvas2DMatrixData From(Matrix2D matrix) =>
        new(
            matrix.M11,
            matrix.M12,
            matrix.M21,
            matrix.M22,
            matrix.OffsetX,
            matrix.OffsetY);
}

internal sealed record Canvas2DSurfaceData(
    double CssWidth,
    double CssHeight,
    double DevicePixelRatio)
{
    internal static Canvas2DSurfaceData From(Canvas2DSurfaceSize surfaceSize) =>
        new(
            surfaceSize.CssWidth,
            surfaceSize.CssHeight,
            surfaceSize.DevicePixelRatio);
}

internal sealed record Canvas2DImageResourceData(string Reference, string Uri);

internal sealed record Canvas2DFontResourceData(
    string FontIdentity,
    string FontVersion,
    string FontFamily,
    string SourceUri,
    int FontWeight,
    string FontStyle);

internal sealed class Canvas2DTextMeasurementRequestData
{
    internal Canvas2DTextMeasurementRequestData(TextMeasurementRequest request)
    {
        Text = request.Text;
        FontFamily = request.FontFamily;
        FontIdentity = request.FontIdentity;
        FontVersion = request.FontVersion;
        FontSize = request.FontSize;
        LineHeight = request.LineHeight;
        FontWeight = request.FontWeight;
        FontStyle = request.FontStyle switch
        {
            TextFontStyle.Normal => "normal",
            TextFontStyle.Italic => "italic",
            TextFontStyle.Oblique => "oblique",
            _ => throw new ArgumentOutOfRangeException(nameof(request), "The font style must be defined."),
        };
        Locale = request.Locale;
        Direction = request.Direction switch
        {
            TextDirection.LeftToRight => "ltr",
            TextDirection.RightToLeft => "rtl",
            _ => throw new ArgumentOutOfRangeException(nameof(request), "The text direction must be defined."),
        };
        WritingMode = (int)request.WritingMode;
        Scale = request.Scale;
    }

    public string Text { get; }

    public string FontFamily { get; }

    public string FontIdentity { get; }

    public string FontVersion { get; }

    public double FontSize { get; }

    public double LineHeight { get; }

    public int FontWeight { get; }

    public string FontStyle { get; }

    public string Locale { get; }

    public string Direction { get; }

    public int WritingMode { get; }

    public double Scale { get; }

}

internal sealed class Canvas2DTextMeasurementInteropResult : Canvas2DInteropOperationResult
{
    public double Width { get; set; }

    public double Ascent { get; set; }

    public double Descent { get; set; }

    public double LineHeight { get; set; }

    public double BoundingX { get; set; }

    public double BoundingY { get; set; }

    public double BoundingWidth { get; set; }

    public double BoundingHeight { get; set; }

    public string? ResolvedFontIdentity { get; set; }
}
