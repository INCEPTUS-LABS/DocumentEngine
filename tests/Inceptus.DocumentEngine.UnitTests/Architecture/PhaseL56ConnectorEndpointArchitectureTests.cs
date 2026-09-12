using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseL56ConnectorEndpointArchitectureTests
{
    [Fact]
    public void StraightConnectorHitTestingAcceptsTwoPointsAndUsesOneLogicalTolerancePolicy()
    {
        var composition = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Composition.cs");
        var geometry = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "Canvas2D",
            "Canvas2DSceneGeometry.cs");
        var hitTesting = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "HitTesting",
            "Canvas2DSceneHitTestService.cs");
        var policy = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DConnectorInteractionConfiguration.cs");

        Assert.Equal(5d,
            Canvas2DConnectorInteractionConfiguration.Default.PathHitTolerance);
        Assert.Contains("var minimumCount = isClosed ? 3 : 2;", geometry,
            StringComparison.Ordinal);
        Assert.Contains("Canvas2DConnectorInteractionConfiguration.Default.PathHitTolerance",
            composition, StringComparison.Ordinal);
        Assert.Contains("for (var index = 1; index < localPoints.Count; index++)", hitTesting,
            StringComparison.Ordinal);
        Assert.Contains("if (lengthSquared == 0d)", hitTesting, StringComparison.Ordinal);
        Assert.Contains("Configuration.MinimumStrokeTolerance", hitTesting,
            StringComparison.Ordinal);
        Assert.Contains("item.HitTestPolicy.StrokeTolerance", hitTesting,
            StringComparison.Ordinal);
        Assert.Contains("halfStrokeWidth", hitTesting, StringComparison.Ordinal);
        Assert.Contains("document-space interaction policy", policy,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectedConnectorEndpointsAreExactlyTwoDeterministicEditorStateSceneItems()
    {
        var composition = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Composition.cs");
        var gestures = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");
        var endpointMethod = Between(
            gestures,
            "private static IEnumerable<Canvas2DSceneItem> CreateConnectorEndpointHandles(",
            "private static bool ComposeRouteGesturePreview(");

        Assert.Equal("StartEndpoint", Canvas2DConnectorEndpointMetadata.StartEndpointRole);
        Assert.Equal("EndEndpoint", Canvas2DConnectorEndpointMetadata.EndEndpointRole);
        Assert.Contains(
            "foreach (var endpointHandle in CreateConnectorEndpointHandles(",
            composition, StringComparison.Ordinal);
        Assert.Contains("target,", composition, StringComparison.Ordinal);
        Assert.Contains("editorState.ActiveGesture", composition, StringComparison.Ordinal);
        Assert.Contains("var logicalPath = Canvas2DConnectorPathMetadata.Resolve(target);",
            endpointMethod, StringComparison.Ordinal);
        Assert.Contains("logicalPath,", endpointMethod, StringComparison.Ordinal);
        Assert.Contains("isStart: true", endpointMethod, StringComparison.Ordinal);
        Assert.Contains("isStart: false", endpointMethod, StringComparison.Ordinal);
        Assert.Contains("logicalPath[0]", endpointMethod, StringComparison.Ordinal);
        Assert.Contains("logicalPath[^1]", endpointMethod, StringComparison.Ordinal);
        Assert.Contains("Canvas2DSceneObjectIdentity.ForEditorState(stableKey)", endpointMethod,
            StringComparison.Ordinal);
        Assert.Contains("$\"connector-endpoint-handle:{target.Id.Value}:{role}\"",
            endpointMethod, StringComparison.Ordinal);
        Assert.Contains("Canvas2DSceneOriginCategory.EditorState", endpointMethod,
            StringComparison.Ordinal);
        Assert.Contains("[target.Id]", endpointMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("BendPoints", endpointMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateConnectionRouteCommand", endpointMethod,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteAsync", endpointMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void EndpointHandlesWinOverBendsLabelsAndPathAndDispatchReconnectBeforeCreation()
    {
        var gestures = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");
        var endpointMethod = Between(
            gestures,
            "private static IEnumerable<Canvas2DSceneItem> CreateConnectorEndpointHandles(",
            "private static bool ComposeRouteGesturePreview(");
        var interaction = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var pointerDown = Between(
            interaction,
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerPressedAsync(",
            "private async ValueTask<Canvas2DInteractionResult> ExecuteNormalizedPointerMoveAsync(");
        var endpointReconnection = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.EndpointReconnection.cs");

        Assert.Contains(
            "Canvas2DConnectorEndpointMetadata.StartEndpointZIndex",
            endpointMethod,
            StringComparison.Ordinal);
        Assert.Contains(
            "Canvas2DConnectorEndpointMetadata.EndEndpointZIndex",
            endpointMethod,
            StringComparison.Ordinal);
        Assert.Contains("Canvas2DSceneLayer.Overlay", endpointMethod, StringComparison.Ordinal);
        Assert.Contains(
            "Canvas2DRouteGestureMetadata.HandleZIndex",
            gestures,
            StringComparison.Ordinal);
        Assert.DoesNotContain("5100 + index", gestures, StringComparison.Ordinal);
        Assert.True(
            Canvas2DConnectorEndpointMetadata.StartEndpointZIndex >
            Canvas2DRouteGestureMetadata.HandleZIndex);
        Assert.True(
            Canvas2DConnectorEndpointMetadata.EndEndpointZIndex >
            Canvas2DRouteGestureMetadata.HandleZIndex);
        Assert.Contains("IsConnectorEndpointHandle(hitItem)", pointerDown,
            StringComparison.Ordinal);
        Assert.Contains("? null", pointerDown, StringComparison.Ordinal);
        var reconnectDispatch = pointerDown.IndexOf(
            "TryStartConnectorEndpointReconnectionUnderGateAsync(",
            StringComparison.Ordinal);
        var creationDispatch = pointerDown.IndexOf(
            "TryStartAnchorConnectionUnderGateAsync(",
            StringComparison.Ordinal);
        Assert.True(reconnectDispatch >= 0 && creationDispatch > reconnectDispatch);
        Assert.Contains("IsConnectorEndpointHandle(hitItem)", endpointReconnection,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ChangeRelationshipEndpoint", interaction,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EndpointPathBendAndLabelNormalizationAllRemainVisualStateOwned()
    {
        var interaction = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var endpointMetadata = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DConnectorEndpointMetadata.cs");

        Assert.Contains("ResolveSelectableVisualStateId", interaction,
            StringComparison.Ordinal);
        Assert.Contains("ResolveCanonicalSceneTarget", interaction,
            StringComparison.Ordinal);
        Assert.Contains("TargetSceneObjectId", endpointMetadata, StringComparison.Ordinal);
        Assert.Contains("TargetVisualStateId", endpointMetadata, StringComparison.Ordinal);
        Assert.DoesNotContain("SemanticElementId", endpointMetadata, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentRevision", endpointMetadata, StringComparison.Ordinal);
        Assert.DoesNotContain("Command", endpointMetadata, StringComparison.Ordinal);
    }

    [Fact]
    public void EndpointsDoNotLeakIntoRendererJavaScriptBpmnSerializationOrFrozenDocumentation()
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
        var endpointSources = ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "Interaction",
                "Canvas2DConnectorEndpointMetadata.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "Scene",
                "Canvas2DConnectorInteractionConfiguration.cs");

        Assert.DoesNotContain("connector-endpoint-role", renderer,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connector-endpoint-role", canvasJavaScript,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connector-endpoint-role", presentationJavaScript,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bpmn", endpointSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Json", endpointSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Serialize", endpointSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            ChangedPaths().Where(static path =>
                !path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)),
            static path =>
                path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) &&
                    !ApprovedOrganizationalProjectChanges.IsApproved(path) &&
                    !ApprovedN104Changes.IsApprovedProjectPath(path) &&
                    !ApprovedP11PackageFoundationChanges.IsApprovedProjectPath(path) &&
                    !ApprovedP12ReusableBpmnBlazorChanges.IsApprovedProjectPath(path) ||
                path.EndsWith("packages.lock.json", StringComparison.OrdinalIgnoreCase) ||
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
