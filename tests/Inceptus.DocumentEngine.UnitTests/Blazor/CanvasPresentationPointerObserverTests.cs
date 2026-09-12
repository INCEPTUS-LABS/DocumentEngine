using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class CanvasPresentationPointerObserverTests
{
    [Fact]
    public async Task WheelCallbackForwardsOnlyOrdinaryFiniteNonzeroDeltas()
    {
        var inputs = new List<CanvasWheelInput>();
        var callback = new CanvasPresentationPointerCallback(
            static _ => Task.CompletedTask,
            input =>
            {
                inputs.Add(input);
                return Task.CompletedTask;
            });

        await callback.OnCanvasWheelInput(2.4d, 7.8d, CanvasWheelDeltaMode.Pixel, false, false);
        await callback.OnCanvasWheelInput(1d, 2d, CanvasWheelDeltaMode.Pixel, true, false);
        await callback.OnCanvasWheelInput(1d, 2d, CanvasWheelDeltaMode.Pixel, false, true);
        await callback.OnCanvasWheelInput(0d, 0d, CanvasWheelDeltaMode.Pixel, false, false);
        await callback.OnCanvasWheelInput(
            double.NaN,
            1d,
            CanvasWheelDeltaMode.Pixel,
            false,
            false);
        await callback.OnCanvasWheelInput(
            1d,
            2d,
            (CanvasWheelDeltaMode)99,
            false,
            false);

        Assert.Equal(
            new CanvasWheelInput(2.4d, 7.8d, CanvasWheelDeltaMode.Pixel, false, false),
            Assert.Single(inputs));
    }

    [Theory]
    [InlineData((int)CanvasWheelDeltaMode.Pixel, -2.4d, -7.8d)]
    [InlineData((int)CanvasWheelDeltaMode.Line, -38.4d, -124.8d)]
    [InlineData((int)CanvasWheelDeltaMode.Page, -1920d, -4680d)]
    public void WheelDeltaModesResolveToCanvasCssTranslation(
        int deltaModeValue,
        double expectedX,
        double expectedY)
    {
        var deltaMode = (CanvasWheelDeltaMode)deltaModeValue;
        var input = new CanvasWheelInput(2.4d, 7.8d, deltaMode, false, false);

        var translation = input.ResolveCanvasTranslation(
            new Canvas2DSurfaceSize(800d, 600d, 2d));

        Assert.Equal(new VectorD(expectedX, expectedY), translation);
    }

    [Fact]
    public async Task CallbackForwardsFiniteContextMenuScalarsWithoutCaptureState()
    {
        var inputs = new List<CanvasPointerInput>();
        var callback = new CanvasPresentationPointerCallback(input =>
        {
            inputs.Add(input);
            return Task.CompletedTask;
        });

        await callback.OnCanvasPointerInput(
            CanvasPointerEventKind.ContextMenu,
            pointerId: 0,
            button: 2,
            buttons: 0,
            isPrimary: true,
            clientX: 410.5d,
            clientY: 260.25d,
            canvasLeft: 15d,
            canvasTop: 25d,
            altKey: true,
            controlKey: false,
            metaKey: true,
            shiftKey: true,
            captureGeneration: 0);

        Assert.Equal(
            new CanvasPointerInput(
                CanvasPointerEventKind.ContextMenu,
                0,
                2,
                0,
                true,
                410.5d,
                260.25d,
                15d,
                25d,
                true,
                false,
                true,
                true),
            Assert.Single(inputs));
    }

    [Fact]
    public async Task CallbackForwardsOnlyFiniteScalarInputAndCanBeDisabled()
    {
        var inputs = new List<CanvasPointerInput>();
        var callback = new CanvasPresentationPointerCallback(input =>
        {
            inputs.Add(input);
            return Task.CompletedTask;
        });

        await callback.OnCanvasPointerInput(
            CanvasPointerEventKind.Down,
            17,
            0,
            1,
            true,
            110d,
            220d,
            10d,
            20d,
            true,
            true,
            false,
            true,
            29);
        await callback.OnCanvasPointerInput(
            CanvasPointerEventKind.Move,
            17,
            -1,
            1,
            true,
            double.NaN,
            1d,
            0d,
            0d,
            false,
            false,
            false,
            false);
        await callback.OnCanvasPointerInput(
            (CanvasPointerEventKind)99,
            17,
            0,
            0,
            true,
            1d,
            1d,
            0d,
            0d,
            false,
            false,
            false,
            false);
        await callback.OnCanvasPointerInput(
            CanvasPointerEventKind.Up,
            -1,
            0,
            0,
            true,
            1d,
            1d,
            0d,
            0d,
            false,
            false,
            false,
            false);
        callback.Disable();
        await callback.OnCanvasPointerInput(
            CanvasPointerEventKind.Cancel,
            17,
            0,
            0,
            true,
            110d,
            220d,
            10d,
            20d,
            false,
            false,
            false,
            false);

        Assert.Equal(
            [
                new CanvasPointerInput(
                    CanvasPointerEventKind.Down,
                    17,
                    0,
                    1,
                    true,
                    110d,
                    220d,
                    10d,
                    20d,
                    true,
                    true,
                    false,
                    true,
                    29),
            ],
            inputs);
    }

    [Fact]
    public async Task FactoryCreatesStartsAndDisposesOneOwnedBrowserObserver()
    {
        var browserObserver = new RecordingJsObjectReference();
        var module = new RecordingJsObjectReference((identifier, _) =>
            identifier == "createCanvasPointerObserver"
                ? browserObserver
                : throw new InvalidOperationException(identifier));
        var runtime = new RecordingJsRuntime(module);
        var factory = new CanvasPresentationPointerObserverFactory(runtime);

        var observer = await factory.CreateAsync(
            "canvas",
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            CancellationToken.None);
        await observer.StartAsync(CancellationToken.None);
        await observer.SetCursorAsync("nwse-resize", CancellationToken.None);
        await observer.ReleaseCaptureAsync(29, CancellationToken.None);
        await observer.DisposeAsync();
        await observer.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await observer.SetCursorAsync("default", CancellationToken.None));

        Assert.Equal(["import"], runtime.Identifiers);
        var creation = Assert.Single(module.Invocations);
        Assert.Equal("createCanvasPointerObserver", creation.Identifier);
        Assert.Equal("canvas", creation.Arguments[0]);
        Assert.IsType<DotNetObjectReference<CanvasPresentationPointerCallback>>(
            creation.Arguments[1]);
        Assert.Equal(
            ["start", "setCursor", "releaseCapture", "dispose"],
            browserObserver.Invocations.Select(call => call.Identifier));
        Assert.Equal("nwse-resize", browserObserver.Invocations[1].Arguments[0]);
        Assert.Equal(29L, browserObserver.Invocations[2].Arguments[0]);
        Assert.Equal(1, browserObserver.DisposeCount);
        Assert.Equal(1, module.DisposeCount);
    }

    [Fact]
    public async Task DisposedObserverSuppressesLateManagedCallbacks()
    {
        var invocations = 0;
        var browserObserver = new RecordingJsObjectReference();
        var module = new RecordingJsObjectReference((_, _) => browserObserver);
        var factory = new CanvasPresentationPointerObserverFactory(new RecordingJsRuntime(module));
        var observer = await factory.CreateAsync(
            "canvas",
            _ =>
            {
                invocations++;
                return Task.CompletedTask;
            },
            _ =>
            {
                invocations++;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        var creation = Assert.Single(module.Invocations);
        var callbackReference = Assert.IsType<DotNetObjectReference<CanvasPresentationPointerCallback>>(
            creation.Arguments[1]);
        var callback = callbackReference.Value;

        await observer.DisposeAsync();
        await callback.OnCanvasPointerInput(
            CanvasPointerEventKind.Move,
            1,
            -1,
            0,
            true,
            10d,
            20d,
            0d,
            0d,
            false,
            false,
            false,
            false);

        Assert.Equal(0, invocations);
    }

    private sealed class RecordingJsRuntime(IJSObjectReference module) : IJSRuntime
    {
        internal List<string> Identifiers { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Identifiers.Add(identifier);
            return ValueTask.FromResult((TValue)module);
        }
    }

    private sealed class RecordingJsObjectReference(
        Func<string, object?[], object?>? resultFactory = null) : IJSObjectReference
    {
        internal List<(string Identifier, object?[] Arguments)> Invocations { get; } = [];

        internal int DisposeCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var arguments = args ?? [];
            Invocations.Add((identifier, arguments));
            var result = resultFactory?.Invoke(identifier, arguments);
            return ValueTask.FromResult(result is null ? default! : (TValue)result);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
