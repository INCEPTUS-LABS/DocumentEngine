using System.Collections.Immutable;

namespace Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;

/// <summary>
/// One internal Canvas2D browser-execution seam. It is not a renderer or backend extension point.
/// </summary>
internal interface ICanvas2DRenderExecution : IAsyncDisposable
{
    ValueTask<Canvas2DInteropOperationResult> InitializeAsync(
        string canvasElementId,
        Canvas2DSurfaceSize surfaceSize,
        ImmutableSortedDictionary<string, string> imageResources,
        ImmutableArray<Canvas2DFontResource> fontResources,
        string? defaultFontFamily);

    ValueTask<Canvas2DInteropOperationResult> ResizeAsync(
        Canvas2DSurfaceSize surfaceSize);

    ValueTask<Canvas2DInteropOperationResult> RenderAsync(
        Canvas2DRenderFrame frame);

    ValueTask<Canvas2DTextMeasurementInteropResult> MeasureTextAsync(
        Canvas2DTextMeasurementRequestData request);
}
