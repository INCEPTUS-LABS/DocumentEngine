using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class Canvas2DSpatialPoolInteractionTests
{
    private static readonly VisualStateId SamePoolVisibilityFlow = new("n101:visibility:same-flow:visual");
    private static readonly VisualStateId CrossPoolVisibilityFlow = new("n101:visibility:cross-flow:visual");

    [Fact]
    public async Task HidingPoolGraphicsPreservesOverlappingCanonicalNodesAndBothConnectorPaths()
    {
        await using var fixture = await CreateVisibilityFixtureAsync();
        var before = fixture.Document;
        var visible = fixture.State;
        var visibleTrace = ObserveVisibility("ON", fixture);
        var ids = Fixture.RegionNodes.Take(5).ToArray();
        var displayed = ids.ToDictionary(id => id, id => fixture.Node(id).Bounds);
        var samePath = PresentedPath(fixture, SamePoolVisibilityFlow);
        var crossPath = PresentedPath(fixture, CrossPoolVisibilityFlow);
        Assert.Equal(fixture.Visual(ids[0]).Position, fixture.Visual(ids[2]).Position);
        Assert.Equal(fixture.Visual(ids[0]).Position, fixture.Visual(ids[4]).Position);
        Assert.False(displayed[ids[0]].Intersects(displayed[ids[2]]));
        Assert.False(displayed[ids[2]].Intersects(displayed[ids[4]]));
        Assert.NotEmpty(OrganizationalDecoration(fixture));

        await SetOrganizationalGraphicsAsync(fixture, false);

        var hidden = fixture.State;
        var hiddenTrace = ObserveVisibility("OFF", fixture);
        Assert.Same(before, fixture.Document);
        Assert.Equal(visible.DocumentRevision, hidden.DocumentRevision);
        Assert.Equal(visible.HistoryStatus, hidden.HistoryStatus);
        Assert.Equal(visible.ActiveScopeId, hidden.ActiveScopeId);
        Assert.Same(visible.ProjectedGraph, hidden.ProjectedGraph);
        Assert.Same(visible.LayoutResult, hidden.LayoutResult);
        Assert.Same(visible.RoutingResult, hidden.RoutingResult);
        Assert.True(hidden.CurrentScene!.SpatialPresentationPlan is not null,
            visibleTrace + Environment.NewLine + hiddenTrace);
        Assert.Equal(visible.CurrentScene!.SpatialPresentationPlan, hidden.CurrentScene.SpatialPresentationPlan);
        foreach (var id in ids)
        {
            Assert.Equal(displayed[id], fixture.Node(id).Bounds);
        }
        Assert.Equal(samePath, PresentedPath(fixture, SamePoolVisibilityFlow));
        Assert.Equal(crossPath, PresentedPath(fixture, CrossPoolVisibilityFlow));
        Assert.Empty(OrganizationalDecoration(fixture));
    }

    [Theory]
    [InlineData(0.6d)]
    [InlineData(0.8d)]
    [InlineData(1d)]
    [InlineData(1.1d)]
    public async Task RepeatedDecorationTogglePreservesEveryProcessItemAndViewportAtAllZooms(double zoom)
    {
        await using var fixture = await CreateVisibilityFixtureAsync();
        Assert.True((await fixture.Session.UpdateViewportAsync(
            new ViewportSnapshot(zoom, new VectorD(137d, -93d)))).Succeeded);
        var before = fixture.Document;
        var stateBefore = fixture.State;
        var originalItems = ProcessPresentationItems(fixture);
        foreach (var visible in new[] { false, true, false, true, false, true })
        {
            await SetOrganizationalGraphicsAsync(fixture, visible);
            Assert.Same(before, fixture.Document);
            Assert.Equal(stateBefore.DocumentRevision, fixture.State.DocumentRevision);
            Assert.Equal(stateBefore.HistoryStatus, fixture.State.HistoryStatus);
            Assert.Equal(stateBefore.EditorState.Viewport, fixture.State.EditorState.Viewport);
            Assert.Equal(stateBefore.ModelProfileElementViewState, fixture.State.ModelProfileElementViewState);
            Assert.Equal(stateBefore.CurrentScene!.SpatialPresentationPlan, fixture.State.CurrentScene!.SpatialPresentationPlan);
            Assert.Equal(originalItems, ProcessPresentationItems(fixture));
            Assert.Equal(visible, OrganizationalDecoration(fixture).Length > 0);
        }
    }

    [Fact]
    public async Task HiddenDestinationsRemainReusableThroughMixedPlacementAndAssignmentChanges()
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        await SetOrganizationalGraphicsAsync(fixture, false);
        async Task<VisualStateId> Place(SemanticElementId? poolId, string type, PointD point)
        {
            var before = fixture.Document;
            var state = fixture.State;
            var result = await PlaceInRegionAsync(fixture, poolId, type, point);
            Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
            var id = Assert.IsType<VisualStateId>(result.CreatedVisualStateId);
            var visual = fixture.Visual(id);
            Assert.Equal(point - new VectorD(visual.Size.Width / 2d, visual.Size.Height / 2d), visual.Position);
            Assert.Equal(poolId, fixture.Document.SemanticModel.ProfileAssignments.SingleOrDefault(
                assignment => assignment.SemanticElementId == visual.SemanticElementId)?.ContainerSemanticElementId);
            AssertUnrelatedVisualsUnchanged(before, fixture.Document);
            AssertSuccessfulSpatialMutation(fixture, before, state);
            Assert.Empty(OrganizationalDecoration(fixture));
            return id;
        }
        async Task Move(VisualStateId id, SemanticElementId? poolId, PointD point)
        {
            var before = fixture.Document;
            var state = fixture.State;
            var original = fixture.Visual(id);
            var result = await TryMoveToRegionAsync(fixture, id, poolId, point);
            Assert.Equal(Canvas2DInteractionStatus.Committed, result.Status);
            Assert.Equal(point - new VectorD(original.Size.Width / 2d, original.Size.Height / 2d), fixture.Visual(id).Position);
            AssertUnrelatedVisualsUnchanged(before, fixture.Document, id);
            Assert.Equal(poolId, fixture.Document.SemanticModel.ProfileAssignments.SingleOrDefault(
                assignment => assignment.SemanticElementId == original.SemanticElementId)?.ContainerSemanticElementId);
            AssertSuccessfulSpatialMutation(fixture, before, state);
            Assert.Empty(OrganizationalDecoration(fixture));
        }
        var timer = await Place(null, "timer-catch-event", new PointD(200d, 40d));
        var task = await Place(null, "task", new PointD(400d, 180d));
        await Move(Fixture.RegionNodes[0], null, new PointD(120d, 270d));
        await Place(Fixture.PoolB, "exclusive-gateway", new PointD(380d, 80d));
        await Move(task, Fixture.PoolA, new PointD(300d, 100d));
        await Move(Fixture.RegionNodes[2], null, new PointD(420d, 270d));
        await Move(timer, null, new PointD(220d, 70d));
        var hiddenItems = ProcessPresentationItems(fixture);
        var beforeShow = fixture.Document;
        await SetOrganizationalGraphicsAsync(fixture, true);
        Assert.Same(beforeShow, fixture.Document);
        Assert.Equal(hiddenItems, ProcessPresentationItems(fixture));
        Assert.NotEmpty(OrganizationalDecoration(fixture));
    }

    [Fact]
    public async Task HiddenHeaderHasNoSemanticHitTargetButPreservesStructuralPlacementRejection()
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        var header = fixture.State.CurrentScene!.Items.Single(item =>
            item.Origin.SemanticElementId == Fixture.PoolA &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);
        var headerPoint = Center(header.Bounds);
        await fixture.Controller.SelectSemanticSceneTargetAsync(Fixture.PoolA);
        var before = fixture.Document;
        var stateBefore = fixture.State;
        await SetOrganizationalGraphicsAsync(fixture, false);
        Assert.Null(fixture.State.EditorState.SemanticSceneSelection);
        Assert.Empty(fixture.State.EditorState.Selection);
        var context = await fixture.Controller.PointerContextMenuAsync(fixture.Css(headerPoint));
        Assert.Null(context.TargetId);
        Assert.Null(context.TargetOrigin?.SemanticElementId);
        fixture.ToolboxSelection.Select(new ToolboxItemId("bpmn:toolbox:task"));
        var rejected = await fixture.Placement.TryPlaceAtCssPointAsync(fixture.Session, fixture.Css(headerPoint));
        Assert.False(rejected.IsCommitted);
        Assert.Same(before, fixture.Document);
        Assert.Equal(stateBefore.HistoryStatus, fixture.State.HistoryStatus);
        Assert.Equal(stateBefore.DocumentRevision, fixture.State.DocumentRevision);
        Assert.Empty(OrganizationalDecoration(fixture));
        await fixture.SelectAsync(Fixture.RegionNodes[2]);
        var node = fixture.Node(Fixture.RegionNodes[2]).Bounds;
        await SetOrganizationalGraphicsAsync(fixture, true);
        await SetOrganizationalGraphicsAsync(fixture, false);
        Assert.Equal(Fixture.RegionNodes[2], Assert.Single(fixture.State.EditorState.Selection));
        Assert.Null(fixture.State.EditorState.SemanticSceneSelection);
        Assert.Equal(node, fixture.Node(Fixture.RegionNodes[2]).Bounds);
    }

    [Fact]
    public async Task HiddenCollapsedPoolKeepsCollapseAndRejectsPlacementAndIncomingMovement()
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        Assert.True((await fixture.Session.UpdateModelProfileElementViewStateAsync(
            fixture.State.ModelProfileElementViewState.WithCollapsed(
                OrganizationalModelProfile.Id, Fixture.PoolA, true))).Succeeded);
        var before = fixture.Document;
        var stateBefore = fixture.State;
        var collapsed = fixture.State.ModelProfileElementViewState;
        await SetOrganizationalGraphicsAsync(fixture, false);
        var region = Destination(fixture, Fixture.PoolA);
        var center = Center(region.Bounds);
        fixture.ToolboxSelection.Select(new ToolboxItemId("bpmn:toolbox:task"));
        var placement = await fixture.Placement.TryPlaceAtCssPointAsync(fixture.Session, fixture.Css(center));
        Assert.False(placement.IsCommitted);
        var move = await TryMoveToRegionAsync(fixture, Fixture.RegionNodes[2], Fixture.PoolA, region.MapSceneToLocal(center));
        Assert.NotEqual(Canvas2DInteractionStatus.Committed, move.Status);
        Assert.Contains(move.Diagnostics, diagnostic => diagnostic.Message.Contains("collapsed", StringComparison.OrdinalIgnoreCase));
        Assert.Same(before, fixture.Document);
        Assert.Equal(stateBefore.HistoryStatus, fixture.State.HistoryStatus);
        Assert.Equal(stateBefore.DocumentRevision, fixture.State.DocumentRevision);
        Assert.Equal(collapsed, fixture.State.ModelProfileElementViewState);
        Assert.DoesNotContain(fixture.State.CurrentScene!.Items,
            item => item.IsVisible && item.Origin.VisualStateId == Fixture.RegionNodes[0]);
        await SetOrganizationalGraphicsAsync(fixture, true);
        Assert.Equal(collapsed, fixture.State.ModelProfileElementViewState);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task HiddenNodeMoveResizeAndAnchorUseTheSameCanonicalRegion(int regionIndex)
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        var id = Fixture.RegionNodes[regionIndex * 2];
        var original = fixture.Visual(id);
        await fixture.DragAsync(fixture.NodePoint(id), new VectorD(20d, 10d));
        var beforeHide = fixture.Node(id).Bounds;
        await SetOrganizationalGraphicsAsync(fixture, false);
        Assert.Equal(beforeHide, fixture.Node(id).Bounds);
        var beforeMove = fixture.Document;
        await fixture.DragAsync(fixture.NodePoint(id), new VectorD(30d, 15d));
        Assert.Equal(original.Position + new VectorD(50d, 25d), fixture.Visual(id).Position);
        AssertUnrelatedVisualsUnchanged(beforeMove, fixture.Document, id);
        var beforeResize = fixture.Visual(id);
        var resize = fixture.State.CurrentScene!.Items.Single(item => item.Origin.VisualStateId == id &&
            Text(item, Canvas2DResizeGestureMetadata.HandleRole) == "southeast");
        await fixture.DragAsync(Center(resize.Bounds), new VectorD(20d, 12d));
        Assert.Equal(beforeResize.Position, fixture.Visual(id).Position);
        Assert.Equal(new SizeD(beforeResize.Size.Width + 20d, beforeResize.Size.Height + 12d), fixture.Visual(id).Size);
        var node = fixture.Node(id);
        var context = await fixture.Controller.PointerContextMenuAsync(
            fixture.Css(new PointD(node.Bounds.Right, node.Bounds.Top + 25d)));
        var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(context.ConnectorAnchorContextAction);
        Assert.Equal(ConnectorAnchorSide.Right, action.Side);
        var state = fixture.State;
        var target = state.CurrentScene!.Items.Single(item => item.Id == action.SourceSceneObjectId);
        var anchorId = new ConnectorAnchorId($"n101:hidden:anchor:{regionIndex}");
        var result = await fixture.Session.ExecuteForSceneTargetAsync(new AddConnectorAnchorCommand(
            state.DocumentId, state.DocumentRevision, id, anchorId, action.Side, ConnectorAnchorRole.Source, action.InsertionIndex),
            state.CurrentScene, state.Generation, target.Id, target.SpatialRegion);
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await fixture.Session.WaitForIdleAsync();
        Assert.Equal(node.Bounds, fixture.Node(id).Bounds);
        Assert.Single(fixture.Visual(id).ConnectorAnchors, anchor => anchor.Id == anchorId);
        var hiddenItems = ProcessPresentationItems(fixture);
        await SetOrganizationalGraphicsAsync(fixture, true);
        Assert.Equal(hiddenItems, ProcessPresentationItems(fixture));
        await fixture.DragAsync(fixture.NodePoint(id), new VectorD(10d, 5d));
        Assert.Equal(original.Position + new VectorD(60d, 30d), fixture.Visual(id).Position);
    }

    [Theory]
    [InlineData("timer")]
    [InlineData("message")]
    [InlineData("signal")]
    public async Task BoundaryEventRemainsAttachedWhenGraphicsAreHiddenAndOwnerMoves(string type)
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        var ownerId = Fixture.RegionNodes[2];
        var owner = fixture.Visual(ownerId);
        var semanticId = new SemanticElementId($"n101:hidden:boundary:{type}");
        var visualId = new VisualStateId($"{semanticId.Value}:visual");
        await fixture.ExecuteAsync(state => type switch
        {
            "message" => new CreateBpmnMessageBoundaryEventCommand(state.DocumentId, state.DocumentRevision,
                semanticId, visualId, owner.SemanticElementId, BoundaryAttachmentSide.Bottom, 0.4d,
                VisualBounds(owner), "Message", targetScopeId: state.ActiveScopeId),
            "signal" => new CreateBpmnSignalBoundaryEventCommand(state.DocumentId, state.DocumentRevision,
                semanticId, visualId, owner.SemanticElementId, BoundaryAttachmentSide.Bottom, 0.4d,
                VisualBounds(owner), "Signal", targetScopeId: state.ActiveScopeId),
            _ => new CreateBpmnTimerBoundaryEventCommand(state.DocumentId, state.DocumentRevision,
                semanticId, visualId, owner.SemanticElementId, BoundaryAttachmentSide.Bottom, 0.4d,
                VisualBounds(owner), "Timer", "PT5M", targetScopeId: state.ActiveScopeId),
        });
        var ownerScene = fixture.Node(ownerId).Bounds;
        var boundaryScene = fixture.Node(visualId).Bounds;
        var boundary = fixture.Visual(visualId);
        var before = fixture.Document;
        await SetOrganizationalGraphicsAsync(fixture, false);
        Assert.Same(before, fixture.Document);
        Assert.Equal(ownerScene, fixture.Node(ownerId).Bounds);
        Assert.Equal(boundaryScene, fixture.Node(visualId).Bounds);
        await fixture.DragAsync(fixture.NodePoint(ownerId), new VectorD(30d, 15d));
        Assert.Equal(ownerScene.Translate(new VectorD(30d, 15d)), fixture.Node(ownerId).Bounds);
        Assert.Equal(boundaryScene.Translate(new VectorD(30d, 15d)), fixture.Node(visualId).Bounds);
        Assert.Equal(boundary.BoundaryAttachment, fixture.Visual(visualId).BoundaryAttachment);
        Assert.DoesNotContain(fixture.Document.SemanticModel.ProfileAssignments,
            assignment => assignment.SemanticElementId == semanticId);
        Assert.Equal(Fixture.PoolB, fixture.Node(visualId).SpatialRegion!.ContainerSemanticElementId);
        AssertUnrelatedVisualsUnchanged(before, fixture.Document, ownerId, visualId);
        Assert.Empty(OrganizationalDecoration(fixture));
    }

    private static async Task<Fixture> CreateVisibilityFixtureAsync()
    {
        var fixture = await Fixture.CreateDragRegressionAsync();
        foreach (var id in new[] { Fixture.RegionNodes[1], Fixture.RegionNodes[3], Fixture.RegionNodes[5] })
        {
            await fixture.ExecuteAsync(state => new DeleteBpmnFlowNodeCommand(
                state.DocumentId, state.DocumentRevision, fixture.Visual(id).SemanticElementId, id));
        }
        var eventId = new SemanticElementId("n101:visibility:event:a2");
        await fixture.ExecuteAsync(state => new CreateBpmnTimerCatchEventCommand(
            state.DocumentId, state.DocumentRevision, eventId, Fixture.RegionNodes[1],
            new PointD(420d, 180d), new SizeD(36d, 36d), "Event A2",
            VisualPlacementMode.Pinned, targetScopeId: state.ActiveScopeId));
        await fixture.ExecuteAsync(state => new AssignOrganizationalElementCommand(
            state.DocumentId, state.DocumentRevision, eventId, Fixture.PoolA));
        var gatewayId = new SemanticElementId("n101:visibility:gateway:b2");
        await fixture.ExecuteAsync(state => new CreateBpmnExclusiveGatewayCommand(
            state.DocumentId, state.DocumentRevision, gatewayId, Fixture.RegionNodes[3],
            new PointD(560d, 240d), new SizeD(48d, 48d), "B2", "Gateway B2",
            VisualPlacementMode.Pinned, targetScopeId: state.ActiveScopeId));
        await fixture.ExecuteAsync(state => new AssignOrganizationalElementCommand(
            state.DocumentId, state.DocumentRevision, gatewayId, Fixture.PoolB));
        foreach (var id in new[] { Fixture.RegionNodes[0], Fixture.RegionNodes[2], Fixture.RegionNodes[4] })
        {
            await fixture.ExecuteAsync(state => new ResizeVisualStateCommand(
                state.DocumentId, state.DocumentRevision, id, new RectD(100d, 100d, 120d, 80d)));
        }
        await AddVisibilityFlowAsync(fixture, SamePoolVisibilityFlow,
            Fixture.RegionNodes[0], Fixture.RegionNodes[1], ConnectorAnchorSide.Right, ConnectorAnchorSide.Left);
        await AddVisibilityFlowAsync(fixture, CrossPoolVisibilityFlow,
            Fixture.RegionNodes[1], Fixture.RegionNodes[3], ConnectorAnchorSide.Right, ConnectorAnchorSide.Left);
        return fixture;
    }

    private static async Task AddVisibilityFlowAsync(Fixture fixture, VisualStateId flowId,
        VisualStateId sourceId, VisualStateId targetId, ConnectorAnchorSide sourceSide, ConnectorAnchorSide targetSide)
    {
        var sourceAnchor = new ConnectorAnchorId($"{flowId.Value}:source");
        var targetAnchor = new ConnectorAnchorId($"{flowId.Value}:target");
        await fixture.ExecuteAsync(state => new AddConnectorAnchorCommand(state.DocumentId,
            state.DocumentRevision, sourceId, sourceAnchor, sourceSide, ConnectorAnchorRole.Source, 0));
        await fixture.ExecuteAsync(state => new AddConnectorAnchorCommand(state.DocumentId,
            state.DocumentRevision, targetId, targetAnchor, targetSide, ConnectorAnchorRole.Target, 0));
        await fixture.ExecuteAsync(state => new CreateBpmnSequenceFlowCommand(state.DocumentId,
            state.DocumentRevision, new SemanticElementId($"{flowId.Value}:semantic"), flowId,
            fixture.Visual(sourceId).SemanticElementId, fixture.Visual(targetId).SemanticElementId,
            sourceAnchor, targetAnchor));
    }

    private static async Task SetOrganizationalGraphicsAsync(Fixture fixture, bool visible)
    {
        var result = await fixture.Session.UpdateModelProfileViewStateAsync(
            fixture.State.ModelProfileViewState.WithPreferredVisibility(OrganizationalModelProfile.Id, visible));
        Assert.True(result.Succeeded, Diagnostics(result.Diagnostics));
        await fixture.Session.WaitForIdleAsync();
        fixture.AssertReady();
    }

    private static PointD[] PresentedPath(Fixture fixture, VisualStateId flowId)
    {
        var connector = fixture.State.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == flowId && item.Layer == Canvas2DSceneLayer.Connector &&
            item.Metadata.ContainsKey(Canvas2DConnectorPathMetadata.LogicalPathPointCount));
        return Canvas2DConnectorPathMetadata.Resolve(connector).Select(connector.Transform.TransformPoint).ToArray();
    }

    private static Canvas2DSceneItem[] OrganizationalDecoration(Fixture fixture) =>
        fixture.State.CurrentScene!.Items.Where(item => item.IsVisible &&
            (item.Origin.SemanticElementId == Fixture.PoolA || item.Origin.SemanticElementId == Fixture.PoolB ||
             item.Origin.StableSourceKey?.EndsWith(":unassigned-region", StringComparison.Ordinal) == true)).ToArray();

    private static Canvas2DSceneItem[] ProcessPresentationItems(Fixture fixture) =>
        fixture.State.CurrentScene!.Items.Where(item => item.Origin.VisualStateId is not null &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();

    private static string ObserveVisibility(string stage, Fixture fixture)
    {
        var state = fixture.State;
        var profile = OrganizationalModelProfile.Id;
        var regionDetails = state.CurrentScene!.SpatialPresentationPlan is { } plan
            ? string.Join("; ", plan.Regions.Select(region =>
                $"region={region.Id}, container={region.ContainerSemanticElementId?.Value ?? "unassigned"}, " +
                $"transform={region.LocalToSceneTransform}, bounds={region.Bounds}"))
            : "spatial plan absent; all three regions absent";
        return $"{stage}: revision={state.DocumentRevision}; history={state.HistoryStatus.EntryCount}; scope={state.ActiveScopeId}; " +
            $"available={fixture.Document.SemanticModel.ModelProfiles.IsAvailable(profile)}; " +
            $"preferred={state.ModelProfileViewState.IsPreferredVisible(profile)}; " +
            $"effective={state.ModelProfileViewState.IsEffectivelyVisible(profile, fixture.Document.SemanticModel.ModelProfiles)}; " +
            $"generation={state.Generation}; {regionDetails}; " +
            string.Join("; ", Fixture.RegionNodes.Take(5).Select(id => $"node={id}, displayed={fixture.Node(id).Bounds}")) +
            $"; samePoolPath={string.Join(",", PresentedPath(fixture, SamePoolVisibilityFlow))}; " +
            $"crossPoolPath={string.Join(",", PresentedPath(fixture, CrossPoolVisibilityFlow))}; " +
            $"decoration=[{string.Join(",", OrganizationalDecoration(fixture).Select(item => item.Origin.StableSourceKey))}]";
    }
}
