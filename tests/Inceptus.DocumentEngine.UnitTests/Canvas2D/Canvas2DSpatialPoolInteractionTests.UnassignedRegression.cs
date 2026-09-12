using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class Canvas2DSpatialPoolInteractionTests
{
    [Fact]
    public async Task FirstTimerDoesNotConsumeUnassignedDestinationAndDeletionIsNotRequiredToUnlockIt()
    {
        await using var fixture = await CreateEmptyUnassignedFixtureAsync();
        var traces = new List<string>();
        var probe = new PointD(400d, 180d);
        var emptyRegion = Unassigned(fixture);
        traces.Add(ObserveUnassigned("empty", fixture, probe));
        Assert.True(IsUnassignedDestinationAvailable(fixture, probe));

        var timer = await PlaceInUnassignedAsync(fixture, "timer-catch-event", new PointD(100d, 40d));
        Assert.True(timer.IsCommitted, Diagnostics(timer.Diagnostics));
        var timerId = Assert.IsType<VisualStateId>(timer.CreatedVisualStateId);
        Assert.Equal(new SizeD(36d, 36d), fixture.Visual(timerId).Size);
        traces.Add(ObserveUnassigned("after first Timer", fixture, probe));

        var task = await PlaceInUnassignedAsync(fixture, "task", probe);
        traces.Add($"second placement: committed={task.IsCommitted}; {Diagnostics(task.Diagnostics)}");
        traces.Add(ObserveUnassigned("after second placement attempt", fixture, probe));
        var move = await TryMoveToUnassignedAsync(fixture, Fixture.RegionNodes[0], probe);
        traces.Add($"incoming move: status={move.Status}; {Diagnostics(move.Diagnostics)}");
        traces.Add(ObserveUnassigned("after incoming move attempt", fixture, probe));

        await fixture.ExecuteAsync(state => new DeleteBpmnFlowNodeCommand(
            state.DocumentId, state.DocumentRevision, fixture.Visual(timerId).SemanticElementId, timerId));
        traces.Add(ObserveUnassigned("after Timer deletion", fixture, probe));
        var availableAfterDelete = IsUnassignedDestinationAvailable(fixture, probe);
        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await fixture.Session.WaitForIdleAsync();
        traces.Add(ObserveUnassigned("after deletion Undo", fixture, probe));
        var availableAfterUndo = IsUnassignedDestinationAvailable(fixture, probe);
        Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
        await fixture.Session.WaitForIdleAsync();
        traces.Add(ObserveUnassigned("after deletion Redo", fixture, probe));
        var availableAfterRedo = IsUnassignedDestinationAvailable(fixture, probe);

        var nextTimer = await PlaceInUnassignedAsync(fixture, "timer-catch-event", new PointD(100d, 40d));
        traces.Add($"replacement Timer: committed={nextTimer.IsCommitted}; {Diagnostics(nextTimer.Diagnostics)}");
        var nextPlacement = await PlaceInUnassignedAsync(fixture, "start-event", new PointD(250d, 180d));
        traces.Add($"next placement: committed={nextPlacement.IsCommitted}; {Diagnostics(nextPlacement.Diagnostics)}");
        traces.Add(ObserveUnassigned("after repeated placement", fixture, probe));

        Assert.Equal(emptyRegion.Id, Unassigned(fixture).Id);
        Assert.Equal(emptyRegion.LocalToSceneTransform, Unassigned(fixture).LocalToSceneTransform);
        Assert.True(task.IsCommitted && move.Status == Canvas2DInteractionStatus.Committed &&
            availableAfterDelete && availableAfterUndo && availableAfterRedo &&
            nextTimer.IsCommitted && nextPlacement.IsCommitted, string.Join(Environment.NewLine, traces));
        fixture.AssertReady();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EmptyAndPopulatedDestinationsAcceptThreeConsecutiveTimers(int regionIndex)
    {
        await using var fixture = await CreateEmptyUnassignedFixtureAsync();
        SemanticElementId? containerId = regionIndex == 0 ? Fixture.PoolA : regionIndex == 1 ? Fixture.PoolB : null;
        if (containerId is not null)
        {
            foreach (var id in Fixture.RegionNodes.Skip(regionIndex * 2).Take(2))
            {
                await fixture.ExecuteAsync(state => new DeleteBpmnFlowNodeCommand(
                    state.DocumentId, state.DocumentRevision, fixture.Visual(id).SemanticElementId, id));
            }
        }
        var originalRegion = Destination(fixture, containerId);
        foreach (var point in new[] { new PointD(100d, 40d), new PointD(400d, 180d), new PointD(250d, 180d) })
        {
            var before = fixture.Document;
            var stateBefore = fixture.State;
            var result = await PlaceInRegionAsync(fixture, containerId, "timer-catch-event", point);
            Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
            var id = Assert.IsType<VisualStateId>(result.CreatedVisualStateId);
            Assert.Equal(point - new VectorD(18d, 18d), fixture.Visual(id).Position);
            Assert.Equal(new SizeD(36d, 36d), fixture.Visual(id).Size);
            AssertUnrelatedVisualsUnchanged(before, fixture.Document);
            Assert.Equal(containerId, fixture.Document.SemanticModel.ProfileAssignments.SingleOrDefault(
                assignment => assignment.SemanticElementId == fixture.Visual(id).SemanticElementId)?.ContainerSemanticElementId);
            Assert.Equal(originalRegion.Id, Destination(fixture, containerId).Id);
            Assert.Equal(originalRegion.LocalToSceneTransform, Destination(fixture, containerId).LocalToSceneTransform);
            Assert.True(Destination(fixture, containerId).Bounds.Contains(
                Destination(fixture, containerId).MapLocalToScene(new PointD(400d, 180d))));
            AssertSuccessfulSpatialMutation(fixture, before, stateBefore);
        }
    }

    [Theory]
    [InlineData(0.6d)]
    [InlineData(0.8d)]
    [InlineData(1d)]
    [InlineData(1.1d)]
    public async Task SequentialMixedToolboxTypesUseCurrentTranslatedUnassignedAtEveryZoom(double zoom)
    {
        await using var fixture = await CreateEmptyUnassignedFixtureAsync();
        // Grow an upper row using an ordinary canonical edit, so the destination is far below the origin.
        await fixture.ExecuteAsync(state => new Inceptus.DocumentEngine.Contracts.Commands.MoveVisualStateCommand(
            state.DocumentId, state.DocumentRevision, Fixture.RegionNodes[1], new PointD(460d, 1000d)));
        Assert.True((await fixture.Session.UpdateViewportAsync(
            new ViewportSnapshot(zoom, new VectorD(137d, -93d)))).Succeeded);
        Assert.True(Unassigned(fixture).LocalToSceneTransform.OffsetY > 1200d);
        var types = new[] { "task", "start-event", "timer-catch-event", "exclusive-gateway",
            "user-task", "signal-catch-event", "message-catch-event", "sub-process" };
        for (var index = 0; index < types.Length; index++)
        {
            var before = fixture.Document;
            var stateBefore = fixture.State;
            var previousRegion = Unassigned(fixture);
            var point = new PointD(index % 2 == 0 ? 200d : 400d, index % 2 == 0 ? 80d : 180d);
            var result = await PlaceInUnassignedAsync(fixture, types[index], point);
            Assert.True(result.IsCommitted, $"{types[index]}: {Diagnostics(result.Diagnostics)}");
            var id = Assert.IsType<VisualStateId>(result.CreatedVisualStateId);
            var visual = fixture.Visual(id);
            AssertPointNear(point - new VectorD(visual.Size.Width / 2d, visual.Size.Height / 2d), visual.Position);
            AssertUnrelatedVisualsUnchanged(before, fixture.Document);
            Assert.DoesNotContain(fixture.Document.SemanticModel.ProfileAssignments,
                assignment => assignment.SemanticElementId == visual.SemanticElementId);
            Assert.Equal(previousRegion.Id, Unassigned(fixture).Id);
            Assert.Equal(previousRegion.LocalToSceneTransform, Unassigned(fixture).LocalToSceneTransform);
            Assert.NotSame(previousRegion, Unassigned(fixture));
            AssertSuccessfulSpatialMutation(fixture, before, stateBefore);
        }
    }

    [Fact]
    public async Task NineMixedOperationsReuseUnassignedWithoutAnyReset()
    {
        await using var fixture = await CreateEmptyUnassignedFixtureAsync();
        async Task<VisualStateId> Place(string type, PointD point)
        {
            var before = fixture.Document;
            var state = fixture.State;
            var result = await PlaceInUnassignedAsync(fixture, type, point);
            Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
            AssertUnrelatedVisualsUnchanged(before, fixture.Document);
            AssertSuccessfulSpatialMutation(fixture, before, state);
            return Assert.IsType<VisualStateId>(result.CreatedVisualStateId);
        }
        async Task Move(VisualStateId id, SemanticElementId? containerId, PointD point)
        {
            var before = fixture.Document;
            var state = fixture.State;
            var original = fixture.Visual(id);
            var destination = Destination(fixture, containerId);
            var sceneCenter = destination.MapLocalToScene(point);
            var result = await TryMoveToRegionAsync(fixture, id, containerId, point);
            Assert.Equal(Canvas2DInteractionStatus.Committed, result.Status);
            Assert.Equal(point - new VectorD(original.Size.Width / 2d, original.Size.Height / 2d), fixture.Visual(id).Position);
            Assert.Equal(sceneCenter, Center(fixture.Node(id).Bounds));
            AssertUnrelatedVisualsUnchanged(before, fixture.Document, id);
            Assert.Equal(original.SemanticElementId, fixture.Visual(id).SemanticElementId);
            Assert.Equal(containerId, fixture.Document.SemanticModel.ProfileAssignments.SingleOrDefault(
                assignment => assignment.SemanticElementId == original.SemanticElementId)?.ContainerSemanticElementId);
            AssertSuccessfulSpatialMutation(fixture, before, state);
        }

        var timer = await Place("timer-catch-event", new PointD(100d, 40d));
        var task = await Place("task", new PointD(400d, 180d));
        await Move(Fixture.RegionNodes[0], null, new PointD(120d, 180d));
        await Place("exclusive-gateway", new PointD(620d, 80d));
        await Move(Fixture.RegionNodes[2], null, new PointD(620d, 240d));
        await Move(timer, null, new PointD(60d, 60d));
        await Move(task, Fixture.PoolA, new PointD(280d, 100d));
        await Move(task, null, new PointD(400d, 180d));
        await Place("start-event", new PointD(300d, 60d));
    }

    [Fact]
    public async Task TrulyNegativeTimerBoundsRejectWithoutConsumingCurrentDestination()
    {
        await using var fixture = await CreateEmptyUnassignedFixtureAsync();
        var before = fixture.Document;
        var state = fixture.State;
        var rejected = await PlaceInUnassignedAsync(fixture, "timer-catch-event", new PointD(10d, 10d));
        Assert.False(rejected.IsCommitted);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "CMD_VISUAL_STATE_GEOMETRY_INVALID");
        Assert.Same(before, fixture.Document);
        Assert.Equal(state.DocumentRevision, fixture.State.DocumentRevision);
        Assert.Equal(state.HistoryStatus, fixture.State.HistoryStatus);
        Assert.True(IsUnassignedDestinationAvailable(fixture, new PointD(400d, 180d)));
        var accepted = await PlaceInUnassignedAsync(fixture, "timer-catch-event", new PointD(400d, 180d));
        Assert.True(accepted.IsCommitted, Diagnostics(accepted.Diagnostics));
        Assert.Equal(new PointD(382d, 162d), fixture.Visual(Assert.IsType<VisualStateId>(accepted.CreatedVisualStateId)).Position);
    }

    [Theory]
    [InlineData("collapse")]
    [InlineData("reorder")]
    [InlineData("visibility")]
    public async Task NextPlacementUsesFreshRegionAfterPresentationChange(string change)
    {
        await using var fixture = await CreateEmptyUnassignedFixtureAsync();
        var first = await PlaceInUnassignedAsync(fixture, "timer-catch-event", new PointD(100d, 40d));
        Assert.True(first.IsCommitted, Diagnostics(first.Diagnostics));
        var previousRegion = Unassigned(fixture);
        var previousScene = fixture.State.CurrentScene;
        // Arm and preview before recomposition; the eventual click must reacquire the new Scene.
        fixture.ToolboxSelection.Select(new ToolboxItemId("bpmn:toolbox:task"));
        await fixture.Placement.UpdatePreviewAtCssPointAsync(fixture.Session,
            fixture.Css(previousRegion.MapLocalToScene(new PointD(400d, 180d))));
        if (change == "collapse")
        {
            Assert.True((await fixture.Session.UpdateModelProfileElementViewStateAsync(
                fixture.State.ModelProfileElementViewState.WithCollapsed(
                    OrganizationalModelProfile.Id, Fixture.PoolA, true))).Succeeded);
            Assert.NotEqual(previousRegion.LocalToSceneTransform, Unassigned(fixture).LocalToSceneTransform);
        }
        else if (change == "reorder")
        {
            await fixture.ExecuteAsync(state => new MoveOrganizationalPoolCommand(
                state.DocumentId, state.DocumentRevision, Fixture.PoolB, OrganizationalPoolMoveDirection.Up));
        }
        else
        {
            Assert.True((await fixture.Session.UpdateModelProfileViewStateAsync(
                fixture.State.ModelProfileViewState.WithPreferredVisibility(OrganizationalModelProfile.Id, false))).Succeeded);
            Assert.True((await fixture.Session.UpdateModelProfileViewStateAsync(
                fixture.State.ModelProfileViewState.WithPreferredVisibility(OrganizationalModelProfile.Id, true))).Succeeded);
        }
        Assert.NotSame(previousScene, fixture.State.CurrentScene);
        Assert.NotSame(previousRegion, Unassigned(fixture));
        Assert.Equal(previousRegion.Id, Unassigned(fixture).Id);
        var before = fixture.Document;
        var stateBefore = fixture.State;
        var result = await fixture.Placement.TryPlaceAtCssPointAsync(fixture.Session,
            fixture.Css(Unassigned(fixture).MapLocalToScene(new PointD(400d, 180d))));
        await fixture.Session.WaitForIdleAsync();
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        AssertUnrelatedVisualsUnchanged(before, fixture.Document);
        AssertSuccessfulSpatialMutation(fixture, before, stateBefore);
        var moved = await TryMoveToUnassignedAsync(fixture, Fixture.RegionNodes[2], new PointD(140d, 180d));
        Assert.Equal(Canvas2DInteractionStatus.Committed, moved.Status);
    }

    [Fact]
    public async Task DeletePopulatedPoolLeavesMassUnassignmentImmediatelyReusable()
    {
        await using var fixture = await CreateEmptyUnassignedFixtureAsync();
        var beforeDelete = fixture.Document;
        await fixture.ExecuteAsync(state => new DeleteOrganizationalPoolCommand(
            state.DocumentId, state.DocumentRevision, Fixture.PoolA));
        AssertUnrelatedVisualsUnchanged(beforeDelete, fixture.Document);
        foreach (var id in Fixture.RegionNodes.Take(2))
        {
            Assert.DoesNotContain(fixture.Document.SemanticModel.ProfileAssignments,
                assignment => assignment.SemanticElementId == fixture.Visual(id).SemanticElementId);
        }
        var result = await PlaceInUnassignedAsync(fixture, "timer-catch-event", new PointD(200d, 60d));
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var moved = await TryMoveToUnassignedAsync(fixture, Fixture.RegionNodes[2], new PointD(120d, 280d));
        Assert.Equal(Canvas2DInteractionStatus.Committed, moved.Status);
        Assert.True(IsUnassignedDestinationAvailable(fixture, new PointD(400d, 180d)));
    }

    private static void AssertSuccessfulSpatialMutation(
        Fixture fixture, DocumentSnapshot before, EditingSessionState stateBefore)
    {
        Assert.Equal(before.Revision.Increment(), fixture.Document.Revision);
        Assert.Equal(stateBefore.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
        Assert.Equal(stateBefore.ActiveScopeId, fixture.State.ActiveScopeId);
        Assert.Equal(fixture.Document.Revision, fixture.State.CurrentScene!.SourceRevision);
        Assert.NotEqual(stateBefore.Generation, fixture.State.Generation);
        Assert.True(IsUnassignedDestinationAvailable(fixture, new PointD(400d, 180d)));
        fixture.AssertReady();
    }

    private static async Task<Fixture> CreateEmptyUnassignedFixtureAsync()
    {
        var fixture = await Fixture.CreateDragRegressionAsync();
        foreach (var id in Fixture.RegionNodes.Skip(4))
        {
            await fixture.ExecuteAsync(state => new DeleteBpmnFlowNodeCommand(
                state.DocumentId, state.DocumentRevision, fixture.Visual(id).SemanticElementId, id));
        }
        return fixture;
    }

    private static Canvas2DSpatialRegion Unassigned(Fixture fixture) =>
        Destination(fixture, null);

    private static Canvas2DSpatialRegion Destination(Fixture fixture, SemanticElementId? containerId) =>
        fixture.State.CurrentScene!.SpatialPresentationPlan!.Regions.Single(
            region => region.ContainerSemanticElementId == containerId);

    private static bool IsUnassignedDestinationAvailable(Fixture fixture, PointD canonicalPoint)
    {
        var state = fixture.State;
        var region = Unassigned(fixture);
        return ToolboxPlacementController.TryResolvePlacementTarget(
            state.CurrentScene!, state.ActiveScopeId, region.MapLocalToScene(canonicalPoint),
            out var resolvedPoint, out var scopeId, out var target) &&
            target?.Id == region.Id && scopeId == state.ActiveScopeId && resolvedPoint == canonicalPoint;
    }

    private static string ObserveUnassigned(string stage, Fixture fixture, PointD canonicalProbe)
    {
        var state = fixture.State;
        var region = Unassigned(fixture);
        var sceneProbe = region.MapLocalToScene(canonicalProbe);
        var moveApplicable = region.Bounds.Contains(sceneProbe) &&
            !state.CurrentScene!.Items.Any(item => item.IsVisible &&
                Canvas2DSemanticSceneInteractionMetadata.BlocksPlacement(item) && item.Bounds.Contains(sceneProbe));
        return $"{stage}: revision={state.DocumentRevision}; generation={state.Generation}; region={region.Id}; " +
            $"container=null; sceneBounds={region.Bounds}; localBounds={region.MapSceneToLocal(region.Bounds)}; " +
            $"transform={region.LocalToSceneTransform}; probeScene={sceneProbe}; probeLocal={region.MapSceneToLocal(sceneProbe)}; " +
            $"ToolboxApplicable={IsUnassignedDestinationAvailable(fixture, canonicalProbe)}; moveApplicable={moveApplicable}";
    }

    private static async Task<ToolboxPlacementControllerResult> PlaceInUnassignedAsync(
        Fixture fixture, string toolboxName, PointD canonicalPoint) =>
        await PlaceInRegionAsync(fixture, null, toolboxName, canonicalPoint);

    private static async Task<ToolboxPlacementControllerResult> PlaceInRegionAsync(
        Fixture fixture, SemanticElementId? containerId, string toolboxName, PointD canonicalPoint)
    {
        fixture.ToolboxSelection.Select(new ToolboxItemId($"bpmn:toolbox:{toolboxName}"));
        var result = await fixture.Placement.TryPlaceAtCssPointAsync(
            fixture.Session, fixture.Css(Destination(fixture, containerId).MapLocalToScene(canonicalPoint)));
        await fixture.Session.WaitForIdleAsync();
        fixture.AssertReady();
        if (result.IsCommitted)
        {
            Assert.False(fixture.Placement.IsPlacementActive);
            Assert.Null(fixture.ToolboxSelection.SelectedItemId);
        }
        return result;
    }

    private static async Task<Canvas2DInteractionResult> TryMoveToUnassignedAsync(
        Fixture fixture, VisualStateId id, PointD canonicalCenter) =>
        await TryMoveToRegionAsync(fixture, id, null, canonicalCenter);

    private static async Task<Canvas2DInteractionResult> TryMoveToRegionAsync(
        Fixture fixture, VisualStateId id, SemanticElementId? containerId, PointD canonicalCenter)
    {
        var start = Center(fixture.Node(id).Bounds);
        var final = Destination(fixture, containerId).MapLocalToScene(canonicalCenter);
        await fixture.Controller.PointerPressedAsync(fixture.Pointer(start, buttons: 1));
        await fixture.Controller.PointerMovedAsync(fixture.Pointer(final, buttons: 1));
        var result = await fixture.Controller.PointerReleasedAsync(fixture.Pointer(final));
        await fixture.Session.WaitForIdleAsync();
        fixture.AssertReady();
        return result;
    }
}
