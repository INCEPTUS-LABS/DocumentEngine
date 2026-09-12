using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Describes how a committed edit that suppresses automatic node layout affects persistent
/// node geometry. Changed IDs replace explicit geometry, insert proven new Pinned geometry,
/// or restore exact historical geometry;
/// removed IDs shrink the compatible layout while preserving every surviving node.
/// </summary>
public sealed class NodeGeometryPipelineImpact : IEquatable<NodeGeometryPipelineImpact>
{
    private NodeGeometryPipelineImpact(
        IEnumerable<VisualStateId> changedVisualStateIds,
        IEnumerable<VisualStateId> removedVisualStateIds,
        DocumentRevision? historicalSourceRevision)
    {
        ChangedVisualStateIds = CopyAndOrder(
            changedVisualStateIds,
            "Changed node geometry IDs cannot contain null values.",
            nameof(changedVisualStateIds));
        RemovedVisualStateIds = CopyAndOrder(
            removedVisualStateIds,
            "Removed node geometry IDs cannot contain null values.",
            nameof(removedVisualStateIds));
        if (ChangedVisualStateIds.Any(RemovedVisualStateIds.Contains))
        {
            throw new ArgumentException(
                "Changed and removed node geometry IDs must be disjoint.",
                nameof(removedVisualStateIds));
        }

        if (historicalSourceRevision is not null &&
            (ChangedVisualStateIds.IsEmpty || !RemovedVisualStateIds.IsEmpty))
        {
            throw new ArgumentException(
                "Historical node geometry restoration requires changed IDs and cannot remove nodes.",
                nameof(historicalSourceRevision));
        }

        HistoricalSourceRevision = historicalSourceRevision;
    }

    private static ImmutableArray<VisualStateId> CopyAndOrder(
        IEnumerable<VisualStateId> visualStateIds,
        string nullMessage,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(visualStateIds);
        var ordered = visualStateIds
            .Select(id => id ?? throw new ArgumentException(
                nullMessage,
                parameterName))
            .OrderBy(static id => id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1] == ordered[index])
            {
                throw new ArgumentException(
                    $"Duplicate node geometry ID '{ordered[index]}'.",
                    parameterName);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Gets the impact used by edits that preserve every compatible node geometry.
    /// </summary>
    public static NodeGeometryPipelineImpact PreserveAll { get; } = new([], [], null);

    public ImmutableArray<VisualStateId> ChangedVisualStateIds { get; }

    public ImmutableArray<VisualStateId> RemovedVisualStateIds { get; }

    public DocumentRevision? HistoricalSourceRevision { get; }

    public bool HasExplicitChanges => !ChangedVisualStateIds.IsEmpty;

    public bool HasRemovedVisualStates => !RemovedVisualStateIds.IsEmpty;

    public bool RequiresExactCarryForward =>
        HasRemovedVisualStates || HistoricalSourceRevision is not null;

    public static NodeGeometryPipelineImpact ForChangedVisualStates(
        IEnumerable<VisualStateId> changedVisualStateIds)
    {
        var impact = new NodeGeometryPipelineImpact(changedVisualStateIds, [], null);
        if (!impact.HasExplicitChanges)
        {
            throw new ArgumentException(
                "An explicit node geometry impact must identify at least one changed Visual State.",
                nameof(changedVisualStateIds));
        }

        return impact;
    }

    public static NodeGeometryPipelineImpact ForRemovedVisualStates(
        IEnumerable<VisualStateId> removedVisualStateIds)
    {
        var impact = new NodeGeometryPipelineImpact([], removedVisualStateIds, null);
        if (!impact.HasRemovedVisualStates)
        {
            throw new ArgumentException(
                "A removed node geometry impact must identify at least one Visual State.",
                nameof(removedVisualStateIds));
        }

        return impact;
    }

    public static NodeGeometryPipelineImpact ForHistoricalRestoration(
        IEnumerable<VisualStateId> restoredVisualStateIds,
        DocumentRevision historicalSourceRevision)
    {
        var impact = new NodeGeometryPipelineImpact(
            restoredVisualStateIds,
            [],
            historicalSourceRevision);
        if (!impact.HasExplicitChanges)
        {
            throw new ArgumentException(
                "Historical node geometry restoration must identify at least one Visual State.",
                nameof(restoredVisualStateIds));
        }

        return impact;
    }

    public bool Equals(NodeGeometryPipelineImpact? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        ChangedVisualStateIds.AsSpan().SequenceEqual(other.ChangedVisualStateIds.AsSpan()) &&
        RemovedVisualStateIds.AsSpan().SequenceEqual(other.RemovedVisualStateIds.AsSpan()) &&
        HistoricalSourceRevision == other.HistoricalSourceRevision;

    public override bool Equals(object? obj) => Equals(obj as NodeGeometryPipelineImpact);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var id in ChangedVisualStateIds)
        {
            hash.Add(id);
        }

        foreach (var id in RemovedVisualStateIds)
        {
            hash.Add(id);
        }

        hash.Add(HistoricalSourceRevision);

        return hash.ToHashCode();
    }

    public static bool operator ==(
        NodeGeometryPipelineImpact? left,
        NodeGeometryPipelineImpact? right) =>
        EqualityComparer<NodeGeometryPipelineImpact>.Default.Equals(left, right);

    public static bool operator !=(
        NodeGeometryPipelineImpact? left,
        NodeGeometryPipelineImpact? right) =>
        !(left == right);
}
