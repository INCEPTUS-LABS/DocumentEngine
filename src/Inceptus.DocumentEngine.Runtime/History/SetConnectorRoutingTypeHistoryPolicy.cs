using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class SetConnectorRoutingTypeHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(SetConnectorRoutingTypeCommand.KnownTypeId, new SetConnectorRoutingTypeHistoryPolicy());
    public CommandHistoryPreparationResult Prepare(ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        if (command is not SetConnectorRoutingTypeCommand change ||
            Find(before, change.TargetVisualStateId) is not { } oldRoute ||
            Find(committed, change.TargetVisualStateId) is not { } newRoute)
        {
            return CommandHistoryPreparationResult.Failure([new Diagnostic("INCEPTUS.ROUTING.TYPE.HISTORY.INVALID",
                DiagnosticSeverity.Error, "Routing-type History requires the connector before and after commit.")]);
        }
        return oldRoute.RoutingType == newRoute.RoutingType
            ? CommandHistoryPreparationResult.PreserveExistingHistory()
            : CommandHistoryPreparationResult.Undoable(
                new Factory(change.TargetVisualStateId, oldRoute.RoutingType),
                new Factory(change.TargetVisualStateId, newRoute.RoutingType),
                [new ConnectorRoutingTypeHistoryDelta(change.TargetVisualStateId, oldRoute.RoutingType, newRoute.RoutingType)], []);
    }
    internal static ConnectorRoutingRecord? Find(DocumentSnapshot snapshot, VisualStateId id) =>
        snapshot.VisualModel.RoutingScopes?.SelectMany(static scope => scope.Connectors)
            .SingleOrDefault(route => route.VisualStateId == id);
    private sealed class Factory(VisualStateId id, ConnectorRoutingType type) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new SetConnectorRoutingTypeCommand(documentId, expectedRevision, id, type);
    }
}
