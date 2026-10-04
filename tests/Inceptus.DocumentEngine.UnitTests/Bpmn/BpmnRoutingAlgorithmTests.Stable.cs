using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed partial class BpmnRoutingAlgorithmTests
{
    [Fact]
    public void StableWorkPreservesRequestedOrderAndUsesTheCompleteRoutersPathAndActualProof()
    {
        var source = StableNode("stable-source");
        var target = StableNode("stable-target");
        var first = StableEdge("a", source, target);
        var last = StableEdge("z", source, target);
        var graph = Graph([source, target], [first, last]);
        var layout = Layout(graph, (source, new RectD(20, 20, 120, 80)), (target, new RectD(400, 140, 120, 80)));
        var algorithm = new BpmnRoutingAlgorithm();

        var selected = algorithm.RouteAffected(graph, layout, RoutingContext.Empty, [last.Id, first.Id]);
        var complete = algorithm.Route(graph, layout, RoutingContext.Empty, CancellationToken.None).Computation!;

        Assert.Empty(selected.Diagnostics);
        Assert.Equal([last.Source.VisualStateId, first.Source.VisualStateId], selected.Records.Select(static record => record.VisualStateId));
        for (var i = 0; i < selected.Records.Length; i++)
        {
            var edge = i == 0 ? last : first;
            var record = selected.Records[i];
            Assert.Equal(complete.Routes.Single(route => route.ProjectedEdgeId == edge.Id).Path.AsEnumerable(), record.Path.AsEnumerable());
            Assert.Equal(10d, record.AutomaticProof!.ObstacleClearance);
            Assert.Equal(10d, record.AutomaticProof.EndpointLead);
            Assert.True(algorithm.Assess(graph, layout, RoutingContext.Empty, edge.Id, record).IsValidAutomaticPath);
        }
    }

    [Fact]
    public void StableAssessmentChecksTheWholePathAgainstCurrentBodiesAndEndpointAuthority()
    {
        var source = StableNode("assess-source");
        var target = StableNode("assess-target");
        var obstacle = StableNode("assess-obstacle");
        var edge = StableEdge("assess", source, target);
        var graph = Graph([source, target, obstacle], [edge]);
        var sourceBounds = new RectD(20, 20, 120, 80);
        var targetBounds = new RectD(500, 20, 120, 80);
        var clear = Layout(graph, (source, sourceBounds), (target, targetBounds), (obstacle, new RectD(280, 220, 80, 60)));
        var algorithm = new BpmnRoutingAlgorithm();
        var record = Assert.Single(algorithm.RouteAffected(graph, clear, RoutingContext.Empty, [edge.Id]).Records);
        Assert.True(algorithm.Assess(graph, clear, RoutingContext.Empty, edge.Id, record).IsValidAutomaticPath);

        var obstructed = Layout(graph, (source, sourceBounds), (target, targetBounds), (obstacle, new RectD(280, 30, 80, 60)));
        Assert.False(algorithm.Assess(graph, obstructed, RoutingContext.Empty, edge.Id, record).IsValidAutomaticPath);
        var moved = Layout(graph, (source, new RectD(20, 30, 120, 80)), (target, targetBounds), (obstacle, new RectD(280, 220, 80, 60)));
        Assert.False(algorithm.Assess(graph, moved, RoutingContext.Empty, edge.Id, record).IsValidAutomaticPath);
        Assert.Equal(new PointD(140, 60), record.Path[0]);
    }

    [Fact]
    public void StableAssessmentRecertifiesTheExactSavedPathUnderASupportedRelaxedClearance()
    {
        var source = StableNode("recertify-source");
        var target = StableNode("recertify-target");
        var obstacle = StableNode("recertify-obstacle");
        var edge = StableEdge("recertify", source, target);
        var graph = Graph([source, target, obstacle], [edge]);
        var sourceBounds = new RectD(20, 20, 120, 80);
        var targetBounds = new RectD(500, 20, 120, 80);
        var clear = Layout(graph, (source, sourceBounds), (target, targetBounds), (obstacle, new RectD(280, 220, 80, 60)));
        var algorithm = new BpmnRoutingAlgorithm();
        var record = Assert.Single(algorithm.RouteAffected(graph, clear, RoutingContext.Empty, [edge.Id]).Records);
        var points = record.Path;
        var nearPath = Layout(graph, (source, sourceBounds), (target, targetBounds), (obstacle, new RectD(280, 67, 80, 60)));

        var assessment = algorithm.Assess(graph, nearPath, RoutingContext.Empty, edge.Id, record);

        Assert.True(assessment.IsValidAutomaticPath);
        Assert.Equal(5d, assessment.AcceptedProof!.ObstacleClearance);
        Assert.Equal(10d, record.AutomaticProof!.ObstacleClearance);
        Assert.Equal(points, record.Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StableAssessmentCertifiesExplicitOrthogonalPathsWithoutNormalizingRedundantPoints(bool leadingDuplicate)
    {
        var source = StableNode("explicit-source");
        var target = StableNode("explicit-target");
        var edge = StableEdge("explicit", source, target);
        var graph = Graph([source, target], [edge]);
        var layout = Layout(graph, (source, new RectD(20, 20, 120, 80)), (target, new RectD(500, 20, 120, 80)));
        PointD[] path = leadingDuplicate
            ? [new(140, 60), new(140, 60), new(200, 60), new(200, 60), new(500, 60), new(500, 60)]
            : [new(140, 60), new(145, 60), new(200, 60), new(200, 60), new(500, 60)];
        var candidate = new ConnectorRoutingRecord(edge.Source.VisualStateId!, ConnectorRoutingType.Manual,
            ConnectorRoutingOutcome.Path, path, path[1..^1]);

        var assessment = new BpmnRoutingAlgorithm().Assess(graph, layout, RoutingContext.Empty, edge.Id, candidate);

        Assert.True(assessment.IsValidAutomaticPath);
        Assert.Equal(path, candidate.Path.AsEnumerable());
        Assert.Equal(10d, assessment.AcceptedProof!.EndpointLead);
    }

    [Fact]
    public void StableAssessmentRejectsDiagonalExplicitGeometryAndUnknownSavedPolicy()
    {
        var source = StableNode("diagonal-source");
        var target = StableNode("diagonal-target");
        var edge = StableEdge("diagonal", source, target);
        var graph = Graph([source, target], [edge]);
        var layout = Layout(graph, (source, new RectD(20, 20, 120, 80)), (target, new RectD(500, 20, 120, 80)));
        var algorithm = new BpmnRoutingAlgorithm();
        var manual = new ConnectorRoutingRecord(edge.Source.VisualStateId!, ConnectorRoutingType.Manual,
            ConnectorRoutingOutcome.Path, [new(140, 60), new(240, 100), new(500, 60)], [new(240, 100)]);
        Assert.False(algorithm.Assess(graph, layout, RoutingContext.Empty, edge.Id, manual).IsValidAutomaticPath);
        var automatic = Assert.Single(algorithm.RouteAffected(graph, layout, RoutingContext.Empty, [edge.Id]).Records);
        var proof = automatic.AutomaticProof!;
        var unknown = new ConnectorRoutingRecord(automatic.VisualStateId, automatic.RoutingType, automatic.Outcome, automatic.Path,
            automaticProof: new ConnectorAutomaticRouteProof(proof.AlgorithmId, "unsupported", proof.ObstacleClearance, proof.EndpointLead, proof.Source, proof.Target));
        Assert.False(algorithm.Assess(graph, layout, RoutingContext.Empty, edge.Id, unknown).IsValidAutomaticPath);
    }

    [Fact]
    public void StableNoRouteCarriesCurrentUnresolvedProofAndRemainsAssessableWithoutSearch()
    {
        var source = StableNode("blocked-source");
        var target = StableNode("blocked-target");
        var edge = StableEdge("blocked", source, target);
        var graph = Graph([source, target], [edge]);
        var layout = Layout(graph, (source, new RectD(20, 20, 120, 80)), (target, new RectD(140, 20, 120, 80)));
        var algorithm = new BpmnRoutingAlgorithm();
        var result = algorithm.RouteAffected(graph, layout, RoutingContext.Empty, [edge.Id]);
        var record = Assert.Single(result.Records);
        Assert.Equal(ConnectorRoutingOutcome.NoRoute, record.Outcome);
        Assert.Equal(ConnectorNoRouteReason.NoFeasiblePath, record.NoRouteReason);
        Assert.Empty(record.Path);
        Assert.Null(record.AutomaticProof!.ObstacleClearance);
        Assert.Null(record.AutomaticProof.EndpointLead);
        var assessment = algorithm.Assess(graph, layout, RoutingContext.Empty, edge.Id, record);
        Assert.False(assessment.IsValidAutomaticPath);
        Assert.Equal(record.AutomaticProof, assessment.AcceptedProof);
        Assert.Equal(BpmnAlgorithmDiagnosticCodes.NoLegalRoute, Assert.Single(assessment.Diagnostics).Code);
    }

    [Fact]
    public void StablePolicyObservesRealAnchorIdentitySideOrderAndApproachDirection()
    {
        var source = StableNode("anchor-source");
        var target = StableNode("anchor-target");
        var sourcePort = Port("stable-source", source, ConnectorAnchorSide.Bottom, ConnectorAnchorRoleCapability.Source);
        var targetPort = Port("stable-target", target, ConnectorAnchorSide.Top, ConnectorAnchorRoleCapability.Target);
        var edge = StableEdge("anchors", source, target, sourcePort.Id, targetPort.Id);
        var graph = Graph([source, target], [edge], [sourcePort, targetPort]);
        var layout = Layout(graph, (source, new RectD(20, 20, 120, 80)), (target, new RectD(20, 200, 120, 80)));
        var record = Assert.Single(new BpmnRoutingAlgorithm().RouteAffected(graph, layout, RoutingContext.Empty, [edge.Id]).Records);
        Assert.Equal(new ConnectorAnchorId("bpmn:m2:anchor:stable-source"), record.AutomaticProof!.Source.AnchorId);
        Assert.Equal(new ConnectorAnchorId("bpmn:m2:anchor:stable-target"), record.AutomaticProof.Target.AnchorId);
        Assert.Equal(new VectorD(0, 1), record.AutomaticProof.Source.Direction);
        Assert.Equal(new VectorD(0, 1), record.AutomaticProof.Target.Direction);
    }

    private static ProjectedNode StableNode(string key)
    {
        var node = Node(key, BpmnSemanticTypes.Task);
        var trace = node.Source;
        return new ProjectedNode(new ProjectionSourceTrace(trace.DocumentId, trace.RuleId, trace.SourceKind,
            trace.SemanticElementId, trace.SemanticTypeId, trace.LocalKey, new VisualStateId("visual:" + key)),
            projectedProperties: node.ProjectedProperties);
    }

    private static ProjectedEdge StableEdge(string key, ProjectedNode source, ProjectedNode target,
        ProjectedObjectId? sourcePortId = null, ProjectedObjectId? targetPortId = null)
    {
        var edge = Edge(key, source, target, sourcePortId: sourcePortId, targetPortId: targetPortId);
        var trace = edge.Source;
        return new ProjectedEdge(new ProjectionSourceTrace(trace.DocumentId, trace.RuleId, trace.SourceKind,
            trace.SemanticElementId, trace.SemanticTypeId, trace.LocalKey, new VisualStateId("flow-visual:" + key)),
            source.Id, target.Id, sourcePortId, targetPortId, projectedProperties: edge.ProjectedProperties);
    }
}
