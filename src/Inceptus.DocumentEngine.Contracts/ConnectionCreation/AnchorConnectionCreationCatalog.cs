using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Inceptus.DocumentEngine.Contracts.ConnectionCreation;

/// <summary>
/// Provides immutable, deterministic connection-capability lookup and source matching.
/// </summary>
public sealed class AnchorConnectionCreationCatalog
{
    private readonly ImmutableDictionary<
        AnchorConnectionCreationId,
        AnchorConnectionCreationRegistration> _registrationsById;

    public AnchorConnectionCreationCatalog(
        IEnumerable<AnchorConnectionCreationRegistration>? registrations = null)
    {
        var copy = registrations?.ToArray() ?? [];
        if (Array.Exists(copy, static registration => registration is null))
        {
            throw new ArgumentException(
                "Anchor-connection creation catalogs cannot contain null registrations.",
                nameof(registrations));
        }

        Array.Sort(
            copy,
            static (left, right) => StringComparer.Ordinal.Compare(
                left.CreationId.Value,
                right.CreationId.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].CreationId != copy[index].CreationId)
            {
                continue;
            }

            throw new ArgumentException(
                $"Anchor-connection creation identity '{copy[index].CreationId}' has more than one registration.",
                nameof(registrations));
        }

        Registrations = [.. copy];
        _registrationsById = Registrations.ToImmutableDictionary(
            static registration => registration.CreationId);
    }

    public static AnchorConnectionCreationCatalog Empty { get; } = new();

    public ImmutableArray<AnchorConnectionCreationRegistration> Registrations { get; }

    public bool TryGetRegistration(
        AnchorConnectionCreationId creationId,
        [NotNullWhen(true)] out AnchorConnectionCreationRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(creationId);
        return _registrationsById.TryGetValue(creationId, out registration);
    }

    public ImmutableArray<AnchorConnectionCreationRegistration> GetMatchingRegistrations(
        AnchorConnectionCreationSourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var matches = ImmutableArray.CreateBuilder<AnchorConnectionCreationRegistration>();
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
