using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class UpdateDocumentPublicationHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(
            UpdateDocumentPublicationCommand.KnownTypeId,
            new UpdateDocumentPublicationHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        if (command is not UpdateDocumentPublicationCommand update ||
            !update.TryCreatePublication(out var requested) ||
            !Equals(requested, committed.Publication) ||
            Equals(before.Publication, committed.Publication))
        {
            return CommandHistoryPreparationResult.Failure(
            [
                new Diagnostic(
                    HistoryDiagnosticCodes.InvalidPreparation,
                    DiagnosticSeverity.Error,
                    "Publication History requires one exact complete Publication replacement.",
                    command.TypeId.Value),
            ]);
        }

        return CommandHistoryPreparationResult.Undoable(
            new Factory(before.Publication),
            new Factory(committed.Publication));
    }

    private sealed class Factory(DocumentPublicationSnapshot? publication) :
        IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new UpdateDocumentPublicationCommand(
                documentId,
                expectedRevision,
                publication);
    }
}
