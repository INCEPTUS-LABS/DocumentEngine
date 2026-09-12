namespace Inceptus.DocumentEngine.Canvas2D.Rendering;

/// <summary>
/// Stable diagnostics produced by Canvas2D graphics execution.
/// </summary>
public static class Canvas2DRendererDiagnosticCodes
{
    public const string NotInitialized = "CANVAS2D_RENDERER_NOT_INITIALIZED";
    public const string AlreadyInitialized = "CANVAS2D_RENDERER_ALREADY_INITIALIZED";
    public const string Disposed = "CANVAS2D_RENDERER_DISPOSED";
    public const string Cancelled = "CANVAS2D_RENDERER_CANCELLED";
    public const string InitializationFailed = "CANVAS2D_RENDERER_INITIALIZATION_FAILED";
    public const string CanvasNotFound = "CANVAS2D_RENDERER_CANVAS_NOT_FOUND";
    public const string ContextUnavailable = "CANVAS2D_RENDERER_CONTEXT_UNAVAILABLE";
    public const string ResizeFailed = "CANVAS2D_RENDERER_RESIZE_FAILED";
    public const string InvalidScene = "CANVAS2D_RENDERER_INVALID_SCENE";
    public const string MissingImageResource = "CANVAS2D_RENDERER_MISSING_IMAGE_RESOURCE";
    public const string ImageLoadFailed = "CANVAS2D_RENDERER_IMAGE_LOAD_FAILED";
    public const string RenderingFailed = "CANVAS2D_RENDERER_RENDERING_FAILED";
    public const string MeasurementFailed = "CANVAS2D_RENDERER_MEASUREMENT_FAILED";
    public const string FontUnavailable = "CANVAS2D_RENDERER_FONT_UNAVAILABLE";
    public const string BrowserInteropFailed = "CANVAS2D_RENDERER_BROWSER_INTEROP_FAILED";
    public const string ResourceDisposalFailed = "CANVAS2D_RENDERER_RESOURCE_DISPOSAL_FAILED";
}
