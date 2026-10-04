using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>One ordered, immutable routing request; it grants no direct path-installation authority.</summary>
public sealed class ConnectorRoutingIntent : IEquatable<ConnectorRoutingIntent>
{
    private ConnectorRoutingIntent(ConnectorRoutingIntentKind kind, VisualStateId visualStateId,
        ConnectorRoutingType? routingType, IEnumerable<PointD>? points,
        PointD? sourcePoint = null, PointD? targetPoint = null)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        if (routingType is { } type && !Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(routingType));
        }
        Kind = kind;
        VisualStateId = visualStateId;
        RoutingType = routingType;
        ManualDefinition = points?.ToImmutableArray() ?? [];
        if (sourcePoint.HasValue != targetPoint.HasValue ||
            (sourcePoint is { } source && (!double.IsFinite(source.X) || !double.IsFinite(source.Y))) ||
            (targetPoint is { } target && (!double.IsFinite(target.X) || !double.IsFinite(target.Y))))
            throw new ArgumentException("Complete route endpoint inputs must both be present and finite.", nameof(sourcePoint));
        SourcePoint = sourcePoint;
        TargetPoint = targetPoint;
        if (ManualDefinition.Any(static point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
        {
            throw new ArgumentException("Manual points must be finite.", nameof(points));
        }
    }

    public ConnectorRoutingIntentKind Kind { get; }
    public VisualStateId VisualStateId { get; }
    public ConnectorRoutingType? RoutingType { get; }
    public ImmutableArray<PointD> ManualDefinition { get; }
    public PointD? SourcePoint { get; }
    public PointD? TargetPoint { get; }
    public static ConnectorRoutingIntent Initialize(VisualStateId id, ConnectorRoutingType type) =>
        new(ConnectorRoutingIntentKind.Initialize, id, type, null);
    public static ConnectorRoutingIntent SetType(VisualStateId id, ConnectorRoutingType type) =>
        new(ConnectorRoutingIntentKind.SetType, id, type, null);
    public static ConnectorRoutingIntent ReplaceManualDefinition(VisualStateId id, IEnumerable<PointD> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        return new(ConnectorRoutingIntentKind.ReplaceManualDefinition, id, null, points);
    }
    public static ConnectorRoutingIntent Recalculate(VisualStateId id) =>
        new(ConnectorRoutingIntentKind.Recalculate, id, null, null);
    public static ConnectorRoutingIntent ReplaceManualDefinition(VisualStateId id,
        IEnumerable<PointD> points, PointD sourcePoint, PointD targetPoint)
    {
        ArgumentNullException.ThrowIfNull(points);
        return new(ConnectorRoutingIntentKind.ReplaceManualDefinition, id, null, points, sourcePoint, targetPoint);
    }
    public bool Equals(ConnectorRoutingIntent? other) => ReferenceEquals(this, other) || other is not null &&
        Kind == other.Kind && VisualStateId == other.VisualStateId && RoutingType == other.RoutingType &&
        SourcePoint == other.SourcePoint && TargetPoint == other.TargetPoint &&
        ManualDefinition.AsSpan().SequenceEqual(other.ManualDefinition.AsSpan());
    public override bool Equals(object? obj) => Equals(obj as ConnectorRoutingIntent);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind); hash.Add(VisualStateId); hash.Add(RoutingType);
        hash.Add(SourcePoint); hash.Add(TargetPoint);
        foreach (var point in ManualDefinition) { hash.Add(point); }
        return hash.ToHashCode();
    }
}

public enum ConnectorRoutingIntentKind
{
    Initialize,
    SetType,
    ReplaceManualDefinition,
    Recalculate,
}
