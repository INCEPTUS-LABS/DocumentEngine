using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class Canvas2DSpatialPoolInteractionTests
{
    [Fact]
    public async Task CompactUnassignedGroupUsesOneClampForPreviewReleaseAndAcceptedPersistentPositions()
    {
        await using var fixture = await Fixture.CreateDragRegressionAsync();
        var expandedBottom = Unassigned(fixture).Bounds.Bottom;
        Assert.True((await fixture.Session.UpdateModelProfileElementViewStateAsync(
            fixture.State.ModelProfileElementViewState.WithCollapsed(
                OrganizationalModelProfile.Id, Fixture.PoolA, true))).Succeeded);
        var selected = Fixture.RegionNodes.Skip(4).ToArray();
        Assert.True((await fixture.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: selected, viewport: new ViewportSnapshot(1.75, new VectorD(37, -19))))).Succeeded);
        var before = fixture.Document;
        var stateBefore = fixture.State;
        var plan = stateBefore.CurrentScene!.SpatialPresentationPlan!;
        var bottom = Unassigned(fixture).Bounds.Bottom;
        Assert.True(bottom < expandedBottom);
        Assert.False(plan.CoordinateMap.IsIdentity);
        Assert.Equal(Unassigned(fixture).Id, plan.MovementBottomBoundaryRegionId);
        var originalBounds = selected.ToDictionary(id => id, id => fixture.Node(id).Bounds);
        var delta = new VectorD(0, bottom - originalBounds.Values.Max(static bounds => bounds.Bottom));
        Assert.True(delta.Y > 0);
        var start = fixture.NodePoint(selected[0]);

        await fixture.Controller.PointerPressedAsync(fixture.Pointer(start, buttons: 1));
        var moved = await fixture.Controller.PointerMovedAsync(fixture.Pointer(start + new VectorD(0, 2000), buttons: 1));
        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        AssertPointNear(start + delta, fixture.State.EditorState.ActiveGesture!.Current);
        foreach (var id in selected)
        {
            var preview = Assert.Single(fixture.State.CurrentScene!.Items, item =>
                item.Origin.VisualStateId == id && item.Layer == Canvas2DSceneLayer.Overlay &&
                item.Origin.RelatedSceneObjectIds.Contains(fixture.Node(id).Id) &&
                item.Origin.StableSourceKey?.StartsWith("move-preview:", StringComparison.Ordinal) == true);
            AssertRectNear(originalBounds[id].Translate(delta), preview.Bounds);
        }
        Assert.Same(before, fixture.Document);
        var released = await fixture.Controller.PointerReleasedAsync(fixture.Pointer(start + new VectorD(0, 3000)));
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        await fixture.Session.WaitForIdleAsync();
        fixture.AssertReady();
        foreach (var id in selected)
        {
            var original = before.VisualModel.VisualStates.Single(visual => visual.Id == id);
            AssertPointNear(original.Position + delta, fixture.Visual(id).Position);
            AssertRectNear(originalBounds[id].Translate(delta), fixture.Node(id).Bounds);
        }
        Assert.Equal(bottom, selected.Max(id => fixture.Node(id).Bounds.Bottom), 8);
        AssertUnrelatedVisualsUnchanged(before, fixture.Document, selected);
        Assert.Equal(stateBefore.DocumentRevision.Increment(), fixture.State.DocumentRevision);
        Assert.Equal(stateBefore.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await fixture.Session.WaitForIdleAsync();
        AssertPersistentGeometryAndAssignments(before, fixture.Document);
    }
}
