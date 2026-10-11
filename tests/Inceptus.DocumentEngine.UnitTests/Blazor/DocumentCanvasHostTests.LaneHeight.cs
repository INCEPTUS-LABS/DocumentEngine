using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("x1", 1d, 160d)]
    [InlineData("x1", 0.4d, 160d)]
    [InlineData("x2", 1d, 160d)]
    [InlineData("x2", 0.4d, -40d)]
    [InlineData("x3", 1d, 160d)]
    [InlineData("x3", 0.4d, -40d)]
    public async Task LaneHeightGesturePreservesParticipantOrderAndReplaysExactGeometry(string lane, double zoom, double delta)
    {
        await using var host = CreatePropertiesUxHost(await OrderingFixtureAsync(historicalGap: 120));
        await host.InitializeAsync("lane-height", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var region = OrderingRegions(before).Single(region => region.ContainerSemanticElementId!.Value == lane);
        await session.UpdateViewportAsync(new ViewportSnapshot(zoom, new VectorD(0, 150 - region.ContentBounds!.Value.Bottom * zoom)));
        await session.WaitForIdleAsync();
        var state = session.CaptureState();
        var target = state.CurrentScene!.SpatialPresentationPlan!.ResizeTargets.Single(target =>
            target.Edge == Canvas2DSpatialResizeEdge.Bottom && target.AuthorityRegionId == region.Id);
        var start = new PointD(target.PaintedBounds.Left + target.PaintedBounds.Width / 2, target.PaintedBounds.Bottom);
        var end = start + new VectorD(0, delta);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(state.CurrentScene, start);
        Assert.Equal(Canvas2DSpatialResizeFeedback.FeedbackKind, session.CaptureState().EditorState.ActiveGesture!.Kind);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, end, buttons: 1);
        var feedback = Assert.Single(session.CaptureState().EditorState.TemporaryFeedback, item => item.SpatialResize is not null).SpatialResize!;
        Assert.True(feedback.IsAllowed);
        Assert.Equal(region.ExpandedHeight + delta, feedback.RequestedExtent, precision: 8);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(state.HistoryStatus, session.CaptureState().HistoryStatus);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, end, 1);
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(state.HistoryStatus.EntryCount + 1, session.CaptureState().HistoryStatus.EntryCount);
        AssertLaneHeightGeometry(before, after, region, delta);
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            await host.UndoAsync();
            AssertOrderingVisuals(before, document.CaptureSnapshot());
            await host.RedoAsync();
            AssertOrderingVisuals(after, document.CaptureSnapshot());
        }
        var opened = NativeDocumentSerializer.Import(NativeDocumentSerializer.Export(after).AsMemory());
        Assert.True(opened.Succeeded);
        await using var reopened = CreatePropertiesUxHost(opened.Document!.CaptureSnapshot());
        await reopened.InitializeAsync("reopened-lane-height", "container");
        Assert.Equal(after, AttachedDocument(Session(reopened)).CaptureSnapshot());
    }

    [Theory]
    [InlineData("a", 160d)]
    [InlineData("a", -40d)]
    [InlineData("b", 160d)]
    [InlineData("b", -40d)]
    [InlineData("y2", 160d)]
    [InlineData("y2", -40d)]
    public async Task LaneHeightCommandPreservesFollowingGroupsAndAuthoredGaps(string pool, double delta)
    {
        await using var host = CreatePropertiesUxHost(await OrderingFixtureAsync(historicalGap: 120));
        await host.InitializeAsync("pool-height-with-groups", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var region = OrderingRegions(before).Single(region => region.ContainerSemanticElementId!.Value == pool);
        var result = await session.ExecuteAsync(new SetOrganizationalRegionExpandedHeightCommand(before.DocumentId, before.Revision,
            session.CaptureState().ActiveScopeId, region.Id, region.ExpandedHeight + delta));
        Assert.True(result.IsCommitted, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        AssertLaneHeightGeometry(before, after, region, delta);
        await host.UndoAsync();
        AssertOrderingVisuals(before, document.CaptureSnapshot());
        await host.RedoAsync();
        AssertOrderingVisuals(after, document.CaptureSnapshot());
    }

    private static void AssertLaneHeightGeometry(DocumentSnapshot before, DocumentSnapshot after,
        SpatialRegionGeometrySnapshot resized, double delta)
    {
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), after.SemanticModel.Relationships);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), after.VisualModel.VisualStates);
        Assert.Equal(before.VisualModel.ProfileElementPresentations.AsEnumerable(), after.VisualModel.ProfileElementPresentations);
        Assert.Equal(OrderingRegions(before).Select(region => region.Id), OrderingRegions(after).Select(region => region.Id));
        var oldRegions = before.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions.ToDictionary(region => region.Id);
        foreach (var region in after.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions)
        {
            var old = oldRegions[region.Id];
            var offset = old.ContentBounds!.Value.Top > resized.ContentBounds!.Value.Top ? delta : 0;
            Assert.Equal(old.ExpandedHeight + (old.Id == resized.Id ? delta : 0), region.ExpandedHeight, precision: 8);
            Assert.Equal(old.ContentBounds.Value.Top + offset, region.ContentBounds!.Value.Top, precision: 8);
            Assert.Equal(old.LocalToScopeTransform!.Value.OffsetY + offset, region.LocalToScopeTransform!.Value.OffsetY, precision: 8);
            Assert.Equal(old.LocalToScopeTransform.Value.OffsetX, region.LocalToScopeTransform.Value.OffsetX);
            Assert.Equal(old.ContentBounds.Value.Width, region.ContentBounds.Value.Width);
        }
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(double.MaxValue)]
    [InlineData(270d)]
    public async Task LaneHeightRejectedOrUnchangedCommandDoesNotShiftAnyGroup(double height)
    {
        await using var host = CreatePropertiesUxHost(await OrderingFixtureAsync());
        await host.InitializeAsync("lane-height-atomic", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var state = session.CaptureState();
        var region = OrderingRegions(before).Single(region => region.ContainerSemanticElementId!.Value == "x1");
        var result = await session.ExecuteAsync(new SetOrganizationalRegionExpandedHeightCommand(before.DocumentId, before.Revision,
            state.ActiveScopeId, region.Id, height));
        Assert.False(result.IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(state.HistoryStatus, session.CaptureState().HistoryStatus);
    }
}
