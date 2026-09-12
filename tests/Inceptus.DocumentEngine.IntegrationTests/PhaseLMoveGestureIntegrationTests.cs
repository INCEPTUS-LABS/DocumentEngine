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
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseLMoveGestureIntegrationTests
{
    [Fact]
    public async Task NeutralMovePreviewsCommitOnceThenUndoAndRedoThroughTheActiveSession()
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var document = composition.Document;
        var initialDocument = document.CaptureSnapshot();
        var events = new RecordingSubscriber();
        var commandProbe = new CountingCommandValidator();
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
            source.CommandValidators.Append(new CommandValidatorRegistration(
                MoveVisualStateCommand.KnownTypeId,
                new CommandValidatorId("test:phase-l:move-count"),
                commandProbe)),
            source.HistoryPolicies,
            [events]);
        var execution = new RecordingRenderExecution();
        var renderer = new Canvas2DRenderer(execution, RendererConfiguration());
        Assert.True((await renderer.InitializeAsync(
            "phase-l-canvas",
            new Canvas2DSurfaceSize(960d, 640d, 2d))).Succeeded);

        var attachment = await EditingSession.AttachAsync(document, renderer, configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var interaction = new Canvas2DInteractionController(session);
        var ready = session.CaptureState();
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        Assert.Equal(EditingSessionStatus.Ready, ready.Status);
        var initialScene = Assert.IsType<Canvas2D.Scene.Canvas2DScene>(ready.CurrentScene);
        var alpha = FindMovableContent(initialScene, "demo:alpha");
        var alphaVisualId = Assert.IsType<VisualStateId>(alpha.Origin.VisualStateId);
        Assert.Contains(alphaVisualId, ready.EditorState.Selection);
        Assert.True(initialDocument.VisualModel.TryGetVisualState(alphaVisualId, out var initialVisual));
        Assert.NotNull(initialVisual);
        var initialSemanticElements = initialDocument.SemanticModel.Elements;
        var initialRelationships = initialDocument.SemanticModel.Relationships;
        var initialProjectionCount = composition.Counters.ProjectionRuleInvocationCount;
        var initialLayoutCount = composition.Counters.LayoutInvocationCount;
        var initialRoutingCount = composition.Counters.RoutingInvocationCount;
        var initialSceneCount = composition.Counters.SceneContributionInvocationCount;
        var initialRenderCount = execution.RenderCount;
        var initialGraph = ready.ProjectedGraph;
        var initialLayout = ready.LayoutResult;
        var initialRouting = ready.RoutingResult;
        var startDocumentPoint = Center(alpha.Bounds);
        const long pointerId = 73;

        var pressed = await interaction.PointerPressedAsync(Pointer(
            pointerId,
            initialScene,
            startDocumentPoint,
            button: 0,
            buttons: 1));
        var firstDocumentPoint = startDocumentPoint + new VectorD(18d, 12d);
        var firstMove = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            pressed.SessionState.CurrentScene!,
            firstDocumentPoint,
            buttons: 1));
        var finalDocumentPoint = startDocumentPoint + new VectorD(46d, 31d);
        var secondMove = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            firstMove.SessionState.CurrentScene!,
            finalDocumentPoint,
            buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, pressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, firstMove.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, secondMove.Status);
        Assert.NotNull(secondMove.SessionState.EditorState.ActiveGesture);
        Assert.Contains(secondMove.SessionState.CurrentScene!.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == alphaVisualId &&
            item.Origin.StableSourceKey?.StartsWith(
                "move-preview:",
                StringComparison.Ordinal) == true &&
            item.Bounds == alpha.Bounds.Translate(new VectorD(46d, 31d)));
        Assert.Same(initialGraph, secondMove.SessionState.ProjectedGraph);
        Assert.Same(initialLayout, secondMove.SessionState.LayoutResult);
        Assert.Same(initialRouting, secondMove.SessionState.RoutingResult);
        Assert.Equal(initialProjectionCount, composition.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, composition.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount, composition.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 2, composition.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 2, execution.RenderCount);
        Assert.Equal(initialDocument, document.CaptureSnapshot());
        Assert.Equal(new HistoryStatus(0, false, false), secondMove.SessionState.HistoryStatus);
        Assert.Empty(events.Events);
        Assert.Equal(0, commandProbe.InvocationCount);

        var released = await interaction.PointerReleasedAsync(Pointer(
            pointerId,
            secondMove.SessionState.CurrentScene,
            finalDocumentPoint,
            button: 0));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var committed = session.CaptureState();
        var committedDocument = document.CaptureSnapshot();
        var expectedPosition = initialVisual!.Position + new VectorD(46d, 31d);

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(released.PersistentOperation!.IsCommitted);
        Assert.Equal(new DocumentRevision(1), committedDocument.Revision);
        Assert.True(committedDocument.VisualModel.TryGetVisualState(alphaVisualId, out var movedVisual));
        Assert.Equal(expectedPosition, movedVisual!.Position);
        Assert.Equal(VisualPlacementMode.Pinned, movedVisual.PlacementMode);
        AssertSemanticMeaningUnchanged(initialSemanticElements, initialRelationships, committedDocument);
        Assert.Equal(new HistoryStatus(1, true, false), committed.HistoryStatus);
        Assert.Null(committed.EditorState.ActiveGesture);
        Assert.Equal(expectedPosition, FindVisualContent(committed.CurrentScene!, alphaVisualId).Bounds.TopLeft);
        Assert.Equal(initialProjectionCount + 5, composition.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, composition.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount + 1, composition.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 3, composition.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 3, execution.RenderCount);
        Assert.Equal(1, commandProbe.InvocationCount);
        Assert.Equal([new DocumentRevision(1)], EventRevisions(events));

        var undo = await session.UndoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var undone = session.CaptureState();
        var undoneDocument = document.CaptureSnapshot();

        Assert.True(undo.IsCommitted);
        Assert.Equal(new DocumentRevision(2), undoneDocument.Revision);
        Assert.True(undoneDocument.VisualModel.TryGetVisualState(alphaVisualId, out var restoredVisual));
        Assert.Equal(initialVisual.Position, restoredVisual!.Position);
        Assert.Equal(initialVisual.PlacementMode, restoredVisual.PlacementMode);
        Assert.Equal(initialVisual.Position, FindVisualContent(undone.CurrentScene!, alphaVisualId).Bounds.TopLeft);
        Assert.Equal(new HistoryStatus(1, false, true), undone.HistoryStatus);
        Assert.Null(undone.EditorState.ActiveGesture);
        AssertSemanticMeaningUnchanged(initialSemanticElements, initialRelationships, undoneDocument);

        var redo = await session.RedoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var redone = session.CaptureState();
        var redoneDocument = document.CaptureSnapshot();

        Assert.True(redo.IsCommitted);
        Assert.Equal(new DocumentRevision(3), redoneDocument.Revision);
        Assert.True(redoneDocument.VisualModel.TryGetVisualState(alphaVisualId, out var replayedVisual));
        Assert.Equal(expectedPosition, replayedVisual!.Position);
        Assert.Equal(VisualPlacementMode.Pinned, replayedVisual.PlacementMode);
        Assert.Equal(expectedPosition, FindVisualContent(redone.CurrentScene!, alphaVisualId).Bounds.TopLeft);
        Assert.Equal(new HistoryStatus(1, true, false), redone.HistoryStatus);
        Assert.Null(redone.EditorState.ActiveGesture);
        AssertSemanticMeaningUnchanged(initialSemanticElements, initialRelationships, redoneDocument);
        Assert.Equal(
            [new DocumentRevision(1), new DocumentRevision(2), new DocumentRevision(3)],
            EventRevisions(events));
        Assert.Equal(3, commandProbe.InvocationCount);
        Assert.Equal(initialProjectionCount + 15, composition.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, composition.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount + 3, composition.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 5, composition.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 5, execution.RenderCount);
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

    private static Canvas2DSceneItem FindMovableContent(
        Canvas2D.Scene.Canvas2DScene scene,
        string semanticElementId) =>
        scene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.SemanticElementId == new SemanticElementId(semanticElementId) &&
            item.Origin.VisualStateId is not null &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);

    private static Canvas2DSceneItem FindVisualContent(
        Canvas2D.Scene.Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);

    private static PointD Center(RectD bounds) =>
        new(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static void AssertSemanticMeaningUnchanged(
        ImmutableArray<SemanticElementSnapshot> elements,
        ImmutableArray<SemanticRelationshipSnapshot> relationships,
        DocumentSnapshot snapshot)
    {
        Assert.True(elements.AsSpan().SequenceEqual(snapshot.SemanticModel.Elements.AsSpan()));
        Assert.True(relationships.AsSpan().SequenceEqual(
            snapshot.SemanticModel.Relationships.AsSpan()));
    }

    private static DocumentRevision[] EventRevisions(RecordingSubscriber subscriber) =>
        subscriber.Events.Select(static change => change.CommittedRevision).ToArray();

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
