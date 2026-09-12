using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Profiles;

/// <summary>
/// Provides deterministic lookup of installed optional model-profile definitions.
/// </summary>
public sealed class ModelProfileCatalog
{
    private readonly ImmutableDictionary<ModelProfileId, ModelProfileDefinition> _byId;

    public ModelProfileCatalog(IEnumerable<ModelProfileDefinition>? definitions = null)
    {
        var copy = definitions?.ToArray() ?? [];
        if (Array.Exists(copy, static definition => definition is null))
        {
            throw new ArgumentException(
                "The collection cannot contain null profile definitions.",
                nameof(definitions));
        }

        var identities = new HashSet<ModelProfileId>();
        foreach (var definition in copy)
        {
            if (!identities.Add(definition.Id))
            {
                throw new ArgumentException(
                    $"Duplicate model profile identity '{definition.Id}'.",
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
        _byId = Definitions.ToImmutableDictionary(static definition => definition.Id);
    }

    public static ModelProfileCatalog Empty { get; } = new();

    public ImmutableArray<ModelProfileDefinition> Definitions { get; }

    public bool TryGetDefinition(ModelProfileId id, out ModelProfileDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId.TryGetValue(id, out definition);
    }
}
