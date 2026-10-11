using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

internal sealed class BpmnElementTypeCommandHandler : ICommandHandler, ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not ChangeBpmnElementTypeCommand change ||
            !document.SemanticModel.TryGetElement(change.ElementId, out var element) || element is null)
        {
            return [BpmnDiagnostics.Error(BpmnCommandDiagnosticCodes.ElementTypeSwitchInvalid,
                "Type switching requires an existing BPMN element, not a relationship.", command.TypeId.Value)];
        }

        if (!BpmnElementTypeSwitching.CanSwitch(element.TypeId, change.TargetSemanticTypeId))
        {
            return [BpmnDiagnostics.Error(BpmnCommandDiagnosticCodes.ElementTypeSwitchInvalid,
                "The current and requested concrete types must belong to the same supported BPMN switch group.",
                element.Id.Value)];
        }

        if (element.TypeId == change.TargetSemanticTypeId)
        {
            return [BpmnDiagnostics.Error(BpmnCommandDiagnosticCodes.ElementTypeSwitchUnchanged,
                "The element already has the requested concrete type.", element.Id.Value)];
        }

        var diagnostics = new List<Diagnostic>();
        if (element.ContainmentKind != SemanticElementContainmentKind.Scope ||
            !document.SemanticModel.TryGetScope(element.Id, out _))
        {
            diagnostics.Add(BpmnDiagnostics.Error(BpmnCommandDiagnosticCodes.ElementTargetScopeInvalid,
                "A switchable BPMN flow node must belong to an existing process scope.", element.Id.Value));
        }

        ValidateAttachment(document, element, diagnostics);
        var proposed = ReplaceType(document, change);
        foreach (var flow in proposed.SemanticModel.Relationships.Where(flow =>
                     flow.TypeId == BpmnSemanticTypes.SequenceFlow &&
                     (flow.SourceId == element.Id || flow.TargetId == element.Id)))
        {
            diagnostics.AddRange(BpmnSequenceFlowCreationValidation.ValidateEndpointSemantics(
                proposed, flow.Id, flow.SourceId, flow.TargetId, replacedRelationshipId: flow.Id));
        }

        foreach (var flow in proposed.SemanticModel.Relationships.Where(flow => flow.TypeId == BpmnSemanticTypes.MessageFlow &&
                     (flow.SourceId == element.Id || flow.TargetId == element.Id)))
        {
            if (!BpmnMessageFlowSemantics.ValidateEndpoints(proposed, flow.SourceId, flow.TargetId).IsEmpty)
                diagnostics.Add(BpmnMessageFlowSemantics.Error(BpmnMessageFlowSemantics.TypeChangeInvalid,
                    "The requested element type would invalidate an existing Message Flow endpoint.", flow.Id));
        }

        return diagnostics.Distinct().ToImmutableArray();
    }

    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command, DocumentSnapshot document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = Validate(command, document);
        return ValueTask.FromResult(diagnostics.IsEmpty
            ? CommandHandlerResult.Success(ReplaceType(document, (ChangeBpmnElementTypeCommand)command))
            : CommandHandlerResult.Failure(diagnostics));
    }

    private static void ValidateAttachment(
        DocumentSnapshot document, SemanticElementSnapshot element, List<Diagnostic> diagnostics)
    {
        if (!BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(element.TypeId))
        {
            if (element.AttachedToElementId is not null)
            {
                diagnostics.Add(BpmnDiagnostics.Error(BpmnCommandDiagnosticCodes.ElementTypeSwitchInvalid,
                    "An ordinary BPMN flow node cannot have a Boundary Event attachment.", element.Id.Value));
            }

            return;
        }

        if (element.AttachedToElementId is not { } ownerId ||
            !document.SemanticModel.TryGetElement(ownerId, out var owner) || owner is null ||
            !BpmnActivitySemanticTypes.IsActivity(owner.TypeId) ||
            owner.ContainmentKind != SemanticElementContainmentKind.Scope)
        {
            diagnostics.Add(BpmnDiagnostics.Error(BpmnCommandDiagnosticCodes.BoundaryEventAttachmentOwnerInvalid,
                "A Boundary Event must remain attached to an existing scope-contained BPMN Activity.", element.Id.Value));
            return;
        }

        if (!document.SemanticModel.TryGetScope(element.Id, out var eventScope) ||
            !document.SemanticModel.TryGetScope(owner.Id, out var ownerScope) || eventScope?.Id != ownerScope?.Id)
        {
            diagnostics.Add(BpmnDiagnostics.Error(BpmnCommandDiagnosticCodes.BoundaryEventAttachmentScopeMismatch,
                "A Boundary Event and its attached Activity must remain in the same process scope.", element.Id.Value));
        }

        var visuals = document.VisualModel.VisualStates.Where(visual => visual.SemanticElementId == element.Id).ToArray();
        if (visuals.Length != 1 || visuals[0].BoundaryAttachment is null ||
            document.VisualModel.VisualStates.Count(visual => visual.SemanticElementId == owner.Id) != 1)
        {
            diagnostics.Add(BpmnDiagnostics.Error(BpmnCommandDiagnosticCodes.BoundaryEventAttachmentGeometryInvalid,
                "A Boundary Event must retain its visual attachment and one existing Activity Visual State.", element.Id.Value));
        }
    }

    private static DocumentSnapshot ReplaceType(DocumentSnapshot document, ChangeBpmnElementTypeCommand change)
    {
        var semantic = document.SemanticModel;
        var proposed = new SemanticModelSnapshot(document.DocumentId, document.Revision,
            semantic.Elements.Select(element => element.Id == change.ElementId
                ? new SemanticElementSnapshot(element.Id, change.TargetSemanticTypeId,
                    element.Properties, element.AttachedToElementId, element.ContainmentKind)
                : element),
            semantic.Relationships, semantic.NestedScopes, semantic.ScopeMemberships,
            semantic.ModelProfiles, semantic.ProfileAssignments);
        return new DocumentSnapshot(proposed, document.VisualModel, document.Metadata, document.Publication);
    }
}
