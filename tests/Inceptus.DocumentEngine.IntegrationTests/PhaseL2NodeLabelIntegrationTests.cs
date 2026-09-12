using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseL2NodeLabelIntegrationTests
{
    [Fact]
    public async Task GammaLabelStaysCenteredThroughSelectionResizeUndoRedoAndMove()
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var events = new RecordingSubscriber();
        var resizeProbe = new CountingCommandValidator();
        var moveProbe = new CountingCommandValidator();
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
            source.CommandValidators
                .Append(new CommandValidatorRegistration(
                    ResizeVisualStateCommand.KnownTypeId,
                    new CommandValidatorId("test:phase-l2:resize-count"),
                    resizeProbe))
                .Append(new CommandValidatorRegistration(
                    MoveVisualStateCommand.KnownTypeId,
                    new CommandValidatorId("test:phase-l2:move-count"),
                    moveProbe)),
            source.HistoryPolicies,
            [events]);
        var execution = new RecordingRenderExecution();
        var renderer = new Canvas2DRenderer(execution, RendererConfiguration());
        Assert.True((await renderer.InitializeAsync(
            "phase-l2-label-canvas",
            new Canvas2DSurfaceSize(960d, 640d, 2d))).Succeeded);
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var interaction = new Canvas2DInteractionController(session);
        var initial = session.CaptureState();
        var initialDocument = composition.Document.CaptureSnapshot();
        var gammaNode = FindNode(initial.CurrentScene!, "demo:gamma");
        var gammaLabel = FindLabel(initial.CurrentScene!, "demo:gamma");

        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        Assert.Equal(1.08d, initial.EditorState.Viewport.Zoom);
        Assert.Equal(new VectorD(30d, 18d), initial.EditorState.Viewport.Pan);
        AssertCentered(gammaNode.Bounds, gammaLabel);

        var selected = await interaction.PointerReleasedAsync(Pointer(
            1200,
            initial.CurrentScene!,
            Center(gammaLabel.Bounds),
            button: 0));
        Assert.Equal(Canvas2DInteractionStatus.Updated, selected.Status);
        gammaNode = FindNode(selected.SessionState.CurrentScene!, "demo:gamma");
        gammaLabel = FindLabel(selected.SessionState.CurrentScene!, "demo:gamma");
        AssertCentered(gammaNode.Bounds, gammaLabel);
        Assert.Equal(4, selected.SessionState.CurrentScene!.Items.Count(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "resize-handle:",
                StringComparison.Ordinal) == true &&
            item.Origin.VisualStateId == gammaNode.Origin.VisualStateId));

        var baseline = selected.SessionState;
        var handle = FindResizeInteraction(
            baseline.CurrentScene!,
            gammaNode.Origin.VisualStateId!,
            Canvas2DResizeDirection.East);
        var start = Center(handle.Bounds);
        var finish = start + new VectorD(36d, 11d);
        var expectedBounds = new RectD(560d, 90d, 186d, 70d);
        var projectionBefore = composition.Counters.ProjectionRuleInvocationCount;
        var layoutBefore = composition.Counters.LayoutInvocationCount;
        var routingBefore = composition.Counters.RoutingInvocationCount;
        var sceneBefore = composition.Counters.SceneContributionInvocationCount;
        var renderBefore = execution.RenderCount;
        var historyBefore = baseline.HistoryStatus;
        var revisionBefore = baseline.DocumentRevision;
        var documentBefore = composition.Document.CaptureSnapshot();
        var eventCountBefore = events.Events.Count;

        var pressed = await interaction.PointerPressedAsync(Pointer(
            1201,
            baseline.CurrentScene!,
            start,
            button: 0,
            buttons: 1));
        var projectionAfterPress = composition.Counters.ProjectionRuleInvocationCount;
        var layoutAfterPress = composition.Counters.LayoutInvocationCount;
        var routingAfterPress = composition.Counters.RoutingInvocationCount;
        var sceneAfterPress = composition.Counters.SceneContributionInvocationCount;
        var renderAfterPress = execution.RenderCount;
        Assert.Equal(projectionBefore, projectionAfterPress);
        Assert.Equal(layoutBefore, layoutAfterPress);
        Assert.Equal(routingBefore, routingAfterPress);
        Assert.Equal(sceneBefore + 1, sceneAfterPress);
        Assert.Equal(renderBefore + 1, renderAfterPress);
        Assert.Equal(documentBefore, composition.Document.CaptureSnapshot());
        Assert.Equal(revisionBefore, pressed.SessionState.DocumentRevision);
        Assert.Equal(historyBefore, pressed.SessionState.HistoryStatus);
        Assert.Equal(eventCountBefore, events.Events.Count);
        Assert.Equal(0, resizeProbe.InvocationCount);

        var moved = await interaction.PointerMovedAsync(Pointer(
            1201,
            pressed.SessionState.CurrentScene!,
            finish,
            buttons: 1));
        var previewNode = FindPreview(
            moved.SessionState.CurrentScene!,
            gammaNode.Id,
            Canvas2DSceneGeometryKind.Rectangle);
        var previewLabel = FindPreview(
            moved.SessionState.CurrentScene!,
            gammaLabel.Id,
            Canvas2DSceneGeometryKind.Text);

        Assert.Equal(expectedBounds, previewNode.Bounds);
        AssertCentered(previewNode.Bounds, previewLabel);
        Assert.Same(baseline.ProjectedGraph, moved.SessionState.ProjectedGraph);
        Assert.Same(baseline.LayoutResult, moved.SessionState.LayoutResult);
        Assert.Same(baseline.RoutingResult, moved.SessionState.RoutingResult);
        Assert.Equal(projectionAfterPress, composition.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(layoutAfterPress, composition.Counters.LayoutInvocationCount);
        Assert.Equal(routingAfterPress, composition.Counters.RoutingInvocationCount);
        Assert.Equal(sceneAfterPress + 1, composition.Counters.SceneContributionInvocationCount);
        Assert.Equal(renderAfterPress + 1, execution.RenderCount);
        Assert.Equal(documentBefore, composition.Document.CaptureSnapshot());
        Assert.Equal(revisionBefore, moved.SessionState.DocumentRevision);
        Assert.Equal(historyBefore, moved.SessionState.HistoryStatus);
        Assert.Equal(eventCountBefore, events.Events.Count);
        Assert.Equal(0, resizeProbe.InvocationCount);

        var released = await interaction.PointerReleasedAsync(Pointer(
            1201,
            moved.SessionState.CurrentScene!,
            finish,
            button: 0));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var committed = session.CaptureState();
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(ResizeVisualStateCommand.KnownTypeId, released.PersistentOperation!.CommandTypeId);
        Assert.Equal(new DocumentRevision(1), committed.DocumentRevision);
        Assert.Equal(new HistoryStatus(1, true, false), committed.HistoryStatus);
        Assert.Equal(1, resizeProbe.InvocationCount);
        Assert.Equal(eventCountBefore + 1, events.Events.Count);
        Assert.Equal(projectionBefore + 5, composition.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(layoutBefore, composition.Counters.LayoutInvocationCount);
        Assert.Equal(routingBefore + 1, composition.Counters.RoutingInvocationCount);
        Assert.Equal(sceneBefore + 3, composition.Counters.SceneContributionInvocationCount);
        Assert.Equal(renderBefore + 3, execution.RenderCount);
        AssertCentered(
            FindNode(committed.CurrentScene!, "demo:gamma").Bounds,
            FindLabel(committed.CurrentScene!, "demo:gamma"));

        var undo = await session.UndoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var undone = session.CaptureState();
        Assert.True(undo.IsCommitted);
        Assert.Equal(new HistoryStatus(1, false, true), undone.HistoryStatus);
        AssertCentered(
            FindNode(undone.CurrentScene!, "demo:gamma").Bounds,
            FindLabel(undone.CurrentScene!, "demo:gamma"));

        var redo = await session.RedoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var redone = session.CaptureState();
        Assert.True(redo.IsCommitted);
        Assert.Equal(new HistoryStatus(1, true, false), redone.HistoryStatus);
        AssertCentered(
            FindNode(redone.CurrentScene!, "demo:gamma").Bounds,
            FindLabel(redone.CurrentScene!, "demo:gamma"));

        gammaNode = FindNode(redone.CurrentScene!, "demo:gamma");
        gammaLabel = FindLabel(redone.CurrentScene!, "demo:gamma");
        var moveStart = Center(gammaLabel.Bounds);
        var moveFinish = moveStart + new VectorD(-22d, 34d);
        var moveProjectionBefore = composition.Counters.ProjectionRuleInvocationCount;
        var moveLayoutBefore = composition.Counters.LayoutInvocationCount;
        var moveRoutingBefore = composition.Counters.RoutingInvocationCount;
        var moveSceneBefore = composition.Counters.SceneContributionInvocationCount;
        var moveRenderBefore = execution.RenderCount;
        var moveEventCountBefore = events.Events.Count;
        var movePressed = await interaction.PointerPressedAsync(Pointer(
            1202,
            redone.CurrentScene!,
            moveStart,
            button: 0,
            buttons: 1));
        var movePreview = await interaction.PointerMovedAsync(Pointer(
            1202,
            movePressed.SessionState.CurrentScene!,
            moveFinish,
            buttons: 1));
        var translatedNode = FindPreview(
            movePreview.SessionState.CurrentScene!,
            gammaNode.Id,
            Canvas2DSceneGeometryKind.Rectangle);
        var translatedLabel = FindPreview(
            movePreview.SessionState.CurrentScene!,
            gammaLabel.Id,
            Canvas2DSceneGeometryKind.Text);
        AssertCentered(translatedNode.Bounds, translatedLabel);
        Assert.Equal(moveProjectionBefore, composition.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(moveLayoutBefore, composition.Counters.LayoutInvocationCount);
        Assert.Equal(moveRoutingBefore, composition.Counters.RoutingInvocationCount);
        Assert.Equal(moveSceneBefore + 1, composition.Counters.SceneContributionInvocationCount);
        Assert.Equal(moveRenderBefore + 1, execution.RenderCount);
        Assert.Equal(0, moveProbe.InvocationCount);
        Assert.Equal(moveEventCountBefore, events.Events.Count);

        var moveReleased = await interaction.PointerReleasedAsync(Pointer(
            1202,
            movePreview.SessionState.CurrentScene!,
            moveFinish,
            button: 0));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var movedReady = session.CaptureState();
        Assert.Equal(Canvas2DInteractionStatus.Committed, moveReleased.Status);
        Assert.Equal(MoveVisualStateCommand.KnownTypeId, moveReleased.PersistentOperation!.CommandTypeId);
        Assert.Equal(1, moveProbe.InvocationCount);
        Assert.Equal(new HistoryStatus(2, true, false), movedReady.HistoryStatus);
        Assert.Equal(moveEventCountBefore + 1, events.Events.Count);
        Assert.Equal(moveProjectionBefore + 5, composition.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(moveLayoutBefore, composition.Counters.LayoutInvocationCount);
        Assert.Equal(moveRoutingBefore + 1, composition.Counters.RoutingInvocationCount);
        Assert.Equal(moveSceneBefore + 2, composition.Counters.SceneContributionInvocationCount);
        Assert.Equal(moveRenderBefore + 2, execution.RenderCount);
        AssertCentered(
            FindNode(movedReady.CurrentScene!, "demo:gamma").Bounds,
            FindLabel(movedReady.CurrentScene!, "demo:gamma"));
        var finalSemantic = composition.Document.CaptureSnapshot().SemanticModel;
        Assert.True(initialDocument.SemanticModel.Elements.AsSpan().SequenceEqual(
            finalSemantic.Elements.AsSpan()));
        Assert.True(initialDocument.SemanticModel.Relationships.AsSpan().SequenceEqual(
            finalSemantic.Relationships.AsSpan()));
    }

    private static Canvas2DPointerInput Pointer(
        long pointerId,
        Canvas2D.Scene.Canvas2DScene scene,
        PointD documentPoint,
        int button = -1,
        int buttons = 0) =>
        new(
            pointerId,
            scene.ViewportTransform.TransformPoint(documentPoint),
            isPrimary: true,
            button: button,
            buttons: buttons);

    private static Canvas2DSceneItem FindNode(
        Canvas2D.Scene.Canvas2DScene scene,
        string semanticElementId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.SemanticElementId == new SemanticElementId(semanticElementId) &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);

    private static Canvas2DSceneItem FindLabel(
        Canvas2D.Scene.Canvas2DScene scene,
        string semanticElementId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.SemanticElementId == new SemanticElementId(semanticElementId));

    private static Canvas2DSceneItem FindResizeInteraction(
        Canvas2D.Scene.Canvas2DScene scene,
        VisualStateId visualStateId,
        Canvas2DResizeDirection direction) =>
        scene.Items.Single(item =>
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.StableSourceKey?.StartsWith(
                $"resize-edge-zone:{Canvas2DResizeGeometry.Role(direction)}:",
                StringComparison.Ordinal) == true);

    private static Canvas2DSceneItem FindPreview(
        Canvas2D.Scene.Canvas2DScene scene,
        SceneObjectId sourceId,
        Canvas2DSceneGeometryKind kind) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Geometry.Kind == kind &&
            item.Origin.RelatedSceneObjectIds.Contains(sourceId) &&
            (item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true ||
             item.Origin.StableSourceKey?.StartsWith(
                 "move-preview:",
                 StringComparison.Ordinal) == true));

    private static PointD Center(RectD bounds) =>
        new(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static void AssertCentered(RectD nodeBounds, Canvas2DSceneItem label)
    {
        Assert.Equal(Canvas2DTextAlignment.Center, label.Geometry.TextAlignment);
        Assert.Equal(Canvas2DTextBaseline.Middle, label.Geometry.TextBaseline);
        var anchor = label.Transform.TransformPoint(label.Geometry.TextAnchor);
        var expected = Center(nodeBounds);
        Assert.Equal(expected.X, anchor.X, precision: 10);
        Assert.Equal(expected.Y, anchor.Y, precision: 10);
    }

    private static Canvas2DRendererConfiguration RendererConfiguration() =>
        new(
            fontResources:
            [
                new Canvas2DFontResource(
                    "demo:font:dejavu",
                    "2.37",
                    "DejaVu Sans",
                    "/fonts/DejaVuSans-2.37.ttf"),
            ],
            defaultFontFamily: "DejaVu Sans");

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingCommandValidator : ICommandValidator
    {
        private int _invocationCount;

        internal int InvocationCount => Volatile.Read(ref _invocationCount);

        public ImmutableArray<Diagnostic> Validate(
            ICommand command,
            DocumentSnapshot document)
        {
            _ = command;
            _ = document;
            Interlocked.Increment(ref _invocationCount);
            return [];
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
                Width = 80d,
                Ascent = 10d,
                Descent = 3d,
                LineHeight = 18d,
                BoundingX = 0d,
                BoundingY = -10d,
                BoundingWidth = 80d,
                BoundingHeight = 13d,
                ResolvedFontIdentity = "demo:font:dejavu@2.37",
            });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static Canvas2DInteropOperationResult Success() => new() { Succeeded = true };
    }
}
