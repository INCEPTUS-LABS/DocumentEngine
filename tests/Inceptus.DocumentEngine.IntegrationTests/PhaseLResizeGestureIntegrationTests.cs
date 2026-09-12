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
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseLResizeGestureIntegrationTests
{
    [Theory]
    [InlineData("north", 60d, 85d, 150d, 55d, true)]
    [InlineData("northeast", 60d, 85d, 170d, 55d, false)]
    [InlineData("east", 60d, 70d, 170d, 70d, false)]
    [InlineData("southeast", 60d, 70d, 170d, 85d, true)]
    [InlineData("south", 60d, 70d, 150d, 85d, false)]
    [InlineData("southwest", 80d, 70d, 130d, 85d, false)]
    [InlineData("west", 80d, 70d, 130d, 70d, true)]
    [InlineData("northwest", 80d, 85d, 130d, 55d, true)]
    public async Task EveryResizeDirectionPreviewsTransientlyAndCommitsExactlyOnce(
        string role,
        double expectedX,
        double expectedY,
        double expectedWidth,
        double expectedHeight,
        bool verifyUndoRedo)
    {
        Assert.True(Canvas2DResizeGeometry.TryParseRole(role, out var direction));
        var context = await AttachAsync();
        await using var session = context.Session;
        await using var interaction = new Canvas2DInteractionController(session);
        var initial = session.CaptureState();
        var initialDocument = context.Document.CaptureSnapshot();
        var alphaVisual = FindVisual(initialDocument, "demo:alpha");
        var alphaContent = FindVisualContent(initial.CurrentScene!, alphaVisual.Id);
        AssertCursorOnlyResizeInteractions(initial.CurrentScene!, alphaVisual.Id);
        var handle = FindResizeHandle(initial.CurrentScene!, alphaVisual.Id, direction);
        var initialProjectionCount = context.Counters.ProjectionRuleInvocationCount;
        var initialLayoutCount = context.Counters.LayoutInvocationCount;
        var initialRoutingCount = context.Counters.RoutingInvocationCount;
        var initialSceneCount = context.Counters.SceneContributionInvocationCount;
        var initialRenderCount = context.Execution.RenderCount;
        var initialElements = initialDocument.SemanticModel.Elements;
        var initialRelationships = initialDocument.SemanticModel.Relationships;
        var expectedBounds = new RectD(
            expectedX,
            expectedY,
            expectedWidth,
            expectedHeight);
        var start = Center(handle.Bounds);
        var firstPoint = start + new VectorD(7d, 5d);
        var finalPoint = start + new VectorD(20d, 15d);
        const long pointerId = 800;

        Assert.Equal(1.08d, initial.EditorState.Viewport.Zoom);
        Assert.Equal(new VectorD(30d, 18d), initial.EditorState.Viewport.Pan);

        var pressed = await interaction.PointerPressedAsync(Pointer(
            pointerId,
            initial.CurrentScene!,
            start,
            button: 0,
            buttons: 1));
        var firstMove = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            pressed.SessionState.CurrentScene!,
            firstPoint,
            buttons: 1));
        var secondMove = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            firstMove.SessionState.CurrentScene!,
            finalPoint,
            buttons: 1));
        var unchangedMove = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            secondMove.SessionState.CurrentScene!,
            finalPoint,
            buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        Assert.Equal(Canvas2DResizeGeometry.CssCursor(direction), pressed.CssCursor);
        Assert.Equal(Canvas2DInteractionStatus.Updated, firstMove.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, secondMove.Status);
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, unchangedMove.Status);
        var activeGesture = Assert.IsType<EditorGestureSnapshot>(
            secondMove.SessionState.EditorState.ActiveGesture);
        Assert.Equal(Canvas2DResizeGestureMetadata.Kind, activeGesture.Kind);
        Assert.True(activeGesture.Properties.TryGetValue(
            Canvas2DResizeGestureMetadata.HandleRole,
            out var activeRole));
        Assert.Equal(role, activeRole.TextValue);
        AssertBoundsEqual(expectedBounds, FindResizePreview(
            secondMove.SessionState.CurrentScene!,
            alphaVisual.Id,
            alphaContent.Id).Bounds);
        AssertOppositeSidesAnchored(Bounds(alphaVisual), expectedBounds, direction);
        Assert.Same(initial.ProjectedGraph, secondMove.SessionState.ProjectedGraph);
        Assert.Same(initial.LayoutResult, secondMove.SessionState.LayoutResult);
        Assert.Same(initial.RoutingResult, secondMove.SessionState.RoutingResult);
        Assert.Equal(initialDocument, context.Document.CaptureSnapshot());
        Assert.Equal(new HistoryStatus(0, false, false), secondMove.SessionState.HistoryStatus);
        Assert.Empty(context.Events.Events);
        Assert.Equal(0, context.ResizeProbe.InvocationCount);
        Assert.Equal(initialProjectionCount, context.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, context.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount, context.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 3, context.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 3, context.Execution.RenderCount);

        var released = await interaction.PointerReleasedAsync(Pointer(
            pointerId,
            unchangedMove.SessionState.CurrentScene!,
            finalPoint,
            button: 0));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var committed = session.CaptureState();
        var committedDocument = context.Document.CaptureSnapshot();

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal("default", released.CssCursor);
        Assert.Equal(ResizeVisualStateCommand.KnownTypeId, released.PersistentOperation!.CommandTypeId);
        Assert.True(released.PersistentOperation.IsCommitted);
        Assert.Equal(new DocumentRevision(1), committedDocument.Revision);
        AssertBoundsEqual(expectedBounds, Bounds(FindVisual(committedDocument, "demo:alpha")));
        Assert.Equal(
            VisualPlacementMode.Pinned,
            FindVisual(committedDocument, "demo:alpha").PlacementMode);
        AssertBoundsEqual(expectedBounds, FindVisualContent(
            committed.CurrentScene!,
            alphaVisual.Id).Bounds);
        Assert.Null(committed.EditorState.ActiveGesture);
        Assert.Equal(new HistoryStatus(1, true, false), committed.HistoryStatus);
        AssertSemanticMeaningUnchanged(initialElements, initialRelationships, committedDocument);
        Assert.Equal([new DocumentRevision(1)], EventRevisions(context.Events));
        Assert.Equal(1, context.ResizeProbe.InvocationCount);
        Assert.Equal(initialProjectionCount + 5, context.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, context.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount + 1, context.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 4, context.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 4, context.Execution.RenderCount);

        if (!verifyUndoRedo)
        {
            return;
        }

        var undo = await session.UndoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var undone = session.CaptureState();
        Assert.True(undo.IsCommitted);
        AssertBoundsEqual(
            Bounds(alphaVisual),
            Bounds(FindVisual(context.Document.CaptureSnapshot(), "demo:alpha")));
        Assert.Equal(alphaVisual.PlacementMode, FindVisual(
            context.Document.CaptureSnapshot(),
            "demo:alpha").PlacementMode);
        Assert.Equal(new HistoryStatus(1, false, true), undone.HistoryStatus);

        var redo = await session.RedoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var redone = session.CaptureState();
        Assert.True(redo.IsCommitted);
        AssertBoundsEqual(
            expectedBounds,
            Bounds(FindVisual(context.Document.CaptureSnapshot(), "demo:alpha")));
        Assert.Equal(VisualPlacementMode.Pinned, FindVisual(
            context.Document.CaptureSnapshot(),
            "demo:alpha").PlacementMode);
        Assert.Equal(new HistoryStatus(1, true, false), redone.HistoryStatus);
        Assert.Equal(
            [new DocumentRevision(1), new DocumentRevision(2), new DocumentRevision(3)],
            EventRevisions(context.Events));
        Assert.Equal(3, context.ResizeProbe.InvocationCount);
        Assert.Equal(initialProjectionCount + 15, context.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, context.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount + 3, context.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 6, context.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 6, context.Execution.RenderCount);
    }

    [Fact]
    public async Task NorthWestResizeOfConnectedNodeReanchorsOnlyAfterCommitAndRoundTrips()
    {
        var context = await AttachAsync();
        await using var session = context.Session;
        await using var interaction = new Canvas2DInteractionController(session);
        var initialDocument = context.Document.CaptureSnapshot();
        var betaVisual = FindVisual(initialDocument, "demo:beta");
        var betaContent = FindVisualContent(session.CaptureState().CurrentScene!, betaVisual.Id);

        var selected = await interaction.PointerReleasedAsync(Pointer(
            900,
            session.CaptureState().CurrentScene!,
            Center(betaContent.Bounds),
            button: 0));
        Assert.Equal(Canvas2DInteractionStatus.Updated, selected.Status);
        var baseline = selected.SessionState;
        var originalBounds = Bounds(betaVisual);
        var originalConnector = FindConnector(
            baseline.CurrentScene!,
            "demo:beta-gamma").Geometry.Points;
        var handle = FindResizeHandle(
            baseline.CurrentScene!,
            betaVisual.Id,
            Canvas2DResizeDirection.NorthWest);
        var start = Center(handle.Bounds);
        var finalPoint = start + new VectorD(-30d, -20d);
        var expectedBounds = new RectD(270d, 170d, 190d, 100d);
        var initialProjectionCount = context.Counters.ProjectionRuleInvocationCount;
        var initialLayoutCount = context.Counters.LayoutInvocationCount;
        var initialRoutingCount = context.Counters.RoutingInvocationCount;
        var initialSceneCount = context.Counters.SceneContributionInvocationCount;
        var initialRenderCount = context.Execution.RenderCount;
        var initialElements = initialDocument.SemanticModel.Elements;
        var initialRelationships = initialDocument.SemanticModel.Relationships;

        var pressed = await interaction.PointerPressedAsync(Pointer(
            901,
            baseline.CurrentScene!,
            start,
            button: 0,
            buttons: 1));
        var moved = await interaction.PointerMovedAsync(Pointer(
            901,
            pressed.SessionState.CurrentScene!,
            finalPoint,
            buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        AssertBoundsEqual(expectedBounds, FindResizePreview(
            moved.SessionState.CurrentScene!,
            betaVisual.Id,
            betaContent.Id).Bounds);
        Assert.Equal(originalBounds.Right, expectedBounds.Right, precision: 10);
        Assert.Equal(originalBounds.Bottom, expectedBounds.Bottom, precision: 10);
        AssertRouteEqual(originalConnector, FindConnector(
            moved.SessionState.CurrentScene!,
            "demo:beta-gamma").Geometry.Points);
        Assert.Same(baseline.RoutingResult, moved.SessionState.RoutingResult);
        Assert.Equal(initialDocument, context.Document.CaptureSnapshot());
        Assert.Equal(0, context.ResizeProbe.InvocationCount);
        Assert.Equal(initialProjectionCount, context.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, context.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount, context.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 2, context.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 2, context.Execution.RenderCount);

        var released = await interaction.PointerReleasedAsync(Pointer(
            901,
            moved.SessionState.CurrentScene!,
            finalPoint,
            button: 0));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var committed = session.CaptureState();
        var committedDocument = context.Document.CaptureSnapshot();
        var committedRoute = FindConnector(
            committed.CurrentScene!,
            "demo:beta-gamma").Geometry.Points;

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        AssertBoundsEqual(expectedBounds, Bounds(FindVisual(committedDocument, "demo:beta")));
        Assert.Equal(new PointD(460d, 220d), committedRoute[0]);
        Assert.NotEqual(originalConnector[0], committedRoute[0]);
        AssertSemanticMeaningUnchanged(initialElements, initialRelationships, committedDocument);
        Assert.Equal(1, context.ResizeProbe.InvocationCount);
        Assert.Equal([new DocumentRevision(1)], EventRevisions(context.Events));
        Assert.Equal(initialProjectionCount + 5, context.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, context.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount + 1, context.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 3, context.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 3, context.Execution.RenderCount);

        var undo = await session.UndoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(undo.IsCommitted);
        AssertBoundsEqual(
            originalBounds,
            Bounds(FindVisual(context.Document.CaptureSnapshot(), "demo:beta")));
        AssertRouteEqual(originalConnector, FindConnector(
            session.CaptureState().CurrentScene!,
            "demo:beta-gamma").Geometry.Points);

        var redo = await session.RedoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(redo.IsCommitted);
        AssertBoundsEqual(
            expectedBounds,
            Bounds(FindVisual(context.Document.CaptureSnapshot(), "demo:beta")));
        AssertRouteEqual(committedRoute, FindConnector(
            session.CaptureState().CurrentScene!,
            "demo:beta-gamma").Geometry.Points);
        Assert.Equal(3, context.ResizeProbe.InvocationCount);
        Assert.Equal(
            [new DocumentRevision(1), new DocumentRevision(2), new DocumentRevision(3)],
            EventRevisions(context.Events));
        Assert.Equal(initialProjectionCount + 15, context.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, context.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount + 3, context.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 5, context.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 5, context.Execution.RenderCount);
    }

    [Fact]
    public async Task SelectedAlphaResizePreviewsCommitOnceThenUndoAndRedo()
    {
        var context = await AttachAsync();
        await using var session = context.Session;
        await using var interaction = new Canvas2DInteractionController(session);
        var initial = session.CaptureState();
        var initialDocument = context.Document.CaptureSnapshot();
        var alphaVisual = FindVisual(initialDocument, "demo:alpha");
        var alphaContent = FindVisualContent(initial.CurrentScene!, alphaVisual.Id);
        var handle = FindSouthEastHandle(initial.CurrentScene!, alphaVisual.Id);
        Assert.Contains(alphaVisual.Id, initial.EditorState.Selection);
        Assert.Equal(new PointD(alphaContent.Bounds.Right, alphaContent.Bounds.Bottom), Center(handle.Bounds));
        var initialProjectionCount = context.Counters.ProjectionRuleInvocationCount;
        var initialLayoutCount = context.Counters.LayoutInvocationCount;
        var initialRoutingCount = context.Counters.RoutingInvocationCount;
        var initialSceneCount = context.Counters.SceneContributionInvocationCount;
        var initialRenderCount = context.Execution.RenderCount;
        var initialElements = initialDocument.SemanticModel.Elements;
        var initialRelationships = initialDocument.SemanticModel.Relationships;
        const long pointerId = 107;
        var start = Center(handle.Bounds);

        var pressed = await interaction.PointerPressedAsync(Pointer(
            pointerId,
            initial.CurrentScene!,
            start,
            button: 0,
            buttons: 1));
        var firstPoint = start + new VectorD(24d, 16d);
        var firstMove = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            pressed.SessionState.CurrentScene!,
            firstPoint,
            buttons: 1));
        var finalPoint = start + new VectorD(58d, 37d);
        var secondMove = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            firstMove.SessionState.CurrentScene!,
            finalPoint,
            buttons: 1));
        var expectedBounds = new RectD(
            alphaVisual.Position.X,
            alphaVisual.Position.Y,
            alphaVisual.Size.Width + 58d,
            alphaVisual.Size.Height + 37d);

        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, firstMove.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, secondMove.Status);
        Assert.Equal("inceptus.canvas2d:resize", secondMove.SessionState.EditorState.ActiveGesture?.Kind);
        AssertBoundsEqual(expectedBounds, FindResizePreview(
            secondMove.SessionState.CurrentScene!,
            alphaVisual.Id,
            alphaContent.Id).Bounds);
        var initialScene = Assert.IsType<Canvas2DScene>(initial.CurrentScene);
        var previewScene = Assert.IsType<Canvas2DScene>(secondMove.SessionState.CurrentScene);
        var alphaPersistentItems = initialScene.Items
            .Where(item =>
                item.Origin.VisualStateId == alphaVisual.Id &&
                (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0)
            .ToArray();
        var alphaResizePreviews = previewScene.Items
            .Where(item =>
                item.Layer == Canvas2DSceneLayer.Overlay &&
                item.Origin.VisualStateId == alphaVisual.Id &&
                item.Origin.StableSourceKey?.StartsWith(
                    "resize-preview:",
                    StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(3, alphaPersistentItems.Length);
        Assert.Equal(alphaPersistentItems.Length, alphaResizePreviews.Length);
        Assert.All(alphaPersistentItems, item => Assert.Contains(
            alphaResizePreviews,
            preview => preview.Origin.RelatedSceneObjectIds.Contains(item.Id)));
        Assert.Same(initial.ProjectedGraph, secondMove.SessionState.ProjectedGraph);
        Assert.Same(initial.LayoutResult, secondMove.SessionState.LayoutResult);
        Assert.Same(initial.RoutingResult, secondMove.SessionState.RoutingResult);
        Assert.Equal(initialDocument, context.Document.CaptureSnapshot());
        Assert.Equal(new HistoryStatus(0, false, false), secondMove.SessionState.HistoryStatus);
        Assert.Empty(context.Events.Events);
        Assert.Equal(0, context.ResizeProbe.InvocationCount);
        Assert.Equal(initialProjectionCount, context.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, context.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount, context.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 3, context.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 3, context.Execution.RenderCount);

        var released = await interaction.PointerReleasedAsync(Pointer(
            pointerId,
            secondMove.SessionState.CurrentScene!,
            finalPoint,
            button: 0));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var committed = session.CaptureState();
        var committedDocument = context.Document.CaptureSnapshot();

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(released.PersistentOperation!.IsCommitted);
        Assert.Equal(new DocumentRevision(1), committedDocument.Revision);
        AssertBoundsEqual(expectedBounds, Bounds(FindVisual(committedDocument, "demo:alpha")));
        Assert.Equal(VisualPlacementMode.Pinned, FindVisual(committedDocument, "demo:alpha").PlacementMode);
        AssertBoundsEqual(expectedBounds, FindVisualContent(committed.CurrentScene!, alphaVisual.Id).Bounds);
        Assert.Equal(new HistoryStatus(1, true, false), committed.HistoryStatus);
        Assert.Null(committed.EditorState.ActiveGesture);
        AssertSemanticMeaningUnchanged(initialElements, initialRelationships, committedDocument);
        Assert.Equal([new DocumentRevision(1)], EventRevisions(context.Events));
        Assert.Equal(1, context.ResizeProbe.InvocationCount);
        Assert.Equal(initialProjectionCount + 5, context.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, context.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount + 1, context.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 4, context.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 4, context.Execution.RenderCount);

        var undo = await session.UndoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var undone = session.CaptureState();
        var undoneDocument = context.Document.CaptureSnapshot();

        Assert.True(undo.IsCommitted);
        Assert.Equal(new DocumentRevision(2), undoneDocument.Revision);
        AssertBoundsEqual(Bounds(alphaVisual), Bounds(FindVisual(undoneDocument, "demo:alpha")));
        Assert.Equal(alphaVisual.PlacementMode, FindVisual(undoneDocument, "demo:alpha").PlacementMode);
        AssertBoundsEqual(Bounds(alphaVisual), FindVisualContent(undone.CurrentScene!, alphaVisual.Id).Bounds);
        Assert.Equal(new HistoryStatus(1, false, true), undone.HistoryStatus);
        Assert.Null(undone.EditorState.ActiveGesture);
        AssertSemanticMeaningUnchanged(initialElements, initialRelationships, undoneDocument);

        var redo = await session.RedoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var redone = session.CaptureState();
        var redoneDocument = context.Document.CaptureSnapshot();

        Assert.True(redo.IsCommitted);
        Assert.Equal(new DocumentRevision(3), redoneDocument.Revision);
        AssertBoundsEqual(expectedBounds, Bounds(FindVisual(redoneDocument, "demo:alpha")));
        Assert.Equal(VisualPlacementMode.Pinned, FindVisual(redoneDocument, "demo:alpha").PlacementMode);
        AssertBoundsEqual(expectedBounds, FindVisualContent(redone.CurrentScene!, alphaVisual.Id).Bounds);
        Assert.Equal(new HistoryStatus(1, true, false), redone.HistoryStatus);
        Assert.Null(redone.EditorState.ActiveGesture);
        AssertSemanticMeaningUnchanged(initialElements, initialRelationships, redoneDocument);
        Assert.Equal(
            [new DocumentRevision(1), new DocumentRevision(2), new DocumentRevision(3)],
            EventRevisions(context.Events));
        Assert.Equal(3, context.ResizeProbe.InvocationCount);
        Assert.Equal(initialProjectionCount + 15, context.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, context.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount + 3, context.Counters.RoutingInvocationCount);
        Assert.Equal(initialSceneCount + 6, context.Counters.SceneContributionInvocationCount);
        Assert.Equal(initialRenderCount + 6, context.Execution.RenderCount);
    }

    [Fact]
    public async Task MinimumPreviewCancellationNoOpAndStaleResizeCreateNoResizeCommit()
    {
        var context = await AttachAsync();
        await using var session = context.Session;
        await using var interaction = new Canvas2DInteractionController(session);
        var initial = session.CaptureState();
        var initialDocument = context.Document.CaptureSnapshot();
        var alphaVisual = FindVisual(initialDocument, "demo:alpha");
        var initialProjectionCount = context.Counters.ProjectionRuleInvocationCount;
        var initialLayoutCount = context.Counters.LayoutInvocationCount;
        var initialRoutingCount = context.Counters.RoutingInvocationCount;
        var handle = FindSouthEastHandle(initial.CurrentScene!, alphaVisual.Id);
        var start = Center(handle.Bounds);

        var pressed = await interaction.PointerPressedAsync(Pointer(
            201,
            initial.CurrentScene!,
            start,
            button: 0,
            buttons: 1));
        var belowMinimum = new PointD(alphaVisual.Position.X - 20d, alphaVisual.Position.Y - 20d);
        var minimum = await interaction.PointerMovedAsync(Pointer(
            201,
            pressed.SessionState.CurrentScene!,
            belowMinimum,
            buttons: 1));

        Assert.Equal(
            new RectD(alphaVisual.Position.X, alphaVisual.Position.Y, 1d, 1d),
            FindResizePreview(
                minimum.SessionState.CurrentScene!,
                alphaVisual.Id,
                FindVisualContent(initial.CurrentScene!, alphaVisual.Id).Id).Bounds);
        Assert.Equal(initialDocument, context.Document.CaptureSnapshot());
        Assert.Empty(context.Events.Events);
        Assert.Equal(0, context.ResizeProbe.InvocationCount);

        var cancelled = await interaction.PointerCancelledAsync(201);
        Assert.Equal(Canvas2DInteractionStatus.Updated, cancelled.Status);
        Assert.Null(cancelled.SessionState.EditorState.ActiveGesture);
        Assert.Equal(initialDocument, context.Document.CaptureSnapshot());

        var noOpHandle = FindSouthEastHandle(cancelled.SessionState.CurrentScene!, alphaVisual.Id);
        var noOpPoint = Center(noOpHandle.Bounds);
        var noOpPressed = await interaction.PointerPressedAsync(Pointer(
            202,
            cancelled.SessionState.CurrentScene!,
            noOpPoint,
            button: 0,
            buttons: 1));
        var noOp = await interaction.PointerReleasedAsync(Pointer(
            202,
            noOpPressed.SessionState.CurrentScene!,
            noOpPoint,
            button: 0));

        Assert.Equal(Canvas2DInteractionStatus.Updated, noOp.Status);
        Assert.Null(noOp.PersistentOperation);
        Assert.Null(noOp.SessionState.EditorState.ActiveGesture);
        Assert.Equal(initialDocument, context.Document.CaptureSnapshot());
        Assert.Empty(context.Events.Events);
        Assert.Equal(0, context.ResizeProbe.InvocationCount);

        var staleHandle = FindSouthEastHandle(noOp.SessionState.CurrentScene!, alphaVisual.Id);
        var stalePoint = Center(staleHandle.Bounds);
        var stalePressed = await interaction.PointerPressedAsync(Pointer(
            203,
            noOp.SessionState.CurrentScene!,
            stalePoint,
            button: 0,
            buttons: 1));
        var beta = FindVisual(initialDocument, "demo:beta");
        var external = await session.ExecuteAsync(new MoveVisualStateCommand(
            context.Document.DocumentId,
            context.Document.Revision,
            beta.Id,
            beta.Position + new VectorD(12d, 8d),
            VisualPlacementMode.Pinned));
        Assert.True(external.IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        var stale = await interaction.PointerReleasedAsync(Pointer(
            203,
            session.CaptureState().CurrentScene!,
            stalePoint + new VectorD(30d, 20d),
            button: 0));

        Assert.Equal(Canvas2DInteractionStatus.Stale, stale.Status);
        Assert.Null(stale.SessionState.EditorState.ActiveGesture);
        AssertBoundsEqual(
            Bounds(alphaVisual),
            Bounds(FindVisual(context.Document.CaptureSnapshot(), "demo:alpha")));
        Assert.Equal(0, context.ResizeProbe.InvocationCount);
        Assert.Equal(new HistoryStatus(1, true, false), stale.SessionState.HistoryStatus);
        Assert.Equal([new DocumentRevision(1)], EventRevisions(context.Events));
        Assert.Equal(initialProjectionCount + 5, context.Counters.ProjectionRuleInvocationCount);
        Assert.Equal(initialLayoutCount, context.Counters.LayoutInvocationCount);
        Assert.Equal(initialRoutingCount + 1, context.Counters.RoutingInvocationCount);
    }

    private static async ValueTask<ResizeIntegrationContext> AttachAsync()
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var events = new RecordingSubscriber();
        var resizeProbe = new CountingCommandValidator();
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
                ResizeVisualStateCommand.KnownTypeId,
                new CommandValidatorId("test:phase-l:resize-count"),
                resizeProbe)),
            source.HistoryPolicies,
            [events]);
        var execution = new RecordingRenderExecution();
        var renderer = new Canvas2DRenderer(execution, RendererConfiguration());
        Assert.True((await renderer.InitializeAsync(
            "phase-l-resize-canvas",
            new Canvas2DSurfaceSize(960d, 640d, 2d))).Succeeded);
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            configuration);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        return new ResizeIntegrationContext(
            attachment.Session!,
            composition.Document,
            composition.Counters,
            execution,
            events,
            resizeProbe);
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

    private static Canvas2DSceneItem FindSouthEastHandle(
        Canvas2D.Scene.Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-handle:southeast:",
                StringComparison.Ordinal) == true &&
            item.HitTestPolicy.Mode == Canvas2DHitTestMode.Bounds);

    private static Canvas2DSceneItem FindResizeHandle(
        Canvas2D.Scene.Canvas2DScene scene,
        VisualStateId visualStateId,
        Canvas2DResizeDirection direction)
    {
        var prefix = Canvas2DResizeGeometry.IsCorner(direction)
            ? "resize-handle:"
            : "resize-edge-zone:";
        return scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.StableSourceKey?.StartsWith(
                $"{prefix}{Canvas2DResizeGeometry.Role(direction)}:",
                StringComparison.Ordinal) == true &&
            item.HitTestPolicy.Mode == Canvas2DHitTestMode.Bounds);
    }

    private static Canvas2DSceneItem FindResizePreview(
        Canvas2D.Scene.Canvas2DScene scene,
        VisualStateId visualStateId,
        SceneObjectId targetSceneObjectId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(targetSceneObjectId) &&
            item.HitTestPolicy.Mode == Canvas2DHitTestMode.None);

    private static void AssertCursorOnlyResizeInteractions(
        Canvas2D.Scene.Canvas2DScene scene,
        VisualStateId visualStateId)
    {
        var interactions = scene.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.StableSourceKey is { } stableKey &&
            stableKey.StartsWith("resize-", StringComparison.Ordinal) &&
            !stableKey.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal)).ToArray();
        var byRole = interactions.ToDictionary(
            item => item.Metadata[Canvas2DResizeGestureMetadata.HandleRole].TextValue,
            StringComparer.Ordinal);

        Assert.Equal(8, interactions.Length);
        Assert.Equal(
            Canvas2DResizeGeometry.Directions
                .Select(Canvas2DResizeGeometry.Role)
                .Order(StringComparer.Ordinal),
            byRole.Keys.Order(StringComparer.Ordinal));
        Assert.All(interactions, item =>
        {
            Assert.True(item.IsVisible);
            Assert.Equal(0d, item.Style.Opacity);
            Assert.Null(item.Style.Fill);
            Assert.Null(item.Style.Stroke);
            Assert.Equal(Canvas2DHitTestMode.Bounds, item.HitTestPolicy.Mode);
            Assert.Equal(
                Canvas2DSceneObjectIdentity.ForEditorState(item.Origin.StableSourceKey!),
                item.Id);
        });
        Assert.DoesNotContain(interactions, item =>
            item.Style.Opacity > 0d &&
            (item.Style.Fill is not null || item.Style.Stroke is not null));

        var corners = Canvas2DResizeGeometry.Directions
            .Where(Canvas2DResizeGeometry.IsCorner)
            .Select(direction => byRole[Canvas2DResizeGeometry.Role(direction)])
            .ToArray();
        var edges = Canvas2DResizeGeometry.Directions
            .Where(direction => !Canvas2DResizeGeometry.IsCorner(direction))
            .Select(direction => byRole[Canvas2DResizeGeometry.Role(direction)])
            .ToArray();
        Assert.True(corners.Min(item => item.ZIndex) > edges.Max(item => item.ZIndex));
    }

    private static Canvas2DSceneItem FindVisualContent(
        Canvas2D.Scene.Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Last(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);

    private static Canvas2DSceneItem FindConnector(
        Canvas2D.Scene.Canvas2DScene scene,
        string semanticRelationshipId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.SemanticElementId == new SemanticElementId(semanticRelationshipId) &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
            !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));

    private static VisualStateSnapshot FindVisual(
        DocumentSnapshot snapshot,
        string semanticElementId) =>
        snapshot.VisualModel.VisualStates.Single(visual =>
            visual.SemanticElementId == new SemanticElementId(semanticElementId));

    private static RectD Bounds(VisualStateSnapshot visual) =>
        new(visual.Position.X, visual.Position.Y, visual.Size.Width, visual.Size.Height);

    private static PointD Center(RectD bounds) =>
        new(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static void AssertBoundsEqual(RectD expected, RectD actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 10);
        Assert.Equal(expected.Y, actual.Y, precision: 10);
        Assert.Equal(expected.Width, actual.Width, precision: 10);
        Assert.Equal(expected.Height, actual.Height, precision: 10);
    }

    private static void AssertOppositeSidesAnchored(
        RectD original,
        RectD resized,
        Canvas2DResizeDirection direction)
    {
        if (direction is Canvas2DResizeDirection.NorthWest or
            Canvas2DResizeDirection.SouthWest or
            Canvas2DResizeDirection.West)
        {
            Assert.Equal(original.Right, resized.Right, precision: 10);
        }
        else
        {
            Assert.Equal(original.Left, resized.Left, precision: 10);
        }

        if (direction is Canvas2DResizeDirection.NorthWest or
            Canvas2DResizeDirection.North or
            Canvas2DResizeDirection.NorthEast)
        {
            Assert.Equal(original.Bottom, resized.Bottom, precision: 10);
        }
        else
        {
            Assert.Equal(original.Top, resized.Top, precision: 10);
        }
    }

    private static void AssertRouteEqual(
        ImmutableArray<PointD> expected,
        ImmutableArray<PointD> actual) =>
        Assert.True(expected.AsSpan().SequenceEqual(actual.AsSpan()));

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

    private sealed record ResizeIntegrationContext(
        EditingSession Session,
        Runtime.Documents.Document Document,
        NeutralDemoPipelineCounters Counters,
        RecordingRenderExecution Execution,
        RecordingSubscriber Events,
        CountingCommandValidator ResizeProbe);

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
