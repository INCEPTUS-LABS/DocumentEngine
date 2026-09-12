using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN6TaskFamilyArchitectureTests
{
    [Fact]
    public void N6IsOneBpmnLocalTaskFamilyLayerOverAcceptedN5()
    {
        var n5 = BpmnPluginRegistration.N5;
        var n6 = BpmnPluginRegistration.N6;

        Assert.Equal(n5.CommandHandlers, n6.CommandHandlers);
        Assert.Equal(n5.CommandValidators, n6.CommandValidators);
        Assert.Equal(n5.HistoryPolicies, n6.HistoryPolicies);
        Assert.Equal(n5.LayoutAlgorithms, n6.LayoutAlgorithms);
        Assert.Equal(n5.RoutingAlgorithms, n6.RoutingAlgorithms);
        Assert.Equal(n5.SceneContributors, n6.SceneContributors);
        Assert.Equal(n5.ModelValidationRules, n6.ModelValidationRules);
        Assert.Equal(n5.ProjectionRules.Length + 5, n6.ProjectionRules.Length);
        Assert.Equal(n5.PropertiesSchemas.Length + 5, n6.PropertiesSchemas.Length);
        Assert.Equal(
            n5.ConnectorAnchorPolicies.Length + 5,
            n6.ConnectorAnchorPolicies.Length);
    }

    [Fact]
    public void RclCompositionPreservesN6ThroughTheAcceptedN100Registration()
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
    }

    [Fact]
    public void ReceiveTaskIsOnlyATaskAndNeverAnEventOrGateway()
    {
        Assert.True(BpmnTaskSemanticTypes.IsTask(BpmnSemanticTypes.ReceiveTask));

        var types = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Semantics",
            "BpmnSemanticTypes.cs");
        var gatewayBody = MethodBody(types, "internal static bool IsGateway");
        var catchBody = MethodBody(types, "public static bool IsCatchEvent");
        Assert.DoesNotContain("ReceiveTask", gatewayBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ReceiveTask", catchBody, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericFrameworkProjectsContainNoN6SemanticKnowledge()
    {
        string[] genericProjects =
        [
            "Inceptus.DocumentEngine.Contracts",
            "Inceptus.DocumentEngine.Runtime",
            "Inceptus.DocumentEngine.Canvas2D",
        ];
        string[] forbidden =
        [
            "BPMN.UserTask",
            "BPMN.ManualTask",
            "BPMN.ServiceTask",
            "BPMN.SendTask",
            "BPMN.ReceiveTask",
            "BpmnTaskSemanticTypes",
        ];
        foreach (var project in genericProjects)
        {
            var source = ReadProductionProject(project);
            Assert.All(forbidden, token =>
                Assert.DoesNotContain(token, source, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void N6IntroducesNoDuplicatePersistentKindRuntimeOrMessageModel()
    {
        var bpmn = ReadProductionProject("Inceptus.DocumentEngine.Bpmn");
        string[] forbidden =
        [
            "TaskKind =",
            "TaskKind {",
            "MessageRef",
            "MessageName",
            "BPMN.Message\"",
            "CorrelationId",
            "CandidateUsers",
            "CandidateGroups",
            "Assignee",
            "Subscription",
            "Instantiate",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, bpmn, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TaskMarkersRemainDerivedNonInteractiveSceneContent()
    {
        var scene = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Scene",
            "BpmnCanvas2DSceneContributor.cs");
        Assert.Contains("BpmnTaskMarkerPlacementPolicy.ResolveBounds", scene,
            StringComparison.Ordinal);
        Assert.Contains("Canvas2DSceneLayer.Decoration", scene, StringComparison.Ordinal);
        Assert.Contains("hitTestPolicy: Canvas2DHitTestPolicy.None", scene,
            StringComparison.Ordinal);
        Assert.DoesNotContain("new SemanticElementSnapshot", scene, StringComparison.Ordinal);
        Assert.DoesNotContain("new VisualStateSnapshot", scene, StringComparison.Ordinal);
        Assert.DoesNotContain("ICommand", scene, StringComparison.Ordinal);
    }

    [Fact]
    public void EventBasedModeIsDerivedByOneBpmnSequenceFlowAuthority()
    {
        var rules = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Semantics",
            "BpmnSequenceFlowConfigurationRules.cs");
        Assert.Contains("MessageCatchEvent", rules, StringComparison.Ordinal);
        Assert.Contains("ReceiveTask", rules, StringComparison.Ordinal);
        Assert.Contains("IsCatchEvent", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("EventBasedGateway.Mode", rules, StringComparison.Ordinal);

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
        Assert.Contains("ValidateEndpointSemantics", reconnect, StringComparison.Ordinal);
        Assert.Contains(".AnalyzeScope(document, activeScopeId)", validation,
            StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf(';', start);
        Assert.True(end > start);
        return source[start..end];
    }

    private static string ReadProductionProject(string project) =>
        string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot, "src", project),
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
