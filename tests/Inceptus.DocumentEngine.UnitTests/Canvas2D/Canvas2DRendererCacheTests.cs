using System.Collections.Immutable;
using System.Text.Json;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DRendererCacheTests
{
    [Theory]
    [InlineData("items")]
    [InlineData("order")]
    [InlineData("style")]
    [InlineData("text")]
    [InlineData("path")]
    [InlineData("overlay")]
    [InlineData("transform")]
    [InlineData("clip")]
    [InlineData("visibility")]
    public async Task ChangedDrawingInputsRequireCompleteReplacement(string change)
    {
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var scene = EditingSessionTestHarness.CreateScene(Canvas2DSceneTestData.Create());
        Assert.True((await renderer.RenderAsync(scene)).Succeeded);
        var initial = execution.LastFrame!;
        var items = scene.Items.ToArray();
        var item = items[0];
        if (change == "order")
        {
            Array.Reverse(items);
        }
        else
        {
            items[0] = new Canvas2DSceneItem(item.Id,
                change == "overlay" ? Canvas2DSceneLayer.Overlay : item.Layer, item.ZIndex,
                change switch
                {
                    "text" => Canvas2DSceneGeometry.Text(new RectD(0, 0, 50, 20), "Changed"),
                    "path" => Canvas2DSceneGeometry.Path([new PointD(1, 2), new PointD(3, 4)]),
                    _ => item.Geometry,
                }, item.Origin,
                transform: change == "transform" ? Matrix2D.CreateTranslation(8, 9) : item.Transform,
                clip: change == "clip" ? new RectD(1, 2, 3, 4) : item.Clip,
                style: change == "style" ? new Canvas2DSceneStyle(fill: "#123456") : item.Style,
                isVisible: change == "visibility" ? !item.IsVisible : item.IsVisible);
        }
        var changed = Copy(scene, items.ToImmutableArray());
        Assert.True((await renderer.RenderAsync(changed)).Succeeded);
        Assert.NotSame(initial, execution.LastFrame);
        Assert.True(execution.LastFrame!.ContentVersion > initial.ContentVersion);
        Assert.Equal(items.Select(candidate => candidate.Id.Value), execution.LastFrame.Items.Select(candidate => candidate.Id));
        Assert.True((await renderer.RenderAsync(changed)).Succeeded);
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));
        Assert.Single(execution.Calls, call => call == "renderViewport");
    }

    [Fact]
    public async Task PanSharesContentAcrossGuideAppearanceAndDisappearance()
    {
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        var builder = new Canvas2DSceneBuilder();
        EditorStateSnapshot Editor(VectorD pan) => new(viewport: new ViewportSnapshot(1, pan,
            Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(new ViewportSnapshot(1, pan), new Canvas2DSurfaceSize(600, 400, 1))));
        var scene = Assert.IsType<Canvas2DScene>(builder.Build(document,
            document.SemanticModel.RootScopeId, ModelProfileViewStateSnapshot.Empty,
            ModelProfileElementViewStateSnapshot.Empty, inputs.Graph, inputs.Layout, inputs.Routing,
            document.VisualModel, Editor(new VectorD(-100, -100))).Scene);
        Assert.True((await renderer.RenderAsync(scene)).Succeeded);
        foreach (var pan in new[] { new VectorD(-110, -120), new VectorD(-130, -140),
                     new VectorD(0, 0), new VectorD(-160, -170), new VectorD(-180, -190) })
        {
            var next = Assert.IsType<Canvas2DScene>(builder.TryReuseForPan(scene, document,
                document.SemanticModel.RootScopeId, ModelProfileViewStateSnapshot.Empty,
                ModelProfileElementViewStateSnapshot.Empty, inputs.Graph, inputs.Layout, inputs.Routing, Editor(pan)));
            var shared = scene.Items == next.Items;
            Assert.True(shared);
            Assert.True((await renderer.RenderAsync(next)).Succeeded);
            Assert.Equal(shared ? "renderViewport" : "render", execution.Calls[^1]);
            scene = next;
        }
        Assert.Equal(1, execution.Calls.Count(call => call == "render"));
        Assert.Equal(5, execution.Calls.Count(call => call == "renderViewport"));
        Assert.Equal(scene.ViewportTransform.OffsetX, execution.LastViewportFrame!.ViewportTransform.OffsetX);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedUploadOrViewportRequiresFullRecovery(bool viewportFailure)
    {
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var scene = EditingSessionTestHarness.CreateScene(Canvas2DSceneTestData.Create());
        if (viewportFailure) Assert.True((await renderer.RenderAsync(scene)).Succeeded);
        execution.RenderResult = Canvas2DRendererTestExecution.Failure(Canvas2DRendererDiagnosticCodes.RenderingFailed);
        Assert.False((await renderer.RenderAsync(scene)).Succeeded);
        execution.RenderResult = Canvas2DRendererTestExecution.Success();
        Assert.True((await renderer.RenderAsync(scene)).Succeeded);
        Assert.Equal("render", execution.Calls[^1]);
        Assert.True((await renderer.RenderAsync(scene)).Succeeded);
        Assert.Equal("renderViewport", execution.Calls[^1]);
    }

    [Theory]
    [InlineData(800, 600, 1)]
    [InlineData(640, 480, 2)]
    public async Task SurfaceObservationRequiresFullUpload(double width, double height, double dpr)
    {
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var scene = EditingSessionTestHarness.CreateScene(Canvas2DSceneTestData.Create());
        await renderer.RenderAsync(scene);
        Assert.True((await renderer.ResizeAsync(new Canvas2DSurfaceSize(width, height, dpr))).Succeeded);
        Assert.True((await renderer.RenderAsync(scene)).Succeeded);
        Assert.Equal("render", execution.Calls[^1]);
        await renderer.RenderAsync(scene);
        Assert.Equal("renderViewport", execution.Calls[^1]);
    }

    [Fact]
    public async Task InstancesDisposalAndRecreationCannotShareAcknowledgments()
    {
        var (first, a) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var (second, b) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = second;
        var scene = EditingSessionTestHarness.CreateScene(Canvas2DSceneTestData.Create());
        await first.RenderAsync(scene);
        await second.RenderAsync(scene);
        Assert.Equal("render", a.Calls[^1]);
        Assert.Equal("render", b.Calls[^1]);
        await first.DisposeAsync();
        Assert.False((await first.RenderAsync(scene)).Succeeded);
        Assert.True((await second.RenderAsync(scene)).Succeeded);
        Assert.Equal("renderViewport", b.Calls[^1]);
        var (third, c) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var thirdLifetime = third;
        await third.RenderAsync(scene);
        Assert.Equal("render", c.Calls[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedFullOrViewportCompletesBeforeNewContentUnderExistingGate(bool cached)
    {
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var scene = EditingSessionTestHarness.CreateScene(Canvas2DSceneTestData.Create());
        if (cached) await renderer.RenderAsync(scene);
        execution.RenderEnteredSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        execution.RenderReleaseSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = renderer.RenderAsync(scene).AsTask();
        await execution.RenderEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var changed = Copy(scene, scene.Items.Reverse().ToImmutableArray());
        var b = renderer.RenderAsync(changed).AsTask();
        Assert.False(b.IsCompleted);
        execution.RenderReleaseSignal.SetResult();
        Assert.True((await a).Succeeded);
        Assert.True((await b).Succeeded);
        Assert.Equal(changed.Items.Select(item => item.Id.Value), execution.LastFrame!.Items.Select(item => item.Id));
        await renderer.RenderAsync(changed);
        Assert.Equal(execution.LastFrame.ContentVersion, execution.LastViewportFrame!.ContentVersion);
        Assert.True(execution.LastViewportFrame.PresentationVersion > execution.LastFrame.PresentationVersion);
    }

    [Fact]
    public async Task DisposalDuringRenderRejectsSubsequentlyQueuedRender()
    {
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var scene = EditingSessionTestHarness.CreateScene(Canvas2DSceneTestData.Create());
        execution.RenderEnteredSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        execution.RenderReleaseSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var render = renderer.RenderAsync(scene).AsTask();
        await execution.RenderEnteredSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var dispose = renderer.DisposeAsync().AsTask();
        var queued = renderer.RenderAsync(scene).AsTask();
        execution.RenderReleaseSignal.SetResult();
        await render;
        await dispose;
        Assert.False((await queued).Succeeded);
        Assert.Single(execution.Calls, call => call == "render");
        Assert.DoesNotContain("renderViewport", execution.Calls);
    }

    [Fact]
    public async Task ViewportPayloadContainsTokensMatrixAndBoundedPresentationOnly()
    {
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var scene = EditingSessionTestHarness.CreateScene(Canvas2DSceneTestData.Create());
        await renderer.RenderAsync(scene);
        await renderer.RenderAsync(scene);
        var json = JsonSerializer.SerializeToElement(execution.LastViewportFrame);
        Assert.Equal(["ViewportTransform", "ContentVersion", "PresentationVersion", "ViewportLines", "PresentationItems"], json.EnumerateObject().Select(property => property.Name));
        Assert.Equal(6, json.GetProperty("ViewportTransform").EnumerateObject().Count());
        Assert.Equal(0, json.GetProperty("ViewportLines").GetArrayLength());
        Assert.Equal(0, json.GetProperty("PresentationItems").GetArrayLength());
    }

    private static Canvas2DScene Copy(Canvas2DScene scene, ImmutableArray<Canvas2DSceneItem> items) => new(
        scene.DocumentId, scene.SourceRevision, scene.LayoutAlgorithmId, scene.RoutingAlgorithmId,
        scene.Configuration, scene.Contributors, scene.Viewport, scene.ViewportTransform,
        scene.ActiveToolId, scene.FocusTargetId, scene.ToolState, scene.ContributorMetadata, items);
}
