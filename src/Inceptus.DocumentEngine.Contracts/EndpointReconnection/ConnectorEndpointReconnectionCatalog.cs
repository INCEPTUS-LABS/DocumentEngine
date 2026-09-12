using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Inceptus.DocumentEngine.Contracts.EndpointReconnection;

/// <summary>
/// Provides immutable, deterministic endpoint-reconnection capability lookup and matching.
/// </summary>
public sealed class ConnectorEndpointReconnectionCatalog
{
    private readonly ImmutableDictionary<
        ConnectorEndpointReconnectionId,
        ConnectorEndpointReconnectionRegistration> _registrationsById;

    public ConnectorEndpointReconnectionCatalog(
        IEnumerable<ConnectorEndpointReconnectionRegistration>? registrations = null)
    {
        var copy = registrations?.ToArray() ?? [];
        if (Array.Exists(copy, static registration => registration is null))
        {
            throw new ArgumentException(
                "Connector endpoint-reconnection catalogs cannot contain null registrations.",
                nameof(registrations));
        }

        Array.Sort(
            copy,
            static (left, right) => StringComparer.Ordinal.Compare(
                left.ReconnectionId.Value,
                right.ReconnectionId.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].ReconnectionId != copy[index].ReconnectionId)
            {
                continue;
            }

            throw new ArgumentException(
                $"Connector endpoint-reconnection identity '{copy[index].ReconnectionId}' has more than one registration.",
                nameof(registrations));
        }

        Registrations = [.. copy];
        _registrationsById = Registrations.ToImmutableDictionary(
            static registration => registration.ReconnectionId);
    }

    public static ConnectorEndpointReconnectionCatalog Empty { get; } = new();

    public ImmutableArray<ConnectorEndpointReconnectionRegistration> Registrations { get; }

    public bool TryGetRegistration(
        ConnectorEndpointReconnectionId reconnectionId,
        [NotNullWhen(true)] out ConnectorEndpointReconnectionRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(reconnectionId);
        return _registrationsById.TryGetValue(reconnectionId, out registration);
    }

    public ImmutableArray<ConnectorEndpointReconnectionRegistration> GetMatchingRegistrations(
        ConnectorEndpointReconnectionStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var matches = ImmutableArray.CreateBuilder<ConnectorEndpointReconnectionRegistration>();
        foreach (var registration in Registrations)
        {
            if (registration.CommandFactory.CanStart(request))
            {
                matches.Add(registration);
            }
        }

        return matches.ToImmutable();
    }
}
