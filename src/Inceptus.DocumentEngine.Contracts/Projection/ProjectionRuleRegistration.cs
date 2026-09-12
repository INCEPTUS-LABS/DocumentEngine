using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Projection;

public sealed class ProjectionRuleRegistration
{
    public ProjectionRuleRegistration(
        ProjectionRuleId ruleId,
        ProjectionSourceKind sourceKind,
        SemanticTypeId semanticTypeId,
        IProjectionRule rule)
    {
        ArgumentNullException.ThrowIfNull(ruleId);
        ArgumentNullException.ThrowIfNull(semanticTypeId);
        ArgumentNullException.ThrowIfNull(rule);
        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceKind),
                sourceKind,
                "The Projection source kind must be defined.");
        }

        RuleId = ruleId;
        SourceKind = sourceKind;
        SemanticTypeId = semanticTypeId;
        Rule = rule;
    }

    public ProjectionRuleId RuleId { get; }

    public ProjectionSourceKind SourceKind { get; }

    public SemanticTypeId SemanticTypeId { get; }

    public IProjectionRule Rule { get; }
}
