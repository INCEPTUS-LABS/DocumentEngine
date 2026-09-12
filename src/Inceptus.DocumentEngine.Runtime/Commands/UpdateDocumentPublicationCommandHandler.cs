using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class UpdateDocumentPublicationCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not UpdateDocumentPublicationCommand update ||
            !update.TryCreatePublication(out var publication))
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                new Diagnostic(
                    CommandExecutionDiagnosticCodes.HandlerFailure,
                    DiagnosticSeverity.Error,
                    "Publication update could not produce a valid immutable Publication snapshot.",
                    command.TypeId.Value),
            ]));
        }

        return ValueTask.FromResult(CommandHandlerResult.Success(
            new DocumentSnapshot(
                document.SemanticModel,
                document.VisualModel,
                document.Metadata,
                publication),
            pipelineInvalidation: PipelineInvalidation.None));
    }
}
