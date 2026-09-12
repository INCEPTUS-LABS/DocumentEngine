using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Organizational.Projection;

internal sealed class OrganizationalPoolProjectionRule : IProjectionRule
{
    internal static ProjectionRuleId RuleId { get; } =
        new("inceptus:organizational/projection/pool-container");

    internal static ProjectionRuleRegistration Registration { get; } =
        new(
            RuleId,
            ProjectionSourceKind.SemanticElement,
            OrganizationalSemanticTypes.Pool,
            new OrganizationalPoolProjectionRule());

    public ProjectionRuleResult Project(
        ProjectionRuleInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (input is not ElementProjectionRuleInput element ||
            element.Element.TypeId != OrganizationalSemanticTypes.Pool)
        {
            throw new ArgumentException(
                "The Organizational Pool rule requires a Pool element input.",
                nameof(input));
        }

        // Pools are rendered by the Organizational Scene contributor as spatial regions.
        // This rule explicitly consumes the semantic element without duplicating it as a node.
        return ProjectionRuleResult.Success(ProjectionRuleContribution.Empty);
    }
}
