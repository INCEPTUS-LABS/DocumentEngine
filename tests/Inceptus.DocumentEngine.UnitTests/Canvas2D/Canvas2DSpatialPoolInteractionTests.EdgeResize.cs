using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed partial class Canvas2DSpatialPoolInteractionTests
{
    [Theory]
    [InlineData("a", Canvas2DSpatialResizeEdge.Bottom, false)]
    [InlineData("b", Canvas2DSpatialResizeEdge.Bottom, true)]
    [InlineData("u", Canvas2DSpatialResizeEdge.Bottom, true)]
    [InlineData("a", Canvas2DSpatialResizeEdge.Right, false)]
    [InlineData("a", Canvas2DSpatialResizeEdge.Right, true)]
    [InlineData("b", Canvas2DSpatialResizeEdge.Right, true)]
    [InlineData("u", Canvas2DSpatialResizeEdge.Right, true)]
    public async Task SpatialEdgeResizeUsesTotalDeltaFinalUpAndOneHistoryAction(string owner, Canvas2DSpatialResizeEdge edge, bool collapse)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (collapse)
            Assert.True((await fixture.Session.UpdateModelProfileElementViewStateAsync(fixture.State.ModelProfileElementViewState
                .WithCollapsed(OrganizationalModelProfile.Id, Fixture.PoolA, true))).Succeeded);
        Assert.True((await fixture.Session.UpdateEditorStateAsync(new EditorStateSnapshot(viewport:
            new ViewportSnapshot(1.75, new VectorD(37, 19))))).Succeeded);
        var target = ResizeTarget(fixture, owner, edge);
        var grab = ResizePoint(target) + (edge == Canvas2DSpatialResizeEdge.Bottom ? new VectorD(0, -1) : new VectorD(-1, 0));
        var delta = edge == Canvas2DSpatialResizeEdge.Bottom ? new VectorD(91, 30) : new VectorD(30, 91);
        var before = fixture.Document;
        var beforeState = fixture.State;
        var bytes = NativeDocumentSerializer.Export(before);
        Assert.Equal(Canvas2DInteractionStatus.Updated, (await fixture.Controller.PointerPressedAsync(fixture.Pointer(grab, 1))).Status);
        foreach (var factor in new[] { 1d, 2d, 1d })
        {
            var move = await fixture.Controller.PointerMovedAsync(fixture.Pointer(grab + new VectorD(delta.X * factor, delta.Y * factor), 1));
            Assert.Equal(Canvas2DInteractionStatus.Updated, move.Status);
            var feedback = Assert.Single(fixture.State.EditorState.TemporaryFeedback).SpatialResize!;
            Assert.Equal(target.AuthoredExtent + 30d * factor, feedback.RequestedExtent, 8);
            Assert.Equal(target.Edge, feedback.Target.Edge);
            Assert.True(feedback.IsAllowed);
            Assert.Same(before, fixture.Document);
            Assert.Equal(beforeState.HistoryStatus, fixture.State.HistoryStatus);
            Assert.Equal(bytes.AsEnumerable(), NativeDocumentSerializer.Export(fixture.Document).AsEnumerable());
            var guides = ResizeGuides(fixture).ToArray();
            Assert.Equal(edge == Canvas2DSpatialResizeEdge.Bottom ? 2 : 3, guides.Length);
            Assert.All(guides, item =>
            {
                Assert.Equal(Canvas2DHitTestMode.None, item.HitTestPolicy.Mode);
                Assert.Equal(Canvas2DSceneOriginCategory.EditorState, item.Origin.Categories);
                Assert.Null(item.Origin.VisualStateId);
                Assert.Empty(item.Metadata);
            });
        }
        var finalDelta = edge == Canvas2DSpatialResizeEdge.Bottom ? new VectorD(-130, 45) : new VectorD(45, -130);
        var released = await fixture.Controller.PointerReleasedAsync(fixture.Pointer(grab + finalDelta));
        Assert.True(released.Status == Canvas2DInteractionStatus.Committed, Diagnostics(released.Diagnostics));
        await fixture.Session.WaitForIdleAsync();
        fixture.AssertReady();
        Assert.Equal(before.Revision.Increment(), fixture.Document.Revision);
        Assert.Equal(beforeState.HistoryStatus.EntryCount + 1, fixture.State.HistoryStatus.EntryCount);
        Assert.Empty(ResizeGuides(fixture));
        var currentTarget = ResizeTarget(fixture, owner, edge);
        Assert.Equal(target.AuthoredExtent + 45, currentTarget.AuthoredExtent, 8);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), fixture.Document.VisualModel.VisualStates.AsEnumerable());
        var after = fixture.Document;
        await fixture.Controller.PointerActivatedAsync(fixture.Css(grab + finalDelta));
        Assert.Same(after, fixture.Document);
        if (edge == Canvas2DSpatialResizeEdge.Right)
        {
            var right = 40 + currentTarget.AuthoredExtent;
            Assert.All(fixture.State.CurrentScene!.SpatialPresentationPlan!.Regions, region => Assert.Equal(right, region.Bounds.Right, 8));
            foreach (var scope in before.VisualModel.RoutingScopes!.Value)
            {
                var currentScope = after.VisualModel.RoutingScopes!.Value.Single(value => value.ScopeId == scope.ScopeId);
                Assert.Equal(scope.Connectors.AsEnumerable(), currentScope.Connectors.AsEnumerable());
                if (scope.ScopeId != beforeState.ActiveScopeId) Assert.Equal(scope, currentScope);
            }
        }
        Assert.True((await fixture.Session.UndoAsync()).IsCommitted);
        await fixture.Session.WaitForIdleAsync();
        Assert.Equal(target.AuthoredExtent, ResizeTarget(fixture, owner, edge).AuthoredExtent, 8);
        Assert.True((await fixture.Session.RedoAsync()).IsCommitted);
        await fixture.Session.WaitForIdleAsync();
        Assert.Equal(currentTarget.AuthoredExtent, ResizeTarget(fixture, owner, edge).AuthoredExtent, 8);
    }

    [Theory]
    [InlineData(Canvas2DSpatialResizeEdge.Bottom)]
    [InlineData(Canvas2DSpatialResizeEdge.Right)]
    public async Task SpatialEdgeResizeRedReleaseAndCancellationAreAtomic(Canvas2DSpatialResizeEdge edge)
    {
        await using var fixture = await Fixture.CreateAsync();
        var target = ResizeTarget(fixture, "u", edge);
        var start = ResizePoint(target);
        var before = fixture.Document;
        var history = fixture.State.HistoryStatus;
        var delta = target.Constraints.Minimum - target.AuthoredExtent - 1;
        var rejectedPoint = start + (edge == Canvas2DSpatialResizeEdge.Bottom ? new VectorD(0, delta) : new VectorD(delta, 0));
        await fixture.Controller.PointerPressedAsync(fixture.Pointer(start, 1));
        await fixture.Controller.PointerMovedAsync(fixture.Pointer(rejectedPoint, 1));
        Assert.False(Assert.Single(fixture.State.EditorState.TemporaryFeedback).SpatialResize!.IsAllowed);
        Assert.Contains(ResizeGuides(fixture), item => item.Style.Stroke == "#dc2626");
        Assert.DoesNotContain(fixture.State.CurrentScene!.Diagnostics, diagnostic => diagnostic.Severity == Contracts.Diagnostics.DiagnosticSeverity.Error);
        var release = await fixture.Controller.PointerReleasedAsync(fixture.Pointer(rejectedPoint));
        Assert.NotEqual(Canvas2DInteractionStatus.Committed, release.Status);
        Assert.Same(before, fixture.Document);
        Assert.Equal(history, fixture.State.HistoryStatus);
        Assert.Empty(ResizeGuides(fixture));
        await fixture.Controller.PointerPressedAsync(fixture.Pointer(start, 1));
        await fixture.Controller.PointerMovedAsync(fixture.Pointer(start + new VectorD(45, 45), 1));
        await fixture.Controller.CancelActiveGestureAsync();
        Assert.Same(before, fixture.Document);
        Assert.Equal(history, fixture.State.HistoryStatus);
        Assert.Empty(ResizeGuides(fixture));
    }

    [Fact]
    public async Task SpatialEdgeResizeTargetsFollowPaintedCompactVisibilityAndCornerTie()
    {
        await using var fixture = await Fixture.CreateAsync();
        var bottom = ResizeTarget(fixture, "a", Canvas2DSpatialResizeEdge.Bottom);
        Assert.Equal(40d, bottom.PaintedBounds.Left);
        Assert.Equal(78d, fixture.State.CurrentScene!.SpatialPresentationPlan!.Regions.Single(region => region.Id == bottom.AcquiredRegionId).Bounds.Left);
        var corner = new PointD(bottom.PaintedBounds.Right, bottom.PaintedBounds.Bottom);
        await fixture.Controller.PointerMovedAsync(fixture.Css(corner));
        Assert.Equal(Canvas2DSpatialResizeEdge.Bottom, Assert.Single(fixture.State.EditorState.TemporaryFeedback).SpatialResize!.Target.Edge);
        await fixture.Controller.PointerLeftAsync();
        Assert.Empty(ResizeGuides(fixture));
        await fixture.Session.UpdateModelProfileElementViewStateAsync(fixture.State.ModelProfileElementViewState
            .WithCollapsed(OrganizationalModelProfile.Id, Fixture.PoolA, true));
        var targets = fixture.State.CurrentScene!.SpatialPresentationPlan!.ResizeTargets;
        Assert.DoesNotContain(targets, target => target.AcquiredRegionId == bottom.AcquiredRegionId && target.Edge == Canvas2DSpatialResizeEdge.Bottom);
        Assert.Contains(targets, target => target.AcquiredRegionId == bottom.AcquiredRegionId && target.Edge == Canvas2DSpatialResizeEdge.Right);
        await fixture.Session.UpdateModelProfileViewStateAsync(fixture.State.ModelProfileViewState.WithPreferredVisibility(OrganizationalModelProfile.Id, false));
        Assert.Empty(fixture.State.CurrentScene!.SpatialPresentationPlan!.ResizeTargets);
    }

    [Theory]
    [InlineData("viewport")]
    [InlineData("collapse")]
    [InlineData("visibility")]
    public async Task SpatialEdgeResizeCancelsAfterPresentationCurrencyChanges(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var target = ResizeTarget(fixture, "b", Canvas2DSpatialResizeEdge.Bottom);
        var point = ResizePoint(target);
        var before = fixture.Document;
        await fixture.Controller.PointerPressedAsync(fixture.Pointer(point, 1));
        if (change == "viewport") await fixture.Session.UpdateEditorStateAsync(new EditorStateSnapshot(viewport: new ViewportSnapshot(1.5, default)));
        else if (change == "collapse") await fixture.Session.UpdateModelProfileElementViewStateAsync(fixture.State.ModelProfileElementViewState.WithCollapsed(OrganizationalModelProfile.Id, Fixture.PoolA, true));
        else await fixture.Session.UpdateModelProfileViewStateAsync(fixture.State.ModelProfileViewState.WithPreferredVisibility(OrganizationalModelProfile.Id, false));
        var released = await fixture.Controller.PointerReleasedAsync(fixture.Pointer(point + new VectorD(0, 60)));
        Assert.NotEqual(Canvas2DInteractionStatus.Committed, released.Status);
        Assert.Same(before, fixture.Document);
        Assert.Empty(ResizeGuides(fixture));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.75)]
    [InlineData(3)]
    public async Task SpatialEdgeResizeHitBandIsEightCssPixelsAndToolboxWins(double zoom)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Session.UpdateEditorStateAsync(new EditorStateSnapshot(viewport: new(zoom, new VectorD(31, -48))));
        var target = ResizeTarget(fixture, "b", Canvas2DSpatialResizeEdge.Bottom);
        var point = ResizePoint(target);
        foreach (var offset in new[] { -4d, 4d, -4.1d, 4.1d })
        {
            await fixture.Controller.PointerMovedAsync(fixture.Css(point) + new VectorD(0, offset));
            Assert.Equal(Math.Abs(offset) <= 4, fixture.State.EditorState.TemporaryFeedback.Any(feedback => feedback.SpatialResize is not null));
        }
        await fixture.Session.UpdateEditorStateAsync(new EditorStateSnapshot(activeToolId: "bpmn:toolbox:BPMN.Task", viewport: fixture.State.EditorState.Viewport));
        await fixture.Controller.PointerMovedAsync(fixture.Css(point));
        Assert.DoesNotContain(fixture.State.EditorState.TemporaryFeedback, feedback => feedback.SpatialResize is not null);
    }

    [Theory]
    [InlineData("no-change")]
    [InlineData("right-button")]
    [InlineData("pointer-cancel")]
    [InlineData("surface")]
    [InlineData("dispose")]
    public async Task SpatialEdgeResizeLifecycleRetiresFeedbackWithoutACommand(string boundary)
    {
        await using var fixture = await Fixture.CreateAsync();
        var target = ResizeTarget(fixture, "u", Canvas2DSpatialResizeEdge.Right);
        var point = ResizePoint(target);
        var document = fixture.Document;
        var history = fixture.State.HistoryStatus;
        await fixture.Controller.PointerPressedAsync(fixture.Pointer(point, 1));
        var sample = point + new VectorD(45, 0);
        if (boundary != "no-change") await fixture.Controller.PointerMovedAsync(fixture.Pointer(sample, 1));
        if (boundary == "right-button")
            await fixture.Controller.PointerPressedAsync(new Canvas2DPointerInput(8101, fixture.Css(sample), button: 2, buttons: 2));
        else if (boundary == "pointer-cancel") await fixture.Controller.PointerCancelledAsync(8101);
        else if (boundary == "dispose") await fixture.Controller.DisposeAsync();
        else
        {
            if (boundary == "surface") await fixture.Session.ResizeAsync(new(901, 601, 2));
            await fixture.Controller.PointerReleasedAsync(fixture.Pointer(boundary == "no-change" ? point : sample));
        }
        Assert.Same(document, fixture.Document);
        Assert.Equal(history, fixture.State.HistoryStatus);
        Assert.Null(fixture.State.EditorState.ActiveGesture);
        Assert.DoesNotContain(fixture.State.EditorState.TemporaryFeedback, feedback => feedback.SpatialResize is not null);
        Assert.Empty(ResizeGuides(fixture));
    }

    [Fact]
    public async Task SpatialEdgeResizeRetainsHiddenSelectionAndCapturedLeaveAndCoalescesOrthogonalSamples()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True((await fixture.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [Fixture.TaskB2]))).Succeeded);
        await fixture.Session.UpdateModelProfileElementViewStateAsync(fixture.State.ModelProfileElementViewState.WithCollapsed(
            OrganizationalModelProfile.Id, Fixture.PoolA, true));
        var target = ResizeTarget(fixture, "a", Canvas2DSpatialResizeEdge.Right);
        var point = ResizePoint(target);
        var selection = fixture.State.EditorState.Selection;
        var document = fixture.Document;
        await fixture.Controller.PointerPressedAsync(fixture.Pointer(point, 1));
        var state = fixture.State;
        var unchanged = await fixture.Controller.PointerMovedAsync(fixture.Pointer(point + new VectorD(0, 150), 1));
        Assert.Equal(Canvas2DInteractionStatus.Unchanged, unchanged.Status);
        Assert.Same(state.CurrentScene, fixture.State.CurrentScene);
        await fixture.Controller.PointerLeftAsync();
        Assert.NotNull(fixture.State.EditorState.ActiveGesture);
        Assert.Equal(Canvas2DInteractionStatus.Unchanged,
            (await fixture.Controller.PointerReleasedAsync(new(99, fixture.Css(point)))).Status);
        Assert.Same(document, fixture.Document);
        var released = await fixture.Controller.PointerReleasedAsync(fixture.Pointer(point + new VectorD(25, 150)));
        Assert.True(released.Status == Canvas2DInteractionStatus.Committed, Diagnostics(released.Diagnostics));
        await fixture.Session.WaitForIdleAsync();
        Assert.Equal(selection.AsEnumerable(), fixture.State.EditorState.Selection.AsEnumerable());
        Assert.Empty(ResizeGuides(fixture));
    }

    private static Canvas2DSpatialResizeTarget ResizeTarget(Fixture fixture, string owner, Canvas2DSpatialResizeEdge edge)
    {
        var plan = fixture.State.CurrentScene!.SpatialPresentationPlan!;
        SemanticElementId? container = owner == "a" ? Fixture.PoolA : owner == "b" ? Fixture.PoolB : null;
        var region = plan.Regions.Single(region => region.ContainerSemanticElementId == container);
        return plan.ResizeTargets.Single(target => target.AcquiredRegionId == region.Id && target.Edge == edge);
    }

    private static PointD ResizePoint(Canvas2DSpatialResizeTarget target) => target.Edge == Canvas2DSpatialResizeEdge.Bottom
        ? new PointD(target.PaintedBounds.Right - 30, target.PaintedBounds.Bottom)
        : new PointD(target.PaintedBounds.Right, target.PaintedBounds.Top + target.PaintedBounds.Height / 2);

    private static IEnumerable<Canvas2DSceneItem> ResizeGuides(Fixture fixture) => fixture.State.CurrentScene!.Items.Where(item =>
        item.Origin.StableSourceKey?.StartsWith("spatial-resize:", StringComparison.Ordinal) == true);
}
