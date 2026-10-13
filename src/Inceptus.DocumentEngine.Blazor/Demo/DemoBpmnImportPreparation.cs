using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.Blazor.Demo;

/// <summary>
/// Host composition of the existing preparation services, independent of the live editor.
/// Its measurement canvas uses the same bundled font as the reference modeler.
/// </summary>
internal static class DemoBpmnImportPreparation
{
    internal static Canvas2DRendererConfiguration RendererConfiguration { get; } = new(
        fontResources: [new Canvas2DFontResource("org.dejavu.DejaVuSans", "2.37", "DejaVu Sans",
            "_content/Inceptus.DocumentEngine.Bpmn.Blazor/fonts/DejaVuSans-2.37.ttf", 400, TextFontStyle.Normal)],
        defaultFontFamily: "DejaVu Sans");

    internal static ConnectorRoutingStatePreparer Create(Canvas2DRenderer renderer)
    {
        var bpmn = BpmnPluginRegistration.N100;
        var eligibility = new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode);
        var organizational = OrganizationalPluginRegistration.Create(eligibility,
            OrganizationalPoolSceneContributor.CreateRegistration(eligibility));
        var configuration = new EditingSessionConfiguration(
            new ProjectionEngine(bpmn.ProjectionRules.AddRange(organizational.ProjectionRules), bpmn.PresentationViewProviders),
            new LayoutEngine(bpmn.LayoutAlgorithms), BpmnAlgorithmIds.DefaultLayout,
            new RoutingEngine(bpmn.RoutingAlgorithms), BpmnAlgorithmIds.DefaultRouting,
            new Canvas2DSceneBuilder(contributors: bpmn.SceneContributors.AddRange(organizational.SceneContributors)),
            connectorAnchorPolicyProvider: new ElementConnectorAnchorPolicyRegistry(bpmn.ConnectorAnchorPolicies),
            routingInputPreparer: organizational.RoutingInputPreparer);
        return new ConnectorRoutingStatePreparer(configuration, renderer);
    }
}
