using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class ManualRoutePointDragIntegrationTests
{
    [Theory]
    [InlineData(0.8, 1d, false)]
    [InlineData(1d, 1.25, false)]
    [InlineData(1.5, 2d, false)]
    [InlineData(0.8, 2d, true)]
    [InlineData(1d, 1d, true)]
    [InlineData(1.5, 1.25, true)]
    public async Task MagneticAndCtrlModesAreAlternativesWithExactFinalRelease(double zoom, double dpr, bool ctrl)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var owner = await PrepareAsync(test, false, zoom);
        Assert.True((await test.Session.ResizeAsync(new Canvas2DSurfaceSize(1000, 700, dpr))).Succeeded);
        var path = BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner).Path;
        PointD[] original = [path[0], new(500, 114), new(600, 114), new(600, 220), new(700, 240), path[^1]];
        await test.ExecuteAsync(new UpdateConnectionRouteCommand(test.Snapshot.DocumentId, test.Snapshot.Revision, owner, original));
        var before = test.Snapshot;
        var state = test.State;
        var events = test.Events.Count;
        var start = original[2];
        await using var controller = test.CreateInteractionController();
        await controller.PointerMovedAsync(Pointer(test, start));
        await controller.PointerPressedAsync(Pointer(test, start));
        var uploads = test.Execution.FullUploadCount;
        var bounded = test.Execution.ViewportRenderCount;
        var full = test.Pipeline.FullRuns;
        var contributors = test.Contributions.Sum(item => item.Calls + item.Routes);

        await Move(630, 12, false, false); // outside: changed horizontal is diagonal
        await Move(631, 5, ctrl, !ctrl); // Ctrl bypasses snapping even within the entry radius
        for (var i = 0; i < 20; i++) await Move(632 + i, 8, ctrl, !ctrl);
        // Switching modes recomputes from the original path and clears magnetic hysteresis.
        await Move(652, 8, true, false);
        await Move(652, 8, false, false); // fresh entry radius, not the old retained target
        await Move(652, 5, false, true);
        await Move(652, 5, true, false);
        await Move(652, 10, false, false); // release
        await Move(653, 7, false, false); // cannot enter yet
        await Move(654, 5, false, true);
        Assert.Equal(uploads, test.Execution.FullUploadCount);
        Assert.Equal(bounded + 29, test.Execution.ViewportRenderCount);
        Assert.Equal(full, test.Pipeline.FullRuns);
        Assert.Equal(contributors, test.Contributions.Sum(item => item.Calls + item.Routes));
        Assert.Equal(events, test.Events.Count);
        await AssertFullOracleAsync(test);

        // Up samples its own position and modifier: it either retains this target,
        // or switches to raw Ctrl movement without a preceding Ctrl Move.
        Assert.Equal(Canvas2DInteractionStatus.Committed,
            (await controller.PointerReleasedAsync(Pointer(test, new(680, 114 + 8 / zoom), ctrl))).Status);
        await test.Session.WaitForIdleAsync();
        var saved = BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner);
        var finalY = ctrl ? 114 + 8 / zoom : 114;
        if (ctrl) Assert.Equal(finalY, saved.Path[2].Y, 8);
        else Assert.Equal(114d, saved.Path[2].Y); // exact authored neighbour equality
        Assert.Equal(680d, saved.Path[2].X, 8);
        Assert.Equal(500d, saved.Path[1].X);
        Assert.Equal(finalY, saved.Path[1].Y, 8); // raw Ctrl dy moves the horizontal neighbour
        Assert.Equal(ctrl ? 680d : 600d, saved.Path[3].X, 8);
        foreach (var index in new[] { 0, 4, 5 }) Assert.Equal(original[index], saved.Path[index]);
        Assert.Equal(saved.Path.Skip(1).SkipLast(1).ToArray(), saved.ManualDefinition!.Value.ToArray());
        Assert.Equal(before.Revision.Increment(), test.Snapshot.Revision);
        Assert.Equal(events + 1, test.Events.Count);
        Assert.Equal(state.HistoryStatus.EntryCount + 1, test.State.HistoryStatus.EntryCount);
        var committedRoute = saved;
        Assert.True((await test.Session.UndoAsync()).IsCommitted);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(original, BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner).Path.AsEnumerable());
        Assert.Equal(ConnectorRoutingType.Manual, BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner).RoutingType);
        Assert.True((await test.Session.RedoAsync()).IsCommitted);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(committedRoute, BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner));
        foreach (var scope in before.VisualModel.RoutingScopes!.Value)
            Assert.Equal(scope.Connectors.Where(item => item.VisualStateId != owner).ToArray(),
                test.Snapshot.VisualModel.RoutingScopes!.Value.Single(item => item.ScopeId == scope.ScopeId)
                    .Connectors.Where(item => item.VisualStateId != owner).ToArray());
        Assert.DoesNotContain(test.State.CurrentScene!.Items, IsPreview);
        Assert.All(test.State.CurrentScene.Items.Where(item => item.Layer == Canvas2DSceneLayer.Connector),
            item => Assert.Empty(item.Style.DashPattern));

        async Task Move(double x, double cssY, bool modifier, bool snapped)
        {
            Assert.Equal(Canvas2DInteractionStatus.Updated,
                (await controller.PointerMovedAsync(Pointer(test, new(x, 114 + cssY / zoom), modifier))).Status);
            Assert.Same(before, test.Snapshot);
            Assert.Equal(state.HistoryStatus, test.State.HistoryStatus);
            Assert.Same(state.ProjectedGraph, test.State.ProjectedGraph);
            Assert.Same(state.LayoutResult, test.State.LayoutResult);
            Assert.Same(state.RoutingResult, test.State.RoutingResult);
            var active = test.State.CurrentScene!.Items.Single(item => item.Origin.VisualStateId == owner &&
                item.Metadata.ContainsKey(Canvas2DRouteGestureMetadata.HandleRole) &&
                item.Metadata[Canvas2DRouteGestureMetadata.BendIndex].IntegerValue == 2);
            var expected = new PointD(x, snapped ? 114 : 114 + cssY / zoom);
            Assert.Equal(expected.X, Center(active.Bounds).X, 8);
            Assert.Equal(expected.Y, Center(active.Bounds).Y, 8);
            var segment = Assert.Single(test.State.CurrentScene.Items.Where(IsPreview), item =>
                Enumerable.Range(0, item.Geometry.Points.Length - 1).Any(i =>
                    Math.Abs(item.Geometry.Points[i].X - 500) < 1e-8 &&
                    Math.Abs(item.Geometry.Points[i + 1].X - x) < 1e-8));
            Assert.Equal(!snapped && !modifier, !segment.Style.DashPattern.IsEmpty);
        }
    }

    [Theory]
    [InlineData(1, 1d)]
    [InlineData(2, 1.1d)]
    [InlineData(3, 1.75d)]
    [InlineData(4, 0.8d)]
    public async Task LiveCtrlUsesOriginalPointsAndCommitsOneAtomicPath(int bend, double zoom)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var owner = await PrepareAsync(test, false, zoom);
        var path = BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner).Path;
        System.Collections.Immutable.ImmutableArray<PointD> original =
            [path[0], new(500, 114), new(600, 114), new(600, 220), new(700, 240), path[^1]];
        await test.ExecuteAsync(new UpdateConnectionRouteCommand(test.Snapshot.DocumentId,
            test.Snapshot.Revision, owner, original));
        var before = test.Snapshot;
        var state = test.State;
        var events = test.Events.Count;
        var connector = Connector(test, owner);
        var start = Center(test.State.CurrentScene!.Items.Single(item =>
            item.Origin.VisualStateId == owner && item.Metadata.ContainsKey(Canvas2DRouteGestureMetadata.HandleRole) &&
            item.Metadata[Canvas2DRouteGestureMetadata.BendIndex].IntegerValue == bend).Bounds);
        await using var controller = test.CreateInteractionController();
        await controller.PointerMovedAsync(Pointer(test, start));
        await controller.PointerPressedAsync(Pointer(test, start));
        var uploads = test.Execution.FullUploadCount;
        var bounded = test.Execution.ViewportRenderCount;
        for (var i = 1; i <= 20; i++)
        {
            var delta = new VectorD(20 + i, 30);
            foreach (var ctrl in new[] { false, true, false, true })
            {
                Assert.Equal(Canvas2DInteractionStatus.Updated,
                    (await controller.PointerMovedAsync(Pointer(test, start + delta, ctrl))).Status);
                AssertCandidate(delta, ctrl);
            }
            Assert.Same(before, test.Snapshot);
            Assert.Equal(state.HistoryStatus, test.State.HistoryStatus);
            Assert.Same(state.ProjectedGraph, test.State.ProjectedGraph);
            Assert.Same(state.LayoutResult, test.State.LayoutResult);
            Assert.Same(state.RoutingResult, test.State.RoutingResult);
        }
        Assert.Equal(uploads, test.Execution.FullUploadCount);
        Assert.Equal(bounded + 80, test.Execution.ViewportRenderCount);
        Assert.Equal(events, test.Events.Count);
        await AssertFullOracleAsync(test);
        // Up has its own position AND modifier, independently of the last Move.
        var finalDelta = new VectorD(55, 45);
        Assert.Equal(Canvas2DInteractionStatus.Committed,
            (await controller.PointerReleasedAsync(Pointer(test, start + finalDelta, true))).Status);
        await test.Session.WaitForIdleAsync();
        var committed = BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner);
        AssertPoints(Expected(finalDelta, true), committed.Path);
        AssertPoints(Expected(finalDelta, true)[1..^1], committed.ManualDefinition!.Value);
        Assert.Equal(before.Revision.Increment(), test.Snapshot.Revision);
        Assert.Equal(events + 1, test.Events.Count);
        Assert.Equal(state.HistoryStatus.EntryCount + 1, test.State.HistoryStatus.EntryCount);
        Assert.Equal(before.VisualModel.VisualStates.ToArray(), test.Snapshot.VisualModel.VisualStates.ToArray());
        foreach (var scope in before.VisualModel.RoutingScopes!.Value)
            Assert.Equal(scope.Connectors.Where(item => item.VisualStateId != owner).ToArray(),
                test.Snapshot.VisualModel.RoutingScopes!.Value.Single(item => item.ScopeId == scope.ScopeId)
                    .Connectors.Where(item => item.VisualStateId != owner).ToArray());
        Assert.Equal(original[0], committed.Path[0]);
        Assert.Equal(original[^1], committed.Path[^1]);
        Assert.Equal(connector.Metadata.Where(pair => pair.Key.Contains("anchor", StringComparison.Ordinal)).ToArray(),
            Connector(test, owner).Metadata.Where(pair => pair.Key.Contains("anchor", StringComparison.Ordinal)).ToArray());
        Assert.DoesNotContain(test.State.CurrentScene!.Items, IsPreview);
        Assert.All(test.State.CurrentScene.Items.Where(item => item.Origin.VisualStateId == owner &&
            item.Layer == Canvas2DSceneLayer.Connector), item => Assert.Empty(item.Style.DashPattern));
        await AssertFullOracleAsync(test);

        PointD[] Expected(VectorD delta, bool ctrl)
        {
            var result = original.ToArray();
            result[bend] += delta;
            if (ctrl)
            {
                if (bend == 1) result[2] += new VectorD(0, delta.Y);
                if (bend == 2) { result[1] += new VectorD(0, delta.Y); result[3] += new VectorD(delta.X, 0); }
                if (bend == 3) result[2] += new VectorD(delta.X, 0);
            }
            return result;
        }

        void AssertCandidate(VectorD delta, bool ctrl)
        {
            var expected = Expected(delta, ctrl);
            var previews = test.State.CurrentScene!.Items.Where(IsPreview).ToArray();
            for (var segment = 0; segment < expected.Length - 1; segment++)
            {
                var preview = Assert.Single(previews, item => Enumerable.Range(0, item.Geometry.Points.Length - 1)
                    .Any(index => Near(item.Geometry.Points[index], expected[segment]) && Near(item.Geometry.Points[index + 1], expected[segment + 1])));
                var changed = expected[segment] != original[segment] || expected[segment + 1] != original[segment + 1];
                var dx = Math.Abs(expected[segment + 1].X - expected[segment].X);
                var dy = Math.Abs(expected[segment + 1].Y - expected[segment].Y);
                Assert.Equal(changed && dx > 1e-9 && dy > 1e-9, !preview.Style.DashPattern.IsEmpty);
                Assert.Equal(Canvas2DSceneLayer.Overlay, preview.Layer);
                Assert.Equal(Canvas2DHitTestMode.None, preview.HitTestPolicy.Mode);
            }
        }

        static bool Near(PointD a, PointD b) => Math.Abs(a.X - b.X) < 1e-8 && Math.Abs(a.Y - b.Y) < 1e-8;
        static void AssertPoints(IReadOnlyList<PointD> expected, IReadOnlyList<PointD> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (var index = 0; index < expected.Count; index++) Assert.True(Near(expected[index], actual[index]));
        }
    }

    private static bool IsPreview(Canvas2DSceneItem item) =>
        item.Origin.StableSourceKey?.StartsWith("route-preview:", StringComparison.Ordinal) == true;

    [Theory]
    [InlineData(1d)]
    [InlineData(0.9d)]
    [InlineData(1.1d)]
    [InlineData(1.75d)]
    public async Task ManualCommitRebuildsOrthogonalBridgesWhilePreservingOtherRoutes(double zoom)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var owner = test.Snapshot.VisualModel.VisualStates.Single(item =>
            item.SemanticElementId == BpmnDemoPipeline.ThirdSequenceFlowId).Id;
        var crossingOwner = test.Snapshot.VisualModel.VisualStates.Single(item =>
            item.SemanticElementId == BpmnDemoPipeline.FirstSequenceFlowId).Id;
        foreach (var id in new[] { owner, crossingOwner })
            await test.ExecuteAsync(new SetConnectorRoutingTypeCommand(test.Snapshot.DocumentId,
                test.Snapshot.Revision, id, ConnectorRoutingType.Manual));
        var other = BpmnModelerTestComposition.SavedRoute(test.Snapshot, crossingOwner).Path;
        await test.ExecuteAsync(new UpdateConnectionRouteCommand(test.Snapshot.DocumentId,
            test.Snapshot.Revision, crossingOwner,
            [other[0], new(550, 80), new(550, 180), new(400, 180), new(400, 240), other[^1]]));
        var path = BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner).Path;
        Assert.Equal(new PointD(430, 114), path[0]);
        await test.ExecuteAsync(new UpdateConnectionRouteCommand(test.Snapshot.DocumentId,
            test.Snapshot.Revision, owner, [path[0], new(500, 114), path[^1]]));
        await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [owner],
            viewport: new ViewportSnapshot(zoom, new VectorD(35.7, 19.3))));
        await using var controller = test.CreateInteractionController();

        AssertBridge(owner, new PointD(550, 118), false);
        await DragToAsync(new PointD(700, 114));
        // Independent arithmetic: horizontal x=430..700, y=114 crosses
        // vertical x=550, y=80..180 strictly inside both, at (550,114).
        AssertCrossing(new PointD(550, 114), 1, 2);
        AssertBridge(owner, new PointD(550, 118), true);
        AssertBridge(crossingOwner, new PointD(550, 118), false);

        await DragToAsync(new PointD(480, 114));
        // x=550 is beyond the horizontal endpoint x=480: no crossing remains.
        // It is also outside the END x=510 magnetic release radius at every zoom.
        Assert.True(BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner).Path[1].X < 550);
        AssertBridge(owner, new PointD(550, 118), false);
        await DragToAsync(new PointD(430, 220));
        // Now the moved segment is vertical x=430, y=114..220 and crosses
        // C's horizontal x=550..400, y=180. C owns the new bridge at (430,180).
        AssertCrossing(new PointD(430, 180), 2, 3);
        AssertBridge(owner, new PointD(550, 118), false);
        AssertBridge(crossingOwner, new PointD(430, 184), true);

        var beforeCancel = test.Snapshot;
        var start = Center(Handle(test, owner).Bounds);
        await controller.PointerMovedAsync(Pointer(test, start));
        await controller.PointerPressedAsync(Pointer(test, start));
        await controller.PointerMovedAsync(Pointer(test, new PointD(700, 114)));
        await controller.PointerCancelledAsync(43);
        Assert.Same(beforeCancel, test.Snapshot);
        AssertBridge(crossingOwner, new PointD(430, 184), true);
        await test.Session.UpdateEditorStateAsync(EditorStateSnapshot.Empty);
        AssertBridge(crossingOwner, new PointD(430, 184), true);

        void AssertCrossing(PointD expected, int cStart, int cEnd)
        {
            var manual = BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner).Path;
            var otherPath = BpmnModelerTestComposition.SavedRoute(test.Snapshot, crossingOwner).Path;
            var a = manual[0];
            var b = manual[1];
            var c = otherPath[cStart];
            var d = otherPath[cEnd];
            var denominator = (b.X - a.X) * (d.Y - c.Y) - (b.Y - a.Y) * (d.X - c.X);
            var t = ((c.X - a.X) * (d.Y - c.Y) - (c.Y - a.Y) * (d.X - c.X)) / denominator;
            var u = ((c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X)) / denominator;
            Assert.InRange(t, 1e-8, 1 - 1e-8);
            Assert.InRange(u, 1e-8, 1 - 1e-8);
            Assert.Equal(expected.X, a.X + t * (b.X - a.X), 8);
            Assert.Equal(expected.Y, a.Y + t * (b.Y - a.Y), 8);
        }

        void AssertBridge(VisualStateId id, PointD apex, bool expected)
        {
            var jumps = test.State.CurrentScene!.Items.Where(item =>
                item.Origin.VisualStateId == id && Canvas2DConnectorLineJumpMetadata.HasHitTarget(item));
            Assert.True(expected == jumps.Any(item => item.Geometry.Points.Contains(apex)),
                $"Expected bridge {expected} at {apex}; edited path: " +
                string.Join(";", BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner).Path
                    .Select(point => $"({point.X:R},{point.Y:R})")) + "; jump paths: " +
                string.Join(";", jumps.SelectMany(item => item.Geometry.Points)
                    .Select(point => $"({point.X:R},{point.Y:R})")));
            Assert.Equal(expected, Connector(test, id).Geometry.Points.Contains(apex));
        }

        async Task DragToAsync(PointD destination)
        {
            var before = test.Snapshot;
            var history = test.State.HistoryStatus;
            var events = test.Events.Count;
            var start = Center(Handle(test, owner).Bounds);
            await controller.PointerMovedAsync(Pointer(test, start));
            await controller.PointerPressedAsync(Pointer(test, start));
            var uploads = test.Execution.FullUploadCount;
            var bounded = test.Execution.ViewportRenderCount;
            for (var i = 1; i <= 20; i++)
            {
                var sample = new PointD(start.X + (destination.X - start.X) * i / 21,
                    start.Y + (destination.Y - start.Y) * i / 21);
                Assert.Equal(Canvas2DInteractionStatus.Updated,
                    (await controller.PointerMovedAsync(Pointer(test, sample))).Status);
                Assert.Same(before, test.Snapshot);
            }
            Assert.Equal(uploads, test.Execution.FullUploadCount);
            Assert.Equal(bounded + 20, test.Execution.ViewportRenderCount);
            Assert.Equal(Canvas2DInteractionStatus.Committed,
                (await controller.PointerReleasedAsync(Pointer(test, destination))).Status);
            await test.Session.WaitForIdleAsync();
            Assert.Equal(before.Revision.Increment(), test.Snapshot.Revision);
            Assert.Equal(events + 1, test.Events.Count);
            Assert.Equal(history.EntryCount + 1, test.State.HistoryStatus.EntryCount);
            Assert.Null(test.State.CurrentScene!.BoundedPresentation?.RouteBend);
            Assert.Null(test.State.EditorState.ActiveGesture);
            Assert.Equal(owner, Assert.Single(test.State.EditorState.Selection));
            foreach (var scope in before.VisualModel.RoutingScopes!.Value)
            {
                var after = test.Snapshot.VisualModel.RoutingScopes!.Value.Single(item => item.ScopeId == scope.ScopeId);
                Assert.Equal(scope.Connectors.Where(item => item.VisualStateId != owner).ToArray(),
                    after.Connectors.Where(item => item.VisualStateId != owner).ToArray());
            }
            var committed = BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner).Path[1];
            Assert.Equal(destination.X, committed.X, 8);
            Assert.Equal(destination.Y, committed.Y, 8);
            Assert.Equal(committed, Canvas2DConnectorPathMetadata.Resolve(Connector(test, owner))[1]);
            await AssertFullOracleAsync(test);
        }
    }

    [Theory]
    [InlineData(false, 1d)]
    [InlineData(true, 1.75d)]
    public async Task SteadyBendUsesBoundedFamilyThenCommitsOnlyFinalPointer(bool pools, double zoom)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var owner = await PrepareAsync(test, pools, zoom);
        var original = test.Snapshot;
        var state = test.State;
        var connector = Connector(test, owner);
        var start = Center(Handle(test, owner).Bounds);
        await using var controller = test.CreateInteractionController();
        await controller.PointerMovedAsync(Pointer(test, start));
        await controller.PointerPressedAsync(Pointer(test, start));
        Assert.Equal(Canvas2DRouteGestureMetadata.Kind, test.State.EditorState.ActiveGesture?.Kind);
        Assert.NotNull(test.State.CurrentScene!.BoundedPresentation?.RouteBend);
        var content = test.State.CurrentScene.RenderContent;
        var uploads = test.Execution.FullUploadCount;
        var bounded = test.Execution.ViewportRenderCount;
        var fullRuns = test.Pipeline.FullRuns;
        var contributors = test.Contributions.Sum(item => item.Calls + item.Routes);
        var events = test.Events.Count;
        var oldRoute = BpmnModelerTestComposition.SavedRoute(original, owner);
        var body = test.State.CurrentScene.Items.First(item => Canvas2DNodeBodyMetadata.IsNodeBody(item) && item.IsVisible);
        var editable = Canvas2DConnectorPathMetadata.ResolveEditable(connector);
        double? retainedX = null, retainedY = null;
        for (var i = 0; i < 20; i++)
        {
            // A Manual bend may pass directly through a real node; no collision policy applies.
            // Keep these unsnapped samples clear of endpoint X targets (covered separately).
            var point = i == 19 ? Center(body.Bounds) : start + new VectorD(90 + i * 3, 10 - i);
            Assert.Equal(Canvas2DInteractionStatus.Updated, (await controller.PointerMovedAsync(Pointer(test, point))).Status);
            Assert.Same(original, test.Snapshot);
            Assert.Equal(state.HistoryStatus, test.State.HistoryStatus);
            Assert.Same(state.ProjectedGraph, test.State.ProjectedGraph);
            Assert.Same(state.LayoutResult, test.State.LayoutResult);
            Assert.Same(state.RoutingResult, test.State.RoutingResult);
            Assert.Equal(connector, Connector(test, owner));
            Assert.True(content == test.State.CurrentScene!.RenderContent);
            var expectedPreview = new PointD(Axis(point.X, editable[0].X, editable[^1].X, ref retainedX),
                Axis(point.Y, editable[0].Y, editable[^1].Y, ref retainedY));
            var previews = test.State.CurrentScene.Items.Where(IsPreview).ToArray();
            Assert.NotEmpty(previews);
            Assert.Contains(previews.SelectMany(item => item.Geometry.Points), p =>
                Math.Abs(p.X - expectedPreview.X) < 1e-8 && Math.Abs(p.Y - expectedPreview.Y) < 1e-8);
            Assert.Equal(expectedPreview.X, Center(Handle(test, owner).Bounds).X, 8);
            Assert.Equal(expectedPreview.Y, Center(Handle(test, owner).Bounds).Y, 8);
            Assert.Contains(test.State.CurrentScene.Items, item =>
                item.Origin.StableSourceKey?.StartsWith("route-label-preview:", StringComparison.Ordinal) == true);
            Assert.All(previews, preview => Assert.Equal(Canvas2DHitTestMode.None, preview.HitTestPolicy.Mode));
        }
        Assert.Equal(uploads, test.Execution.FullUploadCount);
        Assert.Equal(bounded + 20, test.Execution.ViewportRenderCount);
        Assert.Equal(fullRuns, test.Pipeline.FullRuns);
        Assert.Equal(contributors, test.Contributions.Sum(item => item.Calls + item.Routes));
        Assert.Equal(events, test.Events.Count);
        await AssertFullOracleAsync(test);

        // Release is sampled separately, without a preceding Move at this position.
        var final = start + new VectorD(85, 48);
        Assert.Equal(Canvas2DInteractionStatus.Committed, (await controller.PointerReleasedAsync(Pointer(test, final))).Status);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(original.Revision.Increment(), test.Snapshot.Revision);
        Assert.Equal(events + 1, test.Events.Count);
        Assert.Equal(state.HistoryStatus.EntryCount + 1, test.State.HistoryStatus.EntryCount);
        var changed = BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner);
        var expected = connector.ConnectorPresentationMapping?.MapSceneToLogical(final) ?? final;
        Assert.Equal(expected.X, changed.ManualDefinition!.Value[0].X, 8);
        Assert.Equal(expected.Y, changed.ManualDefinition.Value[0].Y, 8);
        Assert.Equal(oldRoute.Path[0], changed.Path[0]);
        Assert.Equal(oldRoute.Path[^1], changed.Path[^1]);
        Assert.Equal(original.VisualModel.VisualStates.ToArray(), test.Snapshot.VisualModel.VisualStates.ToArray());
        foreach (var oldScope in original.VisualModel.RoutingScopes!.Value)
        {
            var next = test.Snapshot.VisualModel.RoutingScopes!.Value.Single(scope => scope.ScopeId == oldScope.ScopeId);
            Assert.Equal(oldScope.Geometry, next.Geometry);
            Assert.Equal(oldScope.Connectors.Where(record => record.VisualStateId != owner).ToArray(),
                next.Connectors.Where(record => record.VisualStateId != owner).ToArray());
            if (next.Connectors.Any(record => record.VisualStateId == owner))
                Assert.Equal(owner, next.Connectors[^1].VisualStateId);
        }
        Assert.Null(test.State.EditorState.ActiveGesture);

        double Axis(double raw, double first, double last, ref double? retained)
        {
            if (retained is { } value && Math.Abs(raw - value) <= 9 / zoom) return value;
            var nearest = Math.Abs(raw - first) <= Math.Abs(raw - last) ? first : last;
            retained = Math.Abs(raw - nearest) <= 6 / zoom ? nearest : null;
            return retained ?? raw;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrStaleSurfaceRestoresOriginalRouteWithoutCommit(bool stale)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var owner = await PrepareAsync(test, false, 0.8);
        var original = test.Snapshot;
        var history = test.State.HistoryStatus;
        var connector = Connector(test, owner);
        var start = Center(Handle(test, owner).Bounds);
        await using var controller = test.CreateInteractionController();
        await controller.PointerMovedAsync(Pointer(test, start));
        await controller.PointerPressedAsync(Pointer(test, start));
        var endpoint = BpmnModelerTestComposition.SavedRoute(original, owner).Path[0];
        await controller.PointerMovedAsync(Pointer(test, new(endpoint.X + 5 / 0.8, start.Y + 30)));
        Assert.Contains(Canvas2DRouteGestureMetadata.SnapXTarget, test.State.EditorState.ActiveGesture!.Properties.Keys);
        if (stale)
        {
            Assert.True((await test.Session.ResizeAsync(new Canvas2DSurfaceSize(1001, 700, 2))).Succeeded);
            await controller.PointerReleasedAsync(Pointer(test, start + new VectorD(40, 50)));
        }
        else await controller.PointerCancelledAsync(43);
        Assert.Same(original, test.Snapshot);
        Assert.Equal(history, test.State.HistoryStatus);
        Assert.Equal(connector, Connector(test, owner));
        Assert.Null(test.State.EditorState.ActiveGesture);
        Assert.DoesNotContain(test.State.CurrentScene!.Items, item =>
            item.Origin.StableSourceKey?.StartsWith("route-preview:", StringComparison.Ordinal) == true);
        // A new gesture must not inherit the cancelled target's wider release radius.
        await controller.PointerMovedAsync(Pointer(test, start));
        await controller.PointerPressedAsync(Pointer(test, start));
        await controller.PointerMovedAsync(Pointer(test, new(endpoint.X + 8 / 0.8, start.Y + 30)));
        Assert.DoesNotContain(Canvas2DRouteGestureMetadata.SnapXTarget, test.State.EditorState.ActiveGesture!.Properties.Keys);
        await controller.PointerCancelledAsync(43);
        Assert.Same(original, test.Snapshot);
    }

    private static async Task<VisualStateId> PrepareAsync(PhaseA122PanSceneReuseIntegrationTests.Fixture test, bool pools, double zoom)
    {
        if (pools) await test.EnablePoolsAsync();
        var flow = BpmnDemoPipeline.ThirdSequenceFlowId;
        await test.ExecuteAsync(new UpdateBpmnSequenceFlowNameCommand(test.Snapshot.DocumentId,
            test.Snapshot.Revision, flow, "Accepted branch with wrapped label"));
        var owner = test.Snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == flow).Id;
        await test.ExecuteAsync(new SetConnectorRoutingTypeCommand(test.Snapshot.DocumentId,
            test.Snapshot.Revision, owner, ConnectorRoutingType.Manual));
        var path = BpmnModelerTestComposition.SavedRoute(test.Snapshot, owner).Path;
        await test.ExecuteAsync(new UpdateConnectionRouteCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            owner, [path[0], new PointD(480, 320), path[^1]]));
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [owner],
            viewport: new ViewportSnapshot(zoom, new VectorD(35, 19))))).Succeeded);
        return owner;
    }

    private static Canvas2DSceneItem Connector(PhaseA122PanSceneReuseIntegrationTests.Fixture test, VisualStateId owner) =>
        test.State.CurrentScene!.Items.Single(item => item.Origin.VisualStateId == owner &&
            item.Layer == Canvas2DSceneLayer.Connector && item.Metadata.ContainsKey(Canvas2DRouteGestureMetadata.RouteEditable));

    private static Canvas2DSceneItem Handle(PhaseA122PanSceneReuseIntegrationTests.Fixture test, VisualStateId owner) =>
        test.State.CurrentScene!.Items.Single(item => item.Origin.VisualStateId == owner &&
            item.Metadata.ContainsKey(Canvas2DRouteGestureMetadata.HandleRole));

    private static PointD Center(RectD bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

    private static Canvas2DPointerInput Pointer(PhaseA122PanSceneReuseIntegrationTests.Fixture test, PointD point, bool controlKey = false) =>
        new(43, test.State.CurrentScene!.ViewportTransform.TransformPoint(point), buttons: 1, controlKey: controlKey);

    private static async Task AssertFullOracleAsync(PhaseA122PanSceneReuseIntegrationTests.Fixture test)
    {
        var eligibility = new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode);
        var registrations = BpmnPluginRegistration.N100.SceneContributors.AddRange(
            OrganizationalPluginRegistration.Create(eligibility,
                OrganizationalPoolSceneContributor.CreateRegistration(eligibility)).SceneContributors);
        var state = test.State;
        var result = await new Canvas2DSceneBuilder(contributors: registrations).BuildMeasuredAsync(test.Snapshot,
            state.ActiveScopeId, state.ModelProfileViewState, state.ModelProfileElementViewState,
            state.ProjectedGraph!, state.LayoutResult!, state.RoutingResult!, test.Snapshot.VisualModel,
            state.EditorState, test.Renderer, test.Renderer.CreateTextMeasurementRequest, CancellationToken.None);
        Assert.Equal(result.Scene, state.CurrentScene);
    }
}
