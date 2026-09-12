using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>
/// Defines a replaceable, read-only routing policy invoked by the framework-owned Routing Engine.
/// </summary>
public interface IRoutingAlgorithm
{
    RoutingAlgorithmResult Route(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingContext context,
        CancellationToken cancellationToken);
}
