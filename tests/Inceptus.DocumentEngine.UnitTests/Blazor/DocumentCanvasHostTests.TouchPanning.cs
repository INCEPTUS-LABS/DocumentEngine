using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(false, 0.75)]
    [InlineData(true, 1.0)]
    [InlineData(true, 1.5)]
    public async Task TouchPanCancelsEditingAndPreservesDocumentSelectionHistoryAndZoom(bool startMove, double zoom)
    {
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 2));
        await using var host = CreateHost(new RecordingRenderExecution(), surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        await session.UpdateViewportAsync(new ViewportSnapshot(zoom, new VectorD(-100, -80)));
        var scene = session.CaptureState().CurrentScene!;
        var task = scene.Items.Last(item => item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId);
        await PointerObserver(host).ClickDocumentPointAsync(scene, Center(task.Bounds));
        var point = session.CaptureState().CurrentScene!.ViewportTransform.TransformPoint(Center(task.Bounds));
        var document = AttachedDocument(session).CaptureSnapshot();
        var saved = host.CaptureNativeSaveStatus();
        await TouchInputAsync(host, CanvasPointerEventKind.Down, point, 1);
        if (startMove)
        {
            await TouchInputAsync(host, CanvasPointerEventKind.Move, point + new VectorD(30, 20), 1);
            Assert.NotNull(session.CaptureState().EditorState.ActiveGesture);
        }
        var before = session.CaptureState();
        await TouchInputAsync(host, CanvasPointerEventKind.TouchPanStart, point, 2);
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        Assert.Equal("grabbing", host.CaptureState().CssCursor);
        var generation = session.CaptureState().Generation;
        await TouchInputAsync(host, CanvasPointerEventKind.TouchPanMove, point, 2);
        Assert.Equal(generation, session.CaptureState().Generation);
        await PointerObserver(host).WheelAsync(100, 100);
        Assert.Equal(before.EditorState.Viewport.Pan, session.CaptureState().EditorState.Viewport.Pan);
        await TouchInputAsync(host, CanvasPointerEventKind.TouchPanMove, point + new VectorD(-20.25, -30.5), 2);
        // A stale previous generation cannot terminate or move the current pan.
        await TouchInputAsync(host, CanvasPointerEventKind.TouchPanCancel, point, 1);
        await TouchInputAsync(host, CanvasPointerEventKind.TouchPanEnd, point + new VectorD(-25.5, -36.75), 2);
        var after = session.CaptureState();
        Assert.Equal(before.EditorState.Viewport.Pan + new VectorD(-25.5, -36.75), after.EditorState.Viewport.Pan);
        Assert.Equal(zoom, after.EditorState.Viewport.Zoom);
        Assert.Equal(before.EditorState.Selection.ToArray(), after.EditorState.Selection.ToArray());
        Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(before.PresentationContext, after.PresentationContext);
        Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
        Assert.Same(before.LayoutResult, after.LayoutResult);
        Assert.Same(before.RoutingResult, after.RoutingResult);
        Assert.Equal(saved!.CurrentCheckpoint, host.CaptureNativeSaveStatus()!.CurrentCheckpoint);
        Assert.Equal(saved.SavedCheckpoint, host.CaptureNativeSaveStatus()!.SavedCheckpoint);
        Assert.Equal("default", host.CaptureState().CssCursor);
        Assert.True(host.CaptureState().LatestPresentationSucceeded);

        // A fresh one-finger drag still commits through the existing interaction path.
        var next = after.CurrentScene!.ViewportTransform.TransformPoint(Center(task.Bounds));
        await TouchInputAsync(host, CanvasPointerEventKind.Down, next, 3);
        await TouchInputAsync(host, CanvasPointerEventKind.Move, next + new VectorD(30, 20), 3);
        await TouchInputAsync(host, CanvasPointerEventKind.Up, next + new VectorD(30, 20), 3);
        await session.WaitForIdleAsync();
        Assert.Equal(document.Revision.Increment(), AttachedDocument(session).Revision);
        await host.UndoAsync();
        Assert.Equal(document.VisualModel.VisualStates.Single(visual => visual.Id == BpmnDemoPipeline.TaskVisualId).Position,
            AttachedDocument(session).VisualModel.VisualStates.Single(visual => visual.Id == BpmnDemoPipeline.TaskVisualId).Position);
    }

    [Fact]
    public async Task TouchPanCancelsResizeWithoutChangingGeometryOrSelection()
    {
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)));
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var before = session.CaptureState();
        var document = AttachedDocument(session).CaptureSnapshot();
        var point = before.CurrentScene!.ViewportTransform.TransformPoint(Center(ResizeHandle(before.CurrentScene, "southeast").Bounds));
        await TouchInputAsync(host, CanvasPointerEventKind.Down, point, 1);
        await TouchInputAsync(host, CanvasPointerEventKind.Move, point + new VectorD(30, 20), 1);
        Assert.NotNull(session.CaptureState().EditorState.ActiveGesture);
        await TouchInputAsync(host, CanvasPointerEventKind.TouchPanStart, point, 2);
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        await TouchInputAsync(host, CanvasPointerEventKind.TouchPanEnd, point + new VectorD(-15, -20), 2);
        Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(before.EditorState.Selection.ToArray(), session.CaptureState().EditorState.Selection.ToArray());
    }

    [Fact]
    public async Task TouchPanCannotCommitToolboxPlacementAndFreshTouchTapStillCreates()
    {
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory, toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        Assert.True(selection.Select(PlacementOverrideCompositionFactory.TaskItemId));
        await host.RefreshToolboxPlacementAsync();
        var point = new PointD(450, 300);
        await TouchInputAsync(host, CanvasPointerEventKind.Down, point, 1);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        await TouchInputAsync(host, CanvasPointerEventKind.TouchPanStart, point, 2);
        await TouchInputAsync(host, CanvasPointerEventKind.TouchPanMove, point + new VectorD(-10, -20), 2);
        await TouchInputAsync(host, CanvasPointerEventKind.TouchPanEnd, point + new VectorD(-10, -20), 2);
        await TouchInputAsync(host, CanvasPointerEventKind.Up, point, 1);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(PlacementOverrideCompositionFactory.TaskItemId, selection.SelectedItemId);
        await TouchInputAsync(host, CanvasPointerEventKind.Down, point, 3);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        await TouchInputAsync(host, CanvasPointerEventKind.Up, point, 3);
        await session.WaitForIdleAsync();
        Assert.Equal(before.Revision.Increment(), AttachedDocument(session).Revision);
    }

    [Fact]
    public async Task TouchPlacementCannotCommitAfterItsToolSelectionWasRefreshed()
    {
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory, toolboxSelection: selection);
        await host.InitializeAsync("canvas", "container");
        Assert.True(selection.Select(PlacementOverrideCompositionFactory.TaskItemId));
        await host.RefreshToolboxPlacementAsync();
        var document = AttachedDocument(Session(host)).CaptureSnapshot();
        await TouchInputAsync(host, CanvasPointerEventKind.Down, new PointD(450, 300), 1);
        await host.RefreshToolboxPlacementAsync();
        await TouchInputAsync(host, CanvasPointerEventKind.Up, new PointD(450, 300), 1);
        Assert.Same(document, AttachedDocument(Session(host)).CaptureSnapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TouchPanAndWheelWorkInBothDisplayModesWithoutRecreatingCanvas(bool maximized)
    {
        using var culture = new ModelerCultureScope("en");
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)), toolboxSelection: selection);
        await host.InitializeAsync("active", "standby", "container");
        var activator = new DisplayModeActivator(host, selection);
        await using var services = DisplayModeServices(activator);
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<BpmnDemoHost>());
        if (maximized) await renderer.Dispatcher.InvokeAsync(activator.Modeler.ToggleDisplayModeAsync);
        var canvas = activator.Canvas;
        var before = Session(host).CaptureState();
        var document = AttachedDocument(Session(host)).CaptureSnapshot();
        for (var cycle = 1; cycle <= 20; cycle++)
        {
            var point = new PointD(200, 200);
            await TouchInputAsync(host, CanvasPointerEventKind.TouchPanStart, point, cycle);
            await TouchInputAsync(host, CanvasPointerEventKind.TouchPanMove, new PointD(190, 185), cycle);
            await TouchInputAsync(host, CanvasPointerEventKind.TouchPanCancel, point, cycle);
            await PointerObserver(host).WheelAsync(0.25, 0.5);
        }
        var after = Session(host).CaptureState();
        Assert.Equal(before.EditorState.Viewport.Pan + new VectorD(-205, -310), after.EditorState.Viewport.Pan);
        Assert.Equal(before.EditorState.Viewport.Zoom, after.EditorState.Viewport.Zoom);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(document, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Same(canvas, activator.Canvas);
        var markup = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        AssertDisplayButton(markup, maximized);
        Assert.Contains("data-viewport-input-enabled=\"true\"", markup, StringComparison.Ordinal);
    }

    private static Task TouchInputAsync(DocumentCanvasHost host, CanvasPointerEventKind kind, PointD point, long generation) =>
        PointerObserver(host).OnPointerInput!(new CanvasPointerInput(kind, 7,
            kind is CanvasPointerEventKind.Down or CanvasPointerEventKind.Up ? 0 : -1,
            kind is CanvasPointerEventKind.Up or CanvasPointerEventKind.Cancel ? 0 : 1,
            true, point.X + 17, point.Y + 23, 17, 23, false, false, false, false, generation, IsTouch: true));
}
