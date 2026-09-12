using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

internal sealed class Canvas2DRendererTestExecution : ICanvas2DRenderExecution
{
    internal List<string> Calls { get; } = [];

    internal Canvas2DRenderFrame? LastFrame { get; private set; }

    internal Canvas2DSurfaceSize? LastSurfaceSize { get; private set; }

    internal ImmutableSortedDictionary<string, string>? LastImageResources { get; private set; }

    internal ImmutableArray<Canvas2DFontResource> LastFontResources { get; private set; } = [];

    internal string? LastDefaultFontFamily { get; private set; }

    internal Canvas2DTextMeasurementRequestData? LastMeasurementRequest { get; private set; }

    internal Canvas2DInteropOperationResult InitializeResult { get; set; } = Success();

    internal Canvas2DInteropOperationResult ResizeResult { get; set; } = Success();

    internal Canvas2DInteropOperationResult RenderResult { get; set; } = Success();

    internal Canvas2DTextMeasurementInteropResult MeasurementResult { get; set; } =
        SuccessfulMeasurement();

    internal Exception? InitializeException { get; set; }

    internal Exception? ResizeException { get; set; }

    internal Exception? RenderException { get; set; }

    internal Exception? MeasurementException { get; set; }

    internal TaskCompletionSource? RenderEnteredSignal { get; set; }

    internal TaskCompletionSource? RenderReleaseSignal { get; set; }

    internal Exception? DisposeException { get; set; }

    internal bool IsDisposed { get; private set; }

    internal int DisposeCount { get; private set; }

    public ValueTask<Canvas2DInteropOperationResult> InitializeAsync(
        string canvasElementId,
        Canvas2DSurfaceSize surfaceSize,
        ImmutableSortedDictionary<string, string> imageResources,
        ImmutableArray<Canvas2DFontResource> fontResources,
        string? defaultFontFamily)
    {
        Calls.Add($"initialize:{canvasElementId}");
        LastSurfaceSize = surfaceSize;
        LastImageResources = imageResources;
        LastFontResources = fontResources;
        LastDefaultFontFamily = defaultFontFamily;
        if (InitializeException is not null)
        {
            throw InitializeException;
        }

        return ValueTask.FromResult(InitializeResult);
    }

    public ValueTask<Canvas2DInteropOperationResult> ResizeAsync(Canvas2DSurfaceSize surfaceSize)
    {
        Calls.Add("resize");
        LastSurfaceSize = surfaceSize;
        if (ResizeException is not null)
        {
            throw ResizeException;
        }

        return ValueTask.FromResult(ResizeResult);
    }

    public async ValueTask<Canvas2DInteropOperationResult> RenderAsync(Canvas2DRenderFrame frame)
    {
        Calls.Add("render");
        LastFrame = frame;
        RenderEnteredSignal?.TrySetResult();
        if (RenderReleaseSignal is not null)
        {
            await RenderReleaseSignal.Task;
        }

        if (RenderException is not null)
        {
            throw RenderException;
        }

        return RenderResult;
    }

    public ValueTask<Canvas2DTextMeasurementInteropResult> MeasureTextAsync(
        Canvas2DTextMeasurementRequestData request)
    {
        Calls.Add("measure");
        LastMeasurementRequest = request;
        if (MeasurementException is not null)
        {
            throw MeasurementException;
        }

        return ValueTask.FromResult(MeasurementResult);
    }

    public ValueTask DisposeAsync()
    {
        Calls.Add("dispose");
        DisposeCount++;
        IsDisposed = true;
        if (DisposeException is not null)
        {
            throw DisposeException;
        }

        return ValueTask.CompletedTask;
    }

    internal static Canvas2DInteropOperationResult Success() => new() { Succeeded = true };

    internal static Canvas2DInteropOperationResult Failure(string code, string? source = null) =>
        new() { Succeeded = false, Code = code, SourceIdentity = source };

    internal static Canvas2DTextMeasurementInteropResult SuccessfulMeasurement() => new()
    {
        Succeeded = true,
        Width = 80d,
        Ascent = 10d,
        Descent = 3d,
        LineHeight = 18d,
        BoundingX = -1d,
        BoundingY = -10d,
        BoundingWidth = 82d,
        BoundingHeight = 14d,
        ResolvedFontIdentity = "test:font:inter@4.0",
    };
}
