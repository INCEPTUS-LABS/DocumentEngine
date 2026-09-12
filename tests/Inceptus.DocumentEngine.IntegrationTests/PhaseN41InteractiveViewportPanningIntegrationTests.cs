using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using PlacementHarness = Inceptus.DocumentEngine.IntegrationTests.PhaseN1ToolboxPlacementIntegrationTests.PlacementHarness;
using SequenceIdentityProvider = Inceptus.DocumentEngine.IntegrationTests.PhaseN1ToolboxPlacementIntegrationTests.SequenceIdentityProvider;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN41InteractiveViewportPanningIntegrationTests
{
    [Fact]
    public async Task RemoteContentBecomesVisibleAndEditableWithoutPersistentOrHistoryPanWork()
    {
        var identity = new DocumentCreationIdentity(
            new SemanticElementId("test:bpmn:n41:remote-semantic"),
            new VisualStateId("test:bpmn:n41:remote-visual"));
        await using var harness = await PlacementHarness.CreateAsync(
            devicePixelRatio: 1.25d,
            identities: new SequenceIdentityProvider(identity));
        var initialScene = Assert.IsType<Canvas2DScene>(
            harness.Session.CaptureState().CurrentScene);
        var remoteCenter = new PointD(1600d, 1200d);
        var offscreenCss = initialScene.ViewportTransform.TransformPoint(remoteCenter);
        Assert.True(offscreenCss.X > harness.SurfaceSize.CssWidth);
        Assert.True(offscreenCss.Y > harness.SurfaceSize.CssHeight);
        Assert.True(harness.Selection.Select(harness.Item(BpmnSemanticTypes.Task).ItemId));

        var placement = await harness.Controller.TryPlaceAtCssPointAsync(
            harness.Session,
            offscreenCss);
        await harness.WaitForIdleAsync();

        Assert.True(placement.IsCommitted);
        var persistentBounds = new RectD(1540d, 1160d, 120d, 80d);
        var created = Assert.Single(
            harness.Composition.Document.VisualModel.VisualStates,
            visual => visual.Id == identity.VisualStateId);
        Assert.Equal(persistentBounds.TopLeft, created.Position);
        Assert.Equal(persistentBounds.Size, created.Size);
        var beforePan = harness.Session.CaptureState();
        var documentBeforePan = harness.Composition.Document.CaptureSnapshot();
        var historyBeforePan = beforePan.HistoryStatus;
        var eventCountBeforePan = harness.Events.Events.Count;
        var fullRunsBeforePan = harness.Pipeline.FullRunCount;
        var layoutPreservingRunsBeforePan = harness.Pipeline.NodeLayoutPreservingRunCount;
        var sceneRunsBeforePan = harness.Pipeline.SceneOnlyRunCount;

        var pan = await harness.Session.PanViewportAsync(new VectorD(-1150d, -900d));
        var afterPan = harness.Session.CaptureState();

        Assert.True(pan.Succeeded);
        Assert.Equal(new VectorD(-1150d, -900d), afterPan.EditorState.Viewport.Pan);
        Assert.Equal(beforePan.EditorState.Viewport.Zoom, afterPan.EditorState.Viewport.Zoom);
        Assert.Equal(documentBeforePan, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(beforePan.DocumentRevision, afterPan.DocumentRevision);
        Assert.Equal(historyBeforePan, afterPan.HistoryStatus);
        Assert.Equal(eventCountBeforePan, harness.Events.Events.Count);
        Assert.Equal(fullRunsBeforePan, harness.Pipeline.FullRunCount);
        Assert.Equal(layoutPreservingRunsBeforePan, harness.Pipeline.NodeLayoutPreservingRunCount);
        Assert.Equal(sceneRunsBeforePan + 1, harness.Pipeline.SceneOnlyRunCount);
        Assert.Same(beforePan.ProjectedGraph, afterPan.ProjectedGraph);
        Assert.Same(beforePan.LayoutResult, afterPan.LayoutResult);
        Assert.Same(beforePan.RoutingResult, afterPan.RoutingResult);
        var scene = Assert.IsType<Canvas2DScene>(afterPan.CurrentScene);
        var body = scene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == identity.VisualStateId &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        Assert.Equal(persistentBounds, body.Bounds);
        var visibleCss = scene.ViewportTransform.TransformPoint(remoteCenter);
        Assert.Equal(new PointD(450d, 300d), visibleCss);
        var documentPoint = Canvas2DRenderer.ConvertCssToDocument(scene, visibleCss);
        Assert.Equal(remoteCenter, documentPoint);
        var hit = new Canvas2DSceneHitTestService().HitTest(scene, documentPoint);
        Assert.NotNull(hit);
        Assert.Equal(identity.VisualStateId, hit.Origin.VisualStateId);

        await using var interaction = new Canvas2DInteractionController(harness.Session);
        var selection = await interaction.PointerActivatedAsync(visibleCss);
        Assert.Contains(
            selection.Status,
            new[] { Canvas2DInteractionStatus.Updated, Canvas2DInteractionStatus.Unchanged });
        Assert.Equal(hit.SceneObjectId, selection.TargetId);
        Assert.Equal(identity.VisualStateId,
            Assert.Single(harness.Session.CaptureState().EditorState.Selection));

        Assert.True((await harness.Session.UndoAsync()).IsCommitted);
        await harness.WaitForIdleAsync();
        Assert.DoesNotContain(
            harness.Composition.Document.VisualModel.VisualStates,
            visual => visual.Id == identity.VisualStateId);
        Assert.True((await harness.Session.RedoAsync()).IsCommitted);
        await harness.WaitForIdleAsync();
        Assert.Contains(
            harness.Composition.Document.VisualModel.VisualStates,
            visual => visual.Id == identity.VisualStateId &&
                visual.Position == persistentBounds.TopLeft &&
                visual.Size == persistentBounds.Size);
    }

    [Fact]
    public async Task N4NodesRemainHitTestableAndSelectableAfterPan()
    {
        await using var harness = await PlacementHarness.CreateAsync();
        var documentBefore = harness.Composition.Document.CaptureSnapshot();
        var before = harness.Session.CaptureState();
        Assert.True((await harness.Session.PanViewportAsync(
            new VectorD(-240.5d, 135.25d))).Succeeded);
        await using var interaction = new Canvas2DInteractionController(harness.Session);
        VisualStateId[] n4Visuals =
        [
            BpmnDemoPipeline.MessageCatchEventVisualId,
            BpmnDemoPipeline.TimerCatchEventVisualId,
            BpmnDemoPipeline.EventBasedGatewayVisualId,
        ];

        foreach (var visualStateId in n4Visuals)
        {
            var scene = Assert.IsType<Canvas2DScene>(
                harness.Session.CaptureState().CurrentScene);
            var body = scene.Items.Last(item =>
                item.Layer == Canvas2DSceneLayer.Content &&
                item.Origin.VisualStateId == visualStateId &&
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
            var center = new PointD(
                body.Bounds.X + (body.Bounds.Width / 2d),
                body.Bounds.Y + (body.Bounds.Height / 2d));
            var cssPoint = scene.ViewportTransform.TransformPoint(center);

            var result = await interaction.PointerActivatedAsync(cssPoint);

            Assert.Contains(
                result.Status,
                new[] { Canvas2DInteractionStatus.Updated, Canvas2DInteractionStatus.Unchanged });
            Assert.Equal(visualStateId,
                Assert.Single(harness.Session.CaptureState().EditorState.Selection));
        }

        var after = harness.Session.CaptureState();
        Assert.Equal(documentBefore, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(before.EditorState.Viewport.Zoom, after.EditorState.Viewport.Zoom);
        Assert.Equal(before.EditorState.Viewport.Pan + new VectorD(-240.5d, 135.25d),
            after.EditorState.Viewport.Pan);
    }
}
