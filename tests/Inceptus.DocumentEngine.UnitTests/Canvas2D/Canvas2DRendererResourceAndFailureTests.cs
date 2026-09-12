using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DRendererResourceAndFailureTests
{
    [Fact]
    public async Task InitializationReceivesCanonicalImageResourcesAndDefaultFont()
    {
        var execution = new Canvas2DRendererTestExecution();
        var configuration = Configuration();
        await using var renderer = new Canvas2DRenderer(execution, configuration);

        var result = await renderer.InitializeAsync(
            "canvas",
            new Canvas2DSurfaceSize(200d, 100d, 2d));

        Assert.True(result.Succeeded);
        Assert.Equal(["test:image:a", "test:image:z"], execution.LastImageResources!.Keys);
        Assert.Equal("Inter", execution.LastDefaultFontFamily);
    }

    [Fact]
    public async Task MissingVisibleImageFailsBeforeBrowserRenderAndLeaksNoPartialSuccess()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(200d, 100d, 1d));
        var scene = CreateImageScene("test:missing", isVisible: true);

        var result = await renderer.RenderAsync(scene);

        Assert.Equal(Canvas2DRendererOperationStatus.Failed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DRendererDiagnosticCodes.MissingImageResource &&
            diagnostic.Context["ImageReference"] == "test:missing");
        Assert.DoesNotContain("render", execution.Calls);
        Assert.Null(execution.LastFrame);
    }

    [Fact]
    public async Task HiddenImageDoesNotRequireBrowserResourceAndRemainsSkippedByFrame()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(200d, 100d, 1d));
        var scene = CreateImageScene("test:hidden", isVisible: false);

        var result = await renderer.RenderAsync(scene);

        Assert.True(result.Succeeded);
        var image = Assert.Single(execution.LastFrame!.Items, item =>
            item.GeometryKind == (int)Canvas2DSceneGeometryKind.Image);
        Assert.False(image.IsVisible);
    }

    [Theory]
    [InlineData(Canvas2DRendererDiagnosticCodes.CanvasNotFound)]
    [InlineData(Canvas2DRendererDiagnosticCodes.ContextUnavailable)]
    [InlineData(Canvas2DRendererDiagnosticCodes.ImageLoadFailed)]
    public async Task ExpectedInteropFailuresProduceStableDiagnostics(string diagnosticCode)
    {
        var execution = new Canvas2DRendererTestExecution
        {
            InitializeResult = Canvas2DRendererTestExecution.Failure(diagnosticCode, "canvas"),
        };
        await using var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());

        var result = await renderer.InitializeAsync(
            "canvas",
            new Canvas2DSurfaceSize(200d, 100d, 1d));

        Assert.Equal(Canvas2DRendererOperationStatus.Failed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == diagnosticCode);
        Assert.False(renderer.IsInitialized);
    }

    [Fact]
    public async Task UnexpectedBrowserExceptionIsDiagnosedWithoutExceptionTextOrAuthoritativeEffect()
    {
        var execution = new Canvas2DRendererTestExecution
        {
            RenderException = new InvalidOperationException("sensitive browser detail"),
        };
        await using var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(200d, 100d, 1d));
        var scene = CreateScene();
        var before = scene.Items.ToArray();

        var result = await renderer.RenderAsync(scene);

        Assert.Equal(Canvas2DRendererOperationStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(Canvas2DRendererDiagnosticCodes.RenderingFailed, diagnostic.Code);
        Assert.DoesNotContain("sensitive browser detail", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(typeof(InvalidOperationException).FullName, diagnostic.Context["ExceptionType"]);
        Assert.Equal(before, scene.Items);
        Assert.True(renderer.IsInitialized);
    }

    [Fact]
    public async Task RendererRemainsUsableAfterOnePresentationFailure()
    {
        var execution = new Canvas2DRendererTestExecution
        {
            RenderResult = Canvas2DRendererTestExecution.Failure(
                Canvas2DRendererDiagnosticCodes.RenderingFailed),
        };
        await using var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(200d, 100d, 1d));
        var first = await renderer.RenderAsync(CreateScene());
        execution.RenderResult = Canvas2DRendererTestExecution.Success();

        var second = await renderer.RenderAsync(CreateScene());

        Assert.False(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task SuccessfulTextMeasurementPreservesAllRequestConfigurationAndNormalizesMetrics()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = new Canvas2DRenderer(execution, Configuration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(200d, 100d, 1d));
        var request = Request();

        var result = await renderer.MeasureAsync(request);

        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.Metrics);
        Assert.Equal(80d, result.Metrics.Width);
        Assert.Equal(82d, result.Metrics.BoundingWidth);
        Assert.Equal("test:font:inter@4.0", result.Metrics.ResolvedFontIdentity);
        var transported = Assert.IsType<Inceptus.DocumentEngine.Canvas2D.Rendering.Interop.Canvas2DTextMeasurementRequestData>(
            execution.LastMeasurementRequest);
        Assert.Equal(request.Text, transported.Text);
        Assert.Equal(request.FontFamily, transported.FontFamily);
        Assert.Equal(request.FontIdentity, transported.FontIdentity);
        Assert.Equal(request.FontVersion, transported.FontVersion);
        Assert.Equal(request.FontWeight, transported.FontWeight);
        Assert.Equal("italic", transported.FontStyle);
        Assert.Equal("ltr", transported.Direction);
        Assert.Equal(request.Scale, transported.Scale);
    }

    [Fact]
    public async Task SuccessfulTextMeasurementsAreCachedByTheCompleteVersionedRequest()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = new Canvas2DRenderer(execution, Configuration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(200d, 100d, 1d));
        var request = Request();

        var first = await renderer.MeasureAsync(request);
        var second = await renderer.MeasureAsync(request);
        var differentText = await renderer.MeasureAsync(new TextMeasurementRequest(
            request.Text + "!",
            request.FontFamily,
            request.FontIdentity,
            request.FontVersion,
            request.FontSize,
            request.LineHeight,
            request.FontWeight,
            request.FontStyle,
            request.Locale,
            request.Direction,
            request.WritingMode,
            request.Scale,
            request.ConfigurationId,
            request.ConfigurationVersion));

        Assert.True(first.IsSuccessful);
        Assert.Same(first, second);
        Assert.True(differentText.IsSuccessful);
        Assert.Equal(2, execution.Calls.Count(call => call == "measure"));
    }

    [Fact]
    public async Task UnconfiguredFontAndConfigurationFailBeforeBrowserMeasurement()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = new Canvas2DRenderer(execution, Configuration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(200d, 100d, 1d));
        var badFont = new TextMeasurementRequest(
            "Text", "Missing", "test:font:missing", "1", 12d, 16d, 400,
            TextFontStyle.Normal, "und", TextDirection.LeftToRight,
            TextWritingMode.HorizontalTopToBottom, 1d, "test:metrics", "1");
        var badConfiguration = new TextMeasurementRequest(
            "Text", "Inter", "test:font:inter", "4.0", 12d, 16d, 400,
            TextFontStyle.Normal, "und", TextDirection.LeftToRight,
            TextWritingMode.HorizontalTopToBottom, 1d, "test:unsupported", "1");

        var fontResult = await renderer.MeasureAsync(badFont);
        var configurationResult = await renderer.MeasureAsync(badConfiguration);

        Assert.Equal(TextMeasurementStatus.Failed, fontResult.Status);
        Assert.Contains(fontResult.Diagnostics, diagnostic =>
            diagnostic.Code == TextMetricsDiagnosticCodes.UnavailableFont);
        Assert.Equal(TextMeasurementStatus.Failed, configurationResult.Status);
        Assert.Contains(configurationResult.Diagnostics, diagnostic =>
            diagnostic.Code == TextMetricsDiagnosticCodes.UnsupportedConfiguration);
        Assert.DoesNotContain("measure", execution.Calls);
    }

    [Fact]
    public async Task MalformedOrFailedBrowserMeasurementExposesNoPartialMetrics()
    {
        var execution = new Canvas2DRendererTestExecution
        {
            MeasurementResult = new Inceptus.DocumentEngine.Canvas2D.Rendering.Interop.Canvas2DTextMeasurementInteropResult
            {
                Succeeded = true,
                Width = double.NaN,
                Ascent = 1d,
                Descent = 1d,
                LineHeight = 2d,
                BoundingWidth = 1d,
                BoundingHeight = 1d,
                ResolvedFontIdentity = "test:font:inter@4.0",
            },
        };
        await using var renderer = new Canvas2DRenderer(execution, Configuration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(200d, 100d, 1d));

        var malformed = await renderer.MeasureAsync(Request());
        execution.MeasurementResult = new Inceptus.DocumentEngine.Canvas2D.Rendering.Interop.Canvas2DTextMeasurementInteropResult
        {
            Succeeded = false,
            Code = TextMetricsDiagnosticCodes.UnavailableFont,
        };
        var failed = await renderer.MeasureAsync(Request());

        Assert.Null(malformed.Metrics);
        Assert.Equal(TextMeasurementStatus.Failed, malformed.Status);
        Assert.Contains(malformed.Diagnostics, diagnostic =>
            diagnostic.Code == TextMetricsDiagnosticCodes.MeasurementFailure);
        Assert.Null(failed.Metrics);
        Assert.Contains(failed.Diagnostics, diagnostic =>
            diagnostic.Code == TextMetricsDiagnosticCodes.UnavailableFont);
    }

    [Fact]
    public async Task ForeignResolvedFontIdentityIsRejectedWithoutPartialMetrics()
    {
        var execution = new Canvas2DRendererTestExecution
        {
            MeasurementResult = new Inceptus.DocumentEngine.Canvas2D.Rendering.Interop.Canvas2DTextMeasurementInteropResult
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
                ResolvedFontIdentity = "test:font:foreign@1",
            },
        };
        await using var renderer = new Canvas2DRenderer(execution, Configuration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(200d, 100d, 1d));

        var result = await renderer.MeasureAsync(Request());

        Assert.Equal(TextMeasurementStatus.Failed, result.Status);
        Assert.Null(result.Metrics);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == TextMetricsDiagnosticCodes.FontSubstituted);
    }

    [Fact]
    public async Task NonNeutralLocaleIsRejectedBeforeBrowserMeasurement()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = new Canvas2DRenderer(execution, Configuration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(200d, 100d, 1d));
        var request = new TextMeasurementRequest(
            "Neutral text", "Inter", "test:font:inter", "4.0", 14d, 18d, 600,
            TextFontStyle.Italic, "en-US", TextDirection.LeftToRight,
            TextWritingMode.HorizontalTopToBottom, 1.25d, "test:metrics", "1");

        var result = await renderer.MeasureAsync(request);

        Assert.Equal(TextMeasurementStatus.Failed, result.Status);
        Assert.Null(result.Metrics);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == TextMetricsDiagnosticCodes.UnsupportedConfiguration);
        Assert.DoesNotContain("measure", execution.Calls);
    }

    [Fact]
    public async Task CancelledTextMeasurementDoesNotReachBrowserExecution()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = new Canvas2DRenderer(execution, Configuration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(200d, 100d, 1d));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await renderer.MeasureAsync(Request(), cancellation.Token);

        Assert.Equal(TextMeasurementStatus.Cancelled, result.Status);
        Assert.Null(result.Metrics);
        Assert.DoesNotContain("measure", execution.Calls);
    }

    private static Canvas2DRendererConfiguration Configuration() => new(
        imageResources:
        [
            new KeyValuePair<string, string>("test:image:z", "/z.png"),
            new KeyValuePair<string, string>("test:image:a", "/a.png"),
        ],
        fontResources:
        [
            new Canvas2DFontResource(
                "test:font:inter-default",
                "4.0",
                "Inter",
                "/fonts/inter-400-normal.woff2"),
            new Canvas2DFontResource(
                "test:font:inter",
                "4.0",
                "Inter",
                "/fonts/inter-600-italic.woff2",
                600,
                TextFontStyle.Italic),
        ],
        defaultFontFamily: "Inter",
        textMeasurementConfigurationId: "test:metrics",
        textMeasurementConfigurationVersion: "1");

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

    private static TextMeasurementRequest Request() => new(
        "Neutral text",
        "Inter",
        "test:font:inter",
        "4.0",
        14d,
        18d,
        600,
        TextFontStyle.Italic,
        "und",
        TextDirection.LeftToRight,
        TextWritingMode.HorizontalTopToBottom,
        1.25d,
        "test:metrics",
        "1");

    private static Canvas2DScene CreateScene()
    {
        var inputs = Canvas2DSceneTestData.Create();
        return Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph, inputs.Layout, inputs.Routing, inputs.VisualModel, inputs.EditorState).Scene);
    }

    private static Canvas2DScene CreateImageScene(string reference, bool isVisible)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var contributor = new ImageContributor(reference, isVisible);
        return Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder(
            contributors:
            [
                new Canvas2DSceneContributorRegistration(
                    new Canvas2DSceneContributorDescriptor(ImageContributor.Id, "1"),
                    contributor),
            ]).Build(
                inputs.Graph,
                inputs.Layout,
                inputs.Routing,
                inputs.VisualModel,
                inputs.EditorState).Scene);
    }

    private sealed class ImageContributor(string reference, bool isVisible) : ICanvas2DSceneContributor
    {
        internal static Canvas2DSceneContributorId Id { get; } = new("test:image-contributor");

        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context) =>
            Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(
            [
                new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForExtension(Id, reference),
                    Canvas2DSceneLayer.Decoration,
                    0,
                    Canvas2DSceneGeometry.Image(new RectD(0d, 0d, 10d, 10d), reference),
                    new Canvas2DSceneOriginTrace(
                        Canvas2DSceneOriginCategory.RegisteredExtension,
                        stableSourceKey: reference),
                    isVisible: isVisible),
            ]));
    }
}
