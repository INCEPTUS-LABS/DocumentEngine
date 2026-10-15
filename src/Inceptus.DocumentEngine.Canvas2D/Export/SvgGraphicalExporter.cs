using System.Globalization;
using System.Text;
using System.Xml;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.Export;

/// <summary>Native vector serialization. No browser, model validation or rasterization is involved.</summary>
public static class SvgGraphicalExporter
{
    public static GraphicalExportResult Export(PreparedGraphicalScene scene,
        GraphicalExportResources? resources = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        resources ??= new();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in scene.Items.Where(i => i.Geometry.Kind == Canvas2DSceneGeometryKind.Image))
                if (!resources.Images.TryGetValue(item.Geometry.Content!, out var uri) || !ValidDataUri(uri, false))
                    return GraphicalExportResult.Failure("GRAPHICAL_EXPORT_IMAGE", "An image requires an explicitly embedded PNG, JPEG or WebP resource.");
            if (resources.Fonts.Values.Any(uri => !ValidDataUri(uri, true)))
                return GraphicalExportResult.Failure("GRAPHICAL_EXPORT_FONT", "An embedded font has an unsupported or invalid data format.");

            using var stream = new MemoryStream();
            using (var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }))
            {
                xml.WriteStartDocument();
                xml.WriteStartElement("svg", "http://www.w3.org/2000/svg");
                Attribute(xml, "width", scene.Bounds.Width);
                Attribute(xml, "height", scene.Bounds.Height);
                xml.WriteAttributeString("viewBox", $"{N(scene.Bounds.X)} {N(scene.Bounds.Y)} {N(scene.Bounds.Width)} {N(scene.Bounds.Height)}");
                xml.WriteAttributeString("version", "1.1");
                xml.WriteStartElement("defs");
                if (!resources.Fonts.IsEmpty)
                {
                    xml.WriteStartElement("style");
                    foreach (var (family, uri) in resources.Fonts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                        xml.WriteString($"@font-face{{font-family:'{CssEscape(family)}';src:url('{uri}');font-weight:400;font-style:normal;}}");
                    xml.WriteEndElement();
                }
                for (var i = 0; i < scene.Items.Length; i++)
                {
                    if (scene.Items[i].Clip is not { } clip) continue;
                    xml.WriteStartElement("clipPath");
                    xml.WriteAttributeString("id", $"clip{i}");
                    xml.WriteAttributeString("clipPathUnits", "userSpaceOnUse");
                    Rectangle(xml, clip);
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
                xml.WriteStartElement("g");
                xml.WriteAttributeString("fill", "white");
                Rectangle(xml, scene.Bounds);
                xml.WriteEndElement();
                for (var i = 0; i < scene.Items.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = scene.Items[i];
                    xml.WriteStartElement("g");
                    if (item.Clip is not null) xml.WriteAttributeString("clip-path", $"url(#clip{i})");
                    xml.WriteStartElement("g");
                    var m = item.Transform;
                    xml.WriteAttributeString("transform", $"matrix({N(m.M11)} {N(m.M12)} {N(m.M21)} {N(m.M22)} {N(m.OffsetX)} {N(m.OffsetY)})");
                    // Per-paint alpha matches Canvas globalAlpha, including overlapping fill and stroke.
                    xml.WriteAttributeString("fill", item.Style.Fill ?? "none");
                    xml.WriteAttributeString("stroke", item.Style.Stroke ?? "none");
                    Attribute(xml, "fill-opacity", item.Style.Opacity);
                    Attribute(xml, "stroke-opacity", item.Style.Opacity);
                    Attribute(xml, "stroke-width", item.Style.StrokeWidth);
                    xml.WriteAttributeString("stroke-linecap", "butt");
                    xml.WriteAttributeString("stroke-linejoin", "miter");
                    xml.WriteAttributeString("stroke-miterlimit", "10");
                    if (!item.Style.DashPattern.IsEmpty)
                        xml.WriteAttributeString("stroke-dasharray", string.Join(" ", item.Style.DashPattern.Select(N)));
                    WriteGeometry(xml, item, resources);
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
                xml.WriteEndDocument();
            }
            return GraphicalExportResult.Success(GraphicalExportFormat.Svg, stream.ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return GraphicalExportResult.Cancelled(); }
        catch (ArgumentException) { return GraphicalExportResult.Failure("GRAPHICAL_EXPORT_XML", "The scene contains text that cannot be represented in XML."); }
        catch (XmlException) { return GraphicalExportResult.Failure("GRAPHICAL_EXPORT_XML", "The scene cannot be represented as a valid SVG document."); }
    }

    private static void WriteGeometry(XmlWriter xml, Canvas2DSceneItem item, GraphicalExportResources resources)
    {
        var g = item.Geometry;
        var b = g.Bounds;
        switch (g.Kind)
        {
            case Canvas2DSceneGeometryKind.Rectangle:
                Rectangle(xml, b);
                break;
            case Canvas2DSceneGeometryKind.Ellipse:
                xml.WriteStartElement("ellipse");
                Attribute(xml, "cx", b.X + b.Width / 2); Attribute(xml, "cy", b.Y + b.Height / 2);
                Attribute(xml, "rx", b.Width / 2); Attribute(xml, "ry", b.Height / 2);
                xml.WriteEndElement();
                break;
            case Canvas2DSceneGeometryKind.Path:
                xml.WriteStartElement("path");
                xml.WriteAttributeString("d", "M" + string.Join(" L", g.Points.Select(p => $"{N(p.X)} {N(p.Y)}")) + (g.IsClosed ? " Z" : ""));
                xml.WriteEndElement();
                break;
            case Canvas2DSceneGeometryKind.Text:
                xml.WriteStartElement("text");
                Attribute(xml, "x", g.TextAnchor.X); Attribute(xml, "y", g.TextAnchor.Y);
                xml.WriteAttributeString("font-family", $"'{CssEscape(item.Style.FontFamily ?? resources.DefaultFontFamily)}', sans-serif");
                Attribute(xml, "font-size", item.Style.FontSize);
                xml.WriteAttributeString("font-weight", "400");
                xml.WriteAttributeString("text-anchor", g.TextAlignment switch { Canvas2DTextAlignment.Center => "middle", Canvas2DTextAlignment.End => "end", _ => "start" });
                xml.WriteAttributeString("dominant-baseline", g.TextBaseline switch { Canvas2DTextBaseline.Middle => "central", Canvas2DTextBaseline.Bottom => "text-after-edge", _ => "text-before-edge" });
                xml.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                xml.WriteString(g.Content);
                xml.WriteEndElement();
                break;
            case Canvas2DSceneGeometryKind.Image:
                xml.WriteStartElement("image");
                Bounds(xml, b);
                Attribute(xml, "opacity", item.Style.Opacity);
                xml.WriteAttributeString("preserveAspectRatio", "none");
                xml.WriteAttributeString("href", "http://www.w3.org/1999/xlink", resources.Images[g.Content!]);
                xml.WriteEndElement();
                break;
        }
    }

    internal static bool ValidDataUri(string uri, bool font)
    {
        if (uri.Length > 24 * 1024 * 1024) return false;
        var comma = uri.IndexOf(',');
        if (comma < 0) return false;
        var header = uri[..comma];
        var allowed = font
            ? new[] { "data:font/ttf;base64", "data:font/otf;base64", "data:font/woff;base64", "data:font/woff2;base64" }
            : new[] { "data:image/png;base64", "data:image/jpeg;base64", "data:image/webp;base64" };
        if (!allowed.Contains(header, StringComparer.Ordinal)) return false;
        try
        {
            var bytes = Convert.FromBase64String(uri[(comma + 1)..]);
            if (bytes.Length < 12) return false;
            return header switch
            {
                "data:image/png;base64" => bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                "data:image/jpeg;base64" => bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
                "data:image/webp;base64" => bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
                "data:font/ttf;base64" => bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 0, 1, 0, 0 }),
                "data:font/otf;base64" => bytes.AsSpan(0, 4).SequenceEqual("OTTO"u8),
                "data:font/woff;base64" => bytes.AsSpan(0, 4).SequenceEqual("wOFF"u8),
                "data:font/woff2;base64" => bytes.AsSpan(0, 4).SequenceEqual("wOF2"u8),
                _ => false,
            };
        }
        catch (FormatException) { return false; }
    }

    private static string CssEscape(string value) => string.Concat(value.EnumerateRunes().Select(r => $"\\{r.Value:x} "));
    private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static void Attribute(XmlWriter xml, string name, double value) => xml.WriteAttributeString(name, N(value));
    private static void Bounds(XmlWriter xml, RectD b)
    { Attribute(xml, "x", b.X); Attribute(xml, "y", b.Y); Attribute(xml, "width", b.Width); Attribute(xml, "height", b.Height); }
    private static void Rectangle(XmlWriter xml, RectD b) { xml.WriteStartElement("rect"); Bounds(xml, b); xml.WriteEndElement(); }
}
