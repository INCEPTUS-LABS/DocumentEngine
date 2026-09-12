using Inceptus.DocumentEngine.Contracts.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN312StableNodeGeometryArchitectureTests
{
    [Fact]
    public void ManualGeometryHandlersDeclareOneNotationNeutralSelectiveAuthority()
    {
        var handlers = ReadProductionFile(
                "Inceptus.DocumentEngine.Runtime",
                "Commands",
                "MoveVisualStateCommandHandler.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Runtime",
                "Commands",
                "MoveVisualStatesCommandHandler.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Runtime",
                "Commands",
                "ResizeVisualStateCommandHandler.cs");
        var impactTypes = typeof(NodeGeometryPipelineImpact).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == typeof(NodeGeometryPipelineImpact).Namespace)
            .Where(type => type.Name.Contains("NodeGeometry", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal([typeof(NodeGeometryPipelineImpact)], impactTypes);
        Assert.Equal(3, Count(
            handlers,
            "CommandPipelineInvalidation.WithoutNodeLayout"));
        Assert.Equal(3, Count(
            handlers,
            "NodeGeometryPipelineImpact.ForChangedVisualStates"));
        Assert.DoesNotContain("Bpmn", handlers, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenericPipelineHasNoCommandNotationOrPinnedSwitches()
    {
        var pipeline = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSessionPipeline.cs");
        string[] forbidden =
        [
            "MoveVisualStateCommand",
            "MoveVisualStatesCommand",
            "ResizeVisualStateCommand",
            "Bpmn",
            "SemanticTypeId",
            "VisualPlacementMode.Pinned",
        ];

        Assert.All(forbidden, value => Assert.DoesNotContain(
            value,
            pipeline,
            StringComparison.OrdinalIgnoreCase));
        Assert.Contains("nodeGeometryImpact.HasExplicitChanges", pipeline,
            StringComparison.Ordinal);
        Assert.Contains("ChangedVisualStateIds", pipeline, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectiveLayoutIsCurrentSafeAndAlwaysRecomputesRoutingAndScene()
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
        Assert.Contains("candidate.ProjectedGraph.SourceRevision < currentGraph.SourceRevision",
            pipeline,
            StringComparison.Ordinal);
        Assert.Contains("candidate.LayoutResult.AlgorithmId == _configuration.LayoutAlgorithmId",
            pipeline,
            StringComparison.Ordinal);
        Assert.Contains("ProjectedGraphContentMatches", pipeline, StringComparison.Ordinal);
        Assert.Contains("NodeIdentitiesMatch", pipeline, StringComparison.Ordinal);
        Assert.Contains("RoutingEngine.Route(", pipeline, StringComparison.Ordinal);
        Assert.Contains("BuildSceneAsync(", pipeline, StringComparison.Ordinal);
    }

    [Fact]
    public void FullLayoutPathRemainsAvailableForInitialAndUnsafeOperations()
    {
        var pipeline = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSessionPipeline.cs");

        Assert.Contains("RunFullAsync(", pipeline, StringComparison.Ordinal);
        Assert.Contains("previousArtifacts: null", pipeline, StringComparison.Ordinal);
        Assert.Contains("LayoutEngine.Layout(", pipeline, StringComparison.Ordinal);
        Assert.Contains("if (layout is null)", pipeline, StringComparison.Ordinal);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            project,
            directory,
            fileName));

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

            throw new DirectoryNotFoundException(
                "Could not locate the repository root.");
        }
    }
}
