using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

internal sealed class BpmnSequenceFlowDeletionCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not DeleteBpmnSequenceFlowCommand deletion)
        {
            return ValueTask.FromResult(Invalid(command));
        }

        var diagnostics = BpmnDeletionValidation.Validate(deletion, document);
        if (!diagnostics.IsEmpty)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        }

        var semanticModel = new SemanticModelSnapshot(
            document.DocumentId,
            document.Revision,
            document.SemanticModel.Elements,
            document.SemanticModel.Relationships.Where(relationship =>
                relationship.Id != deletion.RelationshipId),
            document.SemanticModel.NestedScopes,
            document.SemanticModel.ScopeMemberships,
            document.SemanticModel.ModelProfiles,
            document.SemanticModel.ProfileAssignments);
        var visualModel = new VisualModelSnapshot(
            document.DocumentId,
            document.Revision,
            document.VisualModel.VisualStates.Where(visual =>
                visual.Id != deletion.ConnectorVisualStateId),
            document.VisualModel.ProfileElementPresentations);
        return ValueTask.FromResult(CommandHandlerResult.Success(
            BpmnDocumentReplacement.Replace(document, semanticModel, visualModel),
            pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout,
            nodeGeometryImpact: NodeGeometryPipelineImpact.PreserveAll));
    }

    private static CommandHandlerResult Invalid(ICommand command) =>
        CommandHandlerResult.Failure(
        [
            BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidCommand,
                "The BPMN Sequence Flow deletion request has an invalid shape.",
                command.TypeId.Value),
        ]);
}

internal sealed class BpmnFlowNodeDeletionCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not DeleteBpmnFlowNodeCommand deletion)
        {
            return ValueTask.FromResult(Invalid(command));
        }

        var diagnostics = BpmnDeletionValidation.Validate(deletion, document);
        if (!diagnostics.IsEmpty)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        }

        _ = document.SemanticModel.TryGetElement(deletion.ElementId, out var element);
        if (element is null ||
            !BpmnFlowNodeDeletionPlan.TryCreate(
                document,
                element,
                out var deletionPlan,
                out _) ||
            deletionPlan is null)
        {
            return ValueTask.FromResult(Invalid(command));
        }

        var semanticModel = new SemanticModelSnapshot(
            document.DocumentId,
            document.Revision,
            document.SemanticModel.Elements.Where(candidate =>
                !deletionPlan.RemovedElementIds.Contains(candidate.Id)),
            document.SemanticModel.Relationships.Where(relationship =>
                !deletionPlan.RemovedRelationshipIds.Contains(relationship.Id)),
            document.SemanticModel.NestedScopes.Where(scope =>
                !deletionPlan.RemovedScopeIds.Contains(scope.Id)),
            document.SemanticModel.ScopeMemberships.Where(membership =>
                !deletionPlan.RemovedElementIds.Contains(membership.SemanticElementId)),
            document.SemanticModel.ModelProfiles,
            document.SemanticModel.ProfileAssignments.Where(assignment =>
                !deletionPlan.RemovedElementIds.Contains(assignment.SemanticElementId) &&
                !deletionPlan.RemovedElementIds.Contains(assignment.ContainerSemanticElementId)));
        var visualModel = new VisualModelSnapshot(
            document.DocumentId,
            document.Revision,
            document.VisualModel.VisualStates.Where(visual =>
                !deletionPlan.RemovedSemanticIds.Contains(visual.SemanticElementId)),
            document.VisualModel.ProfileElementPresentations.Where(presentation =>
                !deletionPlan.RemovedElementIds.Contains(presentation.SemanticElementId)));
        return ValueTask.FromResult(CommandHandlerResult.Success(
            BpmnDocumentReplacement.Replace(document, semanticModel, visualModel),
            pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout,
            nodeGeometryImpact: NodeGeometryPipelineImpact.ForRemovedVisualStates(
                deletionPlan.RemovedNodeVisualStateIds)));
    }

    private static CommandHandlerResult Invalid(ICommand command) =>
        CommandHandlerResult.Failure(
        [
            BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.InvalidCommand,
                "The BPMN flow-node deletion request has an invalid shape.",
                command.TypeId.Value),
        ]);
}

internal sealed class RestoreBpmnDeletionCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        return command is RestoreBpmnDeletionCommand restore
            ? ValueTask.FromResult(CommandHandlerResult.Success(
                BpmnDocumentReplacement.Replace(
                    document,
                    restore.SemanticModel,
                    restore.VisualModel),
                pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout,
                nodeGeometryImpact: restore.NodeGeometryImpact))
            : ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidCommand,
                    "The BPMN deletion History restore request has an invalid shape.",
                    command.TypeId.Value),
            ]));
    }
}
