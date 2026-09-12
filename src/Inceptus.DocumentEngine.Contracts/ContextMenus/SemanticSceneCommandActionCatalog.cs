using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Inceptus.DocumentEngine.Contracts.ContextMenus;

/// <summary>
/// Provides deterministic lookup of notation-contributed command-backed semantic actions.
/// </summary>
public sealed class SemanticSceneCommandActionCatalog
{
    private readonly ImmutableDictionary<
        SemanticSceneCommandActionId,
        SemanticSceneCommandActionDefinition> _definitionsById;

    public SemanticSceneCommandActionCatalog(
        IEnumerable<SemanticSceneCommandActionDefinition>? definitions = null)
    {
        var copy = definitions?.ToArray() ?? [];
        if (Array.Exists(copy, static definition => definition is null))
        {
            throw new ArgumentException(
                "Semantic Scene command actions cannot contain null definitions.",
                nameof(definitions));
        }

        var identities = new HashSet<SemanticSceneCommandActionId>();
        foreach (var definition in copy)
        {
            if (!identities.Add(definition.Id))
            {
                throw new ArgumentException(
                    $"Duplicate semantic Scene command action identity '{definition.Id}'.",
                    nameof(definitions));
            }
        }

        Array.Sort(copy, static (left, right) =>
        {
            var order = left.Order.CompareTo(right.Order);
            return order != 0
                ? order
                : StringComparer.Ordinal.Compare(left.Id.Value, right.Id.Value);
        });
        Definitions = [.. copy];
        _definitionsById = Definitions.ToImmutableDictionary(static definition => definition.Id);
    }

    public static SemanticSceneCommandActionCatalog Empty { get; } = new();

    public ImmutableArray<SemanticSceneCommandActionDefinition> Definitions { get; }

    public bool TryGetDefinition(
        SemanticSceneCommandActionId id,
        [NotNullWhen(true)] out SemanticSceneCommandActionDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _definitionsById.TryGetValue(id, out definition);
    }
}
