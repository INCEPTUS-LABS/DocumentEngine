using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;

namespace Inceptus.DocumentEngine.Bpmn.History;

internal sealed class BpmnOrganizationalHistoryPolicy<TCommand> : ICommandHistoryPolicy
    where TCommand : BpmnOrganizationalCommand
{
    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);
        if (command is not TCommand ||
            !BpmnOrganizationalCommandSupport.TryCreateProposedModel(
                command,
                before,
                out var expected,
                out _) ||
            expected is null ||
            !SemanticContentMatches(expected, committed) ||
            !before.VisualModel.VisualStates.AsSpan().SequenceEqual(
                committed.VisualModel.VisualStates.AsSpan()) ||
            !BpmnSequenceFlowDeletionHistoryPolicy.ProfileRecordsMatch(before, committed) ||
            !BpmnSequenceFlowDeletionHistoryPolicy.MetadataMatches(before, committed))
        {
            return CommandHistoryPreparationResult.Failure(
            [
                new Diagnostic(
                    BpmnCommandDiagnosticCodes.HistoryInvalid,
                    DiagnosticSeverity.Error,
                    "BPMN Organizational History requires one exact semantic-only mutation.",
                    command.TypeId.Value),
            ]);
        }

        return CommandHistoryPreparationResult.Undoable(
            new BpmnSnapshotRestoreCommandFactory(
                before.SemanticModel,
                before.VisualModel,
                PipelineInvalidation.Scene),
            new BpmnSnapshotRestoreCommandFactory(
                committed.SemanticModel,
                committed.VisualModel,
                PipelineInvalidation.Scene));
    }

    private static bool SemanticContentMatches(
        Contracts.Semantics.SemanticModelSnapshot expected,
        DocumentSnapshot committed) =>
        expected.DocumentId == committed.DocumentId &&
        expected.Elements.AsSpan().SequenceEqual(committed.SemanticModel.Elements.AsSpan()) &&
        expected.Relationships.AsSpan().SequenceEqual(
            committed.SemanticModel.Relationships.AsSpan()) &&
        expected.NestedScopes.AsSpan().SequenceEqual(
            committed.SemanticModel.NestedScopes.AsSpan()) &&
        expected.ScopeMemberships.AsSpan().SequenceEqual(
            committed.SemanticModel.ScopeMemberships.AsSpan()) &&
        expected.ModelProfiles.Equals(committed.SemanticModel.ModelProfiles) &&
        expected.ProfileAssignments.AsSpan().SequenceEqual(
            committed.SemanticModel.ProfileAssignments.AsSpan());
}
