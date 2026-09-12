using System.Globalization;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Validation;

/// <summary>
/// Identifies one model-validation rule independently of its CLR type.
/// </summary>
public sealed record ModelValidationRuleId
{
    public ModelValidationRuleId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Identifies one deterministic finding produced by a model-validation rule.
/// </summary>
public sealed record ModelValidationIssueId
{
    public ModelValidationIssueId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;

    internal static ModelValidationIssueId Create(
        ModelValidationRuleId ruleId,
        string code,
        ModelValidationTarget target,
        string? discriminator)
    {
        ArgumentNullException.ThrowIfNull(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(target);

        return new ModelValidationIssueId(string.Concat(
            "validation:",
            Part(ruleId.Value),
            Part(code),
            Part(target.SemanticElementId?.Value ?? string.Empty),
            Part(target.VisualStateId?.Value ?? string.Empty),
            Part(discriminator ?? string.Empty)));
    }

    private static string Part(string value) => string.Concat(
        value.Length.ToString(CultureInfo.InvariantCulture),
        ":",
        value);
}
