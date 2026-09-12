using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseL2NodeLabelArchitectureTests
{
    [Fact]
    public void AutomaticCenteringIsSceneDerivedAndAddsNoPersistentLabelSurface()
    {
        var sceneSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Composition.cs");
        var visualSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "Visuals",
            "VisualStateSnapshot.cs");

        Assert.Contains("nodeGeometry.TryGetValue(label.OwnerId", sceneSource, StringComparison.Ordinal);
        Assert.Contains("CreateAutomaticNodeLabelTransform(", sceneSource, StringComparison.Ordinal);
        Assert.Contains("localBounds.Width / 2d", sceneSource, StringComparison.Ordinal);
        Assert.Contains("localBounds.Height / 2d", sceneSource, StringComparison.Ordinal);
        Assert.Contains("Canvas2DTextAlignment.Center", sceneSource, StringComparison.Ordinal);
        Assert.Contains("Canvas2DTextBaseline.Middle", sceneSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Gamma", sceneSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LabelPosition", visualSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(VisualStateSnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.Name.Contains("Label", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AutomaticCenteringAndAllNodeLabelPreviewsRemainCommandFree()
    {
        var commands = typeof(ICommand).Assembly.GetTypes()
            .Where(type =>
                !type.IsAbstract &&
                typeof(ICommand).IsAssignableFrom(type) &&
                type.Name.Contains("Label", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var interactionSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var moveBody = Between(
            interactionSource,
            "private async ValueTask<Canvas2DInteractionResult> ExecuteNormalizedPointerMoveAsync(",
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerReleasedAsync(");

        Assert.Contains(typeof(MoveLabelCommand), commands);
        Assert.Contains(typeof(UpdateNodeLabelVisualOverrideCommand), commands);
        Assert.DoesNotContain("new MoveLabelCommand(", moveBody, StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"new {nameof(UpdateNodeLabelVisualOverrideCommand)}(",
            moveBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CommandProcessor", moveBody, StringComparison.Ordinal);
        Assert.DoesNotContain("History.ExecuteAsync", moveBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ResizeVisualStateCommand", moveBody, StringComparison.Ordinal);
        Assert.Contains("ApplyEditorStateAsync(", moveBody, StringComparison.Ordinal);
    }

    [Fact]
    public void RendererAndPresentationJavaScriptContainNoNodeLabelPlacementMeaning()
    {
        var rendererSource = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Canvas2D",
            "Rendering",
            "*.cs");
        var canvasJavaScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Canvas2D",
            "wwwroot",
            "inceptus.canvas2d.js"));
        var presentationJavaScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));
        string[] prohibitedPlacementMeaning =
        [
            "CenterLabel",
            "DrawNodeLabel",
            "NodeLabel",
            "label placement",
            "label-placement",
            "labelPosition",
            "label-center",
        ];

        Assert.DoesNotContain(prohibitedPlacementMeaning, fragment =>
            rendererSource.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(prohibitedPlacementMeaning, fragment =>
            canvasJavaScript.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(prohibitedPlacementMeaning, fragment =>
            presentationJavaScript.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("item.textAlignment", canvasJavaScript, StringComparison.Ordinal);
        Assert.Contains("item.textBaseline", canvasJavaScript, StringComparison.Ordinal);
    }

    [Fact]
    public void LabelCenteringHasNoBpmnSelectionPersistenceOrDocumentationChange()
    {
        var compositionSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Composition.cs");
        var gestureSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");

        Assert.DoesNotContain("Bpmn", compositionSource + gestureSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EditorState", Between(
            compositionSource,
            "private static void ComposePipelineItems(",
            "private static void ComposeEditorOverlays("), StringComparison.Ordinal);
        Assert.DoesNotContain("VisualStateSnapshot(", compositionSource, StringComparison.Ordinal);
    }

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate '{startMarker}'.");
        return source[start..end];
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
