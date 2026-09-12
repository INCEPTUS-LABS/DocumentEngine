using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DRendererLifecycleTests
{
    [Fact]
    public async Task InitializeResizeRenderAndDisposeFollowOwnedLifecycle()
    {
        var execution = new Canvas2DRendererTestExecution();
        var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());
        var scene = CreateScene();

        var initialized = await renderer.InitializeAsync(
            "diagram-canvas",
            new Canvas2DSurfaceSize(640d, 480d, 2d));
        var resized = await renderer.ResizeAsync(new Canvas2DSurfaceSize(800d, 600d, 1.5d));
        var rendered = await renderer.RenderAsync(scene);
        await renderer.DisposeAsync();
        await renderer.DisposeAsync();

        Assert.True(initialized.Succeeded);
        Assert.True(resized.Succeeded);
        Assert.True(rendered.Succeeded);
        Assert.Equal(["initialize:diagram-canvas", "resize", "render", "dispose"], execution.Calls);
        Assert.True(renderer.IsDisposed);
        Assert.False(renderer.IsInitialized);
        Assert.True(execution.IsDisposed);
        Assert.Equal(1, execution.DisposeCount);
    }

    [Fact]
    public async Task OperationsBeforeInitializationFailWithoutBrowserDispatch()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());

        var resize = await renderer.ResizeAsync(new Canvas2DSurfaceSize(10d, 10d, 1d));
        var render = await renderer.RenderAsync(CreateScene());

        Assert.Equal(Canvas2DRendererOperationStatus.Failed, resize.Status);
        Assert.Equal(Canvas2DRendererOperationStatus.Failed, render.Status);
        Assert.All(
            resize.Diagnostics.Concat(render.Diagnostics),
            diagnostic => Assert.Equal(Canvas2DRendererDiagnosticCodes.NotInitialized, diagnostic.Code));
        Assert.Empty(execution.Calls);
    }

    [Fact]
    public async Task DuplicateInitializationFailsAndKeepsOriginalRendererActive()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());
        var size = new Canvas2DSurfaceSize(100d, 80d, 1d);

        Assert.True((await renderer.InitializeAsync("one", size)).Succeeded);
        var duplicate = await renderer.InitializeAsync("two", size);
        var rendered = await renderer.RenderAsync(CreateScene());

        Assert.Equal(Canvas2DRendererOperationStatus.Failed, duplicate.Status);
        Assert.Contains(duplicate.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DRendererDiagnosticCodes.AlreadyInitialized);
        Assert.True(rendered.Succeeded);
        Assert.Equal(["initialize:one", "render"], execution.Calls);
    }

    [Fact]
    public async Task DisposedRendererReturnsStableFailuresWithoutReenteringExecution()
    {
        var execution = new Canvas2DRendererTestExecution();
        var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(10d, 10d, 1d));
        await renderer.DisposeAsync();

        var resize = await renderer.ResizeAsync(new Canvas2DSurfaceSize(20d, 20d, 1d));
        var render = await renderer.RenderAsync(CreateScene());
        var initialize = await renderer.InitializeAsync(
            "replacement",
            new Canvas2DSurfaceSize(20d, 20d, 1d));

        Assert.All(
            new[] { resize, render, initialize },
            result => Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == Canvas2DRendererDiagnosticCodes.Disposed));
        Assert.Equal(["initialize:canvas", "dispose"], execution.Calls);
    }

    [Fact]
    public async Task DisposalFailureIsBoundedObservableAndIdempotentAfterCleanupAttempt()
    {
        var execution = new Canvas2DRendererTestExecution
        {
            DisposeException = new InvalidOperationException("sensitive disposal detail"),
        };
        var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(10d, 10d, 1d));

        await renderer.DisposeAsync();
        var firstSnapshot = renderer.DisposalDiagnostics;
        await renderer.DisposeAsync();

        Assert.True(renderer.IsDisposed);
        Assert.False(renderer.IsInitialized);
        var diagnostic = Assert.Single(firstSnapshot);
        Assert.Equal(Canvas2DRendererDiagnosticCodes.ResourceDisposalFailed, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.DoesNotContain("sensitive disposal detail", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(typeof(InvalidOperationException).FullName, diagnostic.Context["ExceptionType"]);
        Assert.Same(diagnostic, Assert.Single(renderer.DisposalDiagnostics));
        Assert.Equal(1, execution.DisposeCount);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Diagnostic>)renderer.DisposalDiagnostics).Add(diagnostic));
    }

    [Fact]
    public async Task CancellationBeforeDispatchProducesNoPresentationEffect()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await renderer.InitializeAsync(
            "canvas",
            new Canvas2DSurfaceSize(10d, 10d, 1d),
            cancellation.Token);

        Assert.Equal(Canvas2DRendererOperationStatus.Cancelled, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DRendererDiagnosticCodes.Cancelled &&
            diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(execution.Calls);
        Assert.False(renderer.IsInitialized);
    }

    [Fact]
    public async Task IndependentRendererInstancesOwnIndependentExecutionResources()
    {
        var firstExecution = new Canvas2DRendererTestExecution();
        var secondExecution = new Canvas2DRendererTestExecution();
        await using var first = new Canvas2DRenderer(firstExecution, RenderingConfiguration());
        await using var second = new Canvas2DRenderer(secondExecution, RenderingConfiguration());

        await first.InitializeAsync("first", new Canvas2DSurfaceSize(20d, 20d, 1d));
        await second.InitializeAsync("second", new Canvas2DSurfaceSize(30d, 30d, 2d));
        await first.RenderAsync(CreateScene());

        Assert.Equal(["initialize:first", "render"], firstExecution.Calls);
        Assert.Equal(["initialize:second"], secondExecution.Calls);
    }

    [Fact]
    public void SurfaceSizeRejectsInvalidCssAndDeviceValues()
    {
        double[] invalid = [double.NaN, double.PositiveInfinity, 0d, -1d];
        foreach (var value in invalid)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Canvas2DSurfaceSize(value, 1d, 1d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Canvas2DSurfaceSize(1d, value, 1d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Canvas2DSurfaceSize(1d, 1d, value));
        }
    }

    [Fact]
    public async Task DefaultSurfaceAndBlankCanvasIdentifierFailBeforeBrowserDispatch()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());

        var blankIdentifier = await renderer.InitializeAsync(
            " ",
            new Canvas2DSurfaceSize(10d, 10d, 1d));
        var defaultSurface = await renderer.InitializeAsync("canvas", default);

        Assert.All(
            new[] { blankIdentifier, defaultSurface },
            result => Assert.Equal(Canvas2DRendererOperationStatus.Failed, result.Status));
        Assert.All(
            blankIdentifier.Diagnostics.Concat(defaultSurface.Diagnostics),
            diagnostic => Assert.Equal(Canvas2DRendererDiagnosticCodes.InitializationFailed, diagnostic.Code));
        Assert.Empty(execution.Calls);
        Assert.False(renderer.IsInitialized);
    }

    [Fact]
    public void SurfaceSizePreservesPositiveSubpixelCssDimensions()
    {
        var surface = new Canvas2DSurfaceSize(0.25d, 0.5d, 1.5d);

        Assert.Equal(0.25d, surface.CssWidth);
        Assert.Equal(0.5d, surface.CssHeight);
        Assert.Equal(1.5d, surface.DevicePixelRatio);
    }

    [Fact]
    public async Task CancellationWhileQueuedBehindRenderGateDoesNotDispatchSecondOperation()
    {
        var execution = new Canvas2DRendererTestExecution
        {
            RenderEnteredSignal = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously),
            RenderReleaseSignal = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously),
        };
        await using var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(100d, 80d, 1d));
        var first = renderer.RenderAsync(CreateScene()).AsTask();
        await execution.RenderEnteredSignal.Task;
        using var cancellation = new CancellationTokenSource();
        var queued = renderer.RenderAsync(CreateScene(), cancellation.Token).AsTask();

        cancellation.Cancel();
        var cancelled = await queued;
        execution.RenderReleaseSignal.SetResult();
        var completed = await first;

        Assert.True(completed.Succeeded);
        Assert.Equal(Canvas2DRendererOperationStatus.Cancelled, cancelled.Status);
        Assert.Single(execution.Calls, call => call == "render");
    }

    [Fact]
    public void RendererResultsUseStructuralDiagnosticEqualityAndHashing()
    {
        static Diagnostic Diagnostic() => new(
            "TEST_RENDER_FAILURE",
            DiagnosticSeverity.Error,
            "Stable failure.",
            "test:source",
            [new KeyValuePair<string, string>("Detail", "stable")]);
        var first = Canvas2DRendererResult.Failure([Diagnostic()]);
        var second = Canvas2DRendererResult.Failure([Diagnostic()]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.False(ReferenceEquals(first.Diagnostics[0], second.Diagnostics[0]));
    }

    [Fact]
    public void ConfigurationDefensivelyCopiesCanonicalImageResources()
    {
        var resources = new List<KeyValuePair<string, string>>
        {
            new("test:z", "/images/z.png"),
            new("test:a", "/images/a.png"),
        };
        var configuration = new Canvas2DRendererConfiguration(resources);
        resources.Clear();

        Assert.Equal(["test:a", "test:z"], configuration.ImageResources.Keys);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)configuration.ImageResources).Add("test:x", "x"));
    }

    private static Canvas2DScene CreateScene()
    {
        var inputs = Canvas2DSceneTestData.Create();
        return Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState).Scene);
    }

    private static Canvas2DRendererConfiguration RenderingConfiguration() => new(
        fontResources:
        [
            new Canvas2DFontResource(
                "test:font:sans",
                "1",
                "sans-serif",
                "/fonts/sans.woff2"),
        ],
        defaultFontFamily: "sans-serif");
}
