using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.ContextMenus;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Deletion;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.ScopeNavigation;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

/// <summary>
/// Creates the downstream application composition attached by a document-canvas host.
/// </summary>
internal interface IDocumentCanvasCompositionFactory
{
    ValueTask<DocumentCanvasComposition> CreateAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable application-owned inputs for one document-canvas attachment.
/// </summary>
internal sealed record DocumentCanvasComposition
{
    internal DocumentCanvasComposition(
        Document document,
        EditingSessionConfiguration configuration,
        ElementPropertiesSchemaCatalog propertiesSchemaCatalog,
        IDocumentCanvasPipelineCounters? counters = null,
        ToolboxPlacementCatalog? toolboxPlacementCatalog = null,
        AnchorConnectionCreationCatalog? anchorConnectionCreationCatalog = null,
        IDocumentCreationIdentityProvider? documentCreationIdentityProvider = null,
        ConnectorEndpointReconnectionCatalog? endpointReconnectionCatalog = null,
        DiagramDeletionCatalog? deletionCatalog = null,
        ModelValidationCatalog? modelValidationCatalog = null,
        ScopeNavigationCatalog? scopeNavigationCatalog = null,
        CanvasBackgroundActionCatalog? backgroundActionCatalog = null,
        SemanticSceneViewActionCatalog? semanticSceneViewActionCatalog = null,
        SemanticSceneCommandActionCatalog? semanticSceneCommandActionCatalog = null,
        Canvas2DSpatialEditPlannerCatalog? spatialEditPlanners = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(propertiesSchemaCatalog);
        Document = document;
        Configuration = configuration;
        PropertiesSchemaCatalog = propertiesSchemaCatalog;
        Counters = counters;
        ToolboxPlacementCatalog = toolboxPlacementCatalog ?? ToolboxPlacementCatalog.Empty;
        AnchorConnectionCreationCatalog = anchorConnectionCreationCatalog ??
            AnchorConnectionCreationCatalog.Empty;
        DocumentCreationIdentityProvider = documentCreationIdentityProvider ??
            GuidDocumentCreationIdentityProvider.Instance;
        EndpointReconnectionCatalog = endpointReconnectionCatalog ??
            ConnectorEndpointReconnectionCatalog.Empty;
        DeletionCatalog = deletionCatalog ?? DiagramDeletionCatalog.Empty;
        ModelValidationCatalog = modelValidationCatalog ?? ModelValidationCatalog.Empty;
        ScopeNavigationCatalog = scopeNavigationCatalog ?? ScopeNavigationCatalog.Empty;
        BackgroundActionCatalog = backgroundActionCatalog ??
            CanvasBackgroundActionCatalog.Empty;
        SemanticSceneViewActionCatalog = semanticSceneViewActionCatalog ??
            SemanticSceneViewActionCatalog.Empty;
        SemanticSceneCommandActionCatalog = semanticSceneCommandActionCatalog ??
            SemanticSceneCommandActionCatalog.Empty;
        SpatialEditPlanners = spatialEditPlanners ?? Canvas2DSpatialEditPlannerCatalog.Empty;
    }

    internal Document Document { get; }

    internal EditingSessionConfiguration Configuration { get; }

    internal ElementPropertiesSchemaCatalog PropertiesSchemaCatalog { get; }

    internal IDocumentCanvasPipelineCounters? Counters { get; }

    internal ToolboxPlacementCatalog ToolboxPlacementCatalog { get; }

    internal AnchorConnectionCreationCatalog AnchorConnectionCreationCatalog { get; }

    internal IDocumentCreationIdentityProvider DocumentCreationIdentityProvider { get; }

    internal ConnectorEndpointReconnectionCatalog EndpointReconnectionCatalog { get; }

    internal DiagramDeletionCatalog DeletionCatalog { get; }

    internal ModelValidationCatalog ModelValidationCatalog { get; }

    internal ScopeNavigationCatalog ScopeNavigationCatalog { get; }

    internal CanvasBackgroundActionCatalog BackgroundActionCatalog { get; }

    internal SemanticSceneViewActionCatalog SemanticSceneViewActionCatalog { get; }

    internal SemanticSceneCommandActionCatalog SemanticSceneCommandActionCatalog { get; }

    internal Canvas2DSpatialEditPlannerCatalog SpatialEditPlanners { get; }
}

/// <summary>
/// Optional read-only pipeline instrumentation displayed by the presentation host.
/// </summary>
internal interface IDocumentCanvasPipelineCounters
{
    int ProjectionRuleInvocationCount { get; }

    int LayoutInvocationCount { get; }

    int RoutingInvocationCount { get; }

    int SceneContributionInvocationCount { get; }
}
