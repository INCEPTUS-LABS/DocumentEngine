using System.Collections.Immutable;

namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Immutable contribution returned by one Projection rule.
/// Cross-rule topology is validated by the framework-owned Projection Engine.
/// </summary>
public sealed class ProjectionRuleContribution : IEquatable<ProjectionRuleContribution>
{
    public ProjectionRuleContribution(
        IEnumerable<ProjectedNode>? nodes = null,
        IEnumerable<ProjectedEdge>? edges = null,
        IEnumerable<ProjectedGroup>? groups = null,
        IEnumerable<ProjectedPort>? ports = null,
        IEnumerable<ProjectedLabel>? labels = null)
    {
        Nodes = CopyAndOrder(nodes, nameof(nodes));
        Edges = CopyAndOrder(edges, nameof(edges));
        Groups = CopyAndOrder(groups, nameof(groups));
        Ports = CopyAndOrder(ports, nameof(ports));
        Labels = CopyAndOrder(labels, nameof(labels));
    }

    public static ProjectionRuleContribution Empty { get; } = new();

    public ImmutableArray<ProjectedNode> Nodes { get; }

    public ImmutableArray<ProjectedEdge> Edges { get; }

    public ImmutableArray<ProjectedGroup> Groups { get; }

    public ImmutableArray<ProjectedPort> Ports { get; }

    public ImmutableArray<ProjectedLabel> Labels { get; }

    public bool Equals(ProjectionRuleContribution? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Nodes.AsSpan().SequenceEqual(other.Nodes.AsSpan()) &&
        Edges.AsSpan().SequenceEqual(other.Edges.AsSpan()) &&
        Groups.AsSpan().SequenceEqual(other.Groups.AsSpan()) &&
        Ports.AsSpan().SequenceEqual(other.Ports.AsSpan()) &&
        Labels.AsSpan().SequenceEqual(other.Labels.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as ProjectionRuleContribution);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        AddHashCodes(ref hash, Nodes);
        AddHashCodes(ref hash, Edges);
        AddHashCodes(ref hash, Groups);
        AddHashCodes(ref hash, Ports);
        AddHashCodes(ref hash, Labels);
        return hash.ToHashCode();
    }

    private static ImmutableArray<T> CopyAndOrder<T>(
        IEnumerable<T>? values,
        string parameterName)
        where T : class, IProjectedObject
    {
        if (values is null)
        {
            return [];
        }

        var copy = values.ToArray();
        if (Array.Exists(copy, static value => value is null))
        {
            throw new ArgumentException(
                "Projected contributions cannot contain null values.",
                parameterName);
        }

        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.Id.Value, right.Id.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].Id == copy[index].Id)
            {
                throw new ArgumentException(
                    $"Duplicate projected object ID '{copy[index].Id}'.",
                    parameterName);
            }
        }

        return [.. copy];
    }

    private static void AddHashCodes<T>(ref HashCode hash, ImmutableArray<T> values)
    {
        foreach (var value in values)
        {
            hash.Add(value);
        }
    }
}
