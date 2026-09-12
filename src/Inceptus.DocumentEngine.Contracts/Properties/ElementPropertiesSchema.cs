using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Properties;

/// <summary>
/// Defines the immutable, ordered Properties fields for one Semantic element type.
/// </summary>
public sealed class ElementPropertiesSchema
{
    public ElementPropertiesSchema(
        SemanticTypeId semanticTypeId,
        IEnumerable<ElementPropertyFieldDefinition> fields)
    {
        ArgumentNullException.ThrowIfNull(semanticTypeId);
        ArgumentNullException.ThrowIfNull(fields);

        var copy = fields.ToArray();
        if (Array.Exists(copy, static field => field is null))
        {
            throw new ArgumentException(
                "Element Properties schemas cannot contain null field definitions.",
                nameof(fields));
        }

        RejectDuplicateFieldIds(copy, nameof(fields));
        RejectDuplicateSemanticPropertyKeys(copy, nameof(fields));

        SemanticTypeId = semanticTypeId;
        Fields = copy
            .OrderBy(static field => field.Order)
            .ThenBy(static field => field.FieldId.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public SemanticTypeId SemanticTypeId { get; }

    public ImmutableArray<ElementPropertyFieldDefinition> Fields { get; }

    private static void RejectDuplicateFieldIds(
        ElementPropertyFieldDefinition[] fields,
        string parameterName)
    {
        var ordered = fields
            .OrderBy(static field => field.FieldId.Value, StringComparer.Ordinal)
            .ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1].FieldId != ordered[index].FieldId)
            {
                continue;
            }

            throw new ArgumentException(
                $"Element Properties field ID '{ordered[index].FieldId}' occurs more than once.",
                parameterName);
        }
    }

    private static void RejectDuplicateSemanticPropertyKeys(
        ElementPropertyFieldDefinition[] fields,
        string parameterName)
    {
        var ordered = fields
            .OrderBy(static field => field.SemanticPropertyKey, StringComparer.Ordinal)
            .ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (!StringComparer.Ordinal.Equals(
                    ordered[index - 1].SemanticPropertyKey,
                    ordered[index].SemanticPropertyKey))
            {
                continue;
            }

            throw new ArgumentException(
                $"Semantic property key '{ordered[index].SemanticPropertyKey}' is bound to more than one element Properties field.",
                parameterName);
        }
    }
}
