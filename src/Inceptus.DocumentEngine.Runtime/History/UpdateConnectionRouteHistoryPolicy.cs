using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

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

        if (before.VisualModel.RoutingScopes is not null)
        {
            var oldRoute = SetConnectorRoutingTypeHistoryPolicy.Find(before, update.TargetVisualStateId);
            var newRoute = SetConnectorRoutingTypeHistoryPolicy.Find(committed, update.TargetVisualStateId);
            // Automatic recalculation owns no authored points. Manual edits, including a
            // reset to an empty definition, use the same global chronological History.
            if (oldRoute?.RoutingType != ConnectorRoutingType.Manual ||
                newRoute?.RoutingType != ConnectorRoutingType.Manual ||
                oldRoute.ManualDefinition!.Value.AsSpan().SequenceEqual(newRoute.ManualDefinition!.Value.AsSpan()))
                return CommandHistoryPreparationResult.PreserveExistingHistory();

            var operation = Classify(oldRoute.ManualDefinition.Value.Length, newRoute.ManualDefinition.Value.Length);
            return CommandHistoryPreparationResult.Undoable(
                new UpdateConnectionRouteHistoryCommandFactory(update.TargetVisualStateId, oldRoute.Path, operation),
                new UpdateConnectionRouteHistoryCommandFactory(update.TargetVisualStateId, newRoute.Path, operation));
        }

        return CommandHistoryPreparationResult.Undoable(
            new UpdateConnectionRouteHistoryCommandFactory(
                update.TargetVisualStateId,
                oldState.Route),
            new UpdateConnectionRouteHistoryCommandFactory(
                update.TargetVisualStateId,
                newState.Route));
    }

    private static ConnectorRoutingHistoryOperation Classify(int beforeCount, int afterCount) =>
        afterCount > beforeCount ? ConnectorRoutingHistoryOperation.AddManualRoutePoint :
        afterCount < beforeCount ? ConnectorRoutingHistoryOperation.RemoveManualRoutePoint :
        ConnectorRoutingHistoryOperation.MoveManualRoutePoint;
}

/// <summary>Logical user intent retained only in the session's existing History entries.</summary>
internal enum ConnectorRoutingHistoryOperation
{
    ChangeRoutingMode,
    AddManualRoutePoint,
    MoveManualRoutePoint,
    RemoveManualRoutePoint,
}

internal sealed class UpdateConnectionRouteHistoryCommandFactory : IHistoryCommandFactory
{
    private readonly ImmutableArray<PointD> _route;
    private readonly VisualStateId _visualStateId;

    internal UpdateConnectionRouteHistoryCommandFactory(
        VisualStateId visualStateId,
        IEnumerable<PointD> route,
        ConnectorRoutingHistoryOperation? operation = null)
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
        Operation = operation;
    }

    internal ConnectorRoutingHistoryOperation? Operation { get; }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new UpdateConnectionRouteCommand(
            documentId,
            expectedRevision,
            _visualStateId,
            _route);
}
