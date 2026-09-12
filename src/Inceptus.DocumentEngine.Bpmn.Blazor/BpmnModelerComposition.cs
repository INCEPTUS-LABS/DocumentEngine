using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.ContextMenus;
using Inceptus.DocumentEngine.Contracts.Deletion;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.ScopeNavigation;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Composition;

/// <summary>
/// Single production authority for the BPMN and Organizational modeler composition.
/// </summary>
internal static class BpmnModelerComposition
{
    private static readonly BpmnPluginRegistration BpmnRegistration =
        BpmnPluginRegistration.N100;

    private static readonly OrganizationalPluginRegistration OrganizationalRegistration =
        CreateOrganizationalRegistration();

    private static readonly IElementConnectorAnchorPolicyProvider ConnectorAnchorPolicyProvider =
        new ElementConnectorAnchorPolicyRegistry(BpmnRegistration.ConnectorAnchorPolicies);

    internal static ToolboxCatalog ToolboxCatalog { get; } =
        new(BpmnRegistration.ToolboxContributions);

    internal static DocumentCanvasComposition Create(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // A startup provider deliberately knows nothing about the RCL's milestone-specific
        // composition. Reconstructing the supplied persistent snapshot here proves that it is
        // valid under the same canonical BPMN connector-anchor policy used by this modeler,
        // while the originally supplied Document remains the instance attached and owned by
        // the component.
        var canonicalReconstruction = ReconstructDocument(document.CaptureSnapshot());
        if (!canonicalReconstruction.Succeeded)
        {
            throw new InvalidOperationException(
                "The initial Document is not valid for the BPMN modeler connector-anchor policy.");
        }

        var configuration = new EditingSessionConfiguration(
            new ProjectionEngine(
                BpmnRegistration.ProjectionRules.AddRange(
                    OrganizationalRegistration.ProjectionRules)),
            new LayoutEngine(BpmnRegistration.LayoutAlgorithms),
            BpmnAlgorithmIds.DefaultLayout,
            new RoutingEngine(BpmnRegistration.RoutingAlgorithms),
            BpmnAlgorithmIds.DefaultRouting,
            new Canvas2DSceneBuilder(contributors:
                BpmnRegistration.SceneContributors.AddRange(
                    OrganizationalRegistration.SceneContributors)),
            initialEditorState: EditorStateSnapshot.Empty,
            commandHandlers: BpmnRegistration.CommandHandlers.AddRange(
                OrganizationalRegistration.CommandHandlers),
            commandValidators: BpmnRegistration.CommandValidators.AddRange(
                OrganizationalRegistration.CommandValidators),
            historyPolicies: BpmnRegistration.HistoryPolicies.AddRange(
                OrganizationalRegistration.HistoryPolicies),
            connectorAnchorPolicyProvider: ConnectorAnchorPolicyProvider,
            modelProfileCatalog: new ModelProfileCatalog(
                BpmnRegistration.ModelProfileDefinitions));

        return new DocumentCanvasComposition(
            document,
            configuration,
            new ElementPropertiesSchemaCatalog(
                BpmnRegistration.PropertiesSchemas.AddRange(
                    OrganizationalRegistration.PropertiesSchemas)),
            toolboxPlacementCatalog: new ToolboxPlacementCatalog(
                BpmnRegistration.ToolboxPlacementRegistrations,
                ToolboxCatalog),
            anchorConnectionCreationCatalog: new AnchorConnectionCreationCatalog(
                BpmnRegistration.AnchorConnectionCreationRegistrations),
            endpointReconnectionCatalog: new ConnectorEndpointReconnectionCatalog(
                BpmnRegistration.ConnectorEndpointReconnectionRegistrations),
            deletionCatalog: new DiagramDeletionCatalog(
                BpmnRegistration.DiagramDeletionRegistrations.AddRange(
                    OrganizationalRegistration.DiagramDeletionRegistrations)),
            modelValidationCatalog: new ModelValidationCatalog(
                BpmnRegistration.ModelValidationRules.AddRange(
                    OrganizationalRegistration.ModelValidationRules)),
            scopeNavigationCatalog: new ScopeNavigationCatalog(
                BpmnRegistration.ScopeNavigationRegistrations),
            backgroundActionCatalog: new CanvasBackgroundActionCatalog(
                OrganizationalRegistration.BackgroundActions),
            semanticSceneViewActionCatalog: new SemanticSceneViewActionCatalog(
                OrganizationalRegistration.SemanticSceneViewActions),
            semanticSceneCommandActionCatalog: new SemanticSceneCommandActionCatalog(
                OrganizationalRegistration.SemanticSceneCommandActions),
            spatialEditPlanners: new Canvas2DSpatialEditPlannerCatalog(
                OrganizationalRegistration.SpatialEditPlannerRegistrations));
    }

    internal static Document CreateEmptyDocument()
    {
        var creation = DocumentFactory.CreateEmpty(
            new DocumentId($"document:{Guid.NewGuid():N}"),
            connectorAnchorPolicyProvider: ConnectorAnchorPolicyProvider);
        return creation.Document ?? throw new InvalidOperationException(
            "The initial empty BPMN Document could not be created.");
    }

    internal static DocumentConstructionResult ReconstructDocument(DocumentSnapshot snapshot) =>
        DocumentReconstructor.Reconstruct(snapshot, ConnectorAnchorPolicyProvider);

    private static OrganizationalPluginRegistration CreateOrganizationalRegistration()
    {
        var eligibilityPolicy = new OrganizationalElementEligibilityPolicy(
            BpmnSemanticTypes.IsFlowNode);
        return OrganizationalPluginRegistration.Create(
            eligibilityPolicy,
            OrganizationalPoolSceneContributor.CreateRegistration(eligibilityPolicy));
    }
}
