using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN3ConnectorEndpointReconnectionArchitectureTests
{
    [Fact]
    public void GenericN3ContractsAndCanvasMechanismsRemainNotationNeutral()
    {
        Type[] contracts =
        [
            typeof(ConnectorEndpointReconnectionId),
            typeof(ConnectorEndpointReconnectionStartRequest),
            typeof(ConnectorEndpointReconnectionRequest),
            typeof(IConnectorEndpointReconnectionCommandFactory),
            typeof(ConnectorEndpointReconnectionPlan),
            typeof(ConnectorEndpointReconnectionPlanResult),
            typeof(ConnectorEndpointReconnectionRegistration),
            typeof(ConnectorEndpointReconnectionCatalog),
            typeof(ConnectorEndpointKind),
        ];
        Assert.All(contracts, static contract => Assert.True(contract.IsPublic));

        var contractSource = ReadProductionDirectory(
                "Inceptus.DocumentEngine.Contracts",
                "EndpointReconnection") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "Visuals",
                "ConnectorEndpointKind.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "Visuals",
                "ConnectorAnchorOccupancy.cs");
        var controller = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.EndpointReconnection.cs");
        var metadata = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DConnectorEndpointReconnectionGestureMetadata.cs");
        var composition = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Composition.cs");
        var gestures = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");
        var canvasN3Source = controller + metadata +
            Between(
                composition,
                "if (editorState.ActiveGesture is { } reconnectionGesture &&",
                "if (editorState.HoveredObjectId is not null)") +
            Between(
                gestures,
                "private static bool ComposeConnectorEndpointReconnectionGesturePreview(",
                "private static bool ComposeRouteGesturePreview(");

        string[] notationTokens =
        [
            "Bpmn",
            "BPMN",
            "SequenceFlow",
            "StartEvent",
            "EndEvent",
            "Gateway",
            "InceptusNotation",
        ];
        Assert.All(notationTokens, token =>
            Assert.DoesNotContain(token, contractSource + canvasN3Source,
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Canvas2D", contractSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Inceptus.DocumentEngine.Bpmn", canvasN3Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void N2CreationAndN3ReconnectionUseSeparateCatalogAuthorities()
    {
        Assert.NotEqual(
            typeof(AnchorConnectionCreationCatalog),
            typeof(ConnectorEndpointReconnectionCatalog));
        Assert.NotEqual(
            typeof(AnchorConnectionCreationCatalog).Namespace,
            typeof(ConnectorEndpointReconnectionCatalog).Namespace);
        Assert.Empty(AnchorConnectionCreationCatalog.Empty.Registrations);
        Assert.Empty(ConnectorEndpointReconnectionCatalog.Empty.Registrations);

        var controller = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var n2Contracts = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Contracts",
            "ConnectionCreation");
        var n3Contracts = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Contracts",
            "EndpointReconnection");

        Assert.Contains("AnchorConnectionCreationCatalog _connectionCreationCatalog",
            controller, StringComparison.Ordinal);
        Assert.Contains(
            "ConnectorEndpointReconnectionCatalog _endpointReconnectionCatalog",
            controller,
            StringComparison.Ordinal);
        Assert.Contains("AnchorConnectionCreationCatalog.Empty", controller,
            StringComparison.Ordinal);
        Assert.Contains("ConnectorEndpointReconnectionCatalog.Empty", controller,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectorEndpointReconnectionCatalog", n2Contracts,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AnchorConnectionCreationCatalog", n3Contracts,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EndpointReconnectDispatchPrecedesN2AnchorCreationDispatch()
    {
        var controller = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var pointerPressed = Between(
            controller,
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerPressedAsync(",
            "private async ValueTask<Canvas2DInteractionResult> ExecuteNormalizedPointerMoveAsync(");
        var endpointController = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.EndpointReconnection.cs");
        var reconnectIndex = pointerPressed.IndexOf(
            "TryStartConnectorEndpointReconnectionUnderGateAsync(",
            StringComparison.Ordinal);
        var n2Index = pointerPressed.IndexOf(
            "TryStartAnchorConnectionUnderGateAsync(",
            StringComparison.Ordinal);

        Assert.True(
            reconnectIndex >= 0 && n2Index > reconnectIndex,
            "Selected connector endpoint reconnection must dispatch before N2 anchor creation.");
        Assert.Contains("IsConnectorEndpointHandle(hitItem)", endpointController,
            StringComparison.Ordinal);
        Assert.Contains("TryResolveConnectorAnchorHandle(", controller,
            StringComparison.Ordinal);
        Assert.DoesNotContain("TryStartAnchorConnectionUnderGateAsync(", endpointController,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GenericControllerDelegatesPersistentMutationThroughOneEditingSessionCommand()
    {
        var controller = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.EndpointReconnection.cs");

        Assert.Contains("currentRegistration.CommandFactory.CreatePlan(", controller,
            StringComparison.Ordinal);
        Assert.Contains("plan.Command", controller, StringComparison.Ordinal);
        Assert.Contains("_session.CompletePersistentGestureAsync(", controller,
            StringComparison.Ordinal);
        string[] forbidden =
        [
            "new CommandProcessor(",
            "new HistoryManager(",
            ".HandleAsync(",
            "BpmnDocumentReplacement",
            "new SemanticModelSnapshot(",
            "new VisualModelSnapshot(",
            "new SemanticRelationshipSnapshot(",
            "new VisualStateSnapshot(",
            "CreateBpmnSequenceFlowCommand",
            "DeleteBpmnSequenceFlowCommand",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, controller, StringComparison.Ordinal));
    }

    [Fact]
    public void CandidateAndPreviewSceneCodeIsTransientAndCannotSynthesizeAnchors()
    {
        var composition = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Composition.cs");
        var gestures = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");
        var candidates = Between(
            composition,
            "if (editorState.ActiveGesture is { } reconnectionGesture &&",
            "if (editorState.HoveredObjectId is not null)");
        var actualAnchorFilter = Between(
            composition,
            "private static ProjectedConnectorAnchor[] ExistingDynamicAnchors(",
            "private static IEnumerable<Canvas2DSceneItem> CreateTargetOverlays(");
        var anchorHandles = Between(
            gestures,
            "private static IEnumerable<Canvas2DSceneItem> CreateConnectorAnchorHandles(",
            "private static bool ComposeAnchorConnectionGesturePreview(");
        var preview = Between(
            gestures,
            "private static bool ComposeConnectorEndpointReconnectionGesturePreview(",
            "private static bool ComposeRouteGesturePreview(");
        var transientScene = candidates + actualAnchorFilter + anchorHandles + preview;

        Assert.Contains("ExistingDynamicAnchors(", candidates, StringComparison.Ordinal);
        Assert.Contains("anchor.Kind == ResolvedConnectorAnchorKind.Dynamic",
            actualAnchorFilter, StringComparison.Ordinal);
        Assert.Contains("persistentById.TryGetValue(anchor.Id", actualAnchorFilter,
            StringComparison.Ordinal);
        Assert.Contains("persistent.Role == requiredRole", actualAnchorFilter,
            StringComparison.Ordinal);
        Assert.Contains("Canvas2DHitTestPolicy.None", preview, StringComparison.Ordinal);
        Assert.Contains(".Select(target.Transform.TransformPoint)", preview,
            StringComparison.Ordinal);

        string[] forbidden =
        [
            "RoutingEngine",
            "BpmnRoutingAlgorithm",
            "ExecuteAsync(",
            "CompletePersistentGestureAsync(",
            "CommandFactory",
            "ICommand",
            "new SemanticModelSnapshot(",
            "new VisualModelSnapshot(",
            "AddConnectorAnchorCommand",
            "new ConnectorAnchor(",
            "new ProjectedConnectorAnchor(",
            "ConnectorAnchorReferenceIdentity",
            "Nearest",
            "VirtualAnchor",
            "Guid.NewGuid",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, transientScene, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OccupancyExclusionNamesOneExactConnectorEndpointAndOriginalAnchor()
    {
        var method = typeof(ConnectorAnchorOccupancy).GetMethod(
            nameof(ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint),
            [
                typeof(IVisualModelView),
                typeof(ConnectorAnchorId),
                typeof(VisualStateId),
                typeof(ConnectorEndpointKind),
            ]);
        Assert.NotNull(method);

        var occupancy = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "Visuals",
            "ConnectorAnchorOccupancy.cs");
        var composition = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Composition.cs");
        var gestures = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");
        var controller = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.EndpointReconnection.cs");
        var candidates = Between(
            composition,
            "if (editorState.ActiveGesture is { } reconnectionGesture &&",
            "if (editorState.HoveredObjectId is not null)");
        var anchorHandles = Between(
            gestures,
            "private static IEnumerable<Canvas2DSceneItem> CreateConnectorAnchorHandles(",
            "private static bool ComposeAnchorConnectionGesturePreview(");

        Assert.Contains("visualState.SourceAnchorId == anchorId", occupancy,
            StringComparison.Ordinal);
        Assert.Contains("excludedEndpointKind != ConnectorEndpointKind.Source", occupancy,
            StringComparison.Ordinal);
        Assert.Contains("visualState.TargetAnchorId == anchorId", occupancy,
            StringComparison.Ordinal);
        Assert.Contains("excludedEndpointKind != ConnectorEndpointKind.Target", occupancy,
            StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(occupancy, "!isExcludedConnector ||"));
        Assert.Contains("ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(", candidates,
            StringComparison.Ordinal);
        Assert.Contains("reconnection.ConnectorVisualStateId", candidates,
            StringComparison.Ordinal);
        Assert.Contains("reconnection.EndpointKind", candidates, StringComparison.Ordinal);
        Assert.Contains(
            "referencedCandidateAnchorExemption: reconnection.OriginalAnchorId",
            candidates,
            StringComparison.Ordinal);
        Assert.Contains("anchor.Id != referencedCandidateAnchorExemption", anchorHandles,
            StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(", controller,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BpmnReconnectionCommandUpdatesExistingIdentitiesWithoutDeleteCreate()
    {
        var command = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Commands",
            "BpmnEndpointReconnectionCommands.cs");
        var handler = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Commands",
            "BpmnEndpointReconnectionCommandHandler.cs");
        var contribution = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "EndpointReconnection",
            "BpmnSequenceFlowEndpointReconnectionContribution.cs");
        var updatePath = command + handler + contribution;

        Assert.Contains("ReconnectBpmnSequenceFlowEndpointCommand :", command,
            StringComparison.Ordinal);
        Assert.Contains("ICommandPipelineInvalidation", command,
            StringComparison.Ordinal);
        Assert.Contains("AuthoritativeDocumentComponent.SemanticModel", command,
            StringComparison.Ordinal);
        Assert.Contains("AuthoritativeDocumentComponent.VisualModel", command,
            StringComparison.Ordinal);
        Assert.Contains("new ReconnectBpmnSequenceFlowEndpointCommand(", contribution,
            StringComparison.Ordinal);
        Assert.Contains("relationship!.Id", handler, StringComparison.Ordinal);
        Assert.Contains("connectorVisual!.Id", handler, StringComparison.Ordinal);
        Assert.Contains("relationship.Properties", handler, StringComparison.Ordinal);
        Assert.Contains("connectorVisual.Route", handler, StringComparison.Ordinal);
        Assert.Contains("connectorVisual.Properties", handler, StringComparison.Ordinal);
        Assert.Contains("connectorVisual.ConnectorAnchors", handler,
            StringComparison.Ordinal);
        Assert.Contains("BpmnDocumentReplacement.Replace(document, semanticModel, visualModel)",
            handler, StringComparison.Ordinal);

        string[] forbidden =
        [
            "CreateBpmnSequenceFlowCommand",
            "DeleteBpmnSequenceFlowCommand",
            "RemoveRelationship",
            "RemoveVisualState",
            "DeleteRelationship",
            "DeleteVisualState",
            "DocumentCreationIdentity",
            "Guid.NewGuid",
            "CommandProcessor",
            "HistoryManager",
            ".HandleAsync(",
            ".ExecuteAsync(",
            "RoutingEngine",
            "AddConnectorAnchorCommand",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, updatePath, StringComparison.Ordinal));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate '{startMarker}'.");
        return source[start..end];
    }

    private static string ReadProductionDirectory(string project, string directory) =>
        string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot, "src", project, directory),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, fileName));

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Inceptus.DocumentEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
