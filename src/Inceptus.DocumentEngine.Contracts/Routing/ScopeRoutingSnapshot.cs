using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>Saved state for one semantic scope; connector array order is its sole priority sequence.</summary>
public sealed class ScopeRoutingSnapshot : IEquatable<ScopeRoutingSnapshot>
{
    public ScopeRoutingSnapshot(
        DocumentScopeId scopeId,
        ScopeGeometrySnapshot geometry,
        IEnumerable<ConnectorRoutingRecord> connectors)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        ArgumentNullException.ThrowIfNull(geometry);
        ScopeId = scopeId;
        Geometry = geometry;
        Connectors = RoutingStateCollection.Unique(connectors,
            static record => record.VisualStateId.Value, nameof(connectors), order: false);
    }

    public DocumentScopeId ScopeId { get; }
    public ScopeGeometrySnapshot Geometry { get; }
    public ImmutableArray<ConnectorRoutingRecord> Connectors { get; }

    public bool Equals(ScopeRoutingSnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null && ScopeId == other.ScopeId && Geometry.Equals(other.Geometry) &&
        Connectors.AsSpan().SequenceEqual(other.Connectors.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as ScopeRoutingSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ScopeId);
        hash.Add(Geometry);
        foreach (var record in Connectors) hash.Add(record);
        return hash.ToHashCode();
    }
}
