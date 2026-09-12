using Inceptus.DocumentEngine.Bpmn.Projection;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Routing;

/// <summary>
/// Provides deterministic polyline routing for the supported BPMN Sequence Flow slice.
/// </summary>
public sealed class BpmnRoutingAlgorithm : IRoutingAlgorithm
{
    public RoutingAlgorithmResult Route(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSequenceFlow(edge))
            {
                return Failure(
                    BpmnAlgorithmDiagnosticCodes.UnsupportedRoutingContent,
                    $"Projected edge '{edge.Id}' is not a BPMN Sequence Flow.",
                    edge.Id.Value);
            }
        }

        var geometries = layout.Nodes.ToDictionary(static node => node.ProjectedObjectId);
        var ports = graph.Ports.ToDictionary(static port => port.Id);
        var obstacles = layout.Nodes
            .OrderBy(static node => node.ProjectedObjectId.Value, StringComparer.Ordinal)
            .Select(static node => new BpmnRoutingObstacle(
                node.ProjectedObjectId,
                node.Bounds))
            .ToArray();
        var routes = new List<RoutedConnectorGeometry>(graph.Edges.Length);
        var noRouteEdgeIds = new List<ProjectedObjectId>();
        var diagnostics = new List<Diagnostic>();
        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!geometries.TryGetValue(edge.SourceNodeId, out var sourceGeometry) ||
                !geometries.TryGetValue(edge.TargetNodeId, out var targetGeometry))
            {
                return Failure(
                    BpmnAlgorithmDiagnosticCodes.InvalidRoutingInput,
                    $"BPMN Sequence Flow '{edge.Id}' has no complete layout geometry.",
                    edge.Id.Value);
            }

            if (!TryResolveEndpoint(
                    edge.SourcePortId,
                    sourceGeometry.Bounds,
                    ConnectorAnchorRole.Source,
                    ports,
                    out var sourceAnchor) ||
                !TryResolveEndpoint(
                    edge.TargetPortId,
                    targetGeometry.Bounds,
                    ConnectorAnchorRole.Target,
                    ports,
                    out var targetAnchor))
            {
                return Failure(
                    BpmnAlgorithmDiagnosticCodes.InvalidRoutingInput,
                    $"BPMN Sequence Flow '{edge.Id}' has invalid projected connector-anchor metadata.",
                    edge.Id.Value);
            }

            var sourceEndpoint = new BpmnRoutingEndpoint(
                edge.SourceNodeId,
                sourceAnchor.Point,
                sourceAnchor.Side);
            var targetEndpoint = new BpmnRoutingEndpoint(
                edge.TargetNodeId,
                targetAnchor.Point,
                targetAnchor.Side);
            var mandatoryWaypoints = PersistentWaypoints(edge, cancellationToken);
            var outcome = BpmnOrthogonalRouter.TryRoute(
                sourceEndpoint,
                targetEndpoint,
                obstacles,
                mandatoryWaypoints,
                allowPolicyRelaxation: true,
                cancellationToken,
                out var path,
                out var failureReason);
            if (outcome == BpmnRoutingOutcome.NoRoute && mandatoryWaypoints.Length > 0)
            {
                // Guidance remains persistent and editable, but an infeasible set cannot make an
                // otherwise routable diagram unavailable. Retry presentation with the whole set
                // omitted; never partially salvage, move, or delete authored points here.
                cancellationToken.ThrowIfCancellationRequested();
                outcome = BpmnOrthogonalRouter.TryRoute(
                    sourceEndpoint,
                    targetEndpoint,
                    obstacles,
                    [],
                    allowPolicyRelaxation: true,
                    cancellationToken,
                    out path,
                    out failureReason);
            }

            if (outcome == BpmnRoutingOutcome.InvalidOutput)
            {
                return Failure(
                    BpmnAlgorithmDiagnosticCodes.InvalidRoutingOutput,
                    $"BPMN Sequence Flow '{edge.Id}' produced invalid routing geometry. " +
                    failureReason,
                    edge.Id.Value);
            }

            if (outcome == BpmnRoutingOutcome.NoRoute)
            {
                noRouteEdgeIds.Add(edge.Id);
                diagnostics.Add(NoRouteDiagnostic(
                    edge,
                    sourceAnchor,
                    targetAnchor,
                    failureReason));
                continue;
            }

            routes.Add(new RoutedConnectorGeometry(
                edge.Id,
                sourceAnchor.Point,
                targetAnchor.Point,
                path.AsSpan(1, path.Length - 2).ToArray(),
                edge.SourcePortId,
                edge.TargetPortId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return RoutingAlgorithmResult.Success(
            new RoutingComputation(routes, noRouteEdgeIds: noRouteEdgeIds),
            diagnostics);
    }

    private static PointD[] PersistentWaypoints(
        ProjectedEdge edge,
        CancellationToken cancellationToken)
    {
        if (edge.PersistentRoute.Length < 2)
        {
            return [];
        }

        var bends = new PointD[edge.PersistentRoute.Length - 2];
        for (var index = 0; index < bends.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bends[index] = edge.PersistentRoute[index + 1];
        }

        return bends;
    }

    private static bool TryResolveEndpoint(
        ProjectedObjectId? portId,
        RectD bounds,
        ConnectorAnchorRole role,
        Dictionary<ProjectedObjectId, ProjectedPort> ports,
        out ResolvedRoutingEndpoint endpoint)
    {
        if (portId is null)
        {
            var side = role == ConnectorAnchorRole.Source
                ? ConnectorAnchorSide.Right
                : ConnectorAnchorSide.Left;
            endpoint = new ResolvedRoutingEndpoint(
                side,
                role == ConnectorAnchorRole.Source
                    ? new PointD(bounds.Right, bounds.Y + (bounds.Height / 2d))
                    : new PointD(bounds.Left, bounds.Y + (bounds.Height / 2d)));
            return true;
        }

        if (!ports.TryGetValue(portId, out var port) ||
            !ProjectedConnectorAnchorMetadata.TryDecode(port, out var anchor) ||
            anchor is null ||
            !anchor.Allows(role))
        {
            endpoint = default;
            return false;
        }

        endpoint = new ResolvedRoutingEndpoint(
            anchor.Side,
            ConnectorAnchorGeometryResolver.ResolvePoint(
                bounds,
                anchor.Side,
                anchor.Order,
                anchor.SideCount));
        return true;
    }

    private static bool IsSequenceFlow(ProjectedEdge edge) =>
        edge.ProjectedProperties.TryGetValue(
            BpmnProjectionIdentities.ProjectedSemanticTypeProperty,
            out var projectedType) &&
        projectedType.Kind == PropertyValueKind.Text &&
        string.Equals(
            projectedType.TextValue,
            edge.Source.SemanticTypeId.Value,
            StringComparison.Ordinal) &&
        edge.Source.SemanticTypeId == BpmnSemanticTypes.SequenceFlow;

    private static RoutingAlgorithmResult Failure(
        string code,
        string message,
        string sourceIdentity) =>
        RoutingAlgorithmResult.Failure(
        [
            new Diagnostic(code, DiagnosticSeverity.Error, message, sourceIdentity),
        ]);

    private static Diagnostic NoRouteDiagnostic(
        ProjectedEdge edge,
        ResolvedRoutingEndpoint sourceAnchor,
        ResolvedRoutingEndpoint targetAnchor,
        string failureReason) =>
        new(
            BpmnAlgorithmDiagnosticCodes.NoLegalRoute,
            DiagnosticSeverity.Warning,
            $"No legal obstacle-safe route could be found for BPMN Sequence Flow " +
            $"'{edge.Source.SemanticElementId}' from {sourceAnchor.Side} anchor " +
            $"'{edge.SourcePortId?.Value ?? "implicit"}' at {sourceAnchor.Point} to " +
            $"{targetAnchor.Side} anchor '{edge.TargetPortId?.Value ?? "implicit"}' at " +
            $"{targetAnchor.Point}. {failureReason}",
            edge.Id.Value,
            [
                new KeyValuePair<string, string>(
                    "ReasonCategory",
                    "NoLegalObstacleSafeRoute"),
                new KeyValuePair<string, string>(
                    "SourceAnchor",
                    edge.SourcePortId?.Value ?? "implicit"),
                new KeyValuePair<string, string>(
                    "TargetAnchor",
                    edge.TargetPortId?.Value ?? "implicit"),
            ]);

    private readonly record struct ResolvedRoutingEndpoint(
        ConnectorAnchorSide Side,
        PointD Point);
}
