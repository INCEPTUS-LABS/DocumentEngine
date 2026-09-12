using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseM322ExternalNodeLabelArchitectureTests
{
    [Fact]
    public void PlacementContractIsPublicTransientNotationNeutralAndIntentionallySmall()
    {
        Assert.Equal(
            [NodeLabelPlacementKind.InsideCentered, NodeLabelPlacementKind.OutsideBelow],
            Enum.GetValues<NodeLabelPlacementKind>());
        Assert.Equal(
            typeof(ProjectedLabel).Assembly,
            typeof(NodeLabelPlacement).Assembly);

        var placementSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "Projection",
            "NodeLabelPlacement.cs");
        Assert.DoesNotContain("Bpmn", placementSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Gateway", placementSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OutsideAbove", placementSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OutsideLeft", placementSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OutsideRight", placementSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Manual", placementSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PlacementExistsOnlyInProjectionAndSceneInterpretationNotPersistentState()
    {
        var persistentSource = string.Concat(
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Documents", "*.cs"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Semantics", "*.cs"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Visuals", "*.cs"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Metadata", "*.cs"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "EditorState", "*.cs"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "History", "*.cs"));
        var sceneSource = string.Concat(
            ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "Scene",
                "Canvas2DSceneBuilder.TextLayout.cs"),
            ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "Scene",
                "Canvas2DSceneBuilder.Composition.cs"));

        Assert.DoesNotContain(nameof(NodeLabelPlacement), persistentSource, StringComparison.Ordinal);
        Assert.Contains(nameof(NodeLabelPlacementKind), sceneSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Bpmn", sceneSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RendererJavaScriptLayoutAndRoutingRemainPlacementUnaware()
    {
        var rendererSource = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Canvas2D",
            "Rendering",
            "*.cs");
        var layoutRoutingSource = string.Concat(
            ReadProductionFile(
                "Inceptus.DocumentEngine.Bpmn",
                "Layout",
                "BpmnLayoutAlgorithm.cs"),
            ReadProductionFile(
                "Inceptus.DocumentEngine.Bpmn",
                "Routing",
                "BpmnRoutingAlgorithm.cs"));
        var javaScript = string.Concat(
            File.ReadAllText(Path.Combine(
                RepositoryRoot,
                "src",
                "Inceptus.DocumentEngine.Canvas2D",
                "wwwroot",
                "inceptus.canvas2d.js")),
            File.ReadAllText(Path.Combine(
                RepositoryRoot,
                "src",
                "Inceptus.DocumentEngine.Bpmn.Blazor",
                "wwwroot",
                "inceptus.presentation.js")));

        Assert.DoesNotContain(nameof(NodeLabelPlacementKind), rendererSource, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(NodeLabelPlacementKind), javaScript, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(NodeLabelPlacementKind), layoutRoutingSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OutsideBelow", rendererSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OutsideBelow", javaScript, StringComparison.Ordinal);
        Assert.DoesNotContain("OutsideBelow", layoutRoutingSource, StringComparison.Ordinal);
    }

    [Fact]
    public void BpmnContributesPlacementButDoesNotMeasureWrapOrCreateGatewayText()
    {
        var projectionSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Projection",
            "BpmnProjectionRules.cs");
        var registrationSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            string.Empty,
            "BpmnPluginRegistration.cs");
        var contributorSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Scene",
            "BpmnCanvas2DSceneContributor.cs");

        Assert.Contains(nameof(NodeLabelPlacement), projectionSource, StringComparison.Ordinal);
        Assert.Contains("NodeLabelPlacementKind.OutsideBelow", registrationSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("MeasureText", projectionSource + registrationSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Wrap", projectionSource + registrationSource,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Canvas2DSceneGeometry.Text", contributorSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Approved?", contributorSource, StringComparison.Ordinal);
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
