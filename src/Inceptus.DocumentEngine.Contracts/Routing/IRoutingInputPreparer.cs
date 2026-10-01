using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>
/// Pure, synchronous preparation of notation-neutral obstacle domains before routing.
/// Returning null retains the complete graph domain for every edge. Implementations must
/// not mutate the Document, execute commands, or depend on derived Scene presentation.
/// </summary>
public interface IRoutingInputPreparer
{
    PreparedRoutingInput? Prepare(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        ProjectedGraph graph,
        LayoutResult layout,
        CancellationToken cancellationToken);
}
