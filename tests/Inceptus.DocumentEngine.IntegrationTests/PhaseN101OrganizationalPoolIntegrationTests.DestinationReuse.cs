using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed partial class PhaseN101OrganizationalPoolIntegrationTests
{
    [Fact]
    public async Task NineMixedOperationsReuseUnassignedWithoutDeletionOrAnyRegionReset()
    {
        await using var fixture = await DragFixture.CreateAsync(includeUnassignedTasks: false);
        var initial = fixture.State;
        var identity = UnassignedRegion(fixture).Id;
        AssertReusableDestination(fixture, identity);

        // The second center is in the initially empty destination, below the first small Event.
        // It must not disappear from the usable band when that first Event becomes its content.
        var timer = await PlaceInUnassignedAsync(fixture, "timer-catch-event", new PointD(80d, 40d), identity);
        Assert.Equal(new SizeD(36d, 36d), timer.Size);
        var task = await PlaceInUnassignedAsync(fixture, "task", new PointD(320d, 160d), identity);
        await MoveToRegionAsync(fixture, DragFixture.TaskIds[0], null, new PointD(500d, 40d), identity);
        await PlaceInUnassignedAsync(fixture, "exclusive-gateway", new PointD(640d, 180d), identity);
        await MoveToRegionAsync(fixture, DragFixture.TaskIds[2], null, new PointD(40d, 180d), identity);
        await MoveToRegionAsync(fixture, timer.Id, null, timer.Position + new VectorD(20d, 0d), identity);
        await MoveToRegionAsync(fixture, task.Id, PoolAId, new PointD(340d, 80d), identity);
        await MoveToRegionAsync(fixture, task.Id, null, new PointD(400d, 230d), identity);
        await PlaceInUnassignedAsync(fixture, "start-event", new PointD(620d, 290d), identity);

        Assert.Equal(initial.DocumentRevision.Value + 9, fixture.State.DocumentRevision.Value);
        Assert.Equal(initial.HistoryStatus.EntryCount + 9, fixture.State.HistoryStatus.EntryCount);
        Assert.Equal(initial.ActiveScopeId, fixture.State.ActiveScopeId);
        Assert.True(fixture.Document.SemanticModel.TryGetElement(timer.SemanticElementId, out _));
        Assert.Null(AssignedPool(fixture.Document, task.SemanticElementId));
        Assert.Null(AssignedPool(fixture.Document, fixture.Visual(DragFixture.TaskIds[0]).SemanticElementId));
        Assert.Null(AssignedPool(fixture.Document, fixture.Visual(DragFixture.TaskIds[2]).SemanticElementId));
    }

    [Fact]
    public async Task DeletingLastUnassignedEventUndoAndRedoNeverControlNextDestinationAvailability()
    {
        await using var fixture = await DragFixture.CreateAsync(includeUnassignedTasks: false);
        var identity = UnassignedRegion(fixture).Id;
        var timer = await PlaceInUnassignedAsync(fixture, "timer-catch-event", new PointD(80d, 40d), identity);
        var populated = fixture.Document;
        await fixture.ExecuteAsync(state => new DeleteBpmnFlowNodeCommand(
            state.DocumentId, state.DocumentRevision, timer.SemanticElementId, timer.Id));
        Assert.DoesNotContain(fixture.Document.VisualModel.VisualStates, visual => visual.Id == timer.Id);
        AssertReusableDestination(fixture, identity);
        var empty = fixture.Document;

        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(populated, fixture.Document);
        AssertReusableDestination(fixture, identity);
        Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(empty, fixture.Document);
        AssertReusableDestination(fixture, identity);

        await PlaceInUnassignedAsync(fixture, "timer-catch-event", new PointD(80d, 40d), identity);
        await PlaceInUnassignedAsync(fixture, "task", new PointD(320d, 160d), identity);
        await MoveToRegionAsync(fixture, DragFixture.TaskIds[2], null, new PointD(500d, 80d), identity);
    }

    [Fact]
    public async Task DeletingPopulatedPoolLeavesMassUnassignedContentAndAcceptsNextPlacementAndMove()
    {
        await using var fixture = await DragFixture.CreateAsync(includeUnassignedTasks: false);
        var identity = UnassignedRegion(fixture).Id;
        var beforeDelete = fixture.Document;
        await fixture.ExecuteAsync(state => new DeleteOrganizationalPoolCommand(
            state.DocumentId, state.DocumentRevision, PoolBId));
        Assert.True(beforeDelete.VisualModel.VisualStates.AsSpan().SequenceEqual(
            fixture.Document.VisualModel.VisualStates.AsSpan()));
        foreach (var id in DragFixture.TaskIds.Skip(2).Take(2))
        {
            Assert.Null(AssignedPool(fixture.Document, fixture.Visual(id).SemanticElementId));
            Assert.Null(fixture.Node(id).SpatialRegion!.ContainerSemanticElementId);
        }
        AssertReusableDestination(fixture, identity);

        await PlaceInUnassignedAsync(fixture, "timer-catch-event", new PointD(80d, 40d), identity);
        await MoveToRegionAsync(fixture, DragFixture.TaskIds[0], null, new PointD(400d, 80d), identity);
    }

    [Fact]
    public async Task BoundaryActivityMoveToPopulatedUnassignedPreservesAttachmentAndDestinationReusability()
    {
        await using var fixture = await DragFixture.CreateAsync(includeUnassignedTasks: false);
        var identity = UnassignedRegion(fixture).Id;
        var ownerId = DragFixture.TaskIds[2];
        var owner = fixture.Visual(ownerId);
        var boundaryId = new VisualStateId("n101:destination:boundary:visual");
        var boundarySemanticId = new SemanticElementId("n101:destination:boundary");
        await fixture.ExecuteAsync(state => new CreateBpmnTimerBoundaryEventCommand(
            state.DocumentId, state.DocumentRevision, boundarySemanticId, boundaryId,
            owner.SemanticElementId, BoundaryAttachmentSide.Bottom, 0.4d,
            new RectD(owner.Position.X, owner.Position.Y, owner.Size.Width, owner.Size.Height),
            "Timer", "PT5M", targetScopeId: state.ActiveScopeId));
        await PlaceInUnassignedAsync(fixture, "timer-catch-event", new PointD(80d, 40d), identity);
        var before = fixture.Document;
        var boundary = fixture.Visual(boundaryId);
        var boundarySemantic = before.SemanticModel.Elements.Single(element => element.Id == boundarySemanticId);
        var sourceBounds = fixture.Node(ownerId).Bounds;
        var boundaryRelativePosition = fixture.Node(boundaryId).Bounds.TopLeft - sourceBounds.TopLeft;

        await MoveToRegionAsync(fixture, ownerId, null, new PointD(300d, 80d), identity,
            additionallyAffectedVisualId: boundaryId);

        Assert.Equal(boundary.BoundaryAttachment, fixture.Visual(boundaryId).BoundaryAttachment);
        Assert.Equal(boundarySemantic, fixture.Document.SemanticModel.Elements.Single(
            element => element.Id == boundarySemanticId));
        Assert.Null(AssignedPool(fixture.Document, owner.SemanticElementId));
        Assert.Null(AssignedPool(fixture.Document, boundarySemanticId));
        Assert.Equal(boundaryRelativePosition,
            fixture.Node(boundaryId).Bounds.TopLeft - fixture.Node(ownerId).Bounds.TopLeft);
        Assert.Equal(UnassignedRegion(fixture).Id, fixture.Node(boundaryId).SpatialRegion!.Id);
        var moved = fixture.Document;
        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(before, fixture.Document);
        AssertReusableDestination(fixture, identity);
        Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(moved, fixture.Document);
        AssertReusableDestination(fixture, identity);
        await PlaceInUnassignedAsync(fixture, "start-event", new PointD(600d, 160d), identity);
    }

    private static Canvas2DSpatialRegion UnassignedRegion(DragFixture fixture) =>
        Assert.Single(fixture.State.CurrentScene!.SpatialPresentationPlan!.Regions,
            region => region.ContainerSemanticElementId is null);

    private static async Task<VisualStateSnapshot> PlaceInUnassignedAsync(
        DragFixture fixture, string toolboxSuffix, PointD canonicalCenter, Canvas2DSpatialRegionId identity)
    {
        var before = fixture.Document;
        var beforeState = fixture.State;
        var region = UnassignedRegion(fixture);
        var point = region.MapLocalToScene(canonicalCenter);
        Assert.True(region.Bounds.Contains(point),
            $"Current reusable destination {region.Bounds} lost positive canonical center {canonicalCenter}.");
        fixture.ToolboxSelection.Select(new ToolboxItemId($"bpmn:toolbox:{toolboxSuffix}"));
        var result = await fixture.Placement.TryPlaceAtCssPointAsync(fixture.Session, fixture.Css(point));
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await WaitForReadyAsync(fixture.Session);
        var visual = fixture.Visual(Assert.IsType<VisualStateId>(result.CreatedVisualStateId));
        Assert.Equal(canonicalCenter - new VectorD(visual.Size.Width / 2d, visual.Size.Height / 2d), visual.Position);
        Assert.Equal(point - new VectorD(visual.Size.Width / 2d, visual.Size.Height / 2d), fixture.Node(visual.Id).Bounds.TopLeft);
        Assert.Null(AssignedPool(fixture.Document, visual.SemanticElementId));
        AssertUnrelatedVisualsUnchanged(before, fixture.Document);
        AssertSuccessfulSpatialMutation(fixture, beforeState, identity);
        return visual;
    }

    private static async Task MoveToRegionAsync(
        DragFixture fixture, VisualStateId visualId, SemanticElementId? poolId,
        PointD canonicalTopLeft, Canvas2DSpatialRegionId identity,
        VisualStateId? additionallyAffectedVisualId = null)
    {
        var before = fixture.Document;
        var beforeState = fixture.State;
        var original = fixture.Visual(visualId);
        var region = fixture.State.CurrentScene!.SpatialPresentationPlan!.Regions.Single(
            candidate => candidate.ContainerSemanticElementId == poolId);
        var start = fixture.Node(visualId).Bounds.TopLeft + DragFixture.GrabOffset;
        var destination = region.MapLocalToScene(canonicalTopLeft + DragFixture.GrabOffset);
        Assert.True(region.Bounds.Contains(destination));
        await fixture.DragAsync(start, destination);
        Assert.Equal(canonicalTopLeft, fixture.Visual(visualId).Position);
        Assert.Equal(original.Id, fixture.Visual(visualId).Id);
        Assert.Equal(original.SemanticElementId, fixture.Visual(visualId).SemanticElementId);
        Assert.Equal(poolId, AssignedPool(fixture.Document, original.SemanticElementId));
        Assert.Equal(destination - DragFixture.GrabOffset, fixture.Node(visualId).Bounds.TopLeft);
        AssertUnrelatedVisualsUnchanged(before, fixture.Document, visualId, additionallyAffectedVisualId);
        AssertSuccessfulSpatialMutation(fixture, beforeState, identity);
    }

    private static void AssertSuccessfulSpatialMutation(
        DragFixture fixture, EditingSessionState before, Canvas2DSpatialRegionId identity)
    {
        var state = fixture.State;
        Assert.Equal(before.DocumentRevision.Increment(), state.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, state.HistoryStatus.EntryCount);
        Assert.NotEqual(before.Generation, state.Generation);
        Assert.NotSame(before.CurrentScene, state.CurrentScene);
        Assert.Equal(before.ActiveScopeId, state.ActiveScopeId);
        AssertReusableDestination(fixture, identity);
    }

    private static void AssertUnrelatedVisualsUnchanged(
        DocumentSnapshot before, DocumentSnapshot after,
        VisualStateId? changedId = null, VisualStateId? additionallyAffectedId = null)
    {
        foreach (var visual in before.VisualModel.VisualStates.Where(
            visual => visual.Id != changedId && visual.Id != additionallyAffectedId))
        {
            Assert.Equal(visual, after.VisualModel.VisualStates.Single(candidate => candidate.Id == visual.Id));
        }
    }

    private static void AssertReusableDestination(DragFixture fixture, Canvas2DSpatialRegionId identity)
    {
        var before = fixture.Document;
        var state = fixture.State;
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        Assert.Equal(before.Revision, state.CurrentScene!.SourceRevision);
        var region = UnassignedRegion(fixture);
        Assert.Equal(identity, region.Id);
        Assert.Null(region.ContainerSemanticElementId);
        var freeCanonicalPoint = new PointD(300d, 160d);
        var scenePoint = region.MapLocalToScene(freeCanonicalPoint);
        Assert.True(region.Bounds.Contains(scenePoint),
            $"The populated destination must retain the empty region's reusable area: {region.Bounds}.");
        Assert.Equal(freeCanonicalPoint, region.MapSceneToLocal(scenePoint));
        var probeSemanticId = new SemanticElementId("n101:destination:uncommitted-probe");
        var create = new CreateBpmnTaskCommand(state.DocumentId, state.DocumentRevision,
            probeSemanticId, new VisualStateId("n101:destination:uncommitted-probe:visual"),
            new PointD(220d, 110d), new SizeD(160d, 100d), "PROBE", "Probe", 999,
            targetScopeId: state.ActiveScopeId);
        var creation = fixture.SpatialEditPlanners.Plan(new Canvas2DSpatialEditRequest(
            before, state.ActiveScopeId, Canvas2DSpatialEditKind.Creation,
            create, probeSemanticId, region));
        Assert.True(creation.Succeeded, Diagnostics(creation.Diagnostics));
        var source = fixture.Visual(DragFixture.TaskIds[1]);
        var move = new MoveVisualStateCommand(state.DocumentId, state.DocumentRevision, source.Id, source.Position);
        var movement = fixture.SpatialEditPlanners.Plan(new Canvas2DSpatialEditRequest(
            before, state.ActiveScopeId, Canvas2DSpatialEditKind.Move, move, source.SemanticElementId, region));
        Assert.True(movement.Succeeded, Diagnostics(movement.Diagnostics));
        Assert.Same(before, fixture.Document);
        Assert.Equal(state.DocumentRevision, fixture.State.DocumentRevision);
        Assert.Equal(state.HistoryStatus, fixture.State.HistoryStatus);
        Assert.Equal(state.EditorState, fixture.State.EditorState);
    }
}
