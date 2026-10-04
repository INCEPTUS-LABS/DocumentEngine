using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>One saved expanded scope-logical path. Array order in its scope supplies priority.</summary>
public sealed class ConnectorRoutingRecord : IEquatable<ConnectorRoutingRecord>
{
    public ConnectorRoutingRecord(
        VisualStateId visualStateId,
        ConnectorRoutingType routingType,
        ConnectorRoutingOutcome outcome,
        IEnumerable<PointD> path,
        IEnumerable<PointD>? manualDefinition = null,
        ConnectorAutomaticRouteProof? automaticProof = null,
        ConnectorNoRouteReason? noRouteReason = null)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        ArgumentNullException.ThrowIfNull(path);
        if (!Enum.IsDefined(routingType)) throw new ArgumentOutOfRangeException(nameof(routingType));
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if (noRouteReason is { } reason && !Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(noRouteReason));
        VisualStateId = visualStateId;
        RoutingType = routingType;
        Outcome = outcome;
        Path = [.. path];
        ManualDefinition = manualDefinition is null ? null : [.. manualDefinition];
        AutomaticProof = automaticProof;
        NoRouteReason = noRouteReason;

        if (outcome == ConnectorRoutingOutcome.NoRoute)
        {
            if (routingType != ConnectorRoutingType.Automatic || !Path.IsEmpty ||
                noRouteReason is null || automaticProof is null ||
                automaticProof.ObstacleClearance is not null)
                throw new ArgumentException("NoRoute requires Automatic, an empty path, a reason and unresolved proof.");
        }
        else
        {
            if (Path.Length < 2 || noRouteReason is not null)
                throw new ArgumentException("A successful path requires both endpoints and no NoRoute reason.", nameof(path));
            if (routingType == ConnectorRoutingType.Straight && Path.Length != 2)
                throw new ArgumentException("Straight requires exactly two endpoints.", nameof(path));
            if (routingType == ConnectorRoutingType.Automatic &&
                (automaticProof is null || automaticProof.ObstacleClearance is null))
                throw new ArgumentException("Automatic paths require their accepted policy attempt.", nameof(automaticProof));
            if (routingType == ConnectorRoutingType.Manual &&
                (ManualDefinition is not { } definition ||
                 !Path.AsSpan(1, Path.Length - 2).SequenceEqual(definition.AsSpan())))
                throw new ArgumentException("Manual path interiors must match the authored definition.", nameof(manualDefinition));
        }

        if (routingType != ConnectorRoutingType.Automatic && automaticProof is not null)
            throw new ArgumentException("Only Automatic records have automatic proof.", nameof(automaticProof));
        if (outcome == ConnectorRoutingOutcome.Path && automaticProof is not null &&
            (Path[0] != automaticProof.Source.Point || Path[^1] != automaticProof.Target.Point))
            throw new ArgumentException("Path endpoints must match the proof observations.", nameof(path));
    }

    public VisualStateId VisualStateId { get; }
    public ConnectorRoutingType RoutingType { get; }
    public ConnectorRoutingOutcome Outcome { get; }
    public ImmutableArray<PointD> Path { get; }
    public ImmutableArray<PointD>? ManualDefinition { get; }
    public ConnectorAutomaticRouteProof? AutomaticProof { get; }
    public ConnectorNoRouteReason? NoRouteReason { get; }

    public bool Equals(ConnectorRoutingRecord? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        VisualStateId == other.VisualStateId && RoutingType == other.RoutingType &&
        Outcome == other.Outcome && Path.AsSpan().SequenceEqual(other.Path.AsSpan()) &&
        ManualDefinition.HasValue == other.ManualDefinition.HasValue &&
        (!ManualDefinition.HasValue ||
         ManualDefinition.Value.AsSpan().SequenceEqual(other.ManualDefinition!.Value.AsSpan())) &&
        Equals(AutomaticProof, other.AutomaticProof) && NoRouteReason == other.NoRouteReason;

    public override bool Equals(object? obj) => Equals(obj as ConnectorRoutingRecord);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(VisualStateId);
        hash.Add(RoutingType);
        hash.Add(Outcome);
        foreach (var point in Path) hash.Add(point);
        hash.Add(ManualDefinition.HasValue);
        if (ManualDefinition is { } definition)
            foreach (var point in definition) hash.Add(point);
        hash.Add(AutomaticProof);
        hash.Add(NoRouteReason);
        return hash.ToHashCode();
    }
}
