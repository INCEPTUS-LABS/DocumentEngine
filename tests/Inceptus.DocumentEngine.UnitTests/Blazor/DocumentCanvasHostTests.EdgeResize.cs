using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("hover")]
    [InlineData("down")]
    [InlineData("move")]
    [InlineData("render")]
    public async Task EdgeResizeRenderFailureRetiresFeedbackCaptureAndCursorWithoutCommit(string phase)
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution,
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(2400d, 1600d, 1.5d)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        await EnableOrganizationalProfileAsync(session);
        await AddOrganizationalPoolFromMenuAsync(host);
        var before = session.CaptureState();
        var snapshot = AttachedDocument(session).CaptureSnapshot();
        var target = before.CurrentScene!.SpatialPresentationPlan!.ResizeTargets.First(
            target => target.Edge == Canvas2DSpatialResizeEdge.Bottom);
        var point = new PointD(target.PaintedBounds.Left + 12d, target.PaintedBounds.Bottom);
        var pointer = PointerObserver(host);
        if (phase is "move" or "render")
        {
            await pointer.DownDocumentPointAsync(before.CurrentScene, point);
            Assert.NotNull(session.CaptureState().EditorState.ActiveGesture);
            Assert.Equal("ns-resize", host.CaptureState().CssCursor);
        }
        execution.RenderResult = new Canvas2DInteropOperationResult { Succeeded = false, Code = "TEST_RESIZE_RENDER_FAILED" };
        if (phase == "render")
            Assert.False((await session.RenderCurrentAsync()).Succeeded);
        else if (phase == "down")
            await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, point);
        else
            await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!,
                phase == "hover" ? point : point + new VectorD(0d, 25d), buttons: phase == "hover" ? 0 : 1);
        await WaitForCursorAsync(host, "default");
        if (phase != "hover") await WaitForReleaseCountAsync(pointer, 1);
        await session.WaitForIdleAsync();
        var current = session.CaptureState();
        Assert.Null(current.EditorState.ActiveGesture);
        Assert.DoesNotContain(current.EditorState.TemporaryFeedback, feedback => feedback.SpatialResize is not null);
        Assert.Equal(before.DocumentRevision, current.DocumentRevision);
        Assert.Equal(before.HistoryStatus, current.HistoryStatus);
        Assert.Same(snapshot, AttachedDocument(session).CaptureSnapshot());
        await pointer.UpDocumentPointAsync(before.CurrentScene, point + new VectorD(0d, 40d), 1);
        Assert.Same(snapshot, AttachedDocument(session).CaptureSnapshot());
    }
}
