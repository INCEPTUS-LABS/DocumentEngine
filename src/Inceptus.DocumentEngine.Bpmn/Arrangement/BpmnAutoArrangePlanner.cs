using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Spatial;

namespace Inceptus.DocumentEngine.Bpmn.Arrangement;

public static class BpmnAutoArrangePlanner
{
    public static bool ContainsArrangeableNode(DocumentSnapshot document, IEnumerable<VisualStateId> visibleVisuals) =>
        visibleVisuals.Any(id => document.VisualModel.TryGetVisualState(id, out var visual) &&
            document.SemanticModel.TryGetElement(visual!.SemanticElementId, out var element) &&
            element is { AttachedToElementId: null } && BpmnSemanticTypes.IsFlowNode(element.TypeId));

    public static BpmnAutoArrangePlan Plan(DocumentSnapshot document, IEnumerable<DocumentScopeId> scopeIds,
        CancellationToken cancellationToken = default) => Plan(document, scopeIds, null, cancellationToken);

    public static BpmnAutoArrangePlan Plan(DocumentSnapshot document, IEnumerable<DocumentScopeId> scopeIds,
        GridSettings? gridSettings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scopeIds);
        var selected = scopeIds.Distinct().OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
        var proposal = document;
        var replacements = new Dictionary<VisualStateId, VisualStateSnapshot>();
        var heights = new List<SpatialRegionHeightIntent>();
        var widths = new List<SpatialScopeWidthIntent>();
        foreach (var scopeId in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var saved = document.VisualModel.RoutingScopes?.SingleOrDefault(scope => scope.ScopeId == scopeId)
                ?? throw new InvalidOperationException("Auto Arrange requires the current saved presentation geometry.");
            var elements = document.SemanticModel.Elements.Where(element =>
                document.SemanticModel.TryGetScope(element.Id, out var scope) && scope!.Id == scopeId &&
                BpmnSemanticTypes.IsFlowNode(element.TypeId)).ToDictionary(element => element.Id);
            var visuals = document.VisualModel.VisualStates.Where(visual => elements.ContainsKey(visual.SemanticElementId)).ToArray();
            if (visuals.GroupBy(visual => visual.SemanticElementId).Any(group => group.Count() != 1))
                throw new InvalidOperationException("Auto Arrange requires unambiguous visual identities.");
            var geometry = saved.Geometry.Nodes.ToDictionary(node => node.VisualStateId);
            var regionOrder = saved.Geometry.Regions.Where(region => region.IsActive)
                .OrderBy(region => region.ContentBounds!.Value.Top).ThenBy(region => region.Id.Value, StringComparer.Ordinal)
                .Select((region, index) => (region.Id, index)).ToDictionary(item => item.Id, item => item.index);
            var nodes = visuals.Where(visual => visual.BoundaryAttachment is null).Select(visual =>
            {
                if (!geometry.TryGetValue(visual.Id, out var body))
                    throw new InvalidOperationException("Auto Arrange requires saved geometry for every arranged node.");
                var type = elements[visual.SemanticElementId].TypeId;
                return new ArrangeNode(visual.SemanticElementId, visual.Id, body.LocalBounds, body.RegionId?.Value ?? "",
                    body.RegionId is { } region ? regionOrder.GetValueOrDefault(region) : 0,
                    type == BpmnSemanticTypes.StartEvent ? 0 : type == BpmnSemanticTypes.EndEvent ? 2 : 1,
                    BpmnGridPolicy.Supports(type));
            }).ToArray();
            var ids = nodes.Select(node => node.Id).ToHashSet();
            // Boundary outgoing branches begin at their owner's track, never at an independent rank.
            SemanticElementId Owner(SemanticElementId id) => elements.GetValueOrDefault(id)?.AttachedToElementId ?? id;
            var edges = document.SemanticModel.Relationships.Where(edge => edge.TypeId == BpmnSemanticTypes.SequenceFlow &&
                ids.Contains(Owner(edge.SourceId)) && ids.Contains(Owner(edge.TargetId)))
                .Select(edge => new ArrangeEdge(edge.Id, Owner(edge.SourceId), Owner(edge.TargetId))).ToArray();
            if (nodes.Length == 0) continue;
            var layers = BpmnAutoArrangeGraph.Prepare(nodes, edges, cancellationToken);
            var basis = proposal;
            var frames = saved.Geometry.Regions.Where(region => region.IsActive)
                .ToDictionary(region => region.Id.Value, region => region.LocalToScopeTransform!.Value);
            // Expansion can move a downstream Pool/Lane frame. Re-evaluate coordinate allocation
            // from the same logical plan and original geometry, before any connector planning.
            // Each pass stabilizes at least the next frame in the ordered container stack.
            for (var pass = 0; ; pass++)
            {
                var arranged = BpmnAutoArrangeGraph.Place(nodes, layers, gridSettings, frames, cancellationToken);
                var local = nodes.ToDictionary(node => node.VisualId, node => arranged[node.Id]);
                foreach (var visual in visuals.Where(visual => visual.BoundaryAttachment is not null))
                {
                    var attachment = visual.BoundaryAttachment!;
                    if (!arranged.TryGetValue(elements[visual.SemanticElementId].AttachedToElementId!, out var owner))
                        throw new InvalidOperationException("Boundary attachment owner is not in the active arrangement.");
                    local.Add(visual.Id, attachment.ResolveBounds(owner, visual.Size));
                }
                var expanded = OrganizationalArrangeGeometry.Expand(basis, scopeId, local);
                var nextFrames = expanded.Document.VisualModel.RoutingScopes!.Value.Single(scope => scope.ScopeId == scopeId)
                    .Geometry.Regions.Where(region => region.IsActive)
                    .ToDictionary(region => region.Id.Value, region => region.LocalToScopeTransform!.Value);
                if (gridSettings is { SnapToGrid: true } && frames.Any(pair => nextFrames[pair.Key] != pair.Value))
                {
                    if (pass >= frames.Count) throw new InvalidOperationException("The grid cannot fit the container arrangement.");
                    frames = nextFrames;
                    continue;
                }
                foreach (var visual in visuals)
                    replacements[visual.Id] = WithGeometry(visual, local[visual.Id].TopLeft, visual.ConnectorAnchors);
                proposal = expanded.Document;
                heights.AddRange(expanded.Heights);
                widths.AddRange(expanded.Widths);
                break;
            }
        }
        proposal = ReplaceVisuals(proposal, replacements);
        var anchors = BpmnAutoArrangeConnectorPlanner.ArrangeAttachments(proposal, selected, replacements.Keys.ToHashSet(), cancellationToken);
        foreach (var pair in anchors) replacements[pair.Key] = pair.Value;
        proposal = ReplaceVisuals(proposal, replacements);
        var changed = replacements.Where(pair => !document.VisualModel.TryGetVisualState(pair.Key, out var old) || !Equals(old, pair.Value))
            .Select(pair => pair.Key);
        return new(document, proposal, heights, widths, changed);
    }

    internal static VisualStateSnapshot WithGeometry(VisualStateSnapshot visual, PointD position, IEnumerable<ConnectorAnchor> anchors) =>
        new(visual.Id, visual.SemanticElementId, position, visual.Size, visual.PlacementMode, visual.Route, visual.Properties,
            anchors, visual.SourceAnchorId, visual.TargetAnchorId, visual.BoundaryAttachment);

    internal static DocumentSnapshot ReplaceVisuals(DocumentSnapshot document, IReadOnlyDictionary<VisualStateId, VisualStateSnapshot> replacements) =>
        new(document.SemanticModel, new VisualModelSnapshot(document.DocumentId, document.Revision,
            document.VisualModel.VisualStates.Select(visual => replacements.GetValueOrDefault(visual.Id, visual)),
            document.VisualModel.ProfileElementPresentations, document.VisualModel.RoutingScopes, document.VisualModel.DocumentRoutingStates),
            document.Metadata, document.Publication);
}
