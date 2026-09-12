using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.Contracts.Layout;

/// <summary>
/// Defines a replaceable, read-only layout policy invoked by the framework-owned Layout Engine.
/// </summary>
public interface ILayoutAlgorithm
{
    LayoutAlgorithmResult Compute(
        ProjectedGraph graph,
        LayoutContext context,
        CancellationToken cancellationToken);
}
