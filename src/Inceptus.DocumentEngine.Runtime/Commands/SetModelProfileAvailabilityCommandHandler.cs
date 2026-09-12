using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class SetModelProfileAvailabilityCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (command is not SetModelProfileAvailabilityCommand update)
        {
            return ValueTask.FromResult(InvalidShape(command));
        }

        var state = document.SemanticModel.ModelProfiles;
        foreach (var change in update.Changes)
        {
            state = state.WithAvailability(change.ProfileId, change.IsAvailable);
        }

        if (state.Equals(document.SemanticModel.ModelProfiles))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                Error(
                    CommandExecutionDiagnosticCodes.ModelProfileAvailabilityUnchanged,
                    "Every requested model profile already has the requested availability.",
                    command.TypeId.Value),
            ]));
        }

        var semanticModel = new SemanticModelSnapshot(
            document.DocumentId,
            document.Revision,
            document.SemanticModel.Elements,
            document.SemanticModel.Relationships,
            document.SemanticModel.NestedScopes,
            document.SemanticModel.ScopeMemberships,
            state,
            document.SemanticModel.ProfileAssignments);
        return ValueTask.FromResult(CommandHandlerResult.Success(
            new DocumentSnapshot(
                semanticModel,
                document.VisualModel,
                document.Metadata,
                document.Publication),
            pipelineInvalidation: PipelineInvalidation.Scene));
    }

    private static CommandHandlerResult InvalidShape(ICommand command) =>
        CommandHandlerResult.Failure(
        [
            Error(
                CommandExecutionDiagnosticCodes.HandlerFailure,
                $"Command type '{command.TypeId}' does not use the expected immutable request shape.",
                command.TypeId.Value),
        ]);

    private static Diagnostic Error(string code, string message, string sourceIdentity) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity);
}
