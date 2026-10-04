using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>Optional current-state assessment and selected repair on an existing routing algorithm.</summary>
public interface IStableConnectorRoutingPolicy
{
    ConnectorRoutingAssessment Assess(
        ProjectedGraph graph, LayoutResult logicalLayout, RoutingContext context,
        ProjectedObjectId edgeId, ConnectorRoutingRecord? candidate,
        CancellationToken cancellationToken = default);

    ConnectorRoutingWorkResult RouteAffected(
        ProjectedGraph graph, LayoutResult logicalLayout, RoutingContext context,
        ImmutableArray<ProjectedObjectId> orderedEdgeIds,
        CancellationToken cancellationToken = default);
}

public sealed class ConnectorRoutingAssessment
{
    public ConnectorRoutingAssessment(
        ConnectorRoutingEndpointObservation source,
        ConnectorRoutingEndpointObservation target,
        ConnectorAutomaticRouteProof? acceptedProof,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        Source = source;
        Target = target;
        AcceptedProof = acceptedProof;
        Diagnostics = RoutingDiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
    }

    public ConnectorRoutingEndpointObservation Source { get; }
    public ConnectorRoutingEndpointObservation Target { get; }
    public ConnectorAutomaticRouteProof? AcceptedProof { get; }
    public bool IsValidAutomaticPath => AcceptedProof?.ObstacleClearance is not null;
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

public sealed class ConnectorRoutingWorkResult
{
    public ConnectorRoutingWorkResult(
        IEnumerable<ConnectorRoutingRecord> records,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        Records = RoutingStateCollection.Unique(records, static record => record.VisualStateId.Value,
            nameof(records), order: false);
        Diagnostics = RoutingDiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
    }

    public ImmutableArray<ConnectorRoutingRecord> Records { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}
