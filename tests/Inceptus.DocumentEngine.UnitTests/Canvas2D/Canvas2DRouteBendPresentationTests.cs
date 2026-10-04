using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DRouteBendPresentationTests
{
    [Theory]
    [InlineData(150, 104, 150, 100, null, 1)]
    [InlineData(150, 204, 150, 200, null, 3)]
    [InlineData(104, 150, 100, 150, 1, null)]
    [InlineData(204, 150, 200, 150, 3, null)]
    [InlineData(104, 204, 100, 200, 1, 3)]
    [InlineData(107, 193, 107, 193, null, null)]
    [InlineData(106, 194, 100, 200, 1, 3)]
    public void SnapUsesOnlyImmediateNeighboursOnIndependentAxes(double x, double y,
        double expectedX, double expectedY, int? targetX, int? targetY)
    {
        System.Collections.Immutable.ImmutableArray<PointD> path =
            [new(150, 150), new(100, 100), new(140, 140), new(200, 200), new(107, 193)];
        var result = Canvas2DRouteBendGeometry.Snap(path, 2, new(x, y), 1, default);
        Assert.Equal(new PointD(expectedX, expectedY), result.Point);
        Assert.Equal(new Canvas2DRouteBendSnapState(targetX, targetY), result.State);
        var candidate = Canvas2DRouteBendGeometry.CandidateAt(path, 2, result.Point, false);
        foreach (var index in new[] { 0, 1, 3, 4 }) Assert.Equal(path[index], candidate[index]);
    }

    [Theory]
    [InlineData(0.8)]
    [InlineData(1)]
    [InlineData(1.5)]
    public void SnapRetainsTargetUntilNineCssPixelsAndReentersOnlyWithinSix(double zoom)
    {
        System.Collections.Immutable.ImmutableArray<PointD> path = [new(0, 0), new(100, 100), new(200, 200)];
        var entered = Canvas2DRouteBendGeometry.Snap(path, 1, new(5 / zoom, 50), zoom, default);
        Assert.Equal(0d, entered.Point.X);
        var retained = Canvas2DRouteBendGeometry.Snap(path, 1, new(9 / zoom, 50), zoom, entered.State);
        Assert.Equal(0d, retained.Point.X);
        var released = Canvas2DRouteBendGeometry.Snap(path, 1, new(9.1 / zoom, 50), zoom, retained.State);
        Assert.Null(released.State.XTarget);
        Assert.Equal(9.1 / zoom, released.Point.X);
        var outside = Canvas2DRouteBendGeometry.Snap(path, 1, new(7 / zoom, 50), zoom, released.State);
        Assert.Null(outside.State.XTarget);
        Assert.Equal(7 / zoom, outside.Point.X);
    }

    [Theory]
    [InlineData(4, 6, 0, 10, 0, 2)]
    [InlineData(5, 5, 0, 0, 0, 0)]
    [InlineData(6, 4, 10, 0, 2, 0)]
    public void NearestTargetsAndTiesAreDeterministic(double x, double y, double ex, double ey, int ix, int iy)
    {
        System.Collections.Immutable.ImmutableArray<PointD> path = [new(0, 0), new(100, 100), new(10, 10)];
        var entered = Canvas2DRouteBendGeometry.Snap(path, 1, new(x, y), 1, default);
        Assert.Equal(new PointD(ex, ey), entered.Point);
        Assert.Equal(new Canvas2DRouteBendSnapState(ix, iy), entered.State);
        var retained = Canvas2DRouteBendGeometry.Snap(path, 1, new(5, 5), 1, entered.State);
        Assert.Equal(entered.State, retained.State);
        var closerOther = Canvas2DRouteBendGeometry.Snap(path, 1,
            new(ix == 0 ? 8 : 2, iy == 0 ? 8 : 2), 1, retained.State);
        Assert.Equal(entered.State, closerOther.State); // release hysteresis wins over the other target
    }

    [Fact]
    public void EqualCoordinatesDoNotOscillateAndCtrlBypassesSnappingWithoutDrift()
    {
        System.Collections.Immutable.ImmutableArray<PointD> path =
            [new(0, 0), new(50, 100), new(100, 100), new(100, 200), new(100, 300), new(400, 400)];
        var snapped = Canvas2DRouteBendGeometry.Snap(path, 2, new(140, 103), 1, default);
        Assert.Equal(new PointD(140, 100), snapped.Point);
        foreach (var ctrl in new[] { false, true, false, true })
        {
            snapped = Canvas2DRouteBendGeometry.Snap(path, 2, new(140, 103), 1, snapped.State, controlKey: ctrl);
            var result = Canvas2DRouteBendGeometry.CandidateAt(path, 2, snapped.Point, ctrl);
            Assert.Equal(new PointD(140, ctrl ? 103 : 100), result[2]);
            Assert.Equal(ctrl ? new PointD(50, 103) : path[1], result[1]);
            Assert.Equal(ctrl ? new PointD(140, 200) : path[3], result[3]);
            Assert.Equal(ctrl ? default : new Canvas2DRouteBendSnapState(null, 1), snapped.State);
            foreach (var index in new[] { 0, 4, 5 }) Assert.Equal(path[index], result[index]);
        }
        var equal = Canvas2DRouteBendGeometry.Snap(path, 3, new(104, 240), 1, default);
        Assert.Equal(2, equal.State.XTarget);
        foreach (var rawX in new[] { 98d, 105d, 92d, 109d })
        {
            equal = Canvas2DRouteBendGeometry.Snap(path, 3, new(rawX, 240), 1, equal.State);
            Assert.Equal(100d, equal.Point.X);
            Assert.Equal(2, equal.State.XTarget);
        }
    }

    [Theory]
    [InlineData(1, 140, 4, 140, 0, 140, 100)]
    [InlineData(2, 104, 140, 100, 140, 104, 0)]
    public void EndpointsAttractWithoutCtrlAndRemainFixedInBothModes(int bend, double x, double y,
        double ex, double ey, double nx, double ny)
    {
        System.Collections.Immutable.ImmutableArray<PointD> path = [new(0, 0), new(100, 0), new(100, 100), new(100, 200)];
        var snapped = Canvas2DRouteBendGeometry.Snap(path, bend, new(x, y), 1, default);
        var result = Canvas2DRouteBendGeometry.CandidateAt(path, bend, snapped.Point, false);
        Assert.Equal(new PointD(ex, ey), result[bend]);
        Assert.Equal(path[bend == 1 ? 2 : 1], result[bend == 1 ? 2 : 1]);
        var bypassed = Canvas2DRouteBendGeometry.Snap(path, bend, new(x, y), 1, snapped.State, controlKey: true);
        Assert.Equal(default, bypassed.State);
        result = Canvas2DRouteBendGeometry.CandidateAt(path, bend, bypassed.Point, true);
        Assert.Equal(new PointD(x, y), result[bend]);
        Assert.Equal(new PointD(nx, ny), result[bend == 1 ? 2 : 1]);
        Assert.Equal(path[0], result[0]);
        Assert.Equal(path[^1], result[^1]);
    }

    [Fact]
    public void CompactMapMeasuresDisplayedDistanceAndKeepsExactLogicalTarget()
    {
        System.Collections.Immutable.ImmutableArray<PointD> logical = [new(20, 200), new(100, 400), new(300, 700)];
        // Independent map: the 100..500 band shrinks from 400 to 100.
        var map = new Canvas2DSpatialCoordinateMap([new(new("snap:row"), 100, 500, 100, 200)]);
        var mapping = new Canvas2DConnectorPresentationMapping(logical,
            [new(20, 125), new(100, 175), new(300, 400)], map, null, null);
        var snap = Canvas2DRouteBendGeometry.Snap(logical, 1, new(100, 220), 1, default, mapping);
        // 20 logical units are 5 displayed/CSS pixels inside this band.
        Assert.Equal(new PointD(100, 200), snap.Point);
        Assert.Equal(0, snap.State.YTarget);
        var retained = Canvas2DRouteBendGeometry.Snap(logical, 1, new(100, 232), 1, snap.State, mapping);
        Assert.Equal(200d, retained.Point.Y);
        var released = Canvas2DRouteBendGeometry.Snap(logical, 1, new(100, 240), 1, retained.State, mapping);
        Assert.Equal(240d, released.Point.Y);
        Assert.Null(released.State.YTarget);
    }

    [Fact]
    public void SnappedCoordinateIsCopiedExactlyWithoutSubtractAddRoundoff()
    {
        System.Collections.Immutable.ImmutableArray<PointD> path = [new(0.1, 0.2), new(300, 400), new(600, 700)];
        var snap = Canvas2DRouteBendGeometry.Snap(path, 1, new(0.3, 200), 1, default);
        var candidate = Canvas2DRouteBendGeometry.CandidateAt(path, 1, snap.Point, false);
        Assert.Equal(path[0].X, candidate[1].X);
        Assert.Equal(path[0], candidate[0]);
        Assert.Equal(path[^1], candidate[^1]);
    }

    [Theory]
    [InlineData(100, 100, 300, 100, false, false)]
    [InlineData(200, 0, 200, 200, false, false)]
    [InlineData(100, 100, 200, 200, false, false)]
    [InlineData(100, 100, 300, 200, false, true)]
    [InlineData(200, 0, 300, 200, false, true)]
    [InlineData(100, 0, 300, 200, true, true)]
    [InlineData(100, 100.0000000005, 300, 100, false, false)]
    [InlineData(200.0000000005, 0, 200, 200, false, false)]
    [InlineData(100, 100.000001, 200.000001, 200, true, true)]
    public void OnlyChangedDiagonalCandidateSegmentsAreDashed(double qx, double qy, double rx, double ry,
        bool leftDashed, bool rightDashed)
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var edge = inputs.Graph.Edges[0];
        var route = inputs.Routing.Routes[0];
        var q = new PointD(qx, qy);
        var r = new PointD(rx, ry);
        var manual = new ProjectedEdge(edge.Source, edge.SourceNodeId, edge.TargetNodeId,
            persistentRoute: [route.SourceAnchor, q, new(160, 70), r, route.DestinationAnchor]);
        var graph = new ProjectedGraph(inputs.Graph.DocumentId, inputs.Graph.SourceRevision,
            inputs.Graph.Nodes, [manual], inputs.Graph.Groups, inputs.Graph.Ports, inputs.Graph.Labels);
        var targetId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
        var state = new EditorStateSnapshot(selection: [edge.Source.VisualStateId!], activeGesture:
            new EditorGestureSnapshot("style", Canvas2DRouteGestureMetadata.Kind, default, new(40, 30),
            [new(Canvas2DRouteGestureMetadata.TargetSceneObjectId, PropertyValue.FromText(targetId.Value)),
             new(Canvas2DRouteGestureMetadata.TargetVisualStateId, PropertyValue.FromText(edge.Source.VisualStateId!.Value)),
             new(Canvas2DRouteGestureMetadata.BendIndex, PropertyValue.FromInteger(2))]));
        var scene = new Canvas2DSceneBuilder().Build(graph, inputs.Layout, inputs.Routing, inputs.VisualModel, state).Scene!;
        Check(route.SourceAnchor, q, false); // unrelated diagonal remains standard
        Check(q, new(200, 100), leftDashed);
        Check(new(200, 100), r, rightDashed);
        Check(r, route.DestinationAnchor, false);
        var idle = new Canvas2DSceneBuilder().Build(graph, inputs.Layout, inputs.Routing, inputs.VisualModel, EditorStateSnapshot.Empty).Scene!;
        Assert.DoesNotContain(idle.Items, item => item.Origin.StableSourceKey?.StartsWith("route-preview:", StringComparison.Ordinal) == true);

        void Check(PointD a, PointD b, bool dashed)
        {
            var item = Assert.Single(scene.Items, item =>
                item.Origin.StableSourceKey?.StartsWith("route-preview:", StringComparison.Ordinal) == true &&
                Enumerable.Range(0, item.Geometry.Points.Length - 1).Any(index =>
                    item.Geometry.Points[index] == a && item.Geometry.Points[index + 1] == b));
            Assert.Equal(dashed, !item.Style.DashPattern.IsEmpty);
        }
    }

    [Theory]
    [InlineData(0, 0, 30, 0, (int)Canvas2DSegmentOrientation.Horizontal)]
    [InlineData(0, 0, 30, 0.0000000005, (int)Canvas2DSegmentOrientation.Horizontal)]
    [InlineData(0, 0, 0.0000000005, 30, (int)Canvas2DSegmentOrientation.Vertical)]
    [InlineData(0, 0, 0, -30, (int)Canvas2DSegmentOrientation.Vertical)]
    [InlineData(0, 0, 30, 0.000001, (int)Canvas2DSegmentOrientation.Diagonal)]
    [InlineData(0, 0, 0.000001, 30, (int)Canvas2DSegmentOrientation.Diagonal)]
    [InlineData(0, 0, 0, 0, (int)Canvas2DSegmentOrientation.Degenerate)]
    [InlineData(0, 0, 0.0000000005, -0.0000000005, (int)Canvas2DSegmentOrientation.Degenerate)]
    public void LogicalOrientationSeparatesRoundoffFromSlope(double ax, double ay, double bx, double by,
        int expected) =>
        Assert.Equal((Canvas2DSegmentOrientation)expected, Canvas2DSegmentGeometry.Orientation(new(ax, ay), new(bx, by)));

    [Theory]
    [InlineData(100, 100, 200, 200, 100, 130, 240, 200)] // H/V
    [InlineData(200, 50, 300, 100, 240, 50, 300, 130)] // V/H
    [InlineData(100, 100, 300, 100, 100, 130, 300, 130)] // H/H
    [InlineData(200, 50, 200, 200, 240, 50, 240, 200)] // V/V
    [InlineData(100, 50, 300, 200, 100, 50, 300, 200)] // diagonal/diagonal
    [InlineData(100, 130, 200, 200, 100, 130, 240, 200)] // original diagonal becomes H without propagation
    [InlineData(200, 100, 300, 100, 200, 100, 300, 130)] // degenerate/H
    public void CtrlChangesOnlyDirectIntermediateNeighboursFromOriginalGeometry(
        double qx, double qy, double rx, double ry, double eqx, double eqy, double erx, double ery)
    {
        System.Collections.Immutable.ImmutableArray<PointD> original =
            [new(0, 0), new(50, 50), new(qx, qy), new(200, 100), new(rx, ry), new(350, 250), new(400, 300)];
        var delta = new VectorD(40, 30);
        var assisted = Canvas2DRouteBendGeometry.Candidate(original, 3, delta, true);
        Assert.Equal(new PointD(240, 130), assisted[3]);
        Assert.Equal(new PointD(eqx, eqy), assisted[2]);
        Assert.Equal(new PointD(erx, ery), assisted[4]);
        foreach (var index in new[] { 0, 1, 5, 6 }) Assert.Equal(original[index], assisted[index]);
        var plain = Canvas2DRouteBendGeometry.Candidate(original, 3, delta, false);
        Assert.Equal(original[2], plain[2]);
        Assert.Equal(original[4], plain[4]);
        Assert.Equal(assisted.ToArray(), Canvas2DRouteBendGeometry.Candidate(original, 3, delta, true).ToArray());
        Assert.Equal(new PointD(200, 100), original[3]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void CtrlNeverChangesEndpointsEvenWhenOriginallyOrthogonal(int bend)
    {
        System.Collections.Immutable.ImmutableArray<PointD> original =
            [new(0, 0), new(100, 0), new(100, 100), new(100, 200)];
        var candidate = Canvas2DRouteBendGeometry.Candidate(original, bend, new(40, 30), true);
        Assert.Equal(original[0], candidate[0]);
        Assert.Equal(original[^1], candidate[^1]);
        Assert.Equal(bend == 1 ? new PointD(140, 100) : new PointD(140, 0), candidate[bend == 1 ? 2 : 1]);
    }

    [Fact]
    public void RouteDependencyIsIndependentOfEveryExistingCapability()
    {
        var old = new Canvas2DSceneContributorDescriptor(new("test:route-bend"), "1",
            Canvas2DScenePanDependency.Invariant, Canvas2DSceneMoveGestureDependency.Invariant,
            Canvas2DSceneTransientDependency.Invariant, Canvas2DSceneTransientDependency.Invariant,
            Canvas2DScenePlacementDependency.Invariant, Canvas2DSceneTransientDependency.Invariant,
            Canvas2DSceneTransientDependency.Invariant);
        Assert.Equal(Canvas2DSceneTransientDependency.Unknown, old.RouteBendDependency);
        Assert.Equal(old, Descriptor(Canvas2DSceneTransientDependency.Unknown));
        Assert.NotEqual(old, Descriptor(Canvas2DSceneTransientDependency.Invariant));
        Assert.Throws<ArgumentOutOfRangeException>(() => Descriptor((Canvas2DSceneTransientDependency)99));
    }

    [Theory]
    [InlineData(Canvas2DSceneTransientDependency.Unknown)]
    [InlineData(Canvas2DSceneTransientDependency.Dependent)]
    [InlineData(Canvas2DSceneTransientDependency.Invariant)]
    public async Task ExplicitCapabilityReusesMeasuredFamilyIncludingJumpLayersAndRetainsFullFallback(
        Canvas2DSceneTransientDependency dependency)
    {
        var inputs = Canvas2DSceneTestData.Create().WithCrossingConnector();
        var edge = inputs.Graph.Edges[0];
        var route = inputs.Routing.Routes.Single(item => item.ProjectedEdgeId == edge.Id);
        var manual = new ProjectedEdge(edge.Source, edge.SourceNodeId, edge.TargetNodeId,
            persistentRoute: [route.SourceAnchor, new PointD(180, 45), route.DestinationAnchor]);
        inputs = inputs.WithGraph(new ProjectedGraph(inputs.Graph.DocumentId, inputs.Graph.SourceRevision,
            inputs.Graph.Nodes, inputs.Graph.Edges.Select(item => item.Id == edge.Id ? manual : item),
            inputs.Graph.Groups, inputs.Graph.Ports, inputs.Graph.Labels));
        var document = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        var probe = new Probe();
        var builder = new Canvas2DSceneBuilder(contributors: [new(Descriptor(dependency), probe)]);
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        await using var lifetime = renderer;
        var view = ModelProfileViewStateSnapshot.Empty;
        var elements = ModelProfileElementViewStateSnapshot.Empty;
        var targetId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
        EditorStateSnapshot Moving(PointD point) => new(selection: [edge.Source.VisualStateId!],
            activeGesture: new EditorGestureSnapshot("route:bend", Canvas2DRouteGestureMetadata.Kind,
                default, point,
                [new(Canvas2DRouteGestureMetadata.TargetSceneObjectId, PropertyValue.FromText(targetId.Value)),
                 new(Canvas2DRouteGestureMetadata.TargetVisualStateId, PropertyValue.FromText(edge.Source.VisualStateId!.Value)),
                 new(Canvas2DRouteGestureMetadata.BendIndex, PropertyValue.FromInteger(1))]));
        async Task<Canvas2DScene> Build(EditorStateSnapshot state) => Assert.IsType<Canvas2DScene>(
            (await builder.BuildMeasuredAsync(document, document.SemanticModel.RootScopeId, view, elements,
                inputs.Graph, inputs.Layout, inputs.Routing, document.VisualModel, state, renderer,
                renderer.CreateTextMeasurementRequest, CancellationToken.None)).Scene);
        var active = await Build(Moving(new PointD(10, 10)));
        Assert.Contains(active.Items, Canvas2DConnectorLineJumpMetadata.HasHitTarget);
        Assert.True((await renderer.RenderAsync(active)).Succeeded);
        var version = execution.LastFrame!.ContentVersion;
        foreach (var position in new[] { new PointD(30, 40), new PointD(-15, -5), new PointD(0, 0) })
        {
            var state = Moving(position);
            var calls = probe.Calls;
            var next = builder.TryReuseForRouteBend(active, document, document.SemanticModel.RootScopeId,
                view, elements, inputs.Graph, inputs.Layout, inputs.Routing, state, CancellationToken.None);
            Assert.Equal(calls, probe.Calls);
            if (dependency != Canvas2DSceneTransientDependency.Invariant)
            {
                Assert.Null(next);
                Assert.Null(active.BoundedPresentation?.RouteBend);
                continue;
            }
            Assert.NotNull(next);
            Assert.Equal(await Build(state), next); // independent complete composition
            Assert.True((await renderer.RenderAsync(next)).Succeeded);
            Assert.Equal("renderViewport", execution.Calls[^1]);
            Assert.Equal(version, execution.LastViewportFrame!.ContentVersion);
            Assert.Equal(next.BoundedPresentation!.Items.Length, execution.LastViewportFrame.PresentationItems!.Length);
            Assert.All(execution.LastViewportFrame.PresentationItems, item => Assert.Equal(5, item.Item.Layer));
            Assert.All(next.BoundedPresentation.Items, item => Assert.Equal(Canvas2DSceneLayer.Overlay, item.Layer));
            active = next;
        }
        Assert.Null(builder.TryReuseForRouteBend(active, document, document.SemanticModel.RootScopeId,
            view, elements, inputs.Graph, inputs.Layout, inputs.Routing, EditorStateSnapshot.Empty, CancellationToken.None));
        Assert.Throws<OperationCanceledException>(() => builder.TryReuseForRouteBend(active, document,
            document.SemanticModel.RootScopeId, view, elements, inputs.Graph, inputs.Layout, inputs.Routing,
            Moving(new PointD(20, 30)), new CancellationToken(true)));
    }

    private static Canvas2DSceneContributorDescriptor Descriptor(Canvas2DSceneTransientDependency dependency) =>
        new(new("test:route-bend"), "1", Canvas2DScenePanDependency.Invariant,
            Canvas2DSceneMoveGestureDependency.Invariant, Canvas2DSceneTransientDependency.Invariant,
            Canvas2DSceneTransientDependency.Invariant, Canvas2DScenePlacementDependency.Invariant,
            Canvas2DSceneTransientDependency.Invariant, Canvas2DSceneTransientDependency.Invariant, dependency);

    private sealed class Probe : ICanvas2DSceneContributor
    {
        internal int Calls { get; private set; }
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            Calls++;
            return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution([]));
        }
    }
}
