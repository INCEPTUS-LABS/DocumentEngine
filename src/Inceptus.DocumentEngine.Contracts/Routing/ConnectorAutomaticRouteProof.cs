using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>Observations of authoritative endpoint bindings, never replacement bindings.</summary>
public sealed record ConnectorRoutingEndpointObservation
{
    public ConnectorRoutingEndpointObservation(
        SemanticElementId semanticElementId,
        VisualStateId visualStateId,
        ConnectorAnchorId? anchorId,
        ConnectorAnchorRole role,
        ConnectorAnchorSide side,
        int order,
        int sideCount,
        PointD point,
        VectorD direction)
    {
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentNullException.ThrowIfNull(visualStateId);
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        if (!Enum.IsDefined(side)) throw new ArgumentOutOfRangeException(nameof(side));
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sideCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(order, sideCount);
        if (Math.Abs(direction.X) + Math.Abs(direction.Y) != 1d ||
            direction.X != 0d && direction.Y != 0d)
        {
            throw new ArgumentException("An endpoint direction must be an axis unit vector.", nameof(direction));
        }
        SemanticElementId = semanticElementId;
        VisualStateId = visualStateId;
        AnchorId = anchorId;
        Role = role;
        Side = side;
        Order = order;
        SideCount = sideCount;
        Point = point;
        Direction = direction;
    }

    public SemanticElementId SemanticElementId { get; }
    public VisualStateId VisualStateId { get; }
    public ConnectorAnchorId? AnchorId { get; }
    public ConnectorAnchorRole Role { get; }
    public ConnectorAnchorSide Side { get; }
    public int Order { get; }
    public int SideCount { get; }
    public PointD Point { get; }
    public VectorD Direction { get; }
}

/// <summary>The actual accepted Automatic policy attempt and observed endpoint authority.</summary>
public sealed record ConnectorAutomaticRouteProof
{
    public ConnectorAutomaticRouteProof(
        AlgorithmId algorithmId,
        string policyVersion,
        double? obstacleClearance,
        double? endpointLead,
        ConnectorRoutingEndpointObservation source,
        ConnectorRoutingEndpointObservation target)
    {
        ArgumentNullException.ThrowIfNull(algorithmId);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (obstacleClearance.HasValue != endpointLead.HasValue)
            throw new ArgumentException("Clearance and lead must both be present or absent.");
        if (obstacleClearance is { } clearance && (!double.IsFinite(clearance) || clearance < 0d))
            throw new ArgumentOutOfRangeException(nameof(obstacleClearance));
        if (endpointLead is { } lead && (!double.IsFinite(lead) || lead <= 0d))
            throw new ArgumentOutOfRangeException(nameof(endpointLead));
        if (source.Role != ConnectorAnchorRole.Source || target.Role != ConnectorAnchorRole.Target)
            throw new ArgumentException("Endpoint observations must identify their respective roles.");
        AlgorithmId = algorithmId;
        PolicyVersion = policyVersion;
        ObstacleClearance = obstacleClearance;
        EndpointLead = endpointLead;
        Source = source;
        Target = target;
    }

    public AlgorithmId AlgorithmId { get; }
    public string PolicyVersion { get; }
    public double? ObstacleClearance { get; }
    public double? EndpointLead { get; }
    public ConnectorRoutingEndpointObservation Source { get; }
    public ConnectorRoutingEndpointObservation Target { get; }
}
