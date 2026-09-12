using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class MoveLabelHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(MoveLabelCommand.KnownTypeId, new MoveLabelHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);

        if (command is not MoveLabelCommand move ||
            !before.VisualModel.TryGetVisualState(
                move.TargetVisualStateId,
                out var oldState) ||
            oldState is null ||
            !committed.VisualModel.TryGetVisualState(
                move.TargetVisualStateId,
                out var newState) ||
            newState is null)
        {
            return Invalid(command);
        }

        var oldIsExplicit = ConnectorLabelPlacement.TryRead(
            oldState.Properties,
            out var oldPlacement);
        var newIsExplicit = ConnectorLabelPlacement.TryRead(
            newState.Properties,
            out var newPlacement);
        if (newIsExplicit != (move.TargetPlacement is not null) ||
            newPlacement != move.TargetPlacement ||
            oldIsExplicit == newIsExplicit && oldPlacement == newPlacement ||
            ConnectorLabelPlacement.Resolve(oldState.Properties) ==
                ConnectorLabelPlacement.Resolve(newState.Properties))
        {
            return Invalid(command);
        }

        return CommandHistoryPreparationResult.Undoable(
            new MoveLabelHistoryCommandFactory(
                move.TargetVisualStateId,
                oldIsExplicit ? oldPlacement : null),
            new MoveLabelHistoryCommandFactory(
                move.TargetVisualStateId,
                newIsExplicit ? newPlacement : null));
    }

    private static CommandHistoryPreparationResult Invalid(ICommand command) =>
        CommandHistoryPreparationResult.Failure(
        [
            new Diagnostic(
                HistoryDiagnosticCodes.InvalidPreparation,
                DiagnosticSeverity.Error,
                "Move-label History requires different route-relative placements before and after commit.",
                command.TypeId.Value),
        ]);
}

internal sealed class MoveLabelHistoryCommandFactory : IHistoryCommandFactory
{
    private readonly ConnectorLabelPlacement? _placement;
    private readonly VisualStateId _visualStateId;

    internal MoveLabelHistoryCommandFactory(
        VisualStateId visualStateId,
        ConnectorLabelPlacement? placement)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        _visualStateId = visualStateId;
        _placement = placement;
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new MoveLabelCommand(
            documentId,
            expectedRevision,
            _visualStateId,
            _placement);
}
