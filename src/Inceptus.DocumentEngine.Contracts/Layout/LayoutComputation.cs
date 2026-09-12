using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Layout;

/// <summary>
/// Immutable geometry and metadata computed by one Layout Algorithm.
/// </summary>
public sealed class LayoutComputation : IEquatable<LayoutComputation>
{
    public LayoutComputation(
        IEnumerable<LayoutNodeGeometry>? nodes = null,
        IEnumerable<LayoutGroupGeometry>? groups = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? metadata = null)
    {
        Nodes = CopyAndOrder(
            nodes,
            static geometry => geometry.ProjectedObjectId.Value,
            nameof(nodes));
        Groups = CopyAndOrder(
            groups,
            static geometry => geometry.ProjectedObjectId.Value,
            nameof(groups));
        RejectCrossCategoryDuplicates(Nodes, Groups);
        Metadata = new PropertyMap(metadata);
    }

    public static LayoutComputation Empty { get; } = new();

    public ImmutableArray<LayoutNodeGeometry> Nodes { get; }

    public ImmutableArray<LayoutGroupGeometry> Groups { get; }

    public PropertyMap Metadata { get; }

    public bool Equals(LayoutComputation? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Nodes.AsSpan().SequenceEqual(other.Nodes.AsSpan()) &&
        Groups.AsSpan().SequenceEqual(other.Groups.AsSpan()) &&
        Metadata.Equals(other.Metadata);

    public override bool Equals(object? obj) => Equals(obj as LayoutComputation);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        AddHashCodes(ref hash, Nodes);
        AddHashCodes(ref hash, Groups);
        hash.Add(Metadata);
        return hash.ToHashCode();
    }

    private static ImmutableArray<T> CopyAndOrder<T>(
        IEnumerable<T>? values,
        Func<T, string> identitySelector,
        string parameterName)
        where T : class
    {
        if (values is null)
        {
            return [];
        }

        var copy = values.ToArray();
        if (Array.Exists(copy, static value => value is null))
        {
            throw new ArgumentException(
                "Layout computation collections cannot contain null values.",
                parameterName);
        }

        Array.Sort(copy, (left, right) => StringComparer.Ordinal.Compare(
            identitySelector(left),
            identitySelector(right)));
        for (var index = 1; index < copy.Length; index++)
        {
            if (string.Equals(
                    identitySelector(copy[index - 1]),
                    identitySelector(copy[index]),
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Duplicate layout geometry ID '{identitySelector(copy[index])}'.",
                    parameterName);
            }
        }

        return [.. copy];
    }

    private static void RejectCrossCategoryDuplicates(
        ImmutableArray<LayoutNodeGeometry> nodes,
        ImmutableArray<LayoutGroupGeometry> groups)
    {
        var nodeIds = nodes
            .Select(static geometry => geometry.ProjectedObjectId)
            .ToHashSet();
        var duplicate = groups.FirstOrDefault(geometry =>
            nodeIds.Contains(geometry.ProjectedObjectId));
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Layout geometry ID '{duplicate.ProjectedObjectId}' occurs in more than one category.",
                nameof(groups));
        }
    }

    private static void AddHashCodes<T>(ref HashCode hash, ImmutableArray<T> values)
    {
        foreach (var value in values)
        {
            hash.Add(value);
        }
    }
}
