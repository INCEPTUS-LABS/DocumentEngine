using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseJBlazorPresentationIntegrationTests
{
    [Fact]
    public async Task NeutralHostRunsCompleteSessionPipelineAndResizesWithoutAuthoritativeEffects()
    {
        var execution = new RecordingExecution();
        var renderer = new Canvas2DRenderer(
            execution,
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "org.dejavu.DejaVuSans",
                        "2.37",
                        "DejaVu Sans",
                        "fonts/DejaVuSans-2.37.ttf"),
                ],
                defaultFontFamily: "DejaVu Sans"));
        var observer = new RecordingObserver(new Canvas2DSurfaceSize(800d, 600d, 1d));
        await using var host = new DocumentCanvasHost(
            BpmnModelerTestComposition.NeutralFactory,
            renderer,
            new ObserverFactory(observer));

        await host.InitializeAsync("canvas", "container");
        var initial = host.CaptureState();
        var initialSession = Assert.IsType<EditingSessionState>(initial.Session);
        var initialScene = Assert.IsType<Canvas2D.Scene.Canvas2DScene>(initialSession.CurrentScene);
        var initialRevision = initialSession.DocumentRevision;
        var initialEditor = initialSession.EditorState;
        var initialHistory = initialSession.HistoryStatus;
        var initialGraph = initialSession.ProjectedGraph;
        var initialLayout = initialSession.LayoutResult;
        var initialRouting = initialSession.RoutingResult;
        var counters = Assert.IsType<NeutralDemoPipelineCountersAdapter>(
            initial.PipelineCounters).Counters;

        Assert.Equal(EditingSessionStatus.Ready, initialSession.Status);
        Assert.True(initial.LatestPresentationSucceeded);
        Assert.Equal(5, counters.ProjectionRuleInvocationCount);
        Assert.Equal(1, counters.LayoutInvocationCount);
        Assert.Equal(1, counters.RoutingInvocationCount);
        Assert.Equal(1, counters.SceneContributionInvocationCount);
        Assert.Equal(6, initialScene.Items.Count(item =>
            item.Origin.SemanticElementId is not null &&
            item.Origin.ProjectedObjectId is not null &&
            item.Layer == Contracts.Canvas2D.Canvas2DSceneLayer.Content));
        Assert.Equal(3, initialScene.Items.Count(item =>
            item.Layer == Contracts.Canvas2D.Canvas2DSceneLayer.Content &&
            item.Origin.Categories.HasFlag(
                Contracts.Canvas2D.Canvas2DSceneOriginCategory.RegisteredExtension) &&
            item.PersistentAppearance.Count > 0));
        Assert.Equal(4, initialScene.Items.Count(item =>
            item.Layer == Contracts.Canvas2D.Canvas2DSceneLayer.Connector));

        var resized = new Canvas2DSurfaceSize(960d, 540d, 2d);
        await observer.RaiseAsync(resized);
        var afterResize = host.CaptureState();
        var resizedSession = Assert.IsType<EditingSessionState>(afterResize.Session);

        Assert.Equal(resized, afterResize.SurfaceSize);
        Assert.Equal(["initialize:canvas", "render", "resize", "render"], execution.Calls);
        Assert.Equal(2, afterResize.SuccessfulRenderCount);
        Assert.Equal(initialRevision, resizedSession.DocumentRevision);
        Assert.True(initialEditor.Selection.AsSpan().SequenceEqual(
            resizedSession.EditorState.Selection.AsSpan()));
        Assert.Equal(initialEditor.HoveredObjectId, resizedSession.EditorState.HoveredObjectId);
        Assert.Equal(initialEditor.ActiveToolId, resizedSession.EditorState.ActiveToolId);
        Assert.Equal(initialEditor.FocusTargetId, resizedSession.EditorState.FocusTargetId);
        Assert.Equal(initialEditor.ActiveGesture, resizedSession.EditorState.ActiveGesture);
        Assert.Equal(initialEditor.TemporaryFeedback, resizedSession.EditorState.TemporaryFeedback);
        Assert.Equal(initialEditor.ToolState, resizedSession.EditorState.ToolState);
        Assert.Equal(
            Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(
                resizedSession.EditorState.Viewport,
                resized),
            resizedSession.EditorState.Viewport.VisibleDocumentRegion);
        Assert.Equal(initialHistory, resizedSession.HistoryStatus);
        Assert.Same(initialGraph, resizedSession.ProjectedGraph);
        Assert.Same(initialLayout, resizedSession.LayoutResult);
        Assert.Same(initialRouting, resizedSession.RoutingResult);
        Assert.NotSame(initialScene, resizedSession.CurrentScene);
        Assert.Equal(5, counters.ProjectionRuleInvocationCount);
        Assert.Equal(1, counters.LayoutInvocationCount);
        Assert.Equal(1, counters.RoutingInvocationCount);
        Assert.Equal(2, counters.SceneContributionInvocationCount);
    }

    private sealed class ObserverFactory : ICanvasPresentationSurfaceObserverFactory
    {
        private readonly RecordingObserver _observer;

        internal ObserverFactory(RecordingObserver observer) => _observer = observer;

        public ValueTask<ICanvasPresentationSurfaceObserver> CreateAsync(
            string containerElementId,
            Func<Canvas2DSurfaceSize, Task> onSurfaceChanged,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _observer.Callback = onSurfaceChanged;
            return ValueTask.FromResult<ICanvasPresentationSurfaceObserver>(_observer);
        }
    }

    private sealed class RecordingObserver : ICanvasPresentationSurfaceObserver
    {
        private readonly Canvas2DSurfaceSize _initial;

        internal RecordingObserver(Canvas2DSurfaceSize initial) => _initial = initial;

        internal Func<Canvas2DSurfaceSize, Task>? Callback { get; set; }

        public ValueTask<Canvas2DSurfaceSize> StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_initial);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal Task RaiseAsync(Canvas2DSurfaceSize surfaceSize) =>
            Callback?.Invoke(surfaceSize) ?? Task.CompletedTask;
    }

    private sealed class RecordingExecution : ICanvas2DRenderExecution
    {
        internal List<string> Calls { get; } = [];

        public ValueTask<Canvas2DInteropOperationResult> InitializeAsync(
            string canvasElementId,
            Canvas2DSurfaceSize surfaceSize,
            ImmutableSortedDictionary<string, string> imageResources,
            ImmutableArray<Canvas2DFontResource> fontResources,
            string? defaultFontFamily)
        {
            Calls.Add($"initialize:{canvasElementId}");
            return ValueTask.FromResult(Success());
        }

        public ValueTask<Canvas2DInteropOperationResult> ResizeAsync(Canvas2DSurfaceSize surfaceSize)
        {
            Calls.Add("resize");
            return ValueTask.FromResult(Success());
        }

        public ValueTask<Canvas2DInteropOperationResult> RenderAsync(Canvas2DRenderFrame frame)
        {
            Calls.Add("render");
            return ValueTask.FromResult(Success());
        }

        public ValueTask<Canvas2DTextMeasurementInteropResult> MeasureTextAsync(
            Canvas2DTextMeasurementRequestData request)
        {
            var width = request.Text.Sum(character => character switch
            {
                ' ' => request.FontSize * 0.33d,
                >= 'A' and <= 'Z' => request.FontSize * 0.62d,
                >= 'a' and <= 'z' => request.FontSize * 0.54d,
                >= '0' and <= '9' => request.FontSize * 0.55d,
                _ => request.FontSize * 0.58d,
            });
            var ascent = request.FontSize * 0.75d;
            var descent = request.FontSize * 0.25d;
            return ValueTask.FromResult(new Canvas2DTextMeasurementInteropResult
            {
                Succeeded = true,
                Width = width,
                Ascent = ascent,
                Descent = descent,
                LineHeight = request.LineHeight,
                BoundingX = 0d,
                BoundingY = -ascent,
                BoundingWidth = width,
                BoundingHeight = ascent + descent,
                ResolvedFontIdentity = $"{request.FontIdentity}@{request.FontVersion}",
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static Canvas2DInteropOperationResult Success() => new() { Succeeded = true };
    }
}
