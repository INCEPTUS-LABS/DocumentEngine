using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using static Inceptus.Bpmn.Integration.BpmnXmlVocabulary;

namespace Inceptus.Bpmn.Integration;

internal sealed partial class BpmnXmlImporter
{
    private readonly Dictionary<SemanticElementId, RectD> _bounds = [];
    private readonly Dictionary<SemanticElementId, RectD> _labels = [];
    private readonly Dictionary<SemanticElementId, ImmutableArray<PointD>> _paths = [];
    private readonly Dictionary<DocumentScopeId, VectorD> _offsets = [];
    private readonly Dictionary<SemanticElementId, XElement> _shapePlanes = [];

    private void ReadDiagramData()
    {
        foreach (var diagram in _root.Elements(BpmnDi + "BPMNDiagram"))
        {
            _read.Add(diagram); _ = Attr(diagram, "id");
            foreach (var plane in diagram.Elements(BpmnDi + "BPMNPlane"))
            {
                _read.Add(plane); _ = Attr(plane, "id"); _ = Attr(plane, "bpmnElement");
                foreach (var item in plane.Elements())
                {
                    if (item.Name != BpmnDi + "BPMNShape" && item.Name != BpmnDi + "BPMNEdge") continue;
                    var reference = Reference(item, "bpmnElement", required: false, critical: false);
                    if (reference is null || !_elements.ContainsKey(reference) && !_flows.Any(flow => flow.Id.Value == reference)) continue;
                    _read.Add(item); _ = Attr(item, "id");
                    var id = new SemanticElementId(reference);
                    if (item.Name == BpmnDi + "BPMNShape")
                    {
                        if (ReadBounds(item.Element(Dc + "Bounds"), id.Value) is { IsEmpty: false } bounds)
                        {
                            if (!_bounds.TryAdd(id, bounds)) UnmappedLoss(id.Value, "additional DI shape", "Only the first source-ordered visual representation is retained.");
                            else _shapePlanes.Add(id, plane);
                        }
                    }
                    else
                    {
                        var points = new List<PointD>();
                        foreach (var point in item.Elements(Di + "waypoint"))
                        {
                            Consume(point);
                            if (Number(point, "x") is { } x && Number(point, "y") is { } y) points.Add(new(x, y));
                            else { points.Clear(); break; }
                        }
                        if (points.Count < 2) UnmappedLoss(id.Value, "waypoints", "Invalid or missing DI path ignored; normal routing can reconstruct it.");
                        else if (!_paths.TryAdd(id, [.. points])) UnmappedLoss(id.Value, "additional DI edge", "Only the first source-ordered path is retained.");
                    }
                    if (item.Element(BpmnDi + "BPMNLabel") is { } label)
                    {
                        _read.Add(label); _ = Attr(label, "id");
                        if (ReadBounds(label.Element(Dc + "Bounds"), id.Value) is { } labelBounds) _labels.TryAdd(id, labelBounds);
                    }
                }
            }
        }
    }

    private RectD? ReadBounds(XElement? element, string source)
    {
        if (element is null) return null;
        Consume(element);
        if (Number(element, "x") is { } x && Number(element, "y") is { } y &&
            Number(element, "width") is >= 0 and var width && Number(element, "height") is >= 0 and var height &&
            double.IsFinite(x + width) && double.IsFinite(y + height)) return new(x, y, width, height);
        UnmappedLoss(source, "DI bounds", "Invalid DI bounds ignored; existing layout will supply geometry.");
        return null;
    }

    private static double? Number(XElement element, string name)
    {
        try { var value = XmlConvert.ToDouble((string?)element.Attribute(name) ?? string.Empty); return double.IsFinite(value) ? value : null; }
        catch (FormatException) { return null; }
    }

    private IReadOnlyList<VisualStateSnapshot> CreateVisuals(SemanticModelSnapshot semantic)
    {
        var result = new Dictionary<SemanticElementId, VisualStateSnapshot>();
        foreach (var scope in semantic.EnumerateScopeForest())
        {
            var rectangles = _bounds.Where(pair => _memberships.GetValueOrDefault(pair.Key) == scope.Id).Select(pair => pair.Value).ToArray();
            var offset = rectangles.Length == 0 ? default : new VectorD(Math.Max(0, -rectangles.Min(bounds => bounds.X)), Math.Max(0, -rectangles.Min(bounds => bounds.Y)));
            _offsets.Add(scope.Id, offset);
            if (offset != default) ReportLoss(profile.ImportRules.Transformations[BpmnImportTransformation.DiagramOrigin], scope.Id.Value);
        }
        foreach (var element in semantic.Elements.Where(element => BpmnSemanticTypes.IsFlowNode(element.TypeId)))
        {
            var imported = _bounds.TryGetValue(element.Id, out var bounds);
            var size = imported ? bounds.Size : DefaultSize(element.TypeId);
            var position = imported ? LocalBounds(semantic, element.Id, bounds).TopLeft : new PointD(40, 40);
            var properties = PropertyMap.Empty;
            if (_labels.TryGetValue(element.Id, out var label) && !label.IsEmpty)
                properties = NodeLabelVisualOverride.UpdateProperties(properties, NodeLabelVisualOverride.FromBounds(bounds, label));
            result.Add(element.Id, new(VisualId(element.Id), element.Id, position, size,
                imported ? VisualPlacementMode.Pinned : VisualPlacementMode.Automatic, properties: properties));
            if (!imported) _log.Add(BpmnConversionDisposition.Warning, "BPMN_DI_LAYOUT_REQUIRED", "No usable shape bounds; the existing initial layout will position this node.", element.Id.Value);
        }
        foreach (var element in semantic.Elements.Where(element => element.AttachedToElementId is not null))
        {
            if (!result.TryGetValue(element.Id, out var visual) || !result.TryGetValue(element.AttachedToElementId!, out var owner)) continue;
            var ownerBounds = new RectD(owner.Position.X, owner.Position.Y, owner.Size.Width, owner.Size.Height);
            var center = new PointD(visual.Position.X + visual.Size.Width / 2, visual.Position.Y + visual.Size.Height / 2);
            if (!BoundaryAttachmentPlacement.TryProjectToBoundary(ownerBounds, center, visual.Size, out var placement))
            { _log.Error("BPMN_BOUNDARY_GEOMETRY", "The boundary event cannot be placed on a legal owner boundary.", element.Id.Value); continue; }
            var synchronized = placement!.ResolveBounds(ownerBounds, visual.Size);
            if (_bounds.ContainsKey(element.Id) && synchronized.TopLeft != visual.Position)
                ReportLoss(profile.ImportRules.Transformations[BpmnImportTransformation.BoundaryPosition], element.Id.Value);
            result[element.Id] = new(visual.Id, visual.SemanticElementId, synchronized.TopLeft, visual.Size,
                visual.PlacementMode, properties: visual.Properties, boundaryAttachment: placement);
        }
        foreach (var flow in _flows)
        {
            if (!EnsureEndpoint(flow.SourceId) || !EnsureEndpoint(flow.TargetId)) continue;
            _paths.TryGetValue(flow.Id, out var path);
            var source = AddAnchor(flow, flow.SourceId, ConnectorAnchorRole.Source, path.IsDefaultOrEmpty ? null : path[0]);
            var target = AddAnchor(flow, flow.TargetId, ConnectorAnchorRole.Target, path.IsDefaultOrEmpty ? null : path[^1]);
            var properties = PropertyMap.Empty;
            if (_labels.TryGetValue(flow.Id, out var label) && !path.IsDefaultOrEmpty)
            {
                var middle = BpmnXmlGeometry.PointAt(path, 0.5);
                properties = ConnectorLabelPlacement.UpdateProperties(properties,
                    new(0.5, new VectorD(label.X + label.Width / 2 - middle.X, label.Y + label.Height / 2 - middle.Y)));
                UnmappedLoss(flow.Id.Value, "DI label size", "Label center is retained as a route-relative offset; label dimensions are measured by the native renderer.");
            }
            result[flow.Id] = new(VisualId(flow.Id), flow.Id, default, default, VisualPlacementMode.Automatic,
                properties: properties, sourceAnchorId: source, targetAnchorId: target);
        }
        return [.. result.Values];

        bool EnsureEndpoint(SemanticElementId id)
        {
            if (result.ContainsKey(id)) return true;
            if (!_elements.TryGetValue(id.Value, out var element) || element.TypeId != BpmnSemanticTypes.Participant && !OrganizationalSemantics.IsPool(element))
            { _log.Error("BPMN_VISUAL_ENDPOINT", "A flow endpoint has no usable native visual representation.", id.Value); return false; }
            result.Add(id, new(VisualId(id), id, default, default, VisualPlacementMode.Derived));
            return true;
        }
        ConnectorAnchorId AddAnchor(SemanticRelationshipSnapshot flow, SemanticElementId id, ConnectorAnchorRole role, PointD? point)
        {
            var visual = result[id];
            var rectangle = _bounds.GetValueOrDefault(id, new RectD(visual.Position.X, visual.Position.Y, visual.Size.Width, visual.Size.Height));
            var side = point is { } supplied && !rectangle.IsEmpty ? ConnectorTargetEdgeResolver.Resolve(rectangle, supplied) :
                role == ConnectorAnchorRole.Source ? ConnectorAnchorSide.Right : ConnectorAnchorSide.Left;
            var parameter = point is { } at && !rectangle.IsEmpty ? ConnectorAnchorGeometryResolver.ResolveEdgeParameter(rectangle, side, at) : 0.5;
            parameter = Math.Clamp(parameter, 0.000001, 0.999999);
            var anchor = new ConnectorAnchor(new("bpmn:anchor:" + flow.Id.Value + ":" + role), side, role,
                visual.ConnectorAnchors.Count(anchor => anchor.Side == side), parameter);
            result[id] = new(visual.Id, id, visual.Position, visual.Size, visual.PlacementMode,
                properties: visual.Properties, connectorAnchors: visual.ConnectorAnchors.Add(anchor), boundaryAttachment: visual.BoundaryAttachment);
            if (point is { } original && !rectangle.IsEmpty && ConnectorAnchorGeometryResolver.ResolvePoint(rectangle, side, parameter) != original)
                ReportLoss(profile.ImportRules.Transformations[BpmnImportTransformation.ConnectorEndpoint], flow.Id.Value, deduplicate: false);
            return anchor.Id;
        }
    }

    private RectD LocalBounds(SemanticModelSnapshot semantic, SemanticElementId id, RectD bounds)
    {
        var poolId = OrganizationalSemantics.GetOwningPoolId(semantic, id);
        if (poolId is not null && _bounds.TryGetValue(poolId, out var pool))
        {
            ReportLoss(profile.ImportRules.Transformations[BpmnImportTransformation.PoolLocalCoordinates], poolId.Value, "Pool placement",
                name: Text(_elements[poolId.Value].Properties, OrganizationalSemanticProperties.Name), owner: poolId.Value,
                diagram: semantic.GetScope(id).Id.Value, document: _documentId.Value);
            return new(Math.Max(0, bounds.X - pool.X), Math.Max(0, bounds.Y - pool.Y), bounds.Width, bounds.Height);
        }
        return bounds.Translate(_offsets.GetValueOrDefault(semantic.GetScope(id).Id));
    }

    private async ValueTask<BpmnStandardConversionResult> PrepareAsync(DocumentSnapshot document,
        IConnectorRoutingStatePreparer preparer, CancellationToken cancellationToken)
    {
        var intents = new List<ConnectorRoutingIntent>();
        foreach (var (id, path) in _paths.Where(pair => _flows.Any(flow => flow.Id == pair.Key)))
        {
            var flow = _flows.Single(flow => flow.Id == id);
            var offset = _memberships.TryGetValue(flow.SourceId, out var scope) ? _offsets.GetValueOrDefault(scope) : default;
            intents.Add(ConnectorRoutingIntent.Initialize(VisualId(id), ConnectorRoutingType.Manual));
            intents.Add(ConnectorRoutingIntent.ReplaceManualDefinition(VisualId(id), path.Skip(1).Take(path.Length - 2).Select(point => point + offset)));
        }
        var messages = document.SemanticModel.Relationships.Where(flow => flow.TypeId == BpmnSemanticTypes.MessageFlow).Select(flow => flow.Id).ToHashSet();
        // A2 adds Message Flows after initial process layout. Follow that same preparation
        // order: the standard layout algorithm accepts Sequence Flows, not Message Flows.
        // Both snapshots remain private conversion candidates; no editor or History is involved.
        var initial = messages.Count == 0 ? document : new DocumentSnapshot(new SemanticModelSnapshot(document.DocumentId, document.Revision,
            document.SemanticModel.Elements, document.SemanticModel.Relationships.Where(flow => !messages.Contains(flow.Id)),
            document.SemanticModel.NestedScopes, document.SemanticModel.ScopeMemberships, document.SemanticModel.ModelProfiles, document.SemanticModel.ProfileAssignments),
            new VisualModelSnapshot(document.DocumentId, document.Revision, document.VisualModel.VisualStates.Where(visual => !messages.Contains(visual.SemanticElementId)),
                document.VisualModel.ProfileElementPresentations), document.Metadata);
        var messageVisuals = messages.Select(VisualId).ToHashSet();
        var prepared = await PreparePathsAsync(initial, initial, intents.Where(intent => !messageVisuals.Contains(intent.VisualStateId)),
            ConnectorRoutingPreparationPurpose.InitialConstruction).ConfigureAwait(false);
        if (!prepared.Succeeded)
        {
            foreach (var diagnostic in prepared.Diagnostics) _log.Add(BpmnConversionDisposition.Error, diagnostic.Code, diagnostic.Message, diagnostic.SourceIdentity);
            return _log.Result();
        }
        if (messages.Count > 0)
        {
            var initialPrepared = WithPreparation(initial, prepared);
            prepared = await PreparePathsAsync(initialPrepared, WithPreparation(document, prepared),
                intents.Where(intent => messageVisuals.Contains(intent.VisualStateId)), ConnectorRoutingPreparationPurpose.Mutation).ConfigureAwait(false);
            if (!prepared.Succeeded)
            {
                foreach (var diagnostic in prepared.Diagnostics) _log.Add(BpmnConversionDisposition.Error, diagnostic.Code, diagnostic.Message, diagnostic.SourceIdentity);
                return _log.Result();
            }
        }
        document = WithPreparation(document, prepared);
        // Pool height and common width are authored only through existing preparation intents.
        var heights = prepared.RoutingScopes.SelectMany(scope => scope.Geometry.Regions
            .Where(region => region.ContainerSemanticElementId is { } id && _bounds.ContainsKey(id))
            .Select(region => new SpatialRegionHeightIntent(scope.ScopeId, region.Id, _bounds[region.ContainerSemanticElementId!].Height))).ToArray();
        var widths = prepared.RoutingScopes.Select(scope => (scope.ScopeId, Width: scope.Geometry.Regions
            .Where(region => region.ContainerSemanticElementId is { } id && _bounds.ContainsKey(id))
            .Select(region => _bounds[region.ContainerSemanticElementId!].Width).DefaultIfEmpty().Max()))
            .Where(item => item.Width > 0).Select(item => new SpatialScopeWidthIntent(item.ScopeId, OrganizationalModelProfile.Id, item.Width)).ToArray();
        if (heights.Length > 0)
        {
            var sized = await preparer.PrepareAsync(new(document, document, [], heights, widths, null, false), cancellationToken).ConfigureAwait(false);
            if (sized.Succeeded) document = WithPreparation(document, sized);
            else foreach (var diagnostic in sized.Diagnostics)
                UnmappedLoss(diagnostic.SourceIdentity ?? document.DocumentId.Value, "Pool dimensions", "DI Pool dimensions could not be retained: " + diagnostic.Message);
        }
        foreach (var diagnostic in prepared.Diagnostics)
            _log.Add(BpmnConversionDisposition.Warning, diagnostic.Code, diagnostic.Message, diagnostic.SourceIdentity);
        foreach (var (id, path) in _paths)
        {
            var actual = document.VisualModel.EnumerateConnectorRoutingRecords().FirstOrDefault(record => record.VisualStateId == VisualId(id));
            if (actual is { RoutingType: ConnectorRoutingType.Manual })
            {
                if (!actual.Path.SequenceEqual(path)) ReportLoss(profile.ImportRules.Transformations[BpmnImportTransformation.ConnectorPath], id.Value);
            }
        }
        _log.CheckIntegrity(document);
        CheckIndependentDiagramComposition(document);
        return _log.Result(document);

        async ValueTask<ConnectorRoutingStatePreparationResult> PreparePathsAsync(DocumentSnapshot before, DocumentSnapshot proposed,
            IEnumerable<ConnectorRoutingIntent> supplied, ConnectorRoutingPreparationPurpose purpose)
        {
            var routeIntents = supplied.ToArray();
            var result = await preparer.PrepareAsync(new(before, proposed, routeIntents, [], null, false, purpose), cancellationToken).ConfigureAwait(false);
            if (result.Succeeded || routeIntents.Length == 0) return result;
            // DI is optional. An incompatible path must not prevent an otherwise coherent
            // import; ask the same preparer to use its ordinary automatic routing policy.
            foreach (var visual in routeIntents.Select(intent => intent.VisualStateId).Distinct())
                UnmappedLoss(visual.Value, "waypoints", "Imported routing intent was rejected; retrying normal routing. " +
                    string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
            return await preparer.PrepareAsync(new(before, proposed, [], [], null, false, purpose), cancellationToken).ConfigureAwait(false);
        }
    }

    private void CheckIndependentDiagramComposition(DocumentSnapshot document)
    {
        // Independent DI coordinate systems can overlap after composition. Inspect the
        // existing preparation's result; never create a second placement algorithm.
        var geometry = document.VisualModel.RoutingScopes?.FirstOrDefault(scope => scope.ScopeId == RootScope)?.Geometry;
        var envelopes = new Dictionary<SemanticElementId, RectD>();
        if (geometry is not null && BpmnIntegrationPolicy.Projection.Project(document, RootScope).Graph is { } graph)
            foreach (var node in graph.Nodes.Where(node => node.RegionEnvelope is not null))
                envelopes[node.Source.SemanticElementId] = node.RegionEnvelope!.Resolve(geometry.Regions);
        foreach (var element in document.SemanticModel.Elements.Where(element =>
                     BpmnSemanticTypes.IsFlowNode(element.TypeId) && element.AttachedToElementId is null &&
                     document.SemanticModel.GetScope(element.Id).Id == RootScope &&
                     OrganizationalSemantics.GetOwningPoolId(document.SemanticModel, element.Id) is null))
        {
            if (!_shapePlanes.TryGetValue(element.Id, out var plane)) continue;
            var node = geometry?.Nodes.FirstOrDefault(node => node.VisualStateId == VisualId(element.Id));
            var bounds = node?.LocalBounds ?? _bounds[element.Id];
            if (node?.RegionId is { } regionId && geometry?.Regions.FirstOrDefault(region => region.Id == regionId)?.LocalToScopeTransform is { } transform)
                bounds = bounds.Translate(new VectorD(transform.OffsetX, transform.OffsetY));
            foreach (var pool in OrganizationalSemantics.GetPoolsInScope(document, RootScope))
            {
                var sourceId = _shapePlanes.ContainsKey(pool.Id) ? pool.Id : OrganizationalSemantics.GetParticipantId(pool);
                if (sourceId is null || !_shapePlanes.TryGetValue(sourceId, out var poolPlane) || ReferenceEquals(plane, poolPlane)) continue;
                var region = geometry?.Regions.FirstOrDefault(region => region.ContainerSemanticElementId == pool.Id);
                var poolBounds = envelopes.GetValueOrDefault(pool.Id, region?.ContentBounds ?? _bounds.GetValueOrDefault(sourceId));
                if (!bounds.Intersects(poolBounds)) continue;
                _log.Error("BPMN_IMPORT_DI_COMPOSITION_UNSUPPORTED",
                    $"Independent BPMN diagrams overlap in the main diagram: element '{element.Id.Value}' overlaps Pool '{pool.Id.Value}'. Supply non-overlapping DI coordinates; import does not invent a new arrangement.", element.Id.Value);
                return;
            }
        }
    }

    private static DocumentSnapshot WithPreparation(DocumentSnapshot document, ConnectorRoutingStatePreparationResult prepared) =>
        new(document.SemanticModel, new VisualModelSnapshot(document.DocumentId, document.Revision,
            document.VisualModel.VisualStates, document.VisualModel.ProfileElementPresentations, prepared.RoutingScopes, prepared.DocumentRoutingStates), document.Metadata);

    private static VisualStateId VisualId(SemanticElementId id) => new("bpmn:visual:" + id.Value);
    private static SizeD DefaultSize(SemanticTypeId type) => BpmnTaskSemanticTypes.IsTask(type) || type == BpmnSemanticTypes.SubProcess
        ? new(140, 80) : BpmnGatewaySemanticTypes.IsGateway(type) ? new(60, 60) : new(40, 40);
}

/// <summary>Coordinate conversion only; this does not choose or route connector paths.</summary>
internal static class BpmnXmlGeometry
{
    internal static PointD PointAt(IReadOnlyList<PointD> path, double fraction)
    {
        if (path.Count == 0) return default;
        var lengths = Enumerable.Range(1, path.Count - 1).Select(index =>
            Math.Sqrt(Math.Pow(path[index].X - path[index - 1].X, 2) + Math.Pow(path[index].Y - path[index - 1].Y, 2))).ToArray();
        var remaining = lengths.Sum() * fraction;
        for (var index = 0; index < lengths.Length; index++)
        {
            if (remaining <= lengths[index] && lengths[index] > 0)
            {
                var ratio = remaining / lengths[index];
                return new(path[index].X + (path[index + 1].X - path[index].X) * ratio,
                    path[index].Y + (path[index + 1].Y - path[index].Y) * ratio);
            }
            remaining -= lengths[index];
        }
        return path[^1];
    }
}
