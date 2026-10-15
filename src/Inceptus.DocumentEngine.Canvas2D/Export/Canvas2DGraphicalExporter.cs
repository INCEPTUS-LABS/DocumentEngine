using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.Canvas2D.Export;

/// <summary>
/// Browser export service. Uses a private resource lifetime and canvas, never the editor renderer.
/// Configured same-origin font/image assets are embedded; remote URLs and SVG image assets are rejected.
/// </summary>
public sealed class Canvas2DGraphicalExporter(IJSRuntime javascript, Canvas2DRendererConfiguration? configuration = null)
{
    private readonly IJSRuntime _javascript = javascript ?? throw new ArgumentNullException(nameof(javascript));
    private readonly Canvas2DRendererConfiguration _configuration = configuration ?? Canvas2DRendererConfiguration.Default;

    public async ValueTask<GraphicalExportResult> ExportAsync(EditingSessionPresentationCapture capture,
        GraphicalExportFormat format, GraphicalExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var preparation = GraphicalExportPreparer.Prepare(capture, options, cancellationToken);
            if (!preparation.Succeeded) return new(GraphicalExportStatus.Failed, null, preparation.Diagnostics);
            return await ExportAsync(preparation.Scene!, format, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return GraphicalExportResult.Cancelled(); }
    }

    public async ValueTask<GraphicalExportResult> ExportAsync(PreparedGraphicalScene scene,
        GraphicalExportFormat format, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!Enum.IsDefined(format)) return GraphicalExportResult.Failure("GRAPHICAL_EXPORT_FORMAT", "The requested graphical format is unsupported.");
        var width = Math.Ceiling(scene.Bounds.Width * scene.Options.RasterScale);
        var height = Math.Ceiling(scene.Bounds.Height * scene.Options.RasterScale);
        if (format == GraphicalExportFormat.Png && (!double.IsFinite(width) || !double.IsFinite(height) ||
            width > GraphicalExportOptions.MaximumPixelDimension || height > GraphicalExportOptions.MaximumPixelDimension ||
            width * height > GraphicalExportOptions.MaximumPixelCount))
            return GraphicalExportResult.Failure("GRAPHICAL_EXPORT_SIZE", "Requested PNG exceeds 16,384 pixels per side or 32 megapixels. Choose a smaller scale explicitly.");
        IJSObjectReference? module = null;
        IJSObjectReference? operation = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            module = await _javascript.InvokeAsync<IJSObjectReference>("import", cancellationToken,
                "./_content/Inceptus.DocumentEngine.Canvas2D/inceptus.canvas2d.js").ConfigureAwait(false);
            operation = await module.InvokeAsync<IJSObjectReference>("createGraphicalExport", cancellationToken).ConfigureAwait(false);
            var imageReferences = scene.Items.Where(i => i.Geometry.Kind == Canvas2DSceneGeometryKind.Image)
                .Select(i => i.Geometry.Content!).Distinct(StringComparer.Ordinal).ToArray();
            if (imageReferences.Any(reference => !_configuration.ImageResources.ContainsKey(reference)))
                return GraphicalExportResult.Failure("GRAPHICAL_EXPORT_IMAGE", "The scene references an image without a configured export resource.");
            var families = scene.Items.Where(i => i.Geometry.Kind == Canvas2DSceneGeometryKind.Text)
                .Select(i => i.Style.FontFamily ?? _configuration.DefaultFontFamily ?? "sans-serif").Distinct(StringComparer.Ordinal).ToArray();
            var fonts = _configuration.FontResources.Where(f => families.Contains(f.FontFamily, StringComparer.Ordinal) &&
                f.FontWeight == 400 && f.FontStyle == Contracts.Text.TextFontStyle.Normal).ToArray();
            var resources = await operation.InvokeAsync<ExportResourceData>("resources", cancellationToken,
                imageReferences.Select(reference => new Canvas2DImageResourceData(reference, _configuration.ImageResources[reference])).ToArray(),
                fonts.Select(font => new { font.FontFamily, font.SourceUri }).ToArray()).ConfigureAwait(false);
            var embedded = new GraphicalExportResources
            {
                Images = resources.Images.ToImmutableDictionary(StringComparer.Ordinal),
                Fonts = resources.Fonts.ToImmutableDictionary(StringComparer.Ordinal),
                DefaultFontFamily = _configuration.DefaultFontFamily ?? "sans-serif",
            };
            // Validate resource payloads once for both formats, including safe standalone SVG references.
            if (embedded.Images.Values.Any(uri => !SvgGraphicalExporter.ValidDataUri(uri, false)) ||
                embedded.Fonts.Values.Any(uri => !SvgGraphicalExporter.ValidDataUri(uri, true)))
                return GraphicalExportResult.Failure("GRAPHICAL_EXPORT_RESOURCE", "A graphical resource has an unsupported or invalid encoding.");
            if (format == GraphicalExportFormat.Svg) return SvgGraphicalExporter.Export(scene, embedded, cancellationToken);

            var frame = new Canvas2DRenderFrame(Matrix2D.CreateTranslation(-scene.Bounds.X, -scene.Bounds.Y),
                scene.Items.Select(item => new Canvas2DRenderItem(item)).ToArray(), 1, 1);
            var bytes = await operation.InvokeAsync<byte[]>("png", cancellationToken, frame,
                (int)width, (int)height, scene.Options.RasterScale, embedded).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                return GraphicalExportResult.Failure("GRAPHICAL_EXPORT_ENCODING", "The browser did not produce a valid PNG.");
            return GraphicalExportResult.Success(format, bytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return GraphicalExportResult.Cancelled(); }
        catch (JSException)
        { return GraphicalExportResult.Failure("GRAPHICAL_EXPORT_BROWSER", "Graphical export failed while loading same-origin resources, allocating the canvas, or encoding the image. Check resource availability and requested dimensions."); }
        catch (ObjectDisposedException)
        { return GraphicalExportResult.Failure("GRAPHICAL_EXPORT_UNAVAILABLE", "The browser export context is no longer available."); }
        finally
        {
            if (operation is not null)
            {
                try { await operation.InvokeVoidAsync("dispose").ConfigureAwait(false); await operation.DisposeAsync().ConfigureAwait(false); }
                catch (JSException) { /* A disconnected browser already owns cleanup. */ }
                catch (ObjectDisposedException) { }
            }
            if (module is not null)
            {
                try { await module.DisposeAsync().ConfigureAwait(false); }
                catch (JSException) { }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private sealed class ExportResourceData
    {
        public Dictionary<string, string> Images { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Fonts { get; set; } = new(StringComparer.Ordinal);
    }
}
