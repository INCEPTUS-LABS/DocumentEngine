using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class MoveVisualStateHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(MoveVisualStateCommand.KnownTypeId, new MoveVisualStateHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);

        if (command is not MoveVisualStateCommand move ||
            !before.VisualModel.TryGetVisualState(move.TargetVisualStateId, out var oldState) ||
            oldState is null ||
            !committed.VisualModel.TryGetVisualState(move.TargetVisualStateId, out var newState) ||
            newState is null)
        {
            return CommandHistoryPreparationResult.Failure(
            [
                new Diagnostic(
                    HistoryDiagnosticCodes.InvalidPreparation,
                    DiagnosticSeverity.Error,
                    "Move History requires the target Visual state before and after commit.",
                    command.TypeId.Value),
            ]);
        }

        return CommandHistoryPreparationResult.Undoable(
            new MoveVisualStateHistoryCommandFactory(
                move.TargetVisualStateId,
                oldState.Position,
                oldState.PlacementMode),
            new MoveVisualStateHistoryCommandFactory(
                move.TargetVisualStateId,
                newState.Position,
                newState.PlacementMode));
    }
}

internal sealed class MoveVisualStateHistoryCommandFactory : IHistoryCommandFactory
{
    private readonly VisualPlacementMode _placementMode;
    private readonly PointD _position;
    private readonly VisualStateId _visualStateId;

    internal MoveVisualStateHistoryCommandFactory(
        VisualStateId visualStateId,
        PointD position,
        VisualPlacementMode placementMode)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        _visualStateId = visualStateId;
        _position = position;
        _placementMode = placementMode;
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new MoveVisualStateCommand(
            documentId,
            expectedRevision,
            _visualStateId,
            _position,
            _placementMode);
}
