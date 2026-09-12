using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnInclusiveGatewayGraphTests
{
    private const string ProjectedSemanticTypeProperty = "BPMN.ProjectedSemanticType";

    private static readonly DocumentId DocumentId = new("bpmn:m34-graph-unit");
    private static readonly DocumentRevision Revision = new(83);
    private static readonly ProjectionRuleId NodeRuleId = new("bpmn:m34-test/node");
    private static readonly ProjectionRuleId EdgeRuleId = new("bpmn:m34-test/edge");

    [Fact]
    public void M34AddsInclusivePolicyWithoutChangingHistoricalM33Set()
    {
        var historical = BpmnPluginRegistration.M33.ConnectorAnchorPolicies;
        var current = BpmnPluginRegistration.M34.ConnectorAnchorPolicies;

        Assert.Equal(5, historical.Length);
        Assert.DoesNotContain(
            historical,
            registration =>
                registration.ElementTypeId == BpmnSemanticTypes.InclusiveGateway);
        Assert.Equal(6, current.Length);
        Assert.Equal(historical.AsEnumerable(), current.Take(historical.Length));

        var registration = Assert.Single(
            current,
            candidate =>
                candidate.ElementTypeId == BpmnSemanticTypes.InclusiveGateway);
        var resolved = new ElementConnectorAnchorPolicyRegistry(current).Resolve(
            BpmnSemanticTypes.InclusiveGateway);

        Assert.Same(registration.Policy, resolved);
        Assert.All(Enum.GetValues<ConnectorAnchorSide>(), side =>
        {
            var edge = resolved.ForSide(side);
            Assert.Equal(ConnectorAnchorPolicyMode.DynamicUnlimited, edge.Mode);
            Assert.Equal(
                ConnectorAnchorRoleCapability.SourceOrTarget,
                edge.AllowedRoles);
        });
    }

    [Fact]
    public void TwoAndThreeWaySplitJoinAnchorsUseDistinctEvenlyDistributedPoints()
    {
        var twoWaySplit = GatewayVisual(
            "two-way-split",
            [
                Anchor("two-split-target", ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target, 0),
                Anchor("two-split-source-a", ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source, 0),
                Anchor("two-split-source-b", ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source, 1),
            ]);
        var threeWaySplit = GatewayVisual(
            "three-way-split",
            [
                Anchor("three-split-target", ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target, 0),
                Anchor("three-split-source-a", ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source, 0),
                Anchor("three-split-source-b", ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source, 1),
                Anchor("three-split-source-c", ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source, 2),
            ]);
        var threeWayJoin = GatewayVisual(
            "three-way-join",
            [
                Anchor("three-join-target-a", ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target, 0),
                Anchor("three-join-target-b", ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target, 1),
                Anchor("three-join-target-c", ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target, 2),
                Anchor("three-join-source", ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source, 0),
            ]);
        var bounds = new RectD(0d, 0d, 48d, 48d);

        var twoSources = SideAnchors(
            ResolveAnchors(twoWaySplit),
            ConnectorAnchorSide.Right);
        Assert.Equal(2, twoSources.Length);
        Assert.Equal(2, twoSources.Select(static anchor => anchor.Id).Distinct().Count());
        Assert.All(twoSources, static anchor =>
            Assert.True(anchor.Allows(ConnectorAnchorRole.Source)));
        Assert.Equal(
            [new PointD(48d, 16d), new PointD(48d, 32d)],
            ResolvePoints(bounds, twoSources));

        var threeSources = SideAnchors(
            ResolveAnchors(threeWaySplit),
            ConnectorAnchorSide.Right);
        Assert.Equal(3, threeSources.Length);
        Assert.Equal(3, threeSources.Select(static anchor => anchor.Id).Distinct().Count());
        Assert.All(threeSources, static anchor =>
            Assert.True(anchor.Allows(ConnectorAnchorRole.Source)));
        Assert.Equal(
            [new PointD(48d, 12d), new PointD(48d, 24d), new PointD(48d, 36d)],
            ResolvePoints(bounds, threeSources));
        Assert.Equal(
            new PointD(0d, 24d),
            ResolvePoint(
                bounds,
                Assert.Single(
                    SideAnchors(
                        ResolveAnchors(threeWaySplit),
                        ConnectorAnchorSide.Left))));

        var threeTargets = SideAnchors(
            ResolveAnchors(threeWayJoin),
            ConnectorAnchorSide.Left);
        Assert.Equal(3, threeTargets.Length);
        Assert.Equal(3, threeTargets.Select(static anchor => anchor.Id).Distinct().Count());
        Assert.All(threeTargets, static anchor =>
            Assert.True(anchor.Allows(ConnectorAnchorRole.Target)));
        Assert.Equal(
            [new PointD(0d, 12d), new PointD(0d, 24d), new PointD(0d, 36d)],
            ResolvePoints(bounds, threeTargets));
        Assert.Equal(
            new PointD(48d, 24d),
            ResolvePoint(
                bounds,
                Assert.Single(
                    SideAnchors(
                        ResolveAnchors(threeWayJoin),
                        ConnectorAnchorSide.Right))));

        var allIds = twoWaySplit.ConnectorAnchors
            .Concat(threeWaySplit.ConnectorAnchors)
            .Concat(threeWayJoin.ConnectorAnchors)
            .Select(static anchor => anchor.Id)
            .ToArray();
        Assert.Equal(allIds.Length, allIds.Distinct().Count());
    }

    [Fact]
    public void LayoutHandlesLinearTwoAndThreeWaySplitJoinAndPinnedInclusiveGateway()
    {
        var linearSource = Node("linear-source", BpmnSemanticTypes.Task);
        var linearGateway = Node(
            "linear-gateway",
            BpmnSemanticTypes.InclusiveGateway,
            includePlacementHint: false);
        var linearTarget = Node("linear-target", BpmnSemanticTypes.Task);
        var linear = RunLayout(Graph(
            [linearTarget, linearGateway, linearSource],
            [
                Flow("linear-in", linearSource, linearGateway),
                Flow("linear-out", linearGateway, linearTarget),
            ]));

        Assert.Equal(new SizeD(48d, 48d), Geometry(linear, linearGateway).Size);
        Assert.True(
            Geometry(linear, linearSource).Bounds.Right <
            Geometry(linear, linearGateway).Bounds.Left);
        Assert.True(
            Geometry(linear, linearGateway).Bounds.Right <
            Geometry(linear, linearTarget).Bounds.Left);

        var twoSource = Node("two-source", BpmnSemanticTypes.Task);
        var twoSplit = Node("two-split", BpmnSemanticTypes.InclusiveGateway);
        var twoA = Node("two-a", BpmnSemanticTypes.Task);
        var twoB = Node("two-b", BpmnSemanticTypes.Task);
        var twoJoin = Node("two-join", BpmnSemanticTypes.InclusiveGateway);
        var twoNext = Node("two-next", BpmnSemanticTypes.Task);
        ProjectedNode[] twoNodes = [twoB, twoJoin, twoSource, twoA, twoNext, twoSplit];
        ProjectedEdge[] twoEdges =
        [
            Flow("two-source-split", twoSource, twoSplit),
            Flow("two-split-b", twoSplit, twoB),
            Flow("two-split-a", twoSplit, twoA),
            Flow("two-a-join", twoA, twoJoin),
            Flow("two-b-join", twoB, twoJoin),
            Flow("two-join-next", twoJoin, twoNext),
        ];
        var twoLayout = RunLayout(Graph(twoNodes, twoEdges));
        Assert.Equal(
            twoLayout,
            RunLayout(Graph(twoNodes.Reverse(), twoEdges.Reverse())));
        AssertBranchColumn(twoLayout, twoSplit, [twoA, twoB], twoJoin);

        var threeSource = Node("three-source", BpmnSemanticTypes.Task);
        var threeSplit = Node("three-split", BpmnSemanticTypes.InclusiveGateway);
        var threeA = Node("three-a", BpmnSemanticTypes.Task);
        var threeB = Node("three-b", BpmnSemanticTypes.Task);
        var threeC = Node("three-c", BpmnSemanticTypes.Task);
        var threeJoin = Node("three-join", BpmnSemanticTypes.InclusiveGateway);
        var threeNext = Node("three-next", BpmnSemanticTypes.Task);
        ProjectedNode[] threeNodes =
            [threeC, threeJoin, threeSource, threeB, threeNext, threeSplit, threeA];
        ProjectedEdge[] threeEdges =
        [
            Flow("three-source-split", threeSource, threeSplit),
            Flow("three-split-c", threeSplit, threeC),
            Flow("three-split-a", threeSplit, threeA),
            Flow("three-split-b", threeSplit, threeB),
            Flow("three-a-join", threeA, threeJoin),
            Flow("three-b-join", threeB, threeJoin),
            Flow("three-c-join", threeC, threeJoin),
            Flow("three-join-next", threeJoin, threeNext),
        ];
        var threeLayout = RunLayout(Graph(threeNodes, threeEdges));
        Assert.Equal(
            threeLayout,
            RunLayout(Graph(threeNodes.Reverse(), threeEdges.Reverse())));
        AssertBranchColumn(threeLayout, threeSplit, [threeA, threeB, threeC], threeJoin);
        Assert.All(threeLayout.Nodes, static geometry =>
        {
            Assert.True(double.IsFinite(geometry.Bounds.X));
            Assert.True(double.IsFinite(geometry.Bounds.Y));
            Assert.True(double.IsFinite(geometry.Bounds.Width));
            Assert.True(double.IsFinite(geometry.Bounds.Height));
        });

        var pinnedBounds = new RectD(410d, 275d, 64d, 58d);
        var pinnedGateway = Node(
            "pinned-gateway",
            BpmnSemanticTypes.InclusiveGateway,
            new ProjectedPlacementHint(
                pinnedBounds.TopLeft,
                pinnedBounds.Size,
                VisualPlacementMode.Pinned));
        var pinnedA = Node("pinned-a", BpmnSemanticTypes.Task);
        var pinnedB = Node("pinned-b", BpmnSemanticTypes.Task);
        var pinnedGraph = Graph(
            [pinnedB, pinnedGateway, pinnedA],
            [
                Flow("pinned-a", pinnedGateway, pinnedA),
                Flow("pinned-b", pinnedGateway, pinnedB),
            ]);
        var pinned = RunLayout(pinnedGraph);

        Assert.Equal(pinned, RunLayout(pinnedGraph));
        Assert.Equal(pinnedBounds, Geometry(pinned, pinnedGateway).Bounds);
        Assert.False(
            Geometry(pinned, pinnedA).Bounds.Intersects(
                Geometry(pinned, pinnedB).Bounds));
    }

    [Fact]
    public void RoutingUsesIndependentExplicitThreeWaySplitAndJoinEndpoints()
    {
        var split = Node("route-split", BpmnSemanticTypes.InclusiveGateway);
        var taskA = Node("route-task-a", BpmnSemanticTypes.Task);
        var taskB = Node("route-task-b", BpmnSemanticTypes.Task);
        var taskC = Node("route-task-c", BpmnSemanticTypes.Task);
        var join = Node("route-join", BpmnSemanticTypes.InclusiveGateway);

        var splitSources = Enumerable.Range(0, 3)
            .Select(index => Port(
                $"split-source-{index}",
                split,
                ConnectorAnchorSide.Right,
                ConnectorAnchorRoleCapability.Source,
                index,
                3))
            .ToArray();
        var tasks = new[] { taskA, taskB, taskC };
        var taskTargets = tasks.Select((task, index) => Port(
                $"task-target-{index}",
                task,
                ConnectorAnchorSide.Left,
                ConnectorAnchorRoleCapability.Target,
                0,
                1))
            .ToArray();
        var taskSources = tasks.Select((task, index) => Port(
                $"task-source-{index}",
                task,
                ConnectorAnchorSide.Right,
                ConnectorAnchorRoleCapability.Source,
                0,
                1))
            .ToArray();
        var joinTargets = Enumerable.Range(0, 3)
            .Select(index => Port(
                $"join-target-{index}",
                join,
                ConnectorAnchorSide.Left,
                ConnectorAnchorRoleCapability.Target,
                index,
                3))
            .ToArray();
        var outgoing = Enumerable.Range(0, 3)
            .Select(index => Flow(
                $"route-split-{index}",
                split,
                tasks[index],
                splitSources[index],
                taskTargets[index]))
            .ToArray();
        var incoming = Enumerable.Range(0, 3)
            .Select(index => Flow(
                $"route-join-{index}",
                tasks[index],
                join,
                taskSources[index],
                joinTargets[index]))
            .ToArray();
        var graph = Graph(
            [join, taskC, split, taskA, taskB],
            [incoming[2], outgoing[1], incoming[0], outgoing[2], incoming[1], outgoing[0]],
            splitSources
                .Concat(taskTargets)
                .Concat(taskSources)
                .Concat(joinTargets));
        var layout = Layout(
            Geometry(split, new RectD(0d, 100d, 48d, 48d)),
            Geometry(taskA, new RectD(200d, 0d, 120d, 80d)),
            Geometry(taskB, new RectD(200d, 120d, 120d, 80d)),
            Geometry(taskC, new RectD(200d, 240d, 120d, 80d)),
            Geometry(join, new RectD(450d, 100d, 48d, 48d)));

        var algorithm = new BpmnRoutingAlgorithm();
        var first = algorithm.Route(
            graph,
            layout,
            RoutingContext.Empty,
            CancellationToken.None);
        var second = algorithm.Route(
            graph,
            layout,
            RoutingContext.Empty,
            CancellationToken.None);

        Assert.Equal(first, second);
        Assert.True(first.Succeeded);
        var routes = Assert.IsType<RoutingComputation>(first.Computation).Routes;
        Assert.Equal(6, routes.Length);
        PointD[] splitPoints =
            [new(48d, 112d), new(48d, 124d), new(48d, 136d)];
        PointD[] taskLeftPoints =
            [new(200d, 40d), new(200d, 160d), new(200d, 280d)];
        PointD[] taskRightPoints =
            [new(320d, 40d), new(320d, 160d), new(320d, 280d)];
        PointD[] joinPoints =
            [new(450d, 112d), new(450d, 124d), new(450d, 136d)];
        for (var index = 0; index < 3; index++)
        {
            AssertRoute(
                routes,
                outgoing[index],
                splitSources[index],
                taskTargets[index],
                splitPoints[index],
                taskLeftPoints[index]);
            AssertRoute(
                routes,
                incoming[index],
                taskSources[index],
                joinTargets[index],
                taskRightPoints[index],
                joinPoints[index]);
        }

        Assert.Equal(6, routes.Select(static route => route.Path).Distinct().Count());
        Assert.All(routes.SelectMany(static route => route.Path), static point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
    }

    [Fact]
    public void SceneUsesHollowNonHittableInclusiveCircleAndPreservesXAndPlus()
    {
        var inclusive = Node("scene-inclusive", BpmnSemanticTypes.InclusiveGateway);
        var parallel = Node("scene-parallel", BpmnSemanticTypes.ParallelGateway);
        var exclusive = Node("scene-exclusive", BpmnSemanticTypes.ExclusiveGateway);
        var inclusiveBounds = new RectD(200d, 80d, 64d, 58d);
        var parallelBounds = new RectD(330d, 85d, 48d, 48d);
        var exclusiveBounds = new RectD(450d, 85d, 48d, 48d);
        var inclusiveTarget = Port(
            "scene-inclusive-target",
            inclusive,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRoleCapability.Target,
            0,
            1);
        var inclusiveSource = Port(
            "scene-inclusive-source",
            inclusive,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source,
            0,
            1);
        var graph = Graph(
            [parallel, inclusive, exclusive],
            ports: [inclusiveSource, inclusiveTarget]);
        var layout = Layout(
            Geometry(inclusive, inclusiveBounds),
            Geometry(parallel, parallelBounds),
            Geometry(exclusive, exclusiveBounds));
        var inclusiveVisual = Visual(
            inclusive,
            inclusiveBounds,
            [
                ProjectedAnchor(inclusiveTarget, ConnectorAnchorRole.Target),
                ProjectedAnchor(inclusiveSource, ConnectorAnchorRole.Source),
            ]);
        var visuals = new VisualModelSnapshot(
            DocumentId,
            Revision,
            [
                inclusiveVisual,
                Visual(parallel, parallelBounds),
                Visual(exclusive, exclusiveBounds),
            ]);
        var registration = Assert.Single(BpmnPluginRegistration.M34.SceneContributors);
        var routing = new RoutingResult(
            DocumentId,
            Revision,
            BpmnAlgorithmIds.DefaultLayout,
            BpmnAlgorithmIds.DefaultRouting,
            RoutingComputation.Empty);

        var contributionResult = registration.Contributor.Contribute(
            new Canvas2DSceneContributionContext(
                graph,
                layout,
                routing,
                visuals,
                EditorStateSnapshot.Empty,
                Canvas2DSceneConfiguration.Default,
                registration.Descriptor));

        Assert.True(contributionResult.Succeeded);
        var contribution = Assert.IsType<Canvas2DSceneContribution>(
            contributionResult.Contribution);
        Assert.Equal(3, contribution.CanonicalItemVisualOverrides.Length);
        var inclusiveOverride = Assert.Single(
            contribution.CanonicalItemVisualOverrides,
            candidate => candidate.TargetSceneObjectId == CanonicalNodeId(inclusive));
        Assert.Equal(Canvas2DSceneGeometryKind.Path, inclusiveOverride.Geometry.Kind);
        Assert.True(inclusiveOverride.Geometry.IsClosed);
        Assert.Equal(
            [
                new PointD(32d, 0d),
                new PointD(64d, 29d),
                new PointD(32d, 58d),
                new PointD(0d, 29d),
            ],
            inclusiveOverride.Geometry.Points.AsEnumerable());

        var marker = Assert.Single(contribution.Items, item =>
            item.Origin.SemanticElementId == inclusive.Source.SemanticElementId);
        var plus = Assert.Single(contribution.Items, item =>
            item.Origin.SemanticElementId == parallel.Source.SemanticElementId);
        var xor = Assert.Single(contribution.Items, item =>
            item.Origin.SemanticElementId == exclusive.Source.SemanticElementId);
        Assert.Contains("inclusive-gateway-o", marker.Origin.StableSourceKey,
            StringComparison.Ordinal);
        Assert.DoesNotContain("parallel-gateway-plus", marker.Origin.StableSourceKey,
            StringComparison.Ordinal);
        Assert.DoesNotContain("exclusive-gateway-x", marker.Origin.StableSourceKey,
            StringComparison.Ordinal);
        Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, marker.Geometry.Kind);
        var expectedMarkerRadius = 58d * 0.18d;
        Assert.Equal(
            new RectD(
                32d - expectedMarkerRadius,
                29d - expectedMarkerRadius,
                expectedMarkerRadius * 2d,
                expectedMarkerRadius * 2d),
            marker.Geometry.Bounds);
        Assert.Null(marker.Style.Fill);
        Assert.Equal("#000000", marker.Style.Stroke);
        Assert.Equal(3.19d, marker.Style.StrokeWidth, precision: 2);
        Assert.Equal(Canvas2DHitTestMode.None, marker.HitTestPolicy.Mode);
        Assert.Contains("parallel-gateway-plus", plus.Origin.StableSourceKey,
            StringComparison.Ordinal);
        Assert.Contains("exclusive-gateway-x", xor.Origin.StableSourceKey,
            StringComparison.Ordinal);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, plus.Geometry.Kind);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, xor.Geometry.Kind);
        Assert.True(plus.Geometry.IsClosed);
        Assert.True(xor.Geometry.IsClosed);
        Assert.Equal(12, plus.Geometry.Points.Length);
        Assert.Equal(12, xor.Geometry.Points.Length);
        Assert.Equal("#000000", plus.Style.Fill);
        Assert.Equal("#000000", xor.Style.Fill);
        Assert.All(contribution.Items, static item =>
            Assert.NotEqual(Canvas2DSceneGeometryKind.Text, item.Geometry.Kind));

        var sceneResult = new Canvas2DSceneBuilder(
            contributors: BpmnPluginRegistration.M34.SceneContributors).Build(
                graph,
                layout,
                routing,
                visuals,
                new EditorStateSnapshot(selection: [inclusive.Source.VisualStateId!]));
        Assert.True(sceneResult.Succeeded);
        var scene = Assert.IsType<Canvas2DScene>(sceneResult.Scene);
        var body = Assert.Single(scene.Items, item => item.Id == CanonicalNodeId(inclusive));
        Assert.Equal(inclusiveBounds, body.Bounds);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, body.Geometry.Kind);
        Assert.DoesNotContain(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.ProjectedObjectId == inclusive.Id &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle);
        Assert.Single(scene.Items, item =>
            item.Origin.ProjectedObjectId == inclusive.Id &&
            item.Origin.Categories.HasFlag(
                Canvas2DSceneOriginCategory.RegisteredExtension));

        var selection = Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.StableSourceKey?.StartsWith("selection:",
                StringComparison.Ordinal) == true &&
            item.Origin.VisualStateId == inclusive.Source.VisualStateId);
        Assert.Equal(body.Bounds, selection.Bounds);
        Assert.Equal(body.Geometry, selection.Geometry);
        Assert.Equal(8, scene.Items.Count(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == inclusive.Source.VisualStateId &&
            (item.Origin.StableSourceKey?.StartsWith("resize-handle:",
                 StringComparison.Ordinal) == true ||
             item.Origin.StableSourceKey?.StartsWith("resize-edge-zone:",
                 StringComparison.Ordinal) == true)));
        Assert.Equal(2, scene.Items.Count(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == inclusive.Source.VisualStateId &&
            item.Origin.StableSourceKey?.StartsWith("connector-anchor-handle:",
                StringComparison.Ordinal) == true));

        var hit = new Canvas2DSceneHitTestService().HitTest(
            scene,
            new PointD(
                inclusiveBounds.X + (inclusiveBounds.Width / 2d),
                inclusiveBounds.Y + (inclusiveBounds.Height / 2d)));
        Assert.NotNull(hit);
        Assert.Equal(body.Id, hit.SceneObjectId);
    }

    private static ConnectorAnchor Anchor(
        string key,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role,
        int order) =>
        new(new ConnectorAnchorId($"bpmn:m34:anchor:{key}"), side, role, order);

    private static VisualStateSnapshot GatewayVisual(
        string key,
        IEnumerable<ConnectorAnchor> anchors) =>
        new(
            new VisualStateId($"bpmn:m34:visual:{key}"),
            new SemanticElementId($"bpmn:m34:gateway:{key}"),
            new PointD(0d, 0d),
            new SizeD(48d, 48d),
            VisualPlacementMode.Pinned,
            connectorAnchors: anchors);

    private static ResolvedConnectorAnchor[] ResolveAnchors(VisualStateSnapshot visual) =>
        ElementConnectorAnchorResolver.Resolve(
                visual,
                BpmnSemanticTypes.InclusiveGateway,
                new ElementConnectorAnchorPolicyRegistry(
                    BpmnPluginRegistration.M34.ConnectorAnchorPolicies))
            .ToArray();

    private static ResolvedConnectorAnchor[] SideAnchors(
        IEnumerable<ResolvedConnectorAnchor> anchors,
        ConnectorAnchorSide side) =>
        anchors.Where(anchor => anchor.Side == side)
            .OrderBy(static anchor => anchor.Order)
            .ToArray();

    private static PointD[] ResolvePoints(
        RectD bounds,
        ResolvedConnectorAnchor[] anchors) =>
        anchors.Select(anchor => ConnectorAnchorGeometryResolver.ResolvePoint(
                bounds,
                anchor.Side,
                anchor.Order,
                anchors.Length))
            .ToArray();

    private static PointD ResolvePoint(RectD bounds, ResolvedConnectorAnchor anchor) =>
        ConnectorAnchorGeometryResolver.ResolvePoint(
            bounds,
            anchor.Side,
            anchor.Order,
            1);

    private static LayoutComputation RunLayout(ProjectedGraph graph)
    {
        var result = new BpmnLayoutAlgorithm().Compute(
            graph,
            LayoutContext.Empty,
            CancellationToken.None);
        Assert.True(
            result.Succeeded,
            string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        return Assert.IsType<LayoutComputation>(result.Computation);
    }

    private static void AssertBranchColumn(
        LayoutComputation layout,
        ProjectedNode split,
        ProjectedNode[] branches,
        ProjectedNode join)
    {
        var branchBounds = branches
            .Select(branch => Geometry(layout, branch).Bounds)
            .OrderBy(static bounds => bounds.Top)
            .ToArray();
        Assert.All(branchBounds, bounds => Assert.Equal(branchBounds[0].Left, bounds.Left));
        for (var index = 1; index < branchBounds.Length; index++)
        {
            Assert.False(branchBounds[index - 1].Intersects(branchBounds[index]));
        }

        var splitBounds = Geometry(layout, split).Bounds;
        var joinBounds = Geometry(layout, join).Bounds;
        Assert.True(splitBounds.Right < branchBounds[0].Left);
        Assert.True(branchBounds[0].Right < joinBounds.Left);
        var branchCenter =
            (CenterY(branchBounds[0]) + CenterY(branchBounds[^1])) / 2d;
        Assert.Equal(branchCenter, CenterY(splitBounds));
        Assert.Equal(branchCenter, CenterY(joinBounds));
    }

    private static LayoutNodeGeometry Geometry(
        LayoutComputation computation,
        ProjectedNode node) =>
        Assert.Single(
            computation.Nodes,
            geometry => geometry.ProjectedObjectId == node.Id);

    private static LayoutNodeGeometry Geometry(ProjectedNode node, RectD bounds) =>
        new(
            node.Id,
            bounds,
            Matrix2D.CreateTranslation(bounds.X, bounds.Y));

    private static ProjectedNode Node(
        string key,
        SemanticTypeId semanticTypeId,
        ProjectedPlacementHint? placementHint = null,
        bool includePlacementHint = true) =>
        new(
            new ProjectionSourceTrace(
                DocumentId,
                NodeRuleId,
                ProjectionSourceKind.SemanticElement,
                new SemanticElementId($"bpmn:m34:node:{key}"),
                semanticTypeId,
                "node",
                new VisualStateId($"bpmn:m34:node:{key}:visual")),
            includePlacementHint
                ? placementHint ?? new ProjectedPlacementHint(
                    new PointD(900d, 700d),
                    DefaultSize(semanticTypeId),
                    VisualPlacementMode.Automatic)
                : null,
            projectedProperties: SemanticTypeMetadata(semanticTypeId));

    private static SizeD DefaultSize(SemanticTypeId semanticTypeId) =>
        semanticTypeId == BpmnSemanticTypes.Task
            ? new SizeD(120d, 80d)
            : semanticTypeId == BpmnSemanticTypes.ExclusiveGateway ||
                semanticTypeId == BpmnSemanticTypes.ParallelGateway ||
                semanticTypeId == BpmnSemanticTypes.InclusiveGateway
                ? new SizeD(48d, 48d)
                : new SizeD(36d, 36d);

    private static ConnectorAnchorId ProjectedAnchorId(string key) =>
        new($"bpmn:m34:projected-anchor:{key}");

    private static ProjectedPort Port(
        string key,
        ProjectedNode owner,
        ConnectorAnchorSide side,
        ConnectorAnchorRoleCapability roleCapability,
        int order,
        int sideCount)
    {
        var anchorId = ProjectedAnchorId(key);
        return new ProjectedPort(
            new ProjectionSourceTrace(
                DocumentId,
                owner.Source.RuleId,
                ProjectionSourceKind.SemanticElement,
                owner.Source.SemanticElementId,
                owner.Source.SemanticTypeId,
                $"connector-anchor:{anchorId.Value}",
                owner.Source.VisualStateId),
            owner.Id,
            routingHints: ProjectedConnectorAnchorMetadata.Encode(
                new ProjectedConnectorAnchor(
                    anchorId,
                    side,
                    roleCapability,
                    order,
                    sideCount,
                    ResolvedConnectorAnchorKind.Dynamic)));
    }

    private static ConnectorAnchor ProjectedAnchor(
        ProjectedPort port,
        ConnectorAnchorRole role)
    {
        Assert.True(ProjectedConnectorAnchorMetadata.TryDecode(port, out var projected));
        var anchor = Assert.IsType<ProjectedConnectorAnchor>(projected);
        return new ConnectorAnchor(anchor.Id, anchor.Side, role, anchor.Order);
    }

    private static ProjectedEdge Flow(
        string key,
        ProjectedNode source,
        ProjectedNode target,
        ProjectedPort? sourcePort = null,
        ProjectedPort? targetPort = null) =>
        new(
            new ProjectionSourceTrace(
                DocumentId,
                EdgeRuleId,
                ProjectionSourceKind.SemanticRelationship,
                new SemanticElementId($"bpmn:m34:flow:{key}"),
                BpmnSemanticTypes.SequenceFlow,
                "edge",
                new VisualStateId($"bpmn:m34:flow:{key}:visual")),
            source.Id,
            target.Id,
            sourcePort?.Id,
            targetPort?.Id,
            projectedProperties: SemanticTypeMetadata(BpmnSemanticTypes.SequenceFlow));

    private static IEnumerable<KeyValuePair<string, PropertyValue>> SemanticTypeMetadata(
        SemanticTypeId semanticTypeId) =>
        [
            new(
                ProjectedSemanticTypeProperty,
                PropertyValue.FromText(semanticTypeId.Value)),
        ];

    private static ProjectedGraph Graph(
        IEnumerable<ProjectedNode> nodes,
        IEnumerable<ProjectedEdge>? edges = null,
        IEnumerable<ProjectedPort>? ports = null) =>
        new(DocumentId, Revision, nodes, edges, ports: ports);

    private static LayoutResult Layout(params LayoutNodeGeometry[] geometries) =>
        new(
            DocumentId,
            Revision,
            BpmnAlgorithmIds.DefaultLayout,
            new LayoutComputation(geometries));

    private static void AssertRoute(
        IEnumerable<RoutedConnectorGeometry> routes,
        ProjectedEdge edge,
        ProjectedPort sourcePort,
        ProjectedPort targetPort,
        PointD expectedSource,
        PointD expectedTarget)
    {
        var route = Assert.Single(
            routes,
            candidate => candidate.ProjectedEdgeId == edge.Id);
        Assert.Equal(sourcePort.Id, route.SourcePortId);
        Assert.Equal(targetPort.Id, route.TargetPortId);
        Assert.Equal(expectedSource, route.SourceAnchor);
        Assert.Equal(expectedTarget, route.DestinationAnchor);
        Assert.Equal(expectedSource, route.Path[0]);
        Assert.Equal(expectedTarget, route.Path[^1]);
    }

    private static VisualStateSnapshot Visual(
        ProjectedNode node,
        RectD bounds,
        IEnumerable<ConnectorAnchor>? anchors = null) =>
        new(
            node.Source.VisualStateId!,
            node.Source.SemanticElementId,
            bounds.TopLeft,
            bounds.Size,
            VisualPlacementMode.Pinned,
            connectorAnchors: anchors);

    private static SceneObjectId CanonicalNodeId(ProjectedNode node) =>
        Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node");

    private static double CenterY(RectD bounds) => bounds.Top + (bounds.Height / 2d);
}
