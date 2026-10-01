using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Bpmn.Routing;

/// <summary>
/// Operation-local preprocessing, shared once per distinct immutable obstacle domain.
/// No results or search state survive a top-level Route call.
/// </summary>
internal sealed class BpmnRoutingOperation
{
    private readonly Dictionary<ProjectedObjectId, LayoutNodeGeometry> _geometry;
    private readonly PreparedRoutingInput? _prepared;
    private readonly RoutingObstacleDomain _complete;
    private readonly Dictionary<RoutingObstacleDomain, BpmnOrthogonalRouter.OperationContext> _domains = [];

    public BpmnRoutingOperation(LayoutResult layout, PreparedRoutingInput? prepared)
    {
        _geometry = layout.Nodes.ToDictionary(static node => node.ProjectedObjectId);
        _prepared = prepared;
        _complete = new RoutingObstacleDomain(_geometry.Keys);
    }

    public BpmnOrthogonalRouter.OperationContext GetDomain(ProjectedObjectId edgeId)
    {
        var domain = _prepared is null ? _complete : _prepared.EdgeDomains[edgeId];
        if (!_domains.TryGetValue(domain, out var context))
        {
            context = new BpmnOrthogonalRouter.OperationContext(domain.NodeIds
                .Select(id => new BpmnRoutingObstacle(id, _geometry[id].Bounds)).ToArray());
            _domains.Add(domain, context);
        }

        return context;
    }
}
