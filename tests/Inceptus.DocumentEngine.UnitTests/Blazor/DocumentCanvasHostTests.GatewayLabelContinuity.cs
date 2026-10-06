using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task GatewayLabelBoundarySurvivesResizeZoneFrameBeforeCursorPublication()
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution,
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000, 700, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("gateway-continuity", "container");
        var scene = Session(host).CaptureState().CurrentScene!;
        var label = scene.Items.Single(item => item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == BpmnDemoPipeline.ExclusiveGatewayVisualId &&
            item.Metadata.ContainsKey(Canvas2DNodeLabelGestureMetadata.InteractionCapable));
        await PointerObserver(host).ClickDocumentPointAsync(scene, Center(label.Bounds));
        await PointerObserver(host).MoveDocumentPointAsync(Session(host).CaptureState().CurrentScene!, Center(label.Bounds));
        var boundaryKey = $"hover:{label.Id.Value}";
        Assert.Contains(Session(host).CaptureState().CurrentScene!.Items,
            item => item.IsVisible && item.Origin.StableSourceKey == boundaryKey);
        Assert.Equal("grab", host.CaptureState().CssCursor);
        scene = Session(host).CaptureState().CurrentScene!;
        var zone = scene.Items.Single(item => item.Origin.VisualStateId == label.Origin.VisualStateId &&
            item.Origin.StableSourceKey == $"node-label-resize-zone:east:{label.Id.Value}");
        execution.BlockRender = true;
        var movement = Task.Run(() => PointerObserver(host).MoveDocumentPointAsync(scene, Center(zone.Bounds)));
        bool boundaryVisible;
        string cursor;
        try
        {
            // Observe the real frame/cursor ordering without relying on browser timing.
            await execution.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            boundaryVisible = Session(host).CaptureState().CurrentScene!.Items.Any(item =>
                item.IsVisible && item.Origin.StableSourceKey == boundaryKey);
            cursor = host.CaptureState().CssCursor;
        }
        finally
        {
            execution.ReleaseRender();
            await movement.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.True(boundaryVisible,
            $"Exclusive Gateway east-zone frame lost its label boundary before cursor publication (cursor={cursor}).");
        Assert.Equal("ew-resize", host.CaptureState().CssCursor);
        Assert.Contains(Session(host).CaptureState().CurrentScene!.Items,
            item => item.IsVisible && item.Origin.StableSourceKey == boundaryKey);
    }
}
