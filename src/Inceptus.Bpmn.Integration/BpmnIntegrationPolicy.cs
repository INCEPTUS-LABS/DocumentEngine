using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.Bpmn.Integration;

internal static class BpmnIntegrationPolicy
{
    internal static IElementConnectorAnchorPolicyProvider ConnectorAnchors { get; } =
        new ElementConnectorAnchorPolicyRegistry(BpmnPluginRegistration.N100.ConnectorAnchorPolicies);

    internal static ModelValidationCatalog ValidationRules { get; } = CreateRules();

    internal static ProjectionEngine Projection { get; } = new(BpmnPluginRegistration.N100.ProjectionRules
        .AddRange(CreateOrganizational().ProjectionRules), BpmnPluginRegistration.N100.PresentationViewProviders);

    private static ModelValidationCatalog CreateRules()
    {
        var organizational = CreateOrganizational();
        return new ModelValidationCatalog(BpmnPluginRegistration.N100.ModelValidationRules
            .AddRange(organizational.ModelValidationRules));
    }

    private static OrganizationalPluginRegistration CreateOrganizational()
    {
        var eligibility = new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode);
        return OrganizationalPluginRegistration.Create(eligibility,
            OrganizationalPoolSceneContributor.CreateRegistration(eligibility));
    }
}
