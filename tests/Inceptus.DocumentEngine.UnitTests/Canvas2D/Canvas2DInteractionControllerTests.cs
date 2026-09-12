using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DInteractionControllerTests
{
    [Fact]
    public async Task PointerMoveUpdatesOnlyHoverAndRunsOneSceneOnlyRebuild()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = VisualId(inputs, 0);
        var initial = RichEditorState(selected);
        var context = await AttachAsync(inputs, initial);
        context.Pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));
        await using var controller = new Canvas2DInteractionController(context.Session);

        var result = await controller.PointerMovedAsync(ToCss(initial.Viewport, new PointD(220d, 30d)));

        Assert.Equal(Canvas2DInteractionStatus.Updated, result.Status);
        Assert.True(result.Succeeded);
        Assert.True(result.StateChanged);
        Assert.Equal(NodeId(inputs, 1), result.TargetId);
        Assert.Equal(NodeId(inputs, 1), result.SessionState.EditorState.HoveredObjectId);
        Assert.Equal(selected, Assert.Single(result.SessionState.EditorState.Selection));
        AssertUnrelatedStatePreserved(initial, result.SessionState.EditorState);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(1, context.Pipeline.SceneRebuildCount);
        Assert.Equal(2, RenderCount(context.Execution));
        Assert.Same(
            context.InitialState.ProjectedGraph,
            result.SessionState.ProjectedGraph);
        Assert.Same(context.InitialState.LayoutResult, result.SessionState.LayoutResult);
        Assert.Same(context.InitialState.RoutingResult, result.SessionState.RoutingResult);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task UnchangedHoverDoesNotRebuildOrRender()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var target = NodeId(inputs, 1);
        var initial = new EditorStateSnapshot(hoveredObjectId: target);
        var context = await AttachAsync(inputs, initial);
        await using var controller = new Canvas2DInteractionController(context.Session);

        var result = await controller.PointerMovedAsync(new PointD(220d, 30d));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, result.Status);
        Assert.Equal(target, result.TargetId);
        Assert.Equal(0, context.Pipeline.SceneRebuildCount);
        Assert.Equal(1, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task HoverChangesTargetThenClearsOverEmptySpace()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var initial = new EditorStateSnapshot(hoveredObjectId: NodeId(inputs, 0));
        var context = await AttachAsync(inputs, initial);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);

        var changed = await controller.PointerMovedAsync(new PointD(220d, 30d));
        var cleared = await controller.PointerMovedAsync(new PointD(700d, 500d));

        Assert.Equal(Canvas2DInteractionStatus.Updated, changed.Status);
        Assert.Equal(NodeId(inputs, 1), changed.SessionState.EditorState.HoveredObjectId);
        Assert.Equal(Canvas2DInteractionStatus.Updated, cleared.Status);
        Assert.Null(cleared.SessionState.EditorState.HoveredObjectId);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        Assert.Equal(3, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ActivationReplacesThenClearsSingleSelection()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var initial = new EditorStateSnapshot(selection: [VisualId(inputs, 0)]);
        var context = await AttachAsync(inputs, initial);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);

        var selected = await controller.PointerActivatedAsync(new PointD(220d, 30d));
        var cleared = await controller.PointerActivatedAsync(new PointD(700d, 500d));

        Assert.Equal(Canvas2DInteractionStatus.Updated, selected.Status);
        Assert.Equal(NodeId(inputs, 1), selected.TargetId);
        Assert.Equal(
            VisualId(inputs, 1),
            Assert.Single(selected.SessionState.EditorState.Selection));
        Assert.Equal(Canvas2DInteractionStatus.Updated, cleared.Status);
        Assert.Null(cleared.TargetId);
        Assert.Empty(cleared.SessionState.EditorState.Selection);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        Assert.Equal(3, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ActivationOfTheExistingSingleSelectionIsUnchanged()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = VisualId(inputs, 1);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [selected]));
        await using var controller = new Canvas2DInteractionController(context.Session);

        var result = await controller.PointerActivatedAsync(new PointD(220d, 30d));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, result.Status);
        Assert.Equal(NodeId(inputs, 1), result.TargetId);
        Assert.Equal(selected, Assert.Single(result.SessionState.EditorState.Selection));
        Assert.Equal(0, context.Pipeline.SceneRebuildCount);
        Assert.Equal(1, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ContextMenuOnUnselectedObjectReplacesSelectionThroughOneSceneOnlyRebuild()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var initial = new EditorStateSnapshot(
            selection: [VisualId(inputs, 0)],
            hoveredObjectId: NodeId(inputs, 0),
            activeToolId: "test:pointer",
            focusTargetId: "test:focus",
            viewport: new ViewportSnapshot(1.25d, new VectorD(15d, -4d)),
            temporaryFeedback:
            [
                new EditorFeedbackSnapshot(
                    "test:feedback",
                    "guide",
                    new RectD(1d, 2d, 3d, 4d)),
            ],
            toolState:
            [
                new KeyValuePair<string, PropertyValue>(
                    "test:value",
                    PropertyValue.FromText("preserved")),
            ]);
        var context = await AttachAsync(inputs, initial);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);

        var result = await controller.PointerContextMenuAsync(
            ToCss(initial.Viewport, new PointD(220d, 30d)));

        Assert.Equal(Canvas2DInteractionStatus.Updated, result.Status);
        Assert.Equal(NodeId(inputs, 1), result.TargetId);
        Assert.Equal(VisualId(inputs, 1), result.TargetOrigin?.VisualStateId);
        AssertSelection(result.SessionState.EditorState, VisualId(inputs, 1));
        Assert.Equal(initial.HoveredObjectId, result.SessionState.EditorState.HoveredObjectId);
        AssertUnrelatedStatePreserved(initial, result.SessionState.EditorState);
        Assert.Equal(context.InitialState.DocumentRevision, result.SessionState.DocumentRevision);
        Assert.Equal(0, result.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(1, context.Pipeline.SceneRebuildCount);
        Assert.Equal(2, RenderCount(context.Execution));
        Assert.Same(context.InitialState.ProjectedGraph, result.SessionState.ProjectedGraph);
        Assert.Same(context.InitialState.LayoutResult, result.SessionState.LayoutResult);
        Assert.Same(context.InitialState.RoutingResult, result.SessionState.RoutingResult);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ContextMenuPreservesSelectedMultiSetAndEmptyCanvasPreservesSelection()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var initial = new EditorStateSnapshot(
            selection: [VisualId(inputs, 0), VisualId(inputs, 1)]);
        var context = await AttachAsync(inputs, initial);
        await using var controller = new Canvas2DInteractionController(context.Session);

        var selectedMember = await controller.PointerContextMenuAsync(new PointD(220d, 30d));
        var empty = await controller.PointerContextMenuAsync(new PointD(700d, 500d));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, selectedMember.Status);
        Assert.Equal(NodeId(inputs, 1), selectedMember.TargetId);
        Assert.Equal(VisualId(inputs, 1), selectedMember.TargetOrigin?.VisualStateId);
        AssertSelection(
            selectedMember.SessionState.EditorState,
            VisualId(inputs, 0),
            VisualId(inputs, 1));
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, empty.Status);
        Assert.Null(empty.HitResult);
        Assert.Null(empty.TargetId);
        AssertSelection(
            empty.SessionState.EditorState,
            VisualId(inputs, 0),
            VisualId(inputs, 1));
        Assert.Equal(0, context.Pipeline.SceneRebuildCount);
        Assert.Equal(1, RenderCount(context.Execution));
        Assert.Equal(0, empty.SessionState.HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ContextMenuRejectsWhilePersistentGestureIsActive()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var initial = RichEditorState(VisualId(inputs, 0));
        var context = await AttachAsync(inputs, initial);
        await using var controller = new Canvas2DInteractionController(context.Session);

        var result = await controller.PointerContextMenuAsync(new PointD(20d, 30d));

        Assert.Equal(Canvas2DInteractionStatus.Unavailable, result.Status);
        Assert.Null(result.TargetId);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code ==
                Canvas2DInteractionDiagnosticCodes.GestureAlreadyActive);
        Assert.Same(initial, result.SessionState.EditorState);
        Assert.Equal(0, context.Pipeline.SceneRebuildCount);
        Assert.Equal(1, RenderCount(context.Execution));
        Assert.Equal(0, result.SessionState.HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ContextMenuRejectsWhilePrimarySelectionPressIsPending()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var context = await AttachAsync(inputs, EditorStateSnapshot.Empty);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var pressed = await controller.PointerPressedAsync(new Canvas2DPointerInput(
            901,
            new PointD(20d, 30d),
            buttons: 1));

        var result = await controller.PointerContextMenuAsync(new PointD(220d, 30d));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, pressed.Status);
        Assert.Null(pressed.SessionState.EditorState.ActiveGesture);
        Assert.Equal(Canvas2DInteractionStatus.Unavailable, result.Status);
        Assert.Null(result.TargetId);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code ==
                Canvas2DInteractionDiagnosticCodes.GestureAlreadyActive);
        Assert.Empty(result.SessionState.EditorState.Selection);
        Assert.Equal(0, context.Pipeline.SceneRebuildCount);
        Assert.Equal(1, RenderCount(context.Execution));
        Assert.Equal(0, result.SessionState.HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ContextMenuNormalizesLabelAndResizeRegionsToTheirLogicalOwner()
    {
        var inputs = Canvas2DSceneTestData.CreateWithNodeLabels();
        var visualStateId = VisualId(inputs, 0);
        var initial = new EditorStateSnapshot(selection: [visualStateId]);
        var context = await AttachAsync(inputs, initial);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var scene = context.InitialState.CurrentScene!;
        var targetId = NodeId(inputs, 0);
        var label = scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == visualStateId);
        var west = FindResizeInteraction(scene, targetId, "west");
        var northWest = FindResizeInteraction(scene, targetId, "northwest");

        foreach (var item in new[] { label, west, northWest })
        {
            var result = await controller.PointerContextMenuAsync(new PointD(
                item.Bounds.Left + (item.Bounds.Width / 2d),
                item.Bounds.Top + (item.Bounds.Height / 2d)));

            Assert.Equal(Canvas2DInteractionStatus.Unchanged, result.Status);
            Assert.Equal(item.Id, result.TargetId);
            Assert.Equal(visualStateId, result.TargetOrigin?.VisualStateId);
            AssertSelection(result.SessionState.EditorState, visualStateId);
        }

        Assert.Equal(0, context.Pipeline.SceneRebuildCount);
        Assert.Equal(1, RenderCount(context.Execution));
        Assert.Equal(0, context.Session.CaptureState().HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task CardinalResizeEdgesResolveOrderedConnectorAnchorAddsButCornersDoNot()
    {
        var inputs = Canvas2DSceneTestData.CreateWithConnectorAnchors();
        var visualStateId = VisualId(inputs, 0);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]));
        await using var controller = new Canvas2DInteractionController(context.Session);
        var scene = context.InitialState.CurrentScene!;
        var targetId = NodeId(inputs, 0);

        var cases = new[]
        {
            (Role: "north", Side: ConnectorAnchorSide.Top, InsertionIndex: 0),
            (Role: "east", Side: ConnectorAnchorSide.Right, InsertionIndex: 1),
            (Role: "south", Side: ConnectorAnchorSide.Bottom, InsertionIndex: 0),
            (Role: "west", Side: ConnectorAnchorSide.Left, InsertionIndex: 0),
        };
        foreach (var testCase in cases)
        {
            var zone = FindResizeInteraction(scene, targetId, testCase.Role);
            var point = Center(zone.Bounds);

            var result = await controller.PointerContextMenuAsync(point);

            var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
                result.ConnectorAnchorContextAction);
            Assert.Equal(Canvas2DConnectorAnchorContextActionKind.AddAnchor, action.Kind);
            Assert.Equal(visualStateId, action.TargetVisualStateId);
            Assert.Equal(zone.Id, action.SourceSceneObjectId);
            Assert.Equal(targetId, action.TargetSceneObjectId);
            Assert.Equal(testCase.Side, action.Side);
            Assert.Equal(testCase.InsertionIndex, action.InsertionIndex);
            Assert.Equal(0.5d, action.EdgeParameter, 10);
            Assert.Null(action.AnchorId);
            Assert.Null(action.AnchorRole);
            Assert.False(action.CanDelete);
            Assert.Equal(zone.Id, result.TargetId);
            Assert.Equal(visualStateId, result.TargetOrigin?.VisualStateId);
            AssertSelection(result.SessionState.EditorState, visualStateId);
            var visual = inputs.VisualModel.VisualStates.Single(item =>
                item.Id == visualStateId);
            Assert.True(action.IsCurrent(scene, visual));
        }

        var corner = FindResizeInteraction(scene, targetId, "northwest");
        var cornerResult = await controller.PointerContextMenuAsync(Center(corner.Bounds));
        Assert.Null(cornerResult.ConnectorAnchorContextAction);
        Assert.Equal(corner.Id, cornerResult.TargetId);
        Assert.Equal(visualStateId, cornerResult.TargetOrigin?.VisualStateId);

        var target = scene.Items.Single(item => item.Id == targetId);
        var interiorResult = await controller.PointerContextMenuAsync(Center(target.Bounds));
        Assert.Null(interiorResult.ConnectorAnchorContextAction);
        Assert.Equal(targetId, interiorResult.TargetId);
        Assert.Equal(0, context.Pipeline.SceneRebuildCount);
        Assert.Equal(1, RenderCount(context.Execution));
        Assert.Equal(0, context.Session.CaptureState().HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task FixedSizeNodeBodyEdgesResolveConnectorAnchorAddsWithoutResizeInteractions()
    {
        var inputs = WithFixedSizeNode(Canvas2DSceneTestData.Create());
        var visualStateId = VisualId(inputs, 0);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]));
        await using var controller = new Canvas2DInteractionController(context.Session);
        var scene = context.InitialState.CurrentScene!;
        var targetId = NodeId(inputs, 0);
        var target = scene.Items.Single(item => item.Id == targetId);
        var visual = inputs.VisualModel.VisualStates.Single(item =>
            item.Id == visualStateId);

        Assert.True(Canvas2DNodeBodyMetadata.IsNodeBody(target));
        Assert.False(target.Metadata.ContainsKey(
            Canvas2DResizeGestureMetadata.ResizeCapable));
        Assert.DoesNotContain(scene.Items, item =>
            item.Origin.RelatedSceneObjectIds.Contains(targetId) &&
            item.Metadata.ContainsKey(Canvas2DResizeGestureMetadata.HandleRole));

        var cases = new[]
        {
            (
                Point: new PointD(
                    target.Bounds.Left + (target.Bounds.Width * 0.5d),
                    target.Bounds.Top),
                Side: ConnectorAnchorSide.Top,
                EdgeParameter: 0.5d),
            (
                Point: new PointD(
                    target.Bounds.Right,
                    target.Bounds.Top + (target.Bounds.Height * 0.25d)),
                Side: ConnectorAnchorSide.Right,
                EdgeParameter: 0.25d),
            (
                Point: new PointD(
                    target.Bounds.Left + (target.Bounds.Width * 0.5d),
                    target.Bounds.Bottom),
                Side: ConnectorAnchorSide.Bottom,
                EdgeParameter: 0.5d),
            (
                Point: new PointD(
                    target.Bounds.Left,
                    target.Bounds.Top + (target.Bounds.Height * 0.75d)),
                Side: ConnectorAnchorSide.Left,
                EdgeParameter: 0.75d),
        };
        Canvas2DConnectorAnchorContextAction? canonicalAction = null;
        foreach (var testCase in cases)
        {
            var result = await controller.PointerContextMenuAsync(testCase.Point);

            var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
                result.ConnectorAnchorContextAction);
            canonicalAction ??= action;
            Assert.Equal(Canvas2DConnectorAnchorContextActionKind.AddAnchor, action.Kind);
            Assert.Equal(targetId, action.SourceSceneObjectId);
            Assert.Equal(targetId, action.TargetSceneObjectId);
            Assert.Equal(testCase.Side, action.Side);
            Assert.Equal(0, action.InsertionIndex);
            Assert.Equal(testCase.EdgeParameter, action.EdgeParameter, 10);
            Assert.Equal(
                ConnectorAnchorRoleCapability.SourceOrTarget,
                action.AllowedRoles);
            Assert.True(action.IsCurrent(scene, visual));
            Assert.Equal(targetId, result.TargetId);
        }

        var insideEdgeBand = await controller.PointerContextMenuAsync(new PointD(
            target.Bounds.Left + (target.Bounds.Width * 0.5d),
            target.Bounds.Top +
            (Canvas2DResizeGestureMetadata.EdgeHitExtent / 2d) - 0.1d));
        var beyondEdgeBand = await controller.PointerContextMenuAsync(new PointD(
            target.Bounds.Left + (target.Bounds.Width * 0.5d),
            target.Bounds.Top +
            (Canvas2DResizeGestureMetadata.EdgeHitExtent / 2d) + 0.1d));
        var corner = await controller.PointerContextMenuAsync(target.Bounds.TopLeft);
        var interior = await controller.PointerContextMenuAsync(Center(target.Bounds));
        Assert.NotNull(insideEdgeBand.ConnectorAnchorContextAction);
        Assert.Null(beyondEdgeBand.ConnectorAnchorContextAction);
        Assert.Null(corner.ConnectorAnchorContextAction);
        Assert.Null(interior.ConnectorAnchorContextAction);
        Assert.NotNull(canonicalAction);

        using var resizableScene = ReplaceSceneItem(
            scene,
            CopySceneItem(
                target,
                target.Bounds,
                WithMetadata(
                    target,
                    Canvas2DResizeGestureMetadata.ResizeCapable,
                    PropertyValue.FromBoolean(true))));
        using var malformedScene = ReplaceSceneItem(
            scene,
            CopySceneItem(
                target,
                target.Bounds,
                WithMetadata(
                    target,
                    Canvas2DResizeGestureMetadata.ResizeCapable,
                    PropertyValue.FromText("malformed"))));
        using var degenerateScene = ReplaceSceneItem(
            scene,
            CopySceneItem(
                target,
                new RectD(target.Bounds.X, target.Bounds.Y, 0d, target.Bounds.Height),
                target.Metadata));
        Assert.False(canonicalAction.IsCurrent(resizableScene, visual));
        Assert.False(canonicalAction.IsCurrent(malformedScene, visual));
        Assert.False(canonicalAction.IsCurrent(degenerateScene, visual));
        Assert.Equal(targetId, corner.TargetId);
        Assert.Equal(targetId, interior.TargetId);
        Assert.Equal(0, context.Pipeline.SceneRebuildCount);
        Assert.Equal(1, RenderCount(context.Execution));
        Assert.Equal(0, context.Session.CaptureState().HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task FixedSizeBodyFallbackRejectsDifferentSelectionAndSameVisualLabel()
    {
        var inputs = WithFixedSizeNode(Canvas2DSceneTestData.CreateWithNodeLabels());
        var visualStateId = VisualId(inputs, 0);
        var otherVisualStateId = VisualId(inputs, 1);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [otherVisualStateId]));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var target = context.InitialState.CurrentScene!.Items.Single(item =>
            item.Id == NodeId(inputs, 0));

        var differentSelection = await controller.PointerContextMenuAsync(new PointD(
            target.Bounds.Right,
            target.Bounds.Top + (target.Bounds.Height * 0.25d)));

        Assert.Null(differentSelection.ConnectorAnchorContextAction);
        AssertSelection(differentSelection.SessionState.EditorState, visualStateId);
        var label = differentSelection.SessionState.CurrentScene!.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == visualStateId);

        var labelContext = await controller.PointerContextMenuAsync(Center(label.Bounds));

        Assert.Null(labelContext.ConnectorAnchorContextAction);
        Assert.Equal(label.Id, labelContext.TargetId);
        AssertSelection(labelContext.SessionState.EditorState, visualStateId);
        Assert.Equal(1, context.Pipeline.SceneRebuildCount);
        Assert.Equal(0, context.Session.CaptureState().HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task FixedSizeNodeBodyEdgeContextRequiresOneSelectionAndHonorsPolicyRoles()
    {
        var inputs = WithFixedSizeNode(Canvas2DSceneTestData.Create());
        var visualStateId = VisualId(inputs, 0);
        var otherVisualStateId = VisualId(inputs, 1);
        var policy = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.DynamicUnlimited(
                ConnectorAnchorRoleCapability.Source),
            EdgeConnectorAnchorPolicy.Predefined(
            [
                new PredefinedConnectorAnchorDefinition(
                    new PredefinedConnectorAnchorDefinitionId("test:fixed:bottom"),
                    ConnectorAnchorSide.Bottom,
                    ConnectorAnchorRoleCapability.SourceOrTarget,
                    0),
            ]),
            EdgeConnectorAnchorPolicy.DynamicSingle(
                ConnectorAnchorRoleCapability.Target));
        var multi = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId, otherVisualStateId]),
            PolicyProvider(policy));
        await using (var controller = new Canvas2DInteractionController(multi.Session))
        {
            var target = multi.InitialState.CurrentScene!.Items.Single(item =>
                item.Id == NodeId(inputs, 0));
            var result = await controller.PointerContextMenuAsync(new PointD(
                target.Bounds.Right,
                target.Bounds.Top + (target.Bounds.Height * 0.25d)));

            Assert.Null(result.ConnectorAnchorContextAction);
            AssertSelection(
                result.SessionState.EditorState,
                visualStateId,
                otherVisualStateId);
        }

        await multi.Session.DisposeAsync();

        var single = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]),
            PolicyProvider(policy));
        await using (var controller = new Canvas2DInteractionController(single.Session))
        {
            var target = single.InitialState.CurrentScene!.Items.Single(item =>
                item.Id == NodeId(inputs, 0));
            var top = await controller.PointerContextMenuAsync(new PointD(
                target.Bounds.Left + (target.Bounds.Width * 0.5d),
                target.Bounds.Top));
            var right = await controller.PointerContextMenuAsync(new PointD(
                target.Bounds.Right,
                target.Bounds.Top + (target.Bounds.Height * 0.25d)));
            var bottom = await controller.PointerContextMenuAsync(new PointD(
                target.Bounds.Left + (target.Bounds.Width * 0.5d),
                target.Bounds.Bottom));
            var left = await controller.PointerContextMenuAsync(new PointD(
                target.Bounds.Left,
                target.Bounds.Top + (target.Bounds.Height * 0.25d)));

            Assert.Null(top.ConnectorAnchorContextAction);
            var rightAction = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
                right.ConnectorAnchorContextAction);
            Assert.Equal(ConnectorAnchorRoleCapability.Source, rightAction.AllowedRoles);
            Assert.True(rightAction.CanAdd(ConnectorAnchorRole.Source));
            Assert.False(rightAction.CanAdd(ConnectorAnchorRole.Target));
            Assert.Null(bottom.ConnectorAnchorContextAction);
            var leftAction = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
                left.ConnectorAnchorContextAction);
            Assert.Equal(ConnectorAnchorRoleCapability.Target, leftAction.AllowedRoles);
            Assert.False(leftAction.CanAdd(ConnectorAnchorRole.Source));
            Assert.True(leftAction.CanAdd(ConnectorAnchorRole.Target));
            Assert.Equal(0, single.Session.CaptureState().HistoryStatus.EntryCount);
        }

        await single.Session.DisposeAsync();
    }

    [Fact]
    public async Task FixedSizeNodeAnchorHandleKeepsDeletePrecedenceOverBodyFallback()
    {
        var inputs = WithFixedSizeNode(
            Canvas2DSceneTestData.CreateWithConnectorAnchors());
        var visualStateId = VisualId(inputs, 0);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]));
        await using var controller = new Canvas2DInteractionController(context.Session);
        var scene = context.InitialState.CurrentScene!;
        var targetId = NodeId(inputs, 0);
        var handle = scene.Items.Single(item =>
            item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorId, out var id) &&
            id.Kind == PropertyValueKind.Text &&
            StringComparer.Ordinal.Equals(id.TextValue, "test:anchor:a:source"));

        var result = await controller.PointerContextMenuAsync(Center(handle.Bounds));

        var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            result.ConnectorAnchorContextAction);
        Assert.Equal(Canvas2DConnectorAnchorContextActionKind.DeleteAnchor, action.Kind);
        Assert.Equal(handle.Id, action.SourceSceneObjectId);
        Assert.Equal(targetId, action.TargetSceneObjectId);
        Assert.NotEqual(action.SourceSceneObjectId, action.TargetSceneObjectId);
        Assert.True(action.CanDelete);
        Assert.True(action.IsCurrent(
            scene,
            inputs.VisualModel.VisualStates.Single(item => item.Id == visualStateId)));
        Assert.Equal(0, context.Session.CaptureState().HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task FixedSizeNodeBodyEdgeHoverRemainsTransientAndUsesMoveCursor()
    {
        var inputs = WithFixedSizeNode(Canvas2DSceneTestData.Create());
        var visualStateId = VisualId(inputs, 0);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var before = context.Document.CaptureSnapshot();
        var target = context.InitialState.CurrentScene!.Items.Single(item =>
            item.Id == NodeId(inputs, 0));
        var point = new PointD(
            target.Bounds.Left + (target.Bounds.Width * 0.5d),
            target.Bounds.Top);

        var result = await controller.PointerMovedAsync(point);

        var after = context.Document.CaptureSnapshot();
        Assert.Equal(Canvas2DInteractionStatus.Updated, result.Status);
        Assert.Equal(target.Id, result.TargetId);
        Assert.Equal(target.Id, result.SessionState.EditorState.HoveredObjectId);
        Assert.Equal("grab", result.CssCursor);
        Assert.Null(result.SessionState.EditorState.ActiveGesture);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Empty(after.VisualModel.VisualStates.Single(item =>
            item.Id == visualStateId).ConnectorAnchors);
        Assert.Equal(0, result.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, context.Pipeline.SceneRebuildCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task EdgeContextActionsHonorIndependentModesAndRoleCapabilities()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var visualStateId = VisualId(inputs, 0);
        var policy = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.DynamicUnlimited(
                ConnectorAnchorRoleCapability.Source),
            EdgeConnectorAnchorPolicy.Predefined(
            [
                new PredefinedConnectorAnchorDefinition(
                    new PredefinedConnectorAnchorDefinitionId("test:bottom:predefined"),
                    ConnectorAnchorSide.Bottom,
                    ConnectorAnchorRoleCapability.SourceOrTarget,
                    0),
            ]),
            EdgeConnectorAnchorPolicy.DynamicSingle(
                ConnectorAnchorRoleCapability.Target));
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]),
            PolicyProvider(policy));
        await using var controller = new Canvas2DInteractionController(context.Session);
        var scene = context.InitialState.CurrentScene!;
        var targetId = NodeId(inputs, 0);

        var top = await controller.PointerContextMenuAsync(Center(
            FindResizeInteraction(scene, targetId, "north").Bounds));
        var right = await controller.PointerContextMenuAsync(Center(
            FindResizeInteraction(scene, targetId, "east").Bounds));
        var bottom = await controller.PointerContextMenuAsync(Center(
            FindResizeInteraction(scene, targetId, "south").Bounds));
        var left = await controller.PointerContextMenuAsync(Center(
            FindResizeInteraction(scene, targetId, "west").Bounds));

        Assert.Null(top.ConnectorAnchorContextAction);
        var rightAction = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            right.ConnectorAnchorContextAction);
        Assert.Equal(ConnectorAnchorRoleCapability.Source, rightAction.AllowedRoles);
        Assert.True(rightAction.CanAdd(ConnectorAnchorRole.Source));
        Assert.False(rightAction.CanAdd(ConnectorAnchorRole.Target));
        Assert.Null(bottom.ConnectorAnchorContextAction);
        var leftAction = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            left.ConnectorAnchorContextAction);
        Assert.Equal(ConnectorAnchorRoleCapability.Target, leftAction.AllowedRoles);
        Assert.False(leftAction.CanAdd(ConnectorAnchorRole.Source));
        Assert.True(leftAction.CanAdd(ConnectorAnchorRole.Target));
        Assert.Equal(0, context.Session.CaptureState().HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task OccupiedDynamicSingleEdgeOffersNoAddButUnusedAnchorRemainsDeletable()
    {
        var inputs = WithSinglePersistentConnectorAnchor(
            Canvas2DSceneTestData.CreateWithConnectorAnchors());
        var visualStateId = VisualId(inputs, 0);
        var policy = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.DynamicUnlimited(),
            EdgeConnectorAnchorPolicy.DynamicSingle(),
            EdgeConnectorAnchorPolicy.DynamicUnlimited(),
            EdgeConnectorAnchorPolicy.DynamicUnlimited());
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]),
            PolicyProvider(policy));
        await using var controller = new Canvas2DInteractionController(context.Session);
        var scene = context.InitialState.CurrentScene!;
        var targetId = NodeId(inputs, 0);

        var edgeResult = await controller.PointerContextMenuAsync(Center(
            FindResizeInteraction(scene, targetId, "east").Bounds));
        var sourceHandle = scene.Items.Single(item =>
            item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorId, out var id) &&
            id.Kind == PropertyValueKind.Text &&
            StringComparer.Ordinal.Equals(id.TextValue, "test:anchor:a:source"));
        var anchorResult = await controller.PointerContextMenuAsync(Center(sourceHandle.Bounds));

        Assert.Null(edgeResult.ConnectorAnchorContextAction);
        var deleteAction = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            anchorResult.ConnectorAnchorContextAction);
        Assert.Equal(Canvas2DConnectorAnchorContextActionKind.DeleteAnchor, deleteAction.Kind);
        Assert.True(deleteAction.CanDelete);
        Assert.Equal(ConnectorAnchorRoleCapability.None, deleteAction.AllowedRoles);
        Assert.Equal(0, context.Session.CaptureState().HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ConnectorAnchorHandlesNormalizeToNodeAndCannotStartNodeMove()
    {
        var inputs = Canvas2DSceneTestData.CreateWithConnectorAnchors();
        var visualStateId = VisualId(inputs, 0);
        var targetId = NodeId(inputs, 0);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]));
        await using var controller = new Canvas2DInteractionController(context.Session);
        var scene = context.InitialState.CurrentScene!;
        var handles = scene.Items.Where(item =>
            item.Metadata.TryGetValue(
                Canvas2DConnectorAnchorMetadata.AnchorId,
                out var value) &&
            value.Kind == PropertyValueKind.Text).ToArray();

        Assert.Equal(2, handles.Length);
        foreach (var handle in handles)
        {
            var point = Center(handle.Bounds);
            var contextMenu = await controller.PointerContextMenuAsync(point);
            var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
                contextMenu.ConnectorAnchorContextAction);
            Assert.Equal(Canvas2DConnectorAnchorContextActionKind.DeleteAnchor, action.Kind);
            Assert.Equal(handle.Id, action.SourceSceneObjectId);
            Assert.Equal(targetId, action.TargetSceneObjectId);
            Assert.Equal(visualStateId, action.TargetVisualStateId);
            Assert.Equal(handle.Id, contextMenu.TargetId);
            Assert.Equal(visualStateId, contextMenu.TargetOrigin?.VisualStateId);
            Assert.True(action.CanDelete);
            AssertSelection(contextMenu.SessionState.EditorState, visualStateId);

            var pointerId = action.AnchorRole == ConnectorAnchorRole.Source ? 911L : 912L;
            var pressed = await controller.PointerPressedAsync(new Canvas2DPointerInput(
                pointerId,
                point,
                buttons: 1));
            var moved = await controller.PointerMovedAsync(new Canvas2DPointerInput(
                pointerId,
                point + new VectorD(30d, 20d),
                buttons: 1));
            var cancelled = await controller.PointerCancelledAsync(pointerId);

            Assert.Equal(Canvas2DInteractionStatus.Unchanged, pressed.Status);
            Assert.Equal("pointer", pressed.CssCursor);
            Assert.Equal(Canvas2DInteractionStatus.Unchanged, moved.Status);
            Assert.Equal("pointer", moved.CssCursor);
            Assert.Equal(Canvas2DInteractionStatus.Unchanged, cancelled.Status);
            Assert.Equal(context.InitialState.DocumentRevision, cancelled.SessionState.DocumentRevision);
            Assert.Equal(0, cancelled.SessionState.HistoryStatus.EntryCount);
            Assert.Null(cancelled.SessionState.EditorState.ActiveGesture);
            AssertSelection(cancelled.SessionState.EditorState, visualStateId);
        }

        Assert.Equal(0, context.Pipeline.SceneRebuildCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(1, RenderCount(context.Execution));
        Assert.True(context.Session.TryCaptureDocumentSnapshot(out var document));
        Assert.True(inputs.VisualModel.VisualStates.Single(item => item.Id == visualStateId)
            .ConnectorAnchors.SequenceEqual(
                document!.VisualModel.VisualStates
                    .Single(item => item.Id == visualStateId).ConnectorAnchors));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task PredefinedConnectorAnchorNormalizesToNodeWithoutMutationOrDrag()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPredefinedConnectorAnchor();
        var visualStateId = VisualId(inputs, 0);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]));
        await using var controller = new Canvas2DInteractionController(context.Session);
        var handle = context.InitialState.CurrentScene!.Items.Single(item =>
            item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorKind, out var kind) &&
            kind.Kind == PropertyValueKind.Integer &&
            kind.IntegerValue == (long)ResolvedConnectorAnchorKind.Predefined);
        var point = Center(handle.Bounds);

        var contextMenu = await controller.PointerContextMenuAsync(point);
        var pressed = await controller.PointerPressedAsync(new Canvas2DPointerInput(
            913L,
            point,
            buttons: 1));
        var moved = await controller.PointerMovedAsync(new Canvas2DPointerInput(
            913L,
            point + new VectorD(30d, 20d),
            buttons: 1));
        var cancelled = await controller.PointerCancelledAsync(913L);

        Assert.Equal(handle.Id, contextMenu.TargetId);
        Assert.Equal(visualStateId, contextMenu.TargetOrigin?.VisualStateId);
        Assert.Null(contextMenu.ConnectorAnchorContextAction);
        AssertSelection(contextMenu.SessionState.EditorState, visualStateId);
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, pressed.Status);
        Assert.Equal("pointer", pressed.CssCursor);
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, moved.Status);
        Assert.Equal("pointer", moved.CssCursor);
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, cancelled.Status);
        Assert.Null(cancelled.SessionState.EditorState.ActiveGesture);
        Assert.Equal(context.InitialState.DocumentRevision, cancelled.SessionState.DocumentRevision);
        Assert.Equal(0, cancelled.SessionState.HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ReferencedConnectorAnchorNormalizesButDoesNotOfferDelete()
    {
        var inputs = Canvas2DSceneTestData.CreateWithConnectorAnchors(
            referenceSourceAnchor: true);
        var visualStateId = VisualId(inputs, 0);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]));
        await using var controller = new Canvas2DInteractionController(context.Session);
        var sourceHandle = context.InitialState.CurrentScene!.Items.Single(item =>
            item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorId, out var id) &&
            id.Kind == PropertyValueKind.Text &&
            StringComparer.Ordinal.Equals(id.TextValue, "test:anchor:a:source"));

        var result = await controller.PointerContextMenuAsync(Center(sourceHandle.Bounds));

        var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            result.ConnectorAnchorContextAction);
        Assert.Equal(Canvas2DConnectorAnchorContextActionKind.DeleteAnchor, action.Kind);
        Assert.False(action.CanDelete);
        Assert.Equal(sourceHandle.Id, result.TargetId);
        Assert.Equal(visualStateId, result.TargetOrigin?.VisualStateId);
        AssertSelection(result.SessionState.EditorState, visualStateId);
        Assert.Equal(0, context.Session.CaptureState().HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ContextMenuOnConnectorReturnsItsCanonicalPersistentIdentity()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var visualStateId = ConnectorVisualId(inputs);
        var initial = new EditorStateSnapshot(selection: [visualStateId]);
        var context = await AttachAsync(inputs, initial);
        await using var controller = new Canvas2DInteractionController(context.Session);

        var result = await controller.PointerContextMenuAsync(new PointD(135d, 45d));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, result.Status);
        Assert.Equal(ConnectorId(inputs), result.TargetId);
        Assert.Equal(visualStateId, result.TargetOrigin?.VisualStateId);
        AssertSelection(result.SessionState.EditorState, visualStateId);
        Assert.Equal(0, context.Pipeline.SceneRebuildCount);
        Assert.Equal(1, RenderCount(context.Execution));
        Assert.Equal(0, result.SessionState.HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task TargetArrowNormalizesToConnectorWithoutOfferingRoutePointInsertion()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var visualStateId = ConnectorVisualId(inputs);
        var context = await AttachAsync(inputs, EditorStateSnapshot.Empty);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var arrow = context.InitialState.CurrentScene!.Items.Single(item =>
            item.Metadata.TryGetValue(Canvas2DConnectorArrowMetadata.TargetArrow, out var value) &&
            value.Kind == PropertyValueKind.Boolean &&
            value.BooleanValue);

        var result = await controller.PointerContextMenuAsync(Center(arrow.Bounds));

        Assert.Equal(Canvas2DInteractionStatus.Updated, result.Status);
        Assert.Equal(arrow.Id, result.TargetId);
        Assert.Equal(visualStateId, result.TargetOrigin?.VisualStateId);
        Assert.Null(result.ConnectorRouteContextAction);
        Assert.Null(result.ConnectorAnchorContextAction);
        AssertSelection(result.SessionState.EditorState, visualStateId);
        Assert.Equal(1, context.Pipeline.SceneRebuildCount);
        Assert.Equal(2, RenderCount(context.Execution));
        Assert.Equal(0, result.SessionState.HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ConnectorPathContextResolvesProjectedAddPointAndOrderedImmutableRoute()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var visualStateId = ConnectorVisualId(inputs);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]));
        await using var controller = new Canvas2DInteractionController(context.Session);

        var result = await controller.PointerContextMenuAsync(new PointD(181d, 48d));

        var action = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            result.ConnectorRouteContextAction);
        Assert.Equal(Canvas2DConnectorRouteContextActionKind.AddPoint, action.Kind);
        Assert.Equal(visualStateId, action.TargetVisualStateId);
        Assert.Equal(1, action.RouteIndex);
        Assert.Equal(new PointD(181d, 45d), action.RoutePoint);
        Assert.True(context.Session.TryCaptureDocumentSnapshot(out var document));
        var visual = document!.VisualModel.VisualStates.Single(item => item.Id == visualStateId);
        Assert.True(action.TryResolveTargetRoute(
            result.SessionState.CurrentScene!,
            visual.Route,
            out var targetRoute));
        Assert.True(
            new PointD[]
            {
                new PointD(110d, 45d),
                new PointD(160d, 45d),
                new PointD(181d, 45d),
                new PointD(210d, 45d),
            }.AsSpan().SequenceEqual(targetRoute.AsSpan()));
        Assert.Equal(visual.Route, document.VisualModel.VisualStates
            .Single(item => item.Id == visualStateId).Route);
        Assert.Equal(0, result.SessionState.HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task AutomaticLogicalBendCanBecomeFirstSparseManualWaypoint()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var visualStateId = ConnectorVisualId(inputs);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]));
        await using var controller = new Canvas2DInteractionController(context.Session);
        var automaticBend = new PointD(160d, 45d);

        var result = await controller.PointerContextMenuAsync(automaticBend);

        var action = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            result.ConnectorRouteContextAction);
        Assert.Equal(Canvas2DConnectorRouteContextActionKind.AddPoint, action.Kind);
        Assert.Equal(automaticBend, action.RoutePoint);
        Assert.True(context.Session.TryCaptureDocumentSnapshot(out var document));
        var visual = document!.VisualModel.VisualStates.Single(item => item.Id == visualStateId);
        Assert.Empty(visual.Route);
        Assert.True(action.TryResolveTargetRoute(
            result.SessionState.CurrentScene!,
            visual.Route,
            out var targetRoute));
        Assert.Equal(
            [new PointD(110d, 45d), automaticBend, new PointD(210d, 45d)],
            targetRoute.AsEnumerable());
        Assert.Empty(document.VisualModel.VisualStates.Single(item =>
            item.Id == visualStateId).Route);
        Assert.Equal(0, result.SessionState.HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task BridgedAutomaticDisplayHitProjectsFirstSparseAddOntoLogicalCenterline()
    {
        var inputs = Canvas2DSceneTestData.Create()
            .WithCrossingConnector();
        var edge = inputs.Graph.Edges.Single(candidate =>
            candidate.Source.SemanticElementId == new SemanticElementId("test:semantic:ab"));
        var visualStateId = edge.Source.VisualStateId!;
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]));
        await using var controller = new Canvas2DInteractionController(context.Session);
        var connector = context.InitialState.CurrentScene!.Items.Single(item =>
            item.Id == connectorId);
        var bridgePeak = Assert.Single(connector.Geometry.Points, point =>
            point == new PointD(180d, 49d));

        var result = await controller.PointerContextMenuAsync(bridgePeak);

        var action = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            result.ConnectorRouteContextAction);
        Assert.Equal(connectorId, action.SourceSceneObjectId);
        Assert.Equal(1, action.RouteIndex);
        Assert.Equal(new PointD(180d, 45d), action.RoutePoint);
        Assert.True(context.Session.TryCaptureDocumentSnapshot(out var document));
        var persistentRoute = document!.VisualModel.VisualStates.Single(visual =>
            visual.Id == visualStateId).Route;
        Assert.True(action.TryResolveTargetRoute(
            result.SessionState.CurrentScene!,
            persistentRoute,
            out var targetRoute));
        Assert.Equal(
            [
                new PointD(110d, 45d),
                new PointD(180d, 45d),
                new PointD(210d, 45d),
            ],
            targetRoute.AsEnumerable());
        Assert.DoesNotContain(new PointD(160d, 45d), targetRoute);
        Assert.DoesNotContain(bridgePeak, targetRoute);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task BendContextResolvesDeleteWhileEndpointsAndNearRoutePointsOfferNoAction()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var visualStateId = ConnectorVisualId(inputs);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]));
        await using var controller = new Canvas2DInteractionController(context.Session);
        var scene = context.InitialState.CurrentScene!;
        var bend = scene.Items.Single(item =>
            item.Metadata.TryGetValue(Canvas2DRouteGestureMetadata.HandleRole, out var role) &&
            role.Kind == PropertyValueKind.Text &&
            StringComparer.Ordinal.Equals(role.TextValue, Canvas2DRouteGestureMetadata.BendRole));
        var endpoints = scene.Items.Where(item =>
            item.Metadata.ContainsKey(Canvas2DConnectorEndpointMetadata.HandleRole)).ToArray();

        var deleteResult = await controller.PointerContextMenuAsync(new PointD(
            bend.Bounds.X + (bend.Bounds.Width / 2d),
            bend.Bounds.Y + (bend.Bounds.Height / 2d)));
        var delete = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            deleteResult.ConnectorRouteContextAction);
        Assert.Equal(Canvas2DConnectorRouteContextActionKind.DeletePoint, delete.Kind);
        Assert.Equal(1, delete.RouteIndex);
        Assert.Equal(new PointD(160d, 45d), delete.RoutePoint);
        Assert.True(context.Session.TryCaptureDocumentSnapshot(out var document));
        var route = document!.VisualModel.VisualStates.Single(item => item.Id == visualStateId).Route;
        Assert.True(delete.TryResolveTargetRoute(scene, route, out var deletedRoute));
        Assert.Empty(deletedRoute);

        foreach (var endpoint in endpoints)
        {
            var endpointResult = await controller.PointerContextMenuAsync(new PointD(
                endpoint.Bounds.X + (endpoint.Bounds.Width / 2d),
                endpoint.Bounds.Y + (endpoint.Bounds.Height / 2d)));
            Assert.Null(endpointResult.ConnectorRouteContextAction);
        }

        var nearExisting = await controller.PointerContextMenuAsync(new PointD(114d, 45d));
        Assert.Null(nearExisting.ConnectorRouteContextAction);
        Assert.Equal(context.InitialState.DocumentRevision, nearExisting.SessionState.DocumentRevision);
        Assert.Equal(0, nearExisting.SessionState.HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public void ConnectorRouteContextActionInsertsIntoFirstMiddleAndLastSegments()
    {
        PointD[] route =
        [
            new(0d, 0d),
            new(40d, 0d),
            new(40d, 40d),
            new(80d, 40d),
            new(80d, 80d),
        ];
        var (scene, connectorId, visualStateId) = RouteContextScene(route);
        (int SegmentIndex, PointD Point)[] cases =
        [
            (0, new PointD(20d, 0d)),
            (2, new PointD(60d, 40d)),
            (3, new PointD(80d, 60d)),
        ];

        foreach (var (segmentIndex, point) in cases)
        {
            var action = new Canvas2DConnectorRouteContextAction(
                Canvas2DConnectorRouteContextActionKind.AddPoint,
                visualStateId,
                connectorId,
                segmentIndex,
                point,
                point);

            Assert.True(action.TryResolveTargetRoute(scene, route, out var targetRoute));
            var expected = route
                .Take(segmentIndex + 1)
                .Append(point)
                .Concat(route.Skip(segmentIndex + 1));
            Assert.Equal(expected, targetRoute);
        }

        var staleRoute = route.ToArray();
        staleRoute[2] = new PointD(41d, 40d);
        var staleAction = new Canvas2DConnectorRouteContextAction(
            Canvas2DConnectorRouteContextActionKind.AddPoint,
            visualStateId,
            connectorId,
            routeIndex: 2,
            documentPoint: new PointD(60d, 40d),
            routePoint: new PointD(60d, 40d));
        Assert.False(staleAction.TryResolveTargetRoute(scene, staleRoute, out _));

        var outsideDocument = new Canvas2DConnectorRouteContextAction(
            Canvas2DConnectorRouteContextActionKind.AddPoint,
            visualStateId,
            connectorId,
            routeIndex: 0,
            documentPoint: new PointD(-1d, 0d),
            routePoint: new PointD(20d, 0d));
        Assert.False(outsideDocument.TryResolveTargetRoute(scene, route, out _));
    }

    [Fact]
    public void SparseRouteAddsStayOrderedAndNeverSnapshotGeneratedBends()
    {
        PointD[] firstLogicalRoute =
        [
            new(0d, 0d),
            new(30d, 0d),
            new(30d, 30d),
            new(60d, 30d),
            new(60d, 60d),
            new(100d, 60d),
        ];
        var firstWaypoint = firstLogicalRoute[2];
        var (firstScene, connectorId, visualStateId) = RouteContextScene(firstLogicalRoute);
        var firstAdd = new Canvas2DConnectorRouteContextAction(
            Canvas2DConnectorRouteContextActionKind.AddPoint,
            visualStateId,
            connectorId,
            routeIndex: 1,
            documentPoint: firstWaypoint,
            routePoint: firstWaypoint);

        Assert.True(firstAdd.TryResolveTargetRoute(firstScene, [], out var firstRoute));
        Assert.Equal(
            [firstLogicalRoute[0], firstWaypoint, firstLogicalRoute[^1]],
            firstRoute.AsEnumerable());
        Assert.DoesNotContain(firstLogicalRoute[1], firstRoute);
        Assert.DoesNotContain(firstLogicalRoute[3], firstRoute);
        Assert.DoesNotContain(firstLogicalRoute[4], firstRoute);

        var secondWaypoint = firstLogicalRoute[4];
        var (secondScene, _, _) = RouteContextScene(
            firstLogicalRoute,
            firstLogicalRoute,
            firstRoute);
        var secondAdd = new Canvas2DConnectorRouteContextAction(
            Canvas2DConnectorRouteContextActionKind.AddPoint,
            visualStateId,
            connectorId,
            routeIndex: 3,
            documentPoint: secondWaypoint,
            routePoint: secondWaypoint);

        Assert.True(secondAdd.TryResolveTargetRoute(
            secondScene,
            firstRoute,
            out var secondRoute));
        Assert.Equal(
            [firstLogicalRoute[0], firstWaypoint, secondWaypoint, firstLogicalRoute[^1]],
            secondRoute.AsEnumerable());
        Assert.DoesNotContain(firstLogicalRoute[1], secondRoute);
        Assert.DoesNotContain(firstLogicalRoute[3], secondRoute);

        var (deleteScene, _, _) = RouteContextScene(
            firstLogicalRoute,
            firstLogicalRoute,
            secondRoute);
        var deleteFirst = new Canvas2DConnectorRouteContextAction(
            Canvas2DConnectorRouteContextActionKind.DeletePoint,
            visualStateId,
            connectorId,
            routeIndex: 1,
            documentPoint: firstWaypoint,
            routePoint: firstWaypoint);
        Assert.True(deleteFirst.TryResolveTargetRoute(
            deleteScene,
            secondRoute,
            out var oneWaypointRoute));
        Assert.Equal(
            [firstLogicalRoute[0], secondWaypoint, firstLogicalRoute[^1]],
            oneWaypointRoute.AsEnumerable());

        var (finalDeleteScene, _, _) = RouteContextScene(
            firstLogicalRoute,
            firstLogicalRoute,
            oneWaypointRoute);
        var deleteLast = new Canvas2DConnectorRouteContextAction(
            Canvas2DConnectorRouteContextActionKind.DeletePoint,
            visualStateId,
            connectorId,
            routeIndex: 1,
            documentPoint: secondWaypoint,
            routePoint: secondWaypoint);
        Assert.True(deleteLast.TryResolveTargetRoute(
            finalDeleteScene,
            oneWaypointRoute,
            out var automaticRoute));
        Assert.Empty(automaticRoute);
        Assert.Equal(
            [firstLogicalRoute[0], firstWaypoint, firstLogicalRoute[^1]],
            firstRoute.AsEnumerable());
        Assert.Equal(
            [firstLogicalRoute[0], firstWaypoint, secondWaypoint, firstLogicalRoute[^1]],
            secondRoute.AsEnumerable());
    }

    [Fact]
    public void BridgedConnectorRouteActionsUseOnlyLogicalAndEditablePaths()
    {
        PointD[] logicalRoute =
        [
            new(0d, 10d),
            new(30d, 10d),
            new(30d, 40d),
            new(50d, 40d),
            new(70d, 40d),
            new(70d, 10d),
            new(100d, 10d),
        ];
        PointD[] editableRoute =
        [
            logicalRoute[0],
            logicalRoute[3],
            logicalRoute[^1],
        ];
        PointD[] bridgedDisplayRoute =
        [
            logicalRoute[0],
            new PointD(10d, 10d),
            new PointD(15d, 14d),
            new PointD(20d, 10d),
            .. logicalRoute[1..],
        ];
        var (scene, connectorId, visualStateId) = RouteContextScene(
            logicalRoute,
            bridgedDisplayRoute,
            editableRoute);
        (int SegmentIndex, PointD Point, int PersistentInsertionIndex)[] additions =
        [
            (1, new PointD(30d, 25d), 1),
            (4, new PointD(70d, 25d), 2),
        ];

        foreach (var (segmentIndex, point, insertionIndex) in additions)
        {
            var add = new Canvas2DConnectorRouteContextAction(
                Canvas2DConnectorRouteContextActionKind.AddPoint,
                visualStateId,
                connectorId,
                segmentIndex,
                point,
                point);

            Assert.True(add.TryResolveTargetRoute(
                scene,
                editableRoute,
                out var targetRoute));
            Assert.Equal(
                editableRoute.Take(insertionIndex)
                    .Append(point)
                    .Concat(editableRoute.Skip(insertionIndex)),
                targetRoute);
            Assert.DoesNotContain(logicalRoute[1], targetRoute);
            Assert.DoesNotContain(logicalRoute[2], targetRoute);
            Assert.DoesNotContain(logicalRoute[4], targetRoute);
            Assert.DoesNotContain(logicalRoute[5], targetRoute);
            Assert.DoesNotContain(bridgedDisplayRoute[2], targetRoute);
        }

        var delete = new Canvas2DConnectorRouteContextAction(
            Canvas2DConnectorRouteContextActionKind.DeletePoint,
            visualStateId,
            connectorId,
            routeIndex: 1,
            documentPoint: editableRoute[1],
            routePoint: editableRoute[1]);
        Assert.True(delete.TryResolveTargetRoute(
            scene,
            editableRoute,
            out var deletedRoute));
        Assert.Empty(deletedRoute);
    }

    [Fact]
    public void FallbackConnectorRouteAddMapsOntoPreservedEditableGuidance()
    {
        PointD[] logicalRoute =
        [
            new(0d, 0d),
            new(100d, 0d),
            new(100d, 100d),
        ];
        PointD[] editableRoute =
        [
            logicalRoute[0],
            new PointD(50d, 50d),
            logicalRoute[^1],
        ];
        var originalGuidance = editableRoute.ToArray();
        var (scene, connectorId, visualStateId) = RouteContextScene(
            logicalRoute,
            logicalRoute,
            editableRoute);
        (int SegmentIndex, PointD Point, PointD[] Expected)[] cases =
        [
            (0, new PointD(50d, 0d),
            [
                logicalRoute[0],
                new PointD(50d, 0d),
                editableRoute[1],
                logicalRoute[^1],
            ]),
            (1, new PointD(100d, 50d),
            [
                logicalRoute[0],
                editableRoute[1],
                new PointD(100d, 50d),
                logicalRoute[^1],
            ]),
        ];

        foreach (var (segmentIndex, point, expected) in cases)
        {
            var add = new Canvas2DConnectorRouteContextAction(
                Canvas2DConnectorRouteContextActionKind.AddPoint,
                visualStateId,
                connectorId,
                segmentIndex,
                point,
                point);

            Assert.True(add.TryResolveTargetRoute(scene, editableRoute, out var targetRoute));
            Assert.Equal(expected.AsEnumerable(), targetRoute.AsEnumerable());
            Assert.Equal(originalGuidance, editableRoute);
        }
    }

    [Fact]
    public void ConnectorRouteContextActionDeletesFirstMiddleAndLastInternalBends()
    {
        PointD[] route =
        [
            new(0d, 0d),
            new(40d, 0d),
            new(40d, 40d),
            new(80d, 40d),
            new(80d, 80d),
        ];
        var (scene, connectorId, visualStateId) = RouteContextScene(route);

        foreach (var bendIndex in new[] { 1, 2, 3 })
        {
            var action = new Canvas2DConnectorRouteContextAction(
                Canvas2DConnectorRouteContextActionKind.DeletePoint,
                visualStateId,
                connectorId,
                bendIndex,
                route[bendIndex],
                route[bendIndex]);

            Assert.True(action.TryResolveTargetRoute(scene, route, out var targetRoute));
            Assert.Equal(route.Where((_, index) => index != bendIndex), targetRoute);
        }

        var staleRoute = route.ToArray();
        staleRoute[2] = new PointD(41d, 40d);
        var staleAction = new Canvas2DConnectorRouteContextAction(
            Canvas2DConnectorRouteContextActionKind.DeletePoint,
            visualStateId,
            connectorId,
            routeIndex: 2,
            documentPoint: route[2],
            routePoint: route[2]);
        Assert.False(staleAction.TryResolveTargetRoute(scene, staleRoute, out _));
    }

    [Fact]
    public async Task ConnectorEndpointHandlesNormalizeAndCannotStartPersistentDrag()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var visualStateId = ConnectorVisualId(inputs);
        var connectorId = ConnectorId(inputs);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [visualStateId]));
        await using var controller = new Canvas2DInteractionController(context.Session);
        var endpoints = context.InitialState.CurrentScene!.Items.Where(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "connector-endpoint-handle:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(connectorId)).ToArray();

        Assert.Equal(2, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            var point = new PointD(
                endpoint.Bounds.X + (endpoint.Bounds.Width / 2d),
                endpoint.Bounds.Y + (endpoint.Bounds.Height / 2d));
            var contextMenu = await controller.PointerContextMenuAsync(point);
            Assert.Equal(endpoint.Id, contextMenu.TargetId);
            Assert.Equal(visualStateId, contextMenu.TargetOrigin?.VisualStateId);

            var pointerId = StringComparer.Ordinal.Equals(
                endpoint.Metadata[Canvas2DConnectorEndpointMetadata.HandleRole].TextValue,
                Canvas2DConnectorEndpointMetadata.StartEndpointRole)
                    ? 901L
                    : 902L;
            var pressed = await controller.PointerPressedAsync(new Canvas2DPointerInput(
                pointerId,
                point,
                buttons: 1));
            var moved = await controller.PointerMovedAsync(new Canvas2DPointerInput(
                pointerId,
                point + new VectorD(30d, 20d),
                buttons: 1));
            var cancelled = await controller.PointerCancelledAsync(pointerId);

            Assert.Equal(Canvas2DInteractionStatus.Unchanged, pressed.Status);
            Assert.Equal("pointer", pressed.CssCursor);
            Assert.Equal(Canvas2DInteractionStatus.Unchanged, moved.Status);
            Assert.Equal("pointer", moved.CssCursor);
            Assert.Equal(Canvas2DInteractionStatus.Unchanged, cancelled.Status);
            Assert.Null(cancelled.SessionState.EditorState.ActiveGesture);
            Assert.Equal(context.InitialState.DocumentRevision, cancelled.SessionState.DocumentRevision);
            Assert.Equal(0, cancelled.SessionState.HistoryStatus.EntryCount);
            Assert.Same(context.InitialState.RoutingResult, cancelled.SessionState.RoutingResult);
            Assert.Same(context.InitialState.ProjectedGraph, cancelled.SessionState.ProjectedGraph);
        }

        Assert.Equal(0, context.Pipeline.SceneRebuildCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(1, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task PointerReleaseImplementsPlainAndControlSetSelectionSemantics()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var context = await AttachAsync(inputs, EditorStateSnapshot.Empty);
        for (var index = 0; index < 7; index++)
        {
            EnqueueSuccessfulSceneRebuild(context.Pipeline);
        }

        await using var controller = new Canvas2DInteractionController(context.Session);
        var alpha = new PointD(20d, 30d);
        var beta = new PointD(220d, 30d);
        var empty = new PointD(700d, 500d);

        AssertSelection(
            (await ClickAsync(controller, 101, alpha)).SessionState.EditorState,
            VisualId(inputs, 0));
        AssertSelection(
            (await ClickAsync(controller, 102, beta)).SessionState.EditorState,
            VisualId(inputs, 1));
        AssertSelection(
            (await ClickAsync(controller, 103, alpha, controlKey: true))
                .SessionState.EditorState,
            VisualId(inputs, 0),
            VisualId(inputs, 1));
        AssertSelection(
            (await ClickAsync(controller, 104, beta, controlKey: true))
                .SessionState.EditorState,
            VisualId(inputs, 0));
        Assert.Empty((await ClickAsync(controller, 105, alpha, controlKey: true))
            .SessionState.EditorState.Selection);

        var controlEmpty = await ClickAsync(controller, 106, empty, controlKey: true);
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, controlEmpty.Status);
        Assert.Empty(controlEmpty.SessionState.EditorState.Selection);

        _ = await ClickAsync(controller, 107, alpha);
        var plainEmpty = await ClickAsync(controller, 108, empty);
        Assert.Empty(plainEmpty.SessionState.EditorState.Selection);
        Assert.Equal(new DocumentRevision(3), plainEmpty.SessionState.DocumentRevision);
        Assert.Equal(0, plainEmpty.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(7, context.Pipeline.SceneRebuildCount);
        Assert.Equal(8, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task NodeThenOwnedLabelControlClickTogglesOneLogicalVisualSelection()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var labelled = Canvas2DSceneTestData.CreateWithNodeLabels();
        var context = await AttachAsync(inputs, EditorStateSnapshot.Empty);
        EnqueueLabelledSceneRebuild();
        EnqueueLabelledSceneRebuild();
        await using var controller = new Canvas2DInteractionController(context.Session);
        var scene = context.InitialState.CurrentScene!;
        var visualStateId = VisualId(inputs, 0);
        var node = scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId);

        var selected = await ClickAsync(controller, 120, new PointD(
            node.Bounds.Left + (node.Bounds.Width / 2d),
            node.Bounds.Top + (node.Bounds.Height / 2d)));
        var selectedScene = selected.SessionState.CurrentScene!;
        var label = selectedScene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.VisualStateId == visualStateId);
        var toggled = await ClickAsync(
            controller,
            121,
            new PointD(
                label.Bounds.Left + (label.Bounds.Width / 2d),
                label.Bounds.Top + (label.Bounds.Height / 2d)),
            controlKey: true);

        Assert.Equal(node.Id, selected.TargetId);
        AssertSelection(selected.SessionState.EditorState, visualStateId);
        Assert.Equal(label.Id, toggled.TargetId);
        Assert.Empty(toggled.SessionState.EditorState.Selection);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        Assert.Equal(0, toggled.SessionState.HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();

        void EnqueueLabelledSceneRebuild() => context.Pipeline.EnqueueScene(
            (artifacts, visualModel, editorState, _) =>
            {
                var build = new Canvas2DSceneBuilder().Build(
                    labelled.Graph,
                    labelled.Layout,
                    labelled.Routing,
                    visualModel,
                    editorState);
                return ValueTask.FromResult(EditingSessionPipelineResult.Success(
                    artifacts,
                    Assert.IsType<Canvas2DScene>(build.Scene)));
            });
    }

    [Fact]
    public async Task SelectedBodyPressDefersCollapseUntilClickAndHonorsThreshold()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var initial = new EditorStateSnapshot(
            selection: [VisualId(inputs, 1), VisualId(inputs, 0)]);
        var context = await AttachAsync(inputs, initial);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);

        var pressed = await controller.PointerPressedAsync(new Canvas2DPointerInput(
            109,
            new PointD(220d, 30d),
            buttons: 1));
        var jitter = await controller.PointerMovedAsync(new Canvas2DPointerInput(
            109,
            new PointD(222d, 30d),
            buttons: 1));
        var released = await controller.PointerReleasedAsync(new Canvas2DPointerInput(
            109,
            new PointD(222d, 30d)));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, pressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, jitter.Status);
        AssertSelection(
            jitter.SessionState.EditorState,
            VisualId(inputs, 0),
            VisualId(inputs, 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, released.Status);
        Assert.Equal(VisualId(inputs, 1), Assert.Single(
            released.SessionState.EditorState.Selection));
        Assert.Equal(1, context.Pipeline.SceneRebuildCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ObjectBodyCursorUsesGrabAndEmptyCanvasUsesDefault()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var context = await AttachAsync(inputs, EditorStateSnapshot.Empty);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);

        var overNode = await controller.PointerMovedAsync(new PointD(20d, 30d));
        var repeated = await controller.PointerMovedAsync(new PointD(21d, 31d));
        var empty = await controller.PointerMovedAsync(new PointD(700d, 500d));

        Assert.Equal("grab", overNode.CssCursor);
        Assert.Equal("grab", repeated.CssCursor);
        Assert.Equal("default", empty.CssCursor);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        Assert.Equal(0, empty.SessionState.HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task PointerLeaveClearsHoverAndPreservesSelection()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = VisualId(inputs, 0);
        var hovered = NodeId(inputs, 1);
        var initial = new EditorStateSnapshot(selection: [selected], hoveredObjectId: hovered);
        var context = await AttachAsync(inputs, initial);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);

        var result = await controller.PointerLeftAsync();

        Assert.Equal(Canvas2DInteractionStatus.Updated, result.Status);
        Assert.Null(result.SessionState.EditorState.HoveredObjectId);
        Assert.Equal(selected, Assert.Single(result.SessionState.EditorState.Selection));
        Assert.Equal(1, context.Pipeline.SceneRebuildCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task PointerLeaveDuringFullRebuildClearsHoverBeforeTheFollowUpSceneInstalls()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = VisualId(inputs, 0);
        var initial = new EditorStateSnapshot(
            selection: [selected],
            hoveredObjectId: NodeId(inputs, 1));
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, initial));
        var fullRunEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFullRun = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.EnqueueFull(async (snapshot, editorState, _) =>
        {
            fullRunEntered.TrySetResult();
            await releaseFullRun.Task;
            return ControlledEditingSessionPipeline.Success(
                EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision),
                snapshot.VisualModel,
                editorState);
        });
        EnqueueSuccessfulSceneRebuild(pipeline);
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initial),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var controller = new Canvas2DInteractionController(session);
        var visual = document.VisualModel.VisualStates.First();

        var command = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            new PointD(350d, 250d)));
        Assert.True(command.IsCommitted);
        await fullRunEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EditingSessionStatus.Rebuilding, session.CaptureState().Status);

        var leave = await controller.PointerLeftAsync();

        Assert.Equal(Canvas2DInteractionStatus.Updated, leave.Status);
        Assert.Null(session.CaptureState().EditorState.HoveredObjectId);
        Assert.Equal(selected, Assert.Single(session.CaptureState().EditorState.Selection));
        releaseFullRun.TrySetResult();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var ready = session.CaptureState();
        Assert.True(
            ready.Status == EditingSessionStatus.Ready,
            string.Join(Environment.NewLine, ready.RuntimeDiagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.Null(ready.EditorState.HoveredObjectId);
        Assert.Equal(selected, Assert.Single(ready.EditorState.Selection));
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.PreservingNodeLayoutRunCount);
        Assert.Equal(1, pipeline.SceneRebuildCount);
        Assert.Equal(2, RenderCount(execution));
    }

    [Fact]
    public async Task OptimisticGuardRejectsAnObsoleteSceneWithoutChangingEditorState()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var context = await AttachAsync(inputs, EditorStateSnapshot.Empty);
        var stale = context.Session.CaptureState();
        var intervening = new EditorStateSnapshot(hoveredObjectId: NodeId(inputs, 0));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        var installed = await context.Session.UpdateEditorStateAsync(intervening);
        Assert.True(installed.Succeeded);
        var sceneRebuilds = context.Pipeline.SceneRebuildCount;
        var renders = RenderCount(context.Execution);

        var result = await context.Session.UpdateEditorStateFromInteractionAsync(
            stale.CurrentScene!,
            stale.Generation,
            stale.EditorState,
            new EditorStateSnapshot(hoveredObjectId: NodeId(inputs, 1)));

        Assert.Equal(EditingSessionOperationStatus.Superseded, result.Status);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == Canvas2DInteractionDiagnosticCodes.StaleScene);
        Assert.Same(intervening, result.State.EditorState);
        Assert.Equal(sceneRebuilds, context.Pipeline.SceneRebuildCount);
        Assert.Equal(renders, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task RuntimeFaultedSessionRejectsGraphicalInteraction()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Failure());
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            EditingSessionTestHarness.CreateDocument(inputs),
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        var session = attachment.Session!;
        await using var controller = new Canvas2DInteractionController(session);

        var result = await controller.PointerMovedAsync(new PointD(20d, 30d));

        Assert.Equal(Canvas2DInteractionStatus.Unavailable, result.Status);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == Canvas2DInteractionDiagnosticCodes.UnavailableSession);
        Assert.Equal(0, pipeline.SceneRebuildCount);
        Assert.Equal(0, RenderCount(execution));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task DisposedControllerIsIdempotentAndStartsNoFurtherWork()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var context = await AttachAsync(inputs, EditorStateSnapshot.Empty);
        var controller = new Canvas2DInteractionController(context.Session);

        await controller.DisposeAsync();
        await controller.DisposeAsync();
        var result = await controller.PointerActivatedAsync(new PointD(20d, 30d));

        Assert.Equal(Canvas2DInteractionStatus.Disposed, result.Status);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == Canvas2DInteractionDiagnosticCodes.Disposed);
        Assert.Equal(0, context.Pipeline.SceneRebuildCount);
        Assert.Equal(1, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task DisposalCancelsTheActiveInteractionAndRejectsQueuedInput()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var context = await AttachAsync(inputs, EditorStateSnapshot.Empty);
        var sceneRunEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Pipeline.EnqueueScene(async (_, _, _, cancellationToken) =>
        {
            sceneRunEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation should end the controlled scene run.");
        });
        var controller = new Canvas2DInteractionController(context.Session);

        var active = controller.PointerMovedAsync(new PointD(220d, 30d)).AsTask();
        await sceneRunEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = controller.PointerActivatedAsync(new PointD(20d, 30d)).AsTask();
        var disposal = controller.DisposeAsync().AsTask();
        await Task.WhenAll(active, queued, disposal).WaitAsync(TimeSpan.FromSeconds(5));
        var activeResult = await active;
        var queuedResult = await queued;

        Assert.Equal(Canvas2DInteractionStatus.Disposed, activeResult.Status);
        Assert.Equal(Canvas2DInteractionStatus.Disposed, queuedResult.Status);
        Assert.Equal(1, context.Pipeline.SceneRebuildCount);
        Assert.Equal(1, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task MoveGesturePreviewsExactTranslationAndCommitsPinnedPositionOnce()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        EnqueueSuccessfulSceneRebuild(pipeline);
        EnqueueSuccessfulSceneRebuild(pipeline);
        Exception? pipelineException = null;
        pipeline.EnqueueFull((snapshot, editorState, _) =>
        {
            try
            {
                return ValueTask.FromResult(
                    SuccessAtVisualPositions(inputs, snapshot, editorState));
            }
            catch (Exception exception)
            {
                pipelineException = exception;
                return ValueTask.FromResult(ControlledEditingSessionPipeline.Failure());
            }
        });
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var controller = new Canvas2DInteractionController(session);
        var targetVisualId = inputs.Graph.Nodes[0].Source.VisualStateId!;
        var targetSceneId = NodeId(inputs, 0);
        var originalItem = Assert.Single(
            session.CaptureState().CurrentScene!.Items,
            item => item.Id == targetSceneId);
        var start = new PointD(20d, 30d);
        var current = new PointD(45d, 65d);
        var translation = new VectorD(25d, 35d);

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(7, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(7, current, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, pressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        var preview = Assert.Single(
            moved.SessionState.CurrentScene!.Items,
            item => item.Origin.VisualStateId == targetVisualId &&
                item.Origin.StableSourceKey?.StartsWith(
                    "move-preview:",
                    StringComparison.Ordinal) == true &&
                item.Geometry.Kind == originalItem.Geometry.Kind);
        Assert.Equal(originalItem.Bounds.Translate(translation), preview.Bounds);
        Assert.Equal(
            originalItem.Transform.Then(Matrix2D.CreateTranslation(translation)),
            preview.Transform);
        Assert.Equal(Canvas2DHitTestMode.None, preview.HitTestPolicy.Mode);

        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(7, current));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(released.PersistentOperation!.IsCommitted);
        var committed = document.CaptureSnapshot();
        Assert.Equal(new DocumentRevision(4), committed.Revision);
        Assert.True(committed.VisualModel.TryGetVisualState(targetVisualId, out var visual));
        Assert.Equal(new PointD(35d, 55d), visual!.Position);
        Assert.Equal(VisualPlacementMode.Pinned, visual.PlacementMode);
        var ready = session.CaptureState();
        Assert.True(pipelineException is null, pipelineException?.ToString());
        Assert.True(
            ready.Status == EditingSessionStatus.Ready,
            string.Join(Environment.NewLine, ready.RuntimeDiagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var installed = Assert.Single(
            ready.CurrentScene!.Items,
            item => item.Id == targetSceneId);
        Assert.Equal(new RectD(35d, 55d, 100d, 50d), installed.Bounds);
        Assert.Null(ready.EditorState.ActiveGesture);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.PreservingNodeLayoutRunCount);
        Assert.Equal(1, pipeline.SceneRebuildCount);
        Assert.Equal(3, RenderCount(execution));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SelectedMovableSubsetPreviewsAndCommitsOneAtomicGroupMove(
        int initiatorIndex)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = new EditorStateSnapshot(selection:
        [
            VisualId(inputs, 1),
            ConnectorVisualId(inputs),
            VisualId(inputs, 0),
        ]);
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, selected));
        EnqueueSuccessfulSceneRebuild(pipeline);
        EnqueueSuccessfulSceneRebuild(pipeline);
        pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(selected),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var controller = new Canvas2DInteractionController(session);
        var originalInitiator = inputs.Layout.Nodes.Single(node =>
            node.ProjectedObjectId == inputs.Graph.Nodes[initiatorIndex].Id).Bounds;
        var start = new PointD(originalInitiator.Left + 10d, originalInitiator.Top + 10d);
        var firstPoint = start + new VectorD(20d, 20d);
        var finalPoint = start + new VectorD(30d, 30d);
        var finalDelta = new VectorD(30d, 30d);

        var pressed = await controller.PointerPressedAsync(new Canvas2DPointerInput(
            110,
            start,
            buttons: 1,
            controlKey: true));
        var firstPreview = await controller.PointerMovedAsync(new Canvas2DPointerInput(
            110,
            firstPoint,
            buttons: 1,
            controlKey: true));
        var finalPreview = await controller.PointerMovedAsync(new Canvas2DPointerInput(
            110,
            finalPoint,
            buttons: 1,
            controlKey: true));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, pressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, firstPreview.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, finalPreview.Status);
        Assert.Equal("grabbing", firstPreview.CssCursor);
        Assert.Equal("grabbing", finalPreview.CssCursor);
        AssertSelectionEqual(selected, finalPreview.SessionState.EditorState);
        foreach (var index in new[] { 0, 1 })
        {
            var visualStateId = VisualId(inputs, index);
            var original = inputs.Layout.Nodes.Single(node =>
                node.ProjectedObjectId == inputs.Graph.Nodes[index].Id).Bounds;
            var preview = finalPreview.SessionState.CurrentScene!.Items.Single(item =>
                item.Origin.VisualStateId == visualStateId &&
                item.Origin.StableSourceKey?.StartsWith(
                    "move-preview:",
                    StringComparison.Ordinal) == true &&
                item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle);
            Assert.Equal(original.Translate(finalDelta), preview.Bounds);
        }

        Assert.Equal(new DocumentRevision(3), document.Revision);
        Assert.Equal(0, finalPreview.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(2, pipeline.SceneRebuildCount);

        var released = await controller.PointerReleasedAsync(new Canvas2DPointerInput(
            110,
            finalPoint,
            controlKey: true));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(MoveVisualStatesCommand.KnownTypeId,
            released.PersistentOperation?.CommandTypeId);
        Assert.Equal("grab", released.CssCursor);
        var committed = document.CaptureSnapshot();
        Assert.Equal(new DocumentRevision(4), committed.Revision);
        Assert.Equal(1, released.SessionState.HistoryStatus.EntryCount);
        AssertSelectionEqual(selected, session.CaptureState().EditorState);
        Assert.True(committed.VisualModel.TryGetVisualState(VisualId(inputs, 0), out var alpha));
        Assert.True(committed.VisualModel.TryGetVisualState(VisualId(inputs, 1), out var beta));
        Assert.True(committed.VisualModel.TryGetVisualState(
            ConnectorVisualId(inputs),
            out var connector));
        Assert.Equal(new PointD(40d, 50d), alpha!.Position);
        Assert.Equal(new PointD(240d, 50d), beta!.Position);
        Assert.Equal(default, connector!.Position);
        Assert.Equal(VisualPlacementMode.Pinned, alpha.PlacementMode);
        Assert.Equal(VisualPlacementMode.Pinned, beta.PlacementMode);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.PreservingNodeLayoutRunCount);
        Assert.Equal(2, pipeline.SceneRebuildCount);
        Assert.Equal(4, RenderCount(execution));

        var syntheticClick = await controller.PointerActivatedAsync(finalPoint);
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, syntheticClick.Status);
        AssertSelectionEqual(selected, syntheticClick.SessionState.EditorState);
    }

    [Fact]
    public async Task SingleMovePreviewAndCommitClampExactlyToDocumentOrigin()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        EnqueueSuccessfulSceneRebuild(pipeline);
        EnqueueSuccessfulSceneRebuild(pipeline);
        pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var controller = new Canvas2DInteractionController(session);
        var targetVisualId = VisualId(inputs, 0);
        var start = new PointD(20d, 30d);
        var outside = new PointD(-100d, -100d);

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(123, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(123, outside, buttons: 1));

        var preview = Assert.Single(moved.SessionState.CurrentScene!.Items, item =>
            item.Origin.VisualStateId == targetVisualId &&
            item.Origin.StableSourceKey?.StartsWith(
                "move-preview:",
                StringComparison.Ordinal) == true &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle);
        Assert.Equal(new RectD(0d, 0d, 100d, 50d), preview.Bounds);

        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(123, outside));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(document.VisualModel.TryGetVisualState(targetVisualId, out var visual));
        Assert.Equal(new PointD(0d, 0d), visual!.Position);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);

        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(document.VisualModel.TryGetVisualState(targetVisualId, out var undone));
        Assert.Equal(new PointD(10d, 20d), undone!.Position);
        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(document.VisualModel.TryGetVisualState(targetVisualId, out var redone));
        Assert.Equal(new PointD(0d, 0d), redone!.Position);
    }

    [Fact]
    public async Task MultiMoveUsesOneClampedDeltaAndPreservesRelativeGeometry()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = new EditorStateSnapshot(selection:
        [
            VisualId(inputs, 0),
            VisualId(inputs, 1),
        ]);
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, selected));
        EnqueueSuccessfulSceneRebuild(pipeline);
        EnqueueSuccessfulSceneRebuild(pipeline);
        pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(selected),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var controller = new Canvas2DInteractionController(session);
        var start = new PointD(20d, 30d);
        var outside = new PointD(-100d, -100d);

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(124, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(124, outside, buttons: 1));

        Assert.Equal(
            new RectD(0d, 0d, 100d, 50d),
            FindMovePreview(moved.SessionState.CurrentScene!, VisualId(inputs, 0)).Bounds);
        Assert.Equal(
            new RectD(200d, 0d, 100d, 50d),
            FindMovePreview(moved.SessionState.CurrentScene!, VisualId(inputs, 1)).Bounds);

        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(124, outside));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(document.VisualModel.TryGetVisualState(VisualId(inputs, 0), out var alpha));
        Assert.True(document.VisualModel.TryGetVisualState(VisualId(inputs, 1), out var beta));
        Assert.Equal(new PointD(0d, 0d), alpha!.Position);
        Assert.Equal(new PointD(200d, 0d), beta!.Position);
        Assert.Equal(200d, beta.Position.X - alpha.Position.X);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);

        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(document.VisualModel.TryGetVisualState(VisualId(inputs, 0), out alpha));
        Assert.True(document.VisualModel.TryGetVisualState(VisualId(inputs, 1), out beta));
        Assert.Equal(new PointD(10d, 20d), alpha!.Position);
        Assert.Equal(new PointD(210d, 20d), beta!.Position);
        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(document.VisualModel.TryGetVisualState(VisualId(inputs, 0), out alpha));
        Assert.True(document.VisualModel.TryGetVisualState(VisualId(inputs, 1), out beta));
        Assert.Equal(new PointD(0d, 0d), alpha!.Position);
        Assert.Equal(new PointD(200d, 0d), beta!.Position);
    }

    [Fact]
    public async Task ControlDragOfUnselectedObjectReplacesSelectionAndMovesOnlyInitiator()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var initial = new EditorStateSnapshot(selection: [VisualId(inputs, 0)]);
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, initial));
        EnqueueSuccessfulSceneRebuild(pipeline);
        pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initial),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var controller = new Canvas2DInteractionController(session);
        var start = new PointD(220d, 30d);
        var end = start + new VectorD(25d, 15d);

        var pressed = await controller.PointerPressedAsync(new Canvas2DPointerInput(
            122,
            start,
            buttons: 1,
            controlKey: true));
        var previewed = await controller.PointerMovedAsync(new Canvas2DPointerInput(
            122,
            end,
            buttons: 1,
            controlKey: true));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, pressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, previewed.Status);
        AssertSelection(previewed.SessionState.EditorState, VisualId(inputs, 1));
        Assert.DoesNotContain(previewed.SessionState.CurrentScene!.Items, item =>
            item.Origin.VisualStateId == VisualId(inputs, 0) &&
            item.Origin.StableSourceKey?.StartsWith(
                "move-preview:",
                StringComparison.Ordinal) == true);
        Assert.Contains(previewed.SessionState.CurrentScene.Items, item =>
            item.Origin.VisualStateId == VisualId(inputs, 1) &&
            item.Origin.StableSourceKey?.StartsWith(
                "move-preview:",
                StringComparison.Ordinal) == true);

        var released = await controller.PointerReleasedAsync(new Canvas2DPointerInput(
            122,
            end,
            controlKey: true));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(MoveVisualStateCommand.KnownTypeId,
            released.PersistentOperation?.CommandTypeId);
        AssertSelection(session.CaptureState().EditorState, VisualId(inputs, 1));
        var committed = document.CaptureSnapshot();
        Assert.True(committed.VisualModel.TryGetVisualState(VisualId(inputs, 0), out var alpha));
        Assert.True(committed.VisualModel.TryGetVisualState(VisualId(inputs, 1), out var beta));
        Assert.Equal(new PointD(10d, 20d), alpha!.Position);
        Assert.Equal(new PointD(235d, 35d), beta!.Position);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData(VisualPlacementMode.Automatic)]
    [InlineData(VisualPlacementMode.Manual)]
    public async Task MoveUsesDisplayedBoundsWhenVisualHintDiffersFromLayout(
        VisualPlacementMode placementMode)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var targetVisualId = inputs.Graph.Nodes[0].Source.VisualStateId!;
        var targetSceneId = NodeId(inputs, 0);
        var displayedBounds = new RectD(10d, 20d, 100d, 50d);
        var delta = new VectorD(25d, 35d);
        var expectedBounds = displayedBounds.Translate(delta);
        var document = CreateDocumentWithVisualHint(
            inputs,
            targetVisualId,
            new PointD(410d, 320d),
            displayedBounds.Size,
            placementMode);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        EnqueueSuccessfulSceneRebuild(pipeline);
        EnqueueSuccessfulSceneRebuild(pipeline);
        pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var controller = new Canvas2DInteractionController(session);
        var start = new PointD(20d, 30d);
        var current = start + delta;

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(70, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(70, current, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, pressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        Assert.DoesNotContain(moved.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DInteractionDiagnosticCodes.InvalidGestureGeometry);
        Assert.Equal(expectedBounds, Assert.Single(
            moved.SessionState.CurrentScene!.Items,
            item => item.Origin.VisualStateId == targetVisualId &&
                item.Origin.StableSourceKey?.StartsWith(
                    "move-preview:",
                    StringComparison.Ordinal) == true &&
                item.Origin.RelatedSceneObjectIds.Contains(targetSceneId)).Bounds);

        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(70, current));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.DoesNotContain(released.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DInteractionDiagnosticCodes.InvalidGestureGeometry);
        var committed = document.CaptureSnapshot();
        Assert.True(committed.VisualModel.TryGetVisualState(targetVisualId, out var visual));
        Assert.Equal(expectedBounds.TopLeft, visual!.Position);
        Assert.Equal(expectedBounds.Size, visual.Size);
        Assert.Equal(VisualPlacementMode.Pinned, visual.PlacementMode);
        Assert.Equal(expectedBounds, Assert.Single(
            session.CaptureState().CurrentScene!.Items,
            item => item.Id == targetSceneId).Bounds);
    }

    [Fact]
    public async Task NoOpMoveClearsPreviewWithoutCommandOrFullPipelineRun()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = new EditorStateSnapshot(
            selection: [VisualId(inputs, 0), VisualId(inputs, 1)]);
        var context = await AttachAsync(inputs, selected);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var pointer = new Canvas2DPointerInput(8, new PointD(20d, 30d), buttons: 1);

        var pressed = await controller.PointerPressedAsync(pointer);
        var moved = await controller.PointerMovedAsync(new Canvas2DPointerInput(
            8,
            new PointD(30d, 40d),
            buttons: 1));
        var released = await controller.PointerReleasedAsync(new Canvas2DPointerInput(
            8,
            pointer.CssPoint));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, pressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        Assert.True(
            released.Status == Canvas2DInteractionStatus.Updated,
            string.Join(Environment.NewLine, released.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.Null(released.PersistentOperation);
        Assert.Null(released.SessionState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), released.SessionState.DocumentRevision);
        Assert.Equal(0, released.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        AssertSelectionEqual(selected, released.SessionState.EditorState);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        Assert.Equal(3, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task CancelledMoveClearsPreviewWithoutPersistentMutation()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var selected = new EditorStateSnapshot(
            selection: [VisualId(inputs, 0), VisualId(inputs, 1)]);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, selected));
        EnqueueSuccessfulSceneRebuild(pipeline);
        EnqueueSuccessfulSceneRebuild(pipeline);
        EnqueueSuccessfulSceneRebuild(pipeline);
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(selected),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var controller = new Canvas2DInteractionController(session);

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(9, new PointD(20d, 30d), buttons: 1));
        _ = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(9, new PointD(50d, 70d), buttons: 1));
        var cancelled = await controller.PointerCancelledAsync(9);

        Assert.Equal(Canvas2DInteractionStatus.Updated, cancelled.Status);
        Assert.Null(cancelled.SessionState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), document.Revision);
        Assert.Equal(0, cancelled.SessionState.HistoryStatus.EntryCount);
        AssertSelectionEqual(selected, cancelled.SessionState.EditorState);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(2, pipeline.SceneRebuildCount);
        Assert.Equal(3, RenderCount(execution));
    }

    [Fact]
    public async Task StaleMoveCannotCommitAndOnlyClearsItsTransientGesture()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var selected = new EditorStateSnapshot(
            selection: [VisualId(inputs, 0), VisualId(inputs, 1)]);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, selected));
        EnqueueSuccessfulSceneRebuild(pipeline);
        pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        EnqueueSuccessfulSceneRebuild(pipeline);
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(selected),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var controller = new Canvas2DInteractionController(session);
        var targetVisualId = inputs.Graph.Nodes[0].Source.VisualStateId!;
        var otherVisualId = inputs.Graph.Nodes[1].Source.VisualStateId!;

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(10, new PointD(20d, 30d), buttons: 1));
        _ = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(10, new PointD(30d, 40d), buttons: 1));
        var external = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            otherVisualId,
            new PointD(230d, 40d),
            VisualPlacementMode.Pinned));
        Assert.True(external.IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        var stale = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(10, new PointD(60d, 80d)));

        Assert.Equal(Canvas2DInteractionStatus.Stale, stale.Status);
        Assert.Contains(
            stale.Diagnostics,
            diagnostic => diagnostic.Code == Canvas2DInteractionDiagnosticCodes.StaleGesture);
        Assert.Null(stale.SessionState.EditorState.ActiveGesture);
        var snapshot = document.CaptureSnapshot();
        Assert.Equal(new DocumentRevision(4), snapshot.Revision);
        Assert.True(snapshot.VisualModel.TryGetVisualState(targetVisualId, out var target));
        Assert.Equal(new PointD(10d, 20d), target!.Position);
        Assert.Equal(1, stale.SessionState.HistoryStatus.EntryCount);
        AssertSelectionEqual(selected, stale.SessionState.EditorState);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.PreservingNodeLayoutRunCount);
        Assert.Equal(1, pipeline.SceneRebuildCount);
        Assert.Equal(3, RenderCount(execution));
    }

    [Fact]
    public async Task PrimaryReleaseWithoutMoveGesturePreservesPhaseKSelectionSemantics()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var context = await AttachAsync(inputs, EditorStateSnapshot.Empty);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);

        var selected = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(11, new PointD(220d, 30d)));

        Assert.Equal(Canvas2DInteractionStatus.Updated, selected.Status);
        Assert.Equal(NodeId(inputs, 1), selected.TargetId);
        Assert.Equal(VisualId(inputs, 1), Assert.Single(selected.SessionState.EditorState.Selection));
        Assert.Equal(1, context.Pipeline.SceneRebuildCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task SelectedItemHasTopmostResizeHandleAndResizeMovesAreSceneOnly()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = NodeId(inputs, 0);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [VisualId(inputs, 0)]));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var initial = context.Session.CaptureState();
        var initialScene = Assert.IsType<Canvas2DScene>(initial.CurrentScene);
        var handle = FindResizeHandle(initialScene, selected);
        Assert.Equal(new RectD(105d, 65d, 10d, 10d), handle.Bounds);
        Assert.Same(handle, initialScene.Items[^1]);

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(21, new PointD(110d, 70d), buttons: 1));
        var samePoint = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(21, new PointD(110d, 70d), buttons: 1));
        var leftWhileCaptured = await controller.PointerLeftAsync();
        var first = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(21, new PointD(135d, 85d), buttons: 1));
        var second = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(21, new PointD(150d, 100d), buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        Assert.Equal("inceptus.canvas2d:resize", pressed.SessionState.EditorState.ActiveGesture?.Kind);
        Assert.Equal("nwse-resize", pressed.CssCursor);
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, samePoint.Status);
        Assert.Equal("nwse-resize", samePoint.CssCursor);
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, leftWhileCaptured.Status);
        Assert.Equal("nwse-resize", leftWhileCaptured.CssCursor);
        Assert.Equal(Canvas2DInteractionStatus.Updated, first.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, second.Status);
        var secondScene = Assert.IsType<Canvas2DScene>(second.SessionState.CurrentScene);
        var targetPreview = FindResizePreview(secondScene, selected);
        Assert.Equal(new RectD(10d, 20d, 140d, 80d), targetPreview.Bounds);
        Assert.Same(initial.ProjectedGraph, second.SessionState.ProjectedGraph);
        Assert.Same(initial.LayoutResult, second.SessionState.LayoutResult);
        Assert.Same(initial.RoutingResult, second.SessionState.RoutingResult);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(3, context.Pipeline.SceneRebuildCount);
        Assert.Equal(4, RenderCount(context.Execution));

        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        var cancelled = await controller.PointerCancelledAsync(21);
        Assert.Equal(Canvas2DInteractionStatus.Updated, cancelled.Status);
        Assert.Null(cancelled.SessionState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), cancelled.SessionState.DocumentRevision);
        Assert.Equal(0, cancelled.SessionState.HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    public static IEnumerable<object[]> ResizeDirectionCases =>
    [
        ["northwest", 5d, 7d, new RectD(15d, 27d, 95d, 43d), "nwse-resize"],
        ["north", 5d, 7d, new RectD(10d, 27d, 100d, 43d), "ns-resize"],
        ["northeast", 5d, 7d, new RectD(10d, 27d, 105d, 43d), "nesw-resize"],
        ["east", 5d, 7d, new RectD(10d, 20d, 105d, 50d), "ew-resize"],
        ["southeast", 5d, 7d, new RectD(10d, 20d, 105d, 57d), "nwse-resize"],
        ["south", 5d, 7d, new RectD(10d, 20d, 100d, 57d), "ns-resize"],
        ["southwest", 5d, 7d, new RectD(15d, 20d, 95d, 57d), "nesw-resize"],
        ["west", 5d, 7d, new RectD(15d, 20d, 95d, 50d), "ew-resize"],
    ];

    [Theory]
    [MemberData(nameof(ResizeDirectionCases))]
    public async Task EveryResizeDirectionUsesSharedPreviewGeometryAndStableCursor(
        string role,
        double deltaX,
        double deltaY,
        RectD expectedBounds,
        string expectedCursor)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = NodeId(inputs, 0);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [VisualId(inputs, 0)]));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var initial = context.Session.CaptureState();
        var handle = FindResizeInteraction(initial.CurrentScene!, selected, role);
        var start = new PointD(
            handle.Bounds.X + (handle.Bounds.Width / 2d),
            handle.Bounds.Y + (handle.Bounds.Height / 2d));

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(80, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(new Canvas2DPointerInput(
            80,
            start + new VectorD(deltaX, deltaY),
            buttons: 1));
        var cancelled = await controller.PointerCancelledAsync(80);

        Assert.Equal(expectedCursor, pressed.CssCursor);
        Assert.Equal(expectedCursor, moved.CssCursor);
        var previewBounds = FindResizePreview(moved.SessionState.CurrentScene!, selected).Bounds;
        Assert.Equal(expectedBounds.X, previewBounds.X, precision: 10);
        Assert.Equal(expectedBounds.Y, previewBounds.Y, precision: 10);
        Assert.Equal(expectedBounds.Width, previewBounds.Width, precision: 10);
        Assert.Equal(expectedBounds.Height, previewBounds.Height, precision: 10);
        Assert.True(Canvas2DResizeGeometry.TryParseRole(role, out var direction));
        var movedInteraction = FindResizeInteraction(
            moved.SessionState.CurrentScene!,
            selected,
            role);
        Assert.Equal(
            Canvas2DResizeGeometry.InteractionBounds(expectedBounds, direction),
            movedInteraction.Bounds);
        Assert.Equal("default", cancelled.CssCursor);
        Assert.Null(cancelled.SessionState.EditorState.ActiveGesture);
        Assert.Equal(initial.DocumentRevision, cancelled.SessionState.DocumentRevision);
        Assert.Equal(0, cancelled.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(3, context.Pipeline.SceneRebuildCount);
    }

    [Fact]
    public async Task OffCenterCornerPressUsesPointerDeltaWithoutSnappingToCorner()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = NodeId(inputs, 0);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [VisualId(inputs, 0)]));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var initial = context.Session.CaptureState();
        var handle = FindResizeInteraction(initial.CurrentScene!, selected, "northwest");
        var pressPoint = new PointD(12d, 22d);
        Assert.True(handle.Bounds.Contains(pressPoint));

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(183, pressPoint, buttons: 1));
        var moved = await controller.PointerMovedAsync(new Canvas2DPointerInput(
            183,
            pressPoint + new VectorD(20d, 15d),
            buttons: 1));

        var gesture = Assert.IsType<EditorGestureSnapshot>(
            pressed.SessionState.EditorState.ActiveGesture);
        Assert.Equal(pressPoint, gesture.Origin);
        Assert.Equal(pressPoint, gesture.Current);
        Assert.Equal(
            new RectD(30d, 35d, 80d, 35d),
            FindResizePreview(moved.SessionState.CurrentScene!, selected).Bounds);
        Assert.Equal("nwse-resize", moved.CssCursor);
        Assert.Equal(initial.DocumentRevision, moved.SessionState.DocumentRevision);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);

        _ = await controller.PointerCancelledAsync(183);
        await context.Session.DisposeAsync();
    }

    [Theory]
    [InlineData("east", 0d, 15d, "ew-resize")]
    [InlineData("west", 0d, -15d, "ew-resize")]
    [InlineData("north", 15d, 0d, "ns-resize")]
    [InlineData("south", -15d, 0d, "ns-resize")]
    public async Task OrthogonalResizeSamplesAreNoOpsWithoutSceneRebuild(
        string role,
        double deltaX,
        double deltaY,
        string expectedCursor)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = NodeId(inputs, 0);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [VisualId(inputs, 0)]));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var interaction = FindResizeInteraction(
            context.Session.CaptureState().CurrentScene!,
            selected,
            role);
        var start = new PointD(
            interaction.Bounds.X + (interaction.Bounds.Width / 2d),
            interaction.Bounds.Y + (interaction.Bounds.Height / 2d));

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(181, start, buttons: 1));
        var beforeNoOpScene = pressed.SessionState.CurrentScene;
        var moved = await controller.PointerMovedAsync(new Canvas2DPointerInput(
            181,
            start + new VectorD(deltaX, deltaY),
            buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, moved.Status);
        Assert.Equal(expectedCursor, moved.CssCursor);
        Assert.Same(beforeNoOpScene, moved.SessionState.CurrentScene);
        Assert.Equal(1, context.Pipeline.SceneRebuildCount);
        Assert.Equal(2, RenderCount(context.Execution));

        _ = await controller.PointerCancelledAsync(181);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task RepeatedBeyondClampResizeSampleIsANoOpWithoutSceneRebuild()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = NodeId(inputs, 0);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [VisualId(inputs, 0)]));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var interaction = FindResizeInteraction(
            context.Session.CaptureState().CurrentScene!,
            selected,
            "southeast");
        var start = new PointD(
            interaction.Bounds.X + (interaction.Bounds.Width / 2d),
            interaction.Bounds.Y + (interaction.Bounds.Height / 2d));

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(182, start, buttons: 1));
        var firstClamp = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(182, new PointD(-100d, -100d), buttons: 1));
        var repeatedClamp = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(182, new PointD(-200d, -200d), buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, repeatedClamp.Status);
        Assert.Equal("nwse-resize", repeatedClamp.CssCursor);
        Assert.Same(firstClamp.SessionState.CurrentScene, repeatedClamp.SessionState.CurrentScene);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        Assert.Equal(3, RenderCount(context.Execution));

        _ = await controller.PointerCancelledAsync(182);
        Assert.Equal(3, context.Pipeline.SceneRebuildCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ResizeHoverReportsCursorEvenWhenHoverStateIsAlreadyCurrent()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = NodeId(inputs, 0);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [VisualId(inputs, 0)]));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var handle = FindResizeInteraction(
            context.Session.CaptureState().CurrentScene!,
            selected,
            "northwest");
        var point = new PointD(
            handle.Bounds.X + (handle.Bounds.Width / 2d),
            handle.Bounds.Y + (handle.Bounds.Height / 2d));

        var changed = await controller.PointerMovedAsync(point);
        var unchanged = await controller.PointerMovedAsync(point);

        Assert.Equal(Canvas2DInteractionStatus.Updated, changed.Status);
        Assert.Equal("nwse-resize", changed.CssCursor);
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, unchanged.Status);
        Assert.Equal("nwse-resize", unchanged.CssCursor);
        Assert.Equal(1, context.Pipeline.SceneRebuildCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task NorthWestResizeCompletionCommitsChangedPositionAndSizeOnce()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = NodeId(inputs, 0);
        var targetVisualId = inputs.Graph.Nodes[0].Source.VisualStateId!;
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        var editorState = new EditorStateSnapshot(selection: [VisualId(inputs, 0)]);
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, editorState));
        EnqueueSuccessfulSceneRebuild(pipeline);
        EnqueueSuccessfulSceneRebuild(pipeline);
        pipeline.EnqueueFull((snapshot, currentEditorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, currentEditorState)));
        pipeline.EnqueueFull((snapshot, currentEditorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, currentEditorState)));
        pipeline.EnqueueFull((snapshot, currentEditorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, currentEditorState)));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(editorState),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var controller = new Canvas2DInteractionController(session);
        var handle = FindResizeInteraction(session.CaptureState().CurrentScene!, selected, "northwest");
        var start = new PointD(
            handle.Bounds.X + (handle.Bounds.Width / 2d),
            handle.Bounds.Y + (handle.Bounds.Height / 2d));
        var end = start + new VectorD(-15d, -10d);

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(81, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(81, end, buttons: 1));
        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(81, end));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("nwse-resize", pressed.CssCursor);
        Assert.Equal("nwse-resize", moved.CssCursor);
        Assert.Equal("default", released.CssCursor);
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(ResizeVisualStateCommand.KnownTypeId, released.PersistentOperation!.CommandTypeId);
        Assert.True(document.VisualModel.TryGetVisualState(targetVisualId, out var visual));
        Assert.Equal(new PointD(0d, 10d), visual!.Position);
        Assert.Equal(new SizeD(110d, 60d), visual.Size);
        Assert.Equal(VisualPlacementMode.Pinned, visual.PlacementMode);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.PreservingNodeLayoutRunCount);
        Assert.Equal(2, pipeline.SceneRebuildCount);

        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(document.VisualModel.TryGetVisualState(targetVisualId, out var undone));
        Assert.Equal(new PointD(10d, 20d), undone!.Position);
        Assert.Equal(new SizeD(100d, 50d), undone.Size);
        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(document.VisualModel.TryGetVisualState(targetVisualId, out var redone));
        Assert.Equal(new PointD(0d, 10d), redone!.Position);
        Assert.Equal(new SizeD(110d, 60d), redone.Size);
    }

    [Fact]
    public async Task ResizePreviewClampsToOneDocumentUnitAndNoOpDoesNotCommit()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = NodeId(inputs, 0);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [VisualId(inputs, 0)]));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(22, new PointD(110d, 70d), buttons: 1));
        var minimum = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(22, new PointD(-100d, -100d), buttons: 1));
        Assert.Equal(
            new RectD(
                10d,
                20d,
                Canvas2DInteractionController.MinimumVisualExtent,
                Canvas2DInteractionController.MinimumVisualExtent),
            FindResizePreview(minimum.SessionState.CurrentScene!, selected).Bounds);
        _ = await controller.PointerCancelledAsync(22);

        var current = context.Session.CaptureState();
        var noOpHandle = FindResizeHandle(current.CurrentScene!, selected);
        var center = new PointD(
            noOpHandle.Bounds.X + 5d,
            noOpHandle.Bounds.Y + 5d);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        var noOpPressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(23, center, buttons: 1));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        var noOp = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(23, center));

        Assert.Equal(Canvas2DInteractionStatus.Updated, noOpPressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, noOp.Status);
        Assert.Null(noOp.PersistentOperation);
        Assert.Equal(new DocumentRevision(3), noOp.SessionState.DocumentRevision);
        Assert.Equal(0, noOp.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ResizeCompletionCommitsOnePinnedCommandAndOneSelectivePipelineRun()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = NodeId(inputs, 0);
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        var initialEditorState = new EditorStateSnapshot(selection: [VisualId(inputs, 0)]);
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, initialEditorState));
        EnqueueSuccessfulSceneRebuild(pipeline);
        EnqueueSuccessfulSceneRebuild(pipeline);
        pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initialEditorState),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var controller = new Canvas2DInteractionController(session);
        var targetVisualId = inputs.Graph.Nodes[0].Source.VisualStateId!;

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(24, new PointD(110d, 70d), buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(24, new PointD(135d, 90d), buttons: 1));
        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(24, new PointD(135d, 90d)));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(ResizeVisualStateCommand.KnownTypeId, released.PersistentOperation!.CommandTypeId);
        Assert.True(released.PersistentOperation.IsCommitted);
        var snapshot = document.CaptureSnapshot();
        Assert.Equal(new DocumentRevision(4), snapshot.Revision);
        Assert.True(snapshot.VisualModel.TryGetVisualState(targetVisualId, out var visual));
        Assert.Equal(new PointD(10d, 20d), visual!.Position);
        Assert.Equal(new SizeD(125d, 70d), visual.Size);
        Assert.Equal(VisualPlacementMode.Pinned, visual.PlacementMode);
        var ready = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, ready.Status);
        Assert.Null(ready.EditorState.ActiveGesture);
        Assert.Equal(new RectD(10d, 20d, 125d, 70d), Assert.Single(
            ready.CurrentScene!.Items,
            item => item.Id == selected).Bounds);
        Assert.Equal(1, ready.HistoryStatus.EntryCount);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.PreservingNodeLayoutRunCount);
        Assert.Equal(2, pipeline.SceneRebuildCount);
        Assert.Equal(4, RenderCount(execution));
    }

    [Theory]
    [InlineData(VisualPlacementMode.Automatic)]
    [InlineData(VisualPlacementMode.Manual)]
    public async Task ResizeUsesDisplayedBoundsWhenVisualHintDiffersFromLayout(
        VisualPlacementMode placementMode)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var targetVisualId = inputs.Graph.Nodes[0].Source.VisualStateId!;
        var targetSceneId = NodeId(inputs, 0);
        var displayedBounds = new RectD(10d, 20d, 100d, 50d);
        var expectedBounds = new RectD(10d, 20d, 125d, 70d);
        var initialEditorState = new EditorStateSnapshot(selection: [targetVisualId]);
        var document = CreateDocumentWithVisualHint(
            inputs,
            targetVisualId,
            new PointD(410d, 320d),
            new SizeD(5d, 7d),
            placementMode);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(
            inputs,
            initialEditorState));
        EnqueueSuccessfulSceneRebuild(pipeline);
        EnqueueSuccessfulSceneRebuild(pipeline);
        pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initialEditorState),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var controller = new Canvas2DInteractionController(session);
        var handle = FindResizeHandle(session.CaptureState().CurrentScene!, targetSceneId);
        var start = new PointD(
            handle.Bounds.X + (handle.Bounds.Width / 2d),
            handle.Bounds.Y + (handle.Bounds.Height / 2d));
        var current = start + new VectorD(25d, 20d);

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(71, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(71, current, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        Assert.DoesNotContain(moved.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DInteractionDiagnosticCodes.InvalidGestureGeometry);
        Assert.Equal(
            expectedBounds,
            FindResizePreview(moved.SessionState.CurrentScene!, targetSceneId).Bounds);

        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(71, current));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.DoesNotContain(released.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DInteractionDiagnosticCodes.InvalidGestureGeometry);
        var committed = document.CaptureSnapshot();
        Assert.True(committed.VisualModel.TryGetVisualState(targetVisualId, out var visual));
        Assert.Equal(expectedBounds.TopLeft, visual!.Position);
        Assert.Equal(expectedBounds.Size, visual.Size);
        Assert.Equal(VisualPlacementMode.Pinned, visual.PlacementMode);
        Assert.Equal(expectedBounds, Assert.Single(
            session.CaptureState().CurrentScene!.Items,
            item => item.Id == targetSceneId).Bounds);
    }

    [Fact]
    public async Task StaleResizeCannotCommitAndDisposalClearsAnActiveResize()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = NodeId(inputs, 0);
        var initialEditorState = new EditorStateSnapshot(selection: [VisualId(inputs, 0)]);
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, initialEditorState));
        EnqueueSuccessfulSceneRebuild(pipeline);
        pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        EnqueueSuccessfulSceneRebuild(pipeline);
        EnqueueSuccessfulSceneRebuild(pipeline);
        EnqueueSuccessfulSceneRebuild(pipeline);
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initialEditorState),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var controller = new Canvas2DInteractionController(session);
        var targetVisualId = inputs.Graph.Nodes[0].Source.VisualStateId!;
        var otherVisualId = inputs.Graph.Nodes[1].Source.VisualStateId!;

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(25, new PointD(110d, 70d), buttons: 1));
        var external = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            otherVisualId,
            new PointD(230d, 40d),
            VisualPlacementMode.Pinned));
        Assert.True(external.IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var stale = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(25, new PointD(145d, 95d)));

        Assert.Equal(Canvas2DInteractionStatus.Stale, stale.Status);
        Assert.Null(stale.SessionState.EditorState.ActiveGesture);
        Assert.True(document.VisualModel.TryGetVisualState(targetVisualId, out var unchanged));
        Assert.Equal(new SizeD(100d, 50d), unchanged!.Size);
        Assert.Equal(1, stale.SessionState.HistoryStatus.EntryCount);

        var current = session.CaptureState();
        var handle = FindResizeHandle(current.CurrentScene!, selected);
        var center = new PointD(handle.Bounds.X + 5d, handle.Bounds.Y + 5d);
        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(26, center, buttons: 1));
        await controller.DisposeAsync();

        var disposedState = session.CaptureState();
        Assert.Null(disposedState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(4), disposedState.DocumentRevision);
        Assert.Equal(1, disposedState.HistoryStatus.EntryCount);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.PreservingNodeLayoutRunCount);
        Assert.Equal(3, pipeline.SceneRebuildCount);
        Assert.Equal(5, RenderCount(execution));
    }

    [Fact]
    public async Task RouteBendGesturePreviewsMultipleMovesWithoutRunningUpstreamStages()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var connector = ConnectorId(inputs);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [ConnectorVisualId(inputs)]));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var initial = context.Session.CaptureState();
        var handle = FindRouteHandle(initial.CurrentScene!, connector);

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(31, new PointD(160d, 45d), buttons: 1));
        var first = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(31, new PointD(170d, 55d), buttons: 1));
        var second = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(31, new PointD(180d, 70d), buttons: 1));

        Assert.Equal(new RectD(155d, 40d, 10d, 10d), handle.Bounds);
        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        Assert.Equal("inceptus.canvas2d:route-bend", pressed.SessionState.EditorState.ActiveGesture?.Kind);
        Assert.Equal(Canvas2DInteractionStatus.Updated, first.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, second.Status);
        Assert.Equal(
            [new PointD(110d, 45d), new PointD(180d, 70d), new PointD(210d, 45d)],
            FindRoutePreview(second.SessionState.CurrentScene!, connector)
                .Geometry.Points.AsEnumerable());
        Assert.Same(initial.ProjectedGraph, second.SessionState.ProjectedGraph);
        Assert.Same(initial.LayoutResult, second.SessionState.LayoutResult);
        Assert.Same(initial.RoutingResult, second.SessionState.RoutingResult);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(3, context.Pipeline.SceneRebuildCount);
        Assert.Equal(4, RenderCount(context.Execution));

        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        var cancelled = await controller.PointerCancelledAsync(31);
        Assert.Null(cancelled.SessionState.EditorState.ActiveGesture);
        Assert.Equal(0, cancelled.SessionState.HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task BridgedConnectorBendGestureUsesOnlyTheEditableManualRoute()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute()
            .WithCrossingConnector();
        var edge = inputs.Graph.Edges.Single(candidate =>
            candidate.Source.SemanticElementId == new SemanticElementId("test:semantic:ab"));
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
        var connectorVisualId = edge.Source.VisualStateId!;
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [connectorVisualId]));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var connector = context.InitialState.CurrentScene!.Items.Single(item =>
            item.Id == connectorId);
        var handle = FindRouteHandle(context.InitialState.CurrentScene!, connectorId);

        Assert.True(
            connector.Geometry.Points.Length >
            Canvas2DConnectorPathMetadata.ResolveEditable(connector).Length);
        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(310, Center(handle.Bounds), buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(310, new PointD(175d, 65d), buttons: 1));

        var preview = FindRoutePreview(moved.SessionState.CurrentScene!, connectorId);
        Assert.Equal(
            [new PointD(110d, 45d), new PointD(175d, 65d), new PointD(210d, 45d)],
            preview.Geometry.Points.AsEnumerable());
        Assert.Equal(3, preview.Geometry.Points.Length);

        var cancelled = await controller.PointerCancelledAsync(310);
        Assert.Null(cancelled.SessionState.EditorState.ActiveGesture);
        Assert.Equal(0, cancelled.SessionState.HistoryStatus.EntryCount);
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task RouteBendPreviewAndCommitClampToDocumentOrigin()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var connector = ConnectorId(inputs);
        var connectorVisualId = ConnectorVisualId(inputs);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [connectorVisualId]));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var start = new PointD(160d, 45d);
        var outside = new PointD(-100d, -100d);

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(34, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(34, outside, buttons: 1));

        Assert.Equal(
            [new PointD(110d, 45d), new PointD(0d, 0d), new PointD(210d, 45d)],
            FindRoutePreview(moved.SessionState.CurrentScene!, connector)
                .Geometry.Points.AsEnumerable());

        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(34, outside));
        await context.Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(context.Document.VisualModel.TryGetVisualState(
            connectorVisualId,
            out var connectorVisual));
        Assert.Equal(
            [new PointD(110d, 45d), new PointD(0d, 0d), new PointD(210d, 45d)],
            connectorVisual!.Route.AsEnumerable());
        Assert.Equal(1, context.Session.CaptureState().HistoryStatus.EntryCount);

        Assert.True((await context.Session.UndoAsync()).IsCommitted);
        await context.Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(context.Document.VisualModel.TryGetVisualState(
            connectorVisualId,
            out var undone));
        Assert.Equal(
            [new PointD(110d, 45d), new PointD(160d, 45d), new PointD(210d, 45d)],
            undone!.Route.AsEnumerable());
        Assert.True((await context.Session.RedoAsync()).IsCommitted);
        await context.Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(context.Document.VisualModel.TryGetVisualState(
            connectorVisualId,
            out var redone));
        Assert.Equal(
            [new PointD(110d, 45d), new PointD(0d, 0d), new PointD(210d, 45d)],
            redone!.Route.AsEnumerable());
    }

    [Fact]
    public async Task RouteBendNoOpAndDisposalNeverCommit()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var connector = ConnectorId(inputs);
        var context = await AttachAsync(
            inputs,
            new EditorStateSnapshot(selection: [ConnectorVisualId(inputs)]));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        var controller = new Canvas2DInteractionController(context.Session);
        var point = new PointD(160d, 45d);

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(32, point, buttons: 1));
        var noOp = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(32, point));
        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, noOp.Status);
        Assert.Null(noOp.PersistentOperation);
        Assert.Equal(0, noOp.SessionState.HistoryStatus.EntryCount);

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(33, point, buttons: 1));
        await controller.DisposeAsync();
        var state = context.Session.CaptureState();
        Assert.Null(state.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), state.DocumentRevision);
        Assert.Equal(0, state.HistoryStatus.EntryCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(4, context.Pipeline.SceneRebuildCount);
        await context.Session.DisposeAsync();
    }

    [Theory]
    [InlineData("move")]
    [InlineData("resize")]
    [InlineData("route")]
    public async Task ExtremeFinitePreviewSampleTerminatesGestureWithoutPersistentChange(
        string gestureKind)
    {
        var (context, start) = await AttachGestureAsync(gestureKind);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(41, start, buttons: 1));
        var activated = StringComparer.Ordinal.Equals(gestureKind, "move")
            ? await controller.PointerMovedAsync(new Canvas2DPointerInput(
                41,
                start + new VectorD(10d, 10d),
                buttons: 1))
            : pressed;
        var invalid = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(
                41,
                new PointD(double.MaxValue, double.MaxValue),
                buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, activated.Status);
        Assert.Equal(Canvas2DInteractionStatus.Failed, invalid.Status);
        Assert.Contains(
            invalid.Diagnostics,
            diagnostic => diagnostic.Code ==
                Canvas2DInteractionDiagnosticCodes.InvalidGestureGeometry);
        Assert.Null(invalid.SessionState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), invalid.SessionState.DocumentRevision);
        Assert.Equal(0, invalid.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        Assert.Equal(3, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Theory]
    [InlineData("move")]
    [InlineData("resize")]
    [InlineData("route")]
    public async Task ExtremeFiniteReleaseTerminatesGestureWithoutCommand(
        string gestureKind)
    {
        var (context, start) = await AttachGestureAsync(gestureKind);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(42, start, buttons: 1));
        if (StringComparer.Ordinal.Equals(gestureKind, "move"))
        {
            _ = await controller.PointerMovedAsync(new Canvas2DPointerInput(
                42,
                start + new VectorD(10d, 10d),
                buttons: 1));
        }
        var invalid = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(
                42,
                new PointD(double.MaxValue, double.MaxValue)));

        Assert.Equal(Canvas2DInteractionStatus.Failed, invalid.Status);
        Assert.Null(invalid.PersistentOperation);
        Assert.Contains(
            invalid.Diagnostics,
            diagnostic => diagnostic.Code ==
                Canvas2DInteractionDiagnosticCodes.InvalidGestureGeometry);
        Assert.Null(invalid.SessionState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), invalid.SessionState.DocumentRevision);
        Assert.Equal(0, invalid.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        Assert.Equal(3, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task ExternalCommitReconcilesPrivateGestureAndNextPressNeedsNoOldPointerBoundary()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        EnqueueSuccessfulSceneRebuild(pipeline);
        pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        EnqueueSuccessfulSceneRebuild(pipeline);
        EnqueueSuccessfulSceneRebuild(pipeline);
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var controller = new Canvas2DInteractionController(session);

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(51, new PointD(20d, 30d), buttons: 1));
        var external = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            inputs.Graph.Nodes[1].Source.VisualStateId!,
            new PointD(230d, 40d),
            VisualPlacementMode.Pinned));
        Assert.True(external.IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);

        var nextPress = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(52, new PointD(20d, 30d), buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, nextPress.Status);
        Assert.DoesNotContain(
            nextPress.Diagnostics,
            diagnostic => diagnostic.Code ==
                Canvas2DInteractionDiagnosticCodes.GestureAlreadyActive);
        var nextMove = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(52, new PointD(30d, 40d), buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, nextMove.Status);
        Assert.NotNull(nextMove.SessionState.EditorState.ActiveGesture);
        var cancelled = await controller.PointerCancelledAsync(52);
        Assert.Null(cancelled.SessionState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(4), cancelled.SessionState.DocumentRevision);
        Assert.Equal(1, cancelled.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.PreservingNodeLayoutRunCount);
        Assert.Equal(2, pipeline.SceneRebuildCount);
        Assert.Equal(4, RenderCount(execution));
    }

    [Fact]
    public async Task FailedGestureSceneRebuildClearsStateAndAllowsRetry()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        pipeline.EnqueueScene(ControlledEditingSessionPipeline.Failure("TEST_GESTURE_SCENE_FAILURE"));
        pipeline.EnqueueFull((_, editorState, _) => ValueTask.FromResult(
            ControlledEditingSessionPipeline.Success(inputs, editorState)));
        EnqueueSuccessfulSceneRebuild(pipeline);
        EnqueueSuccessfulSceneRebuild(pipeline);
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            EditingSessionTestHarness.CreateDocument(inputs),
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var controller = new Canvas2DInteractionController(session);

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(53, new PointD(20d, 30d), buttons: 1));
        var failed = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(53, new PointD(40d, 50d), buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Failed, failed.Status);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, failed.SessionState.Status);
        Assert.Null(failed.SessionState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), failed.SessionState.DocumentRevision);
        Assert.Equal(0, failed.SessionState.HistoryStatus.EntryCount);
        var retry = await session.RetryAsync();
        Assert.True(retry.Succeeded);
        Assert.Equal(EditingSessionStatus.Ready, retry.State.Status);
        Assert.Null(retry.State.EditorState.ActiveGesture);

        var retriedPress = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(54, new PointD(20d, 30d), buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, retriedPress.Status);
        var retriedMove = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(54, new PointD(40d, 50d), buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, retriedMove.Status);
        var cancelled = await controller.PointerCancelledAsync(54);
        Assert.Null(cancelled.SessionState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), cancelled.SessionState.DocumentRevision);
        Assert.Equal(0, cancelled.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(2, pipeline.FullRunCount);
        Assert.Equal(3, pipeline.SceneRebuildCount);
        Assert.Equal(4, RenderCount(execution));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversionOverflowTerminatesGestureAndNextGestureWorks(bool failOnRelease)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var initialEditorState = new EditorStateSnapshot(
            viewport: new ViewportSnapshot(0.5d, default));
        var context = await AttachAsync(inputs, initialEditorState);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var startCss = new PointD(10d, 15d);

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(61, startCss, buttons: 1));
        var failed = failOnRelease
            ? await controller.PointerReleasedAsync(
                new Canvas2DPointerInput(
                    61,
                    new PointD(double.MaxValue, double.MaxValue)))
            : await controller.PointerMovedAsync(
                new Canvas2DPointerInput(
                    61,
                    new PointD(double.MaxValue, double.MaxValue),
                    buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Failed, failed.Status);
        Assert.Contains(
            failed.Diagnostics,
            diagnostic => diagnostic.Code ==
                Canvas2DInteractionDiagnosticCodes.CoordinateConversionFailed);
        Assert.Null(failed.SessionState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), failed.SessionState.DocumentRevision);
        Assert.Equal(0, failed.SessionState.HistoryStatus.EntryCount);

        var nextPress = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(62, startCss, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, nextPress.Status);
        var nextMove = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(62, startCss + new VectorD(10d, 10d), buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, nextMove.Status);
        Assert.NotNull(nextMove.SessionState.EditorState.ActiveGesture);
        var cancelled = await controller.PointerCancelledAsync(62);
        Assert.Null(cancelled.SessionState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), cancelled.SessionState.DocumentRevision);
        Assert.Equal(0, cancelled.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        Assert.Equal(3, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task PointerPressOnRelationshipVisualDoesNotStartMoveGesture()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = new EditorStateSnapshot(
            selection: [VisualId(inputs, 0), VisualId(inputs, 1)]);
        var context = await AttachAsync(inputs, selected);
        await using var controller = new Canvas2DInteractionController(context.Session);

        var result = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(12, new PointD(160d, 45d), buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, result.Status);
        Assert.Equal("pointer", result.CssCursor);
        Assert.Null(result.SessionState.EditorState.ActiveGesture);
        Assert.Equal(0, context.Pipeline.SceneRebuildCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(1, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task DisposalDuringMoveClearsPreviewWithoutPersistentCommand()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selected = new EditorStateSnapshot(
            selection: [VisualId(inputs, 0), VisualId(inputs, 1)]);
        var context = await AttachAsync(inputs, selected);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        var controller = new Canvas2DInteractionController(context.Session);

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(13, new PointD(20d, 30d), buttons: 1));
        _ = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(13, new PointD(40d, 50d), buttons: 1));
        await controller.DisposeAsync();

        var state = context.Session.CaptureState();
        Assert.Null(state.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), state.DocumentRevision);
        Assert.Equal(0, state.HistoryStatus.EntryCount);
        AssertSelectionEqual(selected, state.EditorState);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        Assert.Equal(3, RenderCount(context.Execution));
        await context.Session.DisposeAsync();
    }

    [Fact]
    public async Task EditableNodeLabelMovePreviewsSceneOnlyAndCommitsOneVisualOverride()
    {
        var inputs = CreateEditableNodeLabelInputs();
        var context = await AttachAsync(inputs, EditorStateSnapshot.Empty);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var initialSnapshot = context.Document.CaptureSnapshot();
        var scene = context.InitialState.CurrentScene!;
        var labelBody = FindNodeLabelBody(scene);
        var node = scene.Items.Single(item => item.Id == NodeId(inputs, 0));
        var start = Center(labelBody.Bounds);
        var delta = new VectorD(35d, -20d);
        var end = start + delta;

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(701, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(701, end, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        Assert.Equal("grabbing", pressed.CssCursor);
        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        Assert.Equal("grabbing", moved.CssCursor);
        Assert.Equal(initialSnapshot.Revision, moved.SessionState.DocumentRevision);
        Assert.Equal(0, moved.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        var preview = FindNodeLabelPreview(moved.SessionState.CurrentScene!);
        Assert.Equal(labelBody.Bounds.Translate(delta), preview.Bounds);
        Assert.Equal(node.Bounds, moved.SessionState.CurrentScene!.Items.Single(item =>
            item.Id == node.Id).Bounds);

        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(701, end));
        await context.Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(
            UpdateNodeLabelVisualOverrideCommand.KnownTypeId,
            released.PersistentOperation!.CommandTypeId);
        Assert.Equal(1, released.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(3, context.Pipeline.SceneRebuildCount);
        Assert.True(context.Document.VisualModel.TryGetVisualState(
            VisualId(inputs, 0),
            out var visual));
        Assert.True(NodeLabelVisualOverride.TryRead(visual!.Properties, out var actual));
        var expectedCenter = Center(labelBody.Bounds.Translate(delta));
        var ownerCenter = Center(node.Bounds);
        Assert.Equal(expectedCenter.X - ownerCenter.X, actual!.OffsetX, 9);
        Assert.Equal(expectedCenter.Y - ownerCenter.Y, actual.OffsetY, 9);
        Assert.Equal(labelBody.Bounds.Width, actual.Width, 9);
        Assert.Equal(labelBody.Bounds.Height, actual.Height, 9);
        Assert.Equal(node.Bounds.TopLeft, visual.Position);
        Assert.Equal(node.Bounds.Size, visual.Size);
        var finalSemantic = context.Document.CaptureSnapshot().SemanticModel;
        Assert.True(initialSnapshot.SemanticModel.Elements.AsSpan().SequenceEqual(
            finalSemantic.Elements.AsSpan()));
        Assert.True(initialSnapshot.SemanticModel.Relationships.AsSpan().SequenceEqual(
            finalSemantic.Relationships.AsSpan()));
    }

    [Fact]
    public async Task EditableNodeLabelMovePreviewAndCommitClampAtDocumentOrigin()
    {
        var inputs = CreateEditableNodeLabelInputs();
        var context = await AttachAsync(inputs, EditorStateSnapshot.Empty);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var labelBody = FindNodeLabelBody(context.InitialState.CurrentScene!);
        var node = context.InitialState.CurrentScene!.Items.Single(item =>
            item.Id == NodeId(inputs, 0));
        var start = Center(labelBody.Bounds);
        var outside = new PointD(-100d, -100d);

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(706, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(706, outside, buttons: 1));

        AssertBoundsApproximately(
            new RectD(0d, 0d, labelBody.Bounds.Width, labelBody.Bounds.Height),
            FindNodeLabelPreview(moved.SessionState.CurrentScene!).Bounds);
        Assert.Equal(node.Bounds, moved.SessionState.CurrentScene!.Items.Single(item =>
            item.Id == node.Id).Bounds);

        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(706, outside));
        await context.Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(context.Document.VisualModel.TryGetVisualState(
            VisualId(inputs, 0),
            out var visual));
        Assert.True(NodeLabelVisualOverride.TryRead(visual!.Properties, out var actual));
        AssertBoundsApproximately(
            new RectD(0d, 0d, labelBody.Bounds.Width, labelBody.Bounds.Height),
            actual!.ResolveBounds(node.Bounds));
        Assert.Equal(node.Bounds.TopLeft, visual.Position);
        Assert.Equal(1, context.Session.CaptureState().HistoryStatus.EntryCount);

        Assert.True((await context.Session.UndoAsync()).IsCommitted);
        await context.Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(context.Document.VisualModel.TryGetVisualState(
            VisualId(inputs, 0),
            out var undone));
        Assert.False(NodeLabelVisualOverride.TryRead(undone!.Properties, out _));
        Assert.True((await context.Session.RedoAsync()).IsCommitted);
        await context.Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(context.Document.VisualModel.TryGetVisualState(
            VisualId(inputs, 0),
            out var redone));
        Assert.True(NodeLabelVisualOverride.TryRead(redone!.Properties, out var restored));
        AssertBoundsApproximately(
            new RectD(0d, 0d, labelBody.Bounds.Width, labelBody.Bounds.Height),
            restored!.ResolveBounds(node.Bounds));
    }

    [Fact]
    public async Task EditableNodeLabelResizeUsesDirectionalCursorAndAtomicBoxOverride()
    {
        var inputs = CreateEditableNodeLabelInputs();
        var selected = new EditorStateSnapshot(selection: [VisualId(inputs, 0)]);
        var context = await AttachAsync(inputs, selected);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var scene = context.InitialState.CurrentScene!;
        var labelBody = FindNodeLabelBody(scene);
        var node = scene.Items.Single(item => item.Id == NodeId(inputs, 0));
        var zone = FindNodeLabelResizeZone(scene, "west");
        var start = Center(zone.Bounds);
        var delta = new VectorD(65d, 0d);
        var end = start + delta;
        var expectedBounds = Canvas2DResizeGeometry.CalculateBounds(
            labelBody.Bounds,
            delta,
            Canvas2DResizeDirection.West,
            NodeLabelVisualOverride.MinimumWidth,
            NodeLabelVisualOverride.MinimumHeight);

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(702, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(702, end, buttons: 1));
        var movedZone = FindNodeLabelResizeZone(moved.SessionState.CurrentScene!, "west");

        Assert.Equal("ew-resize", pressed.CssCursor);
        Assert.Equal("ew-resize", moved.CssCursor);
        Assert.Equal(expectedBounds, FindNodeLabelPreview(moved.SessionState.CurrentScene!).Bounds);
        Assert.Equal(
            Canvas2DResizeGeometry.InteractionBounds(
                expectedBounds,
                Canvas2DResizeDirection.West),
            movedZone.Bounds);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        Assert.Equal(0, moved.SessionState.HistoryStatus.EntryCount);

        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(702, end));
        await context.Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(
            UpdateNodeLabelVisualOverrideCommand.KnownTypeId,
            released.PersistentOperation!.CommandTypeId);
        Assert.Equal(1, released.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(3, context.Pipeline.SceneRebuildCount);
        Assert.True(context.Document.VisualModel.TryGetVisualState(
            VisualId(inputs, 0),
            out var visual));
        Assert.True(NodeLabelVisualOverride.TryRead(visual!.Properties, out var actual));
        var expectedCenter = Center(expectedBounds);
        var ownerCenter = Center(node.Bounds);
        Assert.Equal(expectedCenter.X - ownerCenter.X, actual!.OffsetX, 9);
        Assert.Equal(expectedCenter.Y - ownerCenter.Y, actual.OffsetY, 9);
        Assert.Equal(expectedBounds.Width, actual.Width, 9);
        Assert.Equal(expectedBounds.Height, actual.Height, 9);
        Assert.Equal(node.Bounds.TopLeft, visual.Position);
        Assert.Equal(node.Bounds.Size, visual.Size);
    }

    [Fact]
    public async Task EditableNodeLabelNorthWestResizeClampsToDocumentBoundary()
    {
        var initialBounds = new RectD(20d, 20d, 90d, 30d);
        var ownerBounds = new RectD(10d, 20d, 100d, 50d);
        var inputs = CreateEditableNodeLabelInputs(
            NodeLabelVisualOverride.FromBounds(ownerBounds, initialBounds));
        var selected = new EditorStateSnapshot(selection: [VisualId(inputs, 0)]);
        var context = await AttachAsync(inputs, selected);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var start = Center(FindNodeLabelResizeZone(
            context.InitialState.CurrentScene!,
            "northwest").Bounds);
        var outside = new PointD(-100d, -100d);

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(707, start, buttons: 1));
        var moved = await controller.PointerMovedAsync(
            new Canvas2DPointerInput(707, outside, buttons: 1));

        var expected = Canvas2DResizeGeometry.CalculateBounds(
            initialBounds,
            new PointD(0d, 0d) - start,
            Canvas2DResizeDirection.NorthWest,
            NodeLabelVisualOverride.MinimumWidth,
            NodeLabelVisualOverride.MinimumHeight);
        Assert.Equal(expected, FindNodeLabelPreview(moved.SessionState.CurrentScene!).Bounds);
        Assert.Equal(0d, expected.Left);
        Assert.Equal(0d, expected.Top);

        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(707, outside));
        await context.Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.True(context.Document.VisualModel.TryGetVisualState(
            VisualId(inputs, 0),
            out var visual));
        Assert.True(NodeLabelVisualOverride.TryRead(visual!.Properties, out var actual));
        Assert.Equal(expected, actual!.ResolveBounds(ownerBounds));
        Assert.Equal(ownerBounds.TopLeft, visual.Position);
    }

    [Fact]
    public async Task UnchangedNodeLabelGestureClearsPreviewWithoutCommandOrHistory()
    {
        var inputs = CreateEditableNodeLabelInputs();
        var context = await AttachAsync(inputs, EditorStateSnapshot.Empty);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var point = Center(FindNodeLabelBody(context.InitialState.CurrentScene!).Bounds);

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(703, point, buttons: 1));
        var released = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(703, point));
        var activation = await controller.PointerActivatedAsync(point);

        Assert.Equal(Canvas2DInteractionStatus.Updated, pressed.Status);
        Assert.Equal(Canvas2DInteractionStatus.Updated, released.Status);
        Assert.Null(released.PersistentOperation);
        Assert.Null(released.SessionState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(3), released.SessionState.DocumentRevision);
        Assert.Equal(0, released.SessionState.HistoryStatus.EntryCount);
        Assert.Equal(1, context.Pipeline.FullRunCount);
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, activation.Status);
        Assert.True(context.Document.VisualModel.TryGetVisualState(
            VisualId(inputs, 0),
            out var visual));
        Assert.False(NodeLabelVisualOverride.TryRead(visual!.Properties, out _));
    }

    [Fact]
    public async Task EditableNodeLabelHoverUsesGrabAndResizeZoneUsesCursorWithoutOverlay()
    {
        var inputs = CreateEditableNodeLabelInputs();
        var selected = new EditorStateSnapshot(selection: [VisualId(inputs, 0)]);
        var context = await AttachAsync(inputs, selected);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var body = FindNodeLabelBody(context.InitialState.CurrentScene!);

        var bodyHover = await controller.PointerMovedAsync(Center(body.Bounds));
        Assert.Equal("grab", bodyHover.CssCursor);
        var zone = FindNodeLabelResizeZone(bodyHover.SessionState.CurrentScene!, "east");
        var zoneHover = await controller.PointerMovedAsync(Center(zone.Bounds));

        Assert.Equal("ew-resize", zoneHover.CssCursor);
        Assert.DoesNotContain(zoneHover.SessionState.CurrentScene!.Items, item =>
            item.Origin.StableSourceKey?.Contains("hover", StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(zone.Id));
        Assert.Equal(2, context.Pipeline.SceneRebuildCount);
        Assert.Equal(0, zoneHover.SessionState.HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task NodeLabelContextActionExistsOnlyForCurrentManualOverride()
    {
        var automaticInputs = CreateEditableNodeLabelInputs();
        var automatic = await AttachAsync(
            automaticInputs,
            new EditorStateSnapshot(selection: [VisualId(automaticInputs, 0)]));
        await using var automaticController = new Canvas2DInteractionController(automatic.Session);
        var automaticScene = automatic.InitialState.CurrentScene!;
        var automaticResult = await automaticController.PointerContextMenuAsync(
            Center(FindNodeLabelBody(automaticScene).Bounds));

        Assert.Null(automaticResult.NodeLabelContextAction);

        var manualOverride = new NodeLabelVisualOverride(100d, 40d, 90d, 30d);
        var manualInputs = CreateEditableNodeLabelInputs(manualOverride);
        var manual = await AttachAsync(
            manualInputs,
            new EditorStateSnapshot(selection: [VisualId(manualInputs, 0)]));
        await using var manualController = new Canvas2DInteractionController(manual.Session);
        var manualScene = manual.InitialState.CurrentScene!;
        var manualResult = await manualController.PointerContextMenuAsync(
            Center(FindNodeLabelBody(manualScene).Bounds));
        var action = Assert.IsType<Canvas2DNodeLabelContextAction>(
            manualResult.NodeLabelContextAction);
        Assert.True(manualInputs.VisualModel.TryGetVisualState(
            action.TargetVisualStateId,
            out var visual));
        var currentVisual = Assert.IsType<VisualStateSnapshot>(visual);

        Assert.Equal(VisualId(manualInputs, 0), action.TargetVisualStateId);
        Assert.Equal(FindNodeLabelBody(manualScene).Id, action.SourceSceneObjectId);
        Assert.Equal(NodeId(manualInputs, 0), action.TargetNodeSceneObjectId);
        Assert.True(action.IsCurrent(manualScene, currentVisual));
        var resetVisual = new VisualStateSnapshot(
            currentVisual.Id,
            currentVisual.SemanticElementId,
            currentVisual.Position,
            currentVisual.Size,
            currentVisual.PlacementMode,
            currentVisual.Route,
            NodeLabelVisualOverride.UpdateProperties(currentVisual.Properties, null),
            currentVisual.ConnectorAnchors,
            currentVisual.SourceAnchorId,
            currentVisual.TargetAnchorId);
        Assert.False(action.IsCurrent(manualScene, resetVisual));

        await automatic.Session.DisposeAsync();
        await manual.Session.DisposeAsync();
    }

    [Fact]
    public async Task StaleNodeLabelGestureCannotOverwriteAnInterveningRevision()
    {
        var inputs = CreateEditableNodeLabelInputs();
        var context = await AttachAsync(inputs, EditorStateSnapshot.Empty);
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        context.Pipeline.EnqueueFull((snapshot, editorState, _) => ValueTask.FromResult(
            SuccessAtVisualPositions(inputs, snapshot, editorState)));
        EnqueueSuccessfulSceneRebuild(context.Pipeline);
        await using var controller = new Canvas2DInteractionController(context.Session);
        var body = FindNodeLabelBody(context.InitialState.CurrentScene!);
        var start = Center(body.Bounds);

        _ = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(704, start, buttons: 1));
        var external = await context.Session.ExecuteAsync(new MoveVisualStateCommand(
            context.Document.DocumentId,
            context.Document.Revision,
            VisualId(inputs, 1),
            new PointD(230d, 40d),
            VisualPlacementMode.Pinned));
        Assert.True(external.IsCommitted);
        await context.Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        var stale = await controller.PointerReleasedAsync(
            new Canvas2DPointerInput(704, start + new VectorD(30d, 20d)));

        Assert.Equal(Canvas2DInteractionStatus.Stale, stale.Status);
        Assert.Contains(stale.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DInteractionDiagnosticCodes.StaleGesture);
        Assert.Null(stale.SessionState.EditorState.ActiveGesture);
        Assert.Equal(new DocumentRevision(4), context.Document.Revision);
        Assert.True(context.Document.VisualModel.TryGetVisualState(
            VisualId(inputs, 0),
            out var target));
        Assert.False(NodeLabelVisualOverride.TryRead(target!.Properties, out _));
        Assert.Equal(1, stale.SessionState.HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task LastKnownGoodEditableLabelCannotAuthorizeAfterRuntimeFault()
    {
        var inputs = CreateEditableNodeLabelInputs();
        var context = await AttachAsync(inputs, EditorStateSnapshot.Empty);
        context.Pipeline.EnqueueFull(
            ControlledEditingSessionPipeline.Failure("TEST_NODE_LABEL_RUNTIME_FAULT"));
        await using var controller = new Canvas2DInteractionController(context.Session);
        var point = Center(FindNodeLabelBody(context.InitialState.CurrentScene!).Bounds);
        var external = await context.Session.ExecuteAsync(new MoveVisualStateCommand(
            context.Document.DocumentId,
            context.Document.Revision,
            VisualId(inputs, 1),
            new PointD(230d, 40d),
            VisualPlacementMode.Pinned));
        await context.Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(external.IsCommitted);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, context.Session.CaptureState().Status);

        var pressed = await controller.PointerPressedAsync(
            new Canvas2DPointerInput(705, point, buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Unavailable, pressed.Status);
        Assert.Contains(pressed.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DInteractionDiagnosticCodes.UnavailableSession);
        Assert.Null(pressed.SessionState.EditorState.ActiveGesture);
        Assert.True(context.Document.VisualModel.TryGetVisualState(
            VisualId(inputs, 0),
            out var target));
        Assert.False(NodeLabelVisualOverride.TryRead(target!.Properties, out _));
        Assert.Equal(0, context.Pipeline.SceneRebuildCount);
    }

    private static async ValueTask<InteractionTestContext> AttachAsync(
        Canvas2DSceneTestData inputs,
        EditorStateSnapshot initialEditorState,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null)
    {
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, initialEditorState));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var document = EditingSessionTestHarness.CreateDocument(
            inputs,
            connectorAnchorPolicyProvider);
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(
                initialEditorState,
                connectorAnchorPolicyProvider),
            pipeline);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        var session = attachment.Session!;
        return new InteractionTestContext(
            session,
            pipeline,
            execution,
            session.CaptureState(),
            document);
    }

    private static async ValueTask<Canvas2DInteractionResult> ClickAsync(
        Canvas2DInteractionController controller,
        long pointerId,
        PointD point,
        bool controlKey = false)
    {
        _ = await controller.PointerPressedAsync(new Canvas2DPointerInput(
            pointerId,
            point,
            buttons: 1,
            controlKey: controlKey));
        return await controller.PointerReleasedAsync(new Canvas2DPointerInput(
            pointerId,
            point,
            controlKey: controlKey));
    }

    private static void AssertSelection(
        EditorStateSnapshot actual,
        params VisualStateId[] expected) =>
        Assert.True(
            expected.AsSpan().SequenceEqual(actual.Selection.AsSpan()),
            $"Expected [{string.Join(", ", expected.Select(item => item.Value))}] but found " +
            $"[{string.Join(", ", actual.Selection.Select(item => item.Value))}].");

    private static void AssertSelectionEqual(
        EditorStateSnapshot expected,
        EditorStateSnapshot actual) =>
        Assert.True(expected.Selection.AsSpan().SequenceEqual(actual.Selection.AsSpan()));

    private static async ValueTask<(InteractionTestContext Context, PointD Start)>
        AttachGestureAsync(string gestureKind)
    {
        var inputs = StringComparer.Ordinal.Equals(gestureKind, "route")
            ? Canvas2DSceneTestData.CreateWithPersistentRoute()
            : Canvas2DSceneTestData.Create();
        var editorState = gestureKind switch
        {
            "resize" => new EditorStateSnapshot(selection: [VisualId(inputs, 0)]),
            "route" => new EditorStateSnapshot(selection: [ConnectorVisualId(inputs)]),
            _ => EditorStateSnapshot.Empty,
        };
        var start = gestureKind switch
        {
            "resize" => new PointD(110d, 70d),
            "route" => new PointD(160d, 45d),
            _ => new PointD(20d, 30d),
        };
        return (await AttachAsync(inputs, editorState), start);
    }

    private static void EnqueueSuccessfulSceneRebuild(ControlledEditingSessionPipeline pipeline) =>
        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));

    private static Canvas2DSceneTestData CreateEditableNodeLabelInputs(
        NodeLabelVisualOverride? visualOverride = null)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var owner = inputs.Graph.Nodes[0];
        var label = new Inceptus.DocumentEngine.Contracts.Projection.ProjectedLabel(
            owner.Source,
            owner.Id,
            "Is the customer order approved?",
            nodePlacement: new Inceptus.DocumentEngine.Contracts.Projection.NodeLabelPlacement(
                Inceptus.DocumentEngine.Contracts.Projection.NodeLabelPlacementKind.OutsideBelow,
                gap: 8d,
                maximumWidth: 120d),
            nodeInteractionPolicy:
                Inceptus.DocumentEngine.Contracts.Projection.NodeLabelInteractionPolicy
                    .MoveAndResize);
        var graph = new Inceptus.DocumentEngine.Contracts.Projection.ProjectedGraph(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            inputs.Graph.Nodes,
            inputs.Graph.Edges,
            inputs.Graph.Groups,
            inputs.Graph.Ports,
            [label]);
        var result = inputs.WithGraph(graph);
        if (visualOverride is null)
        {
            return result;
        }

        var visualModel = new VisualModelSnapshot(
            inputs.VisualModel.DocumentId,
            inputs.VisualModel.Revision,
            inputs.VisualModel.VisualStates.Select(visual =>
                visual.Id == owner.Source.VisualStateId
                    ? new VisualStateSnapshot(
                        visual.Id,
                        visual.SemanticElementId,
                        visual.Position,
                        visual.Size,
                        visual.PlacementMode,
                        visual.Route,
                        NodeLabelVisualOverride.UpdateProperties(
                            visual.Properties,
                            visualOverride),
                        visual.ConnectorAnchors,
                        visual.SourceAnchorId,
                        visual.TargetAnchorId)
                    : visual));
        return result.WithVisualModel(visualModel);
    }

    private static Canvas2DSceneItem FindNodeLabelBody(Canvas2DScene scene) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.StableSourceKey?.StartsWith(
                "node-label-interaction:",
                StringComparison.Ordinal) == true);

    private static Canvas2DSceneItem FindNodeLabelResizeZone(
        Canvas2DScene scene,
        string role) =>
        scene.Items.Single(item =>
            item.Origin.StableSourceKey?.StartsWith(
                $"node-label-resize-zone:{role}:",
                StringComparison.Ordinal) == true);

    private static Canvas2DSceneItem FindNodeLabelPreview(Canvas2DScene scene) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Any(related =>
                StringComparer.Ordinal.Equals(
                    related.Value,
                    FindNodeLabelBody(scene).Id.Value)));

    private static ElementConnectorAnchorPolicyRegistry PolicyProvider(
        ElementConnectorAnchorPolicy policy) =>
        new ElementConnectorAnchorPolicyRegistry(
        [
            new ElementConnectorAnchorPolicyRegistration(
                new SemanticTypeId("test:session:node"),
                policy),
        ]);

    private static Canvas2DScene ReplaceSceneItem(
        Canvas2DScene scene,
        Canvas2DSceneItem replacement) =>
        new(
            scene.DocumentId,
            scene.SourceRevision,
            scene.LayoutAlgorithmId,
            scene.RoutingAlgorithmId,
            scene.Configuration,
            scene.Contributors,
            scene.Viewport,
            scene.ViewportTransform,
            scene.ActiveToolId,
            scene.FocusTargetId,
            scene.ToolState,
            scene.ContributorMetadata,
            scene.Items.Select(item => item.Id == replacement.Id ? replacement : item),
            scene.Diagnostics);

    private static Canvas2DSceneItem CopySceneItem(
        Canvas2DSceneItem source,
        RectD bounds,
        IEnumerable<KeyValuePair<string, PropertyValue>> metadata) =>
        new(
            source.Id,
            source.Layer,
            source.ZIndex,
            source.Geometry,
            source.Origin,
            source.Transform,
            source.Clip,
            source.Style,
            source.IsVisible,
            source.HitTestPolicy,
            source.PersistentAppearance,
            metadata,
            bounds);

    private static IEnumerable<KeyValuePair<string, PropertyValue>> WithMetadata(
        Canvas2DSceneItem source,
        string key,
        PropertyValue value) =>
        source.Metadata
            .Where(property => !StringComparer.Ordinal.Equals(property.Key, key))
            .Append(new KeyValuePair<string, PropertyValue>(key, value));

    private static Canvas2DSceneTestData WithFixedSizeNode(
        Canvas2DSceneTestData inputs,
        int nodeIndex = 0)
    {
        var source = inputs.Graph.Nodes[nodeIndex];
        var replacement = new ProjectedNode(
            source.Source,
            source.PlacementHint,
            source.SemanticProperties,
            source.ProjectedProperties,
            source.LayoutHints,
            source.RoutingHints,
            source.AlgorithmMetadata,
            NodeGeometryInteractionPolicy.AttachedBoundaryMoveFixedSize);
        return inputs.WithGraph(new ProjectedGraph(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            inputs.Graph.Nodes.Select(node => node.Id == source.Id ? replacement : node),
            inputs.Graph.Edges,
            inputs.Graph.Groups,
            inputs.Graph.Ports,
            inputs.Graph.Labels));
    }

    private static Canvas2DSceneTestData WithSinglePersistentConnectorAnchor(
        Canvas2DSceneTestData inputs)
    {
        var ownerId = VisualId(inputs, 0);
        var visualModel = new VisualModelSnapshot(
            inputs.VisualModel.DocumentId,
            inputs.VisualModel.Revision,
            inputs.VisualModel.VisualStates.Select(visual =>
            {
                if (visual.Id != ownerId)
                {
                    return visual;
                }

                return new VisualStateSnapshot(
                    visual.Id,
                    visual.SemanticElementId,
                    visual.Position,
                    visual.Size,
                    visual.PlacementMode,
                    visual.Route,
                    visual.Properties,
                    [visual.ConnectorAnchors[0]],
                    visual.SourceAnchorId,
                    visual.TargetAnchorId);
            }));
        return inputs.WithVisualModel(visualModel);
    }

    private static Inceptus.DocumentEngine.Runtime.Documents.Document
        CreateDocumentWithVisualHint(
            Canvas2DSceneTestData inputs,
            VisualStateId visualStateId,
            PointD position,
            SizeD size,
            VisualPlacementMode placementMode)
    {
        var source = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        var visualModel = new VisualModelSnapshot(
            source.DocumentId,
            source.Revision,
            source.VisualModel.VisualStates.Select(visual =>
                visual.Id == visualStateId
                    ? new VisualStateSnapshot(
                        visual.Id,
                        visual.SemanticElementId,
                        position,
                        size,
                        placementMode,
                        visual.Route,
                        visual.Properties)
                    : visual));
        var reconstructed = Inceptus.DocumentEngine.Runtime.Documents.DocumentReconstructor
            .Reconstruct(new DocumentSnapshot(
                source.SemanticModel,
                visualModel,
                source.Metadata));
        return Assert.IsType<Inceptus.DocumentEngine.Runtime.Documents.Document>(
            reconstructed.Document);
    }

    private static EditingSessionPipelineResult SuccessAtVisualPositions(
        Canvas2DSceneTestData inputs,
        DocumentSnapshot snapshot,
        EditorStateSnapshot editorState)
    {
        var artifacts = EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision);
        var nodes = artifacts.LayoutResult.Computation.Nodes.Select(node =>
        {
            var projected = artifacts.ProjectedGraph.Nodes.Single(candidate =>
                candidate.Id == node.ProjectedObjectId);
            if (projected.Source.VisualStateId is not { } visualStateId ||
                !snapshot.VisualModel.TryGetVisualState(visualStateId, out var visual) ||
                visual is null)
            {
                return node;
            }

            return new LayoutNodeGeometry(
                node.ProjectedObjectId,
                new RectD(
                    visual.Position.X,
                    visual.Position.Y,
                    visual.Size.Width,
                    visual.Size.Height),
                Matrix2D.CreateTranslation(visual.Position.X, visual.Position.Y));
        });
        var layout = new LayoutResult(
            artifacts.ProjectedGraph.DocumentId,
            snapshot.Revision,
            artifacts.LayoutResult.AlgorithmId,
            new LayoutComputation(
                nodes,
                artifacts.LayoutResult.Computation.Groups,
                artifacts.LayoutResult.Computation.Metadata),
            artifacts.LayoutResult.Diagnostics);
        var nodesById = layout.Computation.Nodes.ToDictionary(
            static node => node.ProjectedObjectId);
        var routes = artifacts.ProjectedGraph.Edges.Select(edge =>
        {
            var source = nodesById[edge.SourceNodeId].Bounds;
            var target = nodesById[edge.TargetNodeId].Bounds;
            var sourceAnchor = new PointD(source.Right, source.Top + (source.Height / 2d));
            var targetAnchor = new PointD(target.Left, target.Top + (target.Height / 2d));
            return new RoutedConnectorGeometry(
                edge.Id,
                sourceAnchor,
                targetAnchor,
                [new PointD(
                    (sourceAnchor.X + targetAnchor.X) / 2d,
                    (sourceAnchor.Y + targetAnchor.Y) / 2d)]);
        });
        var routing = new RoutingResult(
            artifacts.ProjectedGraph.DocumentId,
            snapshot.Revision,
            layout.AlgorithmId,
            artifacts.RoutingResult.RoutingAlgorithmId,
            new RoutingComputation(routes),
            artifacts.RoutingResult.Diagnostics);
        var revised = new EditingSessionPipelineArtifacts(
            artifacts.ProjectedGraph,
            layout,
            routing);
        var build = new Canvas2DSceneBuilder().Build(
            revised.ProjectedGraph,
            revised.LayoutResult,
            revised.RoutingResult,
            snapshot.VisualModel,
            editorState);
        if (build.Scene is null)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                build.Diagnostics.Select(static diagnostic =>
                    $"{diagnostic.Code}: {diagnostic.Message}")));
        }

        return EditingSessionPipelineResult.Success(revised, build.Scene);
    }

    private static SceneObjectId NodeId(Canvas2DSceneTestData inputs, int index) =>
        Canvas2DSceneObjectIdentity.ForProjected(inputs.Graph.Nodes[index].Id, "node");

    private static (Canvas2DScene Scene, SceneObjectId ConnectorId, VisualStateId VisualStateId)
        RouteContextScene(
            IEnumerable<PointD> route,
            IEnumerable<PointD>? displayRoute = null,
            IEnumerable<PointD>? editableRoute = null)
    {
        var points = route.ToArray();
        var displayPoints = displayRoute?.ToArray() ?? points;
        var editablePoints = editableRoute?.ToArray();
        var connectorId = new SceneObjectId("test:scene:route-context");
        var visualStateId = new VisualStateId("test:visual:route-context");
        var connector = new Canvas2DSceneItem(
            connectorId,
            Canvas2DSceneLayer.Connector,
            zIndex: 0,
            Canvas2DSceneGeometry.Path(displayPoints),
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.VisualState,
                visualStateId: visualStateId,
                stableSourceKey: "test:route-context"),
            hitTestPolicy: new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Stroke, 5d),
            metadata: displayRoute is null && editableRoute is null
                ? null
                : Canvas2DConnectorPathMetadata.CreateProperties(points, editablePoints));
        var scene = new Canvas2DScene(
            new DocumentId("test:route-context"),
            DocumentRevision.Zero,
            new AlgorithmId("test:layout"),
            new AlgorithmId("test:routing"),
            Canvas2DSceneConfiguration.Default,
            [],
            ViewportSnapshot.Default,
            Matrix2D.Identity,
            activeToolId: null,
            focusTargetId: null,
            PropertyMap.Empty,
            PropertyMap.Empty,
            [connector]);
        return (scene, connectorId, visualStateId);
    }

    private static VisualStateId VisualId(Canvas2DSceneTestData inputs, int index) =>
        inputs.Graph.Nodes[index].Source.VisualStateId!;

    private static SceneObjectId ConnectorId(Canvas2DSceneTestData inputs) =>
        Canvas2DSceneObjectIdentity.ForProjected(
            Assert.Single(inputs.Graph.Edges).Id,
            "connector");

    private static VisualStateId ConnectorVisualId(Canvas2DSceneTestData inputs) =>
        Assert.Single(inputs.Graph.Edges).Source.VisualStateId!;

    private static PointD Center(RectD bounds) =>
        new(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static void AssertBoundsApproximately(RectD expected, RectD actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Width, actual.Width, 9);
        Assert.Equal(expected.Height, actual.Height, 9);
    }

    private static Canvas2DSceneItem FindResizeHandle(
        Canvas2DScene scene,
        SceneObjectId targetId) =>
        scene.Items.Single(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "resize-handle:southeast:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(targetId));

    private static Canvas2DSceneItem FindResizeInteraction(
        Canvas2DScene scene,
        SceneObjectId targetId,
        string role) =>
        scene.Items.Single(item =>
            (item.Origin.StableSourceKey?.StartsWith(
                $"resize-handle:{role}:",
                StringComparison.Ordinal) == true ||
             item.Origin.StableSourceKey?.StartsWith(
                 $"resize-edge-zone:{role}:",
                 StringComparison.Ordinal) == true) &&
            item.Origin.RelatedSceneObjectIds.Contains(targetId));

    private static Canvas2DSceneItem FindResizePreview(
        Canvas2DScene scene,
        SceneObjectId targetId) =>
        scene.Items.Single(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(targetId));

    private static Canvas2DSceneItem FindMovePreview(
        Canvas2DScene scene,
        VisualStateId targetId) =>
        scene.Items.Single(item =>
            item.Origin.VisualStateId == targetId &&
            item.Origin.StableSourceKey?.StartsWith(
                "move-preview:",
                StringComparison.Ordinal) == true &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle);

    private static Canvas2DSceneItem FindRouteHandle(
        Canvas2DScene scene,
        SceneObjectId targetId) =>
        scene.Items.Single(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "route-bend-handle:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(targetId));

    private static Canvas2DSceneItem FindRoutePreview(
        Canvas2DScene scene,
        SceneObjectId targetId) =>
        scene.Items.Single(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "route-preview:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(targetId));

    private static EditorStateSnapshot RichEditorState(VisualStateId selected) =>
        new(
            selection: [selected],
            activeToolId: "test:pointer",
            focusTargetId: "test:focus",
            viewport: new ViewportSnapshot(1.25d, new VectorD(15d, -4d)),
            activeGesture: new EditorGestureSnapshot(
                "test:gesture",
                "preview",
                new PointD(1d, 2d),
                new PointD(3d, 4d)),
            temporaryFeedback:
            [
                new EditorFeedbackSnapshot(
                    "test:feedback",
                    "guide",
                    new RectD(1d, 2d, 3d, 4d)),
            ],
            toolState:
            [
                new KeyValuePair<string, PropertyValue>(
                    "test:value",
                    PropertyValue.FromText("preserved")),
            ]);

    private static PointD ToCss(ViewportSnapshot viewport, PointD documentPoint) =>
        new(
            (documentPoint.X * viewport.Zoom) + viewport.Pan.X,
            (documentPoint.Y * viewport.Zoom) + viewport.Pan.Y);

    private static void AssertUnrelatedStatePreserved(
        EditorStateSnapshot expected,
        EditorStateSnapshot actual)
    {
        Assert.Equal(expected.ActiveToolId, actual.ActiveToolId);
        Assert.Equal(expected.FocusTargetId, actual.FocusTargetId);
        Assert.Same(expected.Viewport, actual.Viewport);
        Assert.Same(expected.ActiveGesture, actual.ActiveGesture);
        Assert.Equal(
            Assert.Single(expected.TemporaryFeedback),
            Assert.Single(actual.TemporaryFeedback));
        Assert.Equal(expected.ToolState, actual.ToolState);
    }

    private static int RenderCount(Canvas2DRendererTestExecution execution) =>
        execution.Calls.Count(static call => StringComparer.Ordinal.Equals(call, "render"));

    private sealed record InteractionTestContext(
        EditingSession Session,
        ControlledEditingSessionPipeline Pipeline,
        Canvas2DRendererTestExecution Execution,
        EditingSessionState InitialState,
        Inceptus.DocumentEngine.Runtime.Documents.Document Document);
}
