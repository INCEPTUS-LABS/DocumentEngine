using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Text;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;

internal sealed class Canvas2DJsInterop : ICanvas2DRenderExecution
{
    private const string ModulePath =
        "./_content/Inceptus.DocumentEngine.Canvas2D/inceptus.canvas2d.js";

    private readonly IJSRuntime _jsRuntime;
    private IJSObjectReference? _module;
    private IJSObjectReference? _renderer;
    private bool _disposed;

    internal Canvas2DJsInterop(IJSRuntime jsRuntime)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        _jsRuntime = jsRuntime;
    }

    public async ValueTask<Canvas2DInteropOperationResult> InitializeAsync(
        string canvasElementId,
        Canvas2DSurfaceSize surfaceSize,
        ImmutableSortedDictionary<string, string> imageResources,
        ImmutableArray<Canvas2DFontResource> fontResources,
        string? defaultFontFamily)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            ModulePath);
        var resources = imageResources
            .Select(static entry => new Canvas2DImageResourceData(entry.Key, entry.Value))
            .ToArray();
        var fonts = fontResources
            .Select(static font => new Canvas2DFontResourceData(
                font.FontIdentity,
                font.FontVersion,
                font.FontFamily,
                font.SourceUri,
                font.FontWeight,
                font.FontStyle switch
                {
                    TextFontStyle.Normal => "normal",
                    TextFontStyle.Italic => "italic",
                    TextFontStyle.Oblique => "oblique",
                    _ => throw new ArgumentOutOfRangeException(nameof(fontResources)),
                }))
            .ToArray();

        IJSObjectReference? renderer = null;
        try
        {
            renderer = await _module.InvokeAsync<IJSObjectReference>(
                "createRenderer",
                canvasElementId);
            ArgumentNullException.ThrowIfNull(renderer);
            var result = await renderer.InvokeAsync<Canvas2DInteropOperationResult>(
                "initialize",
                Canvas2DSurfaceData.From(surfaceSize),
                resources,
                fonts,
                defaultFontFamily);
            ArgumentNullException.ThrowIfNull(result);
            if (result.Succeeded)
            {
                _renderer = renderer;
                renderer = null;
            }

            return result;
        }
        finally
        {
            if (renderer is not null)
            {
                await ReleaseUncommittedRendererAsync(renderer);
            }
        }
    }

    public async ValueTask<Canvas2DInteropOperationResult> ResizeAsync(
        Canvas2DSurfaceSize surfaceSize)
    {
        var renderer = RequireInitialized();
        var result = await renderer.InvokeAsync<Canvas2DInteropOperationResult>(
            "resize",
            Canvas2DSurfaceData.From(surfaceSize));
        ArgumentNullException.ThrowIfNull(result);
        return result;
    }

    public async ValueTask<Canvas2DInteropOperationResult> RenderAsync(
        Canvas2DRenderFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var renderer = RequireInitialized();
        var result = await renderer.InvokeAsync<Canvas2DInteropOperationResult>(
            "render",
            frame);
        ArgumentNullException.ThrowIfNull(result);
        return result;
    }

    public async ValueTask<Canvas2DTextMeasurementInteropResult> MeasureTextAsync(
        Canvas2DTextMeasurementRequestData request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var renderer = RequireInitialized();
        var result = await renderer.InvokeAsync<Canvas2DTextMeasurementInteropResult>(
            "measureText",
            request);
        ArgumentNullException.ThrowIfNull(result);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var renderer = _renderer;
        var module = _module;
        _renderer = null;
        _module = null;
        var failed = false;
        if (renderer is not null)
        {
#pragma warning disable CA1031 // Browser-resource disposal is best effort and cannot escape the boundary.
            try
            {
                await renderer.InvokeVoidAsync("dispose");
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                failed = true;
            }
            try
            {
                await renderer.DisposeAsync();
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                failed = true;
            }
#pragma warning restore CA1031
        }

        if (module is not null)
        {
#pragma warning disable CA1031 // All owned references must be attempted before reporting cleanup failure.
            try
            {
                await module.DisposeAsync();
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                failed = true;
            }
#pragma warning restore CA1031
        }

        if (failed)
        {
            throw new InvalidOperationException("Canvas2D browser-resource disposal failed.");
        }
    }

    private IJSObjectReference RequireInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_renderer is null)
        {
            throw new InvalidOperationException("Canvas2D browser execution is not initialized.");
        }

        return _renderer;
    }

    private static async ValueTask ReleaseUncommittedRendererAsync(
        IJSObjectReference renderer)
    {
#pragma warning disable CA1031 // Failed initialization must not leak or make cleanup transactional.
        try
        {
            await renderer.InvokeVoidAsync("dispose");
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            // Releasing the opaque reference is still required.
        }

        try
        {
            await renderer.DisposeAsync();
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            // Initialization outcome is authoritative; cleanup failure is non-transactional.
        }
#pragma warning restore CA1031
    }

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;

}
