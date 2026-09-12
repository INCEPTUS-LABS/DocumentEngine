using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.EndpointReconnection;

/// <summary>
/// Identifies one notation-owned connector endpoint-reconnection capability.
/// </summary>
public sealed record ConnectorEndpointReconnectionId
{
    public ConnectorEndpointReconnectionId(string value) =>
        Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}
