using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Organizational.Projection;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Bpmn.Projection;

internal sealed class BpmnParticipantGroupProjectionRule : IProjectionRule
{
    public ProjectionRuleResult Project(ProjectionRuleInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input is not ElementProjectionRuleInput { Document: { } document, ViewContext: ProcessScopePresentationContext context } element)
            return ProjectionRuleResult.Success(ProjectionRuleContribution.Empty);
        if (!document.SemanticModel.ModelProfiles.IsAvailable(Inceptus.DocumentEngine.Organizational.Profiles.OrganizationalModelProfile.Id))
            return ProjectionRuleResult.Success(ProjectionRuleContribution.Empty);
        var pools = OrganizationalSemantics.GetPoolsInScope(document, context.ScopeId)
            .Where(pool => OrganizationalSemantics.GetParticipantId(pool) == input.SemanticId).Select(static pool => pool.Id).ToArray();
        return pools.Length == 0 ? ProjectionRuleResult.Success(ProjectionRuleContribution.Empty) :
            OrganizationalContainerProjection.Project(element, Presentation.BpmnParticipantPresentationContribution.ProjectionRuleId, pools, true);
    }
}
