using System.Xml;
using System.Xml.Linq;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Semantics;
using static Inceptus.Bpmn.Integration.BpmnXmlVocabulary;

namespace Inceptus.Bpmn.Integration;

internal sealed partial class BpmnXmlExporter
{
    private void WriteDiagrams()
    {
        foreach (var (scopeId, owner) in _planes.Concat(_additionalPlanes).OrderBy(pair => pair.Key.Value, StringComparer.Ordinal))
        {
            var planeKey = scopeId.Value + ":" + (string)owner.Attribute("id")!;
            var plane = new XElement(BpmnDi + "BPMNPlane", new XAttribute("id", _ids.Generated("plane:" + planeKey)),
                new XAttribute("bpmnElement", (string)owner.Attribute("id")!));
            var scope = _document.VisualModel.RoutingScopes?.FirstOrDefault(candidate => candidate.ScopeId == scopeId);
            var bounds = new Dictionary<SemanticElementId, RectD>();
            var labels = new Dictionary<SemanticElementId, RectD>();
            var visuals = new Dictionary<SemanticElementId, VisualStateSnapshot>();
            foreach (var visual in _document.VisualModel.VisualStates)
            {
                if (!_document.SemanticModel.TryGetElement(visual.SemanticElementId, out var element) ||
                    !_document.SemanticModel.TryGetScope(element!.Id, out var elementScope) || elementScope!.Id != scopeId) continue;
                if (visuals.ContainsKey(element.Id))
                { _log.Omit(visual.Id.Value, "VisualState", "Additional visual representation omitted; one DI shape per semantic element is exported."); continue; }
                visuals.Add(element.Id, visual);
                var geometry = scope?.Geometry.Nodes.FirstOrDefault(node => node.VisualStateId == visual.Id);
                var rectangle = geometry?.LocalBounds ?? new RectD(visual.Position.X, visual.Position.Y, visual.Size.Width, visual.Size.Height);
                if (geometry?.RegionId is { } regionId && scope?.Geometry.Regions.FirstOrDefault(region => region.Id == regionId)?.LocalToScopeTransform is { } transform)
                    rectangle = rectangle.Translate(new VectorD(transform.OffsetX, transform.OffsetY));
                if (!rectangle.IsEmpty) bounds.Add(element.Id, rectangle);
                if (scope?.Geometry.Captions.FirstOrDefault(caption => caption.OwnerVisualStateId == visual.Id) is { } caption)
                {
                    var labelBounds = caption.PlacementBounds;
                    if (geometry?.RegionId is { } labelRegion && scope.Geometry.Regions.FirstOrDefault(region => region.Id == labelRegion)?.LocalToScopeTransform is { } labelTransform)
                        labelBounds = labelBounds.Translate(new VectorD(labelTransform.OffsetX, labelTransform.OffsetY));
                    labels[element.Id] = labelBounds;
                }
            }
            if (scope is not null)
            {
                foreach (var region in scope.Geometry.Regions)
                    if (region.ContainerSemanticElementId is { } id && region.ContentBounds is { } rectangle) bounds[id] = rectangle;
                // Resolve the existing projection envelopes, including native header bands.
                // This reads saved region geometry; it does not run layout or routing.
                var projection = BpmnIntegrationPolicy.Projection.Project(_document, scopeId);
                if (projection.Graph is { } graph)
                    foreach (var node in graph.Nodes.Where(node => node.RegionEnvelope is not null))
                        bounds[node.Source.SemanticElementId] = node.RegionEnvelope!.Resolve(scope.Geometry.Regions);
                else _log.Omit(scopeId.Value, "Container header geometry", "Projection could not resolve header envelopes; saved content bounds are retained.");
                foreach (var group in OrganizationalSemantics.GetPoolsInScope(_document, scopeId)
                    .Where(pool => OrganizationalSemantics.GetParticipantId(pool) is not null)
                    .GroupBy(pool => OrganizationalSemantics.GetParticipantId(pool)!))
                {
                    var rectangles = group.Where(pool => bounds.ContainsKey(pool.Id)).Select(pool => bounds[pool.Id]).ToList();
                    if (bounds.TryGetValue(group.Key, out var header)) rectangles.Add(header);
                    if (rectangles.Count > 0) bounds[group.Key] = Union(rectangles);
                }
            }
            foreach (var (id, rectangle) in bounds.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal))
            {
                if (!_elements.TryGetValue(id, out var semanticXml) || !BelongsToPlane(semanticXml, owner)) continue;
                var shape = new XElement(BpmnDi + "BPMNShape", new XAttribute("id", _ids.Generated("shape:" + scopeId.Value + ":" + id.Value)),
                    new XAttribute("bpmnElement", (string)semanticXml.Attribute("id")!), BoundsXml(rectangle));
                if (semanticXml.Name.LocalName is "participant" or "lane") shape.SetAttributeValue("isHorizontal", true);
                if (semanticXml.Name == Model + "subProcess") shape.SetAttributeValue("isExpanded", false);
                if (visuals.TryGetValue(id, out var visual) && NodeLabelVisualOverride.TryRead(visual.Properties, out var label))
                    shape.Add(new XElement(BpmnDi + "BPMNLabel", BoundsXml(label!.ResolveBounds(rectangle))));
                else if (labels.TryGetValue(id, out var labelBounds))
                    shape.Add(new XElement(BpmnDi + "BPMNLabel", BoundsXml(labelBounds)));
                plane.Add(shape);
            }
            foreach (var flow in _document.SemanticModel.Relationships)
            {
                if (!_elements.TryGetValue(flow.Id, out var flowXml) || !BelongsToPlane(flowXml, owner) ||
                    !bounds.ContainsKey(flow.SourceId) && !bounds.ContainsKey(flow.TargetId)) continue;
                var visual = _document.VisualModel.VisualStates.FirstOrDefault(candidate => candidate.SemanticElementId == flow.Id);
                if (visual is null) { _log.Omit(flow.Id.Value, "DI edge", "The relationship has no visual state; semantic flow is retained."); continue; }
                var route = scope?.Connectors.FirstOrDefault(record => record.VisualStateId == visual.Id)
                    ?? _document.VisualModel.DocumentRoutingStates.SelectMany(state => state.Connectors).FirstOrDefault(record => record.VisualStateId == visual.Id);
                var path = route?.Outcome == ConnectorRoutingOutcome.Path ? route.Path : visual.Route;
                if (path.Length < 2)
                { _log.Omit(flow.Id.Value, "DI edge", "No saved path is available; no waypoints are fabricated."); continue; }
                var edge = new XElement(BpmnDi + "BPMNEdge", new XAttribute("id", _ids.Generated("edge:" + scopeId.Value + ":" + flow.Id.Value)),
                    new XAttribute("bpmnElement", _ids.Semantic(flow.Id.Value)),
                    path.Select(point => new XElement(Di + "waypoint", new XAttribute("x", XmlConvert.ToString(point.X)), new XAttribute("y", XmlConvert.ToString(point.Y)))));
                if (ConnectorLabelPlacement.TryRead(visual.Properties, out var placement))
                {
                    var center = BpmnXmlGeometry.PointAt(path, placement!.PathPosition) + placement.Offset;
                    // Connector text is measured at presentation time. DI's optional label bounds
                    // retain the anchor position; no fictitious text measurement is persisted.
                    edge.Add(new XElement(BpmnDi + "BPMNLabel", BoundsXml(new RectD(center.X, center.Y, 0, 0))));
                    _log.Omit(visual.Id.Value, "Connector label sizing", "Label position is exported; measured text size and route-relative editing intent are omitted.");
                }
                plane.Add(edge);
                if (route is not null) _log.Omit(flow.Id.Value, "RoutingType/ManualDefinition/AutomaticProof/Priority",
                    "BPMN DI retains the visible path, not native routing modes, retained definitions, priority or validity proofs.");
            }
            if (plane.HasElements)
                _definitions.Add(new XElement(BpmnDi + "BPMNDiagram", new XAttribute("id", _ids.Generated("diagram:" + planeKey)), plane));
        }
        foreach (var visual in _document.VisualModel.VisualStates)
        {
            if (!visual.ConnectorAnchors.IsEmpty || visual.BoundaryAttachment is not null || visual.SourceAnchorId is not null)
                _log.Omit(visual.Id.Value, "Anchors/BoundaryPlacement", "Native anchor identities and attachment editing parameters are represented only by DI geometry.");
            _log.Omit(visual.Id.Value, "PlacementMode", "Native pinning/automatic placement mode is not represented by BPMN DI.");
            foreach (var property in visual.Properties.Keys)
                _log.Omit(visual.Id.Value, property,
                    property.StartsWith("inceptus:node-label:", StringComparison.Ordinal) || property.StartsWith("inceptus:connector-label:", StringComparison.Ordinal)
                        ? "Representable label bounds are exported above; native label editing parameters are omitted."
                        : "Visual property has no standard DI mapping.");
        }
    }

    private bool BelongsToPlane(XElement element, XElement owner) => owner.Name == Model + "collaboration"
        ? element.Parent == owner || CollaborationOf(element) == owner
        : element.Ancestors().Contains(owner);

    private static XElement BoundsXml(RectD rectangle) => new(Dc + "Bounds",
        new XAttribute("x", XmlConvert.ToString(rectangle.X)), new XAttribute("y", XmlConvert.ToString(rectangle.Y)),
        new XAttribute("width", XmlConvert.ToString(rectangle.Width)), new XAttribute("height", XmlConvert.ToString(rectangle.Height)));

    private static RectD Union(IEnumerable<RectD> values)
    {
        var rectangles = values.ToArray();
        var left = rectangles.Min(rectangle => rectangle.Left); var top = rectangles.Min(rectangle => rectangle.Top);
        return new(left, top, rectangles.Max(rectangle => rectangle.Right) - left, rectangles.Max(rectangle => rectangle.Bottom) - top);
    }
}
