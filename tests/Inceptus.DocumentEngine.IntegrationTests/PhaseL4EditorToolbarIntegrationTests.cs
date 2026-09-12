using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseL4EditorToolbarIntegrationTests
{
    private static readonly VisualStateId AlphaId = new("demo:visual:alpha");
    private static readonly VisualStateId BetaId = new("demo:visual:beta");
    private static readonly VisualStateId GammaId = new("demo:visual:gamma");
    private static readonly VisualStateId BetaGammaRouteId = new("demo:visual:beta-gamma");

    [Fact]
    public async Task CenterAnchoredViewportUpdateIsSceneOnlyAndHistoryIsolated()
    {
        var visibleRegion = new RectD(-40d, 15d, 900d, 540d);
        var initialEditorState = new EditorStateSnapshot(
            selection: [AlphaId, BetaId],
            activeToolId: "tool:phase-l4",
            focusTargetId: "focus:canvas",
            viewport: new ViewportSnapshot(
                1.08d,
                new VectorD(30d, 18d),
                visibleRegion),
            temporaryFeedback:
            [
                new EditorFeedbackSnapshot(
                    "feedback:phase-l4",
                    "guide",
                    new RectD(1d, 2d, 3d, 4d)),
            ],
            toolState: [new("snap", PropertyValue.FromBoolean(true))]);
        var harness = await Harness.CreateAsync(initialEditorState);
        await using var session = harness.Session;
        var before = session.CaptureState();
        var documentBefore = harness.Document.CaptureSnapshot();
        var countersBefore = harness.CaptureCounters();
        var sceneBefore = Assert.IsType<Canvas2DScene>(before.CurrentScene);
        var cssCenter = new PointD(
            harness.SurfaceSize.CssWidth / 2d,
            harness.SurfaceSize.CssHeight / 2d);
        var documentAnchor = Canvas2DRenderer.ConvertCssToDocument(sceneBefore, cssCenter);
        const double targetZoom = 1.7d;
        var targetPan = new VectorD(
            cssCenter.X - (documentAnchor.X * targetZoom),
            cssCenter.Y - (documentAnchor.Y * targetZoom));

        var updated = await session.UpdateViewportAsync(new ViewportSnapshot(
            targetZoom,
            targetPan,
            new RectD(999d, 999d, 1d, 1d)));
        var after = session.CaptureState();

        Assert.True(updated.Succeeded);
        Assert.Equal(EditingSessionStatus.Ready, after.Status);
        Assert.NotSame(sceneBefore, after.CurrentScene);
        Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
        Assert.Same(before.LayoutResult, after.LayoutResult);
        Assert.Same(before.RoutingResult, after.RoutingResult);
        AssertPointEqual(cssCenter, after.CurrentScene!.ViewportTransform.TransformPoint(documentAnchor));
        Assert.Equal(targetZoom, after.EditorState.Viewport.Zoom);
        Assert.Equal(targetPan, after.EditorState.Viewport.Pan);
        Assert.Equal(
            Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(
                new ViewportSnapshot(targetZoom, targetPan),
                Canvas2DSceneBuilder.CalculateCanvasCssSurface(
                    initialEditorState.Viewport)),
            after.EditorState.Viewport.VisibleDocumentRegion);
        AssertEditorStateExceptViewportEqual(initialEditorState, after.EditorState);
        Assert.Equal(documentBefore, harness.Document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(new HistoryStatus(0, false, false), after.HistoryStatus);
        Assert.Empty(harness.Events.Events);
        AssertSceneOnlyDelta(countersBefore, harness.CaptureCounters(), sceneBuilds: 1);

        var beforeNoOp = session.CaptureState();
        var noOpCounters = harness.CaptureCounters();
        var unchanged = await session.UpdateViewportAsync(new ViewportSnapshot(
            targetZoom,
            targetPan,
            new RectD(-1d, -1d, 2d, 2d)));

        Assert.True(unchanged.Succeeded);
        Assert.Same(beforeNoOp.EditorState, session.CaptureState().EditorState);
        Assert.Same(beforeNoOp.CurrentScene, session.CaptureState().CurrentScene);
        Assert.Equal(noOpCounters, harness.CaptureCounters());
        Assert.Equal(2d, harness.SurfaceSize.DevicePixelRatio);
    }

    [Fact]
    public async Task ZoomedPointerSelectionGroupMoveAndConnectorHitRemainDocumentSpaceCorrect()
    {
        var harness = await Harness.CreateAsync();
        await using var session = harness.Session;
        await using var interaction = new Canvas2DInteractionController(session);
        await SetCenteredZoomAsync(harness, 1.8d);
        var zoomed = session.CaptureState();
        var viewport = zoomed.EditorState.Viewport;
        var initialDocument = harness.Document.CaptureSnapshot();

        await ClickAsync(interaction, session, BetaId, controlKey: true, pointerId: 10);
        AssertSelection(session.CaptureState(), AlphaId, BetaId);

        var selected = session.CaptureState();
        var beta = FindMovableContent(selected.CurrentScene!, BetaId);
        var start = Center(beta.Bounds);
        var delta = new VectorD(36d, -24d);
        const long dragPointerId = 11;
        var pressed = await interaction.PointerPressedAsync(Pointer(
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

        Assert.Equal(Canvas2DInteractionStatus.Updated, preview.Status);
        Assert.NotNull(preview.SessionState.EditorState.ActiveGesture);
        Assert.Equal("grabbing", preview.CssCursor);

        var released = await interaction.PointerReleasedAsync(Pointer(
            dragPointerId,
            preview.SessionState.CurrentScene!,
            start + delta,
            button: 0));
        await AwaitCommittedPipelineAsync(session, harness.Document);
        var committed = session.CaptureState();

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(released.PersistentOperation!.IsCommitted);
        Assert.Equal(new HistoryStatus(1, true, false), committed.HistoryStatus);
        Assert.Equal(viewport, committed.EditorState.Viewport);
        AssertSelection(committed, AlphaId, BetaId);
        AssertPointEqual(
            Visual(initialDocument, AlphaId).Position + delta,
            Visual(harness.Document.CaptureSnapshot(), AlphaId).Position);
        AssertPointEqual(
            Visual(initialDocument, BetaId).Position + delta,
            Visual(harness.Document.CaptureSnapshot(), BetaId).Position);

        var connectorScene = committed.CurrentScene!;
        var connector = connectorScene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == BetaGammaRouteId &&
            !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));
        var connectorPoint = connector.Transform.TransformPoint(connector.Geometry.Points[1]);
        var cssConnectorPoint = connectorScene.ViewportTransform.TransformPoint(connectorPoint);
        AssertPointEqual(
            connectorPoint,
            Canvas2DRenderer.ConvertCssToDocument(connectorScene, cssConnectorPoint));
        Assert.Equal(
            connector.Id,
            new Canvas2DSceneHitTestService()
                .HitTest(connectorScene, connectorPoint)?.SceneObjectId);

        await ClickPointAsync(
            interaction,
            session,
            connectorPoint,
            controlKey: false,
            pointerId: 12);
        AssertSelection(session.CaptureState(), BetaGammaRouteId);
        Assert.Equal(viewport, session.CaptureState().EditorState.Viewport);
        Assert.Equal(2d, harness.SurfaceSize.DevicePixelRatio);
    }

    [Theory]
    [InlineData(0.5d)]
    [InlineData(1d)]
    [InlineData(1.5d)]
    [InlineData(2d)]
    public async Task ResizeGestureUsesTheSameDocumentDeltaAndCursorAtRepresentativeZooms(
        double zoom)
    {
        var harness = await Harness.CreateAsync();
        await using var session = harness.Session;
        await using var interaction = new Canvas2DInteractionController(session);
        await SetCenteredZoomAsync(harness, zoom);
        var initial = session.CaptureState();
        var viewport = initial.EditorState.Viewport;
        var alpha = Visual(harness.Document.CaptureSnapshot(), AlphaId);
        var initialBounds = new RectD(
            alpha.Position.X,
            alpha.Position.Y,
            alpha.Size.Width,
            alpha.Size.Height);
        var alphaContent = FindMovableContent(initial.CurrentScene!, AlphaId);
        var handle = FindResizeInteraction(initial.CurrentScene!, AlphaId, "southeast");
        var start = Center(handle.Bounds);
        var delta = new VectorD(20d, 15d);
        var end = start + delta;
        var expectedBounds = new RectD(
            initialBounds.X,
            initialBounds.Y,
            initialBounds.Width + delta.X,
            initialBounds.Height + delta.Y);
        var pointerId = checked(200L + (long)(zoom * 10d));

        var hovered = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            initial.CurrentScene!,
            start,
            button: -1));
        Assert.Equal("nwse-resize", hovered.CssCursor);

        var pressed = await interaction.PointerPressedAsync(Pointer(
            pointerId,
            hovered.SessionState.CurrentScene!,
            start,
            button: 0,
            buttons: 1));
        Assert.Equal("nwse-resize", pressed.CssCursor);
        var preview = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            pressed.SessionState.CurrentScene!,
            end,
            button: -1,
            buttons: 1));
        var previewItem = preview.SessionState.CurrentScene!.Items.Single(item =>
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle &&
            item.Origin.VisualStateId == AlphaId &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(alphaContent.Id));
        AssertBoundsEqual(expectedBounds, previewItem.Bounds);
        Assert.Equal(viewport, preview.SessionState.EditorState.Viewport);

        var released = await interaction.PointerReleasedAsync(Pointer(
            pointerId,
            preview.SessionState.CurrentScene!,
            end,
            button: 0));
        await AwaitCommittedPipelineAsync(session, harness.Document);
        var committed = session.CaptureState();
        var resized = Visual(harness.Document.CaptureSnapshot(), AlphaId);

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal("default", released.CssCursor);
        Assert.Equal(ResizeVisualStateCommand.KnownTypeId, released.PersistentOperation!.CommandTypeId);
        AssertPointEqual(expectedBounds.TopLeft, resized.Position);
        Assert.Equal(expectedBounds.Width, resized.Size.Width, precision: 10);
        Assert.Equal(expectedBounds.Height, resized.Size.Height, precision: 10);
        Assert.Equal(viewport, committed.EditorState.Viewport);
        Assert.Equal(new HistoryStatus(1, true, false), committed.HistoryStatus);
    }

    [Fact]
    public async Task ConnectorBendGestureCommitsInDocumentSpaceAfterToolbarZoom()
    {
        var harness = await Harness.CreateAsync();
        await using var session = harness.Session;
        await using var interaction = new Canvas2DInteractionController(session);
        await SetCenteredZoomAsync(harness, 1.5d);
        var viewport = session.CaptureState().EditorState.Viewport;
        var routeBefore = Visual(harness.Document.CaptureSnapshot(), BetaGammaRouteId)
            .Route
            .ToArray();

        await ClickPointAsync(
            interaction,
            session,
            routeBefore[1],
            controlKey: false,
            pointerId: 300);
        var selected = session.CaptureState();
        AssertSelection(selected, BetaGammaRouteId);
        var connector = selected.CurrentScene!.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == BetaGammaRouteId &&
            !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));
        var handle = selected.CurrentScene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == BetaGammaRouteId &&
            item.Origin.StableSourceKey == $"route-bend-handle:{connector.Id.Value}:1" &&
            item.Origin.RelatedSceneObjectIds.Contains(connector.Id));
        var start = Center(handle.Bounds);
        var editedBend = start + new VectorD(27d, -19d);
        const long pointerId = 301;

        var pressed = await interaction.PointerPressedAsync(Pointer(
            pointerId,
            selected.CurrentScene,
            start,
            button: 0,
            buttons: 1));
        var preview = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            pressed.SessionState.CurrentScene!,
            editedBend,
            button: -1,
            buttons: 1));
        Assert.Equal(viewport, preview.SessionState.EditorState.Viewport);
        Assert.Contains(preview.SessionState.CurrentScene!.Items, item =>
            item.Origin.VisualStateId == BetaGammaRouteId &&
            item.Origin.StableSourceKey?.StartsWith(
                "route-preview:",
                StringComparison.Ordinal) == true);

        var released = await interaction.PointerReleasedAsync(Pointer(
            pointerId,
            preview.SessionState.CurrentScene!,
            editedBend,
            button: 0));
        await AwaitCommittedPipelineAsync(session, harness.Document);
        var committed = session.CaptureState();
        var routeAfter = Visual(harness.Document.CaptureSnapshot(), BetaGammaRouteId).Route;

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(
            UpdateConnectionRouteCommand.KnownTypeId,
            released.PersistentOperation!.CommandTypeId);
        Assert.Equal(routeBefore[0], routeAfter[0]);
        Assert.Equal(editedBend, routeAfter[1]);
        Assert.Equal(routeBefore[^1], routeAfter[^1]);
        Assert.Equal(viewport, committed.EditorState.Viewport);
        Assert.Equal(new HistoryStatus(1, true, false), committed.HistoryStatus);
    }

    [Fact]
    public async Task MixedVisualHistoryRoundTripsInOrderAndBranchReplacementIsAtomic()
    {
        var harness = await Harness.CreateAsync();
        await using var session = harness.Session;
        await SetCenteredZoomAsync(harness, 1.6d);
        var selectedEditorState = CopyEditorStateWithSelection(
            session.CaptureState().EditorState,
            [AlphaId, BetaId, GammaId]);
        Assert.True((await session.UpdateEditorStateAsync(selectedEditorState)).Succeeded);
        var baseline = session.CaptureState();
        var viewport = baseline.EditorState.Viewport;
        var countersBeforeHistory = harness.CaptureCounters();
        var expectedVisualStates = new List<ImmutableArray<VisualStateSnapshot>>
        {
            harness.Document.CaptureSnapshot().VisualModel.VisualStates,
        };

        var alpha = Visual(harness.Document.CaptureSnapshot(), AlphaId);
        await CommitAsync(new MoveVisualStateCommand(
            harness.Document.DocumentId,
            harness.Document.Revision,
            AlphaId,
            alpha.Position + new VectorD(45d, 25d),
            VisualPlacementMode.Pinned));
        expectedVisualStates.Add(harness.Document.CaptureSnapshot().VisualModel.VisualStates);

        var beta = Visual(harness.Document.CaptureSnapshot(), BetaId);
        await CommitAsync(new ResizeVisualStateCommand(
            harness.Document.DocumentId,
            harness.Document.Revision,
            BetaId,
            new RectD(
                beta.Position.X - 15d,
                beta.Position.Y - 10d,
                beta.Size.Width + 70d,
                beta.Size.Height + 45d),
            VisualPlacementMode.Pinned));
        expectedVisualStates.Add(harness.Document.CaptureSnapshot().VisualModel.VisualStates);

        var route = Visual(harness.Document.CaptureSnapshot(), BetaGammaRouteId).Route.ToArray();
        route[1] += new VectorD(28d, 34d);
        await CommitAsync(new UpdateConnectionRouteCommand(
            harness.Document.DocumentId,
            harness.Document.Revision,
            BetaGammaRouteId,
            route));
        expectedVisualStates.Add(harness.Document.CaptureSnapshot().VisualModel.VisualStates);

        var beforeGroup = harness.Document.CaptureSnapshot();
        var groupDelta = new VectorD(32d, 18d);
        await CommitAsync(new MoveVisualStatesCommand(
            harness.Document.DocumentId,
            harness.Document.Revision,
            [
                new VisualStateMove(
                    AlphaId,
                    Visual(beforeGroup, AlphaId).Position + groupDelta,
                    VisualPlacementMode.Pinned),
                new VisualStateMove(
                    GammaId,
                    Visual(beforeGroup, GammaId).Position + groupDelta,
                    VisualPlacementMode.Pinned),
            ]));
        expectedVisualStates.Add(harness.Document.CaptureSnapshot().VisualModel.VisualStates);

        var committed = session.CaptureState();
        Assert.Equal(new DocumentRevision(4), committed.DocumentRevision);
        Assert.Equal(new HistoryStatus(4, true, false), committed.HistoryStatus);
        Assert.Equal(viewport, committed.EditorState.Viewport);
        AssertSelection(committed, AlphaId, BetaId, GammaId);
        Assert.Equal(4, harness.Events.Events.Count);
        AssertFullPipelineDelta(
            countersBeforeHistory,
            harness.CaptureCounters(),
            fullRuns: 4,
            layoutRuns: 0);

        for (var index = 3; index >= 0; index--)
        {
            var undo = await session.UndoAsync();
            Assert.True(undo.IsCommitted);
            await AwaitCommittedPipelineAsync(session, harness.Document);
            AssertVisualStatesEqual(
                expectedVisualStates[index],
                harness.Document.CaptureSnapshot().VisualModel.VisualStates);
            var state = session.CaptureState();
            Assert.Equal(viewport, state.EditorState.Viewport);
            AssertSelection(state, AlphaId, BetaId, GammaId);
            Assert.Equal(
                new HistoryStatus(4, canUndo: index > 0, canRedo: true),
                state.HistoryStatus);
        }

        Assert.Equal(new DocumentRevision(8), harness.Document.Revision);
        AssertFullPipelineDelta(
            countersBeforeHistory,
            harness.CaptureCounters(),
            fullRuns: 8,
            layoutRuns: 0);

        for (var index = 1; index <= 4; index++)
        {
            var redo = await session.RedoAsync();
            Assert.True(redo.IsCommitted);
            await AwaitCommittedPipelineAsync(session, harness.Document);
            AssertVisualStatesEqual(
                expectedVisualStates[index],
                harness.Document.CaptureSnapshot().VisualModel.VisualStates);
            var state = session.CaptureState();
            Assert.Equal(viewport, state.EditorState.Viewport);
            AssertSelection(state, AlphaId, BetaId, GammaId);
            Assert.Equal(
                new HistoryStatus(4, canUndo: true, canRedo: index < 4),
                state.HistoryStatus);
        }

        Assert.Equal(new DocumentRevision(12), harness.Document.Revision);
        AssertFullPipelineDelta(
            countersBeforeHistory,
            harness.CaptureCounters(),
            fullRuns: 12,
            layoutRuns: 0);

        Assert.True((await session.UndoAsync()).IsCommitted);
        await AwaitCommittedPipelineAsync(session, harness.Document);
        AssertVisualStatesEqual(
            expectedVisualStates[3],
            harness.Document.CaptureSnapshot().VisualModel.VisualStates);
        var branchBeta = Visual(harness.Document.CaptureSnapshot(), BetaId);
        await CommitAsync(new MoveVisualStateCommand(
            harness.Document.DocumentId,
            harness.Document.Revision,
            BetaId,
            branchBeta.Position + new VectorD(19d, -11d),
            VisualPlacementMode.Pinned));
        var branchState = session.CaptureState();
        var beforeUnavailableRedo = harness.CaptureCounters();
        var unavailableRedo = await session.RedoAsync();

        Assert.Equal(HistoryOperationStatus.NothingToRedo, unavailableRedo.Status);
        Assert.Equal(new DocumentRevision(14), harness.Document.Revision);
        Assert.Equal(new HistoryStatus(4, true, false), branchState.HistoryStatus);
        Assert.Equal(beforeUnavailableRedo, harness.CaptureCounters());
        Assert.Equal(14, harness.Events.Events.Count);
        Assert.Equal(
            Enumerable.Range(1, 14).Select(value => new DocumentRevision((ulong)value)),
            harness.Events.Events.Select(static change => change.CommittedRevision));
        AssertFullPipelineDelta(
            countersBeforeHistory,
            harness.CaptureCounters(),
            fullRuns: 14,
            layoutRuns: 0);
        Assert.Equal(viewport, session.CaptureState().EditorState.Viewport);
        AssertSelection(session.CaptureState(), AlphaId, BetaId, GammaId);

        async Task CommitAsync(ICommand command)
        {
            var result = await session.ExecuteAsync(command);
            Assert.True(result.IsCommitted);
            await AwaitCommittedPipelineAsync(session, harness.Document);
            Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
        }
    }

    private static async Task SetCenteredZoomAsync(Harness harness, double targetZoom)
    {
        var state = harness.Session.CaptureState();
        var cssCenter = new PointD(
            harness.SurfaceSize.CssWidth / 2d,
            harness.SurfaceSize.CssHeight / 2d);
        var documentAnchor = Canvas2DRenderer.ConvertCssToDocument(
            state.CurrentScene!,
            cssCenter);
        var targetPan = new VectorD(
            cssCenter.X - (documentAnchor.X * targetZoom),
            cssCenter.Y - (documentAnchor.Y * targetZoom));
        var result = await harness.Session.UpdateViewportAsync(new ViewportSnapshot(
            targetZoom,
            targetPan,
            state.EditorState.Viewport.VisibleDocumentRegion));
        Assert.True(result.Succeeded);
        AssertPointEqual(
            cssCenter,
            harness.Session.CaptureState().CurrentScene!.ViewportTransform.TransformPoint(
                documentAnchor));
    }

    private static async Task AwaitCommittedPipelineAsync(
        EditingSession session,
        Document document)
    {
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task ClickAsync(
        Canvas2DInteractionController interaction,
        EditingSession session,
        VisualStateId visualStateId,
        bool controlKey,
        long pointerId)
    {
        var scene = session.CaptureState().CurrentScene!;
        await ClickPointAsync(
            interaction,
            session,
            Center(FindMovableContent(scene, visualStateId).Bounds),
            controlKey,
            pointerId);
    }

    private static async Task ClickPointAsync(
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
        _ = await interaction.PointerReleasedAsync(Pointer(
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

    private static Canvas2DSceneItem FindResizeInteraction(
        Canvas2DScene scene,
        VisualStateId visualStateId,
        string role) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.StableSourceKey?.StartsWith(
                $"resize-handle:{role}:",
                StringComparison.Ordinal) == true &&
            item.HitTestPolicy.Mode == Canvas2DHitTestMode.Bounds);

    private static VisualStateSnapshot Visual(DocumentSnapshot document, VisualStateId id)
    {
        Assert.True(document.VisualModel.TryGetVisualState(id, out var visual));
        return Assert.IsType<VisualStateSnapshot>(visual);
    }

    private static EditorStateSnapshot CopyEditorStateWithSelection(
        EditorStateSnapshot source,
        IEnumerable<VisualStateId> selection) =>
        new(
            selection,
            source.HoveredObjectId,
            source.ActiveToolId,
            source.FocusTargetId,
            source.Viewport,
            source.ActiveGesture,
            source.TemporaryFeedback,
            source.ToolState);

    private static PointD Center(RectD bounds) =>
        new(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static void AssertSelection(EditingSessionState state, params VisualStateId[] expected) =>
        Assert.True(expected.AsSpan().SequenceEqual(state.EditorState.Selection.AsSpan()));

    private static void AssertEditorStateExceptViewportEqual(
        EditorStateSnapshot expected,
        EditorStateSnapshot actual)
    {
        Assert.True(expected.Selection.AsSpan().SequenceEqual(actual.Selection.AsSpan()));
        Assert.Equal(expected.HoveredObjectId, actual.HoveredObjectId);
        Assert.Equal(expected.ActiveToolId, actual.ActiveToolId);
        Assert.Equal(expected.FocusTargetId, actual.FocusTargetId);
        Assert.Equal(expected.ActiveGesture, actual.ActiveGesture);
        Assert.True(expected.TemporaryFeedback.AsSpan().SequenceEqual(
            actual.TemporaryFeedback.AsSpan()));
        Assert.Equal(expected.ToolState, actual.ToolState);
    }

    private static void AssertVisualStatesEqual(
        ImmutableArray<VisualStateSnapshot> expected,
        ImmutableArray<VisualStateSnapshot> actual) =>
        Assert.True(expected.AsSpan().SequenceEqual(actual.AsSpan()));

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

    private static void AssertSceneOnlyDelta(
        CounterSnapshot before,
        CounterSnapshot after,
        int sceneBuilds)
    {
        Assert.Equal(before.Projection, after.Projection);
        Assert.Equal(before.Layout, after.Layout);
        Assert.Equal(before.Routing, after.Routing);
        Assert.Equal(before.Scene + sceneBuilds, after.Scene);
        Assert.Equal(before.Render + sceneBuilds, after.Render);
    }

    private static void AssertFullPipelineDelta(
        CounterSnapshot before,
        CounterSnapshot after,
        int fullRuns,
        int? layoutRuns = null)
    {
        Assert.Equal(before.Projection + (5 * fullRuns), after.Projection);
        Assert.Equal(before.Layout + (layoutRuns ?? fullRuns), after.Layout);
        Assert.Equal(before.Routing + fullRuns, after.Routing);
        Assert.Equal(before.Scene + fullRuns, after.Scene);
        Assert.Equal(before.Render + fullRuns, after.Render);
    }

    private readonly record struct CounterSnapshot(
        int Projection,
        int Layout,
        int Routing,
        int Scene,
        int Render);

    private sealed class Harness
    {
        private Harness(
            NeutralDemoComposition composition,
            RecordingSubscriber events,
            RecordingRenderExecution execution,
            Canvas2DSurfaceSize surfaceSize,
            EditingSession session)
        {
            Document = composition.Document;
            Counters = composition.Counters;
            Events = events;
            Execution = execution;
            SurfaceSize = surfaceSize;
            Session = session;
        }

        internal Document Document { get; }

        internal NeutralDemoPipelineCounters Counters { get; }

        internal RecordingSubscriber Events { get; }

        internal RecordingRenderExecution Execution { get; }

        internal Canvas2DSurfaceSize SurfaceSize { get; }

        internal EditingSession Session { get; }

        internal CounterSnapshot CaptureCounters() =>
            new(
                Counters.ProjectionRuleInvocationCount,
                Counters.LayoutInvocationCount,
                Counters.RoutingInvocationCount,
                Counters.SceneContributionInvocationCount,
                Execution.RenderCount);

        internal static async Task<Harness> CreateAsync(
            EditorStateSnapshot? initialEditorState = null)
        {
            var composition = NeutralDemoPipeline.CreateComposition();
            var events = new RecordingSubscriber();
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
                initialEditorState ?? source.InitialEditorState,
                source.CommandHandlers,
                source.CommandValidators,
                source.HistoryPolicies,
                [events]);
            var execution = new RecordingRenderExecution();
            var surfaceSize = new Canvas2DSurfaceSize(960d, 640d, 2d);
            var renderer = new Canvas2DRenderer(execution, RendererConfiguration());
            Assert.True((await renderer.InitializeAsync(
                "phase-l4-editor-toolbar-canvas",
                surfaceSize)).Succeeded);
            var attachment = await EditingSession.AttachAsync(
                composition.Document,
                renderer,
                configuration);
            Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
            return new Harness(
                composition,
                events,
                execution,
                surfaceSize,
                Assert.IsType<EditingSession>(attachment.Session));
        }
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
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
