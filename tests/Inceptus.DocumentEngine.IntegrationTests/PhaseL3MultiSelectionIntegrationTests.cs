using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
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
using Inceptus.DocumentEngine.Runtime.Documents;

using static Inceptus.DocumentEngine.IntegrationTests.EditingSessionTestSynchronization;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseL3MultiSelectionIntegrationTests
{
    [Fact]
    public async Task ThreeTargetFivePreviewMoveCommitsOnceAndUndoRedoRemainAtomic()
    {
        var harness = await Harness.CreateAsync();
        await using var session = harness.Session;
        await using var interaction = new Canvas2DInteractionController(session);
        var initial = session.CaptureState();
        var initialDocument = harness.Document.CaptureSnapshot();
        var initialViewport = initial.EditorState.Viewport;
        var ids = new[]
        {
            new VisualStateId("demo:visual:alpha"),
            new VisualStateId("demo:visual:beta"),
            new VisualStateId("demo:visual:gamma"),
        };
        var initialPositions = ids.ToDictionary(
            static id => id,
            id => Visual(initialDocument, id).Position);

        var alpha = FindMovableContent(initial.CurrentScene!, ids[0]);
        var alphaHover = await interaction.PointerMovedAsync(Pointer(
            1,
            initial.CurrentScene!,
            Center(alpha.Bounds),
            button: -1));
        Assert.Equal("grab", alphaHover.CssCursor);

        var selectionSceneCount = harness.Counters.SceneContributionInvocationCount;
        var selectionRenderCount = harness.Execution.RenderCount;
        await ClickAsync(interaction, session, ids[0], controlKey: false, pointerId: 2);
        await ClickAsync(interaction, session, ids[1], controlKey: true, pointerId: 3);
        await ClickAsync(interaction, session, ids[2], controlKey: true, pointerId: 4);
        await ClickAsync(interaction, session, ids[1], controlKey: true, pointerId: 5);
        AssertSelection(session.CaptureState(), ids[0], ids[2]);
        await ClickAsync(interaction, session, ids[1], controlKey: true, pointerId: 6);
        var beforeControlEmpty = session.CaptureState();
        var controlEmpty = await ClickPointAsync(
            interaction,
            session,
            new PointD(-1000d, -1000d),
            controlKey: true,
            pointerId: 7);
        var selected = session.CaptureState();

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, controlEmpty.Status);
        Assert.Same(beforeControlEmpty.CurrentScene, selected.CurrentScene);
        AssertSelection(selected, ids);
        Assert.Equal(selectionSceneCount + 4,
            harness.Counters.SceneContributionInvocationCount);
        Assert.Equal(selectionRenderCount + 4, harness.Execution.RenderCount);
        Assert.Equal(initialDocument, harness.Document.CaptureSnapshot());
        Assert.Equal(new HistoryStatus(0, false, false), selected.HistoryStatus);
        Assert.Empty(harness.Events.Events);
        Assert.Equal(0, harness.CommandProbe.InvocationCount);
        Assert.Equal(harness.InitialProjectionCount, harness.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(harness.InitialLayoutCount, harness.Counters.LayoutInvocationCount);
        Assert.Equal(harness.InitialRoutingCount, harness.Counters.RoutingInvocationCount);

        var beta = FindMovableContent(selected.CurrentScene!, ids[1]);
        var originalBounds = ids.ToDictionary(
            static id => id,
            id => FindMovableContent(selected.CurrentScene!, id).Bounds);
        var start = Center(beta.Bounds);
        const long dragPointerId = 19;
        var pressed = await interaction.PointerPressedAsync(Pointer(
            dragPointerId,
            selected.CurrentScene!,
            start,
            button: 0,
            buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, pressed.Status);
        Assert.Equal("grab", pressed.CssCursor);
        AssertSelection(pressed.SessionState, ids);
        Assert.Same(selected.CurrentScene, pressed.SessionState.CurrentScene);

        var previewSceneCount = harness.Counters.SceneContributionInvocationCount;
        var previewRenderCount = harness.Execution.RenderCount;
        var previewDeltas = new[]
        {
            new VectorD(8d, 4d),
            new VectorD(16d, 8d),
            new VectorD(24d, 12d),
            new VectorD(32d, 16d),
            new VectorD(40d, 20d),
        };
        Canvas2DInteractionResult preview = pressed;
        foreach (var delta in previewDeltas)
        {
            var scene = session.CaptureState().CurrentScene!;
            preview = await interaction.PointerMovedAsync(Pointer(
                dragPointerId,
                scene,
                start + delta,
                button: -1,
                buttons: 1));
            Assert.Equal(Canvas2DInteractionStatus.Updated, preview.Status);
            Assert.Equal("grabbing", preview.CssCursor);
            foreach (var id in ids)
            {
                AssertBoundsEqual(
                    originalBounds[id].Translate(delta),
                    FindMovePreview(preview.SessionState.CurrentScene!, id).Bounds);
            }
        }

        Assert.Equal(previewSceneCount + 5, harness.Counters.SceneContributionInvocationCount);
        Assert.Equal(previewRenderCount + 5, harness.Execution.RenderCount);
        Assert.Equal(harness.InitialProjectionCount, harness.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(harness.InitialLayoutCount, harness.Counters.LayoutInvocationCount);
        Assert.Equal(harness.InitialRoutingCount, harness.Counters.RoutingInvocationCount);
        Assert.Equal(initialDocument, harness.Document.CaptureSnapshot());
        Assert.Equal(new HistoryStatus(0, false, false), preview.SessionState.HistoryStatus);
        Assert.Empty(harness.Events.Events);
        Assert.Equal(0, harness.CommandProbe.InvocationCount);

        var commitSceneCount = harness.Counters.SceneContributionInvocationCount;
        var commitRenderCount = harness.Execution.RenderCount;
        var released = await interaction.PointerReleasedAsync(Pointer(
            dragPointerId,
            preview.SessionState.CurrentScene!,
            start + previewDeltas[^1],
            button: 0));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Events.FirstDelivery.WaitAsync(TimeSpan.FromSeconds(5));
        var committed = session.CaptureState();
        var committedDocument = harness.Document.CaptureSnapshot();

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(released.PersistentOperation!.IsCommitted);
        Assert.Equal(new DocumentRevision(1), committed.DocumentRevision);
        Assert.Equal(new HistoryStatus(1, true, false), committed.HistoryStatus);
        Assert.Single(harness.Events.Events);
        Assert.Equal(1, harness.CommandProbe.InvocationCount);
        Assert.True(ids.AsSpan().SequenceEqual(
            Assert.Single(harness.CommandProbe.Targets).AsSpan()));
        Assert.Equal(harness.InitialProjectionCount + 5,
            harness.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(harness.InitialLayoutCount, harness.Counters.LayoutInvocationCount);
        Assert.Equal(harness.InitialRoutingCount + 1, harness.Counters.RoutingInvocationCount);
        Assert.Equal(commitSceneCount + 1, harness.Counters.SceneContributionInvocationCount);
        Assert.Equal(commitRenderCount + 1, harness.Execution.RenderCount);
        AssertSelection(committed, ids);
        Assert.Equal(initialViewport, committed.EditorState.Viewport);
        Assert.Null(committed.EditorState.ActiveGesture);
        Assert.Equal("grab", released.CssCursor);
        foreach (var id in ids)
        {
            var moved = Visual(committedDocument, id);
            AssertPointEqual(initialPositions[id] + previewDeltas[^1], moved.Position);
            Assert.Equal(VisualPlacementMode.Pinned, moved.PlacementMode);
        }

        AssertSemanticMeaningUnchanged(initialDocument, committedDocument);

        var undo = await session.UndoAsync();
        await WaitForCommittedEventAndSessionIdleAsync(harness.Document, session);
        var undone = session.CaptureState();
        Assert.True(undo.IsCommitted);
        Assert.Equal(new HistoryStatus(1, false, true), undone.HistoryStatus);
        foreach (var id in ids)
        {
            AssertPointEqual(
                initialPositions[id],
                Visual(harness.Document.CaptureSnapshot(), id).Position);
        }

        var redo = await session.RedoAsync();
        await WaitForCommittedEventAndSessionIdleAsync(harness.Document, session);
        var redone = session.CaptureState();
        Assert.True(redo.IsCommitted);
        Assert.Equal(new HistoryStatus(1, true, false), redone.HistoryStatus);
        AssertSelection(redone, ids);
        foreach (var id in ids)
        {
            AssertPointEqual(
                initialPositions[id] + previewDeltas[^1],
                Visual(harness.Document.CaptureSnapshot(), id).Position);
        }

        Assert.Equal(3, harness.CommandProbe.InvocationCount);
        Assert.Equal(
            [new DocumentRevision(1), new DocumentRevision(2), new DocumentRevision(3)],
            harness.Events.Events.Select(static change => change.CommittedRevision).ToArray());
        Assert.Equal(2d, harness.SurfaceSize.DevicePixelRatio);
        Assert.Equal(1.08d, initialViewport.Zoom);
        Assert.Equal(new VectorD(30d, 18d), initialViewport.Pan);
    }

    [Fact]
    public async Task ConnectedPairMovesWhileSelectedNonMovableConnectorRemainsSelected()
    {
        var harness = await Harness.CreateAsync();
        await using var session = harness.Session;
        await using var interaction = new Canvas2DInteractionController(session);
        var initialDocument = harness.Document.CaptureSnapshot();
        var betaId = new VisualStateId("demo:visual:beta");
        var gammaId = new VisualStateId("demo:visual:gamma");
        var connectorId = new VisualStateId("demo:visual:beta-gamma");

        await ClickAsync(interaction, session, betaId, controlKey: false, pointerId: 31);
        await ClickAsync(interaction, session, gammaId, controlKey: true, pointerId: 32);
        var connectorScene = session.CaptureState().CurrentScene!;
        var connector = connectorScene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == connectorId &&
            !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));
        var connectorPoint = connector.Transform.TransformPoint(connector.Geometry.Points[1]);
        await ClickPointAsync(
            interaction,
            session,
            connectorPoint,
            controlKey: true,
            pointerId: 33);
        var selected = session.CaptureState();
        AssertSelection(selected, betaId, connectorId, gammaId);

        var beta = FindMovableContent(selected.CurrentScene!, betaId);
        var gamma = FindMovableContent(selected.CurrentScene!, gammaId);
        var start = Center(beta.Bounds);
        var delta = new VectorD(27.5d, -14.25d);
        var routingBeforePreview = harness.Counters.RoutingInvocationCount;
        const long dragPointerId = 34;
        _ = await interaction.PointerPressedAsync(Pointer(
            dragPointerId,
            selected.CurrentScene!,
            start,
            button: 0,
            buttons: 1));
        var preview = await interaction.PointerMovedAsync(Pointer(
            dragPointerId,
            session.CaptureState().CurrentScene!,
            start + delta,
            button: -1,
            buttons: 1));

        Assert.Equal(routingBeforePreview, harness.Counters.RoutingInvocationCount);
        AssertBoundsEqual(
            beta.Bounds.Translate(delta),
            FindMovePreview(preview.SessionState.CurrentScene!, betaId).Bounds);
        AssertBoundsEqual(
            gamma.Bounds.Translate(delta),
            FindMovePreview(preview.SessionState.CurrentScene!, gammaId).Bounds);
        Assert.DoesNotContain(preview.SessionState.CurrentScene!.Items, item =>
            item.Origin.StableSourceKey?.StartsWith("move-preview:", StringComparison.Ordinal) == true &&
            item.Origin.VisualStateId == connectorId);
        Assert.Equal(initialDocument, harness.Document.CaptureSnapshot());

        var released = await interaction.PointerReleasedAsync(Pointer(
            dragPointerId,
            preview.SessionState.CurrentScene!,
            start + delta,
            button: 0));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Events.FirstDelivery.WaitAsync(TimeSpan.FromSeconds(5));
        var committed = session.CaptureState();
        var committedDocument = harness.Document.CaptureSnapshot();

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(routingBeforePreview + 1, harness.Counters.RoutingInvocationCount);
        Assert.Equal(new DocumentRevision(1), committed.DocumentRevision);
        Assert.Equal(new HistoryStatus(1, true, false), committed.HistoryStatus);
        Assert.Single(harness.Events.Events);
        Assert.Equal(1, harness.CommandProbe.InvocationCount);
        Assert.True(
            new[] { betaId, gammaId }.AsSpan().SequenceEqual(
                Assert.Single(harness.CommandProbe.Targets).AsSpan()));
        AssertSelection(committed, betaId, connectorId, gammaId);
        AssertPointEqual(Visual(initialDocument, betaId).Position + delta,
            Visual(committedDocument, betaId).Position);
        AssertPointEqual(Visual(initialDocument, gammaId).Position + delta,
            Visual(committedDocument, gammaId).Position);
        Assert.Equal(Visual(initialDocument, connectorId), Visual(committedDocument, connectorId));
        AssertSemanticMeaningUnchanged(initialDocument, committedDocument);
        AssertConnectorAnchorsMatchNodes(committed.CurrentScene!, betaId, gammaId, connectorId);

        Assert.True((await session.UndoAsync()).IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(harness.Document, session);
        AssertPointEqual(
            Visual(initialDocument, betaId).Position,
            Visual(harness.Document.CaptureSnapshot(), betaId).Position);
        AssertPointEqual(
            Visual(initialDocument, gammaId).Position,
            Visual(harness.Document.CaptureSnapshot(), gammaId).Position);

        Assert.True((await session.RedoAsync()).IsCommitted);
        await WaitForCommittedEventAndSessionIdleAsync(harness.Document, session);
        AssertPointEqual(Visual(initialDocument, betaId).Position + delta,
            Visual(harness.Document.CaptureSnapshot(), betaId).Position);
        AssertPointEqual(Visual(initialDocument, gammaId).Position + delta,
            Visual(harness.Document.CaptureSnapshot(), gammaId).Position);
    }

    private static async Task<Canvas2DInteractionResult> ClickAsync(
        Canvas2DInteractionController interaction,
        EditingSession session,
        VisualStateId visualStateId,
        bool controlKey,
        long pointerId)
    {
        var scene = session.CaptureState().CurrentScene!;
        return await ClickPointAsync(
            interaction,
            session,
            Center(FindMovableContent(scene, visualStateId).Bounds),
            controlKey,
            pointerId);
    }

    private static async Task<Canvas2DInteractionResult> ClickPointAsync(
        Canvas2DInteractionController interaction,
        EditingSession session,
        PointD documentPoint,
        bool controlKey,
        long pointerId)
    {
        var scene = session.CaptureState().CurrentScene!;
        _ = await interaction.PointerPressedAsync(Pointer(
            pointerId,
            scene,
            documentPoint,
            button: 0,
            buttons: 1,
            controlKey: controlKey));
        return await interaction.PointerReleasedAsync(Pointer(
            pointerId,
            session.CaptureState().CurrentScene!,
            documentPoint,
            button: 0,
            controlKey: controlKey));
    }

    private static Canvas2DPointerInput Pointer(
        long pointerId,
        Canvas2DScene scene,
        PointD documentPoint,
        int button,
        int buttons = 0,
        bool controlKey = false) =>
        new(
            pointerId,
            scene.ViewportTransform.TransformPoint(documentPoint),
            isPrimary: true,
            button: button,
            buttons: buttons,
            controlKey: controlKey);

    private static Canvas2DSceneItem FindMovableContent(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);

    private static Canvas2DSceneItem FindMovePreview(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.First(item =>
            item.Origin.StableSourceKey?.StartsWith("move-preview:", StringComparison.Ordinal) == true &&
            item.Origin.VisualStateId == visualStateId &&
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Geometry.Kind is Canvas2DSceneGeometryKind.Rectangle or Canvas2DSceneGeometryKind.Ellipse);

    private static VisualStateSnapshot Visual(DocumentSnapshot document, VisualStateId id)
    {
        Assert.True(document.VisualModel.TryGetVisualState(id, out var visual));
        return Assert.IsType<VisualStateSnapshot>(visual);
    }

    private static PointD Center(RectD bounds) =>
        new(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static void AssertPointEqual(PointD expected, PointD actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 10);
        Assert.Equal(expected.Y, actual.Y, precision: 10);
    }

    private static void AssertBoundsEqual(RectD expected, RectD actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 10);
        Assert.Equal(expected.Y, actual.Y, precision: 10);
        Assert.Equal(expected.Width, actual.Width, precision: 10);
        Assert.Equal(expected.Height, actual.Height, precision: 10);
    }

    private static void AssertSelection(EditingSessionState state, params VisualStateId[] expected) =>
        Assert.True(expected.AsSpan().SequenceEqual(state.EditorState.Selection.AsSpan()));

    private static void AssertSemanticMeaningUnchanged(
        DocumentSnapshot expected,
        DocumentSnapshot actual)
    {
        Assert.True(expected.SemanticModel.Elements.AsSpan().SequenceEqual(
            actual.SemanticModel.Elements.AsSpan()));
        Assert.True(expected.SemanticModel.Relationships.AsSpan().SequenceEqual(
            actual.SemanticModel.Relationships.AsSpan()));
    }

    private static void AssertConnectorAnchorsMatchNodes(
        Canvas2DScene scene,
        VisualStateId sourceId,
        VisualStateId targetId,
        VisualStateId connectorId)
    {
        var source = FindMovableContent(scene, sourceId).Bounds;
        var target = FindMovableContent(scene, targetId).Bounds;
        var connector = scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == connectorId &&
            !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));
        AssertPointEqual(
            new PointD(source.Right, source.Y + (source.Height / 2d)),
            connector.Geometry.Points[0]);
        AssertPointEqual(
            new PointD(target.Left, target.Y + (target.Height / 2d)),
            connector.Geometry.Points[^1]);
    }

    private sealed class Harness
    {
        private Harness(
            NeutralDemoComposition composition,
            RecordingSubscriber events,
            CountingCommandValidator commandProbe,
            RecordingRenderExecution execution,
            Canvas2DSurfaceSize surfaceSize,
            EditingSession session)
        {
            Document = composition.Document;
            Counters = composition.Counters;
            Events = events;
            CommandProbe = commandProbe;
            Execution = execution;
            SurfaceSize = surfaceSize;
            Session = session;
            InitialProjectionCount = Counters.ProjectionRuleInvocationCount;
            InitialLayoutCount = Counters.LayoutInvocationCount;
            InitialRoutingCount = Counters.RoutingInvocationCount;
        }

        internal Document Document { get; }

        internal NeutralDemoPipelineCounters Counters { get; }

        internal RecordingSubscriber Events { get; }

        internal CountingCommandValidator CommandProbe { get; }

        internal RecordingRenderExecution Execution { get; }

        internal Canvas2DSurfaceSize SurfaceSize { get; }

        internal EditingSession Session { get; }

        internal int InitialProjectionCount { get; }

        internal int InitialLayoutCount { get; }

        internal int InitialRoutingCount { get; }

        internal static async Task<Harness> CreateAsync()
        {
            var composition = NeutralDemoPipeline.CreateComposition();
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
                    MoveVisualStatesCommand.KnownTypeId,
                    new CommandValidatorId("test:phase-l3:move-count"),
                    commandProbe)),
                source.HistoryPolicies,
                [events]);
            var execution = new RecordingRenderExecution();
            var surfaceSize = new Canvas2DSurfaceSize(960d, 640d, 2d);
            var renderer = new Canvas2DRenderer(execution, RendererConfiguration());
            Assert.True((await renderer.InitializeAsync("phase-l3-canvas", surfaceSize)).Succeeded);
            var attachment = await EditingSession.AttachAsync(
                composition.Document,
                renderer,
                configuration);
            var session = Assert.IsType<EditingSession>(attachment.Session);
            Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
            return new Harness(
                composition,
                events,
                commandProbe,
                execution,
                surfaceSize,
                session);
        }
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        private readonly TaskCompletionSource _firstDelivery = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        internal Task FirstDelivery => _firstDelivery.Task;

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            _firstDelivery.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingCommandValidator : ICommandValidator
    {
        private int _invocationCount;

        internal int InvocationCount => Volatile.Read(ref _invocationCount);

        internal ConcurrentQueue<ImmutableArray<VisualStateId>> Targets { get; } = new();

        public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
        {
            _ = document;
            Targets.Enqueue(((MoveVisualStatesCommand)command).Moves
                .Select(static move => move.VisualStateId)
                .ToImmutableArray());
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
            Canvas2DSurfaceSize surfaceSize) => ValueTask.FromResult(Success());

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
}
