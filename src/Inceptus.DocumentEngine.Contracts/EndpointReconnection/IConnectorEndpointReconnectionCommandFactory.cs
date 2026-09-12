namespace Inceptus.DocumentEngine.Contracts.EndpointReconnection;

/// <summary>
/// Matches existing connector endpoints and creates notation-owned immutable
/// endpoint-reconnection Commands.
/// </summary>
public interface IConnectorEndpointReconnectionCommandFactory
{
    bool CanStart(ConnectorEndpointReconnectionStartRequest request);

    ConnectorEndpointReconnectionPlanResult CreatePlan(
        ConnectorEndpointReconnectionRequest request);
}
