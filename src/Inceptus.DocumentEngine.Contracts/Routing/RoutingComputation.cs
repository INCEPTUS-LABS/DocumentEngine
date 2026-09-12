using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>
/// Immutable per-edge routing outcomes and metadata computed by one Routing Algorithm.
/// </summary>
public sealed class RoutingComputation : IEquatable<RoutingComputation>
{
    public RoutingComputation(
        IEnumerable<RoutedConnectorGeometry>? routes = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? metadata = null,
        IEnumerable<ProjectedObjectId>? noRouteEdgeIds = null)
    {
        Routes = CopyAndOrder(routes, nameof(routes));
        Metadata = new PropertyMap(metadata);
        NoRouteEdgeIds = CopyAndOrder(noRouteEdgeIds, nameof(noRouteEdgeIds));
    }

    public static RoutingComputation Empty { get; } = new();

    public ImmutableArray<RoutedConnectorGeometry> Routes { get; }

    public PropertyMap Metadata { get; }

    public ImmutableArray<ProjectedObjectId> NoRouteEdgeIds { get; }

    public bool Equals(RoutingComputation? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Routes.AsSpan().SequenceEqual(other.Routes.AsSpan()) &&
        NoRouteEdgeIds.AsSpan().SequenceEqual(other.NoRouteEdgeIds.AsSpan()) &&
        Metadata.Equals(other.Metadata);

    public override bool Equals(object? obj) => Equals(obj as RoutingComputation);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var route in Routes)
        {
            hash.Add(route);
        }

        foreach (var edgeId in NoRouteEdgeIds)
        {
            hash.Add(edgeId);
        }

        hash.Add(Metadata);
        return hash.ToHashCode();
    }

    private static ImmutableArray<RoutedConnectorGeometry> CopyAndOrder(
        IEnumerable<RoutedConnectorGeometry>? routes,
        string parameterName)
    {
        if (routes is null)
        {
            return [];
        }

        var copy = routes.ToArray();
        if (Array.Exists(copy, static route => route is null))
        {
            throw new ArgumentException(
                "Routing computation collections cannot contain null values.",
                parameterName);
        }

        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(
                left.ProjectedEdgeId.Value,
                right.ProjectedEdgeId.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].ProjectedEdgeId == copy[index].ProjectedEdgeId)
            {
                throw new ArgumentException(
                    $"Duplicate routed connector ID '{copy[index].ProjectedEdgeId}'.",
                    parameterName);
            }
        }

        return [.. copy];
    }

    private static ImmutableArray<ProjectedObjectId> CopyAndOrder(
        IEnumerable<ProjectedObjectId>? edgeIds,
        string parameterName)
    {
        if (edgeIds is null)
        {
            return [];
        }

        var copy = edgeIds.ToArray();
        if (Array.Exists(copy, static edgeId => edgeId is null))
        {
            throw new ArgumentException(
                "No-route edge identity collections cannot contain null values.",
                parameterName);
        }

        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.Value, right.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1] == copy[index])
            {
                throw new ArgumentException(
                    $"Duplicate no-route edge ID '{copy[index]}'.",
                    parameterName);
            }
        }

        return [.. copy];
    }
}
