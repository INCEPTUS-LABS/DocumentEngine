using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class UpdateNodeLabelVisualOverrideHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(
            UpdateNodeLabelVisualOverrideCommand.KnownTypeId,
            new UpdateNodeLabelVisualOverrideHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);

        if (command is not UpdateNodeLabelVisualOverrideCommand update ||
            !before.VisualModel.TryGetVisualState(
                update.TargetVisualStateId,
                out var oldState) ||
            oldState is null ||
            !committed.VisualModel.TryGetVisualState(
                update.TargetVisualStateId,
                out var newState) ||
            newState is null)
        {
            return Invalid(command);
        }

        var oldIsExplicit = NodeLabelVisualOverride.TryRead(
            oldState.Properties,
            out var oldOverride);
        var newIsExplicit = NodeLabelVisualOverride.TryRead(
            newState.Properties,
            out var newOverride);
        if (newIsExplicit != (update.TargetOverride is not null) ||
            newOverride != update.TargetOverride ||
            oldIsExplicit == newIsExplicit && oldOverride == newOverride)
        {
            return Invalid(command);
        }

        return CommandHistoryPreparationResult.Undoable(
            new UpdateNodeLabelVisualOverrideHistoryCommandFactory(
                update.TargetVisualStateId,
                oldIsExplicit ? oldOverride : null),
            new UpdateNodeLabelVisualOverrideHistoryCommandFactory(
                update.TargetVisualStateId,
                newIsExplicit ? newOverride : null));
    }

    private static CommandHistoryPreparationResult Invalid(ICommand command) =>
        CommandHistoryPreparationResult.Failure(
        [
            new Diagnostic(
                HistoryDiagnosticCodes.InvalidPreparation,
                DiagnosticSeverity.Error,
                "Node-label visual-override History requires different explicit overrides before and after commit.",
                command.TypeId.Value),
        ]);
}

internal sealed class UpdateNodeLabelVisualOverrideHistoryCommandFactory :
    IHistoryCommandFactory
{
    private readonly VisualStateId _visualStateId;
    private readonly NodeLabelVisualOverride? _visualOverride;

    internal UpdateNodeLabelVisualOverrideHistoryCommandFactory(
        VisualStateId visualStateId,
        NodeLabelVisualOverride? visualOverride)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        _visualStateId = visualStateId;
        _visualOverride = visualOverride;
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new UpdateNodeLabelVisualOverrideCommand(
            documentId,
            expectedRevision,
            _visualStateId,
            _visualOverride);
}
