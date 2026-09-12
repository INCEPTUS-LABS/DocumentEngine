using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class Canvas2DSpatialPoolInteractionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SpatialPlacementUsesClickedDestinationAndOneAtomicHistoryEntry(
        bool secondPool, bool subProcess)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Controller.SelectSemanticSceneTargetAsync(Fixture.PoolA);
        var before = fixture.State;
        var destinationId = secondPool ? Fixture.PoolB : Fixture.PoolA;
        var region = before.CurrentScene!.SpatialPresentationPlan!.Regions.Single(
            region => region.ContainerSemanticElementId == destinationId);
        var scenePoint = Center(region.Bounds);
        var localPoint = region.MapSceneToLocal(scenePoint);
        fixture.ToolboxSelection.Select(new ToolboxItemId(
            subProcess ? "bpmn:toolbox:sub-process" : "bpmn:toolbox:task"));
        var placed = await fixture.Placement.TryPlaceAtCssPointAsync(
            fixture.Session, fixture.Css(scenePoint));
        Assert.True(placed.IsCommitted, Diagnostics(placed.Diagnostics));
        var visual = fixture.Visual(Assert.IsType<VisualStateId>(placed.CreatedVisualStateId));
        Assert.Equal(new PointD(localPoint.X - visual.Size.Width / 2d,
            localPoint.Y - visual.Size.Height / 2d), visual.Position);
        Assert.Equal(before.ActiveScopeId, fixture.Document.SemanticModel.GetScope(visual.SemanticElementId).Id);
        Assert.Equal(destinationId, Assert.Single(fixture.Document.SemanticModel.ProfileAssignments,
            assignment => assignment.SemanticElementId == visual.SemanticElementId).ContainerSemanticElementId);
        Assert.Equal(before.DocumentRevision.Increment(), fixture.State.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
        Assert.Null(fixture.State.EditorState.SemanticSceneSelection);
        Assert.Equal(visual.Id, Assert.Single(fixture.State.EditorState.Selection));
        if (subProcess)
        {
            Assert.Equal(before.ActiveScopeId, Assert.Single(fixture.Document.SemanticModel.NestedScopes,
                scope => scope.OwnerSemanticElementId == visual.SemanticElementId).ParentScopeId);
        }
        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await fixture.Session.WaitForIdleAsync();
        Assert.DoesNotContain(fixture.Document.SemanticModel.Elements, element => element.Id == visual.SemanticElementId);
        Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
        await fixture.Session.WaitForIdleAsync();
        Assert.Equal(visual, fixture.Visual(visual.Id));
        Assert.Equal(destinationId, Assert.Single(fixture.Document.SemanticModel.ProfileAssignments,
            assignment => assignment.SemanticElementId == visual.SemanticElementId).ContainerSemanticElementId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MovingBetweenPoolAndUnassignedRegionsPersistsOnlyCanonicalGeometryAndAssignment(
        bool unassigned)
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = fixture.State;
        var original = fixture.Visual(Fixture.TaskB);
        var region = before.CurrentScene!.SpatialPresentationPlan!.Regions.Single(
            region => region.ContainerSemanticElementId == (unassigned ? null : Fixture.PoolA));
        var destination = region.MapLocalToScene(new PointD(220d, 60d));
        var start = Center(fixture.Node(Fixture.TaskB).Bounds);
        var expected = region.MapSceneToLocal(destination) -
            new VectorD(original.Size.Width / 2d, original.Size.Height / 2d);
        await fixture.DragAsync(start, destination - start);
        var moved = fixture.Visual(Fixture.TaskB);
        Assert.Equal(expected, moved.Position);
        Assert.Equal(original.Id, moved.Id);
        Assert.Equal(original.SemanticElementId, moved.SemanticElementId);
        Assert.Equal(before.ActiveScopeId, fixture.Document.SemanticModel.GetScope(moved.SemanticElementId).Id);
        var assignment = fixture.Document.SemanticModel.ProfileAssignments.SingleOrDefault(
            assignment => assignment.SemanticElementId == moved.SemanticElementId);
        Assert.Equal(unassigned ? null : Fixture.PoolA, assignment?.ContainerSemanticElementId);
        Assert.Equal(before.DocumentRevision.Increment(), fixture.State.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await fixture.Session.WaitForIdleAsync();
        Assert.Equal(original, fixture.Visual(Fixture.TaskB));
        Assert.Equal(Fixture.PoolB, Assert.Single(fixture.Document.SemanticModel.ProfileAssignments,
            assignment => assignment.SemanticElementId == original.SemanticElementId).ContainerSemanticElementId);
        Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
        await fixture.Session.WaitForIdleAsync();
        Assert.Equal(moved, fixture.Visual(Fixture.TaskB));
    }

    [Fact]
    public async Task MovingIntoCollapsedPoolRejectsTheWholeEdit()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True((await fixture.Session.UpdateModelProfileElementViewStateAsync(
            fixture.State.ModelProfileElementViewState.WithCollapsed(
                OrganizationalModelProfile.Id, Fixture.PoolA, true))).Succeeded);
        var before = fixture.State;
        var original = fixture.Document;
        var source = fixture.NodePoint(Fixture.TaskB);
        var region = before.CurrentScene!.SpatialPresentationPlan!.Regions.Single(
            region => region.ContainerSemanticElementId == Fixture.PoolA);
        var destination = Center(region.Bounds);
        await fixture.Controller.PointerPressedAsync(fixture.Pointer(source, buttons: 1));
        await fixture.Controller.PointerMovedAsync(fixture.Pointer(destination, buttons: 1));
        var result = await fixture.Controller.PointerReleasedAsync(fixture.Pointer(destination));
        Assert.NotEqual(Canvas2DInteractionStatus.Committed, result.Status);
        Assert.Same(original, fixture.Document);
        Assert.Equal(before.DocumentRevision, fixture.State.DocumentRevision);
        Assert.Equal(before.HistoryStatus, fixture.State.HistoryStatus);
        Assert.Null(fixture.State.EditorState.ActiveGesture);
        fixture.AssertReady();
    }

    [Fact]
    public async Task PlacementOutsideSpatialRegionsIsRejectedWithoutMutation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = fixture.State;
        var document = fixture.Document;
        fixture.ToolboxSelection.Select(new ToolboxItemId("bpmn:toolbox:task"));
        var result = await fixture.Placement.TryPlaceAtCssPointAsync(
            fixture.Session, fixture.Css(new PointD(100000d, 100000d)));
        Assert.False(result.IsCommitted);
        Assert.Same(document, fixture.Document);
        Assert.Equal(before.HistoryStatus, fixture.State.HistoryStatus);
        Assert.Equal(before.DocumentRevision, fixture.State.DocumentRevision);
    }

    [Fact]
    public async Task ToolboxPlacementWithoutProfilePresentationUsesActiveScope()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ExecuteAsync(state => new SetModelProfileAvailabilityCommand(
            state.DocumentId, state.DocumentRevision,
            [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, false)]));
        var before = fixture.State;
        Assert.Null(before.CurrentScene!.SpatialPresentationPlan);
        fixture.ToolboxSelection.Select(new ToolboxItemId("bpmn:toolbox:task"));
        var point = new PointD(350d, 250d);
        var placed = await fixture.Placement.TryPlaceAtCssPointAsync(fixture.Session, fixture.Css(point));
        Assert.True(placed.IsCommitted, Diagnostics(placed.Diagnostics));
        var visual = fixture.Visual(Assert.IsType<VisualStateId>(placed.CreatedVisualStateId));
        Assert.True(fixture.Document.SemanticModel.TryGetScope(visual.SemanticElementId, out var scope));
        Assert.Equal(before.ActiveScopeId, scope!.Id);
        Assert.Equal(before.ActiveScopeId, fixture.State.ActiveScopeId);
        fixture.AssertReady();
    }

    [Fact]
    public async Task PooledTaskSelectionMoveAndResizeUseOwningScopeWithoutNavigation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = fixture.State;
        var originalMain = fixture.Visual(BpmnDemoPipeline.TaskVisualId);
        var originalPeer = fixture.Visual(Fixture.TaskB);
        await fixture.Controller.SelectSemanticSceneTargetAsync(Fixture.PoolA);

        var selected = await fixture.Controller.PointerActivatedAsync(
            fixture.Css(fixture.NodePoint(Fixture.TaskB)));

        Assert.Equal(Canvas2DInteractionStatus.Updated, selected.Status);
        Assert.Equal(Fixture.TaskB, Assert.Single(fixture.State.EditorState.Selection));
        Assert.Null(fixture.State.EditorState.SemanticSceneSelection);
        Assert.Equal(original.ActiveScopeId, fixture.State.ActiveScopeId);
        Assert.Equal(original.HistoryStatus, fixture.State.HistoryStatus);

        var movement = new VectorD(36d, 24d);
        await fixture.DragAsync(fixture.NodePoint(Fixture.TaskB), movement);
        Assert.Equal(originalPeer.Position + movement, fixture.Visual(Fixture.TaskB).Position);
        Assert.Equal(originalMain, fixture.Visual(BpmnDemoPipeline.TaskVisualId));
        Assert.Equal(original.DocumentRevision.Increment(), fixture.State.DocumentRevision);
        Assert.Equal(original.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);

        var beforeResize = fixture.Visual(Fixture.TaskB);
        var resize = fixture.State.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == Fixture.TaskB &&
            Text(item, Canvas2DResizeGestureMetadata.HandleRole) == "southeast");
        await fixture.DragAsync(Center(resize.Bounds), new VectorD(28d, 18d));
        Assert.Equal(beforeResize.Position, fixture.Visual(Fixture.TaskB).Position);
        Assert.Equal(new SizeD(beforeResize.Size.Width + 28d, beforeResize.Size.Height + 18d),
            fixture.Visual(Fixture.TaskB).Size);
        Assert.Equal(original.ActiveScopeId, fixture.State.ActiveScopeId);
        Assert.Equal(original.HistoryStatus.EntryCount + 2, fixture.State.HistoryStatus.EntryCount);
        fixture.AssertReady();
    }

    [Fact]
    public async Task CrossPoolAnchorSequenceFlowAndRouteEditingUseOneActiveScope()
    {
        await using var fixture = await Fixture.CreateAsync();
        var initial = fixture.State;
        await fixture.SelectAsync(Fixture.TaskB);
        var node = fixture.Node(Fixture.TaskB);
        var acquired = await fixture.Controller.PointerContextMenuAsync(
            fixture.Css(new PointD(node.Bounds.Right, node.Bounds.Top + 24d)));
        var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(acquired.ConnectorAnchorContextAction);
        Assert.Equal(Canvas2DConnectorAnchorContextActionKind.AddAnchor, action.Kind);
        var sourceAnchor = new ConnectorAnchorId("n101a:source-b");
        var current = fixture.State;
        var sourceItem = current.CurrentScene!.Items.Single(item => item.Id == action.SourceSceneObjectId);
        var committed = await fixture.Session.ExecuteForSceneTargetAsync(
            new AddConnectorAnchorCommand(current.DocumentId, current.DocumentRevision,
                Fixture.TaskB, sourceAnchor, action.Side, ConnectorAnchorRole.Source, action.InsertionIndex),
            current.CurrentScene, current.Generation, sourceItem.Id, sourceItem.SpatialRegion);
        Assert.True(committed.IsCommitted, Diagnostics(committed.Diagnostics));
        await fixture.Session.WaitForIdleAsync();
        Assert.Equal(initial.ActiveScopeId, fixture.State.ActiveScopeId);

        var sourceHandle = fixture.Anchor(Fixture.TaskB, sourceAnchor);
        var beforeFlow = fixture.State;
        await fixture.Controller.PointerPressedAsync(fixture.Pointer(Center(sourceHandle.Bounds), buttons: 1));
        var targetPoint = fixture.NodePoint(Fixture.TaskB2);
        await fixture.Controller.PointerMovedAsync(fixture.Pointer(targetPoint, buttons: 1));
        var completed = await fixture.Controller.PointerReleasedAsync(fixture.Pointer(targetPoint));
        Assert.Equal(Canvas2DInteractionStatus.Committed, completed.Status);
        Assert.DoesNotContain(completed.Diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(beforeFlow.DocumentRevision.Increment(), fixture.State.DocumentRevision);
        Assert.Equal(beforeFlow.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
        Assert.Equal(initial.ActiveScopeId, fixture.State.ActiveScopeId);
        var flowId = Assert.Single(fixture.State.EditorState.Selection);
        var flow = fixture.Visual(flowId);
        Assert.Equal(fixture.State.ActiveScopeId, fixture.Document.SemanticModel.GetScope(flow.SemanticElementId).Id);
        Assert.Empty(flow.Route);
        var connector = fixture.State.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == flowId &&
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Path &&
            item.Metadata.ContainsKey(Canvas2DConnectorPathMetadata.LogicalPathPointCount));
        var path = Canvas2DConnectorPathMetadata.Resolve(connector)
            .Select(connector.Transform.TransformPoint).ToArray();
        Canvas2DConnectorRouteContextAction? acquiredRouteAction = null;
        foreach (var segment in Enumerable.Range(0, path.Length - 1)
                     .OrderByDescending(index => Math.Abs(path[index + 1].X - path[index].X) +
                         Math.Abs(path[index + 1].Y - path[index].Y)))
        {
            // A crowded demo may place a node or a higher connector over one segment midpoint.
            // Use an actual hit on this connector rather than assuming the longest midpoint is free.
            var routePoint = new PointD((path[segment].X + path[segment + 1].X) / 2d,
                (path[segment].Y + path[segment + 1].Y) / 2d);
            var routeContext = await fixture.Controller.PointerContextMenuAsync(fixture.Css(routePoint));
            if (routeContext.ConnectorRouteContextAction is { } candidate && candidate.TargetVisualStateId == flowId)
            {
                acquiredRouteAction = candidate;
                break;
            }
        }
        var routeAction = Assert.IsType<Canvas2DConnectorRouteContextAction>(acquiredRouteAction);
        Assert.True(routeAction.TryResolveTargetRoute(fixture.State.CurrentScene!, flow.Route, out var route));
        var beforeRoute = fixture.State;
        var routeTarget = beforeRoute.CurrentScene!.Items.Single(item => item.Id == routeAction.SourceSceneObjectId);
        var routeCommit = await fixture.Session.ExecuteForSceneTargetAsync(
            new UpdateConnectionRouteCommand(beforeRoute.DocumentId, beforeRoute.DocumentRevision, flowId, route),
            beforeRoute.CurrentScene, beforeRoute.Generation, routeTarget.Id, routeTarget.SpatialRegion);
        Assert.True(routeCommit.IsCommitted, Diagnostics(routeCommit.Diagnostics));
        await fixture.Session.WaitForIdleAsync();
        Assert.Equal(route, fixture.Visual(flowId).Route);
        var bend = fixture.State.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == flowId &&
            item.Metadata.ContainsKey(Canvas2DRouteGestureMetadata.BendIndex));
        var bendMovement = new VectorD(0d, 34d);
        await fixture.DragAsync(Center(bend.Bounds), bendMovement);
        Assert.Equal(route[1] + bendMovement, fixture.Visual(flowId).Route[1]);
        Assert.Equal(beforeRoute.HistoryStatus.EntryCount + 2, fixture.State.HistoryStatus.EntryCount);
        Assert.Equal(initial.ActiveScopeId, fixture.State.ActiveScopeId);
        fixture.AssertReady();
    }

    [Fact]
    public async Task PooledBoundaryUsesItsOwnerScopeForSelectionAnchorAndAttachedMove()
    {
        await using var fixture = await Fixture.CreateAsync();
        var boundaryVisualId = new VisualStateId("n101a:boundary:visual");
        var owner = fixture.Visual(Fixture.TaskB);
        await fixture.ExecuteAsync(state => new CreateBpmnTimerBoundaryEventCommand(
            state.DocumentId, state.DocumentRevision, new SemanticElementId("n101a:boundary"),
            boundaryVisualId, owner.SemanticElementId, BoundaryAttachmentSide.Bottom, 0.4d,
            new RectD(owner.Position.X, owner.Position.Y, owner.Size.Width, owner.Size.Height),
            "Timer", "PT5M", targetScopeId: state.ActiveScopeId));
        var before = fixture.State;
        var boundary = fixture.Node(boundaryVisualId);
        await fixture.Controller.PointerActivatedAsync(fixture.Css(Center(boundary.Bounds)));
        Assert.Equal(boundaryVisualId, Assert.Single(fixture.State.EditorState.Selection));
        Assert.DoesNotContain(fixture.State.CurrentScene!.Items, item =>
            item.Origin.VisualStateId == boundaryVisualId &&
            item.Metadata.ContainsKey(Canvas2DResizeGestureMetadata.HandleRole));
        boundary = fixture.Node(boundaryVisualId);
        var context = await fixture.Controller.PointerContextMenuAsync(fixture.Css(
            new PointD(boundary.Bounds.Right, boundary.Bounds.Top + boundary.Bounds.Height / 2d)));
        var anchor = Assert.IsType<Canvas2DConnectorAnchorContextAction>(context.ConnectorAnchorContextAction);
        Assert.Equal(ConnectorAnchorRoleCapability.Source, anchor.AllowedRoles);
        var stateAtAnchor = fixture.State;
        var anchorTarget = stateAtAnchor.CurrentScene!.Items.Single(item => item.Id == anchor.SourceSceneObjectId);
        var added = await fixture.Session.ExecuteForSceneTargetAsync(new AddConnectorAnchorCommand(
                stateAtAnchor.DocumentId, stateAtAnchor.DocumentRevision, boundaryVisualId,
                new ConnectorAnchorId("n101a:boundary:source"), anchor.Side,
                ConnectorAnchorRole.Source, anchor.InsertionIndex),
            stateAtAnchor.CurrentScene, stateAtAnchor.Generation,
            anchorTarget.Id, anchorTarget.SpatialRegion);
        Assert.True(added.IsCommitted, Diagnostics(added.Diagnostics));
        await fixture.Session.WaitForIdleAsync();
        await fixture.DragAsync(Center(fixture.Node(boundaryVisualId).Bounds), new VectorD(24d, 0d));
        Assert.Equal(owner, fixture.Visual(Fixture.TaskB));
        var moved = fixture.Visual(boundaryVisualId);
        Assert.NotNull(moved.BoundaryAttachment);
        Assert.Equal(BoundaryAttachmentSide.Bottom, moved.BoundaryAttachment.Side);
        Assert.Equal(0.4d + 24d / owner.Size.Width, moved.BoundaryAttachment.PositionOnSide, 10);
        Assert.Equal(fixture.State.ActiveScopeId, fixture.Document.SemanticModel.GetScope(moved.SemanticElementId).Id);
        Assert.Equal(before.ActiveScopeId, fixture.State.ActiveScopeId);
        Assert.Equal(before.HistoryStatus.EntryCount + 2, fixture.State.HistoryStatus.EntryCount);
        fixture.AssertReady();
    }

    [Fact]
    public async Task UnassignedMeasuredGatewayLabelMoveAndResizePersistLocalOverrideAndKeepSceneReady()
    {
        await using var fixture = await Fixture.CreateAsync();
        var gatewayVisualId = new VisualStateId("n101a:gateway-label:visual");
        await fixture.ExecuteAsync(state => new CreateBpmnExclusiveGatewayCommand(
            state.DocumentId, state.DocumentRevision, new SemanticElementId("n101a:gateway-label"),
            gatewayVisualId, new PointD(180d, 360d), new SizeD(60d, 60d),
            "G", "Peer gateway", VisualPlacementMode.Pinned, targetScopeId: state.ActiveScopeId));
        await fixture.SelectAsync(gatewayVisualId);
        var node = fixture.Node(gatewayVisualId);
        var label = fixture.State.CurrentScene!.Items.First(item =>
            item.Origin.VisualStateId == gatewayVisualId &&
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Metadata.ContainsKey(Canvas2DNodeLabelGestureMetadata.InteractionCapable));
        var localNodeBounds = node.SpatialRegion!.MapSceneToLocal(node.Bounds);
        var localLabelBounds = label.SpatialRegion!.MapSceneToLocal(label.Bounds);
        var before = fixture.State;
        var visualBefore = fixture.Visual(gatewayVisualId);
        var translation = new VectorD(23d, 15d);
        await fixture.DragAsync(Center(label.Bounds), translation);
        Assert.True(NodeLabelVisualOverride.TryRead(fixture.Visual(gatewayVisualId).Properties, out var moved));
        Assert.Equal(NodeLabelVisualOverride.FromBounds(localNodeBounds, localLabelBounds.Translate(translation)), moved);
        var zone = fixture.State.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == gatewayVisualId &&
            Text(item, Canvas2DNodeLabelGestureMetadata.ResizeDirection) == "east");
        await fixture.DragAsync(Center(zone.Bounds), new VectorD(24d, 0d));
        Assert.True(NodeLabelVisualOverride.TryRead(fixture.Visual(gatewayVisualId).Properties, out var resized));
        Assert.Equal(moved!.Width + 24d, resized!.Width);
        Assert.Equal(moved.Height, resized.Height);
        Assert.Equal(visualBefore.Position, fixture.Visual(gatewayVisualId).Position);
        Assert.Equal(visualBefore.Size, fixture.Visual(gatewayVisualId).Size);
        Assert.Equal(before.ActiveScopeId, fixture.State.ActiveScopeId);
        Assert.Equal(before.HistoryStatus.EntryCount + 2, fixture.State.HistoryStatus.EntryCount);
        fixture.AssertReady();
    }

    [Fact]
    public async Task CrossPoolConnectorEndpointReconnectionUsesExactPresentedConnector()
    {
        await using var fixture = await Fixture.CreateAsync();
        var sourceAnchor = new ConnectorAnchorId("n101a:reconnect:source");
        var oldTargetAnchor = new ConnectorAnchorId("n101a:reconnect:old-target");
        var newTargetAnchor = new ConnectorAnchorId("n101a:reconnect:new-target");
        await fixture.ExecuteAsync(state => new AddConnectorAnchorCommand(state.DocumentId,
            state.DocumentRevision, Fixture.TaskB, sourceAnchor, ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source, 0));
        await fixture.ExecuteAsync(state => new AddConnectorAnchorCommand(state.DocumentId,
            state.DocumentRevision, Fixture.TaskB2, oldTargetAnchor, ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target, 0));
        await fixture.ExecuteAsync(state => new AddConnectorAnchorCommand(state.DocumentId,
            state.DocumentRevision, Fixture.TaskB2, newTargetAnchor, ConnectorAnchorSide.Bottom,
            ConnectorAnchorRole.Target, 0));
        var flowSemanticId = new SemanticElementId("n101a:reconnect:flow");
        var flowVisualId = new VisualStateId("n101a:reconnect:flow:visual");
        await fixture.ExecuteAsync(state => new CreateBpmnSequenceFlowCommand(state.DocumentId,
            state.DocumentRevision, flowSemanticId, flowVisualId,
            fixture.Visual(Fixture.TaskB).SemanticElementId, fixture.Visual(Fixture.TaskB2).SemanticElementId,
            sourceAnchor, oldTargetAnchor));
        var connector = fixture.State.CurrentScene!.Items.First(item =>
            item.Origin.VisualStateId == flowVisualId && item.Layer == Canvas2DSceneLayer.Connector &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Path);
        var path = Canvas2DConnectorPathMetadata.Resolve(connector).Select(connector.Transform.TransformPoint).ToArray();
        var point = new PointD((path[0].X + path[1].X) / 2d, (path[0].Y + path[1].Y) / 2d);
        await fixture.Controller.PointerActivatedAsync(fixture.Css(point));
        Assert.Equal(flowVisualId, Assert.Single(fixture.State.EditorState.Selection));
        var handle = fixture.State.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == flowVisualId &&
            Text(item, Canvas2DConnectorEndpointMetadata.HandleRole) == Canvas2DConnectorEndpointMetadata.EndEndpointRole);
        var before = fixture.State;
        var started = await fixture.Controller.PointerPressedAsync(fixture.Pointer(Center(handle.Bounds), buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, started.Status);
        var target = fixture.Anchor(Fixture.TaskB2, newTargetAnchor);
        var targetPoint = Center(target.Bounds);
        await fixture.Controller.PointerMovedAsync(fixture.Pointer(targetPoint, buttons: 1));
        var completed = await fixture.Controller.PointerReleasedAsync(fixture.Pointer(targetPoint));
        Assert.Equal(Canvas2DInteractionStatus.Committed, completed.Status);
        Assert.DoesNotContain(completed.Diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(newTargetAnchor, fixture.Visual(flowVisualId).TargetAnchorId);
        Assert.Equal(sourceAnchor, fixture.Visual(flowVisualId).SourceAnchorId);
        Assert.Equal(flowSemanticId, fixture.Visual(flowVisualId).SemanticElementId);
        Assert.Equal(before.DocumentRevision.Increment(), fixture.State.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
        Assert.Equal(before.ActiveScopeId, fixture.State.ActiveScopeId);
        fixture.AssertReady();
    }

    [Fact]
    public async Task VisibilityRecompositionCannotCommitAnInFlightMove()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = fixture.Visual(Fixture.TaskB);
        var before = fixture.State;
        var point = fixture.NodePoint(Fixture.TaskB);
        await fixture.Controller.PointerPressedAsync(fixture.Pointer(point, buttons: 1));
        await fixture.Controller.PointerMovedAsync(fixture.Pointer(point + new VectorD(25d, 15d), buttons: 1));
        var hidden = await fixture.Session.UpdateModelProfileViewStateAsync(
            fixture.State.ModelProfileViewState.WithPreferredVisibility(OrganizationalModelProfile.Id, false));
        Assert.True(hidden.Succeeded, Diagnostics(hidden.Diagnostics));
        var released = await fixture.Controller.PointerReleasedAsync(fixture.Pointer(point + new VectorD(25d, 15d)));
        Assert.NotEqual(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Equal(original, fixture.Visual(Fixture.TaskB));
        Assert.Equal(before.DocumentRevision, fixture.State.DocumentRevision);
        Assert.Equal(before.HistoryStatus, fixture.State.HistoryStatus);
        Assert.Equal(before.ActiveScopeId, fixture.State.ActiveScopeId);
        fixture.AssertReady();
    }

    private static string? Text(Canvas2DSceneItem item, string key) =>
        item.Metadata.TryGetValue(key, out var value) ? value.TextValue : null;

    private static PointD Center(RectD bounds) =>
        new(bounds.Left + bounds.Width / 2d, bounds.Top + bounds.Height / 2d);

    private static string Diagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed partial class Fixture : IAsyncDisposable
    {
        internal static VisualStateId TaskB { get; } = new("n101a:task-b:visual");
        internal static VisualStateId TaskB2 { get; } = new("n101a:task-b2:visual");
        internal static SemanticElementId PoolA { get; } = new("n101a:participant:a");
        internal static SemanticElementId PoolB { get; } = new("n101a:participant:b");

        private Fixture(EditingSession session, DocumentCanvasComposition composition)
        {
            Session = session;
            Placement = new ToolboxPlacementController(composition.ToolboxPlacementCatalog,
                ToolboxSelection, composition.DocumentCreationIdentityProvider, composition.SpatialEditPlanners);
            Controller = new Canvas2DInteractionController(session,
                connectionCreationCatalog: composition.AnchorConnectionCreationCatalog,
                creationIdentityProvider: composition.DocumentCreationIdentityProvider,
                endpointReconnectionCatalog: composition.EndpointReconnectionCatalog,
                spatialEditPlanners: composition.SpatialEditPlanners);
        }

        internal EditingSession Session { get; }
        internal ToolboxSelectionState ToolboxSelection { get; } = new();
        internal ToolboxPlacementController Placement { get; }
        internal Canvas2DInteractionController Controller { get; }
        internal EditingSessionState State => Session.CaptureState();
        internal DocumentSnapshot Document
        {
            get
            {
                Assert.True(Session.TryCaptureDocumentSnapshot(out var document));
                return Assert.IsType<DocumentSnapshot>(document);
            }
        }

        internal static async Task<Fixture> CreateAsync()
        {
            var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
            var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
            var attached = await EditingSession.AttachAsync(composition.Document, renderer, composition.Configuration);
            var fixture = new Fixture(Assert.IsType<EditingSession>(attached.Session), composition);
            Assert.Equal(EditingSessionAttachStatus.Ready, attached.Status);
            await fixture.ExecuteAsync(state => new SetModelProfileAvailabilityCommand(state.DocumentId, state.DocumentRevision,
                [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, true)]));
            await fixture.ExecuteAsync(state => new CreateBpmnTaskCommand(state.DocumentId, state.DocumentRevision,
                new SemanticElementId("n101a:task-b"), TaskB, new PointD(180d, 160d), new SizeD(160d, 100d),
                "B1", "Peer one", 201, VisualPlacementMode.Pinned, targetScopeId: state.ActiveScopeId));
            await fixture.ExecuteAsync(state => new CreateBpmnTaskCommand(state.DocumentId, state.DocumentRevision,
                new SemanticElementId("n101a:task-b2"), TaskB2, new PointD(540d, 160d), new SizeD(160d, 100d),
                "B2", "Peer two", 202, VisualPlacementMode.Pinned, targetScopeId: state.ActiveScopeId));
            await fixture.ExecuteAsync(state => new CreateOrganizationalPoolCommand(
                state.DocumentId, state.DocumentRevision, PoolA, state.ActiveScopeId,
                OrganizationalPoolCreationMode.AdoptEligibleUnassigned, "A"));
            await fixture.ExecuteAsync(state => new CreateOrganizationalPoolCommand(
                state.DocumentId, state.DocumentRevision, PoolB, state.ActiveScopeId,
                OrganizationalPoolCreationMode.Empty, "B"));
            await fixture.ExecuteAsync(state => new AssignOrganizationalElementCommand(
                state.DocumentId, state.DocumentRevision, fixture.Visual(TaskB).SemanticElementId, PoolB));
            fixture.AssertReady();
            return fixture;
        }

        internal async Task ExecuteAsync(Func<EditingSessionState, ICommand> create)
        {
            var result = await Session.ExecuteAsync(create(State));
            Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
            await Session.WaitForIdleAsync();
            AssertReady();
        }

        internal VisualStateSnapshot Visual(VisualStateId id) =>
            Document.VisualModel.VisualStates.Single(visual => visual.Id == id);

        internal Canvas2DSceneItem Node(VisualStateId id, SemanticElementId? owner = null) =>
            State.CurrentScene!.Items.First(item => item.Origin.VisualStateId == id &&
                Canvas2DNodeBodyMetadata.IsNodeBody(item) &&
                (owner is null || item.SpatialRegion?.ContainerSemanticElementId == owner));

        internal PointD NodePoint(VisualStateId id, SemanticElementId? owner = null) =>
            Node(id, owner).Bounds.TopLeft + new VectorD(18d, 18d);

        internal Canvas2DSceneItem Anchor(VisualStateId id, ConnectorAnchorId anchor) =>
            State.CurrentScene!.Items.Single(item => item.Origin.VisualStateId == id &&
                Text(item, Canvas2DConnectorAnchorMetadata.AnchorId) == anchor.Value);

        internal PointD Css(PointD point) => State.CurrentScene!.ViewportTransform.TransformPoint(point);

        internal Canvas2DPointerInput Pointer(PointD point, int buttons = 0) =>
            new(8101, Css(point), buttons: buttons);

        internal async Task SelectAsync(VisualStateId id)
        {
            await Controller.PointerActivatedAsync(Css(NodePoint(id)));
            Assert.Equal(id, Assert.Single(State.EditorState.Selection));
        }

        internal async Task DragAsync(PointD start, VectorD movement)
        {
            await Controller.PointerPressedAsync(Pointer(start, buttons: 1));
            var moving = await Controller.PointerMovedAsync(Pointer(start + movement, buttons: 1));
            Assert.Equal(Canvas2DInteractionStatus.Updated, moving.Status);
            var completed = await Controller.PointerReleasedAsync(Pointer(start + movement));
            Assert.True(completed.Status == Canvas2DInteractionStatus.Committed,
                $"{completed.Status}: {Diagnostics(completed.Diagnostics)}");
            Assert.DoesNotContain(completed.Diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            await Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            AssertReady();
        }

        internal void AssertReady()
        {
            Assert.True(State.Status == EditingSessionStatus.Ready,
                $"{State.Status}: {Diagnostics(State.RuntimeDiagnostics.Concat(State.PresentationDiagnostics))}");
            Assert.NotNull(State.CurrentScene);
            Assert.DoesNotContain(State.PresentationDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        public async ValueTask DisposeAsync()
        {
            await Controller.DisposeAsync();
            await Session.DisposeAsync();
        }
    }
}
