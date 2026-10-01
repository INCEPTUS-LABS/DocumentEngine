using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Organizational.Spatial;

internal sealed class OrganizationalRoutingInputPreparer(
    IOrganizationalElementEligibilityPolicy eligibilityPolicy) : IRoutingInputPreparer
{
    public PreparedRoutingInput? Prepare(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        ProjectedGraph graph,
        LayoutResult layout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!document.SemanticModel.ModelProfiles.IsAvailable(OrganizationalModelProfile.Id))
        {
            return null;
        }

        var pools = OrganizationalSemantics.GetPoolsInScope(document, activeScopeId);
        if (pools.IsEmpty)
        {
            return null;
        }

        var poolIds = pools.Select(static pool => pool.Id).ToHashSet();
        var complete = new RoutingObstacleDomain(graph.Nodes.Select(static node => node.Id));
        var domainByNode = new Dictionary<ProjectedObjectId, RoutingObstacleDomain>();
        foreach (var region in graph.Nodes.GroupBy(node =>
                     OrganizationalSemantics.ResolveSpatialPoolId(
                         document, activeScopeId, node, poolIds, eligibilityPolicy)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var domain = new RoutingObstacleDomain(region.Select(static node => node.Id));
            foreach (var node in region)
            {
                domainByNode.Add(node.Id, domain);
            }
        }

        var ports = graph.Ports.ToDictionary(static port => port.Id);
        var edges = new List<KeyValuePair<ProjectedObjectId, RoutingObstacleDomain>>(graph.EdgeCount);
        foreach (var edge in graph.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = domainByNode[edge.SourceNodeId];
            var domain = ReferenceEquals(source, domainByNode[edge.TargetNodeId]) ? source : complete;
            var owners = new[]
            {
                edge.SourceNodeId,
                edge.TargetNodeId,
                edge.SourcePortId is null ? edge.SourceNodeId : ports[edge.SourcePortId].OwnerNodeId,
                edge.TargetPortId is null ? edge.TargetNodeId : ports[edge.TargetPortId].OwnerNodeId,
            };
            if (owners.Any(owner => !domain.Contains(owner)))
            {
                domain = new RoutingObstacleDomain(domain.NodeIds.Concat(owners));
            }
            edges.Add(new(edge.Id, domain));
        }

        return new PreparedRoutingInput(graph, layout, edges);
    }
}
