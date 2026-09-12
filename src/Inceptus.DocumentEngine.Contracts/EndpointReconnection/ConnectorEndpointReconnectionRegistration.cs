namespace Inceptus.DocumentEngine.Contracts.EndpointReconnection;

/// <summary>
/// Associates one endpoint-reconnection capability with its notation-owned Command factory.
/// </summary>
public sealed class ConnectorEndpointReconnectionRegistration
{
    public ConnectorEndpointReconnectionRegistration(
        ConnectorEndpointReconnectionId reconnectionId,
        IConnectorEndpointReconnectionCommandFactory commandFactory)
    {
        ArgumentNullException.ThrowIfNull(reconnectionId);
        ArgumentNullException.ThrowIfNull(commandFactory);

        ReconnectionId = reconnectionId;
        CommandFactory = commandFactory;
    }

    public ConnectorEndpointReconnectionId ReconnectionId { get; }

    public IConnectorEndpointReconnectionCommandFactory CommandFactory { get; }
}
