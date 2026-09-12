using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class SetModelProfileAvailabilityHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(
            SetModelProfileAvailabilityCommand.KnownTypeId,
            new SetModelProfileAvailabilityHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        if (command is not SetModelProfileAvailabilityCommand update)
        {
            return Invalid(command);
        }

        var undo = update.Changes.Select(change => new ModelProfileAvailabilityChange(
            change.ProfileId,
            before.SemanticModel.ModelProfiles.IsAvailable(change.ProfileId)));
        var redo = update.Changes.Select(change => new ModelProfileAvailabilityChange(
            change.ProfileId,
            committed.SemanticModel.ModelProfiles.IsAvailable(change.ProfileId)));
        if (!update.Changes.All(change =>
                committed.SemanticModel.ModelProfiles.IsAvailable(change.ProfileId) ==
                change.IsAvailable) ||
            before.SemanticModel.ModelProfiles.Equals(committed.SemanticModel.ModelProfiles))
        {
            return Invalid(command);
        }

        return CommandHistoryPreparationResult.Undoable(
            new Factory(undo),
            new Factory(redo));
    }

    private static CommandHistoryPreparationResult Invalid(ICommand command) =>
        CommandHistoryPreparationResult.Failure(
        [
            new Diagnostic(
                HistoryDiagnosticCodes.InvalidPreparation,
                DiagnosticSeverity.Error,
                "Model-profile History requires an exact availability change.",
                command.TypeId.Value),
        ]);

    private sealed class Factory : IHistoryCommandFactory
    {
        private readonly ModelProfileAvailabilityChange[] _changes;

        internal Factory(IEnumerable<ModelProfileAvailabilityChange> changes) =>
            _changes = changes.ToArray();

        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new SetModelProfileAvailabilityCommand(documentId, expectedRevision, _changes);
    }
}
