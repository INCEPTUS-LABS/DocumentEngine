using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class UpdateConnectionRouteHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(UpdateConnectionRouteCommand.KnownTypeId, new UpdateConnectionRouteHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);

        if (command is not UpdateConnectionRouteCommand update ||
            !before.VisualModel.TryGetVisualState(update.TargetVisualStateId, out var oldState) ||
            oldState is null ||
            !committed.VisualModel.TryGetVisualState(update.TargetVisualStateId, out var newState) ||
            newState is null ||
            oldState.Route.Length == 1 ||
            newState.Route.Length == 1)
        {
            return CommandHistoryPreparationResult.Failure(
            [
                new Diagnostic(
                    HistoryDiagnosticCodes.InvalidPreparation,
                    DiagnosticSeverity.Error,
                    "Connection-route History requires the target Visual state before and after commit.",
                    command.TypeId.Value),
            ]);
        }

        return CommandHistoryPreparationResult.Undoable(
            new UpdateConnectionRouteHistoryCommandFactory(
                update.TargetVisualStateId,
                oldState.Route),
            new UpdateConnectionRouteHistoryCommandFactory(
                update.TargetVisualStateId,
                newState.Route));
    }
}

internal sealed class UpdateConnectionRouteHistoryCommandFactory : IHistoryCommandFactory
{
    private readonly ImmutableArray<PointD> _route;
    private readonly VisualStateId _visualStateId;

    internal UpdateConnectionRouteHistoryCommandFactory(
        VisualStateId visualStateId,
        IEnumerable<PointD> route)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        ArgumentNullException.ThrowIfNull(route);
        var copiedRoute = route.ToImmutableArray();
        if (copiedRoute.Length == 1)
        {
            throw new ArgumentException(
                "A connection-route History factory requires an empty route or at least two points.",
                nameof(route));
        }

        _visualStateId = visualStateId;
        _route = copiedRoute;
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new UpdateConnectionRouteCommand(
            documentId,
            expectedRevision,
            _visualStateId,
            _route);
}
