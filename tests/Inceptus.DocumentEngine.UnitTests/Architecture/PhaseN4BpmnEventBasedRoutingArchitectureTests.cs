using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN4BpmnEventBasedRoutingArchitectureTests
{
    private static readonly string[] GenericProjectNames =
    [
        "Inceptus.DocumentEngine.Contracts",
        "Inceptus.DocumentEngine.Runtime",
        "Inceptus.DocumentEngine.Canvas2D",
    ];

    [Fact]
    public void N4IsAnExplicitPluginCompositionLayerOverN314()
    {
        var n314 = BpmnPluginRegistration.N314;
        var n4 = BpmnPluginRegistration.N4;
        Assert.Equal(n314.CommandHandlers, n4.CommandHandlers.Take(n314.CommandHandlers.Length));
        Assert.Equal(n314.CommandValidators,
            n4.CommandValidators.Take(n314.CommandValidators.Length));
        Assert.Equal(n314.HistoryPolicies, n4.HistoryPolicies.Take(n314.HistoryPolicies.Length));
        Assert.Equal(n314.ProjectionRules, n4.ProjectionRules.Take(n314.ProjectionRules.Length));
        Assert.Equal(n314.LayoutAlgorithms, n4.LayoutAlgorithms);
        Assert.Equal(n314.RoutingAlgorithms, n4.RoutingAlgorithms);
        Assert.Equal(n314.SceneContributors, n4.SceneContributors);
        Assert.Equal(n314.DiagramDeletionRegistrations, n4.DiagramDeletionRegistrations);
        Assert.Equal(3, n4.CommandHandlers.Length - n314.CommandHandlers.Length);
        Assert.Equal(3, n4.ProjectionRules.Length - n314.ProjectionRules.Length);
        Assert.Equal(3, n4.PropertiesSchemas.Length - n314.PropertiesSchemas.Length);
        Assert.Equal(3,
            n4.ConnectorAnchorPolicies.Length - n314.ConnectorAnchorPolicies.Length);
    }

    [Fact]
    public void EventBasedTargetRestrictionHasOneBpmnSemanticAuthority()
    {
        var creation = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Commands",
            "BpmnCreationValidation.cs");
        var reconnection = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Commands",
            "BpmnEndpointReconnectionValidation.cs");
        var smartTarget = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "ConnectionCreation",
            "BpmnSequenceFlowConnectionCreationContribution.cs");

        Assert.Contains(
            "BpmnSequenceFlowConfigurationRules.AnalyzeCandidate(",
            creation,
            StringComparison.Ordinal);
        Assert.Contains(
            "BpmnSequenceFlowConfigurationRules",
            creation,
            StringComparison.Ordinal);
        Assert.Contains(
            "BpmnSequenceFlowCreationValidation.ValidateEndpointSemantics(",
            reconnection,
            StringComparison.Ordinal);
        Assert.Contains(
            "BpmnSequenceFlowCreationValidation.ValidateEndpointSemantics(",
            smartTarget,
            StringComparison.Ordinal);
        Assert.DoesNotContain("EventBasedGateway", reconnection, StringComparison.Ordinal);
        Assert.DoesNotContain("EventBasedGateway", smartTarget, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericFrameworkAndRendererRemainNotationNeutral()
    {
        var genericSource = string.Join(
            Environment.NewLine,
            GenericProjectNames.Select(ReadProductionProject));
        string[] n4Types =
        [
            BpmnSemanticTypes.MessageCatchEvent.Value,
            BpmnSemanticTypes.TimerCatchEvent.Value,
            BpmnSemanticTypes.EventBasedGateway.Value,
            nameof(CreateBpmnMessageCatchEventCommand),
            nameof(CreateBpmnTimerCatchEventCommand),
            nameof(CreateBpmnEventBasedGatewayCommand),
        ];
        Assert.All(n4Types, token =>
            Assert.DoesNotContain(token, genericSource, StringComparison.OrdinalIgnoreCase));

        var renderer = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Rendering",
            "Canvas2DRenderer.cs");
        Assert.DoesNotContain("Bpmn", renderer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void N4ContainsNoCancelledConditionDefaultExecutionOrSimulationSemantics()
    {
        var bpmn = ReadProductionProject("Inceptus.DocumentEngine.Bpmn");
        string[] forbidden =
        [
            "ConditionExpression",
            "DefaultSequenceFlowId",
            "SequenceFlow.IsDefault",
            "ActivatedFlows",
            "ActivationRule",
            "DecisionRule",
            "timer scheduler",
            "message correlation",
            "first-event-wins",
            "token waiting",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, bpmn, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void N4VisualsUseSceneContributionsAndNotPersistentMarkerGeometry()
    {
        var scene = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Scene",
            "BpmnCanvas2DSceneContributor.cs");
        var commands = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Bpmn",
            "Commands");
        Assert.Contains("Canvas2DSceneGeometry.Ellipse", scene, StringComparison.Ordinal);
        Assert.Contains("Canvas2DSceneGeometry.Path", scene, StringComparison.Ordinal);
        Assert.Contains("Canvas2DHitTestPolicy.None", scene, StringComparison.Ordinal);
        Assert.DoesNotContain("Canvas2DSceneGeometry", commands, StringComparison.Ordinal);
        Assert.DoesNotContain("RegularPolygon", commands, StringComparison.Ordinal);
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
