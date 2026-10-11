using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.ConnectionCreation;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Arrangement;

internal static class BpmnAutoArrangeConnectorPlanner
{
    internal static Dictionary<VisualStateId, VisualStateSnapshot> ArrangeAttachments(DocumentSnapshot document,
        DocumentScopeId[] scopes, HashSet<VisualStateId> arranged, CancellationToken cancellationToken,
        VisualStateId? selectedVisualId = null, ScopeNodeGeometrySnapshot? addedNode = null)
    {
        var visuals = document.VisualModel.VisualStates.GroupBy(visual => visual.SemanticElementId)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single());
        var bounds = ResolveBounds(document, scopes, arranged, addedNode, out var boundaryContainers);
        return Arrange(document, visuals, bounds, boundaryContainers, selectedVisualId, cancellationToken);
    }

    internal static Dictionary<SemanticElementId, RectD> ResolveBounds(DocumentSnapshot document, DocumentScopeId[] scopes,
        HashSet<VisualStateId> arranged, ScopeNodeGeometrySnapshot? addedNode, out HashSet<SemanticElementId> boundaryContainers)
    {
        var bounds = new Dictionary<SemanticElementId, RectD>();
        boundaryContainers = [];
        foreach (var scope in (document.VisualModel.RoutingScopes ?? []).Where(scope => scopes.Contains(scope.ScopeId)))
        {
            foreach (var node in scope.Geometry.Nodes.Concat(addedNode is null ? [] : new[] { addedNode }))
            {
                if (!document.VisualModel.TryGetVisualState(node.VisualStateId, out var visual)) continue;
                var local = arranged.Contains(visual!.Id)
                    ? new RectD(visual.Position.X, visual.Position.Y, node.LocalBounds.Width, node.LocalBounds.Height) : node.LocalBounds;
                var region = scope.Geometry.Regions.SingleOrDefault(region => region.Id == node.RegionId);
                bounds[visual.SemanticElementId] = region?.LocalToScopeTransform is { } transform
                    ? local.Translate(new VectorD(transform.OffsetX, transform.OffsetY)) : local;
            }
            foreach (var pool in OrganizationalSemantics.GetPoolsInScope(document, scope.ScopeId))
            {
                var region = scope.Geometry.Regions.SingleOrDefault(region => region.ContainerSemanticElementId == pool.Id && region.IsActive);
                if (region is null) continue;
                bounds[pool.Id] = new ProjectedRegionEnvelope([region.Id], 38d).Resolve(scope.Geometry.Regions);
                boundaryContainers.Add(pool.Id);
            }
            foreach (var group in OrganizationalSemantics.GetPoolsInScope(document, scope.ScopeId)
                         .Where(pool => OrganizationalSemantics.GetParticipantId(pool) is not null)
                         .GroupBy(pool => OrganizationalSemantics.GetParticipantId(pool)!))
            {
                var regions = scope.Geometry.Regions.Where(region => region.IsActive &&
                    group.Any(pool => pool.Id == region.ContainerSemanticElementId)).ToArray();
                if (regions.Length == 0) continue;
                bounds[group.Key] = new ProjectedRegionEnvelope(regions.Select(region => region.Id), 76d, 38d).Resolve(scope.Geometry.Regions);
                boundaryContainers.Add(group.Key);
            }
        }
        return bounds;
    }

    private static Dictionary<VisualStateId, VisualStateSnapshot> Arrange(DocumentSnapshot document,
        Dictionary<SemanticElementId, VisualStateSnapshot> visuals, Dictionary<SemanticElementId, RectD> bounds,
        HashSet<SemanticElementId> boundaryContainers, VisualStateId? selectedVisualId, CancellationToken cancellationToken)
    {
        var assignments = new Dictionary<ConnectorAnchorId, (ConnectorAnchorSide Side, PointD Remote)>();
        var paths = new List<ImmutableArray<PointD>>();
        var obstacles = bounds.Where(pair => !boundaryContainers.Contains(pair.Key)).Select(pair =>
            new BpmnRoutingObstacle(new(pair.Key.Value), pair.Value)).ToArray();
        foreach (var flow in document.SemanticModel.Relationships.Where(flow =>
                     flow.TypeId == BpmnSemanticTypes.SequenceFlow || flow.TypeId == BpmnSemanticTypes.MessageFlow)
                     .OrderBy(flow => flow.TypeId == BpmnSemanticTypes.SequenceFlow ? 0 : 1)
                     .ThenBy(flow => flow.Id.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visuals.TryGetValue(flow.Id, out var connector) || !visuals.TryGetValue(flow.SourceId, out var source) ||
                !visuals.TryGetValue(flow.TargetId, out var target) || !bounds.TryGetValue(flow.SourceId, out var first) ||
                !bounds.TryGetValue(flow.TargetId, out var second)) continue;
            if (selectedVisualId is not null && (source.Id != selectedVisualId && target.Id != selectedVisualId ||
                document.VisualModel.EnumerateConnectorRoutingRecords().Any(record => record.VisualStateId == connector.Id &&
                    record.RoutingType == ConnectorRoutingType.Manual))) continue;
            var sourceAnchor = ResolveAnchor(source, connector.SourceAnchorId, ConnectorAnchorRole.Source);
            var targetAnchor = ResolveAnchor(target, connector.TargetAnchorId, ConnectorAnchorRole.Target);
            if (sourceAnchor is null || targetAnchor is null)
            {
                if (connector.SourceAnchorId is null || connector.TargetAnchorId is null ||
                    sourceAnchor is null && !ConnectorAnchorReferenceIdentity.IsPredefinedReference(connector.SourceAnchorId) ||
                    targetAnchor is null && !ConnectorAnchorReferenceIdentity.IsPredefinedReference(connector.TargetAnchorId))
                    throw new InvalidOperationException("Auto Arrange requires valid endpoint anchor identities.");
                continue; // Predefined anchors retain their policy-owned geometry.
            }
            if (selectedVisualId == source.Id && sourceAnchor.Predefined || selectedVisualId == target.Id && targetAnchor.Predefined) continue;
            var rightward = first.Right <= second.Left;
            var down = Center(second).Y >= Center(first).Y;
            var primary = flow.TypeId == BpmnSemanticTypes.SequenceFlow && rightward
                ? (ConnectorAnchorSide.Right, ConnectorAnchorSide.Left)
                : down ? (ConnectorAnchorSide.Bottom, ConnectorAnchorSide.Top) : (ConnectorAnchorSide.Top, ConnectorAnchorSide.Bottom);
            document.SemanticModel.TryGetElement(source.SemanticElementId, out var sourceElement);
            document.SemanticModel.TryGetElement(target.SemanticElementId, out var targetElement);
            var sourceSides = BpmnElementConnectionCreation.CandidateSides(WithoutAnchor(source, sourceAnchor.Id),
                sourceElement!.TypeId, sourceAnchor.Role).ToArray();
            var targetSides = BpmnElementConnectionCreation.CandidateSides(WithoutAnchor(target, targetAnchor.Id),
                targetElement!.TypeId, targetAnchor.Role).ToArray();
            if (selectedVisualId is not null)
            {
                if (source.Id != selectedVisualId) sourceSides = [sourceAnchor.Side];
                if (target.Id != selectedVisualId) targetSides = [targetAnchor.Side];
            }
            var pairs = new[] { primary, (ConnectorAnchorSide.Top, ConnectorAnchorSide.Top),
                (ConnectorAnchorSide.Bottom, ConnectorAnchorSide.Bottom), (ConnectorAnchorSide.Right, ConnectorAnchorSide.Left),
                (ConnectorAnchorSide.Left, ConnectorAnchorSide.Right) }
                .Concat(sourceSides.SelectMany(firstSide => targetSides.Select(secondSide => (firstSide, secondSide))))
                .Where(pair => sourceSides.Contains(pair.Item1) && targetSides.Contains(pair.Item2)).Distinct().ToArray();
            if (pairs.Length == 0) throw new InvalidOperationException("Auto Arrange has no policy-compatible endpoint sides.");
            (int Crossings, int Bends, double Length) bestScore = (int.MaxValue, int.MaxValue, double.MaxValue);
            var best = pairs[0];
            ImmutableArray<PointD> bestPath = [];
            var domain = new BpmnOrthogonalRouter.OperationContext(obstacles.Where(obstacle =>
                // An attached event is allowed on its owner, but the owner is not a valid endpoint obstacle for it.
                !(sourceElement?.AttachedToElementId?.Value == obstacle.NodeId.Value ||
                  targetElement?.AttachedToElementId?.Value == obstacle.NodeId.Value)).ToList());
            foreach (var pair in pairs)
            {
                var sourcePosition = selectedVisualId is not null && source.Id != selectedVisualId
                    ? sourceAnchor.NormalizedPosition ?? ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(sourceAnchor.Order, source.ConnectorAnchors.Count(anchor => anchor.Side == sourceAnchor.Side)) : .5;
                var targetPosition = selectedVisualId is not null && target.Id != selectedVisualId
                    ? targetAnchor.NormalizedPosition ?? ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(targetAnchor.Order, target.ConnectorAnchors.Count(anchor => anchor.Side == targetAnchor.Side)) : .5;
                var outcome = BpmnOrthogonalRouter.TryRoute(
                    new(new(flow.SourceId.Value), ConnectorAnchorGeometryResolver.ResolvePoint(first, pair.Item1, sourcePosition), pair.Item1),
                    new(new(flow.TargetId.Value), ConnectorAnchorGeometryResolver.ResolvePoint(second, pair.Item2, targetPosition), pair.Item2),
                    domain, [], true, cancellationToken, out var path, out _);
                if (outcome != BpmnRoutingOutcome.Routed) continue;
                var score = ConnectorPathQuality.Score(path, paths);
                if (score.CompareTo(bestScore) < 0) { bestScore = score; best = pair; bestPath = path; }
            }
            if (selectedVisualId is not null && bestPath.IsEmpty)
                throw new InvalidOperationException("No legal route for the proposed attachment arrangement is available.");
            if (selectedVisualId is null || source.Id == selectedVisualId) assignments[sourceAnchor.Id] = (best.Item1, Center(second));
            if (selectedVisualId is null || target.Id == selectedVisualId) assignments[targetAnchor.Id] = (best.Item2, Center(first));
            if (!bestPath.IsEmpty) paths.Add(bestPath);
        }
        var result = new Dictionary<VisualStateId, VisualStateSnapshot>();
        foreach (var visual in visuals.Values.Where(visual => visual.ConnectorAnchors.Any(anchor => assignments.ContainsKey(anchor.Id))))
        {
            var anchors = new List<ConnectorAnchor>();
            foreach (var side in Enum.GetValues<ConnectorAnchorSide>())
            {
                var members = visual.ConnectorAnchors.Where(anchor => assignments.TryGetValue(anchor.Id, out var planned)
                    ? planned.Side == side : anchor.Side == side).ToArray();
                var fixedPoints = members.Where(anchor => !assignments.ContainsKey(anchor.Id)).ToDictionary(anchor => anchor.Id,
                    anchor => anchor.NormalizedPosition ?? ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(anchor.Order,
                        visual.ConnectorAnchors.Count(other => other.Side == side)));
                var ordered = members.Where(anchor => assignments.ContainsKey(anchor.Id))
                    .OrderBy(anchor => side is ConnectorAnchorSide.Left or ConnectorAnchorSide.Right
                        ? assignments[anchor.Id].Remote.Y : assignments[anchor.Id].Remote.X)
                    .ThenBy(anchor => anchor.Id.Value, StringComparer.Ordinal).ToArray();
                var positions = new Dictionary<ConnectorAnchorId, double>(fixedPoints);
                for (var index = 0; index < ordered.Length; index++)
                {
                    var position = (index + 1d) / (ordered.Length + 1d);
                    while (positions.ContainsValue(position)) position = Math.BitIncrement(position);
                    positions[ordered[index].Id] = position;
                }
                anchors.AddRange(members.OrderBy(anchor => positions[anchor.Id]).ThenBy(anchor => anchor.Id.Value, StringComparer.Ordinal)
                    .Select((anchor, index) => new ConnectorAnchor(anchor.Id, side, anchor.Role, index, positions[anchor.Id])));
            }
            result.Add(visual.Id, BpmnAutoArrangePlanner.WithGeometry(visual, visual.Position, anchors));
        }
        return result;

        ArrangementAnchor? ResolveAnchor(VisualStateSnapshot visual, ConnectorAnchorId? id, ConnectorAnchorRole role)
        {
            var physical = visual.ConnectorAnchors.SingleOrDefault(anchor => anchor.Id == id);
            if (physical is not null) return new(physical.Id, physical.Side, physical.Role, physical.Order, physical.NormalizedPosition, false);
            if (selectedVisualId is null || !document.SemanticModel.TryGetElement(visual.SemanticElementId, out var element)) return null;
            var resolved = ElementConnectorAnchorResolver.Resolve(visual, element!.TypeId, BpmnConnectorAnchorPolicies.Provider).SingleOrDefault(anchor => anchor.Id == id);
            return resolved is null ? null : new(resolved.Id, resolved.Side, role, resolved.Order,
                resolved.NormalizedPosition ?? ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(resolved.Order,
                    ElementConnectorAnchorResolver.Resolve(visual, element.TypeId, BpmnConnectorAnchorPolicies.Provider).Count(anchor => anchor.Side == resolved.Side)), true);
        }
    }

    private sealed record ArrangementAnchor(ConnectorAnchorId Id, ConnectorAnchorSide Side, ConnectorAnchorRole Role,
        int Order, double? NormalizedPosition, bool Predefined);

    private static PointD Center(RectD bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

    private static VisualStateSnapshot WithoutAnchor(VisualStateSnapshot visual, ConnectorAnchorId excluded) =>
        BpmnAutoArrangePlanner.WithGeometry(visual, visual.Position, visual.ConnectorAnchors.Where(anchor => anchor.Id != excluded)
            .GroupBy(anchor => anchor.Side).SelectMany(group => group.OrderBy(anchor => anchor.Order)
                .Select((anchor, index) => new ConnectorAnchor(anchor.Id, anchor.Side, anchor.Role, index, anchor.NormalizedPosition))));
}
