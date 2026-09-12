using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Toolbox;

/// <summary>
/// Provides immutable, deterministic placement-factory lookup by Toolbox item identity.
/// </summary>
public sealed class ToolboxPlacementCatalog
{
    private readonly ImmutableDictionary<ToolboxItemId, ToolboxPlacementRegistration>
        _registrationsByItemId;

    public ToolboxPlacementCatalog(
        IEnumerable<ToolboxPlacementRegistration>? registrations = null,
        ToolboxCatalog? toolboxCatalog = null)
    {
        var copy = registrations?.ToArray() ?? [];
        if (Array.Exists(copy, static registration => registration is null))
        {
            throw new ArgumentException(
                "Toolbox placement catalogs cannot contain null registrations.",
                nameof(registrations));
        }

        Array.Sort(
            copy,
            static (left, right) => StringComparer.Ordinal.Compare(
                left.ToolboxItemId.Value,
                right.ToolboxItemId.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].ToolboxItemId != copy[index].ToolboxItemId)
            {
                continue;
            }

            throw new ArgumentException(
                $"Toolbox item '{copy[index].ToolboxItemId}' has more than one placement registration.",
                nameof(registrations));
        }

        if (toolboxCatalog is not null)
        {
            foreach (var registration in copy)
            {
                if (toolboxCatalog.TryGetItem(registration.ToolboxItemId, out _))
                {
                    continue;
                }

                throw new ArgumentException(
                    $"Toolbox placement item '{registration.ToolboxItemId}' is not present in the Toolbox catalog.",
                    nameof(registrations));
            }
        }

        Registrations = [.. copy];
        _registrationsByItemId = Registrations.ToImmutableDictionary(
            static registration => registration.ToolboxItemId);
    }

    public static ToolboxPlacementCatalog Empty { get; } = new();

    public ImmutableArray<ToolboxPlacementRegistration> Registrations { get; }

    public bool TryGetRegistration(
        ToolboxItemId toolboxItemId,
        [NotNullWhen(true)] out ToolboxPlacementRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(toolboxItemId);
        return _registrationsByItemId.TryGetValue(toolboxItemId, out registration);
    }
}
