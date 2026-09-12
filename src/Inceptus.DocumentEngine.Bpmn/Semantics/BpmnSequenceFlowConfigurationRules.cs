using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Semantics;

internal enum BpmnSequenceFlowConfigurationViolationKind
{
    SequenceFlowCrossesScope,
    BoundaryEventIncomingSequenceFlow,
    EventBasedGatewayTargetInvalid,
    EventBasedGatewayMixedMessageReceptionModes,
    EventBasedTargetAdditionalIncoming,
}

internal sealed record BpmnSequenceFlowConfigurationViolation(
    BpmnSequenceFlowConfigurationViolationKind Kind,
    SemanticElementId SourceId,
    SemanticElementId TargetId,
    SemanticElementId? RelationshipId = null);

/// <summary>
/// Owns BPMN structural Sequence Flow rules, including process-scope containment and
/// Event-Based Gateway configuration. The effective message-reception mode is always
/// derived from outgoing target types.
/// Boundary Event attachment is structural element state, never a synthetic Sequence Flow.
/// </summary>
internal static class BpmnSequenceFlowConfigurationRules
{
    internal static bool IsSupportedEventBasedGatewayTarget(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        return BpmnIntermediateEventSemanticTypes.IsCatchEvent(typeId) ||
            typeId == BpmnSemanticTypes.ReceiveTask;
    }

    internal static ImmutableArray<BpmnSequenceFlowConfigurationViolation> Analyze(
        DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Analyze(
            document.SemanticModel,
            document.SemanticModel.Relationships
                .Where(static relationship =>
                    relationship.TypeId == BpmnSemanticTypes.SequenceFlow)
                .Select(static relationship => new Flow(
                    relationship.Id,
                    relationship.SourceId,
                    relationship.TargetId))
                .ToImmutableArray());
    }

    internal static ImmutableArray<BpmnSequenceFlowConfigurationViolation> AnalyzeScope(
        DocumentSnapshot document,
        DocumentScopeId scopeId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scopeId);
        return Analyze(
            document.SemanticModel,
            document.SemanticModel.Relationships
                .Where(relationship =>
                    relationship.TypeId == BpmnSemanticTypes.SequenceFlow &&
                    document.SemanticModel.TryGetScope(
                        relationship.SourceId,
                        out var sourceScope) &&
                    sourceScope?.Id == scopeId &&
                    document.SemanticModel.TryGetScope(
                        relationship.TargetId,
                        out var targetScope) &&
                    targetScope?.Id == scopeId)
                .Select(static relationship => new Flow(
                    relationship.Id,
                    relationship.SourceId,
                    relationship.TargetId))
                .ToImmutableArray());
    }

    internal static ImmutableArray<BpmnSequenceFlowConfigurationViolation>
        AnalyzeCandidate(
            DocumentSnapshot document,
            SemanticElementId relationshipId,
            SemanticElementId sourceId,
            SemanticElementId targetId,
            SemanticElementId? replacedRelationshipId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(relationshipId);
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(targetId);

        var flows = document.SemanticModel.Relationships
            .Where(relationship =>
                relationship.TypeId == BpmnSemanticTypes.SequenceFlow &&
                relationship.Id != replacedRelationshipId)
            .Select(static relationship => new Flow(
                relationship.Id,
                relationship.SourceId,
                relationship.TargetId))
            .Append(new Flow(relationshipId, sourceId, targetId))
            .ToImmutableArray();
        return Analyze(
                document.SemanticModel,
                flows)
            .Where(violation =>
                violation.Kind ==
                    BpmnSequenceFlowConfigurationViolationKind.SequenceFlowCrossesScope
                    ? violation.RelationshipId == relationshipId
                    : violation.RelationshipId == relationshipId ||
                        violation.SourceId == sourceId ||
                        violation.TargetId == targetId)
            .ToImmutableArray();
    }

    private static ImmutableArray<BpmnSequenceFlowConfigurationViolation> Analyze(
        Contracts.Semantics.SemanticModelSnapshot semanticModel,
        ImmutableArray<Flow> flows)
    {
        var elements = semanticModel.Elements.ToDictionary(static element => element.Id);
        var violations = ImmutableArray.CreateBuilder<
            BpmnSequenceFlowConfigurationViolation>();
        foreach (var flow in flows.OrderBy(static flow => flow.Id.Value, StringComparer.Ordinal))
        {
            if (elements.ContainsKey(flow.SourceId) &&
                elements.ContainsKey(flow.TargetId) &&
                semanticModel.TryGetScope(flow.SourceId, out var sourceScope) &&
                sourceScope is not null &&
                semanticModel.TryGetScope(flow.TargetId, out var targetScope) &&
                targetScope is not null &&
                sourceScope.Id != targetScope.Id)
            {
                violations.Add(new BpmnSequenceFlowConfigurationViolation(
                    BpmnSequenceFlowConfigurationViolationKind.SequenceFlowCrossesScope,
                    flow.SourceId,
                    flow.TargetId,
                    flow.Id));
            }

            if (elements.TryGetValue(flow.TargetId, out var target) &&
                BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(target.TypeId))
            {
                violations.Add(new BpmnSequenceFlowConfigurationViolation(
                    BpmnSequenceFlowConfigurationViolationKind
                        .BoundaryEventIncomingSequenceFlow,
                    flow.SourceId,
                    flow.TargetId,
                    flow.Id));
            }
        }

        var eventBasedGateways = elements.Values
            .Where(static element =>
                element.TypeId == BpmnSemanticTypes.EventBasedGateway)
            .OrderBy(static element => element.Id.Value, StringComparer.Ordinal);

        foreach (var gateway in eventBasedGateways)
        {
            var outgoing = flows
                .Where(flow => flow.SourceId == gateway.Id)
                .OrderBy(static flow => flow.Id.Value, StringComparer.Ordinal)
                .ToArray();
            var hasMessageCatchEvent = false;
            var hasReceiveTask = false;
            SemanticElementId? firstReceiveFlowId = null;
            SemanticElementId? firstReceiveTargetId = null;
            foreach (var flow in outgoing)
            {
                if (!elements.TryGetValue(flow.TargetId, out var target) ||
                    !IsSupportedEventBasedGatewayTarget(target.TypeId))
                {
                    violations.Add(new BpmnSequenceFlowConfigurationViolation(
                        BpmnSequenceFlowConfigurationViolationKind
                            .EventBasedGatewayTargetInvalid,
                        gateway.Id,
                        flow.TargetId,
                        flow.Id));
                    continue;
                }

                hasMessageCatchEvent |= target.TypeId == BpmnSemanticTypes.MessageCatchEvent;
                if (target.TypeId == BpmnSemanticTypes.ReceiveTask)
                {
                    hasReceiveTask = true;
                    firstReceiveFlowId ??= flow.Id;
                    firstReceiveTargetId ??= flow.TargetId;
                }
            }

            if (hasMessageCatchEvent && hasReceiveTask)
            {
                violations.Add(new BpmnSequenceFlowConfigurationViolation(
                    BpmnSequenceFlowConfigurationViolationKind
                        .EventBasedGatewayMixedMessageReceptionModes,
                    gateway.Id,
                    firstReceiveTargetId!,
                    firstReceiveFlowId));
            }
        }

        var eventBasedTargetIds = flows
            .Where(flow =>
                elements.TryGetValue(flow.SourceId, out var source) &&
                source.TypeId == BpmnSemanticTypes.EventBasedGateway &&
                elements.TryGetValue(flow.TargetId, out var target) &&
                IsSupportedEventBasedGatewayTarget(target.TypeId))
            .Select(static flow => flow.TargetId)
            .Distinct()
            .OrderBy(static id => id.Value, StringComparer.Ordinal);
        foreach (var targetId in eventBasedTargetIds)
        {
            var incoming = flows
                .Where(flow => flow.TargetId == targetId)
                .OrderBy(static flow => flow.Id.Value, StringComparer.Ordinal)
                .ToArray();
            if (incoming.Length <= 1)
            {
                continue;
            }

            var eventBasedFlow = incoming.First(flow =>
                elements.TryGetValue(flow.SourceId, out var source) &&
                source.TypeId == BpmnSemanticTypes.EventBasedGateway);
            violations.Add(new BpmnSequenceFlowConfigurationViolation(
                BpmnSequenceFlowConfigurationViolationKind
                    .EventBasedTargetAdditionalIncoming,
                eventBasedFlow.SourceId,
                targetId,
                eventBasedFlow.Id));
        }

        return violations.ToImmutable();
    }

    private sealed record Flow(
        SemanticElementId Id,
        SemanticElementId SourceId,
        SemanticElementId TargetId);
}
