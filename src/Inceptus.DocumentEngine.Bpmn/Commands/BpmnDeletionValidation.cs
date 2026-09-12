using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

internal sealed class BpmnSequenceFlowDeletionValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document) =>
        command is DeleteBpmnSequenceFlowCommand deletion
            ? BpmnDeletionValidation.Validate(deletion, document)
            : BpmnDeletionValidation.Invalid(
                command,
                "The BPMN Sequence Flow deletion request has an invalid shape.");
}

internal sealed class BpmnFlowNodeDeletionValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document) =>
        command is DeleteBpmnFlowNodeCommand deletion
            ? BpmnDeletionValidation.Validate(deletion, document)
            : BpmnDeletionValidation.Invalid(
                command,
                "The BPMN flow-node deletion request has an invalid shape.");
}

internal static class BpmnDeletionValidation
{
    internal static ImmutableArray<Diagnostic> Validate(
        DeleteBpmnSequenceFlowCommand deletion,
        DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(deletion);
        ArgumentNullException.ThrowIfNull(document);
        if (!document.SemanticModel.TryGetRelationship(
                deletion.RelationshipId,
                out var relationship) ||
            relationship is null ||
            relationship.TypeId != BpmnSemanticTypes.SequenceFlow ||
            !document.VisualModel.TryGetVisualState(
                deletion.ConnectorVisualStateId,
                out var visual) ||
            visual is null ||
            visual.SemanticElementId != relationship.Id ||
            document.VisualModel.VisualStates.Count(candidate =>
                candidate.SemanticElementId == relationship.Id) != 1 ||
            relationship.SourceId != deletion.ExpectedSourceId ||
            relationship.TargetId != deletion.ExpectedTargetId ||
            visual.SourceAnchorId != deletion.ExpectedSourceAnchorId ||
            visual.TargetAnchorId != deletion.ExpectedTargetAnchorId)
        {
            return
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.SequenceFlowDeletionInvalid,
                    $"BPMN Sequence Flow '{deletion.RelationshipId}' no longer has the exact semantic, visual, and endpoint state requested for deletion.",
                    deletion.RelationshipId.Value),
            ];
        }

        return [];
    }

    internal static ImmutableArray<Diagnostic> Validate(
        DeleteBpmnFlowNodeCommand deletion,
        DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(deletion);
        ArgumentNullException.ThrowIfNull(document);
        if (!document.SemanticModel.TryGetElement(deletion.ElementId, out var element) ||
            element is null ||
            !BpmnSemanticTypes.IsFlowNode(element.TypeId) ||
            !document.VisualModel.TryGetVisualState(deletion.VisualStateId, out var visual) ||
            visual is null ||
            visual.SemanticElementId != element.Id ||
            document.VisualModel.VisualStates.Count(candidate =>
                candidate.SemanticElementId == element.Id) != 1)
        {
            return
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.FlowNodeDeletionInvalid,
                    $"Semantic element '{deletion.ElementId}' and Visual State '{deletion.VisualStateId}' are not one current supported BPMN flow node.",
                    deletion.ElementId.Value),
            ];
        }

        if (!BpmnFlowNodeDeletionPlan.TryCreate(
                document,
                element,
                out var deletionPlan,
                out var planFailure) ||
            deletionPlan is null)
        {
            return
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.FlowNodeDeletionInvalid,
                    planFailure ??
                        $"BPMN flow node '{element.Id}' cannot be deleted from its current containment state.",
                    element.Id.Value),
            ];
        }

        if (element.TypeId == BpmnSemanticTypes.SubProcess)
        {
            return [];
        }

        var unsupportedIncident = document.SemanticModel.Relationships.FirstOrDefault(
            relationship =>
                (deletionPlan.RemovedElementIds.Contains(relationship.SourceId) ||
                    deletionPlan.RemovedElementIds.Contains(relationship.TargetId)) &&
                relationship.TypeId != BpmnSemanticTypes.SequenceFlow);
        return unsupportedIncident is null
            ? []
            :
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.FlowNodeDeletionInvalid,
                    $"BPMN flow node '{element.Id}' has unsupported incident relationship '{unsupportedIncident.Id}'.",
                    element.Id.Value),
            ];
    }

    internal static ImmutableArray<Diagnostic> Invalid(ICommand command, string message)
    {
        ArgumentNullException.ThrowIfNull(command);
        return
        [
            BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidCommand,
                message,
                command.TypeId.Value),
        ];
    }
}

internal sealed class BpmnFlowNodeDeletionPlan
{
    private BpmnFlowNodeDeletionPlan(
        ImmutableHashSet<DocumentScopeId> removedScopeIds,
        ImmutableHashSet<SemanticElementId> removedElementIds,
        ImmutableHashSet<SemanticElementId> removedRelationshipIds,
        ImmutableHashSet<SemanticElementId> removedSemanticIds,
        ImmutableArray<VisualStateId> removedNodeVisualStateIds)
    {
        RemovedScopeIds = removedScopeIds;
        RemovedElementIds = removedElementIds;
        RemovedRelationshipIds = removedRelationshipIds;
        RemovedSemanticIds = removedSemanticIds;
        RemovedNodeVisualStateIds = removedNodeVisualStateIds;
    }

    internal ImmutableHashSet<DocumentScopeId> RemovedScopeIds { get; }

    internal ImmutableHashSet<SemanticElementId> RemovedElementIds { get; }

    internal ImmutableHashSet<SemanticElementId> RemovedRelationshipIds { get; }

    internal ImmutableHashSet<SemanticElementId> RemovedSemanticIds { get; }

    internal ImmutableArray<VisualStateId> RemovedNodeVisualStateIds { get; }

    internal static bool TryCreate(
        DocumentSnapshot document,
        SemanticElementSnapshot element,
        out BpmnFlowNodeDeletionPlan? plan,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);

        var removedScopeIds = ImmutableHashSet.CreateBuilder<DocumentScopeId>();
        if (element.TypeId == BpmnSemanticTypes.SubProcess)
        {
            var ownedScopes = document.SemanticModel.NestedScopes
                .Where(scope => scope.OwnerSemanticElementId == element.Id)
                .ToArray();
            if (ownedScopes.Length != 1)
            {
                plan = null;
                failure =
                    $"BPMN SubProcess '{element.Id}' must own exactly one child scope before recursive deletion.";
                return false;
            }

            var ownerScopeId = document.SemanticModel.GetScope(element.Id).Id;
            var childScope = ownedScopes[0];
            if (childScope.ParentScopeId != ownerScopeId)
            {
                plan = null;
                failure =
                    $"BPMN SubProcess '{element.Id}' child scope '{childScope.Id}' does not belong to its parent scope '{ownerScopeId}'.";
                return false;
            }

            removedScopeIds.Add(childScope.Id);
            foreach (var descendant in document.SemanticModel.GetDescendants(childScope.Id))
            {
                removedScopeIds.Add(descendant.Id);
            }
        }

        var removedElementIdsBuilder = document.SemanticModel.ScopeMemberships
            .Where(membership => removedScopeIds.Contains(membership.ScopeId))
            .Select(static membership => membership.SemanticElementId)
            .Append(element.Id)
            .ToImmutableHashSet()
            .ToBuilder();
        foreach (var boundaryEvent in document.SemanticModel.Elements.Where(element =>
                     BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(element.TypeId) &&
                     element.AttachedToElementId is not null))
        {
            if (removedElementIdsBuilder.Contains(boundaryEvent.AttachedToElementId!))
            {
                removedElementIdsBuilder.Add(boundaryEvent.Id);
            }
        }

        var removedElementIds = removedElementIdsBuilder.ToImmutable();
        var removedRelationshipIds = document.SemanticModel.Relationships
            .Where(relationship =>
                removedElementIds.Contains(relationship.SourceId) ||
                removedElementIds.Contains(relationship.TargetId))
            .Select(static relationship => relationship.Id)
            .ToImmutableHashSet();
        var removedSemanticIds = removedElementIds
            .Concat(removedRelationshipIds)
            .ToImmutableHashSet();
        var removedNodeVisualStateIds = document.VisualModel.VisualStates
            .Where(visual => removedElementIds.Contains(visual.SemanticElementId))
            .Select(static visual => visual.Id)
            .ToImmutableArray();

        plan = new BpmnFlowNodeDeletionPlan(
            removedScopeIds.ToImmutable(),
            removedElementIds,
            removedRelationshipIds,
            removedSemanticIds,
            removedNodeVisualStateIds);
        failure = null;
        return true;
    }
}
