using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Inceptus.DocumentEngine.Contracts.ContextMenus;

/// <summary>
/// Provides deterministic lookup of notation-contributed actions shown for the empty
/// model Canvas.
/// </summary>
public sealed class CanvasBackgroundActionCatalog
{
    private readonly ImmutableDictionary<CanvasBackgroundActionId, CanvasBackgroundActionDefinition>
        _definitionsById;

    public CanvasBackgroundActionCatalog(
        IEnumerable<CanvasBackgroundActionDefinition>? definitions = null)
    {
        var copy = definitions?.ToArray() ?? [];
        if (Array.Exists(copy, static definition => definition is null))
        {
            throw new ArgumentException(
                "The collection cannot contain null background-action definitions.",
                nameof(definitions));
        }

        var identities = new HashSet<CanvasBackgroundActionId>();
        foreach (var definition in copy)
        {
            if (!identities.Add(definition.Id))
            {
                throw new ArgumentException(
                    $"Duplicate Canvas background-action identity '{definition.Id}'.",
                    nameof(definitions));
            }
        }

        Array.Sort(copy, static (left, right) =>
        {
            var group = StringComparer.Ordinal.Compare(left.GroupLabel, right.GroupLabel);
            if (group != 0)
            {
                return group;
            }

            var order = left.Order.CompareTo(right.Order);
            return order != 0
                ? order
                : StringComparer.Ordinal.Compare(left.Id.Value, right.Id.Value);
        });
        Definitions = [.. copy];
        _definitionsById = Definitions.ToImmutableDictionary(static definition => definition.Id);
    }

    public static CanvasBackgroundActionCatalog Empty { get; } = new();

    public ImmutableArray<CanvasBackgroundActionDefinition> Definitions { get; }

    public bool TryGetDefinition(
        CanvasBackgroundActionId id,
        [NotNullWhen(true)] out CanvasBackgroundActionDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _definitionsById.TryGetValue(id, out definition);
    }
}
