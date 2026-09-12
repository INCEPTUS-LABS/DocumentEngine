using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class UpdateDocumentPublicationCommandValidator : ICommandValidator
{
    internal static CommandValidatorRegistration Registration { get; } =
        new(
            UpdateDocumentPublicationCommand.KnownTypeId,
            new CommandValidatorId("inceptus:validator/update-document-publication"),
            new UpdateDocumentPublicationCommandValidator());

    public ImmutableArray<Diagnostic> Validate(
        ICommand command,
        DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not UpdateDocumentPublicationCommand update)
        {
            return
            [
                Error(
                    CommandExecutionDiagnosticCodes.InvalidCommandStructure,
                    "Publication update requires the registered immutable request shape.",
                    command.TypeId.Value),
            ];
        }

        if (!update.TryCreatePublication(out var publication))
        {
            return
            [
                Error(
                    CommandExecutionDiagnosticCodes.DocumentPublicationInvalid,
                    "Publication requires a lower-case hyphenated Code and a non-empty Title.",
                    command.TypeId.Value),
            ];
        }

        if (Equals(publication, document.Publication))
        {
            return
            [
                Error(
                    CommandExecutionDiagnosticCodes.DocumentPublicationUnchanged,
                    "The Document already has the requested Publication metadata.",
                    command.TypeId.Value),
            ];
        }

        return [];
    }

    private static Diagnostic Error(string code, string message, string sourceIdentity) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity);
}
