using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN311StableNodeGeometryArchitectureTests
{
    [Fact]
    public void GenericPipelineInvalidationIsNotationNeutralAndUnknownCommandsRemainConservative()
    {
        Assert.True(typeof(PipelineInvalidation).IsPublic);
        Assert.True(typeof(ICommandPipelineInvalidation).IsPublic);
        Assert.True(typeof(CommandPipelineInvalidation).IsPublic);
        var contracts = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Contracts",
            "Commands");
        Assert.DoesNotContain("Bpmn", contracts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SequenceFlow", contracts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("command is ICommandPipelineInvalidation", contracts,
            StringComparison.Ordinal);
        Assert.Contains(": Full", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void EditingSessionUsesDeclaredImpactWithoutCommandOrNotationSwitches()
    {
        var orchestration = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSession.Pipeline.cs");
        Assert.Contains("change.NodeGeometryImpact", orchestration,
            StringComparison.Ordinal);
        Assert.Contains("nodeGeometryImpact is not null", orchestration,
            StringComparison.Ordinal);
        string[] forbidden =
        [
            "CreateBpmn",
            "ReconnectBpmn",
            "SequenceFlow",
            "SemanticTypeId",
            "CommandTypeId ==",
        ];
        Assert.All(forbidden, token => Assert.DoesNotContain(
            token,
            orchestration,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PreservedLayoutIsRevisionAdvancedOnlyForTheSameStructuralGraph()
    {
        var pipeline = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSessionPipeline.cs");
        Assert.Contains("previousArtifacts.ProjectedGraph.DocumentId != currentGraph.DocumentId",
            pipeline,
            StringComparison.Ordinal);
        Assert.Contains("previousArtifacts.ProjectedGraph.SourceRevision >= currentGraph.SourceRevision",
            pipeline,
            StringComparison.Ordinal);
        Assert.Contains("GeometryMatchesGraph", pipeline, StringComparison.Ordinal);
        Assert.Contains("new LayoutResult(", pipeline, StringComparison.Ordinal);
        Assert.Contains("RoutingEngine.Route(", pipeline, StringComparison.Ordinal);
        Assert.Contains("BuildSceneAsync(", pipeline, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectorCommandsDeclareConnectorOnlyImpactAndNoPinnedWorkaroundExists()
    {
        Type[] connectorCommands =
        [
            typeof(AddConnectorAnchorCommand),
            typeof(RemoveConnectorAnchorCommand),
            typeof(UpdateConnectionRouteCommand),
            typeof(MoveLabelCommand),
            typeof(CreateBpmnSequenceFlowCommand),
            typeof(CreateBpmnSequenceFlowWithTargetAnchorCommand),
            typeof(ReconnectBpmnSequenceFlowEndpointCommand),
        ];
        Assert.All(connectorCommands, type => Assert.True(
            typeof(ICommandPipelineInvalidation).IsAssignableFrom(type),
            $"{type.Name} must declare its generic pipeline impact."));

        var implementation = ReadProductionDirectory(
                "Inceptus.DocumentEngine.Canvas2D",
                "EditingSession") +
            ReadProductionDirectory(
                "Inceptus.DocumentEngine.Bpmn",
                "Commands");
        Assert.DoesNotContain("PlacementMode = VisualPlacementMode.Pinned", implementation,
            StringComparison.Ordinal);
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
