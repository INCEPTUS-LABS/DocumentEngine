using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class Canvas2DSpatialPoolInteractionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LeftmostNodeDragRetainsDroppedScenePositionWithoutSiblingDrift(int regionIndex)
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        var targetId = Fixture.RegionNodes[regionIndex * 2];
        var before = fixture.Document;
        var stateBefore = fixture.State;
        var sourceSceneObjectId = fixture.Node(targetId).Id;
        var displayedBefore = Fixture.RegionNodes.ToDictionary(id => id, id => fixture.Node(id).Bounds);
        var movement = new VectorD(40d, 20d);

        await fixture.DragAsync(fixture.NodePoint(targetId), movement);

        Assert.Equal(before.VisualModel.VisualStates.Single(visual => visual.Id == targetId).Position + movement,
            fixture.Visual(targetId).Position);
        Assert.Equal(displayedBefore[targetId].Translate(movement), fixture.Node(targetId).Bounds);
        Assert.Equal(sourceSceneObjectId, fixture.Node(targetId).Id);
        Assert.Equal(targetId, fixture.Node(targetId).Origin.VisualStateId);
        foreach (var id in Fixture.RegionNodes.Where(id => id != targetId))
        {
            Assert.Equal(before.VisualModel.VisualStates.Single(visual => visual.Id == id), fixture.Visual(id));
            Assert.Equal(displayedBefore[id], fixture.Node(id).Bounds);
        }
        Assert.Equal(before.SemanticModel.ProfileAssignments.ToArray(), fixture.Document.SemanticModel.ProfileAssignments.ToArray());
        Assert.Equal(before.VisualModel.ProfileElementPresentations.ToArray(), fixture.Document.VisualModel.ProfileElementPresentations.ToArray());
        Assert.Equal(stateBefore.DocumentRevision.Increment(), fixture.State.DocumentRevision);
        Assert.Equal(stateBefore.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
        Assert.Equal(targetId, Assert.Single(fixture.State.EditorState.Selection));
        Assert.Null(fixture.State.EditorState.SemanticSceneSelection);
        fixture.AssertReady();
    }

    [Theory]
    [InlineData(0.6d)]
    [InlineData(0.8d)]
    [InlineData(1d)]
    [InlineData(1.1d)]
    public async Task IdenticalDragDeltaAcrossAllRegionsUsesViewportInverseOnce(double zoom)
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        Assert.True((await fixture.Session.UpdateViewportAsync(
            new ViewportSnapshot(zoom, new VectorD(137d, -93d)))).Succeeded);
        var regions = fixture.State.CurrentScene!.SpatialPresentationPlan!.Regions;
        Assert.True(regions[1].LocalToSceneTransform != regions[0].LocalToSceneTransform);
        var movement = new VectorD(40d, 20d);
        foreach (var id in Fixture.RegionNodes.Where((_, index) => index % 2 == 0))
        {
            var before = fixture.Document;
            var original = fixture.Visual(id);
            var displayed = fixture.Node(id).Bounds;
            await fixture.DragAsync(fixture.NodePoint(id), movement);
            AssertPointNear(original.Position + movement, fixture.Visual(id).Position);
            AssertRectNear(displayed.Translate(movement), fixture.Node(id).Bounds);
            AssertUnrelatedVisualsUnchanged(before, fixture.Document, id);
            Assert.Equal(before.SemanticModel.ProfileAssignments.ToArray(), fixture.Document.SemanticModel.ProfileAssignments.ToArray());
        }
    }

    [Fact]
    public async Task RepeatedIndependentMovesPreserveUnrelatedCanonicalGeometryAndExactUndoRedo()
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        foreach (var id in Fixture.RegionNodes.Take(5))
        {
            var before = fixture.Document;
            var stateBefore = fixture.State;
            var displayed = fixture.Node(id).Bounds;
            var movement = new VectorD(30d, 14d);
            await fixture.DragWithPreviewAssertionAsync(id, movement);
            var after = fixture.Document;
            var displayedAfter = fixture.Node(id).Bounds;
            Assert.Equal(displayed.Translate(movement), displayedAfter);
            AssertUnrelatedVisualsUnchanged(before, after, id);
            Assert.Equal(before.SemanticModel.ProfileAssignments.ToArray(), after.SemanticModel.ProfileAssignments.ToArray());
            Assert.Equal(stateBefore.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
            Assert.Equal(stateBefore.DocumentRevision.Increment(), fixture.State.DocumentRevision);
            var projected = fixture.State.ProjectedGraph!.Nodes.Single(node => node.Source.VisualStateId == id);
            Assert.Equal(VisualBounds(fixture.Visual(id)),
                fixture.State.LayoutResult!.Nodes.Single(node => node.ProjectedObjectId == projected.Id).Bounds);

            Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
            await fixture.Session.WaitForIdleAsync();
            AssertPersistentGeometryAndAssignments(before, fixture.Document);
            Assert.Equal(displayed, fixture.Node(id).Bounds);
            Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
            await fixture.Session.WaitForIdleAsync();
            AssertPersistentGeometryAndAssignments(after, fixture.Document);
            Assert.Equal(displayedAfter, fixture.Node(id).Bounds);
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 1)]
    public async Task CrossRegionMoveCommitsExactDropAndAssignmentInOneHistoryEntry(
        int sourceNodeIndex, int destinationRegionIndex)
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        var id = Fixture.RegionNodes[sourceNodeIndex];
        var before = fixture.Document;
        var stateBefore = fixture.State;
        var original = fixture.Visual(id);
        var regions = fixture.State.CurrentScene!.SpatialPresentationPlan!.Regions;
        var destination = regions.Single(region => region.ContainerSemanticElementId ==
            (destinationRegionIndex == 0 ? Fixture.PoolA : destinationRegionIndex == 1 ? Fixture.PoolB : null));
        var targetCanonical = new PointD(250d, 110d);
        var targetBounds = destination.MapLocalToScene(new RectD(
            targetCanonical.X, targetCanonical.Y, original.Size.Width, original.Size.Height));
        var start = Center(fixture.Node(id).Bounds);
        var finalPoint = Center(targetBounds);
        await fixture.Controller.PointerPressedAsync(fixture.Pointer(start, buttons: 1));
        var preview = await fixture.Controller.PointerMovedAsync(fixture.Pointer(finalPoint, buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, preview.Status);
        Assert.Contains(fixture.State.CurrentScene!.Items, item =>
            item.Origin.VisualStateId == id && item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.StableSourceKey?.StartsWith("move-preview:", StringComparison.Ordinal) == true &&
            item.Bounds == targetBounds);
        Assert.Same(before, fixture.Document);
        var released = await fixture.Controller.PointerReleasedAsync(fixture.Pointer(finalPoint));
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        await fixture.Session.WaitForIdleAsync();
        fixture.AssertReady();
        var after = fixture.Document;

        Assert.Equal(targetCanonical, fixture.Visual(id).Position);
        Assert.Equal(targetBounds, fixture.Node(id).Bounds);
        Assert.Equal(original.Id, fixture.Visual(id).Id);
        Assert.Equal(original.SemanticElementId, fixture.Visual(id).SemanticElementId);
        AssertUnrelatedVisualsUnchanged(before, after, id);
        Assert.Equal(destination.ContainerSemanticElementId,
            after.SemanticModel.ProfileAssignments.SingleOrDefault(assignment =>
                assignment.SemanticElementId == original.SemanticElementId)?.ContainerSemanticElementId);
        Assert.Equal(stateBefore.DocumentRevision.Increment(), fixture.State.DocumentRevision);
        Assert.Equal(stateBefore.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await fixture.Session.WaitForIdleAsync();
        AssertPersistentGeometryAndAssignments(before, fixture.Document);
        Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
        await fixture.Session.WaitForIdleAsync();
        AssertPersistentGeometryAndAssignments(after, fixture.Document);
        Assert.Equal(targetBounds, fixture.Node(id).Bounds);
    }

    [Fact]
    public async Task ExplicitGroupMoveChangesOnlySelectedNodesWithoutImplicitPoolGrouping()
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        var selected = Fixture.RegionNodes.Take(2).ToArray();
        Assert.True((await fixture.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: selected, viewport: fixture.State.EditorState.Viewport))).Succeeded);
        var before = fixture.Document;
        var stateBefore = fixture.State;
        var displays = selected.ToDictionary(id => id, id => fixture.Node(id).Bounds);
        var movement = new VectorD(40d, 20d);
        await fixture.DragAsync(fixture.NodePoint(selected[0]), movement);

        foreach (var id in selected)
        {
            var original = before.VisualModel.VisualStates.Single(visual => visual.Id == id);
            Assert.Equal(original.Position + movement, fixture.Visual(id).Position);
            Assert.Equal(displays[id].Translate(movement), fixture.Node(id).Bounds);
        }
        AssertUnrelatedVisualsUnchanged(before, fixture.Document, selected);
        Assert.Equal(selected, fixture.State.EditorState.Selection);
        Assert.Equal(before.SemanticModel.ProfileAssignments.ToArray(), fixture.Document.SemanticModel.ProfileAssignments.ToArray());
        Assert.Equal(stateBefore.DocumentRevision.Increment(), fixture.State.DocumentRevision);
        Assert.Equal(stateBefore.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ResizingLeftmostNodeRetainsPresentationAndSiblingGeometry(int regionIndex)
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        var id = Fixture.RegionNodes[regionIndex * 2];
        await fixture.SelectAsync(id);
        var before = fixture.Document;
        var original = fixture.Visual(id);
        var displayed = fixture.Node(id).Bounds;
        var siblingDisplays = Fixture.RegionNodes.Where(other => other != id)
            .ToDictionary(other => other, other => fixture.Node(other).Bounds);
        var resize = fixture.State.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == id &&
            Text(item, Canvas2DResizeGestureMetadata.HandleRole) == "southeast");
        await fixture.DragAsync(Center(resize.Bounds), new VectorD(28d, 18d));

        Assert.Equal(original.Position, fixture.Visual(id).Position);
        Assert.Equal(new SizeD(original.Size.Width + 28d, original.Size.Height + 18d), fixture.Visual(id).Size);
        Assert.Equal(new RectD(displayed.Left, displayed.Top,
            fixture.Visual(id).Size.Width, fixture.Visual(id).Size.Height), fixture.Node(id).Bounds);
        AssertUnrelatedVisualsUnchanged(before, fixture.Document, id);
        foreach (var (other, bounds) in siblingDisplays)
        {
            Assert.Equal(bounds, fixture.Node(other).Bounds);
        }
        Assert.Equal(before.SemanticModel.ProfileAssignments.ToArray(), fixture.Document.SemanticModel.ProfileAssignments.ToArray());
    }

    [Fact]
    public async Task PoolGrowthMovesLowerPresentationOnlyAndKeepsCommonWidth()
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        var id = Fixture.RegionNodes[1];
        var before = fixture.Document;
        var planBefore = fixture.State.CurrentScene!.SpatialPresentationPlan!;
        var regionABefore = planBefore.Regions.Single(region => region.ContainerSemanticElementId == Fixture.PoolA);
        var regionBBefore = planBefore.Regions.Single(region => region.ContainerSemanticElementId == Fixture.PoolB);
        var bDisplayed = fixture.Node(Fixture.RegionNodes[2]).Bounds;
        // Keep the drop center inside the old body while extending the node's right/bottom edge.
        var movement = new VectorD(90d, 60d);
        var displayedBefore = fixture.Node(id).Bounds;
        await fixture.DragAsync(fixture.NodePoint(id), movement);
        var planAfter = fixture.State.CurrentScene!.SpatialPresentationPlan!;
        var regionAAfter = planAfter.Regions.Single(region => region.ContainerSemanticElementId == Fixture.PoolA);
        var regionBAfter = planAfter.Regions.Single(region => region.ContainerSemanticElementId == Fixture.PoolB);

        Assert.Equal(displayedBefore.Translate(movement), fixture.Node(id).Bounds);
        Assert.True(regionAAfter.Bounds.Height > regionABefore.Bounds.Height);
        Assert.True(regionAAfter.Bounds.Width > regionABefore.Bounds.Width);
        Assert.Equal(regionAAfter.Bounds.Width, regionBAfter.Bounds.Width);
        Assert.Equal(regionAAfter.Bounds.Height - regionABefore.Bounds.Height,
            regionBAfter.Bounds.Top - regionBBefore.Bounds.Top);
        Assert.Equal(bDisplayed.Translate(new VectorD(0d, regionBAfter.Bounds.Top - regionBBefore.Bounds.Top)),
            fixture.Node(Fixture.RegionNodes[2]).Bounds);
        AssertUnrelatedVisualsUnchanged(before, fixture.Document, id);
    }

    [Fact]
    public async Task ChangedSourceRegionTranslationRejectsAnInFlightMoveWithoutMutation()
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        var id = Fixture.RegionNodes[2];
        var before = fixture.Document;
        var stateBefore = fixture.State;
        var start = fixture.NodePoint(id);
        await fixture.Controller.PointerPressedAsync(fixture.Pointer(start, buttons: 1));
        await fixture.Controller.PointerMovedAsync(fixture.Pointer(start + new VectorD(40d, 20d), buttons: 1));
        Assert.True((await fixture.Session.UpdateModelProfileElementViewStateAsync(
            fixture.State.ModelProfileElementViewState.WithCollapsed(
                OrganizationalModelProfile.Id, Fixture.PoolA, true))).Succeeded);

        var release = await fixture.Controller.PointerReleasedAsync(fixture.Pointer(start + new VectorD(40d, 20d)));

        Assert.NotEqual(Canvas2DInteractionStatus.Committed, release.Status);
        Assert.Same(before, fixture.Document);
        Assert.Equal(stateBefore.HistoryStatus, fixture.State.HistoryStatus);
        Assert.Equal(stateBefore.DocumentRevision, fixture.State.DocumentRevision);
        Assert.Null(fixture.State.EditorState.ActiveGesture);
        fixture.AssertReady();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SourceAnchorUsesCanonicalPolicyAfterRegionMove(int regionIndex)
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        var id = Fixture.RegionNodes[regionIndex * 2];
        await fixture.DragAsync(fixture.NodePoint(id), new VectorD(40d, 20d));
        await fixture.SelectAsync(id);
        var before = fixture.Document;
        var node = fixture.Node(id);
        var context = await fixture.Controller.PointerContextMenuAsync(
            fixture.Css(new PointD(node.Bounds.Right, node.Bounds.Top + 25d)));
        var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(context.ConnectorAnchorContextAction);
        Assert.Equal(Canvas2DConnectorAnchorContextActionKind.AddAnchor, action.Kind);
        Assert.Equal(ConnectorAnchorSide.Right, action.Side);
        var state = fixture.State;
        var anchorId = new ConnectorAnchorId($"n101:drag:anchor:{regionIndex}");
        var target = state.CurrentScene!.Items.Single(item => item.Id == action.SourceSceneObjectId);
        var result = await fixture.Session.ExecuteForSceneTargetAsync(
            new AddConnectorAnchorCommand(state.DocumentId, state.DocumentRevision, id,
                anchorId, action.Side, ConnectorAnchorRole.Source, action.InsertionIndex),
            state.CurrentScene, state.Generation, target.Id, target.SpatialRegion);
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await fixture.Session.WaitForIdleAsync();

        Assert.Equal(VisualBounds(before.VisualModel.VisualStates.Single(visual => visual.Id == id)),
            VisualBounds(fixture.Visual(id)));
        var anchor = Assert.Single(fixture.Visual(id).ConnectorAnchors, anchor => anchor.Id == anchorId);
        Assert.Equal(ConnectorAnchorSide.Right, anchor.Side);
        Assert.Equal(ConnectorAnchorRole.Source, anchor.Role);
        Assert.Equal(node.Bounds, fixture.Node(id).Bounds);
        AssertUnrelatedVisualsUnchanged(before, fixture.Document, id);
        Assert.Equal(before.SemanticModel.ProfileAssignments.ToArray(), fixture.Document.SemanticModel.ProfileAssignments.ToArray());
        Assert.Equal(state.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
        Assert.Equal(state.DocumentRevision.Increment(), fixture.State.DocumentRevision);
        fixture.AssertReady();
    }

    private static void AssertUnrelatedVisualsUnchanged(
        DocumentSnapshot before, DocumentSnapshot after, params VisualStateId[] movedIds)
    {
        foreach (var visual in before.VisualModel.VisualStates.Where(visual => !movedIds.Contains(visual.Id)))
        {
            Assert.Equal(visual, after.VisualModel.VisualStates.Single(current => current.Id == visual.Id));
        }
    }

    private static RectD VisualBounds(VisualStateSnapshot visual) =>
        new(visual.Position.X, visual.Position.Y, visual.Size.Width, visual.Size.Height);

    private static void AssertPersistentGeometryAndAssignments(DocumentSnapshot expected, DocumentSnapshot actual)
    {
        Assert.Equal(expected.VisualModel.VisualStates.ToArray(), actual.VisualModel.VisualStates.ToArray());
        Assert.Equal(expected.SemanticModel.ProfileAssignments.ToArray(), actual.SemanticModel.ProfileAssignments.ToArray());
        Assert.Equal(expected.VisualModel.ProfileElementPresentations.ToArray(), actual.VisualModel.ProfileElementPresentations.ToArray());
    }

    private static void AssertPointNear(PointD expected, PointD actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
    }

    private static void AssertRectNear(RectD expected, RectD actual)
    {
        AssertPointNear(expected.TopLeft, actual.TopLeft);
        Assert.Equal(expected.Size, actual.Size);
    }

    private sealed partial class Fixture
    {
        internal static VisualStateId[] RegionNodes { get; } =
        [
            new("n101:drag:a1:visual"), new("n101:drag:a2:visual"),
            new("n101:drag:b1:visual"), new("n101:drag:b2:visual"),
            new("n101:drag:u1:visual"), new("n101:drag:u2:visual"),
        ];

        internal async Task DragWithPreviewAssertionAsync(VisualStateId id, VectorD movement)
        {
            var stateBefore = State;
            var originalDocument = Document;
            var originalBounds = Node(id).Bounds;
            var start = NodePoint(id);
            await Controller.PointerPressedAsync(Pointer(start, buttons: 1));
            var preview = await Controller.PointerMovedAsync(Pointer(start + movement, buttons: 1));
            Assert.Equal(Canvas2DInteractionStatus.Updated, preview.Status);
            Assert.Contains(State.CurrentScene!.Items, item =>
                item.Origin.VisualStateId == id && item.Layer == Canvas2DSceneLayer.Overlay &&
                item.Origin.StableSourceKey?.StartsWith("move-preview:", StringComparison.Ordinal) == true &&
                item.Bounds == originalBounds.Translate(movement));
            Assert.Same(originalDocument, Document);
            Assert.Same(stateBefore.LayoutResult, State.LayoutResult);
            Assert.Same(stateBefore.RoutingResult, State.RoutingResult);
            var released = await Controller.PointerReleasedAsync(Pointer(start + movement));
            Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
            await Session.WaitForIdleAsync();
            AssertReady();
        }

        internal static async Task<Fixture> CreateDragRegressionAsync()
        {
            var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
            var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(
                new DocumentId("n101:drag-regression:document")).Document);
            var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
            var attached = await EditingSession.AttachAsync(document, renderer, composition.Configuration);
            var fixture = new Fixture(Assert.IsType<EditingSession>(attached.Session), composition);
            Assert.Equal(EditingSessionAttachStatus.Ready, attached.Status);
            await fixture.ExecuteAsync(state => new SetModelProfileAvailabilityCommand(
                state.DocumentId, state.DocumentRevision,
                [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, true)]));
            for (var index = 0; index < RegionNodes.Length; index++)
            {
                var visualId = RegionNodes[index];
                var semanticId = new SemanticElementId(visualId.Value.Replace(":visual", string.Empty, StringComparison.Ordinal));
                var position = index % 2 == 0 ? new PointD(100d, 100d) : new PointD(460d, 180d);
                await fixture.ExecuteAsync(state => new CreateBpmnTaskCommand(
                    state.DocumentId, state.DocumentRevision, semanticId, visualId, position,
                    new SizeD(160d, 100d), $"T{index}", $"Task {index}", index,
                    VisualPlacementMode.Pinned, targetScopeId: state.ActiveScopeId));
            }
            await fixture.ExecuteAsync(state => new CreateOrganizationalPoolCommand(
                state.DocumentId, state.DocumentRevision, PoolA, state.ActiveScopeId,
                OrganizationalPoolCreationMode.AdoptEligibleUnassigned, "A"));
            await fixture.ExecuteAsync(state => new CreateOrganizationalPoolCommand(
                state.DocumentId, state.DocumentRevision, PoolB, state.ActiveScopeId,
                OrganizationalPoolCreationMode.Empty, "B"));
            foreach (var visualId in RegionNodes.Skip(2).Take(2))
            {
                await fixture.ExecuteAsync(state => new AssignOrganizationalElementCommand(
                    state.DocumentId, state.DocumentRevision, fixture.Visual(visualId).SemanticElementId, PoolB));
            }
            foreach (var visualId in RegionNodes.Skip(4))
            {
                await fixture.ExecuteAsync(state => new UnassignOrganizationalElementCommand(
                    state.DocumentId, state.DocumentRevision, fixture.Visual(visualId).SemanticElementId));
            }
            fixture.AssertReady();
            return fixture;
        }
    }
}
