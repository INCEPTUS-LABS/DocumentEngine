using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
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

public sealed class BpmnParallelGatewayGraphTests
{
    private const string ProjectedSemanticTypeProperty = "BPMN.ProjectedSemanticType";

    private static readonly DocumentId DocumentId = new("bpmn:m33-graph-unit");
    private static readonly DocumentRevision Revision = new(73);
    private static readonly ProjectionRuleId NodeRuleId = new("bpmn:m33-test/node");
    private static readonly ProjectionRuleId EdgeRuleId = new("bpmn:m33-test/edge");

    [Fact]
    public void M33AddsParallelGatewayPolicyWithoutChangingHistoricalM321Set()
    {
        var historical = BpmnPluginRegistration.M321.ConnectorAnchorPolicies;
        var current = BpmnPluginRegistration.M33.ConnectorAnchorPolicies;

        Assert.Equal(4, historical.Length);
        Assert.DoesNotContain(
            historical,
            registration =>
                registration.ElementTypeId == BpmnSemanticTypes.ParallelGateway);
        Assert.Equal(5, current.Length);
        Assert.Equal(
            historical.AsEnumerable(),
            current.Take(historical.Length));

        var registration = Assert.Single(
            current,
            candidate =>
                candidate.ElementTypeId == BpmnSemanticTypes.ParallelGateway);
        var resolved = new ElementConnectorAnchorPolicyRegistry(current).Resolve(
            BpmnSemanticTypes.ParallelGateway);

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
    public void SplitAndJoinAnchorsUseDistinctIdentitiesAndEvenThirds()
    {
        var split = GatewayVisual(
            "split",
            [
                Anchor("split-target", ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0),
                Anchor("split-source-a", ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
                Anchor("split-source-b", ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 1),
            ]);
        var join = GatewayVisual(
            "join",
            [
                Anchor("join-target-a", ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0),
                Anchor("join-target-b", ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 1),
                Anchor("join-source", ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
            ]);
        var bounds = new RectD(0d, 0d, 48d, 48d);

        var splitAnchors = ResolveAnchors(split);
        var splitSources = splitAnchors
            .Where(static anchor => anchor.Side == ConnectorAnchorSide.Right)
            .OrderBy(static anchor => anchor.Order)
            .ToArray();
        Assert.Equal(2, splitSources.Length);
        Assert.Equal(2, splitSources.Select(static anchor => anchor.Id).Distinct().Count());
        Assert.All(splitSources, static anchor =>
            Assert.True(anchor.Allows(ConnectorAnchorRole.Source)));
        Assert.Equal(
            [new PointD(48d, 16d), new PointD(48d, 32d)],
            ResolvePoints(bounds, splitSources));
        Assert.Equal(
            new PointD(0d, 24d),
            ResolvePoint(
                bounds,
                Assert.Single(splitAnchors, static anchor =>
                    anchor.Side == ConnectorAnchorSide.Left)));

        var joinAnchors = ResolveAnchors(join);
        var joinTargets = joinAnchors
            .Where(static anchor => anchor.Side == ConnectorAnchorSide.Left)
            .OrderBy(static anchor => anchor.Order)
            .ToArray();
        Assert.Equal(2, joinTargets.Length);
        Assert.Equal(2, joinTargets.Select(static anchor => anchor.Id).Distinct().Count());
        Assert.All(joinTargets, static anchor =>
            Assert.True(anchor.Allows(ConnectorAnchorRole.Target)));
        Assert.Equal(
            [new PointD(0d, 16d), new PointD(0d, 32d)],
            ResolvePoints(bounds, joinTargets));
        Assert.Equal(
            new PointD(48d, 24d),
            ResolvePoint(
                bounds,
                Assert.Single(joinAnchors, static anchor =>
                    anchor.Side == ConnectorAnchorSide.Right)));
    }

    [Fact]
    public void LayoutSupportsLinearSplitAndJoinTopologiesDeterministically()
    {
        var linearSource = Node("linear-source", BpmnSemanticTypes.Task);
        var linearGateway = Node(
            "linear-gateway",
            BpmnSemanticTypes.ParallelGateway,
            includePlacementHint: false);
        var linearTarget = Node("linear-target", BpmnSemanticTypes.Task);
        var linearGraph = Graph(
            [linearTarget, linearGateway, linearSource],
            [
                Flow("linear-in", linearSource, linearGateway),
                Flow("linear-out", linearGateway, linearTarget),
            ]);

        var linear = RunLayout(linearGraph);
        Assert.Equal(new SizeD(48d, 48d), Geometry(linear, linearGateway).Size);
        Assert.True(
            Geometry(linear, linearSource).Bounds.Right <
            Geometry(linear, linearGateway).Bounds.Left);
        Assert.True(
            Geometry(linear, linearGateway).Bounds.Right <
            Geometry(linear, linearTarget).Bounds.Left);

        var split = Node("split", BpmnSemanticTypes.ParallelGateway);
        var splitA = Node("split-a", BpmnSemanticTypes.Task);
        var splitB = Node("split-b", BpmnSemanticTypes.Task);
        ProjectedNode[] splitNodes = [splitB, split, splitA];
        ProjectedEdge[] splitEdges =
        [
            Flow("split-b", split, splitB),
            Flow("split-a", split, splitA),
        ];
        var splitGraph = Graph(splitNodes, splitEdges);
        var splitLayout = RunLayout(splitGraph);
        Assert.Equal(
            splitLayout,
            RunLayout(Graph(splitNodes.Reverse(), splitEdges.Reverse())));
        var splitABounds = Geometry(splitLayout, splitA).Bounds;
        var splitBBounds = Geometry(splitLayout, splitB).Bounds;
        Assert.Equal(splitABounds.Left, splitBBounds.Left);
        Assert.False(splitABounds.Intersects(splitBBounds));
        Assert.Equal(
            CenterY(Geometry(splitLayout, split).Bounds),
            (CenterY(splitABounds) + CenterY(splitBBounds)) / 2d);

        var joinA = Node("join-a", BpmnSemanticTypes.Task);
        var joinB = Node("join-b", BpmnSemanticTypes.Task);
        var join = Node("join", BpmnSemanticTypes.ParallelGateway);
        ProjectedNode[] joinNodes = [join, joinB, joinA];
        ProjectedEdge[] joinEdges =
        [
            Flow("join-b", joinB, join),
            Flow("join-a", joinA, join),
        ];
        var joinLayout = RunLayout(Graph(joinNodes, joinEdges));
        Assert.Equal(
            joinLayout,
            RunLayout(Graph(joinNodes.Reverse(), joinEdges.Reverse())));
        var joinABounds = Geometry(joinLayout, joinA).Bounds;
        var joinBBounds = Geometry(joinLayout, joinB).Bounds;
        var joinBounds = Geometry(joinLayout, join).Bounds;
        Assert.Equal(joinABounds.Left, joinBBounds.Left);
        Assert.False(joinABounds.Intersects(joinBBounds));
        Assert.True(joinABounds.Right < joinBounds.Left);
        Assert.True(joinBBounds.Right < joinBounds.Left);
        Assert.Equal(
            CenterY(joinBounds),
            (CenterY(joinABounds) + CenterY(joinBBounds)) / 2d);
    }

    [Fact]
    public void LayoutPreservesPinnedParallelGatewayBoundsInABranch()
    {
        var source = Node("pinned-source", BpmnSemanticTypes.Task);
        var pinnedBounds = new RectD(410d, 275d, 64d, 58d);
        var gateway = Node(
            "pinned-gateway",
            BpmnSemanticTypes.ParallelGateway,
            new ProjectedPlacementHint(
                pinnedBounds.TopLeft,
                pinnedBounds.Size,
                VisualPlacementMode.Pinned));
        var targetA = Node("pinned-target-a", BpmnSemanticTypes.Task);
        var targetB = Node("pinned-target-b", BpmnSemanticTypes.Task);
        var graph = Graph(
            [targetB, gateway, source, targetA],
            [
                Flow("pinned-in", source, gateway),
                Flow("pinned-out-a", gateway, targetA),
                Flow("pinned-out-b", gateway, targetB),
            ]);

        var first = RunLayout(graph);
        var second = RunLayout(graph);

        Assert.Equal(first, second);
        Assert.Equal(pinnedBounds, Geometry(first, gateway).Bounds);
        Assert.False(
            Geometry(first, targetA).Bounds.Intersects(
                Geometry(first, targetB).Bounds));
    }

    [Fact]
    public void RoutingUsesDistinctExplicitSplitAndJoinEndpointsDeterministically()
    {
        var split = Node("route-split", BpmnSemanticTypes.ParallelGateway);
        var taskA = Node("route-task-a", BpmnSemanticTypes.Task);
        var taskB = Node("route-task-b", BpmnSemanticTypes.Task);
        var join = Node("route-join", BpmnSemanticTypes.ParallelGateway);

        var splitSourceA = Port(
            "split-source-a", split, ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source, 0, 2);
        var splitSourceB = Port(
            "split-source-b", split, ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source, 1, 2);
        var taskATarget = Port(
            "task-a-target", taskA, ConnectorAnchorSide.Left,
            ConnectorAnchorRoleCapability.Target, 0, 1);
        var taskBTarget = Port(
            "task-b-target", taskB, ConnectorAnchorSide.Left,
            ConnectorAnchorRoleCapability.Target, 0, 1);
        var taskASource = Port(
            "task-a-source", taskA, ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source, 0, 1);
        var taskBSource = Port(
            "task-b-source", taskB, ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source, 0, 1);
        var joinTargetA = Port(
            "join-target-a", join, ConnectorAnchorSide.Left,
            ConnectorAnchorRoleCapability.Target, 0, 2);
        var joinTargetB = Port(
            "join-target-b", join, ConnectorAnchorSide.Left,
            ConnectorAnchorRoleCapability.Target, 1, 2);

        var splitA = Flow("route-split-a", split, taskA, splitSourceA, taskATarget);
        var splitB = Flow("route-split-b", split, taskB, splitSourceB, taskBTarget);
        var joinA = Flow("route-join-a", taskA, join, taskASource, joinTargetA);
        var joinB = Flow("route-join-b", taskB, join, taskBSource, joinTargetB);
        var graph = Graph(
            [join, taskB, split, taskA],
            [joinB, splitB, joinA, splitA],
            [
                joinTargetB,
                taskBSource,
                splitSourceA,
                taskATarget,
                joinTargetA,
                splitSourceB,
                taskBTarget,
                taskASource,
            ]);
        var layout = Layout(
            Geometry(split, new RectD(0d, 100d, 48d, 48d)),
            Geometry(taskA, new RectD(200d, 20d, 120d, 80d)),
            Geometry(taskB, new RectD(200d, 180d, 120d, 80d)),
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
        Assert.Equal(4, routes.Length);
        AssertRoute(
            routes, splitA, splitSourceA, taskATarget,
            new PointD(48d, 116d), new PointD(200d, 60d));
        AssertRoute(
            routes, splitB, splitSourceB, taskBTarget,
            new PointD(48d, 132d), new PointD(200d, 220d));
        AssertRoute(
            routes, joinA, taskASource, joinTargetA,
            new PointD(320d, 60d), new PointD(450d, 116d));
        AssertRoute(
            routes, joinB, taskBSource, joinTargetB,
            new PointD(320d, 220d), new PointD(450d, 132d));
        Assert.Equal(4, routes.Select(static route => route.Path).Distinct().Count());
        Assert.All(routes.SelectMany(static route => route.Path), static point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
    }

    [Fact]
    public void SceneUsesOneParallelDiamondAndNonHittablePlusWithoutRegressingExclusiveX()
    {
        var parallel = Node("scene-parallel", BpmnSemanticTypes.ParallelGateway);
        var exclusive = Node("scene-exclusive", BpmnSemanticTypes.ExclusiveGateway);
        var parallelBounds = new RectD(210d, 85d, 48d, 48d);
        var exclusiveBounds = new RectD(330d, 85d, 48d, 48d);
        var graph = Graph([parallel, exclusive]);
        var layout = Layout(
            Geometry(parallel, parallelBounds),
            Geometry(exclusive, exclusiveBounds));
        var visuals = new VisualModelSnapshot(
            DocumentId,
            Revision,
            [Visual(parallel, parallelBounds), Visual(exclusive, exclusiveBounds)]);
        var registration = Assert.Single(BpmnPluginRegistration.M3.SceneContributors);
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
        Assert.Equal(2, contribution.CanonicalItemVisualOverrides.Length);
        var parallelOverride = Assert.Single(
            contribution.CanonicalItemVisualOverrides,
            candidate => candidate.TargetSceneObjectId == CanonicalNodeId(parallel));
        Assert.Equal(Canvas2DSceneGeometryKind.Path, parallelOverride.Geometry.Kind);
        Assert.True(parallelOverride.Geometry.IsClosed);
        Assert.Equal(
            [
                new PointD(24d, 0d),
                new PointD(48d, 24d),
                new PointD(24d, 48d),
                new PointD(0d, 24d),
            ],
            parallelOverride.Geometry.Points.AsEnumerable());

        var plus = Assert.Single(contribution.Items, item =>
            item.Origin.SemanticElementId == parallel.Source.SemanticElementId);
        var xor = Assert.Single(contribution.Items, item =>
            item.Origin.SemanticElementId == exclusive.Source.SemanticElementId);
        Assert.Contains("parallel-gateway-plus", plus.Origin.StableSourceKey,
            StringComparison.Ordinal);
        Assert.DoesNotContain("exclusive-gateway-x", plus.Origin.StableSourceKey,
            StringComparison.Ordinal);
        Assert.Contains("exclusive-gateway-x", xor.Origin.StableSourceKey,
            StringComparison.Ordinal);
        Assert.DoesNotContain("parallel-gateway-plus", xor.Origin.StableSourceKey,
            StringComparison.Ordinal);
        Assert.Equal(Canvas2DHitTestMode.None, plus.HitTestPolicy.Mode);
        Assert.Equal(Canvas2DHitTestMode.None, xor.HitTestPolicy.Mode);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, plus.Geometry.Kind);
        Assert.True(plus.Geometry.IsClosed);
        Assert.Equal(12, plus.Geometry.Points.Length);
        Assert.NotEqual(xor.Geometry, plus.Geometry);

        var sceneResult = new Canvas2DSceneBuilder(
            contributors: BpmnPluginRegistration.M3.SceneContributors).Build(
                graph,
                layout,
                routing,
                visuals,
                new EditorStateSnapshot(selection: [parallel.Source.VisualStateId!]));
        Assert.True(sceneResult.Succeeded);
        var scene = Assert.IsType<Canvas2DScene>(sceneResult.Scene);
        var body = Assert.Single(scene.Items, item => item.Id == CanonicalNodeId(parallel));
        Assert.Equal(Canvas2DSceneGeometryKind.Path, body.Geometry.Kind);
        Assert.DoesNotContain(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.ProjectedObjectId == parallel.Id &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle);
        Assert.Single(scene.Items, item =>
            item.Origin.ProjectedObjectId == parallel.Id &&
            item.Origin.Categories.HasFlag(
                Canvas2DSceneOriginCategory.RegisteredExtension));
        var hit = new Canvas2DSceneHitTestService().HitTest(
            scene,
            new PointD(parallelBounds.X + 24d, parallelBounds.Y + 24d));
        Assert.NotNull(hit);
        Assert.Equal(body.Id, hit.SceneObjectId);
    }

    private static ConnectorAnchor Anchor(
        string key,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role,
        int order) =>
        new(new ConnectorAnchorId($"bpmn:m33:anchor:{key}"), side, role, order);

    private static VisualStateSnapshot GatewayVisual(
        string key,
        IEnumerable<ConnectorAnchor> anchors) =>
        new(
            new VisualStateId($"bpmn:m33:visual:{key}"),
            new SemanticElementId($"bpmn:m33:gateway:{key}"),
            new PointD(0d, 0d),
            new SizeD(48d, 48d),
            VisualPlacementMode.Pinned,
            connectorAnchors: anchors);

    private static ResolvedConnectorAnchor[] ResolveAnchors(VisualStateSnapshot visual) =>
        ElementConnectorAnchorResolver.Resolve(
                visual,
                BpmnSemanticTypes.ParallelGateway,
                new ElementConnectorAnchorPolicyRegistry(
                    BpmnPluginRegistration.M33.ConnectorAnchorPolicies))
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
                new SemanticElementId($"bpmn:m33:node:{key}"),
                semanticTypeId,
                "node",
                new VisualStateId($"bpmn:m33:node:{key}:visual")),
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
                semanticTypeId == BpmnSemanticTypes.ParallelGateway
                ? new SizeD(48d, 48d)
                : new SizeD(36d, 36d);

    private static ProjectedPort Port(
        string key,
        ProjectedNode owner,
        ConnectorAnchorSide side,
        ConnectorAnchorRoleCapability roleCapability,
        int order,
        int sideCount)
    {
        var anchorId = new ConnectorAnchorId($"bpmn:m33:projected-anchor:{key}");
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
                new SemanticElementId($"bpmn:m33:flow:{key}"),
                BpmnSemanticTypes.SequenceFlow,
                "edge",
                new VisualStateId($"bpmn:m33:flow:{key}:visual")),
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

    private static VisualStateSnapshot Visual(ProjectedNode node, RectD bounds) =>
        new(
            node.Source.VisualStateId!,
            node.Source.SemanticElementId,
            bounds.TopLeft,
            bounds.Size,
            VisualPlacementMode.Pinned);

    private static SceneObjectId CanonicalNodeId(ProjectedNode node) =>
        Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node");

    private static double CenterY(RectD bounds) => bounds.Top + (bounds.Height / 2d);
}
