using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class MoveVisualStatesHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(MoveVisualStatesCommand.KnownTypeId, new MoveVisualStatesHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);

        if (command is not MoveVisualStatesCommand move)
        {
            return Failure(command);
        }

        var undo = ImmutableArray.CreateBuilder<VisualStateMove>(move.Moves.Length);
        var redo = ImmutableArray.CreateBuilder<VisualStateMove>(move.Moves.Length);
        foreach (var target in move.Moves)
        {
            if (!before.VisualModel.TryGetVisualState(target.VisualStateId, out var oldState) ||
                oldState is null ||
                !committed.VisualModel.TryGetVisualState(target.VisualStateId, out var newState) ||
                newState is null)
            {
                return Failure(command);
            }

            undo.Add(new VisualStateMove(
                target.VisualStateId,
                oldState.Position,
                oldState.PlacementMode));
            redo.Add(new VisualStateMove(
                target.VisualStateId,
                newState.Position,
                newState.PlacementMode));
        }

        return CommandHistoryPreparationResult.Undoable(
            new MoveVisualStatesHistoryCommandFactory(undo.MoveToImmutable()),
            new MoveVisualStatesHistoryCommandFactory(redo.MoveToImmutable()));
    }

    private static CommandHistoryPreparationResult Failure(ICommand command) =>
        CommandHistoryPreparationResult.Failure(
        [
            new Diagnostic(
                HistoryDiagnosticCodes.InvalidPreparation,
                DiagnosticSeverity.Error,
                "Atomic move History requires every target Visual state before and after commit.",
                command.TypeId.Value),
        ]);
}

internal sealed class MoveVisualStatesHistoryCommandFactory : IHistoryCommandFactory
{
    private readonly ImmutableArray<VisualStateMove> _moves;

    internal MoveVisualStatesHistoryCommandFactory(IEnumerable<VisualStateMove> moves)
    {
        ArgumentNullException.ThrowIfNull(moves);
        _moves = moves.ToImmutableArray();
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new MoveVisualStatesCommand(documentId, expectedRevision, _moves);
}
