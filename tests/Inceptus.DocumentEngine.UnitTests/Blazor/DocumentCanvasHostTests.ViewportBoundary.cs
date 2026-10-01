using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BoundaryPointerUpKeepsFinalLegalDeltaAndEndsManagedPan(bool top)
    {
        await using var test = await PanComponentFixture.CreateAsync();
        var session = Session(test.Host);
        await test.Renderer.Dispatcher.InvokeAsync(() => session.UpdateViewportAsync(
            new ViewportSnapshot(1, top ? new(-100, 0) : new(0, -100))).AsTask());
        var before = session.CaptureState();
        var content = test.Execution.LastContent;
        var full = test.Execution.FullUploadCount;
        var scalar = test.Execution.ViewportRenderCount;
        var pointer = PointerObserver(test.Host);
        var releases = pointer.ReleaseCaptureCount;
        await test.Renderer.Dispatcher.InvokeAsync(() => pointer.MiddleDownCssPointAsync(new PointD(200, 200)));
        await test.Renderer.Dispatcher.InvokeAsync(() => pointer.MiddleMoveCssPointAsync(top ? new(190, 220) : new(220, 190)));
        await test.Renderer.Dispatcher.InvokeAsync(() => pointer.MiddleUpCssPointAsync(top ? new(185.75, 225) : new(225, 185.75)));
        await test.DrainAsync();
        var after = session.CaptureState();
        Assert.Equal(top ? new VectorD(-114.25, 0) : new VectorD(0, -114.25), after.EditorState.Viewport.Pan);
        // The browser observer owns ordinary pointer-up release. Managed code must not duplicate it.
        Assert.Equal(releases, pointer.ReleaseCaptureCount);
        Assert.Equal("default", test.Host.CaptureState().CssCursor);
        Assert.Equal(before.Generation.Value + 2, after.Generation.Value);
        Assert.Equal(scalar + 2, test.Execution.ViewportRenderCount);
        Assert.Equal(full, test.Execution.FullUploadCount);
        Assert.Same(content, test.Execution.LastContent);
        Assert.Equal(before.CurrentScene!.Items, after.CurrentScene!.Items);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        await test.Renderer.Dispatcher.InvokeAsync(() => pointer.MiddleMoveCssPointAsync(new PointD(300, 300)));
        Assert.Equal(after.EditorState.Viewport, session.CaptureState().EditorState.Viewport);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BoundaryWheelRetainsPerpendicularMovementAndFinalOnlyPresentation(bool top)
    {
        await using var test = await PanComponentFixture.CreateAsync();
        var session = Session(test.Host);
        await test.Renderer.Dispatcher.InvokeAsync(() => session.UpdateViewportAsync(
            new ViewportSnapshot(1, top ? new(-100, 0) : new(0, -100))).AsTask());
        await test.DrainAsync();
        var before = session.CaptureState();
        var publications = 0;
        session.StateChanged += (_, _) => publications++;
        test.ResetCounts();
        await test.Renderer.Dispatcher.InvokeAsync(() => PointerObserver(test.Host).WheelAsync(
            top ? 12 : -30, top ? -30 : 12));
        await test.DrainAsync();
        var after = session.CaptureState();
        Assert.Equal(top ? new VectorD(-112, 0) : new VectorD(0, -112), after.EditorState.Viewport.Pan);
        Assert.Equal(3, publications);
        Assert.Equal(test.FullUploadsBefore, test.Execution.FullUploadCount);
        Assert.Equal(before.CurrentScene!.Items, after.CurrentScene!.Items);
        Assert.InRange(test.Activator.Canvas.BuildCount, 1, 2);
        Assert.Equal(0, test.Activator.Toolbox.BuildCount);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
    }

    [Fact]
    public async Task BlockedPointerAndWheelDoNotPublishOrRenderAndPointerUpEndsManagedPan()
    {
        await using var test = await PanComponentFixture.CreateAsync();
        var session = Session(test.Host);
        await test.Renderer.Dispatcher.InvokeAsync(() => session.UpdateViewportAsync(new ViewportSnapshot(1.1, default)).AsTask());
        var before = session.CaptureState();
        var full = test.Execution.FullUploadCount;
        var scalar = test.Execution.ViewportRenderCount;
        var publications = 0;
        session.StateChanged += (_, _) => publications++;
        var pointer = PointerObserver(test.Host);
        var releases = pointer.ReleaseCaptureCount;
        await test.Renderer.Dispatcher.InvokeAsync(() => pointer.MiddleDownCssPointAsync(new PointD(100, 100)));
        await test.Renderer.Dispatcher.InvokeAsync(() => pointer.MiddleMoveCssPointAsync(new PointD(120, 130)));
        await test.Renderer.Dispatcher.InvokeAsync(() => pointer.MiddleUpCssPointAsync(new PointD(125.5, 135.75)));
        for (var i = 0; i < 20; i++)
            await test.Renderer.Dispatcher.InvokeAsync(() => pointer.WheelAsync(-10, -10));
        await test.DrainAsync();
        var after = session.CaptureState();
        Assert.Equal(releases, pointer.ReleaseCaptureCount);
        Assert.Equal("default", test.Host.CaptureState().CssCursor);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Equal(before.Generation, after.Generation);
        Assert.Equal(0, publications);
        Assert.Equal(full, test.Execution.FullUploadCount);
        Assert.Equal(scalar, test.Execution.ViewportRenderCount);
    }

    [Theory]
    [InlineData(-100, -100, -45, -60)]
    [InlineData(-10, -100, 0, -60)]
    [InlineData(-100, -10, -45, 0)]
    [InlineData(-10, -10, 0, 0)]
    public async Task BoundaryZoomOutPreservesRequestedZoomAndAddsOnlyNecessaryAxisCorrection(
        double x, double y, double expectedX, double expectedY)
    {
        await using var test = await PanComponentFixture.CreateAsync();
        var session = Session(test.Host);
        await session.UpdateViewportAsync(new ViewportSnapshot(1, new VectorD(x, y)));
        var before = session.CaptureState();
        await test.Host.ZoomOutAsync();
        var after = session.CaptureState();
        Assert.Equal(0.9, after.EditorState.Viewport.Zoom);
        Assert.Equal(expectedX, after.EditorState.Viewport.Pan.X, precision: 10);
        Assert.Equal(expectedY, after.EditorState.Viewport.Pan.Y, precision: 10);
        Assert.True(after.EditorState.Viewport.VisibleDocumentRegion!.Value.Left >= 0);
        Assert.True(after.EditorState.Viewport.VisibleDocumentRegion.Value.Top >= 0);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
    }

    [Fact]
    public async Task OriginZoomCyclesAndResizeHaveNoAccumulatingPanDrift()
    {
        await using var test = await PanComponentFixture.CreateAsync();
        var session = Session(test.Host);
        await session.UpdateViewportAsync(new ViewportSnapshot(1, default));
        var before = session.CaptureState();
        for (var i = 0; i < 20; i++)
        {
            await test.Host.ZoomInAsync();
            var enlarged = session.CaptureState().EditorState.Viewport;
            Assert.Equal(1.1, enlarged.Zoom);
            Assert.Equal(-45d, enlarged.Pan.X, precision: 10);
            Assert.Equal(-30d, enlarged.Pan.Y, precision: 10);
            await test.Host.ZoomOutAsync();
            var restored = session.CaptureState().EditorState.Viewport;
            Assert.Equal(1d, restored.Zoom);
            Assert.Equal(0d, restored.Pan.X, precision: 10);
            Assert.Equal(0d, restored.Pan.Y, precision: 10);
            Assert.True(restored.VisibleDocumentRegion!.Value.Left >= 0);
            Assert.True(restored.VisibleDocumentRegion.Value.Top >= 0);
        }
        var pan = session.CaptureState().EditorState.Viewport.Pan;
        foreach (var surface in new[] { new Canvas2DSurfaceSize(700, 450, 1),
                     new Canvas2DSurfaceSize(700, 450, 2), new Canvas2DSurfaceSize(1000, 800, 1.25) })
        {
            await test.Surface.RaiseAsync(surface);
            var viewport = session.CaptureState().EditorState.Viewport;
            Assert.Equal(pan, viewport.Pan);
            Assert.Equal(surface.CssWidth, viewport.VisibleDocumentRegion!.Value.Width, precision: 10);
            Assert.Equal(surface.CssHeight, viewport.VisibleDocumentRegion.Value.Height, precision: 10);
        }
        Assert.Equal(before.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
    }
}
