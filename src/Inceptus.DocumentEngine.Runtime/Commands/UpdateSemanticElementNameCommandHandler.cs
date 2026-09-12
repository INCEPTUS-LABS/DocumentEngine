using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class UpdateSemanticElementNameCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (command is not UpdateSemanticElementNameCommand update)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.HandlerFailure,
                    $"Command type '{command.TypeId}' does not use the expected immutable request shape.",
                    command.TypeId.Value),
            ]));
        }

        if (string.IsNullOrWhiteSpace(update.TargetName))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.SemanticNameInvalid,
                    "A Semantic element Name must contain at least one non-whitespace character.",
                    update.TargetSemanticElementId.Value),
            ]));
        }

        if (!document.SemanticModel.TryGetElement(
                update.TargetSemanticElementId,
                out var existing) ||
            existing is null)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.SemanticElementNotFound,
                    $"Semantic element '{update.TargetSemanticElementId}' does not exist.",
                    update.TargetSemanticElementId.Value),
            ]));
        }

        if (!existing.Properties.TryGetValue(update.NamePropertyKey, out var oldName) ||
            oldName.Kind != PropertyValueKind.Text ||
            string.IsNullOrWhiteSpace(oldName.TextValue))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.SemanticNamePropertyUnsupported,
                    $"Semantic element '{update.TargetSemanticElementId}' does not expose an existing nonblank text property '{update.NamePropertyKey}' as an editable name.",
                    update.TargetSemanticElementId.Value),
            ]));
        }

        if (string.Equals(oldName.TextValue, update.TargetName, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.SemanticNameUnchanged,
                    $"Semantic element '{update.TargetSemanticElementId}' already has the requested Name.",
                    update.TargetSemanticElementId.Value),
            ]));
        }

        var replacement = new SemanticElementSnapshot(
            existing.Id,
            existing.TypeId,
            existing.Properties.Select(entry =>
                StringComparer.Ordinal.Equals(entry.Key, update.NamePropertyKey)
                    ? new KeyValuePair<string, PropertyValue>(
                        entry.Key,
                        PropertyValue.FromText(update.TargetName))
                    : entry),
            existing.AttachedToElementId,
            existing.ContainmentKind);
        var proposedSemanticModel = new SemanticModelSnapshot(
            document.DocumentId,
            document.Revision,
            document.SemanticModel.Elements.Select(element =>
                element.Id == replacement.Id ? replacement : element),
            document.SemanticModel.Relationships,
            document.SemanticModel.NestedScopes,
            document.SemanticModel.ScopeMemberships,
            document.SemanticModel.ModelProfiles,
            document.SemanticModel.ProfileAssignments);

        return ValueTask.FromResult(CommandHandlerResult.Success(
            new DocumentSnapshot(
                proposedSemanticModel,
                document.VisualModel,
                document.Metadata,
                document.Publication)));
    }

    private static Diagnostic Error(string code, string message, string sourceIdentity) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity);
}
