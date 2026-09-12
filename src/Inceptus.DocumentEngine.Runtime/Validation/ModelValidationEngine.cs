using Inceptus.DocumentEngine.Contracts.Validation;

namespace Inceptus.DocumentEngine.Runtime.Validation;

/// <summary>
/// Executes composed read-only validation rules without invoking the editing pipeline.
/// </summary>
public sealed class ModelValidationEngine
{
    private readonly ModelValidationCatalog _catalog;

    public ModelValidationEngine(ModelValidationCatalog? catalog = null) =>
        _catalog = catalog ?? ModelValidationCatalog.Empty;

    public ValidationSnapshot Validate(
        ModelValidationContext context,
        IEnumerable<ModelValidationIssue>? additionalIssues = null,
        long? sourceGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<ModelValidationIssue>();
        foreach (var rule in _catalog.Rules)
        {
            var findings = rule.Validate(context);
            foreach (var finding in findings)
            {
                if (finding is null)
                {
                    throw new InvalidOperationException(
                        $"Model-validation rule '{rule.RuleId}' returned a null finding.");
                }

                if (finding.RuleId != rule.RuleId)
                {
                    throw new InvalidOperationException(
                        $"Model-validation rule '{rule.RuleId}' returned finding '{finding.Id}' for rule '{finding.RuleId}'.");
                }

                issues.Add(finding);
            }
        }

        if (additionalIssues is not null)
        {
            issues.AddRange(additionalIssues);
        }

        return new ValidationSnapshot(
            context.Document.DocumentId,
            context.ActiveScopeId,
            context.Document.Revision,
            issues,
            sourceGeneration);
    }
}
