using Inceptus.DocumentEngine.Contracts.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseL52WrappedNodeLabelArchitectureTests
{
    [Fact]
    public void WrappingIsDerivedBySceneConstructionFromApprovedTextMetrics()
    {
        var builder = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.cs");
        var layout = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DTextLayoutService.cs");
        var configuration = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DNodeLabelLayoutConfiguration.cs");

        Assert.Contains("BuildMeasuredAsync(", builder, StringComparison.Ordinal);
        Assert.Contains("ITextMetricsService textMetrics", builder, StringComparison.Ordinal);
        Assert.Contains("CreateMeasuredNodeLabelLayoutsAsync(", builder, StringComparison.Ordinal);
        Assert.Contains("_metricsService.MeasureAsync(", layout, StringComparison.Ordinal);
        Assert.Contains("WrapExplicitLineAsync(", layout, StringComparison.Ordinal);
        Assert.Contains("BreakLongTokenAsync(", layout, StringComparison.Ordinal);
        Assert.Contains("Replace(\"\\r\\n\", \"\\n\"", layout, StringComparison.Ordinal);
        Assert.Contains("EnumerateRunes()", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("characterCount", layout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("averageCharacter", layout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("horizontalPadding = 8d", configuration, StringComparison.Ordinal);
        Assert.Contains("verticalPadding = 8d", configuration, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultPipelineUsesTheMeasuredScenePathWithoutChangingTheFrozenPublicBuilderContract()
    {
        var builder = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.cs");
        var pipeline = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSessionPipeline.cs");
        var renderer = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Rendering",
            "Canvas2DRenderer.cs");

        Assert.Contains("internal async ValueTask<Canvas2DSceneBuildResult> BuildMeasuredAsync(",
            builder, StringComparison.Ordinal);
        Assert.Contains("_configuration.SceneBuilder.BuildMeasuredAsync(", pipeline,
            StringComparison.Ordinal);
        Assert.Contains("_textMeasurementCache.TryGetValue(request", renderer,
            StringComparison.Ordinal);
        Assert.Contains("TextMeasurementCacheCapacity", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("public ValueTask<Canvas2DSceneBuildResult> BuildAsync(", builder,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", builder + pipeline,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PersistentModelsAndCommandsOwnNeitherWrappedLinesNorClippingState()
    {
        var semanticModels = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Contracts",
            "Semantics");
        var visualModels = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Contracts",
            "Visuals");
        var editorState = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Contracts",
            "EditorState");
        var commandTypes = typeof(ICommand).Assembly.GetTypes()
            .Where(type =>
                !type.IsAbstract &&
                typeof(ICommand).IsAssignableFrom(type))
            .Select(static type => type.Name)
            .ToArray();

        string[] forbiddenPersistentMeaning =
        [
            "WrappedText",
            "WrappedLine",
            "LabelLine",
            "TextClip",
            "LabelClip",
        ];
        Assert.DoesNotContain(forbiddenPersistentMeaning, fragment =>
            (semanticModels + visualModels + editorState).Contains(
                fragment,
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(commandTypes, static name =>
            name.Contains("Wrap", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Reflow", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RendererAndJavaScriptContainNoNodeWrappingSemantics()
    {
        var renderer = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Canvas2D",
            "Rendering");
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
        string[] forbidden =
        [
            "WrapText",
            "LayoutNodeLabel",
            "BreakLongToken",
            "label-line:",
            "wrapped-label",
        ];

        Assert.DoesNotContain(forbidden, fragment =>
            renderer.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(forbidden, fragment =>
            canvasJavaScript.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(forbidden, fragment =>
            presentationJavaScript.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("_metricsService.MeasureAsync(", ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DTextLayoutService.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ResizePreviewReflowIsSceneOnlyAndZoomAndDprDoNotEnterLogicalLayout()
    {
        var textLayout = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DTextLayoutService.cs");
        var measuredLabels = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.TextLayout.cs");
        var gestures = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");
        var interaction = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var pointerMove = Between(
            interaction,
            "private async ValueTask<Canvas2DInteractionResult> ExecuteNormalizedPointerMoveAsync(",
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerReleasedAsync(");

        Assert.Contains("Canvas2DResizeGeometry.CalculateBounds(", measuredLabels,
            StringComparison.Ordinal);
        Assert.Contains("ResizePreview", measuredLabels, StringComparison.Ordinal);
        Assert.Contains("ApplyEditorStateAsync(", pointerMove, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandProcessor", pointerMove, StringComparison.Ordinal);
        Assert.DoesNotContain("ResizeVisualStateCommand", pointerMove, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateSemanticElementNameCommand", pointerMove,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Viewport", textLayout + measuredLabels,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DevicePixelRatio", textLayout + measuredLabels + gestures,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WrappedLinesRetainGenericOwnerTraceAndPhaseAddsNoBpmnSerializationOrDocs()
    {
        var composition = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Composition.cs");
        var layoutSources = ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "Scene",
                "Canvas2DSceneBuilder.TextLayout.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "Scene",
                "Canvas2DTextLayoutService.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "Scene",
                "Canvas2DNodeLabelLayoutConfiguration.cs");

        Assert.Contains("CreateMeasuredNodeLabelItems(", composition, StringComparison.Ordinal);
        Assert.Contains("CreateOrigin(", composition, StringComparison.Ordinal);
        Assert.Contains("Canvas2DSceneLayer.Label", composition, StringComparison.Ordinal);
        Assert.Contains("Canvas2DHitTestMode.Bounds", composition, StringComparison.Ordinal);
        Assert.Contains("label-line:", layoutSources, StringComparison.Ordinal);
        Assert.DoesNotContain("Bpmn", layoutSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Serialize", layoutSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Json", layoutSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            ChangedPaths().Where(static path =>
                !path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)),
            static path =>
                path.Contains("Serialization", StringComparison.OrdinalIgnoreCase) &&
                    !ApprovedN102Changes.IsApprovedSerializationPath(path));
    }

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate '{startMarker}'.");
        return source[start..end];
    }

    private static string ReadProductionDirectory(string project, string directory)
    {
        var path = Path.Combine(RepositoryRoot, "src", project, directory);
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, fileName));

    private static string[] ChangedPaths() =>
        RunGit("status --short")
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Length > 3
                ? line[3..].Trim().Replace('\\', '/')
                : line.Trim())
            .ToArray();

    private static string RunGit(string arguments)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }

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
