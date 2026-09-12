using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Inceptus.DocumentEngine.Contracts.Deletion;

/// <summary>
/// Provides immutable, deterministic diagram deletion capability lookup and matching.
/// </summary>
public sealed class DiagramDeletionCatalog
{
    private readonly ImmutableDictionary<DiagramDeletionId, DiagramDeletionRegistration>
        _registrationsById;

    public DiagramDeletionCatalog(
        IEnumerable<DiagramDeletionRegistration>? registrations = null)
    {
        var copy = registrations?.ToArray() ?? [];
        if (Array.Exists(copy, static registration => registration is null))
        {
            throw new ArgumentException(
                "Diagram deletion catalogs cannot contain null registrations.",
                nameof(registrations));
        }

        Array.Sort(copy, static (left, right) => StringComparer.Ordinal.Compare(
            left.DeletionId.Value,
            right.DeletionId.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].DeletionId == copy[index].DeletionId)
            {
                throw new ArgumentException(
                    $"Diagram deletion identity '{copy[index].DeletionId}' has more than one registration.",
                    nameof(registrations));
            }
        }

        Registrations = [.. copy];
        _registrationsById = Registrations.ToImmutableDictionary(
            static registration => registration.DeletionId);
    }

    public static DiagramDeletionCatalog Empty { get; } = new();

    public ImmutableArray<DiagramDeletionRegistration> Registrations { get; }

    public bool TryGetRegistration(
        DiagramDeletionId deletionId,
        [NotNullWhen(true)] out DiagramDeletionRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(deletionId);
        return _registrationsById.TryGetValue(deletionId, out registration);
    }

    public ImmutableArray<DiagramDeletionRegistration> GetMatchingRegistrations(
        DiagramDeletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Registrations
            .Where(registration => registration.CommandFactory.CanDelete(request))
            .ToImmutableArray();
    }
}
