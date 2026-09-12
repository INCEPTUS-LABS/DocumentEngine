using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Profiles;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;

namespace Inceptus.DocumentEngine.Bpmn.Validation;

/// <summary>
/// Pure document-level validation for BPMN containment and retained Collaboration data. This is
/// intentionally not an active-scope validation rule: N10.0 has no document-level
/// Issues presentation context.
/// </summary>
public static class BpmnCollaborationStructuralValidator
{
    public static ModelValidationRuleId KnownRuleId { get; } =
        new("bpmn:validation/collaboration-structural");

    public static ImmutableArray<ModelValidationIssue> Validate(DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = ImmutableArray.CreateBuilder<ModelValidationIssue>();
        foreach (var processElement in document.SemanticModel.Elements
                     .Where(static element => BpmnSemanticTypes.IsFlowNode(element.TypeId))
                     .OrderBy(static element => element.Id.Value, StringComparer.Ordinal))
        {
            if (processElement.ContainmentKind != SemanticElementContainmentKind.Scope)
            {
                issues.Add(Error(
                    BpmnModelValidationCodes.ProcessElementContainmentInvalid,
                    $"BPMN process element '{processElement.Id}' must use semantic-scope containment.",
                    ModelValidationTarget.ForSemanticElement(processElement.Id)));
            }
        }

        var validateOrganizationalReferences =
            document.SemanticModel.ModelProfiles.IsAvailable(
                BpmnModelProfiles.OrganizationalId);

        foreach (var collaboration in document.SemanticModel.Elements
                     .Where(static element =>
                         element.TypeId == BpmnSemanticTypes.Collaboration)
                     .OrderBy(static element => element.Id.Value, StringComparer.Ordinal))
        {
            ValidateCollaboration(document, collaboration, issues);
        }

        foreach (var participant in document.SemanticModel.Elements
                     .Where(static element =>
                         element.TypeId == BpmnSemanticTypes.Participant)
                     .OrderBy(static element => element.Id.Value, StringComparer.Ordinal))
        {
            ValidateParticipant(
                document,
                participant,
                validateOrganizationalReferences,
                issues);
        }

        return issues.ToImmutable();
    }

    private static void ValidateCollaboration(
        DocumentSnapshot document,
        SemanticElementSnapshot collaboration,
        ImmutableArray<ModelValidationIssue>.Builder issues)
    {
        var target = ModelValidationTarget.ForSemanticElement(collaboration.Id);
        if (collaboration.ContainmentKind != SemanticElementContainmentKind.Document)
        {
            issues.Add(Error(
                BpmnModelValidationCodes.CollaborationContainmentInvalid,
                $"BPMN Collaboration '{collaboration.Id}' must use document-level containment.",
                target));
        }

        if (collaboration.AttachedToElementId is not null ||
            document.SemanticModel.ScopeMemberships.Any(membership =>
                membership.SemanticElementId == collaboration.Id))
        {
            issues.Add(Error(
                BpmnModelValidationCodes.CollaborationOwnershipContradictory,
                $"BPMN Collaboration '{collaboration.Id}' has contradictory structural ownership.",
                target));
        }
    }

    private static void ValidateParticipant(
        DocumentSnapshot document,
        SemanticElementSnapshot participant,
        bool validateReferences,
        ImmutableArray<ModelValidationIssue>.Builder issues)
    {
        var target = ModelValidationTarget.ForSemanticElement(participant.Id);
        if (participant.ContainmentKind != SemanticElementContainmentKind.Document)
        {
            issues.Add(Error(
                BpmnModelValidationCodes.ParticipantContainmentInvalid,
                $"BPMN Participant '{participant.Id}' must use document-level containment.",
                target));
        }

        if (participant.AttachedToElementId is not null ||
            document.SemanticModel.ScopeMemberships.Any(membership =>
                membership.SemanticElementId == participant.Id))
        {
            issues.Add(Error(
                BpmnModelValidationCodes.ParticipantOwnershipContradictory,
                $"BPMN Participant '{participant.Id}' has contradictory structural ownership.",
                target));
        }

        if (!validateReferences)
        {
            return;
        }

        if (!BpmnCollaborationSemantics.TryGetCollaborationId(
                participant,
                out var collaborationId) ||
            collaborationId is null ||
            !document.SemanticModel.TryGetElement(collaborationId, out var collaboration) ||
            collaboration is null ||
            collaboration.TypeId != BpmnSemanticTypes.Collaboration ||
            collaboration.ContainmentKind != SemanticElementContainmentKind.Document)
        {
            issues.Add(Error(
                BpmnModelValidationCodes.ParticipantCollaborationMissing,
                $"BPMN Participant '{participant.Id}' does not reference an existing document-contained Collaboration.",
                target));
        }

        if (!BpmnCollaborationSemantics.TryGetProcessScopeId(
                participant,
                out var processScopeId))
        {
            issues.Add(Error(
                BpmnModelValidationCodes.ParticipantProcessScopeMissing,
                $"BPMN Participant '{participant.Id}' has a malformed Process scope reference.",
                target));
            return;
        }

        if (processScopeId is null)
        {
            return;
        }

        if (!document.SemanticModel.TryGetScope(processScopeId, out _))
        {
            issues.Add(Error(
                BpmnModelValidationCodes.ParticipantProcessScopeMissing,
                $"BPMN Participant '{participant.Id}' references missing Process scope '{processScopeId}'.",
                target));
        }
        else if (!document.SemanticModel.IsTopLevelScope(processScopeId))
        {
            issues.Add(Error(
                BpmnModelValidationCodes.ParticipantProcessScopeNested,
                $"BPMN Participant '{participant.Id}' references nested Process scope '{processScopeId}'.",
                target));
        }
    }

    private static ModelValidationIssue Error(
        string code,
        string message,
        ModelValidationTarget target) =>
        new(KnownRuleId, ModelValidationSeverity.Error, code, message, target);
}
