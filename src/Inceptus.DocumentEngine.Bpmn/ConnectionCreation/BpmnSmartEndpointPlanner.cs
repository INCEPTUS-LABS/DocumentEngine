using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.ConnectionCreation;

internal static class BpmnSmartEndpointPlanner
{
    internal static ElementConnectionCandidate? Choose(DocumentSnapshot document, ScopeGeometrySnapshot geometry,
        ScopeNodeGeometrySnapshot added, ImmutableArray<ElementConnectionCandidate> candidates, CancellationToken cancellationToken)
    {
        var bounds = geometry.Nodes.Append(added).ToDictionary(node => node.VisualStateId, node =>
        {
            var region = document.VisualModel.RoutingScopes?.SelectMany(scope => scope.Geometry.Regions)
                .SingleOrDefault(region => region.Id == node.RegionId);
            return region?.LocalToScopeTransform is { } transform
                ? node.LocalBounds.Translate(new(transform.OffsetX, transform.OffsetY)) : node.LocalBounds;
        });
        var obstacles = bounds.Select(pair => new BpmnRoutingObstacle(new(pair.Key.Value), pair.Value)).ToList();
        var paths = document.VisualModel.EnumerateConnectorRoutingRecords().Select(record => record.Path).ToArray();
        ElementConnectionCandidate? best = null;
        (int Crossings, int Bends, double Length) bestScore = (int.MaxValue, int.MaxValue, double.MaxValue);
        foreach (var candidate in candidates)
        {
            var proposal = candidate.Proposal.ProposedDocument!;
            proposal.VisualModel.TryGetVisualState(candidate.Plan.CreatedConnectorVisualStateId, out var connector);
            var source = Resolve(proposal, connector!.SourceAnchorId!);
            var target = Resolve(proposal, connector.TargetAnchorId!);
            var domain = new BpmnOrthogonalRouter.OperationContext(obstacles.Where(obstacle =>
                !IsAttachmentOwner(source.Visual, obstacle.NodeId) && !IsAttachmentOwner(target.Visual, obstacle.NodeId)).ToList());
            var outcome = BpmnOrthogonalRouter.TryRoute(
                new(new(source.Visual.Id.Value), source.Point, source.Side),
                new(new(target.Visual.Id.Value), target.Point, target.Side), domain, [], true, cancellationToken, out var path, out _);
            if (outcome != BpmnRoutingOutcome.Routed) continue;
            var score = ConnectorPathQuality.Score(path, paths);
            if (score.CompareTo(bestScore) < 0) { bestScore = score; best = candidate; }
        }
        return best;

        bool IsAttachmentOwner(VisualStateSnapshot visual, ProjectedObjectId obstacleId) =>
            document.SemanticModel.TryGetElement(visual.SemanticElementId, out var element) && element?.AttachedToElementId is { } owner &&
            document.VisualModel.VisualStates.Any(state => state.SemanticElementId == owner && state.Id.Value == obstacleId.Value);

        (VisualStateSnapshot Visual, PointD Point, ConnectorAnchorSide Side) Resolve(DocumentSnapshot proposed, ConnectorAnchorId anchorId)
        {
            foreach (var visual in proposed.VisualModel.VisualStates.Where(visual => bounds.ContainsKey(visual.Id)))
            {
                proposed.SemanticModel.TryGetElement(visual.SemanticElementId, out var element);
                var anchor = ElementConnectorAnchorResolver.Resolve(visual, element!.TypeId, BpmnConnectorAnchorPolicies.Provider)
                    .SingleOrDefault(anchor => anchor.Id == anchorId);
                if (anchor is not null)
                    return (visual, ConnectorAnchorGeometryResolver.ResolvePoint(bounds[visual.Id], anchor.Side,
                        anchor.NormalizedPosition ?? ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(anchor.Order,
                            ElementConnectorAnchorResolver.Resolve(visual, element.TypeId, BpmnConnectorAnchorPolicies.Provider).Count(item => item.Side == anchor.Side))), anchor.Side);
            }
            throw new InvalidOperationException("The planned endpoint does not resolve.");
        }
    }
}
