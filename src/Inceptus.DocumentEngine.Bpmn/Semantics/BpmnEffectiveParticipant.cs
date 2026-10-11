using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Semantics;

public enum EffectiveParticipantKind { StandalonePool, Participant }

/// <summary>A derived communication identity. It is never persisted in the Document.</summary>
public sealed record EffectiveParticipantKey(EffectiveParticipantKind Kind, SemanticElementId Id);

/// <summary>The shared Flow ownership authority: explicit Pool membership and optional Participant assignment.</summary>
public static class BpmnEffectiveParticipant
{
    public const string OwnershipMissing = "BPMN_EFFECTIVE_PARTICIPANT_MISSING";
    public const string AssignmentInvalid = "BPMN_POOL_PARTICIPANT_INVALID";
    public const string SequenceCrossesParticipant = "BPMN_SEQUENCE_FLOW_CROSSES_PARTICIPANT";
    public const string LaneEndpointInvalid = "BPMN_LANE_DIRECT_ENDPOINT_INVALID";

    public static bool TryResolve(DocumentSnapshot document, SemanticElementId endpointId,
        out EffectiveParticipantKey? key, out Diagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(endpointId);
        key = null;
        diagnostic = null;
        if (!document.SemanticModel.TryGetElement(endpointId, out var endpoint) || endpoint is null)
            return Fail(OwnershipMissing, "The Flow endpoint does not exist.", endpointId, out diagnostic);
        if (endpoint.TypeId == BpmnSemanticTypes.Participant)
        {
            key = new(EffectiveParticipantKind.Participant, endpoint.Id);
            return true;
        }
        var poolId = OrganizationalSemantics.GetOwningPoolId(document.SemanticModel, endpoint.Id);
        if (poolId is null || !document.SemanticModel.TryGetElement(poolId, out var pool) ||
            pool is null || !OrganizationalSemantics.IsPool(pool))
            return Fail(OwnershipMissing, "The Flow endpoint requires explicit Pool membership.", endpointId, out diagnostic);
        if (!pool.Properties.ContainsKey(OrganizationalSemanticProperties.ParticipantId))
        {
            key = new(EffectiveParticipantKind.StandalonePool, pool.Id);
            return true;
        }
        var participantId = OrganizationalSemantics.GetParticipantId(pool);
        if (participantId is null || !document.SemanticModel.TryGetElement(participantId, out var participant) ||
            participant?.TypeId != BpmnSemanticTypes.Participant || participant.ContainmentKind != SemanticElementContainmentKind.Document)
            return Fail(AssignmentInvalid, "The Pool must reference an existing Participant or have no assignment.", endpointId, out diagnostic);
        key = new(EffectiveParticipantKind.Participant, participantId);
        return true;

    }

    private static bool Fail(string code, string message, SemanticElementId endpointId, out Diagnostic? diagnostic)
    {
        diagnostic = new(code, DiagnosticSeverity.Error, message, endpointId.Value);
        return false;
    }

    public static ImmutableArray<Diagnostic> ValidateSequenceEndpoints(DocumentSnapshot document,
        SemanticElementId source, SemanticElementId target)
    {
        // Unassigned elements have no Participant boundary. The normal Sequence Flow
        // rules still enforce scope and direction; compare ownership only when both
        // endpoints have explicit Pool membership, validating any assigned side first.
        var firstPool = OrganizationalSemantics.GetOwningPoolId(document.SemanticModel, source);
        var secondPool = OrganizationalSemantics.GetOwningPoolId(document.SemanticModel, target);
        EffectiveParticipantKey? first = null;
        EffectiveParticipantKey? second = null;
        if (firstPool is not null && !TryResolve(document, source, out first, out var firstFailure)) return [firstFailure!];
        if (secondPool is not null && !TryResolve(document, target, out second, out var secondFailure)) return [secondFailure!];
        if (firstPool is null || secondPool is null) return [];
        return first == second ? [] : [new(SequenceCrossesParticipant, DiagnosticSeverity.Error,
            "Sequence Flow endpoints must belong to the same effective Participant.", source.Value)];
    }
}
