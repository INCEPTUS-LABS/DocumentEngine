using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class EditingSessionViewportTests
{
    [Theory]
    [InlineData(12.5d, -7.25d)]
    [InlineData(-2.4d, -13.2d)]
    public async Task RelativePanUsesCanvasTranslationAndChangesOnlyViewportSceneAndRender(
        double translationX,
        double translationY)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var surface = new Canvas2DSurfaceSize(800d, 450d, 1.25d);
        var initialViewport = new ViewportSnapshot(0.8d, new VectorD(30d, -20d));
        var initialEditorState = new EditorStateSnapshot(
            selection: [inputs.VisualModel.VisualStates[0].Id],
            viewport: new ViewportSnapshot(
                initialViewport.Zoom,
                initialViewport.Pan,
                Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(initialViewport, surface)));
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var documentBefore = document.CaptureSnapshot();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, initialEditorState));
        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initialEditorState),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();

        var result = await session.PanViewportAsync(new VectorD(translationX, translationY));
        var after = session.CaptureState();

        Assert.True(result.Succeeded);
        Assert.Equal(initialViewport.Zoom, after.EditorState.Viewport.Zoom);
        Assert.Equal(
            initialViewport.Pan + new VectorD(translationX, translationY),
            after.EditorState.Viewport.Pan);
        Assert.True(before.EditorState.Selection.AsSpan().SequenceEqual(
            after.EditorState.Selection.AsSpan()));
        Assert.Equal(documentBefore, document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
        Assert.Same(before.LayoutResult, after.LayoutResult);
        Assert.Same(before.RoutingResult, after.RoutingResult);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.SceneRebuildCount);
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task ZeroRelativePanIsNoOp()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();

        var result = await session.PanViewportAsync(default);
        var after = session.CaptureState();

        Assert.True(result.Succeeded);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Equal(0, pipeline.SceneRebuildCount);
        Assert.Equal(1, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task ChangedViewportRebuildsOnlySceneAndPreservesUnrelatedEditorState()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var target = EditingSessionTestHarness.CreateScene(inputs).Items.First(item =>
            item.Origin.VisualStateId is not null &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        var visibleRegion = Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(
            new ViewportSnapshot(1.25d, new VectorD(18d, -7d)),
            new Canvas2DSurfaceSize(800d, 450d, 1d));
        var initialEditorState = new EditorStateSnapshot(
            selection: [target.Origin.VisualStateId!],
            hoveredObjectId: target.Id,
            activeToolId: "tool:viewport-test",
            focusTargetId: "focus:canvas",
            viewport: new ViewportSnapshot(
                1.25d,
                new VectorD(18d, -7d),
                visibleRegion),
            temporaryFeedback:
            [
                new EditorFeedbackSnapshot(
                    "feedback:viewport-test",
                    "test-feedback",
                    new RectD(1d, 2d, 3d, 4d)),
            ],
            toolState: [new("snap", PropertyValue.FromBoolean(true))]);
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var documentBefore = document.CaptureSnapshot();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, initialEditorState));
        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initialEditorState),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();
        var requested = new ViewportSnapshot(
            1.75d,
            new VectorD(-32.5d, 41.25d),
            new RectD(999d, 999d, 1d, 1d));

        var result = await session.UpdateViewportAsync(requested);
        var after = session.CaptureState();

        Assert.True(result.Succeeded);
        Assert.Equal(EditingSessionStatus.Ready, after.Status);
        Assert.NotSame(before.CurrentScene, after.CurrentScene);
        Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
        Assert.Same(before.LayoutResult, after.LayoutResult);
        Assert.Same(before.RoutingResult, after.RoutingResult);
        Assert.Equal(requested.Zoom, after.EditorState.Viewport.Zoom);
        Assert.Equal(requested.Pan, after.EditorState.Viewport.Pan);
        Assert.Equal(
            Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(
                requested,
                new Canvas2DSurfaceSize(800d, 450d, 1d)),
            after.EditorState.Viewport.VisibleDocumentRegion);
        Assert.Equal(after.EditorState.Viewport, after.CurrentScene!.Viewport);
        Assert.Contains(before.CurrentScene!.Items, item =>
            StringComparer.Ordinal.Equals(
                item.Origin.StableSourceKey,
                "document-boundary:y-axis"));
        Assert.DoesNotContain(before.CurrentScene.Items, item =>
            StringComparer.Ordinal.Equals(
                item.Origin.StableSourceKey,
                "document-boundary:x-axis"));
        Assert.DoesNotContain(after.CurrentScene.Items, item =>
            StringComparer.Ordinal.Equals(
                item.Origin.StableSourceKey,
                "document-boundary:y-axis"));
        Assert.Contains(after.CurrentScene.Items, item =>
            StringComparer.Ordinal.Equals(
                item.Origin.StableSourceKey,
                "document-boundary:x-axis"));
        Assert.Equal(
            new PointD(requested.Pan.X, requested.Pan.Y),
            after.CurrentScene.ViewportTransform.TransformPoint(default));
        Assert.True(initialEditorState.Selection.AsSpan().SequenceEqual(
            after.EditorState.Selection.AsSpan()));
        Assert.Equal(initialEditorState.HoveredObjectId, after.EditorState.HoveredObjectId);
        Assert.Equal(initialEditorState.ActiveToolId, after.EditorState.ActiveToolId);
        Assert.Equal(initialEditorState.FocusTargetId, after.EditorState.FocusTargetId);
        Assert.Null(after.EditorState.ActiveGesture);
        Assert.True(initialEditorState.TemporaryFeedback.AsSpan().SequenceEqual(
            after.EditorState.TemporaryFeedback.AsSpan()));
        Assert.Equal(initialEditorState.ToolState, after.EditorState.ToolState);
        Assert.Equal(new HistoryStatus(0, false, false), after.HistoryStatus);
        Assert.Equal(documentBefore, document.CaptureSnapshot());
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.SceneRebuildCount);
        Assert.Same(before.ProjectedGraph, Assert.Single(pipeline.SceneArtifacts).ProjectedGraph);
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task StructurallyUnchangedViewportIsNoOpAndPreservesVisibleRegion()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var visibleRegion = Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(
            new ViewportSnapshot(1.5d, new VectorD(20d, -30d)),
            new Canvas2DSurfaceSize(1200d, 750d, 1d));
        var initialEditorState = new EditorStateSnapshot(
            activeToolId: "tool:select",
            viewport: new ViewportSnapshot(
                1.5d,
                new VectorD(20d, -30d),
                visibleRegion));
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, initialEditorState));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initialEditorState),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();

        var result = await session.UpdateViewportAsync(new ViewportSnapshot(
            initialEditorState.Viewport.Zoom,
            initialEditorState.Viewport.Pan,
            new RectD(1d, 1d, 1d, 1d)));
        var after = session.CaptureState();

        Assert.True(result.Succeeded);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Equal(visibleRegion, after.EditorState.Viewport.VisibleDocumentRegion);
        Assert.Equal(0, pipeline.SceneRebuildCount);
        Assert.Equal(1, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task ActivePersistentGestureRejectsViewportUpdateWithoutMutation()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var activeGesture = new EditorGestureSnapshot(
            "gesture:viewport-test",
            "test:persistent-gesture",
            new PointD(10d, 20d),
            new PointD(30d, 40d));
        var initialEditorState = new EditorStateSnapshot(
            viewport: new ViewportSnapshot(1.25d, new VectorD(4d, 8d)),
            activeGesture: activeGesture);
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var documentBefore = document.CaptureSnapshot();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, initialEditorState));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initialEditorState),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();

        var result = await session.UpdateViewportAsync(
            new ViewportSnapshot(2d, new VectorD(100d, 50d)));
        var after = session.CaptureState();

        Assert.Equal(EditingSessionOperationStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == EditingSessionDiagnosticCodes.InvalidOperation);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Same(activeGesture, after.EditorState.ActiveGesture);
        Assert.Equal(documentBefore, document.CaptureSnapshot());
        Assert.Equal(0, pipeline.SceneRebuildCount);
        Assert.Equal(1, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task RuntimeFaultedSessionRejectsViewportUpdateWithoutChangingEditorState()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var initialEditorState = new EditorStateSnapshot(
            viewport: new ViewportSnapshot(1.25d, new VectorD(4d, 8d)));
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Failure());
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initialEditorState),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();

        var result = await session.UpdateViewportAsync(ViewportSnapshot.Default);
        var after = session.CaptureState();

        Assert.Equal(EditingSessionAttachStatus.RuntimeFaulted, attachment.Status);
        Assert.Equal(EditingSessionOperationStatus.Rejected, result.Status);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Null(after.CurrentScene);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, after.Status);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(0, pipeline.SceneRebuildCount);
        Assert.DoesNotContain("render", execution.Calls);
    }

    [Fact]
    public async Task PreCancelledViewportUpdateDoesNoWork()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await session.UpdateViewportAsync(
            new ViewportSnapshot(2d, new VectorD(100d, 50d)),
            cancellation.Token);
        var after = session.CaptureState();

        Assert.Equal(EditingSessionOperationStatus.Cancelled, result.Status);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Equal(0, pipeline.SceneRebuildCount);
        Assert.Equal(1, execution.Calls.Count(call => call == "render"));
    }
}
