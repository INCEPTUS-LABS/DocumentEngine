using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Toolbox;

using static Inceptus.DocumentEngine.IntegrationTests.EditingSessionTestSynchronization;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseM05ToolboxIntegrationTests
{
    [Fact]
    public async Task SelectingToolboxItemsChangesOnlyLocalPresentationState()
    {
        await using var fixture = await Fixture.CreateAsync();
        var toolboxSelection = new ToolboxSelectionState();
        var first = Fixture.CatalogItems[0];
        var second = Fixture.CatalogItems[1];
        var before = fixture.Session.CaptureState();
        var documentBefore = fixture.Composition.Document.CaptureSnapshot();
        var countersBefore = fixture.CaptureCounters();
        var renderCountBefore = fixture.Execution.RenderCount;

        Assert.True(toolboxSelection.Select(first.ItemId));
        Assert.True(toolboxSelection.Select(second.ItemId));
        Assert.False(toolboxSelection.Select(second.ItemId));

        var after = fixture.Session.CaptureState();
        Assert.Equal(second.ItemId, toolboxSelection.SelectedItemId);
        Assert.Equal(documentBefore, fixture.Composition.Document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
        Assert.Same(before.LayoutResult, after.LayoutResult);
        Assert.Same(before.RoutingResult, after.RoutingResult);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Equal(countersBefore, fixture.CaptureCounters());
        Assert.Equal(renderCountBefore, fixture.Execution.RenderCount);
        Assert.Empty(fixture.Events);

        await using var interaction = new Canvas2DInteractionController(fixture.Session);
        var canvasClick = await interaction.PointerActivatedAsync(new PointD(5d, 5d));
        await fixture.Session.WaitForIdleAsync();
        var afterCanvasClick = fixture.Session.CaptureState();

        Assert.True(canvasClick.Status is
            Canvas2DInteractionStatus.Updated or Canvas2DInteractionStatus.Unchanged);
        Assert.Equal(second.ItemId, toolboxSelection.SelectedItemId);
        Assert.Equal(documentBefore, fixture.Composition.Document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, afterCanvasClick.DocumentRevision);
        Assert.Equal(before.HistoryStatus, afterCanvasClick.HistoryStatus);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task ToolboxSelectionNeverEntersHistoryAndSurvivesDocumentUndoRedo()
    {
        await using var fixture = await Fixture.CreateAsync();
        var toolboxSelection = new ToolboxSelectionState();
        var initial = fixture.Session.CaptureState();
        var selectedVisualStateId = Assert.Single(initial.EditorState.Selection);
        Assert.True(fixture.Composition.Document.VisualModel.TryGetVisualState(
            selectedVisualStateId,
            out var initialVisual));
        var originalPosition = initialVisual!.Position;
        var movedPosition = new PointD(originalPosition.X + 35d, originalPosition.Y + 20d);

        var move = await fixture.Session.ExecuteForSelectedVisualStateAsync(
            selectedVisualStateId,
            new MoveVisualStateCommand(
                initial.DocumentId,
                initial.DocumentRevision,
                selectedVisualStateId,
                movedPosition));
        Assert.True(move.IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(fixture.Composition.Document, fixture.Session);
        var moved = fixture.Session.CaptureState();
        var movedDocument = fixture.Composition.Document.CaptureSnapshot();
        var movedCounters = fixture.CaptureCounters();
        var movedRenderCount = fixture.Execution.RenderCount;
        Assert.Equal(1, moved.HistoryStatus.EntryCount);
        Assert.True(moved.HistoryStatus.CanUndo);
        Assert.Single(fixture.Events);

        Assert.True(toolboxSelection.Select(Fixture.CatalogItems[0].ItemId));
        Assert.True(toolboxSelection.Select(Fixture.CatalogItems[1].ItemId));
        var selectedToolboxItemId = toolboxSelection.SelectedItemId;
        var afterToolboxSelection = fixture.Session.CaptureState();

        Assert.Equal(movedDocument, fixture.Composition.Document.CaptureSnapshot());
        Assert.Equal(moved.HistoryStatus, afterToolboxSelection.HistoryStatus);
        Assert.Same(moved.EditorState, afterToolboxSelection.EditorState);
        Assert.Same(moved.CurrentScene, afterToolboxSelection.CurrentScene);
        Assert.Equal(movedCounters, fixture.CaptureCounters());
        Assert.Equal(movedRenderCount, fixture.Execution.RenderCount);
        Assert.Single(fixture.Events);

        var undo = await fixture.Session.UndoAsync();
        Assert.True(undo.IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(fixture.Composition.Document, fixture.Session);
        Assert.True(fixture.Composition.Document.VisualModel.TryGetVisualState(
            selectedVisualStateId,
            out var undoneVisual));
        Assert.Equal(originalPosition, undoneVisual!.Position);
        Assert.Equal(selectedToolboxItemId, toolboxSelection.SelectedItemId);
        Assert.Equal(2, fixture.Events.Count);

        var redo = await fixture.Session.RedoAsync();
        Assert.True(redo.IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(fixture.Composition.Document, fixture.Session);
        Assert.True(fixture.Composition.Document.VisualModel.TryGetVisualState(
            selectedVisualStateId,
            out var redoneVisual));
        Assert.Equal(movedPosition, redoneVisual!.Position);
        Assert.Equal(selectedToolboxItemId, toolboxSelection.SelectedItemId);
        Assert.Equal(3, fixture.Events.Count);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            NeutralDemoComposition composition,
            EditingSession session,
            RecordingRenderExecution execution,
            ConcurrentQueue<DocumentChangedEvent> events)
        {
            Composition = composition;
            Session = session;
            Execution = execution;
            Events = events;
        }

        internal NeutralDemoComposition Composition { get; }

        internal EditingSession Session { get; }

        internal RecordingRenderExecution Execution { get; }

        internal ConcurrentQueue<DocumentChangedEvent> Events { get; }

        internal static ImmutableArray<ToolboxItemDefinition> CatalogItems =>
            NeutralDemoToolbox.Catalog.Items;

        internal static async Task<Fixture> CreateAsync()
        {
            var composition = NeutralDemoPipeline.CreateComposition();
            var events = new ConcurrentQueue<DocumentChangedEvent>();
            var source = composition.Configuration;
            var configuration = new EditingSessionConfiguration(
                source.ProjectionEngine,
                source.LayoutEngine,
                source.LayoutAlgorithmId,
                source.RoutingEngine,
                source.RoutingAlgorithmId,
                source.SceneBuilder,
                source.ProjectionContext,
                source.LayoutContext,
                source.RoutingContext,
                source.InitialEditorState,
                source.CommandHandlers,
                source.CommandValidators,
                source.HistoryPolicies,
                [new RecordingSubscriber(events)],
                source.ConnectorAnchorPolicyProvider);
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
                "phase-m05-canvas",
                new Canvas2DSurfaceSize(900d, 600d, 1.25d));
            Assert.True(initialized.Succeeded);
            var attachment = await EditingSession.AttachAsync(
                composition.Document,
                renderer,
                configuration);
            Assert.True(attachment.IsAttached);
            return new Fixture(
                composition,
                Assert.IsType<EditingSession>(attachment.Session),
                execution,
                events);
        }

        internal PipelineCounters CaptureCounters() => new(
            Composition.Counters.ProjectionRuleInvocationCount,
            Composition.Counters.LayoutInvocationCount,
            Composition.Counters.RoutingInvocationCount,
            Composition.Counters.SceneContributionInvocationCount);

        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }

    private sealed record PipelineCounters(
        int Projection,
        int Layout,
        int Routing,
        int Scene);

    private sealed class RecordingSubscriber(
        ConcurrentQueue<DocumentChangedEvent> events) : IDocumentChangedSubscriber
    {
        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
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

        public ValueTask<Canvas2DInteropOperationResult> RenderAsync(
            Canvas2DRenderFrame frame)
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
