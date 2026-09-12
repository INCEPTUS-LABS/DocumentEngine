using System.Collections.Immutable;

namespace Inceptus.DocumentEngine.Contracts.Validation;

/// <summary>
/// Immutable, deterministic collection of composed model-validation rules.
/// </summary>
public sealed class ModelValidationCatalog
{
    public ModelValidationCatalog(IEnumerable<IModelValidationRule>? rules = null)
    {
        var copy = rules?.ToArray() ?? [];
        if (Array.Exists(copy, static rule => rule is null))
        {
            throw new ArgumentException(
                "Model-validation catalogs cannot contain null rules.",
                nameof(rules));
        }

        Array.Sort(copy, static (left, right) => StringComparer.Ordinal.Compare(
            left.RuleId.Value,
            right.RuleId.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].RuleId == copy[index].RuleId)
            {
                throw new ArgumentException(
                    $"Model-validation rule identity '{copy[index].RuleId}' has more than one registration.",
                    nameof(rules));
            }
        }

        Rules = [.. copy];
    }

    public static ModelValidationCatalog Empty { get; } = new();

    public ImmutableArray<IModelValidationRule> Rules { get; }
}
