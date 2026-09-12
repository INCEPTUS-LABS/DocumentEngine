using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseM3BpmnToolboxIntegrationTests
{
    [Fact]
    public async Task BpmnTaskToolboxSelectionAndCanvasClickCreateNoDocumentWork()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var execution = new RecordingRenderExecution();
        var renderer = new Canvas2DRenderer(
            execution,
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "demo:font:dejavu",
                        "2.37",
                        "DejaVu Sans",
                        "/fonts/DejaVuSans-2.37.ttf"),
                ],
                defaultFontFamily: "DejaVu Sans"));
        var initialized = await renderer.InitializeAsync(
            "phase-m3-toolbox-canvas",
            new Canvas2DSurfaceSize(900d, 600d, 1.25d));
        Assert.True(initialized.Succeeded);
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        Assert.True(attachment.IsAttached);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await session.WaitForIdleAsync();

        var catalog = new ToolboxCatalog(BpmnPluginRegistration.M3.ToolboxContributions);
        var taskItem = Assert.Single(catalog.Items, item =>
            item.ElementTypeId == BpmnSemanticTypes.Task);
        var selection = new ToolboxSelectionState();
        var documentBefore = composition.Document.CaptureSnapshot();
        var taskCountBefore = documentBefore.SemanticModel.Elements.Count(element =>
            element.TypeId == BpmnSemanticTypes.Task);
        var stateBefore = session.CaptureState();
        var renderCountBefore = execution.RenderCount;

        Assert.True(selection.Select(taskItem.ItemId));

        var afterSelection = session.CaptureState();
        Assert.Equal(taskItem.ItemId, selection.SelectedItemId);
        Assert.Equal(documentBefore, composition.Document.CaptureSnapshot());
        Assert.Equal(stateBefore.DocumentRevision, afterSelection.DocumentRevision);
        Assert.Equal(stateBefore.HistoryStatus, afterSelection.HistoryStatus);
        Assert.Same(stateBefore.EditorState, afterSelection.EditorState);
        Assert.Same(stateBefore.ProjectedGraph, afterSelection.ProjectedGraph);
        Assert.Same(stateBefore.LayoutResult, afterSelection.LayoutResult);
        Assert.Same(stateBefore.RoutingResult, afterSelection.RoutingResult);
        Assert.Same(stateBefore.CurrentScene, afterSelection.CurrentScene);
        Assert.Equal(renderCountBefore, execution.RenderCount);

        await using var interaction = new Canvas2DInteractionController(session);
        var canvasClick = await interaction.PointerActivatedAsync(new PointD(-200d, -200d));
        await session.WaitForIdleAsync();
        var afterCanvasClick = session.CaptureState();

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, canvasClick.Status);
        Assert.Equal(taskItem.ItemId, selection.SelectedItemId);
        Assert.Equal(documentBefore, composition.Document.CaptureSnapshot());
        Assert.Equal(stateBefore.DocumentRevision, afterCanvasClick.DocumentRevision);
        Assert.Equal(stateBefore.HistoryStatus, afterCanvasClick.HistoryStatus);
        Assert.Same(stateBefore.ProjectedGraph, afterCanvasClick.ProjectedGraph);
        Assert.Same(stateBefore.LayoutResult, afterCanvasClick.LayoutResult);
        Assert.Same(stateBefore.RoutingResult, afterCanvasClick.RoutingResult);
        Assert.Same(stateBefore.CurrentScene, afterCanvasClick.CurrentScene);
        Assert.Equal(renderCountBefore, execution.RenderCount);
        Assert.Equal(
            taskCountBefore,
            composition.Document.SemanticModel.Elements.Count(element =>
                element.TypeId == BpmnSemanticTypes.Task));
    }

    private sealed class RecordingRenderExecution : ICanvas2DRenderExecution
    {
        private int _renderCount;

        internal int RenderCount => Volatile.Read(ref _renderCount);

        public ValueTask<Canvas2DInteropOperationResult> InitializeAsync(
            string canvasElementId,
            Canvas2DSurfaceSize surfaceSize,
            ImmutableSortedDictionary<string, string> imageResources,
            ImmutableArray<Canvas2DFontResource> fontResources,
            string? defaultFontFamily) =>
            ValueTask.FromResult(Success());

        public ValueTask<Canvas2DInteropOperationResult> ResizeAsync(
            Canvas2DSurfaceSize surfaceSize) =>
            ValueTask.FromResult(Success());

        public ValueTask<Canvas2DInteropOperationResult> RenderAsync(Canvas2DRenderFrame frame)
        {
            Interlocked.Increment(ref _renderCount);
            return ValueTask.FromResult(Success());
        }

        public ValueTask<Canvas2DTextMeasurementInteropResult> MeasureTextAsync(
            Canvas2DTextMeasurementRequestData request) =>
            ValueTask.FromResult(new Canvas2DTextMeasurementInteropResult
            {
                Succeeded = true,
                Width = Math.Max(1d, request.Text.Length * request.FontSize * 0.55d),
                Ascent = request.FontSize * 0.75d,
                Descent = request.FontSize * 0.25d,
                LineHeight = request.LineHeight,
                BoundingX = 0d,
                BoundingY = -(request.FontSize * 0.75d),
                BoundingWidth = Math.Max(1d, request.Text.Length * request.FontSize * 0.55d),
                BoundingHeight = request.FontSize,
                ResolvedFontIdentity = $"{request.FontIdentity}@{request.FontVersion}",
            });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static Canvas2DInteropOperationResult Success() => new() { Succeeded = true };
    }
}
