using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DRendererFrameTests
{
    [Fact]
    public async Task RenderBuildsOneCompleteFrameInCanonicalSceneOrderWithoutMutatingScene()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = await CreateInitializedRenderer(execution);
        var scene = CreateScene();
        var sceneItems = scene.Items.ToArray();
        var viewport = scene.ViewportTransform;

        var result = await renderer.RenderAsync(scene);

        Assert.True(result.Succeeded);
        var frame = Assert.IsType<Canvas2DRenderFrame>(execution.LastFrame);
        Assert.Equal(sceneItems.Select(static item => item.Id.Value), frame.Items.Select(static item => item.Id));
        Assert.Equal(sceneItems.Select(static item => (int)item.Layer), frame.Items.Select(static item => item.Layer));
        Assert.Equal(viewport.M11, frame.ViewportTransform.M11);
        Assert.Equal(viewport.M22, frame.ViewportTransform.M22);
        Assert.Equal(viewport.OffsetX, frame.ViewportTransform.OffsetX);
        Assert.Equal(viewport.OffsetY, frame.ViewportTransform.OffsetY);
        Assert.Equal(sceneItems, scene.Items);
        Assert.Equal(viewport, scene.ViewportTransform);
    }

    [Fact]
    public async Task RepeatedRenderingBuildsIndependentFramesWithoutTransformAccumulation()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = await CreateInitializedRenderer(execution);
        var scene = CreateScene();

        await renderer.RenderAsync(scene);
        var first = Assert.IsType<Canvas2DRenderFrame>(execution.LastFrame);
        await renderer.RenderAsync(scene);
        var second = Assert.IsType<Canvas2DRenderFrame>(execution.LastFrame);

        Assert.NotSame(first, second);
        Assert.NotSame(first.Items, second.Items);
        Assert.Equal(
            first.Items.Select(FrameFingerprint),
            second.Items.Select(FrameFingerprint));
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task FramePreservesLocalGeometryDocumentClipTransformAndStyles()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = await CreateInitializedRenderer(execution);
        var scene = CreateScene();
        var source = scene.Items.First(item => item.Layer == Canvas2DSceneLayer.Content);

        await renderer.RenderAsync(scene);

        var item = Assert.IsType<Canvas2DRenderItem>(execution.LastFrame!.Items.Single(candidate =>
            candidate.Id == source.Id.Value));
        Assert.Equal(source.Geometry.Bounds.X, item.GeometryBounds.X);
        Assert.Equal(source.Geometry.Bounds.Y, item.GeometryBounds.Y);
        Assert.Equal(source.Transform.OffsetX, item.Transform.OffsetX);
        Assert.Equal(source.Transform.OffsetY, item.Transform.OffsetY);
        Assert.Equal(source.Style.Fill, item.Fill);
        Assert.Equal(source.Style.Stroke, item.Stroke);
        Assert.Equal(source.Style.StrokeWidth, item.StrokeWidth);
        Assert.Equal(source.Style.DashPattern, item.DashPattern);
        Assert.Equal(source.Style.Opacity, item.Opacity);
        Assert.Equal(source.Style.FontFamily, item.FontFamily);
        Assert.Equal(source.Style.FontSize, item.FontSize);
        Assert.Equal(source.Geometry.TextAnchor.X, item.TextAnchor.X);
        Assert.Equal(source.Geometry.TextAnchor.Y, item.TextAnchor.Y);
        Assert.Equal((int)source.Geometry.TextAlignment, item.TextAlignment);
        Assert.Equal((int)source.Geometry.TextBaseline, item.TextBaseline);
    }

    [Fact]
    public async Task FrameCarriesDocumentSpaceClipSeparatelyFromLocalGeometryTransform()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = await CreateInitializedRenderer(execution);
        var scene = CreateSceneWithContributor(new ClipAndStyleContributor());

        await renderer.RenderAsync(scene);

        var expectedId = Canvas2DSceneObjectIdentity.ForExtension(
            new Canvas2DSceneContributorId("test:clip"),
            "item").Value;
        var item = Assert.Single(execution.LastFrame!.Items, candidate => candidate.Id == expectedId);
        Assert.Equal(0d, item.GeometryBounds.X);
        Assert.Equal(0d, item.GeometryBounds.Y);
        Assert.Equal(100d, item.Transform.OffsetX);
        Assert.Equal(50d, item.Transform.OffsetY);
        Assert.NotNull(item.Clip);
        Assert.Equal(102d, item.Clip!.X);
        Assert.Equal(52d, item.Clip.Y);
        Assert.Equal("#112233", item.Fill);
        Assert.Equal("#445566", item.Stroke);
        Assert.Equal(3d, item.StrokeWidth);
        Assert.Equal([5d, 2d], item.DashPattern);
        Assert.Equal(0.4d, item.Opacity);
        Assert.Equal("Inter", item.FontFamily);
        Assert.Equal(16d, item.FontSize);
    }

    [Fact]
    public async Task EverySupportedGeometryKindIsTransportedWithoutBrowserObjects()
    {
        var execution = new Canvas2DRendererTestExecution();
        var configuration = RenderingConfiguration(
        [
            new KeyValuePair<string, string>("test:image", "/images/neutral.png"),
        ]);
        await using var configuredRenderer = new Canvas2DRenderer(execution, configuration);
        await configuredRenderer.InitializeAsync("canvas-geometry", new Canvas2DSurfaceSize(320d, 200d, 1d));
        var scene = CreateSceneWithContributor(new GeometryContributor());

        var result = await configuredRenderer.RenderAsync(scene);

        Assert.True(result.Succeeded);
        Assert.Equal(
            Enum.GetValues<Canvas2DSceneGeometryKind>().Order(),
            execution.LastFrame!.Items
                .Where(item => item.Id.Contains("test:geometry", StringComparison.Ordinal))
                .Select(item => (Canvas2DSceneGeometryKind)item.GeometryKind)
                .Order());
        var path = Assert.Single(execution.LastFrame.Items, item => item.Id.EndsWith(":path", StringComparison.Ordinal));
        Assert.Equal(3, path.Points.Length);
        Assert.True(path.IsClosed);
        var text = Assert.Single(execution.LastFrame.Items, item => item.Id.EndsWith(":text", StringComparison.Ordinal));
        Assert.Equal("Neutral", text.Content);
        Assert.Equal(45d, text.TextAnchor.X);
        Assert.Equal(30d, text.TextAnchor.Y);
        Assert.Equal((int)Canvas2DTextAlignment.Center, text.TextAlignment);
        Assert.Equal((int)Canvas2DTextBaseline.Middle, text.TextBaseline);
        var image = Assert.Single(execution.LastFrame.Items, item => item.Id.EndsWith(":image", StringComparison.Ordinal));
        Assert.Equal("test:image", image.Content);
    }

    [Fact]
    public async Task InvisibleItemsRemainExplicitInImmutableFrameForDeterministicBrowserSkip()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = await CreateInitializedRenderer(execution);
        var scene = CreateSceneWithContributor(new InvisibleContributor());

        await renderer.RenderAsync(scene);

        var expectedId = Canvas2DSceneObjectIdentity.ForExtension(
            new Canvas2DSceneContributorId("test:invisible"),
            "item").Value;
        var invisible = Assert.Single(execution.LastFrame!.Items, item => item.Id == expectedId);
        Assert.False(invisible.IsVisible);
    }

    private static async ValueTask<Canvas2DRenderer> CreateInitializedRenderer(
        Canvas2DRendererTestExecution execution)
    {
        var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());
        var result = await renderer.InitializeAsync(
            "canvas",
            new Canvas2DSurfaceSize(640d, 480d, 2d));
        Assert.True(result.Succeeded);
        return renderer;
    }

    private static Canvas2DScene CreateScene() => CreateSceneWithContributor(null);

    private static Canvas2DScene CreateSceneWithContributor(ITestContributor? contributor)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var registrations = contributor is null
            ? null
            : new[]
            {
                new Canvas2DSceneContributorRegistration(
                    new Canvas2DSceneContributorDescriptor(contributor.Id, "1"),
                    contributor),
            };
        return Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder(contributors: registrations).Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState).Scene);
    }

    private static string FrameFingerprint(Canvas2DRenderItem item) => string.Join(
        '|',
        item.Id,
        item.Layer,
        item.ZIndex,
        item.GeometryKind,
        item.GeometryBounds,
        item.Transform,
        item.Clip,
        item.Fill,
        item.Stroke,
        item.StrokeWidth,
        item.Opacity,
        item.TextAnchor,
        item.TextAlignment,
        item.TextBaseline,
        item.IsVisible);

    private static Canvas2DRendererConfiguration RenderingConfiguration(
        IEnumerable<KeyValuePair<string, string>>? imageResources = null) => new(
        imageResources,
        fontResources:
        [
            new Canvas2DFontResource(
                "test:font:sans",
                "1",
                "sans-serif",
                "/fonts/sans.woff2"),
        ],
        defaultFontFamily: "sans-serif");

    private interface ITestContributor : ICanvas2DSceneContributor
    {
        Canvas2DSceneContributorId Id { get; }
    }

    private sealed class ClipAndStyleContributor : ITestContributor
    {
        public Canvas2DSceneContributorId Id { get; } = new("test:clip");

        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context) =>
            Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(
            [
                new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForExtension(Id, "item"),
                    Canvas2DSceneLayer.Decoration,
                    10,
                    Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 20d, 10d)),
                    new Canvas2DSceneOriginTrace(
                        Canvas2DSceneOriginCategory.RegisteredExtension,
                        stableSourceKey: "item"),
                    transform: Matrix2D.CreateTranslation(100d, 50d),
                    clip: new RectD(102d, 52d, 12d, 6d),
                    style: new Canvas2DSceneStyle(
                        "#112233",
                        "#445566",
                        3d,
                        [5d, 2d],
                        0.4d,
                        "Inter",
                        16d)),
            ]));
    }

    private sealed class GeometryContributor : ITestContributor
    {
        public Canvas2DSceneContributorId Id { get; } = new("test:geometry");

        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context) =>
            Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(
            [
                Item("rectangle", Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 10d, 10d))),
                Item("ellipse", Canvas2DSceneGeometry.Ellipse(new RectD(20d, 0d, 10d, 10d))),
                Item("path", Canvas2DSceneGeometry.Path(
                    [new PointD(0d, 20d), new PointD(10d, 20d), new PointD(5d, 30d)], true)),
                Item("text", Canvas2DSceneGeometry.Text(
                    new RectD(20d, 20d, 50d, 20d),
                    "Neutral",
                    new PointD(45d, 30d),
                    Canvas2DTextAlignment.Center,
                    Canvas2DTextBaseline.Middle)),
                Item("image", Canvas2DSceneGeometry.Image(new RectD(80d, 20d, 20d, 20d), "test:image")),
            ]));

        private Canvas2DSceneItem Item(string key, Canvas2DSceneGeometry geometry) => new(
            Canvas2DSceneObjectIdentity.ForExtension(Id, key),
            Canvas2DSceneLayer.Decoration,
            0,
            geometry,
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.RegisteredExtension,
                stableSourceKey: key));
    }

    private sealed class InvisibleContributor : ITestContributor
    {
        public Canvas2DSceneContributorId Id { get; } = new("test:invisible");

        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context) =>
            Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(
            [
                new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForExtension(Id, "item"),
                    Canvas2DSceneLayer.Decoration,
                    0,
                    Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 10d, 10d)),
                    new Canvas2DSceneOriginTrace(
                        Canvas2DSceneOriginCategory.RegisteredExtension,
                        stableSourceKey: "item"),
                    isVisible: false),
            ]));
    }
}
