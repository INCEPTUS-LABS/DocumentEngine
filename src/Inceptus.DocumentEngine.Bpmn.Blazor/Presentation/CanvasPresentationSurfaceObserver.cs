using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed class CanvasPresentationSurfaceObserverFactory :
    ICanvasPresentationSurfaceObserverFactory
{
    private readonly IJSRuntime _jsRuntime;

    internal CanvasPresentationSurfaceObserverFactory(IJSRuntime jsRuntime)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        _jsRuntime = jsRuntime;
    }

    public async ValueTask<ICanvasPresentationSurfaceObserver> CreateAsync(
        string containerElementId,
        Func<Canvas2DSurfaceSize, Task> onSurfaceChanged,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerElementId);
        ArgumentNullException.ThrowIfNull(onSurfaceChanged);

        var module = await _jsRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            "./_content/Inceptus.DocumentEngine.Bpmn.Blazor/inceptus.presentation.js");
        var callback = new CanvasPresentationSurfaceCallback(onSurfaceChanged);
        var callbackReference = DotNetObjectReference.Create(callback);
        try
        {
            var observer = await module.InvokeAsync<IJSObjectReference>(
                "createCanvasSurfaceObserver",
                cancellationToken,
                containerElementId,
                callbackReference);
            return new CanvasPresentationSurfaceObserver(
                module,
                observer,
                callback,
                callbackReference);
        }
        catch
        {
            callbackReference.Dispose();
            await module.DisposeAsync();
            throw;
        }
    }
}

internal sealed class CanvasPresentationSurfaceObserver :
    ICanvasPresentationSurfaceObserver
{
    private readonly IJSObjectReference _module;
    private readonly IJSObjectReference _observer;
    private readonly CanvasPresentationSurfaceCallback _callback;
    private readonly DotNetObjectReference<CanvasPresentationSurfaceCallback> _callbackReference;
    private int _disposed;

    internal CanvasPresentationSurfaceObserver(
        IJSObjectReference module,
        IJSObjectReference observer,
        CanvasPresentationSurfaceCallback callback,
        DotNetObjectReference<CanvasPresentationSurfaceCallback> callbackReference)
    {
        _module = module;
        _observer = observer;
        _callback = callback;
        _callbackReference = callbackReference;
    }

    public async ValueTask<Canvas2DSurfaceSize> StartAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        var measurement = await _observer.InvokeAsync<CanvasSurfaceMeasurement>(
            "start",
            cancellationToken);
        return measurement.ToSurfaceSize();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

#pragma warning disable CA1031 // Browser cleanup is best effort and every handle is still released.
        try
        {
            await _observer.InvokeVoidAsync("dispose");
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            // The component is already leaving the presentation tree. Releasing every managed
            // interop handle takes precedence over surfacing an un-actionable browser callback.
        }

        try
        {
            await _observer.DisposeAsync();
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
        }

        _callback.Disable();
        _callbackReference.Dispose();
        try
        {
            await _module.DisposeAsync();
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
        }
#pragma warning restore CA1031
    }

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;
}

internal sealed class CanvasPresentationSurfaceCallback
{
    private Func<Canvas2DSurfaceSize, Task>? _onSurfaceChanged;

    internal CanvasPresentationSurfaceCallback(
        Func<Canvas2DSurfaceSize, Task> onSurfaceChanged) =>
        _onSurfaceChanged = onSurfaceChanged;

    [JSInvokable]
    public Task OnCanvasSurfaceChanged(
        double cssWidth,
        double cssHeight,
        double devicePixelRatio)
    {
        var callback = Volatile.Read(ref _onSurfaceChanged);
        return callback is null
            ? Task.CompletedTask
            : callback(new Canvas2DSurfaceSize(
                cssWidth,
                cssHeight,
                devicePixelRatio));
    }

    internal void Disable() => Interlocked.Exchange(ref _onSurfaceChanged, null);
}

internal sealed class CanvasSurfaceMeasurement
{
    public double CssWidth { get; init; }

    public double CssHeight { get; init; }

    public double DevicePixelRatio { get; init; }

    internal Canvas2DSurfaceSize ToSurfaceSize() =>
        new(CssWidth, CssHeight, DevicePixelRatio);
}
