namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Plugin-facing read-only policy that contributes immutable projected data.
/// </summary>
public interface IProjectionRule
{
    ProjectionRuleResult Project(
        ProjectionRuleInput input,
        CancellationToken cancellationToken);
}
