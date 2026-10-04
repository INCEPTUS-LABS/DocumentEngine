using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Routing;

public sealed partial class BpmnRoutingAlgorithm
{
    private const string StablePolicyVersion = "1";

    public ConnectorRoutingAssessment Assess(ProjectedGraph graph, LayoutResult logicalLayout, RoutingContext context,
        ProjectedObjectId edgeId, ConnectorRoutingRecord? candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(logicalLayout);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(edgeId);
        cancellationToken.ThrowIfCancellationRequested();
        var edge = RequireStableEdge(graph, edgeId);
        // Assessment is a single-edge operation: resolve only its two endpoint owners.
        // The immutable inputs need no retained per-document index or second cache.
        var nodes = graph.Nodes.Where(node => node.Id == edge.SourceNodeId || node.Id == edge.TargetNodeId)
            .ToDictionary(static node => node.Id);
        var geometries = logicalLayout.Nodes.Where(node => node.ProjectedObjectId == edge.SourceNodeId || node.ProjectedObjectId == edge.TargetNodeId)
            .ToDictionary(static node => node.ProjectedObjectId);
        var ports = graph.Ports.Where(port => port.Id == edge.SourcePortId || port.Id == edge.TargetPortId)
            .ToDictionary(static port => port.Id);
        var source = ResolveStableEndpoint(edge.SourceNodeId, edge.SourcePortId, ConnectorAnchorRole.Source, nodes, geometries, ports);
        var target = ResolveStableEndpoint(edge.TargetNodeId, edge.TargetPortId, ConnectorAnchorRole.Target, nodes, geometries, ports);
        var unresolved = new ConnectorAutomaticRouteProof(BpmnAlgorithmIds.DefaultRouting, StablePolicyVersion, null, null, source, target);
        if (candidate?.Outcome == ConnectorRoutingOutcome.NoRoute && candidate.VisualStateId == edge.Source.VisualStateId)
            return new ConnectorRoutingAssessment(source, target, unresolved,
                [NoRouteDiagnostic(edge, new ResolvedRoutingEndpoint(source.Side, source.Point),
                    new ResolvedRoutingEndpoint(target.Side, target.Point), "No feasible path is recorded for the current saved state.")]);
        if (candidate is null || candidate.VisualStateId != edge.Source.VisualStateId || candidate.Outcome != ConnectorRoutingOutcome.Path)
            return new ConnectorRoutingAssessment(source, target, unresolved);

        var domain = new BpmnOrthogonalRouter.OperationContext(
            AssessmentObstacles(logicalLayout, context, edge, candidate.Path, cancellationToken));
        var sourceEndpoint = new BpmnRoutingEndpoint(edge.SourceNodeId, source.Point, source.Side);
        var targetEndpoint = new BpmnRoutingEndpoint(edge.TargetNodeId, target.Point, target.Side);
        if (candidate.RoutingType == ConnectorRoutingType.Automatic)
        {
            if (candidate.AutomaticProof is not { } proof ||
                proof.AlgorithmId != BpmnAlgorithmIds.DefaultRouting || proof.PolicyVersion != StablePolicyVersion)
                return new ConnectorRoutingAssessment(source, target, unresolved);
            if (proof is { ObstacleClearance: { } clearance, EndpointLead: { } lead } &&
                BpmnOrthogonalRouter.IsValidSavedPath(candidate.Path, sourceEndpoint, targetEndpoint, domain, clearance, lead, cancellationToken))
                return Accepted(clearance, lead);
        }

        // A changed obstacle may invalidate the old attempt while the exact path remains
        // valid under another supported attempt. Mode entry uses the same no-search proof.
        foreach (var clearance in BpmnRoutingPolicy.ObstacleClearanceAttempts)
            foreach (var lead in BpmnRoutingPolicy.EndpointLeadDistanceAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (BpmnOrthogonalRouter.IsValidSavedPath(candidate.Path, sourceEndpoint, targetEndpoint, domain, clearance, lead, cancellationToken))
                    return Accepted(clearance, lead);
            }
        return new ConnectorRoutingAssessment(source, target, unresolved);

        ConnectorRoutingAssessment Accepted(double clearance, double lead) => new(source, target,
            new ConnectorAutomaticRouteProof(BpmnAlgorithmIds.DefaultRouting, StablePolicyVersion, clearance, lead, source, target));
    }

    private static List<BpmnRoutingObstacle> AssessmentObstacles(LayoutResult layout, RoutingContext context,
        ProjectedEdge edge, ImmutableArray<PointD> completePath, CancellationToken cancellationToken)
    {
        var minimumX = completePath.Min(static point => point.X);
        var maximumX = completePath.Max(static point => point.X);
        var minimumY = completePath.Min(static point => point.Y);
        var maximumY = completePath.Max(static point => point.Y);
        var maximumInflation = 0d;
        foreach (var clearance in BpmnRoutingPolicy.ObstacleClearanceAttempts)
            maximumInflation = Math.Max(maximumInflation, clearance);
        foreach (var lead in BpmnRoutingPolicy.EndpointLeadDistanceAttempts)
            maximumInflation = Math.Max(maximumInflation, lead);
        var domain = context.PreparedInput?.EdgeDomains[edge.Id];
        var obstacles = new List<BpmnRoutingObstacle>();
        foreach (var node in layout.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (domain is not null && !domain.Contains(node.ProjectedObjectId)) continue;
            var bounds = node.Bounds;
            var endpointOwner = node.ProjectedObjectId == edge.SourceNodeId || node.ProjectedObjectId == edge.TargetNodeId;
            // Every segment lies in the complete saved path bounds. Only bodies whose
            // maximum policy inflation can reach those bounds can invalidate this path.
            if (endpointOwner || bounds.Left - maximumInflation <= maximumX && bounds.Right + maximumInflation >= minimumX &&
                bounds.Top - maximumInflation <= maximumY && bounds.Bottom + maximumInflation >= minimumY)
                obstacles.Add(new BpmnRoutingObstacle(node.ProjectedObjectId, bounds));
        }
        return obstacles;
    }

    public ConnectorRoutingWorkResult RouteAffected(ProjectedGraph graph, LayoutResult logicalLayout, RoutingContext context,
        ImmutableArray<ProjectedObjectId> orderedEdgeIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(logicalLayout);
        ArgumentNullException.ThrowIfNull(context);
        if (orderedEdgeIds.IsDefault) throw new ArgumentException("The ordered work batch must be initialized.", nameof(orderedEdgeIds));
        if (orderedEdgeIds.Distinct().Count() != orderedEdgeIds.Length)
            throw new ArgumentException("The ordered work batch must not contain duplicate edges.", nameof(orderedEdgeIds));
        var nodes = graph.Nodes.ToDictionary(static node => node.Id);
        var geometries = logicalLayout.Nodes.ToDictionary(static node => node.ProjectedObjectId);
        var ports = graph.Ports.ToDictionary(static port => port.Id);
        var operation = new BpmnRoutingOperation(logicalLayout, context.PreparedInput);
        var records = new List<ConnectorRoutingRecord>(orderedEdgeIds.Length);
        var diagnostics = new List<Diagnostic>();
        foreach (var edgeId in orderedEdgeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var edge = RequireStableEdge(graph, edgeId);
            var source = ResolveStableEndpoint(edge.SourceNodeId, edge.SourcePortId, ConnectorAnchorRole.Source, nodes, geometries, ports);
            var target = ResolveStableEndpoint(edge.TargetNodeId, edge.TargetPortId, ConnectorAnchorRole.Target, nodes, geometries, ports);
            var outcome = BpmnOrthogonalRouter.TryRoute(
                new BpmnRoutingEndpoint(edge.SourceNodeId, source.Point, source.Side),
                new BpmnRoutingEndpoint(edge.TargetNodeId, target.Point, target.Side),
                operation.GetDomain(edgeId), [], allowPolicyRelaxation: true, cancellationToken,
                out var path, out var failureReason, out var clearance, out var lead);
            if (outcome == BpmnRoutingOutcome.InvalidOutput)
            {
                diagnostics.Add(new Diagnostic(BpmnAlgorithmDiagnosticCodes.InvalidRoutingOutput, DiagnosticSeverity.Error,
                    $"BPMN Sequence Flow '{edgeId}' produced invalid routing geometry. {failureReason}", edgeId.Value));
                return new ConnectorRoutingWorkResult([], diagnostics);
            }
            var proof = new ConnectorAutomaticRouteProof(BpmnAlgorithmIds.DefaultRouting, StablePolicyVersion, clearance, lead, source, target);
            records.Add(new ConnectorRoutingRecord(edge.Source.VisualStateId!, ConnectorRoutingType.Automatic,
                outcome == BpmnRoutingOutcome.Routed ? ConnectorRoutingOutcome.Path : ConnectorRoutingOutcome.NoRoute,
                path, automaticProof: proof, noRouteReason: outcome == BpmnRoutingOutcome.NoRoute ? ConnectorNoRouteReason.NoFeasiblePath : null));
            if (outcome == BpmnRoutingOutcome.NoRoute)
                diagnostics.Add(NoRouteDiagnostic(edge, new ResolvedRoutingEndpoint(source.Side, source.Point),
                    new ResolvedRoutingEndpoint(target.Side, target.Point), failureReason));
        }
        return new ConnectorRoutingWorkResult(records, diagnostics);
    }

    private static ProjectedEdge RequireStableEdge(ProjectedGraph graph, ProjectedObjectId edgeId)
    {
        var edge = graph.Edges.FirstOrDefault(candidate => candidate.Id == edgeId);
        if (edge is null || !IsSequenceFlow(edge) || edge.Source.VisualStateId is null)
            throw new ArgumentException("Stable BPMN routing requires a Sequence Flow with persistent visual authority.", nameof(edgeId));
        return edge;
    }

    private static ConnectorRoutingEndpointObservation ResolveStableEndpoint(ProjectedObjectId nodeId, ProjectedObjectId? portId,
        ConnectorAnchorRole role, Dictionary<ProjectedObjectId, ProjectedNode> nodes,
        Dictionary<ProjectedObjectId, LayoutNodeGeometry> geometries, Dictionary<ProjectedObjectId, ProjectedPort> ports)
    {
        if (!nodes.TryGetValue(nodeId, out var node) || node.Source.VisualStateId is null ||
            !geometries.TryGetValue(nodeId, out var geometry) ||
            !TryResolveEndpoint(portId, geometry.Bounds, role, ports, out var endpoint))
            throw new ArgumentException("Stable BPMN routing requires complete node and endpoint authority.", nameof(nodeId));
        ProjectedConnectorAnchor? anchor = null;
        if (portId is not null) ProjectedConnectorAnchorMetadata.TryDecode(ports[portId], out anchor);
        var outward = endpoint.Side switch
        {
            ConnectorAnchorSide.Top => new VectorD(0, -1),
            ConnectorAnchorSide.Right => new VectorD(1, 0),
            ConnectorAnchorSide.Bottom => new VectorD(0, 1),
            ConnectorAnchorSide.Left => new VectorD(-1, 0),
            _ => throw new InvalidOperationException("Invalid endpoint side."),
        };
        var direction = role == ConnectorAnchorRole.Source ? outward : new VectorD(-outward.X, -outward.Y);
        return new ConnectorRoutingEndpointObservation(node.Source.SemanticElementId, node.Source.VisualStateId,
            anchor?.Id, role, endpoint.Side, anchor?.Order ?? 0, anchor?.SideCount ?? 1, endpoint.Point, direction);
    }
}
