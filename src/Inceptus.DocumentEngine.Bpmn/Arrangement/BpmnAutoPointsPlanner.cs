using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Arrangement;

/// <summary>Retains anchor identities and optimizes only the selected node's automatic/straight endpoints.</summary>
public static class BpmnAutoPointsPlanner
{
    public static bool CanOptimize(DocumentSnapshot document, VisualStateId visualId)
    {
        if (!document.VisualModel.TryGetVisualState(visualId, out var visual)) return false;
        var anchors = visual!.ConnectorAnchors.Select(anchor => anchor.Id).ToHashSet();
        return document.VisualModel.VisualStates.Any(connector =>
            (connector.SourceAnchorId is { } first && anchors.Contains(first) ||
             connector.TargetAnchorId is { } second && anchors.Contains(second)) &&
            document.VisualModel.EnumerateConnectorRoutingRecords().Any(record => record.VisualStateId == connector.Id && record.RoutingType != ConnectorRoutingType.Manual));
    }

    public static BpmnAutoArrangePlan Plan(DocumentSnapshot document, VisualStateId visualId, CancellationToken cancellationToken = default)
    {
        if (!document.VisualModel.TryGetVisualState(visualId, out var visual) ||
            !document.SemanticModel.TryGetScope(visual!.SemanticElementId, out var scope))
            throw new InvalidOperationException("The selected element is unavailable.");
        var replacements = BpmnAutoArrangeConnectorPlanner.ArrangeAttachments(document, [scope!.Id], [], cancellationToken, visualId);
        var proposal = BpmnAutoArrangePlanner.ReplaceVisuals(document, replacements);
        if (!PreservesQuality(document, proposal, scope.Id, visual.SemanticElementId, cancellationToken))
            return new(document, document, [], [], []);
        return new(document, proposal, [], [], replacements.Where(pair =>
            !document.VisualModel.TryGetVisualState(pair.Key, out var prior) || !Equals(prior, pair.Value)).Select(pair => pair.Key));
    }

    private static bool PreservesQuality(DocumentSnapshot before, DocumentSnapshot proposal, DocumentScopeId scopeId,
        SemanticElementId selected, CancellationToken cancellationToken)
    {
        var bounds = BpmnAutoArrangeConnectorPlanner.ResolveBounds(proposal, [scopeId], [], null, out var containers);
        var obstacles = bounds.Where(pair => !containers.Contains(pair.Key)).Select(pair => new BpmnRoutingObstacle(new(pair.Key.Value), pair.Value)).ToList();
        var visuals = proposal.VisualModel.VisualStates.GroupBy(visual => visual.SemanticElementId).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        var planned = new Dictionary<VisualStateId, ImmutableArray<PointD>>();
        foreach (var record in before.VisualModel.EnumerateConnectorRoutingRecords())
        {
            if (record.RoutingType == ConnectorRoutingType.Manual ||
                !proposal.VisualModel.TryGetVisualState(record.VisualStateId, out var connector) ||
                !proposal.SemanticModel.TryGetRelationship(connector!.SemanticElementId, out var flow) ||
                flow!.SourceId != selected && flow.TargetId != selected) continue;
            if (!visuals.TryGetValue(flow.SourceId, out var source) || !visuals.TryGetValue(flow.TargetId, out var target) ||
                !bounds.ContainsKey(flow.SourceId) || !bounds.ContainsKey(flow.TargetId)) return false;
            proposal.SemanticModel.TryGetElement(source.SemanticElementId, out var sourceElement);
            proposal.SemanticModel.TryGetElement(target.SemanticElementId, out var targetElement);
            var first = Endpoint(source, sourceElement!.TypeId, connector.SourceAnchorId!);
            var second = Endpoint(target, targetElement!.TypeId, connector.TargetAnchorId!);
            var domain = new BpmnOrthogonalRouter.OperationContext(obstacles.Where(obstacle =>
                obstacle.NodeId.Value != sourceElement.AttachedToElementId?.Value && obstacle.NodeId.Value != targetElement.AttachedToElementId?.Value).ToList());
            ImmutableArray<PointD> path;
            if (record.RoutingType == ConnectorRoutingType.Straight) path = [first.Point, second.Point];
            else if (BpmnOrthogonalRouter.TryRoute(first, second, domain, [], true, cancellationToken, out path, out _) != BpmnRoutingOutcome.Routed) return false;
            planned.Add(record.VisualStateId, path);
        }
        return Score(true).CompareTo(Score(false)) <= 0;

        BpmnRoutingEndpoint Endpoint(VisualStateSnapshot visual, SemanticTypeId type, ConnectorAnchorId id)
        {
            var anchors = ElementConnectorAnchorResolver.Resolve(visual, type, BpmnConnectorAnchorPolicies.Provider);
            var anchor = anchors.Single(anchor => anchor.Id == id);
            return new(new(visual.SemanticElementId.Value), ConnectorAnchorGeometryResolver.ResolvePoint(bounds[visual.SemanticElementId], anchor.Side,
                anchor.NormalizedPosition ?? ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(anchor.Order, anchors.Count(item => item.Side == anchor.Side))), anchor.Side);
        }

        (int Crossings, int Bends, double Length) Score(bool usePlanned)
        {
            var crossings = 0; var bends = 0; var length = 0d;
            foreach (var records in (before.VisualModel.RoutingScopes ?? []).Select(scope => scope.Connectors)
                         .Concat(before.VisualModel.DocumentRoutingStates.Select(owner => owner.Connectors)))
                foreach (var record in records.Where(record => planned.ContainsKey(record.VisualStateId)))
                {
                    var score = ConnectorPathQuality.Score(Path(record), records.Where(other => other.VisualStateId != record.VisualStateId).Select(Path));
                    crossings += score.Crossings; bends += score.Bends; length += score.Length;
                }
            return (crossings, bends, length);
            ImmutableArray<PointD> Path(ConnectorRoutingRecord record) => usePlanned ? planned.GetValueOrDefault(record.VisualStateId, record.Path) : record.Path;
        }
    }
}
