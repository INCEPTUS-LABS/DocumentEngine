using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed partial class PhaseN101OrganizationalPoolIntegrationTests
{
    private static readonly VisualStateId StabilityA3 = new("n101:placement-stability:a3:visual");
    private static readonly VisualStateId StabilityBoundary = new("n101:placement-stability:boundary:visual");
    private static readonly VisualStateId StabilityFlow = new("n101:placement-stability:flow:visual");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlacementIntoAdoptedDemoPoolRetainsUnpinnedSiblingsAfterActualReviewOrderDrag(bool hidden)
    {
        await using var fixture = await DragFixture.CreateAdoptedDemoForPlacementAsync();
        await fixture.DragNodeAsync(BpmnDemoPipeline.TaskVisualId, new VectorD(40d, 20d));
        Assert.Contains(fixture.State.ProjectedGraph!.Nodes, node =>
            fixture.Visual(node.Source.VisualStateId!).PlacementMode != VisualPlacementMode.Pinned);
        if (hidden)
        {
            await SetPlacementGraphicsAsync(fixture, false);
        }
        var before = CapturePlacementState(fixture);

        var added = await AddStableToolboxElementAsync(fixture, PoolAId, "task", new PointD(1100d, 300d));

        AssertPlacementSiblingsExact(before, fixture);
        Assert.Equal(PoolAId, AssignedPool(fixture.Document, added.SemanticElementId));
        var after = CapturePlacementState(fixture);
        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(before.Document, fixture.Document);
        AssertPlacementSiblingsExact(before, fixture);
        Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
        await WaitForReadyAsync(fixture.Session);
        AssertEquivalentIgnoringRevision(after.Document, fixture.Document);
        AssertPlacementSiblingsExact(after, fixture);
        Assert.Equal(after.State.CurrentScene!.SpatialPresentationPlan, fixture.State.CurrentScene!.SpatialPresentationPlan);
    }

    [Fact]
    public async Task PlacementSequentialPoolUnassignedAndHiddenAddsPreserveExistingGeometryAndExactUndoRedo()
    {
        await using var fixture = await CreatePlacementStabilityFixtureAsync();
        var initial = CapturePlacementState(fixture);
        var steps = new List<(PlacementState Before, PlacementState After, VisualStateId Added)>();
        var preservedBoundary = fixture.Visual(StabilityBoundary);
        var preservedFlow = fixture.Visual(StabilityFlow);

        async Task AddAsync(SemanticElementId? poolId, string type, PointD center)
        {
            var before = CapturePlacementState(fixture);
            var path = PlacementConnectorPath(fixture, StabilityFlow);
            var added = await AddStableToolboxElementAsync(fixture, poolId, type, center);
            AssertPlacementSiblingsExact(before, fixture);
            Assert.Equal(preservedBoundary, fixture.Visual(StabilityBoundary));
            Assert.Equal(preservedFlow, fixture.Visual(StabilityFlow));
            Assert.Equal(path, PlacementConnectorPath(fixture, StabilityFlow));
            steps.Add((before, CapturePlacementState(fixture), added.Id));
        }

        await AddAsync(PoolAId, "task", new PointD(900d, 300d));
        await AddAsync(PoolAId, "timer-catch-event", new PointD(20d, 20d));
        await AddAsync(PoolBId, "task", new PointD(900d, 160d));
        await AddAsync(null, "task", new PointD(520d, 160d));
        await SetPlacementGraphicsAsync(fixture, false);
        await AddAsync(PoolAId, "task", new PointD(1100d, 300d));
        var hidden = CapturePlacementState(fixture);
        await SetPlacementGraphicsAsync(fixture, true);
        AssertPlacementSiblingsExact(hidden, fixture);
        Assert.Equal(hidden.State.CurrentScene!.SpatialPresentationPlan, fixture.State.CurrentScene!.SpatialPresentationPlan);
        var final = CapturePlacementState(fixture);
        Assert.Equal(initial.State.DocumentRevision.Value + 5, final.State.DocumentRevision.Value);
        Assert.Equal(initial.State.HistoryStatus.EntryCount + 5, final.State.HistoryStatus.EntryCount);

        foreach (var step in steps.AsEnumerable().Reverse())
        {
            Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
            await WaitForReadyAsync(fixture.Session);
            Assert.DoesNotContain(fixture.Document.VisualModel.VisualStates, visual => visual.Id == step.Added);
            AssertEquivalentIgnoringRevision(step.Before.Document, fixture.Document);
            AssertPlacementSiblingsExact(step.Before, fixture);
        }
        AssertEquivalentIgnoringRevision(initial.Document, fixture.Document);
        foreach (var step in steps)
        {
            Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
            await WaitForReadyAsync(fixture.Session);
            AssertEquivalentIgnoringRevision(step.After.Document, fixture.Document);
            AssertPlacementSiblingsExact(step.After, fixture);
        }
        AssertEquivalentIgnoringRevision(final.Document, fixture.Document);
        AssertPlacementSiblingsExact(final, fixture);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("above")]
    [InlineData("right")]
    [InlineData("below")]
    [InlineData("inside")]
    public async Task PlacementAtPoolExtremesChangesOnlyNewNodeAndUniformDerivedLowerRows(string position)
    {
        await using var fixture = await CreatePlacementStabilityFixtureAsync();
        var before = CapturePlacementState(fixture);
        var region = PlacementRegion(fixture, PoolAId);
        var localBand = region.MapSceneToLocal(region.Bounds);
        var center = position switch
        {
            "left" => new PointD(80d, 350d),
            "above" => new PointD(700d, 50d),
            "right" => new PointD(localBand.Right - 1d, 300d),
            "below" => new PointD(1100d, localBand.Bottom - 1d),
            _ => new PointD(700d, 300d),
        };

        await AddStableToolboxElementAsync(fixture, PoolAId, "task", center);

        AssertPlacementSiblingsExact(before, fixture);
        Assert.Equal(region.Id, PlacementRegion(fixture, PoolAId).Id);
        Assert.Equal(region.LocalToSceneTransform, PlacementRegion(fixture, PoolAId).LocalToSceneTransform);
        if (position == "below")
        {
            var lowerBefore = before.State.CurrentScene!.SpatialPresentationPlan!.Regions.Single(
                candidate => candidate.ContainerSemanticElementId == PoolBId);
            Assert.True(PlacementRegion(fixture, PoolBId).LocalToSceneTransform.OffsetY > lowerBefore.LocalToSceneTransform.OffsetY);
        }
    }

    [Fact]
    public async Task PlacementRepeatedMixedAddsLeaveBothPoolAndUnassignedReusableWithoutSiblingDrift()
    {
        await using var fixture = await CreatePlacementStabilityFixtureAsync();
        var types = new[] { "task", "task", "task", "timer-catch-event", "exclusive-gateway" };
        foreach (var poolId in new SemanticElementId?[] { PoolAId, PoolBId, null })
        {
            for (var index = 0; index < types.Length; index++)
            {
                var before = CapturePlacementState(fixture);
                await AddStableToolboxElementAsync(fixture, poolId, types[index], new PointD(650d + (index * 150d), 100d));
                AssertPlacementSiblingsExact(before, fixture);
            }
        }

        var unassignedId = PlacementRegion(fixture, null).Id;
        await MoveToRegionAsync(fixture, DragFixture.TaskIds[2], null, new PointD(450d, 100d), unassignedId);
        await MoveToRegionAsync(fixture, DragFixture.TaskIds[2], PoolBId, new PointD(180d, 160d), unassignedId);
        var beforeFinal = CapturePlacementState(fixture);
        await AddStableToolboxElementAsync(fixture, null, "start-event", new PointD(1100d, 240d));
        AssertPlacementSiblingsExact(beforeFinal, fixture);
    }

    private static async Task<DragFixture> CreatePlacementStabilityFixtureAsync()
    {
        var fixture = await DragFixture.CreateAsync(includeUnassignedTasks: false);
        foreach (var (id, semanticId, position) in new[]
        {
            (StabilityA3, new SemanticElementId("n101:placement-stability:a3"), new PointD(900d, 450d)),
            (DragFixture.TaskIds[4], new SemanticElementId("n101:placement-stability:u1"), new PointD(180d, 160d)),
        })
        {
            await fixture.ExecuteAsync(state => new CreateBpmnTaskCommand(
                state.DocumentId, state.DocumentRevision, semanticId, id, position, new SizeD(160d, 100d),
                id.Value, id.Value, id == StabilityA3 ? 701 : 702, VisualPlacementMode.Pinned,
                targetScopeId: state.ActiveScopeId));
        }
        await fixture.ExecuteAsync(state => new AssignOrganizationalElementCommand(
            state.DocumentId, state.DocumentRevision, fixture.Visual(StabilityA3).SemanticElementId, PoolAId));
        var owner = fixture.Visual(DragFixture.TaskIds[0]);
        await fixture.ExecuteAsync(state => new CreateBpmnTimerBoundaryEventCommand(
            state.DocumentId, state.DocumentRevision, new SemanticElementId("n101:placement-stability:boundary"),
            StabilityBoundary, owner.SemanticElementId, BoundaryAttachmentSide.Bottom, 0.4d,
            new RectD(owner.Position.X, owner.Position.Y, owner.Size.Width, owner.Size.Height),
            "Timer", "PT5M", targetScopeId: state.ActiveScopeId));
        var sourceAnchor = new ConnectorAnchorId("n101:placement-stability:source");
        var targetAnchor = new ConnectorAnchorId("n101:placement-stability:target");
        await fixture.ExecuteAsync(state => new AddConnectorAnchorCommand(
            state.DocumentId, state.DocumentRevision, owner.Id, sourceAnchor,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0));
        await fixture.ExecuteAsync(state => new AddConnectorAnchorCommand(
            state.DocumentId, state.DocumentRevision, StabilityA3, targetAnchor,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0));
        await fixture.ExecuteAsync(state => new CreateBpmnSequenceFlowCommand(
            state.DocumentId, state.DocumentRevision, new SemanticElementId("n101:placement-stability:flow"),
            StabilityFlow, owner.SemanticElementId, fixture.Visual(StabilityA3).SemanticElementId,
            sourceAnchor, targetAnchor));
        await fixture.ExecuteAsync(state => new UpdateConnectionRouteCommand(
            state.DocumentId, state.DocumentRevision, StabilityFlow,
            [new PointD(200d, 450d), new PointD(250d, 600d), new PointD(850d, 600d), new PointD(900d, 500d)]));
        return fixture;
    }

    private static Canvas2DSpatialRegion PlacementRegion(DragFixture fixture, SemanticElementId? poolId) =>
        fixture.State.CurrentScene!.SpatialPresentationPlan!.Regions.Single(region => region.ContainerSemanticElementId == poolId);

    private static PointD[] PlacementConnectorPath(DragFixture fixture, VisualStateId id)
    {
        var item = fixture.Connector(id);
        return Canvas2DConnectorPathMetadata.Resolve(item).Select(item.Transform.TransformPoint).ToArray();
    }

    private static async Task<VisualStateSnapshot> AddStableToolboxElementAsync(
        DragFixture fixture, SemanticElementId? poolId, string type, PointD canonicalCenter)
    {
        var before = fixture.State;
        var region = PlacementRegion(fixture, poolId);
        var scenePoint = region.MapLocalToScene(canonicalCenter);
        Assert.True(region.Bounds.Contains(scenePoint), $"{canonicalCenter} is outside {region.MapSceneToLocal(region.Bounds)}.");
        fixture.ToolboxSelection.Select(new ToolboxItemId($"bpmn:toolbox:{type}"));
        var result = await fixture.Placement.TryPlaceAtCssPointAsync(fixture.Session, fixture.Css(scenePoint));
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await WaitForReadyAsync(fixture.Session);
        var visual = fixture.Visual(Assert.IsType<VisualStateId>(result.CreatedVisualStateId));
        var halfSize = new VectorD(visual.Size.Width / 2d, visual.Size.Height / 2d);
        Assert.Equal(canonicalCenter - halfSize, visual.Position);
        Assert.Equal(scenePoint - halfSize, fixture.Node(visual.Id).Bounds.TopLeft);
        Assert.Equal(poolId, AssignedPool(fixture.Document, visual.SemanticElementId));
        Assert.Equal(before.DocumentRevision.Increment(), fixture.State.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
        Assert.Equal(visual.Id, Assert.Single(fixture.State.EditorState.Selection));
        Assert.Null(fixture.State.EditorState.SemanticSceneSelection);
        Assert.Equal(before.ActiveScopeId, fixture.State.ActiveScopeId);
        return visual;
    }

    private static async Task SetPlacementGraphicsAsync(DragFixture fixture, bool visible)
    {
        var document = fixture.Document;
        var before = fixture.State;
        Assert.True((await fixture.Session.UpdateModelProfileViewStateAsync(
            before.ModelProfileViewState.WithPreferredVisibility(OrganizationalModelProfile.Id, visible))).Succeeded);
        await WaitForReadyAsync(fixture.Session);
        Assert.Same(document, fixture.Document);
        Assert.Equal(before.HistoryStatus, fixture.State.HistoryStatus);
        Assert.Equal(before.CurrentScene!.SpatialPresentationPlan, fixture.State.CurrentScene!.SpatialPresentationPlan);
    }

    private static PlacementState CapturePlacementState(DragFixture fixture) => new(fixture.Document, fixture.State);

    private static void AssertPlacementSiblingsExact(PlacementState before, DragFixture fixture)
    {
        AssertUnrelatedVisualsUnchanged(before.Document, fixture.Document);
        var layout = fixture.State.LayoutResult!.Nodes.ToDictionary(node => node.ProjectedObjectId);
        foreach (var node in before.State.LayoutResult!.Nodes)
        {
            Assert.True(layout.TryGetValue(node.ProjectedObjectId, out var current));
            Assert.True(node.Bounds == current!.Bounds,
                $"Retained node {node.ProjectedObjectId} changed from {node.Bounds} to {current.Bounds}.");
        }
        foreach (var item in before.State.CurrentScene!.Items.Where(Canvas2DNodeBodyMetadata.IsNodeBody))
        {
            var visualId = Assert.IsType<VisualStateId>(item.Origin.VisualStateId);
            var current = fixture.Node(visualId);
            Assert.NotNull(item.SpatialRegion);
            Assert.NotNull(current.SpatialRegion);
            Assert.Equal(item.SpatialRegion.Id, current.SpatialRegion.Id);
            var translation = new VectorD(
                current.SpatialRegion.LocalToSceneTransform.OffsetX - item.SpatialRegion.LocalToSceneTransform.OffsetX,
                current.SpatialRegion.LocalToSceneTransform.OffsetY - item.SpatialRegion.LocalToSceneTransform.OffsetY);
            Assert.Equal(item.Bounds.Translate(translation), current.Bounds);
        }
        Assert.Equal(before.State.ActiveScopeId, fixture.State.ActiveScopeId);
        Assert.Equal(fixture.Document.Revision, fixture.State.CurrentScene!.SourceRevision);
        Assert.True(before.Document.SemanticModel.Relationships.AsSpan().SequenceEqual(fixture.Document.SemanticModel.Relationships.AsSpan()));
        Assert.True(before.Document.VisualModel.ProfileElementPresentations.AsSpan().SequenceEqual(
            fixture.Document.VisualModel.ProfileElementPresentations.AsSpan()));
    }

    private sealed record PlacementState(DocumentSnapshot Document, EditingSessionState State);

    private sealed partial class DragFixture
    {
        internal static async Task<DragFixture> CreateAdoptedDemoForPlacementAsync()
        {
            var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
            var attached = await EditingSession.AttachAsync(composition.Document, await CreateRendererAsync(), composition.Configuration);
            Assert.Equal(EditingSessionAttachStatus.Ready, attached.Status);
            var fixture = new DragFixture(Assert.IsType<EditingSession>(attached.Session), composition);
            await fixture.ExecuteAsync(state => new SetModelProfileAvailabilityCommand(
                state.DocumentId, state.DocumentRevision,
                [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, true)]));
            await fixture.ExecuteAsync(state => new CreateOrganizationalPoolCommand(
                state.DocumentId, state.DocumentRevision, PoolAId, state.ActiveScopeId,
                OrganizationalPoolCreationMode.AdoptEligibleUnassigned, "Adopted demo"));
            return fixture;
        }
    }
}
