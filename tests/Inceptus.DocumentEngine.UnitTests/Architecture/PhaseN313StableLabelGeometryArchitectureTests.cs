namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN313StableLabelGeometryArchitectureTests
{
    [Fact]
    public void LabelOverrideHandlerDeclaresSceneOnlyWithoutNodeGeometryOrRoutingMutation()
    {
        var source = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Commands",
            "UpdateNodeLabelVisualOverrideCommandHandler.cs");

        Assert.Contains(
            "pipelineInvalidation: PipelineInvalidation.Scene",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("NodeGeometryPipelineImpact", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Pinned", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateConnectionRoute", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ConnectorAnchor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateConnectorAnchor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Bpmn", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Gateway", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SceneOnlyArtifactCarryForwardIsGenericAndHasNoConcreteCommandSwitch()
    {
        var pipeline = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSession.Pipeline.cs");
        var artifacts = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSessionPipelineArtifacts.cs");
        var source = pipeline + artifacts;

        Assert.Contains("PipelineInvalidation.Scene", pipeline, StringComparison.Ordinal);
        Assert.Contains("RebindToCommittedRevision", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "UpdateNodeLabelVisualOverrideCommand",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Bpmn", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Gateway", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pinned", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutRoutingAndProjectionRemainUnawareOfPersistentLabelOverride()
    {
        var source = string.Concat(
            ReadProductionDirectory("Inceptus.DocumentEngine.Runtime", "Projection", "*.cs"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Runtime", "Layout", "*.cs"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Runtime", "Routing", "*.cs"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Bpmn", "Layout", "*.cs"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Bpmn", "Routing", "*.cs"));

        Assert.DoesNotContain("NodeLabelVisualOverride", source, StringComparison.Ordinal);
    }

    private static string ReadProductionDirectory(
        string project,
        string directory,
        string pattern)
    {
        var path = Path.Combine(RepositoryRoot, "src", project, directory);
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

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
