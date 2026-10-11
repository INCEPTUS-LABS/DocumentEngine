using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>
/// Saved connector paths in a document presentation owned by an actual semantic element.
/// Node geometry remains in its existing Visual State or process-scope authority.
/// </summary>
public sealed class DocumentRoutingSnapshot : IEquatable<DocumentRoutingSnapshot>
{
    public DocumentRoutingSnapshot(SemanticElementId ownerId, IEnumerable<ConnectorRoutingRecord> connectors)
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        OwnerId = ownerId;
        Connectors = RoutingStateCollection.Unique(connectors,
            static record => record.VisualStateId.Value, nameof(connectors), order: false);
    }

    public SemanticElementId OwnerId { get; }
    public ImmutableArray<ConnectorRoutingRecord> Connectors { get; }

    public bool Equals(DocumentRoutingSnapshot? other) =>
        ReferenceEquals(this, other) || other is not null && OwnerId == other.OwnerId &&
        Connectors.AsSpan().SequenceEqual(other.Connectors.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as DocumentRoutingSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(OwnerId);
        foreach (var record in Connectors) hash.Add(record);
        return hash.ToHashCode();
    }
}
