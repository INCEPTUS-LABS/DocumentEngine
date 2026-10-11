namespace Inceptus.DocumentEngine.Contracts.EndpointReconnection;

/// <summary>
/// Associates one endpoint-reconnection capability with its notation-owned Command factory.
/// </summary>
public sealed class ConnectorEndpointReconnectionRegistration
{
    public ConnectorEndpointReconnectionRegistration(
        ConnectorEndpointReconnectionId reconnectionId,
        IConnectorEndpointReconnectionCommandFactory commandFactory)
        : this(reconnectionId, commandFactory, null)
    {
    }

    public ConnectorEndpointReconnectionRegistration(ConnectorEndpointReconnectionId reconnectionId,
        IConnectorEndpointReconnectionCommandFactory commandFactory,
        IConnectorEndpointReconnectionCandidateEligibility? candidateEligibility)
    {
        ArgumentNullException.ThrowIfNull(reconnectionId);
        ArgumentNullException.ThrowIfNull(commandFactory);

        ReconnectionId = reconnectionId;
        CommandFactory = commandFactory;
        CandidateEligibility = candidateEligibility;
    }

    public ConnectorEndpointReconnectionId ReconnectionId { get; }

    public IConnectorEndpointReconnectionCommandFactory CommandFactory { get; }

    public IConnectorEndpointReconnectionCandidateEligibility? CandidateEligibility { get; }
}
