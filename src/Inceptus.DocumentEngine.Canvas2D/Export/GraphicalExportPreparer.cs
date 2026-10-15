using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.Export;

public static partial class GraphicalExportPreparer
{
    /// <summary>Consumes exactly the captured presentation; does not traverse scopes or run validation/layout/routing.</summary>
    public static GraphicalExportPreparationResult Prepare(EditingSessionPresentationCapture capture,
        GraphicalExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        options ??= new();
        cancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(options.Margin) || options.Margin < 0 ||
            !double.IsFinite(options.RasterScale) || options.RasterScale <= 0)
            return Failure("GRAPHICAL_EXPORT_OPTIONS", "Margin must be finite and non-negative; raster scale must be finite and positive.");

        var document = capture.Document;
        var scene = capture.Scene;
        if (scene.DocumentId != document.DocumentId || scene.SourceRevision != document.Revision ||
            scene.PresentationContext != capture.PresentationContext ||
            capture.ProjectedGraph.DocumentId != document.DocumentId || capture.ProjectedGraph.SourceRevision != document.Revision ||
            capture.LayoutResult.DocumentId != document.DocumentId || capture.LayoutResult.SourceRevision != document.Revision ||
            capture.RoutingResult.DocumentId != document.DocumentId || capture.RoutingResult.SourceRevision != document.Revision)
            return Failure("GRAPHICAL_EXPORT_CAPTURE", "Export requires a coherent document revision and active presentation capture.");

        var items = ImmutableArray.CreateBuilder<Canvas2DSceneItem>();
        RectD? bounds = null;
        try
        {
            foreach (var item in scene.Items.OrderBy(item => item.Layer).ThenBy(item => item.ZIndex)
                         .ThenBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!item.IsVisible || item.Style.Opacity == 0 || Canvas2DSceneExportMetadata.IsExcluded(item) ||
                    (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0) continue;
                if (!SafePaint(item.Style.Fill) || !SafePaint(item.Style.Stroke))
                    return Failure("GRAPHICAL_EXPORT_PAINT", "A scene paint is not a self-contained color.");
                if (item.Style.Stroke is not null && item.Style.StrokeWidth == 0)
                    return Failure("GRAPHICAL_EXPORT_STROKE", "A zero-width canvas stroke has no portable graphical representation.");
                if (item.Geometry.Kind != Canvas2DSceneGeometryKind.Image && item.Style.Fill is null && item.Style.Stroke is null) continue;
                var painted = GraphicalSceneBounds.Calculate(item);
                if (painted is null) continue;
                items.Add(item);
                bounds = bounds is { } previous ? GraphicalSceneBounds.Union(previous, painted.Value) : painted;
            }
            var content = bounds ?? new RectD(0, 0, 1, 1);
            var margin = options.Margin;
            return new(new(items.ToImmutable(), new(content.X - margin, content.Y - margin,
                Math.Max(1, content.Width + 2 * margin), Math.Max(1, content.Height + 2 * margin)), options), []);
        }
        catch (ArgumentException)
        {
            return Failure("GRAPHICAL_EXPORT_GEOMETRY", "The scene exceeds finite graphical coordinate limits.");
        }
    }

    // SVG paints can otherwise contain URL references, CSS variables and executable resource dependencies.
    private static bool SafePaint(string? value) => value is null || ColorPattern().IsMatch(value);

    [GeneratedRegex(@"\A(?:#[0-9a-fA-F]{3,8}|[a-zA-Z]+|(?:rgb|rgba|hsl|hsla)\([0-9.,%+\- /]+\))\z", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();

    private static GraphicalExportPreparationResult Failure(string code, string message) =>
        new(null, [new(code, DiagnosticSeverity.Error, message)]);
}
