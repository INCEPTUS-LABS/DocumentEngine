using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class CreateTopLevelDocumentScopeHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(
            CreateTopLevelDocumentScopeCommand.KnownTypeId,
            new CreateTopLevelDocumentScopeHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        if (command is not CreateTopLevelDocumentScopeCommand create ||
            before.SemanticModel.TryGetScope(create.ScopeId, out _) ||
            !committed.SemanticModel.IsExplicitPeerRoot(create.ScopeId) ||
            committed.SemanticModel.NestedScopes.Length !=
                before.SemanticModel.NestedScopes.Length + 1)
        {
            return CommandHistoryPreparationResult.Failure(
            [
                new Diagnostic(
                    HistoryDiagnosticCodes.InvalidPreparation,
                    DiagnosticSeverity.Error,
                    "Top-level scope History requires exactly one new ownerless peer root.",
                    command.TypeId.Value),
            ]);
        }

        return CommandHistoryPreparationResult.Undoable(
            new Factory(create.ScopeId, shouldExist: false),
            new Factory(create.ScopeId, shouldExist: true));
    }

    private sealed class Factory : IHistoryCommandFactory
    {
        private readonly DocumentScopeId _scopeId;
        private readonly bool _shouldExist;

        internal Factory(DocumentScopeId scopeId, bool shouldExist)
        {
            _scopeId = scopeId;
            _shouldExist = shouldExist;
        }

        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new RestoreTopLevelDocumentScopeCommand(
                documentId,
                expectedRevision,
                _scopeId,
                _shouldExist);
    }
}
