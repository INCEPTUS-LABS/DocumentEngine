using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Inceptus.DocumentEngine.Contracts.ContextMenus;

/// <summary>
/// Provides deterministic lookup of notation-contributed semantic Scene View actions.
/// </summary>
public sealed class SemanticSceneViewActionCatalog
{
    private readonly ImmutableDictionary<
        SemanticSceneViewActionId,
        SemanticSceneViewActionDefinition> _definitionsById;

    public SemanticSceneViewActionCatalog(
        IEnumerable<SemanticSceneViewActionDefinition>? definitions = null)
    {
        var copy = definitions?.ToArray() ?? [];
        if (Array.Exists(copy, static definition => definition is null))
        {
            throw new ArgumentException(
                "Semantic Scene View actions cannot contain null definitions.",
                nameof(definitions));
        }

        var identities = new HashSet<SemanticSceneViewActionId>();
        foreach (var definition in copy)
        {
            if (!identities.Add(definition.Id))
            {
                throw new ArgumentException(
                    $"Duplicate semantic Scene View action identity '{definition.Id}'.",
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

    public static SemanticSceneViewActionCatalog Empty { get; } = new();

    public ImmutableArray<SemanticSceneViewActionDefinition> Definitions { get; }

    public bool TryGetDefinition(
        SemanticSceneViewActionId id,
        [NotNullWhen(true)] out SemanticSceneViewActionDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _definitionsById.TryGetValue(id, out definition);
    }
}
