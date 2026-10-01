using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>
/// Complete per-edge obstacle domains bound to the exact immutable graph and layout
/// used during preparation. RoutingEngine validates compatibility and coverage before
/// invoking an algorithm. This data has no semantic region or presentation identities.
/// </summary>
public sealed class PreparedRoutingInput
{
    public PreparedRoutingInput(
        ProjectedGraph sourceGraph,
        LayoutResult sourceLayout,
        IEnumerable<KeyValuePair<ProjectedObjectId, RoutingObstacleDomain>> edgeDomains)
    {
        ArgumentNullException.ThrowIfNull(sourceGraph);
        ArgumentNullException.ThrowIfNull(sourceLayout);
        ArgumentNullException.ThrowIfNull(edgeDomains);
        SourceGraph = sourceGraph;
        SourceLayout = sourceLayout;
        EdgeDomains = edgeDomains.ToImmutableDictionary();
        if (EdgeDomains.Values.Any(static domain => domain is null))
        {
            throw new ArgumentException("Obstacle domains cannot be null.", nameof(edgeDomains));
        }
    }

    public ProjectedGraph SourceGraph { get; }

    public LayoutResult SourceLayout { get; }

    public ImmutableDictionary<ProjectedObjectId, RoutingObstacleDomain> EdgeDomains { get; }
}
