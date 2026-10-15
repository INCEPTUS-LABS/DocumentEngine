using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.Export;

public enum GraphicalExportFormat { Svg, Png }
public enum GraphicalExportStatus { Succeeded, Failed, Cancelled }

/// <summary>Logical margin and raster scale, independent of the editor viewport.</summary>
public sealed record GraphicalExportOptions(double Margin = 24, double RasterScale = 2)
{
    // A bounded allocation policy, checked again by the browser before creating a canvas.
    public const int MaximumPixelDimension = 16384;
    public const long MaximumPixelCount = 32 * 1024 * 1024;
}

public sealed record GraphicalExportArtifact(string FileName, string ContentType, ImmutableArray<byte> Content);

public sealed record GraphicalExportResult(
    GraphicalExportStatus Status, GraphicalExportArtifact? Artifact, ImmutableArray<Diagnostic> Diagnostics)
{
    public bool Succeeded => Status == GraphicalExportStatus.Succeeded && Artifact is not null;

    internal static GraphicalExportResult Failure(string code, string message) =>
        new(GraphicalExportStatus.Failed, null, [new(code, DiagnosticSeverity.Error, message)]);

    internal static GraphicalExportResult Cancelled() => new(GraphicalExportStatus.Cancelled, null, []);

    internal static GraphicalExportResult Success(GraphicalExportFormat format, byte[] bytes) =>
        new(GraphicalExportStatus.Succeeded, new(format == GraphicalExportFormat.Svg ? "diagram.svg" : "diagram.png",
            format == GraphicalExportFormat.Svg ? "image/svg+xml" : "image/png", [.. bytes]), []);
}

/// <summary>Detached, ordered graphics shared by both exporters. Contains no editor state.</summary>
public sealed class PreparedGraphicalScene
{
    internal PreparedGraphicalScene(ImmutableArray<Canvas2DSceneItem> items, RectD bounds, GraphicalExportOptions options)
    { Items = items; Bounds = bounds; Options = options; }

    public ImmutableArray<Canvas2DSceneItem> Items { get; }
    public RectD Bounds { get; }
    public GraphicalExportOptions Options { get; }
}

public sealed record GraphicalExportPreparationResult(PreparedGraphicalScene? Scene, ImmutableArray<Diagnostic> Diagnostics)
{
    public bool Succeeded => Scene is not null;
}

/// <summary>
/// Explicit self-contained resources. Image values must be base64 PNG/JPEG/WebP data URIs;
/// font values must be base64 TrueType/OpenType/WOFF/WOFF2 data URIs. The caller must have embedding rights.
/// External URLs and SVG image payloads are deliberately unsupported.
/// </summary>
public sealed record GraphicalExportResources
{
    public ImmutableDictionary<string, string> Images { get; init; } = ImmutableDictionary<string, string>.Empty;
    public ImmutableDictionary<string, string> Fonts { get; init; } = ImmutableDictionary<string, string>.Empty;
    public string DefaultFontFamily { get; init; } = "sans-serif";
}
