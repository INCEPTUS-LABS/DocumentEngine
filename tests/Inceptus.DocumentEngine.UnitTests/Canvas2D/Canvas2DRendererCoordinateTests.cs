using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DRendererCoordinateTests
{
    [Fact]
    public void NeutralViewportMapsCanvasTopLeftAndDocumentOriginExactly()
    {
        var scene = CreateScene(ViewportSnapshot.Default);

        var documentOrigin = Canvas2DRenderer.ConvertCssToDocument(scene, new PointD(0d, 0d));
        var canvasOrigin = scene.ViewportTransform.TransformPoint(new PointD(0d, 0d));

        Assert.Equal(new PointD(0d, 0d), documentOrigin);
        Assert.Equal(new PointD(0d, 0d), canvasOrigin);
        Assert.Equal(Matrix2D.Identity, scene.ViewportTransform);
    }

    [Fact]
    public async Task SurfaceCssSizeAndDevicePixelRatioCrossOnlyTheRendererExecutionBoundary()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());
        var initial = new Canvas2DSurfaceSize(320.5d, 200.25d, 2d);
        var resized = new Canvas2DSurfaceSize(640d, 480d, 1.5d);

        await renderer.InitializeAsync("canvas", initial);
        Assert.Equal(initial, execution.LastSurfaceSize);
        await renderer.ResizeAsync(resized);

        Assert.Equal(resized, execution.LastSurfaceSize);
        Assert.DoesNotContain(
            typeof(Canvas2DScene).GetProperties(),
            property => property.Name.Contains("DevicePixel", StringComparison.Ordinal) ||
                property.Name.Contains("BackingStore", StringComparison.Ordinal));
    }

    [Fact]
    public void CssToDocumentUsesOnlyInverseSceneViewportAndExcludesDevicePixelRatio()
    {
        var scene = CreateScene(new ViewportSnapshot(2d, new VectorD(30d, 10d)));
        var documentPoint = new PointD(25d, 40d);
        var cssPoint = scene.ViewportTransform.TransformPoint(documentPoint);

        var converted = Canvas2DRenderer.ConvertCssToDocument(scene, cssPoint);

        Assert.InRange(Math.Abs(converted.X - documentPoint.X), 0d, 1e-10);
        Assert.InRange(Math.Abs(converted.Y - documentPoint.Y), 0d, 1e-10);
    }

    [Theory]
    [InlineData(1d, 0d, 0d, 25.5d, 40.25d)]
    [InlineData(1d, 30d, 10d, -12.75d, 9.5d)]
    [InlineData(2d, 0d, 0d, 25.5d, 40.25d)]
    [InlineData(1.25d, 30.5d, -18.25d, -12.75d, 9.5d)]
    [InlineData(0.625d, -45.5d, -22.75d, 100.125d, -80.875d)]
    public void CssToDocumentInvertsIdentityPanZoomAndFractionalViewport(
        double zoom,
        double panX,
        double panY,
        double documentX,
        double documentY)
    {
        var scene = CreateScene(new ViewportSnapshot(zoom, new VectorD(panX, panY)));
        var documentPoint = new PointD(documentX, documentY);
        var cssPoint = scene.ViewportTransform.TransformPoint(documentPoint);

        var converted = Canvas2DRenderer.ConvertCssToDocument(scene, cssPoint);

        Assert.InRange(Math.Abs(converted.X - documentPoint.X), 0d, 1e-10);
        Assert.InRange(Math.Abs(converted.Y - documentPoint.Y), 0d, 1e-10);
    }

    [Theory]
    [InlineData(1d, 0d, 0d)]
    [InlineData(1.1d, 40d, -20d)]
    [InlineData(2d, -75d, 125d)]
    public void RepresentativeDocumentPointsRoundTripThroughViewport(
        double zoom,
        double panX,
        double panY)
    {
        var scene = CreateScene(new ViewportSnapshot(zoom, new VectorD(panX, panY)));
        PointD[] points =
        [
            new(0d, 0d),
            new(1d, 1d),
            new(100d, 50d),
            new(500d, 300d),
        ];

        foreach (var documentPoint in points)
        {
            var canvasPoint = scene.ViewportTransform.TransformPoint(documentPoint);
            var roundTripped = Canvas2DRenderer.ConvertCssToDocument(scene, canvasPoint);

            Assert.InRange(Math.Abs(roundTripped.X - documentPoint.X), 0d, 1e-10);
            Assert.InRange(Math.Abs(roundTripped.Y - documentPoint.Y), 0d, 1e-10);
        }
    }

    [Fact]
    public async Task RenderFrameCarriesViewportAndItemTransformsSeparatelyInCompositionOrder()
    {
        var execution = new Canvas2DRendererTestExecution();
        await using var renderer = new Canvas2DRenderer(execution, RenderingConfiguration());
        await renderer.InitializeAsync("canvas", new Canvas2DSurfaceSize(400d, 300d, 3d));
        var scene = CreateScene(new ViewportSnapshot(2d, new VectorD(30d, 10d)));
        var source = scene.Items.First(item => item.Transform != Matrix2D.Identity);

        await renderer.RenderAsync(scene);

        var frame = execution.LastFrame!;
        var item = frame.Items.Single(candidate => candidate.Id == source.Id.Value);
        var itemMatrix = new Matrix2D(
            item.Transform.M11,
            item.Transform.M12,
            item.Transform.M21,
            item.Transform.M22,
            item.Transform.OffsetX,
            item.Transform.OffsetY);
        var frameViewport = new Matrix2D(
            frame.ViewportTransform.M11,
            frame.ViewportTransform.M12,
            frame.ViewportTransform.M21,
            frame.ViewportTransform.M22,
            frame.ViewportTransform.OffsetX,
            frame.ViewportTransform.OffsetY);
        var localPoint = new PointD(item.GeometryBounds.X, item.GeometryBounds.Y);
        var expectedCss = scene.ViewportTransform.TransformPoint(source.Transform.TransformPoint(localPoint));
        var transportedCss = frameViewport.TransformPoint(itemMatrix.TransformPoint(localPoint));

        Assert.Equal(expectedCss, transportedCss);
        Assert.Equal(3d, execution.LastSurfaceSize!.Value.DevicePixelRatio);
    }

    [Fact]
    public void SceneViewportRemainsImmutableAcrossConversion()
    {
        var scene = CreateScene(new ViewportSnapshot(1.75d, new VectorD(-20d, 12d)));
        var before = scene.ViewportTransform;

        _ = Canvas2DRenderer.ConvertCssToDocument(scene, new PointD(100d, 80d));

        Assert.Equal(before, scene.ViewportTransform);
    }

    [Fact]
    public void InvalidCssCoordinatesAndNonInvertibleViewportAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointD(double.NaN, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointD(0d, double.PositiveInfinity));
        var source = CreateScene(ViewportSnapshot.Default);
        var nonInvertible = new Canvas2DScene(
            source.DocumentId,
            source.SourceRevision,
            source.LayoutAlgorithmId,
            source.RoutingAlgorithmId,
            source.Configuration,
            source.Contributors,
            source.Viewport,
            default,
            source.ActiveToolId,
            source.FocusTargetId,
            source.ToolState,
            source.ContributorMetadata,
            source.Items,
            source.Diagnostics);

        Assert.Throws<InvalidOperationException>(() =>
            Canvas2DRenderer.ConvertCssToDocument(nonInvertible, new PointD(1d, 1d)));
    }

    private static Canvas2DScene CreateScene(ViewportSnapshot viewport)
    {
        var inputs = Canvas2DSceneTestData.Create();
        return Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(viewport: viewport)).Scene);
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
