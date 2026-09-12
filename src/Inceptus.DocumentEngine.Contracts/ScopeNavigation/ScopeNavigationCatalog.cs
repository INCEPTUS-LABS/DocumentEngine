using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.ScopeNavigation;

/// <summary>
/// Provides immutable, deterministic scope-navigation lookup by semantic type.
/// </summary>
public sealed class ScopeNavigationCatalog
{
    private readonly ImmutableDictionary<SemanticTypeId, ScopeNavigationRegistration>
        _registrationsBySemanticTypeId;

    public ScopeNavigationCatalog(
        IEnumerable<ScopeNavigationRegistration>? registrations = null)
    {
        var copy = registrations?.ToArray() ?? [];
        if (Array.Exists(copy, static registration => registration is null))
        {
            throw new ArgumentException(
                "Scope-navigation catalogs cannot contain null registrations.",
                nameof(registrations));
        }

        Array.Sort(copy, static (left, right) => StringComparer.Ordinal.Compare(
            left.SemanticTypeId.Value,
            right.SemanticTypeId.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].SemanticTypeId == copy[index].SemanticTypeId)
            {
                throw new ArgumentException(
                    $"Semantic type '{copy[index].SemanticTypeId}' has more than one scope-navigation registration.",
                    nameof(registrations));
            }
        }

        Registrations = [.. copy];
        _registrationsBySemanticTypeId = Registrations.ToImmutableDictionary(
            static registration => registration.SemanticTypeId);
    }

    public static ScopeNavigationCatalog Empty { get; } = new();

    public ImmutableArray<ScopeNavigationRegistration> Registrations { get; }

    public bool TryGetRegistration(
        SemanticTypeId semanticTypeId,
        [NotNullWhen(true)] out ScopeNavigationRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(semanticTypeId);
        return _registrationsBySemanticTypeId.TryGetValue(semanticTypeId, out registration);
    }
}
