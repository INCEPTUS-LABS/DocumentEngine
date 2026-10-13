using System.Xml;
using System.Xml.Linq;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using static Inceptus.Bpmn.Integration.BpmnXmlVocabulary;

namespace Inceptus.Bpmn.Integration;

internal sealed partial class BpmnXmlImporter(string xml, BpmnImportProfile profile)
{
    private readonly BpmnConversionLog _log = new();
    private readonly HashSet<XObject> _read = [];
    private readonly Dictionary<string, XElement> _xmlIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SemanticElementSnapshot> _elements = new(StringComparer.Ordinal);
    private readonly List<SemanticRelationshipSnapshot> _flows = [];
    private readonly List<DocumentScopeSnapshot> _scopes = [];
    private readonly Dictionary<SemanticElementId, DocumentScopeId> _memberships = [];
    private readonly Dictionary<SemanticElementId, SemanticElementId> _pools = [];
    private readonly List<ModelProfileElementPresentationSnapshot> _poolOrder = [];
    private readonly HashSet<string> _nativeIds = new(StringComparer.Ordinal);
    private readonly HashSet<XElement> _importedProcesses = [];
    private readonly List<XElement> _flowXml = [];
    private DocumentId _documentId = new("bpmn:import");
    private DocumentScopeId RootScope => new(_documentId.Value);
    private XElement _root = null!;
    private int _generated;

    internal BpmnStandardConversionResult Convert()
    {
        var document = ReadDocument();
        if (document is not null) CheckIndependentDiagramComposition(document);
        foreach (var (id, _) in _paths)
            UnmappedLoss(id.Value, "waypoints", "Shape and anchor geometry were imported. Use ImportAsync with an existing routing preparer to retain the DI path; this snapshot will use normal initial routing.");
        return _log.Result(document);
    }

    internal async ValueTask<BpmnStandardConversionResult> ConvertAsync(IConnectorRoutingStatePreparer preparer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = ReadDocument();
        if (document is null || _log.HasErrors) return _log.Result();
        try { return await PrepareAsync(document, preparer, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { _log.Error("BPMN_IMPORT_PREPARATION", exception.Message); return _log.Result(); }
    }

    private DocumentSnapshot? ReadDocument()
    {
        try
        {
            using var input = new StringReader(xml);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
            var parsed = XDocument.Load(reader, LoadOptions.SetLineInfo);
            if (parsed.Root?.Name != Model + "definitions")
            { _log.Error("BPMN_XML_ROOT", "A BPMN definitions element in the BPMN model namespace is required."); return null; }
            _root = parsed.Root;
            if (_root.Descendants().Any(element => element.Ancestors().Take(MaximumDepth + 1).Count() > MaximumDepth))
            { _log.Error("BPMN_NESTING_LIMIT", "XML nesting exceeds the conversion limit."); return null; }
            if (!BpmnXmlSchema.Validate(parsed, _log)) return null;
            foreach (var element in _root.DescendantsAndSelf())
            {
                if (element.Attribute("id") is not { } attribute) continue;
                if (!IsXmlId(attribute.Value) || !_xmlIds.TryAdd(attribute.Value, element))
                { _log.Error("BPMN_XML_ID", "XML IDs must be valid and unique.", attribute.Value.Length > 0 ? attribute.Value : null); return null; }
                _nativeIds.Add(attribute.Value);
            }
            _read.Add(_root);
            _ = Attr(_root, "id");
            _ = Attr(_root, "targetNamespace");
            var processes = _root.Elements(Model + "process").ToArray();
            var collaborations = _root.Elements(Model + "collaboration").ToArray();
            if (!CanComposeMainDiagram(processes, collaborations)) return null;
            var primary = collaborations.FirstOrDefault(element => element.Elements(Model + "participant").Any())
                ?? processes.FirstOrDefault() ?? _root;
            _documentId = new(Id(primary));
            foreach (var collaboration in collaborations) ReadCollaboration(collaboration);
            foreach (var process in processes.Where(process => !_importedProcesses.Contains(process)))
                ReadProcess(process, RootScope, null);
            foreach (var flow in _flowXml) ReadFlow(flow);
            var semantic = new SemanticModelSnapshot(_documentId, DocumentRevision.Zero, _elements.Values, _flows, _scopes,
                _memberships.Where(pair => pair.Value != RootScope).Select(pair => new SemanticElementScopeMembershipSnapshot(pair.Key, pair.Value)),
                _poolOrder.Count > 0 ? new ModelProfileStateSnapshot([OrganizationalModelProfile.Id]) : null,
                _pools.Select(pair => new ModelProfileElementAssignmentSnapshot(OrganizationalModelProfile.Id, pair.Key, pair.Value)));
            ReadDiagramData();
            var visuals = CreateVisuals(semantic);
            var document = new DocumentSnapshot(semantic, new VisualModelSnapshot(_documentId, DocumentRevision.Zero, visuals, _poolOrder),
                new DocumentMetadataSnapshot(_documentId, DocumentRevision.Zero));
            ValidateFlowOwnership(document);
            _log.CheckIntegrity(document);
            LogUnread(_root);
            return _log.HasErrors ? null : document;
        }
        catch (Exception exception) when (exception is XmlException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            _log.Error("BPMN_IMPORT_INVALID", exception.Message);
            return null;
        }
    }

    private void ReadCollaboration(XElement collaboration)
    {
        _read.Add(collaboration);
        var id = Id(collaboration);
        var scope = RootScope;
        var semantic = new SemanticElementSnapshot(new(id), BpmnSemanticTypes.Collaboration, Common(collaboration), containmentKind: SemanticElementContainmentKind.Document);
        _elements.Add(id, semantic);
        foreach (var participant in collaboration.Elements(Model + "participant"))
        {
            _read.Add(participant);
            var participantId = Id(participant);
            var directEndpoint = collaboration.Elements(Model + "messageFlow").Any(flow =>
                Reference(flow, "sourceRef") == participantId || Reference(flow, "targetRef") == participantId);
            var reference = Reference(participant, "processRef", required: false);
            if (reference is null)
            {
                if (directEndpoint)
                {
                    var properties = Common(participant);
                    properties.Add(new(BpmnSemanticProperties.CollaborationId, PropertyValue.FromText(id)));
                    _elements.Add(participantId, new(new(participantId), BpmnSemanticTypes.Participant, properties, containmentKind: SemanticElementContainmentKind.Document));
                    AddPool(Generated("pool:" + participantId), [new(OrganizationalSemanticProperties.ParticipantId, PropertyValue.FromText(participantId))], scope);
                }
                else AddPool(participantId, Common(participant, organizational: true), scope);
                UnmappedLoss(participantId, "black-box", "Black-box Participant represented by an empty A2 Pool, assigned to a Participant for direct endpoints; the native model has no black-box marker.");
                continue;
            }
            if (!_xmlIds.TryGetValue(reference, out var process) || process.Name != Model + "process")
            { _log.Error("BPMN_PROCESS_REF", "Participant processRef must resolve to a process in this document.", participantId); continue; }
            if (!_importedProcesses.Add(process))
            { _log.Error("BPMN_SHARED_PROCESS", "Several Participants reference one process. A2 requires unique explicit Pool membership; duplicating or arbitrarily assigning the process would be misleading.", participantId); continue; }
            var lanes = process.Elements(Model + "laneSet").Elements(Model + "lane").ToArray();
            if (lanes.Length == 0 && !directEndpoint)
            {
                AddPool(participantId, Common(participant, organizational: true), scope);
                ReadProcess(process, scope, new(participantId));
            }
            else
            {
                var properties = Common(participant);
                properties.Add(new(BpmnSemanticProperties.CollaborationId, PropertyValue.FromText(id)));
                _elements.Add(participantId, new(new(participantId), BpmnSemanticTypes.Participant, properties, containmentKind: SemanticElementContainmentKind.Document));
                SemanticElementId? defaultPool = null;
                if (lanes.Length == 0)
                {
                    defaultPool = new(Generated("pool:" + participantId));
                    AddPool(defaultPool.Value, [new(OrganizationalSemanticProperties.ParticipantId, PropertyValue.FromText(participantId))], scope);
                    _log.Add(BpmnConversionDisposition.Warning, "BPMN_PARTICIPANT_POOL_ADAPTED",
                        "Direct Participant endpoints use the existing A2 Participant label and an unnamed assigned Pool.", participantId, target: defaultPool.Value);
                }
                ReadProcess(process, scope, defaultPool, new(participantId));
            }
        }
        _flowXml.AddRange(collaboration.Elements(Model + "messageFlow"));
    }

    private void ReadProcess(XElement process, DocumentScopeId scope, SemanticElementId? defaultPool, SemanticElementId? participant = null)
    {
        _read.Add(process); _ = Id(process); _importedProcesses.Add(process);
        ReadScopeContents(process, scope, defaultPool, participant);
    }

    private void ReadScopeContents(XElement container, DocumentScopeId scope, SemanticElementId? defaultPool, SemanticElementId? participant)
    {
        var laneMembership = new Dictionary<string, SemanticElementId>(StringComparer.Ordinal);
        var lanes = container.Elements(Model + "laneSet").Elements(Model + "lane").ToArray();
        if (lanes.Length > 0)
        {
            if (participant is null)
            {
                participant = new(Generated("participant:" + Id(container)));
                _elements.Add(participant.Value, new(participant, BpmnSemanticTypes.Participant,
                    containmentKind: SemanticElementContainmentKind.Document));
                _log.Add(BpmnConversionDisposition.Warning, "BPMN_LANES_ADAPTED",
                    "Process/SubProcess lanes share one A2 Participant so cross-lane Sequence Flows keep their existing semantics.", Id(container), target: participant.Value);
            }
            foreach (var laneSet in container.Elements(Model + "laneSet")) { _read.Add(laneSet); _ = Attr(laneSet, "id"); }
            foreach (var lane in lanes)
            {
                _read.Add(lane);
                var laneId = new SemanticElementId(Id(lane));
                var properties = Common(lane, organizational: true);
                properties.Add(new(OrganizationalSemanticProperties.ParticipantId, PropertyValue.FromText(participant.Value)));
                AddPool(laneId.Value, properties, scope);
                foreach (var reference in lane.Elements(Model + "flowNodeRef"))
                {
                    Consume(reference);
                    var nodeId = reference.Value.Trim();
                    if (!_xmlIds.TryGetValue(nodeId, out var node) || node.Parent != container)
                    { _log.Error("BPMN_LANE_REFERENCE", "Lane membership must resolve to a flow node in its owning process/SubProcess.", laneId.Value); continue; }
                    if (!laneMembership.TryAdd(nodeId, laneId))
                        _log.Error("BPMN_LANE_MEMBERSHIP", "One flow node belongs to several lanes; A2 membership cannot choose one safely.", nodeId);
                }
            }
        }
        foreach (var child in container.Elements())
        {
            if (child.Name.Namespace != Model || child.Name.LocalName is "laneSet" or "documentation" or "extensionElements") continue;
            if (child.Name == Model + "sequenceFlow") { _flowXml.Add(child); continue; }
            var mapping = ReadMapping(child);
            if (mapping is null) continue;
            _read.Add(child);
            var id = Id(child);
            var properties = Common(child);
            if (mapping.Type != BpmnSemanticTypes.StartEvent && mapping.Type != BpmnSemanticTypes.EndEvent &&
                !properties.Any(entry => entry.Key == BpmnSemanticProperties.Name && !string.IsNullOrWhiteSpace(entry.Value.TextValue)))
            {
                properties.RemoveAll(entry => entry.Key == BpmnSemanticProperties.Name);
                properties.Add(new(BpmnSemanticProperties.Name, PropertyValue.FromText(id)));
                _log.Add(BpmnConversionDisposition.Warning, "BPMN_NAME_DEFAULTED",
                    "The native projection requires a nonempty Name; the source identity is used as its display label.", id, "name");
            }
            if (BpmnTaskSemanticTypes.IsTask(mapping.Type) || BpmnGatewaySemanticTypes.IsGateway(mapping.Type) || mapping.Type == BpmnSemanticTypes.SubProcess)
            {
                properties.Add(new(BpmnSemanticProperties.Code, PropertyValue.FromText(id)));
                if (BpmnTaskSemanticTypes.IsTask(mapping.Type)) properties.Add(new(BpmnSemanticProperties.ElementNumber, PropertyValue.FromInteger(_elements.Count + 1)));
            }
            SemanticElementId? attached = null;
            if (mapping.Element == "boundaryEvent")
            {
                var attachedId = Reference(child, "attachedToRef");
                if (attachedId is not null) attached = new(attachedId);
                var cancel = Attr(child, "cancelActivity");
                properties.Add(new(BpmnSemanticProperties.CancelActivity, PropertyValue.FromBoolean(cancel is null || XmlConvert.ToBoolean(cancel))));
            }
            if (mapping.Definition is not null)
            {
                var definition = child.Element(Model + mapping.Definition)!;
                _read.Add(definition); _ = Attr(definition, "id");
                if (mapping.Definition == "timerEventDefinition")
                {
                    var timer = definition.Elements().FirstOrDefault(element => element.Name.Namespace == Model && element.Name.LocalName is "timeDate" or "timeDuration" or "timeCycle");
                    if (timer is not null)
                    {
                        _read.Add(timer);
                        properties.Add(new(BpmnSemanticProperties.TimerDefinition, PropertyValue.FromText(timer.Value)));
                        UnmappedLoss(id, timer.Name.LocalName, "Timer text is retained; native timers have no separate definition-kind or expression-language field.");
                    }
                }
            }
            var element = new SemanticElementSnapshot(new(id), mapping.Type, properties, attached);
            _elements.Add(id, element); _memberships.Add(element.Id, scope);
            if (attached is null)
            {
                var pool = laneMembership.GetValueOrDefault(id) ?? defaultPool;
                if (pool is null && participant is not null)
                {
                    defaultPool ??= new(Generated("unassigned-lane:" + Id(container)));
                    if (!_elements.ContainsKey(defaultPool.Value))
                    {
                        AddPool(defaultPool.Value, [new(OrganizationalSemanticProperties.ParticipantId, PropertyValue.FromText(participant.Value))], scope);
                        _log.Add(BpmnConversionDisposition.Warning, "BPMN_UNASSIGNED_LANE", "Process-owned nodes outside explicit lanes use an unnamed A2 lane in the same Participant.", Id(container), target: defaultPool.Value);
                    }
                    pool = defaultPool;
                }
                if (pool is not null) _pools.Add(element.Id, pool);
            }
            if (mapping.Type == BpmnSemanticTypes.SubProcess)
            {
                var nested = new DocumentScopeId(Generated("scope:" + id));
                _scopes.Add(new(nested, scope, element.Id));
                ReadScopeContents(child, nested, null, null);
            }
        }
    }

    private Mapping? ReadMapping(XElement element)
    {
        var behavior = profile.ImportRules.Elements.GetValueOrDefault(element.Name)?.Behavior;
        if (behavior?.Action == BpmnImportAction.Ignore) return null;
        if (behavior?.Action == BpmnImportAction.Degrade)
        {
            Report(behavior, element, behavior.TargetValue == "task" ? "activity kind" : "subprocess kind");
            return Nodes.Single(mapping => mapping.Element == behavior.TargetValue);
        }
        var candidates = Nodes.Where(mapping => element.Name == Model + mapping.Element).ToArray();
        if (candidates.Length == 0) return null;
        var mapping = candidates.FirstOrDefault(candidate => candidate.Definition is null) ?? element.Elements()
            .Select(definition => candidates.FirstOrDefault(candidate => candidate.Definition is not null && definition.Name == Model + candidate.Definition)).FirstOrDefault(candidate => candidate is not null);
        // Unsupported definitions on Start/End are ignored and logged as child content;
        // Intermediate/Boundary events require an actual representable event kind.
        return mapping;
    }

    private void ReadFlow(XElement flow)
    {
        _read.Add(flow);
        var id = Id(flow); var source = Reference(flow, "sourceRef"); var target = Reference(flow, "targetRef");
        if (source is null || target is null) return;
        // Main-diagram composition must not erase the source Process/SubProcess boundary.
        if (flow.Name == Model + "sequenceFlow" &&
            (!_xmlIds.TryGetValue(source, out var sourceNode) || sourceNode.Parent != flow.Parent ||
             !_xmlIds.TryGetValue(target, out var targetNode) || targetNode.Parent != flow.Parent))
        { _log.Error("BPMN_SEQUENCE_FLOW_SCOPE", "Sequence Flow endpoints must belong to its source Process/SubProcess.", id); return; }
        if (!_elements.ContainsKey(source) || !_elements.ContainsKey(target))
        {
            if (_xmlIds.ContainsKey(source) && _xmlIds.ContainsKey(target) && !_elements.ContainsKey(source) && !_elements.ContainsKey(target))
                UnmappedLoss(id, "flow", "Flow between two omitted constructs was ignored with those constructs.");
            else _log.Error("BPMN_FLOW_ENDPOINT_UNRESOLVED", "A retained flow endpoint is missing or unsupported; the relationship cannot be reconstructed safely.", id);
            return;
        }
        var type = flow.Name == Model + "sequenceFlow" ? BpmnSemanticTypes.SequenceFlow : BpmnSemanticTypes.MessageFlow;
        _flows.Add(new(new(id), type, new(source), new(target), Common(flow)));
    }

    private void ValidateFlowOwnership(DocumentSnapshot document)
    {
        foreach (var flow in document.SemanticModel.Relationships)
        {
            if (flow.TypeId == BpmnSemanticTypes.MessageFlow)
            {
                foreach (var diagnostic in BpmnMessageFlowSemantics.ValidateEndpoints(document, flow.SourceId, flow.TargetId))
                    _log.Error(diagnostic.Code, diagnostic.Message, flow.Id.Value);
            }
            else
            {
                if (!_memberships.TryGetValue(flow.SourceId, out var sourceScope) || !_memberships.TryGetValue(flow.TargetId, out var targetScope) || sourceScope != targetScope)
                    _log.Error("BPMN_SEQUENCE_FLOW_SCOPE", "Sequence Flow must remain inside one process/SubProcess.", flow.Id.Value);
                foreach (var diagnostic in BpmnEffectiveParticipant.ValidateSequenceEndpoints(document, flow.SourceId, flow.TargetId))
                    _log.Error(diagnostic.Code, diagnostic.Message, flow.Id.Value);
            }
        }
    }

    private bool CanComposeMainDiagram(XElement[] processes, XElement[] collaborations)
    {
        if (collaborations.Length > 1)
            _log.Error("BPMN_IMPORT_MULTIPLE_COLLABORATIONS",
                $"Import cannot continue: this file contains {collaborations.Length} Collaborations ({Describe(collaborations)}). Inceptus imports at most one Collaboration into the main diagram.");
        var assigned = collaborations.SelectMany(collaboration => collaboration.Elements(Model + "participant"))
            .Select(participant => Reference(participant, "processRef", required: false)).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var unassigned = processes.Where(process => process.Attribute("id") is not { } id || !assigned.Contains(id.Value)).ToArray();
        if (unassigned.Length > 1)
            _log.Error("BPMN_IMPORT_MULTIPLE_UNASSIGNED_PROCESSES",
                $"Import cannot continue: this file contains {unassigned.Length} processes without Participant assignment ({Describe(unassigned)}). Inceptus imports at most one such process into the main diagram.");
        return !_log.HasErrors;

        static string Describe(IEnumerable<XElement> elements) => string.Join(", ", elements.Select(element =>
            element.Attribute("name") is { Value.Length: > 0 } name
                ? $"{name.Value} [{(string?)element.Attribute("id") ?? "no ID"}]"
                : (string?)element.Attribute("id") ?? "unnamed"));
    }

    private void AddPool(string id, List<KeyValuePair<string, PropertyValue>> properties, DocumentScopeId scope)
    {
        foreach (var propertyKey in new[] { OrganizationalSemanticProperties.Name, OrganizationalSemanticProperties.Description })
            if (!properties.Any(entry => entry.Key == propertyKey)) properties.Add(new(propertyKey, PropertyValue.FromText(string.Empty)));
        var key = new SemanticElementId(id);
        _elements.Add(id, new(key, OrganizationalSemanticTypes.Pool, properties)); _memberships.Add(key, scope);
        _poolOrder.Add(new(OrganizationalModelProfile.Id, key, _poolOrder.Count));
    }

    private List<KeyValuePair<string, PropertyValue>> Common(XElement element, bool organizational = false)
    {
        var properties = new List<KeyValuePair<string, PropertyValue>>();
        if (Attr(element, "name") is { } name) properties.Add(new(organizational ? OrganizationalSemanticProperties.Name : BpmnSemanticProperties.Name, PropertyValue.FromText(name)));
        var documentation = element.Elements(Model + "documentation").ToArray();
        if (documentation.Length > 0)
        {
            foreach (var item in documentation) { _read.Add(item); if (item.HasElements) UnmappedLoss(Id(element), "documentation markup", "Documentation markup converted to plain text."); foreach (var nested in item.Descendants()) Consume(nested); }
            properties.Add(new(organizational ? OrganizationalSemanticProperties.Description : BpmnSemanticProperties.Description,
                PropertyValue.FromText(string.Join(Environment.NewLine, documentation.Select(item => item.Value)))));
            if (documentation.Length > 1) UnmappedLoss(Id(element), "documentation blocks", "Several documentation blocks combined into one description.");
        }
        foreach (var reference in element.Elements().Where(child => child.Name == Model + "incoming" || child.Name == Model + "outgoing"))
        {
            Consume(reference); // Relationship sourceRef/targetRef remain the authority.
            var key = reference.Name.LocalName == "incoming" ? "targetRef" : "sourceRef";
            if (!_xmlIds.TryGetValue(reference.Value.Trim(), out var flow) || Reference(flow, key, required: false, critical: false) != Id(element))
                UnmappedLoss(Id(element), reference.Name.LocalName, "Redundant flow reference disagrees with the authoritative relationship endpoints and was ignored.");
        }
        return properties;
    }

    private string Id(XElement element)
    {
        if (Attr(element, "id") is { Length: > 0 } id) return id;
        var generated = Generated(element.Name.LocalName);
        element.SetAttributeValue("id", generated); _read.Add(element.Attribute("id")!); _xmlIds.Add(generated, element);
        _log.Add(BpmnConversionDisposition.Warning, "BPMN_ID_GENERATED", "An optional missing XML identity was assigned at the conversion boundary.", generated);
        return generated;
    }
    private string Generated(string basis)
    {
        var value = "bpmn:" + basis;
        while (!_nativeIds.Add(value)) value = "bpmn:" + basis + ":" + (++_generated).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return value;
    }
    private string? Attr(XElement element, string name)
    { var attribute = element.Attribute(name); if (attribute is not null) _read.Add(attribute); return attribute?.Value; }
    private string? Reference(XElement element, string name, bool required = true, bool critical = true)
    {
        var value = Attr(element, name)?.Trim();
        if (string.IsNullOrEmpty(value))
        { if (required) _log.Error("BPMN_REFERENCE_REQUIRED", $"{name} is required.", Id(element)); return null; }
        var colon = value.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0) return value;
        var ns = element.GetNamespaceOfPrefix(value[..colon]);
        if (ns is null || ns.NamespaceName != (string?)_root.Attribute("targetNamespace"))
        {
            if (critical) _log.Error("BPMN_EXTERNAL_REFERENCE", $"{name} refers to an unresolved external namespace.", Id(element));
            else UnmappedLoss(Id(element), name, "Optional DI reference to an external namespace was ignored.");
            return null;
        }
        return value[(colon + 1)..];
    }
    private void Consume(XElement element)
    { foreach (var child in element.DescendantsAndSelf()) { _read.Add(child); foreach (var attribute in child.Attributes()) _read.Add(attribute); } }
    private void LogUnread(XElement element)
    {
        var configured = profile.ImportRules.Elements.GetValueOrDefault(element.Name);
        if (!_read.Contains(element))
        { Report(configured?.Behavior ?? profile.ImportRules.UnmappedElement, element, element.Name.ToString()); return; }
        if (configured is not null)
            foreach (var attributeRule in configured.Attributes.Values.OrderBy(rule => rule.Name, StringComparer.Ordinal))
            {
                var attribute = element.Attribute(attributeRule.Name);
                if (attribute is not null && _read.Contains(attribute)) continue;
                if (!MatchesContext(element, attributeRule.Context)) continue;
                var rule = attributeRule.Resolve(attribute?.Value);
                if (rule is null) continue;
                if (attribute is not null) _read.Add(attribute);
                if (rule.Strategy == BpmnImportStrategy.DerivedFromTopology && MatchesTopology(element, attribute?.Value)) continue;
                Report(rule, element, attributeRule.Name, attribute?.Value);
            }
        foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration && !_read.Contains(attribute) &&
            attribute.Name != XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance") + "schemaLocation" &&
            attribute.Name != XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance") + "noNamespaceSchemaLocation"))
            Report(profile.ImportRules.UnmappedAttribute, element, attribute.Name.ToString(), attribute.Value, $"Unsupported attribute '{attribute.Value}' was ignored.");
        foreach (var child in element.Elements()) LogUnread(child);
    }

    private bool MatchesContext(XElement element, BpmnImportContext context)
    {
        if (context == BpmnImportContext.Any) return true;
        var reference = Reference(element, "bpmnElement", required: false, critical: false);
        return reference is not null && _xmlIds.TryGetValue(reference, out var target) &&
            (target.Name == Model + "participant" || target.Name == Model + "lane");
    }

    private bool MatchesTopology(XElement element, string? direction)
    {
        var id = (string?)element.Attribute("id");
        // Read only the retained authoritative Sequence Flows, as existing BPMN topology
        // rules do. No validation execution, new topology authority or model mutation.
        var incoming = _flows.Count(flow => flow.TypeId == BpmnSemanticTypes.SequenceFlow && flow.TargetId.Value == id);
        var outgoing = _flows.Count(flow => flow.TypeId == BpmnSemanticTypes.SequenceFlow && flow.SourceId.Value == id);
        var derived = (incoming > 1, outgoing > 1) switch
        { (true, true) => "Mixed", (true, false) => "Converging", (false, true) => "Diverging", _ => "Unspecified" };
        return direction == derived;
    }

    private void Report(BpmnImportValueRule rule, XElement element, string property, string? value = null, string? message = null) =>
        ReportLoss(rule, SourceLocation(element), property, element.Name.LocalName, value,
            (string?)element.Attribute("name"), owner: (string?)element.Parent?.Attribute("id"),
            diagram: (string?)element.AncestorsAndSelf(BpmnDi + "BPMNDiagram").FirstOrDefault()?.Attribute("id"),
            document: _documentId.Value, message: message);

    // A diagnostic location for anonymous XML content, never a persistent semantic ID.
    private static string SourceLocation(XElement element) => (string?)element.Attribute("id") ??
        (element.Parent is { } parent ? SourceLocation(parent) + "/" : string.Empty) + element.Name.LocalName +
        "[" + (element.ElementsBeforeSelf(element.Name).Count() + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
}
