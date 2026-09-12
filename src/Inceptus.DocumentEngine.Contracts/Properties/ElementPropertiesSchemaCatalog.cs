using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Properties;

/// <summary>
/// Provides immutable, deterministic element Properties schema lookup by Semantic type.
/// </summary>
public sealed class ElementPropertiesSchemaCatalog
{
    private readonly ImmutableDictionary<SemanticTypeId, ElementPropertiesSchema> _schemasByType;

    public ElementPropertiesSchemaCatalog(
        IEnumerable<ElementPropertiesSchema>? schemas = null)
    {
        var copy = schemas?.ToArray() ?? [];
        if (Array.Exists(copy, static schema => schema is null))
        {
            throw new ArgumentException(
                "Element Properties schema catalogs cannot contain null schemas.",
                nameof(schemas));
        }

        Array.Sort(
            copy,
            static (left, right) => StringComparer.Ordinal.Compare(
                left.SemanticTypeId.Value,
                right.SemanticTypeId.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].SemanticTypeId != copy[index].SemanticTypeId)
            {
                continue;
            }

            throw new ArgumentException(
                $"Semantic type '{copy[index].SemanticTypeId}' has more than one element Properties schema.",
                nameof(schemas));
        }

        Schemas = [.. copy];
        _schemasByType = Schemas.ToImmutableDictionary(
            static schema => schema.SemanticTypeId);
    }

    public static ElementPropertiesSchemaCatalog Empty { get; } = new();

    public ImmutableArray<ElementPropertiesSchema> Schemas { get; }

    public bool TryGetSchema(
        SemanticTypeId semanticTypeId,
        [NotNullWhen(true)] out ElementPropertiesSchema? schema)
    {
        ArgumentNullException.ThrowIfNull(semanticTypeId);
        return _schemasByType.TryGetValue(semanticTypeId, out schema);
    }
}
