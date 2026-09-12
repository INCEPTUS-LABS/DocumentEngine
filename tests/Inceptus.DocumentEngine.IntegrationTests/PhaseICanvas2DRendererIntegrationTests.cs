using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.EditorState;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseICanvas2DRendererIntegrationTests
{
    [Fact]
    public async Task NeutralPipelineRendersDeterministicallyWithoutAuthoritativeOrSessionEffects()
    {
        var (document, scene, inputEditorState) =
            PhaseHCanvas2DSceneIntegrationTests.CreatePhaseIRendererInput();
        var documentBefore = document.CaptureSnapshot();
        var revisionBefore = document.Revision;
        var inputEditorStateBefore = inputEditorState;
        var sceneItemsBefore = scene.Items.ToArray();
        var sceneHashBefore = scene.GetHashCode();
        var viewportBefore = scene.Viewport;
        var viewportTransformBefore = scene.ViewportTransform;
        var history = new HistoryManager(document);
        var historyBefore = history.CaptureStatus();
        var editorState = new EditorStateStore();
        var editorBefore = editorState.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);
        var execution = new RecordingExecution();
        var rendererConfiguration = new Canvas2DRendererConfiguration(
            fontResources:
            [
                new Canvas2DFontResource(
                    "test:font:sans",
                    "1",
                    "sans-serif",
                    "/fonts/sans-400-normal.woff2"),
            ],
            defaultFontFamily: "sans-serif");
        await using var renderer = new Canvas2DRenderer(execution, rendererConfiguration);

        var initialized = await renderer.InitializeAsync(
            "neutral-canvas",
            new Canvas2DSurfaceSize(800d, 600d, 2d));
        var first = await renderer.RenderAsync(scene);
        var firstFingerprint = execution.LastFingerprint;
        var second = await renderer.RenderAsync(scene);

        Assert.True(initialized.Succeeded);
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(firstFingerprint, execution.LastFingerprint);
        Assert.Equal(2, execution.RenderCount);
        Assert.Equal(2d, Assert.IsType<Canvas2DSurfaceSize>(execution.LastSurface).DevicePixelRatio);
        Assert.Equal(3, scene.Items.Count(item => item.Layer == Canvas2DSceneLayer.Content));
        Assert.Equal(4, scene.Items.Count(item => item.Layer == Canvas2DSceneLayer.Connector));
        Assert.Equal(2, scene.Items.Count(item =>
            item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow)));
        Assert.Single(scene.Items.Where(item => item.Geometry.Kind == Canvas2DSceneGeometryKind.Text));
        Assert.Equal(9, scene.Items.Count(item =>
            item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.EditorState)));
        Assert.Contains(scene.Items, item =>
            item.PersistentAppearance.TryGetValue("test:fill", out var value) &&
            value.TextValue == "#dbeafe");
        var frame = Assert.IsType<Canvas2DRenderFrame>(execution.LastFrame);
        Assert.Equal(1.5d, frame.ViewportTransform.M11);
        Assert.Equal(1.5d, frame.ViewportTransform.M22);
        Assert.Equal(25d, frame.ViewportTransform.OffsetX);
        Assert.Equal(15d, frame.ViewportTransform.OffsetY);
        var editorOverlayIndex = Array.FindIndex(
            scene.Items.ToArray(),
            item => item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.EditorState));
        var lastPersistentContentIndex = Array.FindLastIndex(
            scene.Items.ToArray(),
            item => item.Layer is Canvas2DSceneLayer.Content or Canvas2DSceneLayer.Connector);
        Assert.True(editorOverlayIndex > lastPersistentContentIndex);
        Assert.Equal(
            scene.Items.Select(item => item.Id.Value),
            frame.Items.Select(item => item.Id));
        Assert.Equal(documentBefore, document.CaptureSnapshot());
        Assert.Equal(revisionBefore, document.Revision);
        Assert.Equal(historyBefore, history.CaptureStatus());
        Assert.Equal(editorBefore, editorState.CaptureSnapshot());
        Assert.Equal(inputEditorStateBefore, inputEditorState);
        Assert.Equal(sceneItemsBefore, scene.Items);
        Assert.Equal(sceneHashBefore, scene.GetHashCode());
        Assert.Equal(viewportBefore, scene.Viewport);
        Assert.Equal(viewportTransformBefore, scene.ViewportTransform);
        Assert.Equal(0, subscriber.EventCount);
        GC.KeepAlive(processor);
    }

    private sealed class RecordingExecution : ICanvas2DRenderExecution
    {
        internal int RenderCount { get; private set; }

        internal string? LastFingerprint { get; private set; }

        internal Canvas2DSurfaceSize? LastSurface { get; private set; }

        internal Canvas2DRenderFrame? LastFrame { get; private set; }

        public ValueTask<Canvas2DInteropOperationResult> InitializeAsync(
            string canvasElementId,
            Canvas2DSurfaceSize surfaceSize,
            ImmutableSortedDictionary<string, string> imageResources,
            ImmutableArray<Canvas2DFontResource> fontResources,
            string? defaultFontFamily)
        {
            LastSurface = surfaceSize;
            return ValueTask.FromResult(new Canvas2DInteropOperationResult { Succeeded = true });
        }

        public ValueTask<Canvas2DInteropOperationResult> ResizeAsync(
            Canvas2DSurfaceSize surfaceSize) =>
            ValueTask.FromResult(new Canvas2DInteropOperationResult { Succeeded = true });

        public ValueTask<Canvas2DInteropOperationResult> RenderAsync(Canvas2DRenderFrame frame)
        {
            RenderCount++;
            LastFrame = frame;
            LastFingerprint = string.Join(
                Environment.NewLine,
                frame.Items.Select(item => string.Join(
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
                    item.Opacity,
                    item.IsVisible)));
            return ValueTask.FromResult(new Canvas2DInteropOperationResult { Succeeded = true });
        }

        public ValueTask<Canvas2DTextMeasurementInteropResult> MeasureTextAsync(
            Canvas2DTextMeasurementRequestData request) =>
            ValueTask.FromResult(new Canvas2DTextMeasurementInteropResult
            {
                Succeeded = false,
                Code = TextMetricsDiagnosticCodes.MeasurementFailure,
            });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        private int _eventCount;

        internal int EventCount => Volatile.Read(ref _eventCount);

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Interlocked.Increment(ref _eventCount);
            return ValueTask.CompletedTask;
        }
    }
}
