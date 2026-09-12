using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class UpdateSemanticElementPropertyCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (command is not UpdateSemanticElementPropertyCommand update)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.HandlerFailure,
                    $"Command type '{command.TypeId}' does not use the expected immutable request shape.",
                    command.TypeId.Value),
            ]));
        }

        document.SemanticModel.TryGetElement(
            update.TargetSemanticElementId,
            out var existingElement);
        document.SemanticModel.TryGetRelationship(
            update.TargetSemanticElementId,
            out var existingRelationship);
        var existingProperties = existingElement?.Properties ??
            existingRelationship?.Properties;
        if (existingProperties is null)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.SemanticElementNotFound,
                    $"Semantic source '{update.TargetSemanticElementId}' does not exist.",
                    update.TargetSemanticElementId.Value),
            ]));
        }

        if (!existingProperties.TryGetValue(update.PropertyKey, out var oldValue) ||
            oldValue.Kind != update.TargetValue.Kind)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.SemanticPropertyUnsupported,
                    $"Semantic source '{update.TargetSemanticElementId}' does not expose an existing {update.TargetValue.Kind} property '{update.PropertyKey}' for typed editing.",
                    update.TargetSemanticElementId.Value),
            ]));
        }

        if (oldValue.Equals(update.TargetValue))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.SemanticPropertyUnchanged,
                    $"Semantic source '{update.TargetSemanticElementId}' already has the requested value for property '{update.PropertyKey}'.",
                    update.TargetSemanticElementId.Value),
            ]));
        }

        var replacementProperties = existingProperties.Select(entry =>
            StringComparer.Ordinal.Equals(entry.Key, update.PropertyKey)
                ? new KeyValuePair<string, PropertyValue>(entry.Key, update.TargetValue)
                : entry);
        SemanticModelSnapshot proposedSemanticModel;
        if (existingElement is not null)
        {
            var replacement = new SemanticElementSnapshot(
                existingElement.Id,
                existingElement.TypeId,
                replacementProperties,
                existingElement.AttachedToElementId,
                existingElement.ContainmentKind);
            proposedSemanticModel = new SemanticModelSnapshot(
                document.DocumentId,
                document.Revision,
                document.SemanticModel.Elements.Select(element =>
                    element.Id == replacement.Id ? replacement : element),
                document.SemanticModel.Relationships,
                document.SemanticModel.NestedScopes,
                document.SemanticModel.ScopeMemberships,
                document.SemanticModel.ModelProfiles,
                document.SemanticModel.ProfileAssignments);
        }
        else
        {
            var relationship = existingRelationship!;
            var replacement = new SemanticRelationshipSnapshot(
                relationship.Id,
                relationship.TypeId,
                relationship.SourceId,
                relationship.TargetId,
                replacementProperties);
            proposedSemanticModel = new SemanticModelSnapshot(
                document.DocumentId,
                document.Revision,
                document.SemanticModel.Elements,
                document.SemanticModel.Relationships.Select(existing =>
                    existing.Id == replacement.Id ? replacement : existing),
                document.SemanticModel.NestedScopes,
                document.SemanticModel.ScopeMemberships,
                document.SemanticModel.ModelProfiles,
                document.SemanticModel.ProfileAssignments);
        }

        return ValueTask.FromResult(CommandHandlerResult.Success(
            new DocumentSnapshot(
                proposedSemanticModel,
                document.VisualModel,
                document.Metadata,
                document.Publication),
            pipelineInvalidation: existingRelationship is null
                ? null
                : CommandPipelineInvalidation.ConnectorOnly));
    }

    private static Diagnostic Error(string code, string message, string sourceIdentity) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity);
}
