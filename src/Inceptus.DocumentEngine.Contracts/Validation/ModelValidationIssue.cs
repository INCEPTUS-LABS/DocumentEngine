namespace Inceptus.DocumentEngine.Contracts.Validation;

/// <summary>
/// One immutable, deterministic, non-blocking model-validation finding.
/// </summary>
public sealed class ModelValidationIssue : IEquatable<ModelValidationIssue>
{
    public ModelValidationIssue(
        ModelValidationRuleId ruleId,
        ModelValidationSeverity severity,
        string code,
        string message,
        ModelValidationTarget? target = null,
        string? discriminator = null)
    {
        ArgumentNullException.ThrowIfNull(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(severity),
                severity,
                "The validation severity must be defined.");
        }

        if (discriminator is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(discriminator);
        }

        RuleId = ruleId;
        Severity = severity;
        Code = code;
        Message = message;
        Target = target ?? ModelValidationTarget.Document;
        Discriminator = discriminator;
        Id = ModelValidationIssueId.Create(ruleId, code, Target, discriminator);
    }

    public ModelValidationIssueId Id { get; }

    public ModelValidationRuleId RuleId { get; }

    public ModelValidationSeverity Severity { get; }

    public string Code { get; }

    public string Message { get; }

    public ModelValidationTarget Target { get; }

    public string? Discriminator { get; }

    public bool Equals(ModelValidationIssue? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id &&
        RuleId == other.RuleId &&
        Severity == other.Severity &&
        StringComparer.Ordinal.Equals(Code, other.Code) &&
        StringComparer.Ordinal.Equals(Message, other.Message) &&
        Target == other.Target &&
        StringComparer.Ordinal.Equals(Discriminator, other.Discriminator);

    public override bool Equals(object? obj) => Equals(obj as ModelValidationIssue);

    public override int GetHashCode() => HashCode.Combine(
        Id,
        RuleId,
        Severity,
        StringComparer.Ordinal.GetHashCode(Code),
        StringComparer.Ordinal.GetHashCode(Message),
        Target,
        Discriminator is null ? 0 : StringComparer.Ordinal.GetHashCode(Discriminator));
}
