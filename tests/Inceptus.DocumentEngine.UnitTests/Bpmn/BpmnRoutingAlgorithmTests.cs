using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnRoutingAlgorithmTests
{
    private const string ProjectedSemanticTypeProperty = "BPMN.ProjectedSemanticType";

    private static readonly DocumentId DocumentId = new("bpmn:m2:routing-unit");
    private static readonly DocumentRevision Revision = new(17);
    private static readonly AlgorithmId LayoutAlgorithmId = new("test:bpmn:layout");
    private static readonly ProjectionRuleId NodeRuleId = new("test:bpmn:projection/node");
    private static readonly ProjectionRuleId EdgeRuleId = new("test:bpmn:projection/edge");

    [Fact]
    public void RoutesStraightStartTaskAndTaskEndWithNullPortFallback()
    {
        var start = Node("start", BpmnSemanticTypes.StartEvent);
        var task = Node("task", BpmnSemanticTypes.Task);
        var end = Node("end", BpmnSemanticTypes.EndEvent);
        var startTask = Edge("start-task", start, task);
        var taskEnd = Edge("task-end", task, end);
        var graph = Graph([start, task, end], [startTask, taskEnd]);
        var layout = Layout(
            graph,
            (start, new RectD(0d, 20d, 36d, 36d)),
            (task, new RectD(100d, 0d, 120d, 76d)),
            (end, new RectD(280d, 20d, 36d, 36d)));

        var routing = Route(graph, layout);

        AssertRoute(
            routing,
            startTask,
            new PointD(36d, 38d),
            new PointD(100d, 38d));
        AssertRoute(
            routing,
            taskEnd,
            new PointD(220d, 38d),
            new PointD(280d, 38d));
        Assert.All(routing.Routes, route =>
        {
            Assert.Equal(2, route.Path.Length);
            Assert.Empty(route.BendPoints);
            Assert.Null(route.SourcePortId);
            Assert.Null(route.TargetPortId);
        });
    }

    [Fact]
    public void RoutesVerticallyOffsetSequenceFlowOrthogonally()
    {
        var start = Node("start", BpmnSemanticTypes.StartEvent);
        var task = Node("task", BpmnSemanticTypes.Task);
        var edge = Edge("start-task", start, task);
        var graph = Graph([start, task], [edge]);
        var layout = Layout(
            graph,
            (start, new RectD(0d, 0d, 36d, 36d)),
            (task, new RectD(140d, 70d, 120d, 80d)));

        var route = Assert.Single(Route(graph, layout).Routes);

        Assert.Equal(new PointD(36d, 18d), route.SourceAnchor);
        Assert.Equal(new PointD(140d, 110d), route.DestinationAnchor);
        Assert.Equal(
            [new PointD(46d, 18d), new PointD(46d, 110d)],
            route.BendPoints.AsEnumerable());
        Assert.Equal(
            [
                new PointD(36d, 18d),
                new PointD(46d, 18d),
                new PointD(46d, 110d),
                new PointD(140d, 110d),
            ],
            route.Path.AsEnumerable());
        AssertOrthogonal(route);
    }

    [Fact]
    public void RoutesAroundSingleInflatedObstacleWithTenUnitClearance()
    {
        var source = Node("single-obstacle-source", BpmnSemanticTypes.Task);
        var obstacle = Node("single-obstacle", BpmnSemanticTypes.Task);
        var target = Node("single-obstacle-target", BpmnSemanticTypes.Task);
        var edge = Edge("single-obstacle-flow", source, target);
        var graph = Graph([source, obstacle, target], [edge]);
        var obstacleBounds = new RectD(150d, 80d, 60d, 80d);
        var layout = Layout(
            graph,
            (source, new RectD(20d, 100d, 40d, 40d)),
            (obstacle, obstacleBounds),
            (target, new RectD(320d, 100d, 40d, 40d)));

        var route = Assert.Single(Route(graph, layout).Routes);
        var inflatedObstacle = Inflate(obstacleBounds, 10d);

        AssertOrthogonal(route);
        AssertObstacleClear(route, inflatedObstacle);
        Assert.Contains(route.Path, point =>
            point.X == inflatedObstacle.Left ||
            point.X == inflatedObstacle.Right ||
            point.Y == inflatedObstacle.Top ||
            point.Y == inflatedObstacle.Bottom);
        Assert.All(route.Path, AssertPositiveFinite);
    }

    [Fact]
    public void ShortensEndpointLeadBeforeTurningAroundPreferredClearance()
    {
        var source = Node("adaptive-lead-source", BpmnSemanticTypes.Task);
        var obstacle = Node("adaptive-lead-obstacle", BpmnSemanticTypes.Task);
        var target = Node("adaptive-lead-target", BpmnSemanticTypes.Task);
        var edge = Edge("adaptive-lead-flow", source, target);
        var graph = Graph([source, obstacle, target], [edge]);
        var sourceBounds = new RectD(20d, 100d, 80d, 60d);
        var obstacleBounds = new RectD(119d, 139d, 80d, 80d);
        var targetBounds = new RectD(300d, 100d, 80d, 60d);
        var layout = Layout(
            graph,
            (source, sourceBounds),
            (obstacle, obstacleBounds),
            (target, targetBounds));

        var first = Assert.Single(Route(graph, layout).Routes);
        var second = Assert.Single(Route(graph, layout).Routes);
        var preferredInflatedObstacle = Inflate(obstacleBounds, 10d);

        Assert.Equal(first, second);
        Assert.Equal(new PointD(100d, 130d), first.SourceAnchor);
        Assert.Equal(new PointD(300d, 130d), first.DestinationAnchor);
        Assert.Equal(first.SourceAnchor.Y, first.Path[1].Y);
        Assert.True(first.Path[1].X > first.SourceAnchor.X);
        Assert.True(first.Path[1].X < first.SourceAnchor.X + 10d);
        Assert.True(
            SegmentCrossesStrictInterior(
                first.SourceAnchor,
                new PointD(110d, 130d),
                preferredInflatedObstacle),
            "The fixture must reject the preferred ten-unit endpoint lead.");
        AssertExitsSide(
            first.Path[0],
            first.Path[1],
            ConnectorAnchorSide.Right);
        AssertApproachesSide(
            first.Path[^2],
            first.Path[^1],
            ConnectorAnchorSide.Left);
        AssertOrthogonal(first);
        AssertObstacleClear(first, preferredInflatedObstacle);
        AssertObstacleClear(first, obstacleBounds);
        Assert.All(first.Path, AssertPositiveFinite);
    }

    [Fact]
    public void RelaxesPreferredClearanceWhenEndpointIsOutsideActualBody()
    {
        var source = Node("adaptive-clearance-source", BpmnSemanticTypes.Task);
        var obstacle = Node("adaptive-clearance-obstacle", BpmnSemanticTypes.Task);
        var target = Node("adaptive-clearance-target", BpmnSemanticTypes.Task);
        var edge = Edge("adaptive-clearance-flow", source, target);
        var graph = Graph([source, obstacle, target], [edge]);
        var obstacleBounds = new RectD(109d, 139d, 80d, 80d);
        var layout = Layout(
            graph,
            (source, new RectD(20d, 100d, 80d, 60d)),
            (obstacle, obstacleBounds),
            (target, new RectD(300d, 100d, 80d, 60d)));

        var routing = Route(graph, layout);
        var route = Assert.Single(routing.Routes);
        var preferredInflatedObstacle = Inflate(obstacleBounds, 10d);

        Assert.True(
            route.SourceAnchor.X > preferredInflatedObstacle.Left &&
            route.SourceAnchor.X < preferredInflatedObstacle.Right &&
            route.SourceAnchor.Y > preferredInflatedObstacle.Top &&
            route.SourceAnchor.Y < preferredInflatedObstacle.Bottom,
            "The source attachment must make preferred clearance infeasible.");
        Assert.False(
            route.SourceAnchor.X > obstacleBounds.Left &&
            route.SourceAnchor.X < obstacleBounds.Right &&
            route.SourceAnchor.Y > obstacleBounds.Top &&
            route.SourceAnchor.Y < obstacleBounds.Bottom,
            "The source attachment must remain outside the hard node body.");
        Assert.Empty(routing.NoRouteEdgeIds);
        Assert.Empty(routing.Diagnostics);
        AssertExitsSide(
            route.Path[0],
            route.Path[1],
            ConnectorAnchorSide.Right);
        AssertApproachesSide(
            route.Path[^2],
            route.Path[^1],
            ConnectorAnchorSide.Left);
        AssertOrthogonal(route);
        AssertObstacleClear(route, obstacleBounds);
        Assert.All(route.Path, AssertPositiveFinite);
    }

    [Fact]
    public void RoutesDeterministicallyAroundMultipleInflatedObstacles()
    {
        var source = Node("multiple-source", BpmnSemanticTypes.Task);
        var obstacleA = Node("multiple-obstacle-a", BpmnSemanticTypes.ExclusiveGateway);
        var obstacleB = Node("multiple-obstacle-b", BpmnSemanticTypes.Task);
        var target = Node("multiple-target", BpmnSemanticTypes.Task);
        var edge = Edge("multiple-flow", source, target);
        var graph = Graph([obstacleB, target, source, obstacleA], [edge]);
        var obstacleABounds = new RectD(160d, 130d, 80d, 100d);
        var obstacleBBounds = new RectD(320d, 170d, 80d, 100d);
        var layout = Layout(
            graph,
            (source, new RectD(20d, 180d, 40d, 40d)),
            (obstacleA, obstacleABounds),
            (obstacleB, obstacleBBounds),
            (target, new RectD(560d, 180d, 40d, 40d)));

        var first = Assert.Single(Route(graph, layout).Routes);
        var second = Assert.Single(Route(graph, layout).Routes);

        Assert.Equal(first, second);
        AssertOrthogonal(first);
        AssertObstacleClear(first, Inflate(obstacleABounds, 10d));
        AssertObstacleClear(first, Inflate(obstacleBBounds, 10d));
        Assert.All(first.Path, AssertPositiveFinite);
    }

    [Fact]
    public void RoutingScalesDeterministicallyAcrossManyDistinctObstacleBoundaries()
    {
        var source = Node("scaling-source", BpmnSemanticTypes.Task);
        var target = Node("scaling-target", BpmnSemanticTypes.Task);
        var obstacles = Enumerable.Range(0, 32)
            .Select(index => Node($"scaling-obstacle-{index:D2}", BpmnSemanticTypes.Task))
            .ToArray();
        var edge = Edge("scaling-flow", source, target);
        var graph = Graph([source, .. obstacles.Reverse(), target], [edge]);
        var placements = new List<(ProjectedNode Node, RectD Bounds)>
        {
            (source, new RectD(20d, 300d, 60d, 60d)),
            (target, new RectD(1_260d, 300d, 60d, 60d)),
        };
        placements.AddRange(obstacles.Select(static (obstacle, index) =>
            (
                obstacle,
                new RectD(
                    120d + (index * 34d),
                    40d + ((index * 73d) % 520d),
                    22d,
                    28d))));
        var layout = Layout(graph, [.. placements]);

        var routes = Enumerable.Range(0, 3)
            .Select(_ => Assert.Single(Route(graph, layout).Routes))
            .ToArray();

        Assert.All(routes, route => Assert.Equal(routes[0], route));
        AssertOrthogonal(routes[0]);
        Assert.All(routes[0].Path, AssertPositiveFinite);
        foreach (var (_, bounds) in placements.Skip(2))
        {
            AssertObstacleClear(routes[0], Inflate(bounds, 10d));
        }
    }

    [Fact]
    public void UsesOpenNarrowCorridorButDetoursWhenClearanceInflationClosesIt()
    {
        var source = Node("corridor-source", BpmnSemanticTypes.Task);
        var target = Node("corridor-target", BpmnSemanticTypes.Task);
        var upper = Node("corridor-upper", BpmnSemanticTypes.Task);
        var lower = Node("corridor-lower", BpmnSemanticTypes.Task);
        var edge = Edge("corridor-flow", source, target);
        var graph = Graph([source, target, upper, lower], [edge]);
        var sourceBounds = new RectD(0d, 100d, 100d, 80d);
        var targetBounds = new RectD(400d, 100d, 100d, 80d);
        var upperBounds = new RectD(150d, 20d, 120d, 108d);
        var openLowerBounds = new RectD(150d, 152d, 120d, 108d);
        var closedLowerBounds = new RectD(150d, 146d, 120d, 108d);

        var open = Assert.Single(Route(
            graph,
            Layout(
                graph,
                (source, sourceBounds),
                (target, targetBounds),
                (upper, upperBounds),
                (lower, openLowerBounds))).Routes);
        var closed = Assert.Single(Route(
            graph,
            Layout(
                graph,
                (source, sourceBounds),
                (target, targetBounds),
                (upper, upperBounds),
                (lower, closedLowerBounds))).Routes);

        Assert.Equal([open.SourceAnchor, open.DestinationAnchor], open.Path.AsEnumerable());
        Assert.True(closed.Path.Length > 2);
        Assert.NotEqual(open.Path, closed.Path);
        AssertObstacleClear(closed, Inflate(upperBounds, 10d));
        AssertObstacleClear(closed, Inflate(closedLowerBounds, 10d));
        AssertOrthogonal(closed);
    }

    [Theory]
    [InlineData(ConnectorAnchorSide.Top)]
    [InlineData(ConnectorAnchorSide.Right)]
    [InlineData(ConnectorAnchorSide.Bottom)]
    [InlineData(ConnectorAnchorSide.Left)]
    public void SourceAnchorSideControlsInitialDirection(ConnectorAnchorSide side)
    {
        var source = Node($"source-side-{side}", BpmnSemanticTypes.Task);
        var target = Node($"source-side-target-{side}", BpmnSemanticTypes.Task);
        var sourcePort = Port(
            $"source-side-{side}",
            source,
            side,
            ConnectorAnchorRoleCapability.Source);
        var targetPort = Port(
            $"source-side-target-{side}",
            target,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRoleCapability.Target);
        var edge = Edge(
            $"source-side-{side}",
            source,
            target,
            sourcePortId: sourcePort.Id,
            targetPortId: targetPort.Id);
        var graph = Graph([source, target], [edge], [sourcePort, targetPort]);
        var layout = Layout(
            graph,
            (source, new RectD(200d, 200d, 80d, 80d)),
            (target, new RectD(500d, 200d, 80d, 80d)));

        var route = Assert.Single(Route(graph, layout).Routes);

        AssertExitsSide(route.Path[0], route.Path[1], side);
        AssertOrthogonal(route);
    }

    [Theory]
    [InlineData(ConnectorAnchorSide.Top)]
    [InlineData(ConnectorAnchorSide.Right)]
    [InlineData(ConnectorAnchorSide.Bottom)]
    [InlineData(ConnectorAnchorSide.Left)]
    public void TargetAnchorSideControlsFinalApproach(ConnectorAnchorSide side)
    {
        var source = Node($"target-side-source-{side}", BpmnSemanticTypes.Task);
        var target = Node($"target-side-{side}", BpmnSemanticTypes.Task);
        var sourcePort = Port(
            $"target-side-source-{side}",
            source,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source);
        var targetPort = Port(
            $"target-side-{side}",
            target,
            side,
            ConnectorAnchorRoleCapability.Target);
        var edge = Edge(
            $"target-side-{side}",
            source,
            target,
            sourcePortId: sourcePort.Id,
            targetPortId: targetPort.Id);
        var graph = Graph([source, target], [edge], [sourcePort, targetPort]);
        var layout = Layout(
            graph,
            (source, new RectD(200d, 200d, 80d, 80d)),
            (target, new RectD(500d, 200d, 80d, 80d)));

        var route = Assert.Single(Route(graph, layout).Routes);

        AssertApproachesSide(route.Path[^2], route.Path[^1], side);
        AssertOrthogonal(route);
    }

    [Fact]
    public void EndpointOwnersRemainExcludedWhenTargetIsLeftOfSource()
    {
        var source = Node("reverse-source", BpmnSemanticTypes.Task);
        var target = Node("reverse-target", BpmnSemanticTypes.Task);
        var edge = Edge("reverse-flow", source, target);
        var graph = Graph([source, target], [edge]);
        var sourceBounds = new RectD(320d, 100d, 80d, 60d);
        var targetBounds = new RectD(100d, 100d, 80d, 60d);
        var layout = Layout(graph, (source, sourceBounds), (target, targetBounds));

        var route = Assert.Single(Route(graph, layout).Routes);

        Assert.True(route.Path.Length > 2);
        AssertOrthogonal(route);
        AssertObstacleClear(route, sourceBounds);
        AssertObstacleClear(route, targetBounds);
        AssertExitsSide(route.Path[0], route.Path[1], ConnectorAnchorSide.Right);
        AssertApproachesSide(
            route.Path[^2],
            route.Path[^1],
            ConnectorAnchorSide.Left);
        Assert.Equal(new PointD(sourceBounds.Right + 10d, 130d), route.Path[1]);
        Assert.Equal(new PointD(targetBounds.Left - 10d, 130d), route.Path[^2]);
        AssertEndpointOwnerExcludedAfterLead(route, Inflate(sourceBounds, 10d));
        AssertEndpointOwnerExcludedBeforeLead(route, Inflate(targetBounds, 10d));
    }

    [Fact]
    public void LegalNodesAtLeftTopAndOriginProduceOnlyPositiveDocumentRoutePoints()
    {
        var source = Node("boundary-source", BpmnSemanticTypes.Task);
        var target = Node("boundary-target", BpmnSemanticTypes.Task);
        var edge = Edge("boundary-flow", source, target);
        var graph = Graph([source, target], [edge]);
        var arrangements = new[]
        {
            (new RectD(0d, 100d, 120d, 80d), new RectD(200d, 100d, 120d, 80d)),
            (new RectD(100d, 0d, 120d, 80d), new RectD(300d, 20d, 120d, 80d)),
            (new RectD(0d, 0d, 120d, 80d), new RectD(180d, 70d, 120d, 80d)),
        };

        foreach (var (sourceBounds, targetBounds) in arrangements)
        {
            var route = Assert.Single(Route(
                graph,
                Layout(graph, (source, sourceBounds), (target, targetBounds))).Routes);

            Assert.All(route.Path, point =>
            {
                Assert.True(point.X >= 0d, $"Route X was {point.X}.");
                Assert.True(point.Y >= 0d, $"Route Y was {point.Y}.");
            });
        }
    }

    [Fact]
    public void BoundaryFacingSourceAtDocumentTopProducesDeterministicNoRoute()
    {
        var source = Node("top-boundary-source", BpmnSemanticTypes.Task);
        var target = Node("top-boundary-target", BpmnSemanticTypes.Task);
        var sourcePort = Port(
            "top-boundary-source",
            source,
            ConnectorAnchorSide.Top,
            ConnectorAnchorRoleCapability.Source);
        var targetPort = Port(
            "top-boundary-target",
            target,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRoleCapability.Target);
        var edge = Edge(
            "top-boundary-flow",
            source,
            target,
            sourcePortId: sourcePort.Id,
            targetPortId: targetPort.Id);
        var graph = Graph([source, target], [edge], [sourcePort, targetPort]);
        var layout = Layout(
            graph,
            (source, new RectD(100d, 0d, 80d, 60d)),
            (target, new RectD(400d, 100d, 80d, 60d)));

        var first = Engine().Route(graph, layout, BpmnAlgorithmIds.DefaultRouting);
        var second = Engine().Route(graph, layout, BpmnAlgorithmIds.DefaultRouting);

        Assert.Equal(first, second);
        AssertNoRoute(first, edge);
        AssertNoRoute(second, edge);
    }

    [Fact]
    public void RoutesGatewayIncomingAndTwoOutgoingFlowsIndependentlyAndDeterministically()
    {
        var source = Node("gateway-source", BpmnSemanticTypes.Task);
        var gateway = Node("gateway", BpmnSemanticTypes.ExclusiveGateway);
        var taskA = Node("gateway-target-a", BpmnSemanticTypes.Task);
        var taskB = Node("gateway-target-b", BpmnSemanticTypes.Task);
        var incoming = Edge("gateway-incoming", source, gateway);
        var outgoingA = Edge("gateway-outgoing-a", gateway, taskA);
        var outgoingB = Edge("gateway-outgoing-b", gateway, taskB);
        var graph = Graph(
            [taskB, gateway, source, taskA],
            [outgoingB, incoming, outgoingA]);
        var layout = Layout(
            graph,
            (source, new RectD(0d, 70d, 120d, 80d)),
            (gateway, new RectD(220d, 86d, 48d, 48d)),
            (taskA, new RectD(360d, 0d, 120d, 80d)),
            (taskB, new RectD(360d, 140d, 120d, 80d)));

        var first = Route(graph, layout);
        var second = Route(graph, layout);

        Assert.Equal(first, second);
        Assert.Equal(3, first.Routes.Length);
        AssertRoute(first, incoming, new PointD(120d, 110d), new PointD(220d, 110d));
        AssertRoute(first, outgoingA, new PointD(268d, 110d), new PointD(360d, 40d));
        AssertRoute(first, outgoingB, new PointD(268d, 110d), new PointD(360d, 180d));
        Assert.NotEqual(
            first.Routes.Single(route => route.ProjectedEdgeId == outgoingA.Id).Path,
            first.Routes.Single(route => route.ProjectedEdgeId == outgoingB.Id).Path);
        Assert.All(first.Routes.SelectMany(static route => route.Path), point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
    }

    [Fact]
    public void RoutesDistinctAnchorSelfLoopOutsideOwnerBody()
    {
        var task = Node("self-loop-task", BpmnSemanticTypes.Task);
        var sourcePort = Port(
            "self-loop-source",
            task,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source);
        var targetPort = Port(
            "self-loop-target",
            task,
            ConnectorAnchorSide.Bottom,
            ConnectorAnchorRoleCapability.Target);
        var edge = Edge(
            "self-loop-flow",
            task,
            task,
            sourcePortId: sourcePort.Id,
            targetPortId: targetPort.Id);
        var graph = Graph([task], [edge], [sourcePort, targetPort]);
        var ownerBounds = new RectD(200d, 200d, 100d, 80d);
        var layout = Layout(graph, (task, ownerBounds));

        var route = Assert.Single(Route(graph, layout).Routes);

        Assert.Equal(new PointD(300d, 240d), route.SourceAnchor);
        Assert.Equal(new PointD(250d, 280d), route.DestinationAnchor);
        AssertOrthogonal(route);
        AssertObstacleClear(route, ownerBounds);
        Assert.Contains(route.Path, point => point.X == ownerBounds.Right + 10d);
        Assert.Contains(route.Path, point => point.Y == ownerBounds.Bottom + 10d);
        AssertExitsSide(route.Path[0], route.Path[1], ConnectorAnchorSide.Right);
        AssertApproachesSide(
            route.Path[^2],
            route.Path[^1],
            ConnectorAnchorSide.Bottom);
    }

    [Theory]
    [InlineData(ConnectorAnchorSide.Right, ConnectorAnchorSide.Left)]
    [InlineData(ConnectorAnchorSide.Top, ConnectorAnchorSide.Bottom)]
    public void OpposingAnchorSelfLoopContactsBodyExactlyAndExcludesInflatedOwner(
        ConnectorAnchorSide sourceSide,
        ConnectorAnchorSide targetSide)
    {
        var task = Node(
            $"opposing-self-loop-{sourceSide}-{targetSide}",
            BpmnSemanticTypes.Task);
        var sourcePort = Port(
            $"opposing-self-loop-source-{sourceSide}",
            task,
            sourceSide,
            ConnectorAnchorRoleCapability.Source);
        var targetPort = Port(
            $"opposing-self-loop-target-{targetSide}",
            task,
            targetSide,
            ConnectorAnchorRoleCapability.Target);
        var edge = Edge(
            $"opposing-self-loop-{sourceSide}-{targetSide}",
            task,
            task,
            sourcePortId: sourcePort.Id,
            targetPortId: targetPort.Id);
        var graph = Graph([task], [edge], [sourcePort, targetPort]);
        var ownerBounds = new RectD(200d, 200d, 100d, 80d);

        var route = Assert.Single(Route(
            graph,
            Layout(graph, (task, ownerBounds))).Routes);

        Assert.Equal(AnchorPoint(ownerBounds, sourceSide), route.SourceAnchor);
        Assert.Equal(AnchorPoint(ownerBounds, targetSide), route.DestinationAnchor);
        Assert.Equal(LeadPoint(route.SourceAnchor, sourceSide), route.Path[1]);
        Assert.Equal(LeadPoint(route.DestinationAnchor, targetSide), route.Path[^2]);
        AssertExitsSide(route.Path[0], route.Path[1], sourceSide);
        AssertApproachesSide(route.Path[^2], route.Path[^1], targetSide);
        AssertOrthogonal(route);
        AssertSelfLoopOwnerExcludedBetweenLeads(route, Inflate(ownerBounds, 10d));
    }

    [Fact]
    public void WholeRouteBendCostIncludesEndpointLeadsAndMandatoryWaypointTransition()
    {
        var source = Node("whole-cost-source", BpmnSemanticTypes.Task);
        var target = Node("whole-cost-target", BpmnSemanticTypes.Task);
        var sourcePort = Port(
            "whole-cost-source",
            source,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source);
        var targetPort = Port(
            "whole-cost-target",
            target,
            ConnectorAnchorSide.Bottom,
            ConnectorAnchorRoleCapability.Target);
        var mandatoryWaypoint = new PointD(400d, 500d);
        var edge = Edge(
            "whole-cost-flow",
            source,
            target,
            persistentRoute:
            [
                new PointD(280d, 500d),
                mandatoryWaypoint,
                new PointD(540d, 380d),
            ],
            sourcePortId: sourcePort.Id,
            targetPortId: targetPort.Id);
        var graph = Graph([target, source], [edge], [targetPort, sourcePort]);
        var layout = Layout(
            graph,
            (source, new RectD(200d, 460d, 80d, 80d)),
            (target, new RectD(500d, 300d, 80d, 80d)));

        var route = Assert.Single(Route(graph, layout).Routes);

        Assert.Equal(new PointD(280d, 500d), route.SourceAnchor);
        Assert.Equal(new PointD(540d, 380d), route.DestinationAnchor);
        Assert.Equal(1, route.Path.Count(point => point == mandatoryWaypoint));
        Assert.Equal(1, CountDirectionChanges(route.Path));
        AssertExitsSide(
            route.Path[0],
            route.Path[1],
            ConnectorAnchorSide.Right);
        AssertApproachesSide(
            route.Path[^2],
            route.Path[^1],
            ConnectorAnchorSide.Bottom);
        AssertOrthogonal(route);
    }

    [Fact]
    public void EqualLengthObstacleDetoursPreferDeterministicTopRouteWithMinimumBends()
    {
        var source = Node("tie-source", BpmnSemanticTypes.Task);
        var obstacle = Node("tie-obstacle", BpmnSemanticTypes.Task);
        var target = Node("tie-target", BpmnSemanticTypes.Task);
        var edge = Edge("tie-flow", source, target);
        var graph = Graph([target, obstacle, source], [edge]);
        var obstacleBounds = new RectD(170d, 100d, 80d, 100d);
        var layout = Layout(
            graph,
            (source, new RectD(20d, 130d, 40d, 40d)),
            (obstacle, obstacleBounds),
            (target, new RectD(360d, 130d, 40d, 40d)));

        var routes = Enumerable.Range(0, 12)
            .Select(_ => Assert.Single(Route(graph, layout).Routes))
            .ToArray();

        Assert.All(routes, route => Assert.Equal(routes[0], route));
        Assert.Contains(routes[0].Path, point => point.Y == obstacleBounds.Top - 10d);
        Assert.DoesNotContain(
            routes[0].Path,
            point => point.Y == obstacleBounds.Bottom + 10d);
        Assert.Equal(4, CountDirectionChanges(routes[0].Path));
        AssertObstacleClear(routes[0], Inflate(obstacleBounds, 10d));
    }

    [Fact]
    public void ResolvesExplicitEncodedPortsAndPreservesPortIdentities()
    {
        var start = Node("start", BpmnSemanticTypes.StartEvent);
        var task = Node("task", BpmnSemanticTypes.Task);
        var sourcePort = Port(
            "source",
            start,
            ConnectorAnchorSide.Bottom,
            ConnectorAnchorRoleCapability.Source);
        var targetPort = Port(
            "target",
            task,
            ConnectorAnchorSide.Top,
            ConnectorAnchorRoleCapability.Target);
        var edge = Edge(
            "start-task",
            start,
            task,
            sourcePortId: sourcePort.Id,
            targetPortId: targetPort.Id);
        var graph = Graph([start, task], [edge], [sourcePort, targetPort]);
        var layout = Layout(
            graph,
            (start, new RectD(0d, 0d, 40d, 40d)),
            (task, new RectD(100d, 80d, 80d, 40d)));

        var route = Assert.Single(Route(graph, layout).Routes);

        Assert.Equal(new PointD(20d, 40d), route.SourceAnchor);
        Assert.Equal(new PointD(140d, 80d), route.DestinationAnchor);
        Assert.Equal(sourcePort.Id, route.SourcePortId);
        Assert.Equal(targetPort.Id, route.TargetPortId);
    }

    [Fact]
    public void PersistentRoutePreservesInteriorBendsButRefreshesEndpoints()
    {
        var start = Node("start", BpmnSemanticTypes.StartEvent);
        var task = Node("task", BpmnSemanticTypes.Task);
        var oldSource = new PointD(36d, 18d);
        var firstBend = new PointD(80d, 200d);
        var secondBend = new PointD(170d, 200d);
        var oldTarget = new PointD(140d, 110d);
        var edge = Edge(
            "start-task",
            start,
            task,
            persistentRoute: [oldSource, firstBend, secondBend, oldTarget]);
        var graph = Graph([start, task], [edge]);
        var layout = Layout(
            graph,
            (start, new RectD(20d, 30d, 50d, 40d)),
            (task, new RectD(200d, 90d, 120d, 80d)));

        var route = Assert.Single(Route(graph, layout).Routes);

        Assert.Equal(new PointD(70d, 50d), route.SourceAnchor);
        Assert.Equal(new PointD(200d, 130d), route.DestinationAnchor);
        AssertSubsequence(route.Path, firstBend, secondBend);
        AssertOrthogonal(route);
        Assert.NotEqual(oldSource, route.SourceAnchor);
        Assert.NotEqual(oldTarget, route.DestinationAnchor);
    }

    [Fact]
    public void GatewayConnectedPersistentRoutePreservesBendsAndRefreshesEndpoints()
    {
        var gateway = Node("persistent-gateway", BpmnSemanticTypes.ExclusiveGateway);
        var task = Node("persistent-gateway-target", BpmnSemanticTypes.Task);
        var oldSource = new PointD(48d, 24d);
        var firstBend = new PointD(130d, 210d);
        var secondBend = new PointD(240d, 210d);
        var oldTarget = new PointD(320d, 100d);
        var edge = Edge(
            "persistent-gateway-flow",
            gateway,
            task,
            persistentRoute: [oldSource, firstBend, secondBend, oldTarget]);
        var graph = Graph([task, gateway], [edge]);
        var layout = Layout(
            graph,
            (gateway, new RectD(60d, 80d, 48d, 48d)),
            (task, new RectD(360d, 140d, 120d, 80d)));

        var route = Assert.Single(Route(graph, layout).Routes);

        Assert.Equal(new PointD(108d, 104d), route.SourceAnchor);
        Assert.Equal(new PointD(360d, 180d), route.DestinationAnchor);
        AssertSubsequence(route.Path, firstBend, secondBend);
        AssertOrthogonal(route);
        Assert.NotEqual(oldSource, route.SourceAnchor);
        Assert.NotEqual(oldTarget, route.DestinationAnchor);
    }

    [Fact]
    public void MandatoryManualWaypointRemainsExactWhileLegsAvoidObstacle()
    {
        var source = Node("manual-obstacle-source", BpmnSemanticTypes.Task);
        var obstacle = Node("manual-obstacle", BpmnSemanticTypes.Task);
        var target = Node("manual-obstacle-target", BpmnSemanticTypes.Task);
        var waypoint = new PointD(180d, 200d);
        var edge = Edge(
            "manual-obstacle-flow",
            source,
            target,
            persistentRoute:
            [
                new PointD(60d, 120d),
                waypoint,
                new PointD(320d, 120d),
            ]);
        var graph = Graph([obstacle, source, target], [edge]);
        var obstacleBounds = new RectD(150d, 80d, 60d, 80d);
        var layout = Layout(
            graph,
            (source, new RectD(20d, 100d, 40d, 40d)),
            (obstacle, obstacleBounds),
            (target, new RectD(320d, 100d, 40d, 40d)));

        var route = Assert.Single(Route(graph, layout).Routes);

        Assert.Equal(1, route.Path.Count(point => point == waypoint));
        AssertSubsequence(route.Path, waypoint);
        AssertOrthogonal(route);
        AssertObstacleClear(route, Inflate(obstacleBounds, 10d));
    }

    [Fact]
    public void ManualWaypointInsidePreferredClearanceRelaxesClearanceWithoutMutation()
    {
        var source = Node("illegal-manual-source", BpmnSemanticTypes.Task);
        var obstacle = Node("illegal-manual-obstacle", BpmnSemanticTypes.Task);
        var target = Node("illegal-manual-target", BpmnSemanticTypes.Task);
        var illegalWaypoint = new PointD(145d, 120d);
        var persistentRoute = new[]
        {
            new PointD(60d, 120d),
            illegalWaypoint,
            new PointD(320d, 120d),
        };
        var edge = Edge(
            "illegal-manual-flow",
            source,
            target,
            persistentRoute: persistentRoute);
        var graph = Graph([source, obstacle, target], [edge]);
        var obstacleBounds = new RectD(150d, 80d, 60d, 80d);
        var layout = Layout(
            graph,
            (source, new RectD(20d, 100d, 40d, 40d)),
            (obstacle, obstacleBounds),
            (target, new RectD(320d, 100d, 40d, 40d)));
        var route = Assert.Single(Route(graph, layout).Routes);
        var preferredInflatedObstacle = Inflate(obstacleBounds, 10d);

        Assert.True(
            illegalWaypoint.X > preferredInflatedObstacle.Left &&
            illegalWaypoint.X < preferredInflatedObstacle.Right &&
            illegalWaypoint.Y > preferredInflatedObstacle.Top &&
            illegalWaypoint.Y < preferredInflatedObstacle.Bottom,
            "The authored point must make preferred-clearance guidance infeasible.");
        Assert.Contains(illegalWaypoint, route.Path);
        AssertOrthogonal(route);
        AssertObstacleClear(route, obstacleBounds);
        Assert.Equal(persistentRoute, edge.PersistentRoute.AsEnumerable());
    }

    [Fact]
    public void GuidedRouteShortensEndpointLeadBeforeDiscardingAuthoredWaypoint()
    {
        var source = Node("guided-adaptive-lead-source", BpmnSemanticTypes.Task);
        var obstacle = Node("guided-adaptive-lead-obstacle", BpmnSemanticTypes.Task);
        var target = Node("guided-adaptive-lead-target", BpmnSemanticTypes.Task);
        var waypoint = new PointD(240d, 260d);
        var edge = Edge(
            "guided-adaptive-lead-flow",
            source,
            target,
            persistentRoute:
            [
                new PointD(100d, 130d),
                waypoint,
                new PointD(300d, 130d),
            ]);
        var graph = Graph([source, obstacle, target], [edge]);
        var obstacleBounds = new RectD(119d, 139d, 80d, 80d);
        var layout = Layout(
            graph,
            (source, new RectD(20d, 100d, 80d, 60d)),
            (obstacle, obstacleBounds),
            (target, new RectD(300d, 100d, 80d, 60d)));

        var route = Assert.Single(Route(graph, layout).Routes);
        var preferredInflatedObstacle = Inflate(obstacleBounds, 10d);

        Assert.Contains(waypoint, route.Path);
        Assert.Equal(route.SourceAnchor.Y, route.Path[1].Y);
        Assert.True(route.Path[1].X > route.SourceAnchor.X);
        Assert.True(route.Path[1].X < route.SourceAnchor.X + 10d);
        AssertOrthogonal(route);
        AssertObstacleClear(route, preferredInflatedObstacle);
        AssertObstacleClear(route, obstacleBounds);
        Assert.Equal(waypoint, edge.PersistentRoute[1]);
    }

    [Fact]
    public void AnyIllegalWaypointFallsBackWithoutPartiallySalvagingGuidance()
    {
        var source = Node("whole-fallback-source", BpmnSemanticTypes.Task);
        var obstacle = Node("whole-fallback-obstacle", BpmnSemanticTypes.Task);
        var target = Node("whole-fallback-target", BpmnSemanticTypes.Task);
        var firstValidWaypoint = new PointD(100d, 220d);
        var illegalWaypoint = new PointD(180d, 120d);
        var secondValidWaypoint = new PointD(260d, 220d);
        var persistentRoute = new[]
        {
            new PointD(60d, 120d),
            firstValidWaypoint,
            illegalWaypoint,
            secondValidWaypoint,
            new PointD(320d, 120d),
        };
        var edge = Edge("whole-fallback-flow", source, target, persistentRoute);
        var graph = Graph([source, obstacle, target], [edge]);
        var automaticEdge = Edge("whole-fallback-flow", source, target);
        var automaticGraph = Graph([source, obstacle, target], [automaticEdge]);
        var obstacleBounds = new RectD(150d, 80d, 60d, 80d);
        var layout = Layout(
            graph,
            (source, new RectD(20d, 100d, 40d, 40d)),
            (obstacle, obstacleBounds),
            (target, new RectD(320d, 100d, 40d, 40d)));
        var automaticLayout = Layout(
            automaticGraph,
            (source, new RectD(20d, 100d, 40d, 40d)),
            (obstacle, obstacleBounds),
            (target, new RectD(320d, 100d, 40d, 40d)));

        var fallback = Assert.Single(Route(graph, layout).Routes);
        var automatic = Assert.Single(Route(automaticGraph, automaticLayout).Routes);

        Assert.Equal(automatic.Path.AsEnumerable(), fallback.Path.AsEnumerable());
        Assert.DoesNotContain(firstValidWaypoint, fallback.Path);
        Assert.DoesNotContain(illegalWaypoint, fallback.Path);
        Assert.DoesNotContain(secondValidWaypoint, fallback.Path);
        AssertObstacleClear(fallback, Inflate(obstacleBounds, 10d));
        Assert.Equal(persistentRoute, edge.PersistentRoute.AsEnumerable());
    }

    [Theory]
    [InlineData(-1d, 200d)]
    [InlineData(180d, -1d)]
    public void WaypointOutsidePositiveDocumentBoundaryFallsBackToAutomaticRoute(
        double waypointX,
        double waypointY)
    {
        var source = Node("outside-boundary-source", BpmnSemanticTypes.Task);
        var target = Node("outside-boundary-target", BpmnSemanticTypes.Task);
        var outsideWaypoint = new PointD(waypointX, waypointY);
        var persistentRoute = new[]
        {
            new PointD(60d, 120d),
            outsideWaypoint,
            new PointD(320d, 120d),
        };
        var edge = Edge("outside-boundary-flow", source, target, persistentRoute);
        var graph = Graph([source, target], [edge]);
        var automaticEdge = Edge("outside-boundary-flow", source, target);
        var automaticGraph = Graph([source, target], [automaticEdge]);
        var layout = Layout(
            graph,
            (source, new RectD(20d, 100d, 40d, 40d)),
            (target, new RectD(320d, 100d, 40d, 40d)));
        var automaticLayout = Layout(
            automaticGraph,
            (source, new RectD(20d, 100d, 40d, 40d)),
            (target, new RectD(320d, 100d, 40d, 40d)));

        var fallback = Assert.Single(Route(graph, layout).Routes);
        var automatic = Assert.Single(Route(automaticGraph, automaticLayout).Routes);

        Assert.Equal(automatic.Path.AsEnumerable(), fallback.Path.AsEnumerable());
        Assert.DoesNotContain(outsideWaypoint, fallback.Path);
        Assert.All(fallback.Path, AssertPositiveFinite);
        Assert.Equal(persistentRoute, edge.PersistentRoute.AsEnumerable());
    }

    [Fact]
    public void ObstacleClearWaypointInSealedRegionFallsBackToAutomaticRoute()
    {
        var source = Node("sealed-guidance-source", BpmnSemanticTypes.Task);
        var topWall = Node("sealed-guidance-top", BpmnSemanticTypes.Task);
        var rightWall = Node("sealed-guidance-right", BpmnSemanticTypes.Task);
        var bottomWall = Node("sealed-guidance-bottom", BpmnSemanticTypes.Task);
        var leftWall = Node("sealed-guidance-left", BpmnSemanticTypes.Task);
        var target = Node("sealed-guidance-target", BpmnSemanticTypes.Task);
        var sealedWaypoint = new PointD(200d, 320d);
        var persistentRoute = new[]
        {
            new PointD(60d, 120d),
            sealedWaypoint,
            new PointD(320d, 120d),
        };
        var nodes = new[]
        {
            source,
            topWall,
            rightWall,
            bottomWall,
            leftWall,
            target,
        };
        var edge = Edge("sealed-guidance-flow", source, target, persistentRoute);
        var graph = Graph(nodes, [edge]);
        var automaticEdge = Edge("sealed-guidance-flow", source, target);
        var automaticGraph = Graph(nodes, [automaticEdge]);
        var topBounds = new RectD(150d, 250d, 100d, 20d);
        var rightBounds = new RectD(230d, 250d, 20d, 140d);
        var bottomBounds = new RectD(150d, 370d, 100d, 20d);
        var leftBounds = new RectD(150d, 250d, 20d, 140d);
        var placements = new[]
        {
            (source, new RectD(20d, 100d, 40d, 40d)),
            (topWall, topBounds),
            (rightWall, rightBounds),
            (bottomWall, bottomBounds),
            (leftWall, leftBounds),
            (target, new RectD(320d, 100d, 40d, 40d)),
        };
        var inflatedWalls = new[]
        {
            Inflate(topBounds, 10d),
            Inflate(rightBounds, 10d),
            Inflate(bottomBounds, 10d),
            Inflate(leftBounds, 10d),
        };

        var fallback = Assert.Single(Route(graph, Layout(graph, placements)).Routes);
        var repeatedFallback = Assert.Single(
            Route(graph, Layout(graph, placements)).Routes);
        var automatic = Assert.Single(
            Route(automaticGraph, Layout(automaticGraph, placements)).Routes);

        Assert.All(persistentRoute, point =>
        {
            AssertPositiveFinite(point);
            Assert.DoesNotContain(inflatedWalls, wall =>
                point.X > wall.Left &&
                point.X < wall.Right &&
                point.Y > wall.Top &&
                point.Y < wall.Bottom);
        });
        Assert.Equal(automatic.Path.AsEnumerable(), fallback.Path.AsEnumerable());
        Assert.Equal(fallback.Path.AsEnumerable(), repeatedFallback.Path.AsEnumerable());
        Assert.DoesNotContain(sealedWaypoint, fallback.Path);
        Assert.Equal(persistentRoute, edge.PersistentRoute.AsEnumerable());
    }

    [Fact]
    public void GuidanceRestoresDeterministicallyAfterTemporaryFallback()
    {
        var source = Node("restore-guidance-source", BpmnSemanticTypes.Task);
        var obstacle = Node("restore-guidance-obstacle", BpmnSemanticTypes.Task);
        var target = Node("restore-guidance-target", BpmnSemanticTypes.Task);
        var waypoint = new PointD(180d, 200d);
        var persistentRoute = new[]
        {
            new PointD(60d, 120d),
            waypoint,
            new PointD(320d, 120d),
        };
        var edge = Edge("restore-guidance-flow", source, target, persistentRoute);
        var graph = Graph([source, obstacle, target], [edge]);
        var automaticEdge = Edge("restore-guidance-flow", source, target);
        var automaticGraph = Graph([source, obstacle, target], [automaticEdge]);
        var feasibleObstacleBounds = new RectD(150d, 300d, 60d, 80d);
        var blockingObstacleBounds = new RectD(150d, 160d, 60d, 80d);
        var feasibleLayout = Layout(
            graph,
            (source, new RectD(20d, 100d, 40d, 40d)),
            (obstacle, feasibleObstacleBounds),
            (target, new RectD(320d, 100d, 40d, 40d)));
        var blockingLayout = Layout(
            graph,
            (source, new RectD(20d, 100d, 40d, 40d)),
            (obstacle, blockingObstacleBounds),
            (target, new RectD(320d, 100d, 40d, 40d)));
        var automaticBlockingLayout = Layout(
            automaticGraph,
            (source, new RectD(20d, 100d, 40d, 40d)),
            (obstacle, blockingObstacleBounds),
            (target, new RectD(320d, 100d, 40d, 40d)));

        var firstGuided = Assert.Single(Route(graph, feasibleLayout).Routes);
        var firstFallback = Assert.Single(Route(graph, blockingLayout).Routes);
        var secondFallback = Assert.Single(Route(graph, blockingLayout).Routes);
        var restoredGuided = Assert.Single(Route(graph, feasibleLayout).Routes);
        var automatic = Assert.Single(
            Route(automaticGraph, automaticBlockingLayout).Routes);

        Assert.Equal(firstGuided.Path.AsEnumerable(), restoredGuided.Path.AsEnumerable());
        Assert.Equal(firstFallback.Path.AsEnumerable(), secondFallback.Path.AsEnumerable());
        Assert.Equal(automatic.Path.AsEnumerable(), firstFallback.Path.AsEnumerable());
        Assert.Contains(waypoint, firstGuided.Path);
        Assert.DoesNotContain(waypoint, firstFallback.Path);
        AssertObstacleClear(firstFallback, Inflate(blockingObstacleBounds, 10d));
        Assert.Equal(persistentRoute, edge.PersistentRoute.AsEnumerable());
    }

    [Fact]
    public void ChangedLayoutBoundsRerouteAutomaticConnector()
    {
        var task = Node("task", BpmnSemanticTypes.Task);
        var end = Node("end", BpmnSemanticTypes.EndEvent);
        var edge = Edge("task-end", task, end);
        var graph = Graph([task, end], [edge]);
        var firstLayout = Layout(
            graph,
            (task, new RectD(0d, 0d, 40d, 40d)),
            (end, new RectD(100d, 0d, 60d, 40d)));
        var changedLayout = Layout(
            graph,
            (task, new RectD(20d, 30d, 80d, 60d)),
            (end, new RectD(300d, 100d, 100d, 100d)));

        var first = Assert.Single(Route(graph, firstLayout).Routes);
        var changed = Assert.Single(Route(graph, changedLayout).Routes);

        Assert.Equal(new PointD(40d, 20d), first.SourceAnchor);
        Assert.Equal(new PointD(100d, 20d), first.DestinationAnchor);
        Assert.Equal(new PointD(100d, 60d), changed.SourceAnchor);
        Assert.Equal(new PointD(300d, 150d), changed.DestinationAnchor);
        Assert.NotEqual(first, changed);
        Assert.Empty(graph.Edges.Single().PersistentRoute);
    }

    [Fact]
    public void AutomaticBoundaryFacingTargetAtDocumentOriginProducesDeterministicNoRoute()
    {
        var start = Node("start", BpmnSemanticTypes.StartEvent);
        var task = Node("task", BpmnSemanticTypes.Task);
        var edge = Edge("start-task", start, task);
        var graph = Graph([start, task], [edge]);
        var layout = Layout(
            graph,
            (start, new RectD(300d, 20d, 80d, 40d)),
            (task, new RectD(0d, 20d, 100d, 40d)));

        var first = Engine().Route(graph, layout, BpmnAlgorithmIds.DefaultRouting);
        var second = Engine().Route(graph, layout, BpmnAlgorithmIds.DefaultRouting);

        Assert.Equal(first, second);
        AssertNoRoute(first, edge);
        AssertNoRoute(second, edge);
        Assert.Empty(edge.PersistentRoute);
    }

    [Fact]
    public void GuidedAndAutomaticAttemptsProduceNoRouteForImpossibleBoundaryEndpoint()
    {
        var start = Node("guided-failure-start", BpmnSemanticTypes.StartEvent);
        var task = Node("guided-failure-task", BpmnSemanticTypes.Task);
        var persistentRoute = new[]
        {
            new PointD(380d, 40d),
            new PointD(200d, 100d),
            new PointD(0d, 40d),
        };
        var edge = Edge(
            "guided-failure-flow",
            start,
            task,
            persistentRoute: persistentRoute);
        var graph = Graph([start, task], [edge]);
        var layout = Layout(
            graph,
            (start, new RectD(300d, 20d, 80d, 40d)),
            (task, new RectD(0d, 20d, 100d, 40d)));

        var first = Engine().Route(graph, layout, BpmnAlgorithmIds.DefaultRouting);
        var second = Engine().Route(graph, layout, BpmnAlgorithmIds.DefaultRouting);

        Assert.Equal(first, second);
        AssertNoRoute(first, edge);
        AssertNoRoute(second, edge);
        Assert.Equal(persistentRoute, edge.PersistentRoute.AsEnumerable());
    }

    [Fact]
    public void TargetAnchorInsideUnrelatedActualBodyProducesDeterministicNoRoute()
    {
        var source = Node("source-task", BpmnSemanticTypes.Task);
        var target = Node("target-task", BpmnSemanticTypes.Task);
        var sourcePort = Port(
            "actual-body-source",
            source,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source);
        var targetPort = Port(
            "actual-body-target",
            target,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRoleCapability.Target);
        var edge = Edge(
            "task-task",
            source,
            target,
            sourcePortId: sourcePort.Id,
            targetPortId: targetPort.Id);
        var graph = Graph([source, target], [edge], [sourcePort, targetPort]);
        var sourceBounds = new RectD(212d, 121d, 180d, 92d);
        var targetBounds = new RectD(382d, 98d, 48d, 48d);
        var targetAnchor = new PointD(382d, 122d);
        var layout = Layout(
            graph,
            (source, sourceBounds),
            (target, targetBounds));

        var first = Engine().Route(graph, layout, BpmnAlgorithmIds.DefaultRouting);
        var second = Engine().Route(graph, layout, BpmnAlgorithmIds.DefaultRouting);

        Assert.Equal(first, second);
        Assert.True(
            targetAnchor.X > sourceBounds.Left &&
            targetAnchor.X < sourceBounds.Right &&
            targetAnchor.Y > sourceBounds.Top &&
            targetAnchor.Y < sourceBounds.Bottom,
            "The target attachment must be strictly inside the unrelated source body.");
        AssertNoRoute(first, edge, "inside an unrelated node body");
        AssertNoRoute(second, edge, "inside an unrelated node body");
    }

    [Fact]
    public void IdenticalInputsProduceEquivalentResults()
    {
        var start = Node("start", BpmnSemanticTypes.StartEvent);
        var task = Node("task", BpmnSemanticTypes.Task);
        var edge = Edge("start-task", start, task);
        var graph = Graph([task, start], [edge]);
        var layout = Layout(
            graph,
            (task, new RectD(140d, 70d, 120d, 80d)),
            (start, new RectD(0d, 0d, 36d, 36d)));
        var engine = Engine();

        var first = engine.Route(graph, layout, BpmnAlgorithmIds.DefaultRouting);
        var second = engine.Route(graph, layout, BpmnAlgorithmIds.DefaultRouting);
        var third = engine.Route(graph, layout, BpmnAlgorithmIds.DefaultRouting);

        Assert.True(first.IsSuccessful);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void UnsupportedProjectedEdgeFailsWithPluginDiagnostic()
    {
        var start = Node("start", BpmnSemanticTypes.StartEvent);
        var task = Node("task", BpmnSemanticTypes.Task);
        var unsupportedType = new SemanticTypeId("test:bpmn:unsupported-edge");
        var edge = Edge("unsupported", start, task, typeId: unsupportedType);
        var graph = Graph([start, task], [edge]);
        var layout = Layout(
            graph,
            (start, new RectD(0d, 0d, 36d, 36d)),
            (task, new RectD(100d, 0d, 120d, 80d)));

        var execution = Engine().Route(graph, layout, BpmnAlgorithmIds.DefaultRouting);

        Assert.Equal(RoutingExecutionStatus.Failed, execution.Status);
        Assert.Null(execution.Result);
        Assert.Contains(execution.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(execution.Diagnostics, diagnostic =>
            diagnostic.Code == RoutingDiagnosticCodes.AlgorithmFailure);
    }

    [Fact]
    public void RoutingEngineReportsCancellationWithoutPartialResult()
    {
        var task = Node("task", BpmnSemanticTypes.Task);
        var end = Node("end", BpmnSemanticTypes.EndEvent);
        var edge = Edge("task-end", task, end);
        var graph = Graph([task, end], [edge]);
        var layout = Layout(
            graph,
            (task, new RectD(0d, 0d, 100d, 60d)),
            (end, new RectD(160d, 12d, 36d, 36d)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var execution = Engine().Route(
            graph,
            layout,
            BpmnAlgorithmIds.DefaultRouting,
            cancellationToken: cancellation.Token);

        Assert.Equal(RoutingExecutionStatus.Cancelled, execution.Status);
        Assert.Null(execution.Result);
        Assert.Equal(RoutingDiagnosticCodes.Cancelled, Assert.Single(execution.Diagnostics).Code);
    }

    private static void AssertEndpointOwnerExcludedAfterLead(
        RoutedConnectorGeometry route,
        RectD inflatedOwner)
    {
        for (var index = 1; index < route.Path.Length - 1; index++)
        {
            Assert.False(
                SegmentCrossesStrictInterior(
                    route.Path[index],
                    route.Path[index + 1],
                    inflatedOwner),
                $"Post-lead segment {index} entered endpoint owner '{inflatedOwner}'.");
        }
    }

    private static void AssertEndpointOwnerExcludedBeforeLead(
        RoutedConnectorGeometry route,
        RectD inflatedOwner)
    {
        for (var index = 0; index < route.Path.Length - 2; index++)
        {
            Assert.False(
                SegmentCrossesStrictInterior(
                    route.Path[index],
                    route.Path[index + 1],
                    inflatedOwner),
                $"Pre-lead segment {index} entered endpoint owner '{inflatedOwner}'.");
        }
    }

    private static void AssertSelfLoopOwnerExcludedBetweenLeads(
        RoutedConnectorGeometry route,
        RectD inflatedOwner)
    {
        for (var index = 1; index < route.Path.Length - 2; index++)
        {
            Assert.False(
                SegmentCrossesStrictInterior(
                    route.Path[index],
                    route.Path[index + 1],
                    inflatedOwner),
                $"Self-loop segment {index} entered owner '{inflatedOwner}'.");
        }
    }

    private static PointD AnchorPoint(RectD bounds, ConnectorAnchorSide side) =>
        side switch
        {
            ConnectorAnchorSide.Top => new PointD(bounds.X + (bounds.Width / 2d), bounds.Top),
            ConnectorAnchorSide.Right => new PointD(bounds.Right, bounds.Y + (bounds.Height / 2d)),
            ConnectorAnchorSide.Bottom => new PointD(
                bounds.X + (bounds.Width / 2d),
                bounds.Bottom),
            ConnectorAnchorSide.Left => new PointD(bounds.Left, bounds.Y + (bounds.Height / 2d)),
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
        };

    private static PointD LeadPoint(PointD anchor, ConnectorAnchorSide side) =>
        side switch
        {
            ConnectorAnchorSide.Top => new PointD(anchor.X, anchor.Y - 10d),
            ConnectorAnchorSide.Right => new PointD(anchor.X + 10d, anchor.Y),
            ConnectorAnchorSide.Bottom => new PointD(anchor.X, anchor.Y + 10d),
            ConnectorAnchorSide.Left => new PointD(anchor.X - 10d, anchor.Y),
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
        };

    private static void AssertOrthogonal(RoutedConnectorGeometry route)
    {
        for (var index = 0; index < route.Path.Length - 1; index++)
        {
            var start = route.Path[index];
            var end = route.Path[index + 1];
            Assert.True(
                start.X == end.X || start.Y == end.Y,
                $"Segment '{start}' to '{end}' was not orthogonal.");
        }
    }

    private static void AssertObstacleClear(
        RoutedConnectorGeometry route,
        RectD obstacle)
    {
        for (var index = 0; index < route.Path.Length - 1; index++)
        {
            var start = route.Path[index];
            var end = route.Path[index + 1];
            Assert.False(
                SegmentCrossesStrictInterior(start, end, obstacle),
                $"Segment '{start}' to '{end}' crossed obstacle '{obstacle}'.");
        }
    }

    private static bool SegmentCrossesStrictInterior(
        PointD start,
        PointD end,
        RectD obstacle)
    {
        if (start.X == end.X)
        {
            return start.X > obstacle.Left &&
                start.X < obstacle.Right &&
                Math.Max(Math.Min(start.Y, end.Y), obstacle.Top) <
                Math.Min(Math.Max(start.Y, end.Y), obstacle.Bottom);
        }

        if (start.Y == end.Y)
        {
            return start.Y > obstacle.Top &&
                start.Y < obstacle.Bottom &&
                Math.Max(Math.Min(start.X, end.X), obstacle.Left) <
                Math.Min(Math.Max(start.X, end.X), obstacle.Right);
        }

        return true;
    }

    private static void AssertSubsequence(
        IReadOnlyList<PointD> path,
        params PointD[] expected)
    {
        var expectedIndex = 0;
        foreach (var point in path)
        {
            if (expectedIndex < expected.Length && point == expected[expectedIndex])
            {
                expectedIndex++;
            }
        }

        Assert.Equal(expected.Length, expectedIndex);
    }

    private static void AssertExitsSide(
        PointD anchor,
        PointD next,
        ConnectorAnchorSide side)
    {
        switch (side)
        {
            case ConnectorAnchorSide.Top:
                Assert.Equal(anchor.X, next.X);
                Assert.True(next.Y < anchor.Y);
                break;
            case ConnectorAnchorSide.Right:
                Assert.Equal(anchor.Y, next.Y);
                Assert.True(next.X > anchor.X);
                break;
            case ConnectorAnchorSide.Bottom:
                Assert.Equal(anchor.X, next.X);
                Assert.True(next.Y > anchor.Y);
                break;
            case ConnectorAnchorSide.Left:
                Assert.Equal(anchor.Y, next.Y);
                Assert.True(next.X < anchor.X);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side), side, null);
        }
    }

    private static void AssertApproachesSide(
        PointD previous,
        PointD anchor,
        ConnectorAnchorSide side)
    {
        switch (side)
        {
            case ConnectorAnchorSide.Top:
                Assert.Equal(anchor.X, previous.X);
                Assert.True(previous.Y < anchor.Y);
                break;
            case ConnectorAnchorSide.Right:
                Assert.Equal(anchor.Y, previous.Y);
                Assert.True(previous.X > anchor.X);
                break;
            case ConnectorAnchorSide.Bottom:
                Assert.Equal(anchor.X, previous.X);
                Assert.True(previous.Y > anchor.Y);
                break;
            case ConnectorAnchorSide.Left:
                Assert.Equal(anchor.Y, previous.Y);
                Assert.True(previous.X < anchor.X);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side), side, null);
        }
    }

    private static int CountDirectionChanges(IReadOnlyList<PointD> path)
    {
        bool? horizontal = null;
        var changes = 0;
        for (var index = 0; index < path.Count - 1; index++)
        {
            var currentHorizontal = path[index].Y == path[index + 1].Y;
            if (horizontal.HasValue && horizontal.Value != currentHorizontal)
            {
                changes++;
            }

            horizontal = currentHorizontal;
        }

        return changes;
    }

    private static RectD Inflate(RectD bounds, double clearance) =>
        new(
            bounds.X - clearance,
            bounds.Y - clearance,
            bounds.Width + (2d * clearance),
            bounds.Height + (2d * clearance));

    private static void AssertPositiveFinite(PointD point)
    {
        Assert.True(double.IsFinite(point.X));
        Assert.True(double.IsFinite(point.Y));
        Assert.True(point.X >= 0d);
        Assert.True(point.Y >= 0d);
    }

    private static RoutingEngine Engine() =>
        new(
        [
            new RoutingAlgorithmRegistration(
                BpmnAlgorithmIds.DefaultRouting,
                new BpmnRoutingAlgorithm()),
        ]);

    private static RoutingResult Route(ProjectedGraph graph, LayoutResult layout)
    {
        var execution = Engine().Route(graph, layout, BpmnAlgorithmIds.DefaultRouting);
        Assert.True(
            execution.IsSuccessful,
            string.Join(
                Environment.NewLine,
                execution.Diagnostics.Select(static diagnostic =>
                    $"{diagnostic.Code}: {diagnostic.Message}")));
        return Assert.IsType<RoutingResult>(execution.Result);
    }

    private static RoutingResult AssertNoRoute(
        RoutingExecutionResult execution,
        ProjectedEdge edge,
        string? expectedMessageFragment = null)
    {
        Assert.Equal(RoutingExecutionStatus.Succeeded, execution.Status);
        var result = Assert.IsType<RoutingResult>(execution.Result);
        Assert.Empty(result.Routes);
        Assert.Equal([edge.Id], result.NoRouteEdgeIds.AsEnumerable());
        var diagnostic = Assert.Single(execution.Diagnostics);
        Assert.Equal(BpmnAlgorithmDiagnosticCodes.NoLegalRoute, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(edge.Id.Value, diagnostic.SourceIdentity);
        Assert.Equal(result.Diagnostics.AsEnumerable(), execution.Diagnostics.AsEnumerable());
        Assert.Equal("NoLegalObstacleSafeRoute", diagnostic.Context["ReasonCategory"]);
        if (expectedMessageFragment is not null)
        {
            Assert.Contains(
                expectedMessageFragment,
                diagnostic.Message,
                StringComparison.Ordinal);
        }

        return result;
    }

    private static void AssertRoute(
        RoutingResult routing,
        ProjectedEdge edge,
        PointD expectedSource,
        PointD expectedTarget)
    {
        var route = routing.Routes.Single(candidate => candidate.ProjectedEdgeId == edge.Id);
        Assert.Equal(expectedSource, route.SourceAnchor);
        Assert.Equal(expectedTarget, route.DestinationAnchor);
        Assert.Equal(expectedSource, route.Path[0]);
        Assert.Equal(expectedTarget, route.Path[^1]);
    }

    private static ProjectedGraph Graph(
        IEnumerable<ProjectedNode> nodes,
        IEnumerable<ProjectedEdge> edges,
        IEnumerable<ProjectedPort>? ports = null) =>
        new(DocumentId, Revision, nodes, edges, ports: ports);

    private static ProjectedNode Node(string key, SemanticTypeId typeId) =>
        new(
            Trace(
                NodeRuleId,
                ProjectionSourceKind.SemanticElement,
                new SemanticElementId($"bpmn:m2:node:{key}"),
                typeId,
                "node"),
            projectedProperties: SemanticTypeMetadata(typeId));

    private static ProjectedEdge Edge(
        string key,
        ProjectedNode source,
        ProjectedNode target,
        IEnumerable<PointD>? persistentRoute = null,
        ProjectedObjectId? sourcePortId = null,
        ProjectedObjectId? targetPortId = null,
        SemanticTypeId? typeId = null)
    {
        var semanticTypeId = typeId ?? BpmnSemanticTypes.SequenceFlow;
        return new ProjectedEdge(
            Trace(
                EdgeRuleId,
                ProjectionSourceKind.SemanticRelationship,
                new SemanticElementId($"bpmn:m2:edge:{key}"),
                semanticTypeId,
                "edge"),
            source.Id,
            target.Id,
            sourcePortId,
            targetPortId,
            persistentRoute,
            projectedProperties: SemanticTypeMetadata(semanticTypeId));
    }

    private static ProjectedPort Port(
        string key,
        ProjectedNode owner,
        ConnectorAnchorSide side,
        ConnectorAnchorRoleCapability roleCapability)
    {
        var projectedAnchor = new ProjectedConnectorAnchor(
            new ConnectorAnchorId($"bpmn:m2:anchor:{key}"),
            side,
            roleCapability,
            order: 0,
            sideCount: 1,
            ResolvedConnectorAnchorKind.Dynamic);
        return new ProjectedPort(
            Trace(
                NodeRuleId,
                ProjectionSourceKind.SemanticElement,
                owner.Source.SemanticElementId,
                owner.Source.SemanticTypeId,
                $"connector-anchor:{key}"),
            owner.Id,
            routingHints: ProjectedConnectorAnchorMetadata.Encode(projectedAnchor));
    }

    private static ProjectionSourceTrace Trace(
        ProjectionRuleId ruleId,
        ProjectionSourceKind sourceKind,
        SemanticElementId semanticId,
        SemanticTypeId typeId,
        string localKey) =>
        new(DocumentId, ruleId, sourceKind, semanticId, typeId, localKey);

    private static IEnumerable<KeyValuePair<string, PropertyValue>> SemanticTypeMetadata(
        SemanticTypeId typeId) =>
        [new(ProjectedSemanticTypeProperty, PropertyValue.FromText(typeId.Value))];

    private static LayoutResult Layout(
        ProjectedGraph graph,
        params (ProjectedNode Node, RectD Bounds)[] placements) =>
        new(
            graph.DocumentId,
            graph.SourceRevision,
            LayoutAlgorithmId,
            new LayoutComputation(placements.Select(static placement =>
                new LayoutNodeGeometry(
                    placement.Node.Id,
                    placement.Bounds,
                    Matrix2D.CreateTranslation(
                        placement.Bounds.X,
                        placement.Bounds.Y)))));
}
