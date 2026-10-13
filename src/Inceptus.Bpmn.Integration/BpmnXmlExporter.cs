using System.Xml;
using System.Xml.Linq;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Organizational.Semantics;
using static Inceptus.Bpmn.Integration.BpmnXmlVocabulary;

namespace Inceptus.Bpmn.Integration;

internal sealed partial class BpmnXmlExporter
{
    private readonly DocumentSnapshot _document;
    private readonly BpmnConversionLog _log;
    private readonly BpmnXmlIds _ids;
    private readonly XElement _definitions;
    private readonly Dictionary<SemanticElementId, XElement> _elements = [];
    private readonly Dictionary<DocumentScopeId, XElement> _planes = [];
    private readonly List<KeyValuePair<DocumentScopeId, XElement>> _additionalPlanes = [];
    private readonly Dictionary<XElement, XElement> _processCollaborations = [];
    private readonly Dictionary<SemanticElementId, XElement> _participantProcesses = [];

    internal BpmnXmlExporter(DocumentSnapshot document)
    {
        _log = new();
        _document = document;
        _ids = new(document.SemanticModel.Elements.Select(element => element.Id.Value)
            .Concat(document.SemanticModel.Relationships.Select(flow => flow.Id.Value)));
        _definitions = new(Model + "definitions",
            new XAttribute(XNamespace.Xmlns + "bpmn", Model), new XAttribute(XNamespace.Xmlns + "bpmndi", BpmnDi),
            new XAttribute(XNamespace.Xmlns + "di", Di), new XAttribute(XNamespace.Xmlns + "dc", Dc),
            new XAttribute("id", _ids.Generated("definitions:" + document.DocumentId.Value)),
            new XAttribute("targetNamespace", "urn:inceptus:bpmn:" + Uri.EscapeDataString(document.DocumentId.Value)),
            new XAttribute("exporter", "Inceptus.Bpmn.Integration"));
    }

    internal BpmnStandardConversionResult Convert()
    {
        try
        {
            _log.CheckIntegrity(_document);
            if (_log.HasErrors) return _log.Result();
            foreach (var collaboration in _document.SemanticModel.Elements.Where(element => element.TypeId == BpmnSemanticTypes.Collaboration))
            {
                var collaborationXml = NewRoot("collaboration", _ids.Semantic(collaboration.Id.Value));
                Common(collaboration.Id, collaboration.Properties, collaborationXml);
                _elements.Add(collaboration.Id, collaborationXml);
            }
            foreach (var scope in _document.SemanticModel.EnumerateScopeForest().Where(scope => scope.ParentScopeId is null))
                WriteRoot(scope.Id);
            foreach (var participant in _document.SemanticModel.Elements.Where(element => element.TypeId == BpmnSemanticTypes.Participant))
            {
                if (_elements.ContainsKey(participant.Id)) continue;
                var assigned = _document.SemanticModel.Elements.Where(element => OrganizationalSemantics.IsPool(element) &&
                    OrganizationalSemantics.GetParticipantId(element) == participant.Id).ToArray();
                if (assigned.Length > 0 && assigned.All(pool => _document.SemanticModel.GetScope(pool.Id).ParentScopeId is not null))
                    _log.Omit(participant.Id.Value, "Participant grouping", "Participant grouping inside an embedded SubProcess has no standard equivalent; its Pools are exported as lanes.");
                else WriteDetachedParticipant(participant);
            }
            WriteRelationships();
            WriteDiagrams();
            LogNativeMetadata();
            if (_log.HasErrors) return _log.Result();
            // Root elements precede DI according to the BPMN definitions content model.
            var root = new XElement(_definitions.Name, _definitions.Attributes(),
                _definitions.Elements().Where(element => element.Name.Namespace == Model),
                _definitions.Elements().Where(element => element.Name.Namespace != Model));
            var xml = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
            BpmnXmlSchema.Validate(xml, _log);
            return _log.Result(xml: xml.ToString());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or XmlException)
        {
            _log.Error("BPMN_EXPORT_INVALID", exception.Message, _document.DocumentId.Value);
            return _log.Result();
        }
    }

    private void WriteRoot(DocumentScopeId scope)
    {
        var pools = OrganizationalSemantics.GetOrderedPoolsInScope(_document, scope);
        var owners = new Dictionary<SemanticElementId, XElement>();
        XElement? collaboration = null;
        foreach (var pool in pools)
        {
            if (!BpmnEffectiveParticipant.TryResolve(_document, pool.Id, out var owner, out var error))
            { _log.Error(error!.Code, error.Message, pool.Id.Value); continue; }
            collaboration ??= _elements.TryGetValue(new(scope.Value), out var scopeOwner) && scopeOwner.Name == Model + "collaboration" ? scopeOwner : null;
            collaboration ??= CollaborationFor(pools.Select(OrganizationalSemantics.GetParticipantId).OfType<SemanticElementId>())
                ?? NewRoot("collaboration", _ids.Generated("collaboration:" + scope.Value));
            if (!owners.TryGetValue(owner!.Id, out var process))
            {
                if (_participantProcesses.ContainsKey(owner.Id))
                {
                    _log.Error("BPMN_PARTICIPANT_MULTIPLE_DIAGRAMS",
                        "One A2 Participant occurs in multiple native scope diagrams; its process cannot be selected safely.", owner.Id.Value);
                    continue;
                }
                // This is a serialization of explicit A2 ownership, not inferred process membership.
                process = NewRoot("process", _ids.Generated("process:" + scope.Value + ":" + owner.Id.Value));
                process.SetAttributeValue("isExecutable", false);
                owners.Add(owner.Id, process);
                _participantProcesses.Add(owner.Id, process);
                _processCollaborations.Add(process, collaboration);
                var native = _document.SemanticModel.Elements.Single(element => element.Id == owner.Id);
                var participant = new XElement(Model + "participant", new XAttribute("id", _ids.Semantic(owner.Id.Value)),
                    new XAttribute("processRef", (string)process.Attribute("id")!));
                collaboration.Add(participant);
                Common(native.Id, native.Properties, participant, OrganizationalSemantics.IsPool(native));
                _elements.Add(owner.Id, participant);
            }
            if (owner.Kind == EffectiveParticipantKind.Participant)
            {
                var laneSet = process.Element(Model + "laneSet");
                if (laneSet is null) { laneSet = new(Model + "laneSet", new XAttribute("id", _ids.Generated("lanes:" + owner.Id.Value))); process.Add(laneSet); }
                var lane = new XElement(Model + "lane", new XAttribute("id", _ids.Semantic(pool.Id.Value)));
                Common(pool.Id, pool.Properties, lane, organizational: true);
                laneSet.Add(lane);
                _elements.Add(pool.Id, lane);
            }
        }
        XElement? unassigned = null;
        foreach (var node in NodesIn(scope))
        {
            XElement container;
            var pool = OrganizationalSemantics.GetOwningPoolId(_document.SemanticModel, node.Id);
            if (pool is not null && BpmnEffectiveParticipant.TryResolve(_document, node.Id, out var owner, out _) && owners.TryGetValue(owner!.Id, out var owned))
                container = owned;
            else
            {
                unassigned ??= NewRoot("process", _ids.Generated("process:" + scope.Value));
                unassigned.SetAttributeValue("isExecutable", false);
                container = unassigned;
            }
            WriteNode(node, container, 0);
            if (pool is not null && _elements.TryGetValue(pool, out var lane) && lane.Name == Model + "lane" && _elements.ContainsKey(node.Id))
                lane.Add(new XElement(Model + "flowNodeRef", _ids.Semantic(node.Id.Value)));
        }
        if (collaboration is null && unassigned is null)
        { unassigned = NewRoot("process", _ids.Generated("process:" + scope.Value)); unassigned.SetAttributeValue("isExecutable", false); }
        _planes.Add(scope, collaboration ?? unassigned!);
        if (collaboration is not null && unassigned is not null)
        {
            _additionalPlanes.Add(new(scope, unassigned));
            // Unassigned nodes remain a separate process. No graph-based ownership inference.
            _log.Omit(scope.Value, "Unassigned presentation", "Unassigned nodes remain in an independent BPMN process without a fabricated Participant.");
        }
    }

    private IEnumerable<SemanticElementSnapshot> NodesIn(DocumentScopeId scope) =>
        _document.SemanticModel.Elements.Where(element => element.ContainmentKind == SemanticElementContainmentKind.Scope &&
            !OrganizationalSemantics.IsPool(element) && _document.SemanticModel.GetScope(element.Id).Id == scope);

    private void WriteNode(SemanticElementSnapshot node, XElement container, int depth)
    {
        if (depth > MaximumDepth) { _log.Error("BPMN_NESTING_LIMIT", "Subprocess nesting exceeds the supported conversion limit.", node.Id.Value); return; }
        var mapping = Nodes.FirstOrDefault(candidate => candidate.Type == node.TypeId);
        if (mapping is null) { _log.Omit(node.Id.Value, "TypeId", $"Semantic type '{node.TypeId}' has no BPMN mapping."); return; }
        var xml = new XElement(Model + mapping.Element, new XAttribute("id", _ids.Semantic(node.Id.Value)));
        Common(node.Id, node.Properties, xml);
        if (mapping.Element == "boundaryEvent")
        {
            if (node.AttachedToElementId is null || !_document.SemanticModel.TryGetElement(node.AttachedToElementId, out var attached) ||
                !BpmnActivitySemanticTypes.IsActivity(attached!.TypeId) || _document.SemanticModel.GetScope(node.Id).Id != _document.SemanticModel.GetScope(attached.Id).Id)
            { _log.Error("BPMN_BOUNDARY_OWNER_REQUIRED", "A Boundary Event requires a representable Activity owner in the same scope.", node.Id.Value); return; }
            xml.SetAttributeValue("attachedToRef", _ids.Semantic(node.AttachedToElementId.Value));
            xml.SetAttributeValue("cancelActivity", !node.Properties.TryGetValue(BpmnSemanticProperties.CancelActivity, out var cancel) ||
                cancel.Kind != PropertyValueKind.Boolean || cancel.BooleanValue);
        }
        if (mapping.Definition is not null)
        {
            var definition = new XElement(Model + mapping.Definition);
            if (mapping.Definition == "timerEventDefinition") WriteTimer(node, definition);
            xml.Add(definition);
        }
        container.Add(xml);
        _elements.Add(node.Id, xml);
        if (node.TypeId == BpmnSemanticTypes.SubProcess)
        {
            var child = _document.SemanticModel.NestedScopes.SingleOrDefault(scope => scope.OwnerSemanticElementId == node.Id);
            if (child is null) return;
            _planes.Add(child.Id, xml);
            var pools = OrganizationalSemantics.GetOrderedPoolsInScope(_document, child.Id);
            if (!pools.IsEmpty)
            {
                var lanes = new XElement(Model + "laneSet", new XAttribute("id", _ids.Generated("lanes:" + child.Id.Value)));
                foreach (var pool in pools)
                {
                    var lane = new XElement(Model + "lane", new XAttribute("id", _ids.Semantic(pool.Id.Value)));
                    Common(pool.Id, pool.Properties, lane, organizational: true);
                    lanes.Add(lane); _elements.Add(pool.Id, lane);
                }
                xml.Add(lanes);
            }
            foreach (var nested in NodesIn(child.Id))
            {
                WriteNode(nested, xml, depth + 1);
                if (OrganizationalSemantics.GetOwningPoolId(_document.SemanticModel, nested.Id) is { } pool &&
                    _elements.TryGetValue(pool, out var lane) && _elements.ContainsKey(nested.Id))
                    lane.Add(new XElement(Model + "flowNodeRef", _ids.Semantic(nested.Id.Value)));
            }
        }
    }

    private void WriteTimer(SemanticElementSnapshot node, XElement definition)
    {
        var value = Text(node.Properties, BpmnSemanticProperties.TimerDefinition);
        if (string.IsNullOrWhiteSpace(value)) return;
        string? kind = null;
        if (value.StartsWith('P') || value.StartsWith("-P", StringComparison.Ordinal))
        {
            try { _ = XmlConvert.ToTimeSpan(value); kind = "timeDuration"; } catch (FormatException) { }
        }
        else if (value.StartsWith('R') && value.Contains('/', StringComparison.Ordinal)) kind = "timeCycle";
        else
        {
            try { _ = XmlConvert.ToDateTimeOffset(value); kind = "timeDate"; } catch (FormatException) { }
        }
        if (kind is null) _log.Omit(node.Id.Value, BpmnSemanticProperties.TimerDefinition,
            "Untyped timer text cannot be classified as an ISO date, duration or repeating interval; the event kind is retained without a timer expression.");
        else
        {
            definition.Add(new XElement(Model + kind, value));
            _log.Add(BpmnConversionDisposition.Warning, "BPMN_TIMER_LITERAL_CLASSIFIED",
                $"Untyped native timer literal represented as {kind}.", node.Id.Value, BpmnSemanticProperties.TimerDefinition);
        }
    }

    private void WriteDetachedParticipant(SemanticElementSnapshot native)
    {
        var collaboration = CollaborationFor([native.Id]) ?? NewRoot("collaboration", _ids.Generated("collaboration:detached:" + native.Id.Value));
        var participant = new XElement(Model + "participant", new XAttribute("id", _ids.Semantic(native.Id.Value)));
        if (BpmnCollaborationSemantics.TryGetProcessScopeId(native, out var scope) && scope is not null &&
            _planes.TryGetValue(scope, out var process) && process.Name == Model + "process")
            participant.SetAttributeValue("processRef", (string)process.Attribute("id")!);
        collaboration.Add(participant);
        Common(native.Id, native.Properties, participant);
        _elements.Add(native.Id, participant);
    }

    private XElement? CollaborationFor(IEnumerable<SemanticElementId> participants)
    {
        var ids = participants.Select(id => _document.SemanticModel.Elements.Single(element => element.Id == id))
            .Select(element => Text(element.Properties, BpmnSemanticProperties.CollaborationId)).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        return ids.Length == 1 && _elements.TryGetValue(new(ids[0]), out var collaboration) && collaboration.Name == Model + "collaboration"
            ? collaboration : null;
    }

    private void WriteRelationships()
    {
        foreach (var flow in _document.SemanticModel.Relationships)
        {
            if (flow.TypeId != BpmnSemanticTypes.SequenceFlow && flow.TypeId != BpmnSemanticTypes.MessageFlow)
            { _log.Omit(flow.Id.Value, "TypeId", $"Relationship type '{flow.TypeId}' has no BPMN mapping."); continue; }
            if (!_elements.TryGetValue(flow.SourceId, out var source) || !_elements.TryGetValue(flow.TargetId, out var target))
            {
                if (!_elements.ContainsKey(flow.SourceId) && !_elements.ContainsKey(flow.TargetId))
                    _log.Omit(flow.Id.Value, "flow", "Flow between two omitted constructs was omitted with those constructs.");
                else _log.Error("BPMN_FLOW_ENDPOINT_UNREPRESENTABLE", "A retained flow would reference an omitted endpoint.", flow.Id.Value);
                continue;
            }
            XElement? owner;
            if (flow.TypeId == BpmnSemanticTypes.SequenceFlow)
            {
                var errors = BpmnEffectiveParticipant.ValidateSequenceEndpoints(_document, flow.SourceId, flow.TargetId);
                if (!errors.IsEmpty) { foreach (var error in errors) _log.Error(error.Code, error.Message, flow.Id.Value); continue; }
                var sourceNode = _document.SemanticModel.Elements.Single(element => element.Id == flow.SourceId);
                var targetNode = _document.SemanticModel.Elements.Single(element => element.Id == flow.TargetId);
                if (!BpmnSemanticTypes.IsFlowNode(sourceNode.TypeId) || !BpmnSemanticTypes.IsFlowNode(targetNode.TypeId) ||
                    sourceNode.TypeId == BpmnSemanticTypes.EndEvent || targetNode.TypeId == BpmnSemanticTypes.StartEvent ||
                    BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(targetNode.TypeId))
                { _log.Error("BPMN_SEQUENCE_FLOW_DIRECTION", "Sequence Flow endpoint types or direction cannot form a valid BPMN flow.", flow.Id.Value); continue; }
                if (source.Parent != target.Parent || source.Parent?.Name is not { } parent || parent != Model + "process" && parent != Model + "subProcess")
                { _log.Error("BPMN_SEQUENCE_FLOW_OWNERSHIP", "Sequence Flow endpoints do not map to the same BPMN process/SubProcess. No participant membership was inferred.", flow.Id.Value); continue; }
                owner = source.Parent;
            }
            else
            {
                var errors = BpmnMessageFlowSemantics.ValidateEndpoints(_document, flow.SourceId, flow.TargetId);
                if (!errors.IsEmpty) { foreach (var error in errors) _log.Error(error.Code, error.Message, flow.Id.Value); continue; }
                var sourceCollaboration = CollaborationOf(source);
                if (sourceCollaboration is null || sourceCollaboration != CollaborationOf(target))
                { _log.Error("BPMN_MESSAGE_FLOW_OWNERSHIP", "Message Flow endpoints do not have one representable Collaboration.", flow.Id.Value); continue; }
                var sourceProcess = ProcessOf(source);
                if (sourceProcess is not null && sourceProcess == ProcessOf(target))
                { _log.Error("BPMN_MESSAGE_FLOW_OWNERSHIP", "Message Flow endpoints would belong to the same exported process.", flow.Id.Value); continue; }
                owner = sourceCollaboration;
            }
            var xml = new XElement(Model + (flow.TypeId == BpmnSemanticTypes.SequenceFlow ? "sequenceFlow" : "messageFlow"),
                new XAttribute("id", _ids.Semantic(flow.Id.Value)), new XAttribute("sourceRef", (string)source.Attribute("id")!),
                new XAttribute("targetRef", (string)target.Attribute("id")!));
            Common(flow.Id, flow.Properties, xml);
            owner!.Add(xml);
            _elements.Add(flow.Id, xml);
        }
    }

    private XElement? CollaborationOf(XElement element) => element.Name == Model + "participant" ? element.Parent :
        element.Ancestors(Model + "process").FirstOrDefault() is { } process ? _processCollaborations.GetValueOrDefault(process) : null;

    private XElement? ProcessOf(XElement element) => element.Name == Model + "participant"
        ? _definitions.Elements(Model + "process").FirstOrDefault(process => (string?)process.Attribute("id") == (string?)element.Attribute("processRef"))
        : element.Ancestors(Model + "process").FirstOrDefault();

    private XElement NewRoot(string name, string id)
    { var element = new XElement(Model + name, new XAttribute("id", id)); _definitions.Add(element); return element; }

    private void Common(SemanticElementId id, PropertyMap properties, XElement xml, bool organizational = false)
    {
        var nameKey = organizational ? OrganizationalSemanticProperties.Name : BpmnSemanticProperties.Name;
        var descriptionKey = organizational ? OrganizationalSemanticProperties.Description : BpmnSemanticProperties.Description;
        if (Text(properties, nameKey) is { } name) xml.SetAttributeValue("name", name);
        if (Text(properties, descriptionKey) is { } description) xml.Add(new XElement(Model + "documentation", description));
        foreach (var entry in properties)
        {
            if ((entry.Key == nameKey || entry.Key == descriptionKey) && entry.Value.Kind == PropertyValueKind.Text ||
                entry.Key == BpmnSemanticProperties.CancelActivity && entry.Value.Kind == PropertyValueKind.Boolean && xml.Name == Model + "boundaryEvent" ||
                entry.Key == BpmnSemanticProperties.TimerDefinition && entry.Value.Kind == PropertyValueKind.Text &&
                    _document.SemanticModel.TryGetElement(id, out var timer) && Nodes.Any(mapping => mapping.Type == timer!.TypeId && mapping.Definition == "timerEventDefinition") ||
                entry.Key == BpmnSemanticProperties.CollaborationId && entry.Value.Kind == PropertyValueKind.Text && (string?)xml.Parent?.Attribute("id") == _ids.Semantic(entry.Value.TextValue) ||
                entry.Key == BpmnSemanticProperties.ProcessScopeId && xml.Attribute("processRef") is not null ||
                entry.Key == OrganizationalSemanticProperties.ParticipantId) continue;
            _log.Omit(id.Value, entry.Key, "Native property has no exported BPMN equivalent; A2 Pool membership supplies Participant ownership.");
        }
        if (_ids.Semantic(id.Value) != id.Value)
            _log.Add(BpmnConversionDisposition.Omitted, "BPMN_ID_ENCODED", "Original native identity cannot be retained as an XML ID; a collision-free encoded ID is used consistently for references.",
                id.Value, "id", _ids.Semantic(id.Value));
    }

    private void LogNativeMetadata()
    {
        foreach (var element in _document.SemanticModel.Elements.Where(element => !_elements.ContainsKey(element.Id)))
            _log.Omit(element.Id.Value, "Semantic content", "This native element and its properties have no retained standard representation.");
        foreach (var entry in _document.Metadata.SystemManagedProperties.Concat(_document.Metadata.ExtensionProperties))
            _log.Omit(_document.DocumentId.Value, entry.Key, "Native document metadata is not included in standard BPMN XML.");
        _log.Omit(_document.DocumentId.Value, "Revision", "Native revision and prepared runtime policy state are not BPMN interchange data.");
        if (_document.Publication is { } publication)
        {
            _definitions.SetAttributeValue("name", publication.Title);
            _log.Omit(_document.DocumentId.Value, "Publication.Code/Description", "Native publishing metadata is omitted.");
        }
        foreach (var profile in _document.SemanticModel.ModelProfiles.AvailableProfileIds)
            _log.Omit(_document.DocumentId.Value, profile.Value, "Native profile availability is not a BPMN property; representable Organizational membership is converted separately.");
        foreach (var assignment in _document.SemanticModel.ProfileAssignments.Where(assignment => assignment.ProfileId != Inceptus.DocumentEngine.Organizational.Profiles.OrganizationalModelProfile.Id))
            _log.Omit(assignment.SemanticElementId.Value, assignment.ProfileId.Value, "Non-Organizational profile assignment has no BPMN representation.");
    }
}
