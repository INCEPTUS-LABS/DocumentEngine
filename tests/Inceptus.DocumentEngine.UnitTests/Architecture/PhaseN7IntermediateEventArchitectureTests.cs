using Inceptus.DocumentEngine.Bpmn;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN7IntermediateEventArchitectureTests
{
    [Fact]
    public void N7IsOneBpmnLocalIntermediateEventLayerOverAcceptedN6()
    {
        var n6 = BpmnPluginRegistration.N6;
        var n7 = BpmnPluginRegistration.N7;

        Assert.Equal(n6.CommandHandlers.Length + 3, n7.CommandHandlers.Length);
        Assert.Equal(n6.CommandValidators.Length + 3, n7.CommandValidators.Length);
        Assert.Equal(n6.HistoryPolicies.Length + 3, n7.HistoryPolicies.Length);
        Assert.Equal(n6.ProjectionRules.Length + 3, n7.ProjectionRules.Length);
        Assert.Equal(n6.PropertiesSchemas.Length + 3, n7.PropertiesSchemas.Length);
        Assert.Equal(n6.ConnectorAnchorPolicies.Length + 3,
            n7.ConnectorAnchorPolicies.Length);
        Assert.Equal(n6.LayoutAlgorithms, n7.LayoutAlgorithms);
        Assert.Equal(n6.RoutingAlgorithms, n7.RoutingAlgorithms);
        Assert.Equal(n6.SceneContributors, n7.SceneContributors);
        Assert.Equal(n6.ModelValidationRules, n7.ModelValidationRules);
        Assert.Equal(n6.AnchorConnectionCreationRegistrations,
            n7.AnchorConnectionCreationRegistrations);
        Assert.Equal(n6.ConnectorEndpointReconnectionRegistrations,
            n7.ConnectorEndpointReconnectionRegistrations);
        Assert.Equal(n6.DiagramDeletionRegistrations,
            n7.DiagramDeletionRegistrations);
    }

    [Fact]
    public void GenericFrameworkProjectsContainNoN7SemanticKnowledge()
    {
        string[] genericProjects =
        [
            "Inceptus.DocumentEngine.Contracts",
            "Inceptus.DocumentEngine.Runtime",
            "Inceptus.DocumentEngine.Canvas2D",
        ];
        string[] forbidden =
        [
            "BPMN.MessageThrowEvent",
            "BPMN.SignalCatchEvent",
            "BPMN.SignalThrowEvent",
            "BpmnIntermediateEventSemanticTypes",
        ];
        foreach (var project in genericProjects)
        {
            var source = ReadProductionProject(project);
            Assert.All(forbidden, token =>
                Assert.DoesNotContain(token, source, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void N7IntroducesNoReferenceModelRuntimeOrPersistentCatchThrowFlag()
    {
        var bpmn = ReadProductionProject("Inceptus.DocumentEngine.Bpmn");
        string[] forbidden =
        [
            "MessageRef",
            "MessageName",
            "BPMN.Message\"",
            "SignalRef",
            "SignalName",
            "BPMN.Signal\"",
            "IsThrowing",
            "MessageBus",
            "SignalBus",
            "Correlation",
            "Subscription",
            "RuntimeDispatch",
            "ConditionalCatchEvent",
            "LinkCatchEvent",
            "LinkThrowEvent",
            "MessageStartEvent",
            "MessageEndEvent",
            "SignalStartEvent",
            "SignalEndEvent",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, bpmn, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MarkersRemainDerivedNonInteractiveAndShareOnePlacementPolicy()
    {
        var scene = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Scene",
            "BpmnCanvas2DSceneContributor.cs");
        Assert.Contains("BpmnIntermediateEventMarkerPlacementPolicy.ResolveMarkerBounds",
            scene,
            StringComparison.Ordinal);
        Assert.Contains("Canvas2DSceneLayer.Decoration", scene, StringComparison.Ordinal);
        Assert.Contains("hitTestPolicy: Canvas2DHitTestPolicy.None", scene,
            StringComparison.Ordinal);
        Assert.DoesNotContain("new SemanticElementSnapshot", scene,
            StringComparison.Ordinal);
        Assert.DoesNotContain("new VisualStateSnapshot", scene,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ICommand", scene, StringComparison.Ordinal);
    }

    [Fact]
    public void EventBasedEditingAndN5ShareTheSingleConfigurationAuthority()
    {
        var rules = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Semantics",
            "BpmnSequenceFlowConfigurationRules.cs");
        Assert.Contains("BpmnIntermediateEventSemanticTypes.IsCatchEvent", rules,
            StringComparison.Ordinal);
        Assert.Contains("ReceiveTask", rules, StringComparison.Ordinal);
        Assert.Contains("MessageCatchEvent", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageThrowEvent", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("SignalThrowEvent", rules, StringComparison.Ordinal);

        var creation = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Commands",
            "BpmnCreationValidation.cs");
        var reconnect = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Commands",
            "BpmnEndpointReconnectionValidation.cs");
        var validation = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Validation",
            "BpmnStructuralValidationRule.cs");
        Assert.Contains("BpmnSequenceFlowConfigurationRules.AnalyzeCandidate", creation,
            StringComparison.Ordinal);
        Assert.Contains("ValidateEndpointSemantics", reconnect,
            StringComparison.Ordinal);
        Assert.Contains(".AnalyzeScope(document, activeScopeId)", validation,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RclCompositionPreservesN7ThroughAcceptedN100WithoutSeededMessageSignalShowcaseNodes()
    {
        var program = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            string.Empty,
            "Program.cs");
        var pipeline = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            "Demo",
            "BpmnDemoPipeline.cs");
        var composition = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            string.Empty,
            "BpmnModelerComposition.cs");
        Assert.Contains("BpmnPluginRegistration.N100", composition,
            StringComparison.Ordinal);
        Assert.Contains("BpmnRegistration.ToolboxContributions", composition,
            StringComparison.Ordinal);
        Assert.Contains("OrganizationalPluginRegistration.Create", composition,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnPluginRegistration", program + pipeline,
            StringComparison.Ordinal);
        Assert.DoesNotContain("OrganizationalPluginRegistration", program + pipeline,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBpmnMessageThrowEventCommand", pipeline,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBpmnSignalCatchEventCommand", pipeline,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBpmnSignalThrowEventCommand", pipeline,
            StringComparison.Ordinal);
    }

    private static string ReadProductionProject(string project) =>
        string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot, "src", project),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
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
                if (File.Exists(Path.Combine(directory.FullName,
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
