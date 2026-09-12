using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed class CanvasPresentationPointerObserverFactory :
    ICanvasPresentationPointerObserverFactory
{
    private readonly IJSRuntime _jsRuntime;

    internal CanvasPresentationPointerObserverFactory(IJSRuntime jsRuntime)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        _jsRuntime = jsRuntime;
    }

    public async ValueTask<ICanvasPresentationPointerObserver> CreateAsync(
        string canvasElementId,
        Func<CanvasPointerInput, Task> onPointerInput,
        Func<CanvasWheelInput, Task> onWheelInput,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canvasElementId);
        ArgumentNullException.ThrowIfNull(onPointerInput);
        ArgumentNullException.ThrowIfNull(onWheelInput);

        var module = await _jsRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            cancellationToken,
            "./_content/Inceptus.DocumentEngine.Bpmn.Blazor/inceptus.presentation.js");
        var callback = new CanvasPresentationPointerCallback(onPointerInput, onWheelInput);
        var callbackReference = DotNetObjectReference.Create(callback);
        try
        {
            var observer = await module.InvokeAsync<IJSObjectReference>(
                "createCanvasPointerObserver",
                cancellationToken,
                canvasElementId,
                callbackReference);
            return new CanvasPresentationPointerObserver(
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

internal sealed class CanvasPresentationPointerObserver :
    ICanvasPresentationPointerObserver
{
    private readonly IJSObjectReference _module;
    private readonly IJSObjectReference _observer;
    private readonly CanvasPresentationPointerCallback _callback;
    private readonly DotNetObjectReference<CanvasPresentationPointerCallback> _callbackReference;
    private int _disposed;

    internal CanvasPresentationPointerObserver(
        IJSObjectReference module,
        IJSObjectReference observer,
        CanvasPresentationPointerCallback callback,
        DotNetObjectReference<CanvasPresentationPointerCallback> callbackReference)
    {
        _module = module;
        _observer = observer;
        _callback = callback;
        _callbackReference = callbackReference;
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        await _observer.InvokeVoidAsync("start", cancellationToken);
    }

    public async ValueTask ReleaseCaptureAsync(
        long captureGeneration,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(captureGeneration);
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        await _observer.InvokeVoidAsync(
            "releaseCapture",
            cancellationToken,
            captureGeneration);
    }

    public async ValueTask SetCursorAsync(
        string cssCursor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cssCursor);
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        await _observer.InvokeVoidAsync("setCursor", cancellationToken, cssCursor);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _callback.Disable();
#pragma warning disable CA1031 // Browser cleanup is best effort and every handle is still released.
        try
        {
            await _observer.InvokeVoidAsync("dispose");
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _ = exception;
        }

        try
        {
            await _observer.DisposeAsync();
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _ = exception;
        }

        _callbackReference.Dispose();
        try
        {
            await _module.DisposeAsync();
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _ = exception;
        }
#pragma warning restore CA1031
    }

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;
}

internal sealed class CanvasPresentationPointerCallback
{
    private readonly Func<CanvasPointerInput, Task> _onPointerInput;
    private readonly Func<CanvasWheelInput, Task> _onWheelInput;
    private int _disabled;

    internal CanvasPresentationPointerCallback(Func<CanvasPointerInput, Task> onPointerInput)
        : this(onPointerInput, static _ => Task.CompletedTask)
    {
    }

    internal CanvasPresentationPointerCallback(
        Func<CanvasPointerInput, Task> onPointerInput,
        Func<CanvasWheelInput, Task> onWheelInput)
    {
        ArgumentNullException.ThrowIfNull(onPointerInput);
        ArgumentNullException.ThrowIfNull(onWheelInput);
        _onPointerInput = onPointerInput;
        _onWheelInput = onWheelInput;
    }

    [JSInvokable]
    public Task OnCanvasPointerInput(
        CanvasPointerEventKind kind,
        long pointerId,
        int button,
        int buttons,
        bool isPrimary,
        double clientX,
        double clientY,
        double canvasLeft,
        double canvasTop,
        bool altKey,
        bool controlKey,
        bool metaKey,
        bool shiftKey,
        long captureGeneration = 0)
    {
        var input = new CanvasPointerInput(
            kind,
            pointerId,
            button,
            buttons,
            isPrimary,
            clientX,
            clientY,
            canvasLeft,
            canvasTop,
            altKey,
            controlKey,
            metaKey,
            shiftKey,
            captureGeneration);
        return Volatile.Read(ref _disabled) == 0 &&
            Enum.IsDefined(kind) &&
            pointerId >= 0 &&
            button >= -1 &&
            buttons >= 0 &&
            captureGeneration >= 0 &&
            input.IsFinite
                ? _onPointerInput(input)
                : Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnCanvasWheelInput(
        double deltaX,
        double deltaY,
        CanvasWheelDeltaMode deltaMode,
        bool controlKey,
        bool metaKey)
    {
        var input = new CanvasWheelInput(
            deltaX,
            deltaY,
            deltaMode,
            controlKey,
            metaKey);
        return Volatile.Read(ref _disabled) == 0 &&
            Enum.IsDefined(deltaMode) &&
            input.IsFinite &&
            !input.IsModified &&
            input.HasDelta
                ? _onWheelInput(input)
                : Task.CompletedTask;
    }

    internal void Disable() => Interlocked.Exchange(ref _disabled, 1);
}
