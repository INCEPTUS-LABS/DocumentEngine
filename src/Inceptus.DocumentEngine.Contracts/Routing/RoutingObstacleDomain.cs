using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>
/// An immutable set of projected node owners. Geometry is taken exclusively from the
/// current LayoutResult. Equal sets share preprocessing within one routing operation.
/// </summary>
public sealed class RoutingObstacleDomain : IEquatable<RoutingObstacleDomain>
{
    private readonly int _hashCode;
    private readonly ImmutableHashSet<ProjectedObjectId> _nodeIds;

    public RoutingObstacleDomain(IEnumerable<ProjectedObjectId> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        var copy = nodeIds.ToArray();
        if (Array.Exists(copy, static id => id is null))
        {
            throw new ArgumentException("Obstacle identities cannot be null.", nameof(nodeIds));
        }

        NodeIds = [.. copy.Distinct().OrderBy(static id => id.Value, StringComparer.Ordinal)];
        _nodeIds = NodeIds.ToImmutableHashSet();
        var hash = new HashCode();
        foreach (var id in NodeIds)
        {
            hash.Add(id);
        }
        _hashCode = hash.ToHashCode();
    }

    public ImmutableArray<ProjectedObjectId> NodeIds { get; }

    public bool Contains(ProjectedObjectId nodeId) => _nodeIds.Contains(nodeId);

    public bool Equals(RoutingObstacleDomain? other) =>
        ReferenceEquals(this, other) ||
        other is not null && NodeIds.SequenceEqual(other.NodeIds);

    public override bool Equals(object? obj) => Equals(obj as RoutingObstacleDomain);

    public override int GetHashCode() => _hashCode;
}
