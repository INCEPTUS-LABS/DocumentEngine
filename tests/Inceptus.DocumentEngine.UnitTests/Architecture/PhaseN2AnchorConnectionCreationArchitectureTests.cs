using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN2AnchorConnectionCreationArchitectureTests
{
    [Fact]
    public void GenericConnectionContractsArePublicNeutralAndIndependentAuthorities()
    {
        Type[] contracts =
        [
            typeof(AnchorConnectionCreationId),
            typeof(AnchorConnectionCreationSourceRequest),
            typeof(AnchorConnectionCreationRequest),
            typeof(IAnchorConnectionCreationCommandFactory),
            typeof(AnchorConnectionCreationPlan),
            typeof(AnchorConnectionCreationPlanResult),
            typeof(AnchorConnectionCreationRegistration),
            typeof(AnchorConnectionCreationCatalog),
            typeof(DocumentCreationIdentity),
            typeof(IDocumentCreationIdentityProvider),
            typeof(ConnectorAnchorOccupancy),
        ];
        Assert.All(contracts, static type => Assert.True(type.IsPublic));
        Assert.NotEqual(typeof(AnchorConnectionCreationCatalog).Namespace,
            typeof(Inceptus.DocumentEngine.Contracts.Toolbox.ToolboxPlacementCatalog)
                .Namespace);

        var source = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Contracts",
            "ConnectionCreation") +
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Creation");
        Assert.DoesNotContain("Bpmn", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Canvas2D", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandProcessor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoryManager", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistentEndpointOccupancyIsOneGenericAuthorityAtEveryBoundary()
    {
        var occupancy = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "Visuals",
            "ConnectorAnchorOccupancy.cs");
        var invariant = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Documents",
            "DocumentInvariantValidator.cs");
        var controller = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var scene = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Composition.cs");

        Assert.Contains("visualState.SourceAnchorId", occupancy,
            StringComparison.Ordinal);
        Assert.Contains("visualState.TargetAnchorId", occupancy,
            StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorOccupancy", invariant, StringComparison.Ordinal);
        Assert.Contains("EnumerateEndpointReferences", invariant, StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorOccupancy.IsOccupied", controller,
            StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorOccupancy", scene, StringComparison.Ordinal);
        Assert.DoesNotContain("Bpmn", occupancy + invariant + controller + scene,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NearestFree", controller + scene,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AddConnectorAnchorCommand", controller + scene,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GenericGestureOwnsInteractionOnlyAndDelegatesOnePersistentCommand()
    {
        var controller = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        Assert.Contains("AnchorConnectionCreationCatalog", controller,
            StringComparison.Ordinal);
        Assert.Contains("CommandFactory.CreatePlan(", controller,
            StringComparison.Ordinal);
        Assert.Contains("_session.CompletePersistentGestureAsync(", controller,
            StringComparison.Ordinal);
        Assert.Contains("CreatedConnectorVisualStateId", controller,
            StringComparison.Ordinal);
        string[] forbidden =
        [
            "BPMN.",
            "Bpmn",
            "CreateBpmnSequenceFlowCommand",
            "AddConnectorAnchorCommand",
            "RoutingEngine",
            "BpmnRoutingAlgorithm",
            "new CommandProcessor(",
            "new HistoryManager(",
            "UpdateConnectionEndpoint",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, controller, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PreviewAndCandidateTargetsRemainTransientSceneState()
    {
        var scene = ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "Scene",
                "Canvas2DSceneBuilder.Composition.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "Scene",
                "Canvas2DSceneBuilder.Gestures.cs");
        Assert.Contains("ConnectionTargetCandidate", scene, StringComparison.Ordinal);
        Assert.Contains("Canvas2DHitTestPolicy.None", scene, StringComparison.Ordinal);
        Assert.Contains("Canvas2DSceneGeometry.Path([gesture.Origin, gesture.Current])",
            scene,
            StringComparison.Ordinal);
        string[] forbidden =
        [
            "AddConnectorAnchorCommand",
            "RoutingEngine",
            "HistoryManager",
            "ExecuteAsync(",
            "SemanticModel",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, scene, StringComparison.Ordinal));
    }

    [Fact]
    public void BrowserBoundaryRemainsScalarAndIdentityProviderIsSharedWithN1()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));
        string[] forbidden =
        [
            "BPMN",
            "SequenceFlow",
            "ConnectorAnchorRole",
            "AnchorConnectionCreation",
            "SemanticElementId",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, script, StringComparison.OrdinalIgnoreCase));

        var placement = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "ToolboxPlacementController.cs");
        var composition = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasComposition.cs");
        Assert.Contains("IDocumentCreationIdentityProvider", placement,
            StringComparison.Ordinal);
        Assert.Contains("IDocumentCreationIdentityProvider", composition,
            StringComparison.Ordinal);
        Assert.DoesNotContain("IToolboxPlacementIdentityProvider", placement,
            StringComparison.Ordinal);
        Assert.DoesNotContain("IToolboxPlacementIdentityProvider", composition,
            StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(DocumentCanvasHost).Assembly.GetTypes(), type =>
            type.Name.Contains("ToolboxPlacementIdentityProvider", StringComparison.Ordinal));
    }

    private static string ReadProductionDirectory(string project, string directory) =>
        string.Concat(Directory
            .GetFiles(
                Path.Combine(RepositoryRoot, "src", project, directory),
                "*.cs",
                SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));

    private static string ReadProductionFile(
        string project,
        string directory,
        string file) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, file));

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "Inceptus.DocumentEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
