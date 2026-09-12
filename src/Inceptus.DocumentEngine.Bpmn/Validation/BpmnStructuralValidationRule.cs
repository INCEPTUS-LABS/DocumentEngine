using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;

namespace Inceptus.DocumentEngine.Bpmn.Validation;

/// <summary>
/// Validates the topology of the currently supported in-memory BPMN subset.
/// </summary>
public sealed class BpmnStructuralValidationRule : IModelValidationRule
{
    public static ModelValidationRuleId KnownRuleId { get; } =
        new("bpmn:validation/structural");

    public ModelValidationRuleId RuleId => KnownRuleId;

    public ImmutableArray<ModelValidationIssue> Validate(ModelValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var document = context.Document;
        var activeScopeId = context.ActiveScopeId;
        var allNodes = document.SemanticModel.Elements
            .Where(static element =>
                BpmnSemanticTypes.IsFlowNode(element.TypeId) &&
                element.ContainmentKind == SemanticElementContainmentKind.Scope)
            .ToArray();
        var allNodesById = allNodes.ToDictionary(static element => element.Id);
        var nodes = allNodes
            .Where(node =>
                document.SemanticModel.GetScope(node.Id).Id ==
                activeScopeId)
            .ToArray();
        var nodesById = nodes.ToDictionary(static element => element.Id);
        var activeNodeIds = nodesById.Keys.ToHashSet();
        var allFlows = document.SemanticModel.Relationships
            .Where(static relationship =>
                relationship.TypeId == BpmnSemanticTypes.SequenceFlow)
            .ToArray();
        var flows = allFlows
            .Where(flow =>
                activeNodeIds.Contains(flow.SourceId) &&
                activeNodeIds.Contains(flow.TargetId))
            .ToArray();
        var outgoing = CreateAdjacency(nodes, flows, static flow => flow.SourceId);
        var incoming = CreateAdjacency(nodes, flows, static flow => flow.TargetId);
        var issues = ImmutableArray.CreateBuilder<ModelValidationIssue>();

        AddScopeOwnershipIssues(document, activeScopeId, nodesById, issues);
        var validBoundaryAttachments = AddBoundaryAttachmentIssues(
            document,
            nodes,
            issues);
        var invalidBoundaryEventIds = nodes
            .Where(static node =>
                BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(node.TypeId))
            .Select(static node => node.Id)
            .Where(id => !validBoundaryAttachments.ContainsKey(id))
            .ToHashSet();

        var starts = nodes
            .Where(static node => node.TypeId == BpmnSemanticTypes.StartEvent)
            .ToArray();
        var ends = nodes
            .Where(static node => node.TypeId == BpmnSemanticTypes.EndEvent)
            .ToArray();

        if (document.SemanticModel.IsTopLevelScope(activeScopeId) && starts.Length == 0)
        {
            issues.Add(Warning(
                BpmnModelValidationCodes.NoStartEvent,
                "The BPMN model has no Start Event.",
                ModelValidationTarget.Document));
        }

        if (document.SemanticModel.IsTopLevelScope(activeScopeId) && ends.Length == 0)
        {
            issues.Add(Warning(
                BpmnModelValidationCodes.NoEndEvent,
                "The BPMN model has no End Event.",
                ModelValidationTarget.Document));
        }

        var isolatedOrdinaryNodes = new HashSet<SemanticElementId>();
        foreach (var node in nodes)
        {
            var incomingCount = incoming[node.Id].Length;
            var outgoingCount = outgoing[node.Id].Length;
            var target = Target(document, node.Id);
            var elementReference =
                BpmnValidationTargetFormatter.FormatElementReference(node);

            if (node.TypeId == BpmnSemanticTypes.StartEvent && outgoingCount == 0)
            {
                issues.Add(Warning(
                    BpmnModelValidationCodes.StartEventNoOutgoing,
                    $"{elementReference} has no outgoing Sequence Flow.",
                    target));
            }

            if (node.TypeId == BpmnSemanticTypes.EndEvent && incomingCount == 0)
            {
                issues.Add(Warning(
                    BpmnModelValidationCodes.EndEventNoIncoming,
                    $"{elementReference} has no incoming Sequence Flow.",
                    target));
            }

            if (IsOrdinaryFlowNode(node.TypeId) &&
                incomingCount == 0 && outgoingCount == 0)
            {
                isolatedOrdinaryNodes.Add(node.Id);
                issues.Add(Warning(
                    BpmnModelValidationCodes.FlowNodeIsolated,
                    $"{elementReference} is isolated from every Sequence Flow.",
                    target));
            }

            if (BpmnSemanticTypes.IsGateway(node.TypeId))
            {
                if (incomingCount == 0)
                {
                    issues.Add(Warning(
                        BpmnModelValidationCodes.GatewayNoIncoming,
                        $"{elementReference} has no incoming Sequence Flow.",
                        target));
                }

                if (outgoingCount == 0)
                {
                    issues.Add(Warning(
                        BpmnModelValidationCodes.GatewayNoOutgoing,
                        $"{elementReference} has no outgoing Sequence Flow.",
                        target));
                }
            }

            if (node.TypeId == BpmnSemanticTypes.EventBasedGateway)
            {
                if (outgoingCount < 2)
                {
                    issues.Add(Warning(
                        BpmnModelValidationCodes.EventBasedGatewayIncomplete,
                        $"{elementReference} has fewer than two outgoing event branches.",
                        target));
                }

            }
        }

        var configurationViolations = BpmnSequenceFlowConfigurationRules
            .Analyze(document)
            .Where(violation =>
                violation.Kind ==
                    BpmnSequenceFlowConfigurationViolationKind.SequenceFlowCrossesScope &&
                ((document.SemanticModel.TryGetScope(
                        violation.SourceId,
                        out var sourceScope) &&
                    sourceScope?.Id == activeScopeId) ||
                 (document.SemanticModel.TryGetScope(
                        violation.TargetId,
                        out var targetScope) &&
                    targetScope?.Id == activeScopeId)))
            .Concat(BpmnSequenceFlowConfigurationRules
                .AnalyzeScope(document, activeScopeId)
                .Where(static violation =>
                    violation.Kind != BpmnSequenceFlowConfigurationViolationKind
                        .SequenceFlowCrossesScope));
        foreach (var violation in configurationViolations)
        {
            allNodesById.TryGetValue(violation.SourceId, out var source);
            allNodesById.TryGetValue(violation.TargetId, out var configurationTarget);
            var sourceReference = source is null
                ? BpmnValidationTargetFormatter.FormatUnknownElementReference(
                    violation.SourceId)
                : BpmnValidationTargetFormatter.FormatElementReference(source);
            var targetReference = configurationTarget is null
                ? BpmnValidationTargetFormatter.FormatUnknownElementReference(
                    violation.TargetId)
                : BpmnValidationTargetFormatter.FormatElementReference(configurationTarget);
            if (violation.Kind ==
                BpmnSequenceFlowConfigurationViolationKind.SequenceFlowCrossesScope)
            {
                var flow = allFlows.Single(candidate =>
                    candidate.Id == violation.RelationshipId);
                issues.Add(new ModelValidationIssue(
                    RuleId,
                    ModelValidationSeverity.Error,
                    BpmnModelValidationCodes.SequenceFlowCrossesScope,
                    $"{BpmnValidationTargetFormatter.FormatSequenceFlowReference(flow)} " +
                    $"connects {sourceReference} to {targetReference} across process " +
                    "scopes; both endpoints must belong to the same process scope.",
                    Target(document, flow.Id),
                    flow.TargetId.Value));
            }
            else if (violation.Kind == BpmnSequenceFlowConfigurationViolationKind
                         .BoundaryEventIncomingSequenceFlow)
            {
                var flow = allFlows.Single(candidate =>
                    candidate.Id == violation.RelationshipId);
                issues.Add(new ModelValidationIssue(
                    RuleId,
                    ModelValidationSeverity.Error,
                    BpmnModelValidationCodes.BoundaryEventIncomingSequenceFlow,
                    $"{BpmnValidationTargetFormatter.FormatSequenceFlowReference(flow)} " +
                    $"targets {targetReference}; a BPMN Boundary Event cannot have " +
                    "an incoming Sequence Flow.",
                    Target(document, flow.Id),
                    flow.TargetId.Value));
            }
            else if (violation.Kind == BpmnSequenceFlowConfigurationViolationKind
                    .EventBasedGatewayTargetInvalid)
            {
                var flow = allFlows.Single(candidate =>
                    candidate.Id == violation.RelationshipId);
                issues.Add(new ModelValidationIssue(
                    RuleId,
                    ModelValidationSeverity.Error,
                    BpmnModelValidationCodes.EventBasedGatewayInvalidTarget,
                    $"{BpmnValidationTargetFormatter.FormatSequenceFlowReference(flow)} " +
                    $"from {sourceReference} to {targetReference} has an invalid " +
                    "target for an Event-Based Gateway.",
                    Target(document, flow.Id),
                    flow.TargetId.Value));
            }
            else if (violation.Kind == BpmnSequenceFlowConfigurationViolationKind
                         .EventBasedGatewayMixedMessageReceptionModes)
            {
                issues.Add(new ModelValidationIssue(
                    RuleId,
                    ModelValidationSeverity.Error,
                    BpmnModelValidationCodes
                        .EventBasedGatewayMixedMessageReceptionModes,
                    $"{sourceReference} mixes Message Catch Event and Receive Task " +
                    "branches; use one message-reception mode per Event-Based Gateway.",
                    Target(document, violation.SourceId)));
            }
            else if (violation.Kind == BpmnSequenceFlowConfigurationViolationKind
                         .EventBasedTargetAdditionalIncoming)
            {
                issues.Add(new ModelValidationIssue(
                    RuleId,
                    ModelValidationSeverity.Error,
                    BpmnModelValidationCodes.EventBasedTargetAdditionalIncoming,
                    $"{targetReference} participates in {sourceReference} and has an " +
                    "additional incoming Sequence Flow.",
                    Target(document, violation.TargetId)));
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported BPMN Sequence Flow violation '{violation.Kind}'.");
            }
        }

        if (starts.Length > 0)
        {
            var structuralOutgoing = CreateAttachmentAdjacency(
                nodes,
                validBoundaryAttachments,
                reverse: false);
            var reachable = Traverse(
                starts.Select(static start => start.Id),
                outgoing,
                static flow => flow.TargetId,
                nodesById,
                structuralOutgoing);
            foreach (var node in nodes)
            {
                if (node.TypeId != BpmnSemanticTypes.StartEvent &&
                    !isolatedOrdinaryNodes.Contains(node.Id) &&
                    !invalidBoundaryEventIds.Contains(node.Id) &&
                    !reachable.Contains(node.Id))
                {
                    issues.Add(Warning(
                        BpmnModelValidationCodes.NodeUnreachableFromStart,
                        $"{BpmnValidationTargetFormatter.FormatElementReference(node)} " +
                        "is not reachable from any Start Event.",
                        Target(document, node.Id)));
                }
            }
        }

        if (ends.Length > 0)
        {
            var structuralIncoming = CreateAttachmentAdjacency(
                nodes,
                validBoundaryAttachments,
                reverse: true);
            var canReachEnd = Traverse(
                ends.Select(static end => end.Id),
                incoming,
                static flow => flow.SourceId,
                nodesById,
                structuralIncoming);
            foreach (var node in nodes)
            {
                if (node.TypeId != BpmnSemanticTypes.EndEvent &&
                    !isolatedOrdinaryNodes.Contains(node.Id) &&
                    !invalidBoundaryEventIds.Contains(node.Id) &&
                    !canReachEnd.Contains(node.Id))
                {
                    issues.Add(Warning(
                        BpmnModelValidationCodes.NodeCannotReachEnd,
                        $"{BpmnValidationTargetFormatter.FormatElementReference(node)} " +
                        "cannot reach any End Event.",
                        Target(document, node.Id)));
                }
            }
        }

        return issues.ToImmutable();
    }

    private Dictionary<SemanticElementId, SemanticElementId>
        AddBoundaryAttachmentIssues(
            Contracts.Documents.DocumentSnapshot document,
            IEnumerable<SemanticElementSnapshot> activeNodes,
            ImmutableArray<ModelValidationIssue>.Builder issues)
    {
        var allElementsById = document.SemanticModel.Elements.ToDictionary(
            static element => element.Id);
        var validAttachments = new Dictionary<SemanticElementId, SemanticElementId>();
        foreach (var boundaryEvent in activeNodes
                     .Where(static node =>
                         BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(node.TypeId))
                     .OrderBy(static node => node.Id.Value, StringComparer.Ordinal))
        {
            var boundaryReference =
                BpmnValidationTargetFormatter.FormatElementReference(boundaryEvent);
            if (boundaryEvent.AttachedToElementId is not { } ownerId)
            {
                issues.Add(Error(
                    BpmnModelValidationCodes.BoundaryEventAttachmentMissing,
                    $"{boundaryReference} has no attached Activity.",
                    Target(document, boundaryEvent.Id)));
                continue;
            }

            if (!allElementsById.TryGetValue(ownerId, out var owner))
            {
                issues.Add(Error(
                    BpmnModelValidationCodes.BoundaryEventAttachmentTargetMissing,
                    $"{boundaryReference} references missing attached Activity " +
                    $"'{ownerId}'.",
                    Target(document, boundaryEvent.Id)));
                continue;
            }

            if (!BpmnActivitySemanticTypes.IsActivity(owner.TypeId))
            {
                var ownerReference = BpmnSemanticTypes.IsFlowNode(owner.TypeId)
                    ? BpmnValidationTargetFormatter.FormatElementReference(owner)
                    : BpmnValidationTargetFormatter.FormatUnknownElementReference(owner.Id);
                issues.Add(Error(
                    BpmnModelValidationCodes.BoundaryEventAttachmentOwnerInvalid,
                    $"{boundaryReference} is attached to {ownerReference}; only a " +
                    "BPMN Activity may own a Boundary Event.",
                    Target(document, boundaryEvent.Id)));
                continue;
            }

            if (!document.SemanticModel.TryGetScope(boundaryEvent.Id, out var boundaryScope) ||
                boundaryScope is null ||
                !document.SemanticModel.TryGetScope(owner.Id, out var ownerScope) ||
                ownerScope is null ||
                boundaryScope.Id != ownerScope.Id)
            {
                issues.Add(Error(
                    BpmnModelValidationCodes.BoundaryEventAttachmentScopeMismatch,
                    $"{boundaryReference} and its attached Activity must belong to " +
                    "the same process scope.",
                    Target(document, boundaryEvent.Id)));
                continue;
            }

            validAttachments.Add(boundaryEvent.Id, owner.Id);
        }

        return validAttachments;
    }

    private void AddScopeOwnershipIssues(
        Contracts.Documents.DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        IReadOnlyDictionary<SemanticElementId, SemanticElementSnapshot> flowNodesById,
        ImmutableArray<ModelValidationIssue>.Builder issues)
    {
        var childScopes = document.SemanticModel.GetChildScopes(activeScopeId);
        var childScopesByOwner = childScopes
            .Where(static scope => scope.OwnerSemanticElementId is not null)
            .ToDictionary(static scope => scope.OwnerSemanticElementId!);

        foreach (var subProcess in flowNodesById.Values
                     .Where(static node => node.TypeId == BpmnSemanticTypes.SubProcess)
                     .OrderBy(static node => node.Id.Value, StringComparer.Ordinal))
        {
            if (childScopesByOwner.ContainsKey(subProcess.Id))
            {
                continue;
            }

            issues.Add(Error(
                BpmnModelValidationCodes.SubProcessChildScopeMissing,
                $"{BpmnValidationTargetFormatter.FormatElementReference(subProcess)} " +
                "does not own a child process scope.",
                Target(document, subProcess.Id)));
        }

        foreach (var childScope in childScopes
                     .OrderBy(static scope => scope.Id.Value, StringComparer.Ordinal))
        {
            if (childScope.ParentScopeId is null ||
                childScope.OwnerSemanticElementId is not { } ownerId ||
                !flowNodesById.TryGetValue(ownerId, out var owner))
            {
                issues.Add(new ModelValidationIssue(
                    RuleId,
                    ModelValidationSeverity.Error,
                    BpmnModelValidationCodes.ProcessScopeOwnerInvalid,
                    $"Nested process scope '{childScope.Id}' must have one containing " +
                    "scope and one owning BPMN SubProcess.",
                    ModelValidationTarget.Document,
                    childScope.Id.Value));
                continue;
            }

            if (owner.TypeId == BpmnSemanticTypes.SubProcess)
            {
                continue;
            }

            issues.Add(new ModelValidationIssue(
                RuleId,
                ModelValidationSeverity.Error,
                BpmnModelValidationCodes.ProcessScopeOwnerInvalid,
                $"{BpmnValidationTargetFormatter.FormatElementReference(owner)} owns " +
                $"process scope '{childScope.Id}'; only SubProcess may own a BPMN " +
                "process scope.",
                Target(document, owner.Id),
                childScope.Id.Value));
        }
    }

    private ModelValidationIssue Warning(
        string code,
        string message,
        ModelValidationTarget target) =>
        new(RuleId, ModelValidationSeverity.Warning, code, message, target);

    private ModelValidationIssue Error(
        string code,
        string message,
        ModelValidationTarget target) =>
        new(RuleId, ModelValidationSeverity.Error, code, message, target);

    private static Dictionary<SemanticElementId, ImmutableArray<SemanticRelationshipSnapshot>>
        CreateAdjacency(
            IEnumerable<SemanticElementSnapshot> nodes,
            IEnumerable<SemanticRelationshipSnapshot> flows,
            Func<SemanticRelationshipSnapshot, SemanticElementId> endpoint)
    {
        var adjacency = nodes.ToDictionary(
            static node => node.Id,
            static _ => ImmutableArray<SemanticRelationshipSnapshot>.Empty);
        foreach (var group in flows
                     .GroupBy(endpoint)
                     .Where(group => adjacency.ContainsKey(group.Key)))
        {
            adjacency[group.Key] = group
                .OrderBy(static flow => flow.Id.Value, StringComparer.Ordinal)
                .ToImmutableArray();
        }

        return adjacency;
    }

    private static HashSet<SemanticElementId> Traverse(
        IEnumerable<SemanticElementId> roots,
        Dictionary<SemanticElementId, ImmutableArray<SemanticRelationshipSnapshot>> adjacency,
        Func<SemanticRelationshipSnapshot, SemanticElementId> next,
        Dictionary<SemanticElementId, SemanticElementSnapshot> nodesById,
        Dictionary<SemanticElementId, ImmutableArray<SemanticElementId>>
            structuralAdjacency)
    {
        var visited = new HashSet<SemanticElementId>();
        var queue = new Queue<SemanticElementId>();
        foreach (var root in roots.OrderBy(static id => id.Value, StringComparer.Ordinal))
        {
            if (visited.Add(root))
            {
                queue.Enqueue(root);
            }
        }

        while (queue.TryDequeue(out var current))
        {
            foreach (var flow in adjacency[current])
            {
                var candidate = next(flow);
                if (nodesById.ContainsKey(candidate) && visited.Add(candidate))
                {
                    queue.Enqueue(candidate);
                }
            }

            foreach (var candidate in structuralAdjacency[current])
            {
                if (nodesById.ContainsKey(candidate) && visited.Add(candidate))
                {
                    queue.Enqueue(candidate);
                }
            }
        }

        return visited;
    }

    private static Dictionary<SemanticElementId, ImmutableArray<SemanticElementId>>
        CreateAttachmentAdjacency(
            IEnumerable<SemanticElementSnapshot> nodes,
            IReadOnlyDictionary<SemanticElementId, SemanticElementId> attachments,
            bool reverse)
    {
        var adjacency = nodes.ToDictionary(
            static node => node.Id,
            static _ => ImmutableArray<SemanticElementId>.Empty);
        foreach (var attachment in attachments.OrderBy(
                     static pair => pair.Key.Value,
                     StringComparer.Ordinal))
        {
            var sourceId = reverse ? attachment.Key : attachment.Value;
            var targetId = reverse ? attachment.Value : attachment.Key;
            adjacency[sourceId] = adjacency[sourceId].Add(targetId);
        }

        return adjacency;
    }

    private static bool IsOrdinaryFlowNode(SemanticTypeId typeId) =>
        BpmnActivitySemanticTypes.IsActivity(typeId) ||
        BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(typeId);

    private static ModelValidationTarget Target(
        Contracts.Documents.DocumentSnapshot document,
        SemanticElementId semanticElementId)
    {
        var visualState = document.VisualModel.VisualStates.FirstOrDefault(
            visual => visual.SemanticElementId == semanticElementId);
        return visualState is null
            ? ModelValidationTarget.ForSemanticElement(semanticElementId)
            : ModelValidationTarget.ForVisualState(semanticElementId, visualState.Id);
    }
}
