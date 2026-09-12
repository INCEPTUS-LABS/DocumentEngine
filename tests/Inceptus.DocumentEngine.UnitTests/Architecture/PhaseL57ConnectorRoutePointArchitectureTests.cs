using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseL57ConnectorRoutePointArchitectureTests
{
    [Fact]
    public void RoutePointEditsReuseOneAtomicVisualRouteCommandIncludingAutomaticRestoration()
    {
        var documentId = new DocumentId("test:l5-7");
        var visualStateId = new VisualStateId("test:connector");
        var automatic = new UpdateConnectionRouteCommand(
            documentId,
            DocumentRevision.Zero,
            visualStateId,
            []);
        var complete = new UpdateConnectionRouteCommand(
            documentId,
            DocumentRevision.Zero,
            visualStateId,
            [new PointD(0d, 0d), new PointD(10d, 10d)]);

        Assert.Empty(automatic.TargetRoute);
        Assert.Equal(2, complete.TargetRoute.Length);
        Assert.Equal(CommandCategory.Visual, automatic.Category);
        Assert.Equal(
            AuthoritativeDocumentComponent.VisualModel,
            automatic.AffectedComponents);
        Assert.Throws<ArgumentException>(() => new UpdateConnectionRouteCommand(
            documentId,
            DocumentRevision.Zero,
            visualStateId,
            [new PointD(0d, 0d)]));

        var commandSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "Commands",
            "UpdateConnectionRouteCommand.cs");
        var handlerSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Commands",
            "UpdateConnectionRouteCommandHandler.cs");
        var historySource = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "History",
            "UpdateConnectionRouteHistoryPolicy.cs");

        Assert.Contains("route.Length == 1", commandSource, StringComparison.Ordinal);
        Assert.Contains("existing.Properties", handlerSource, StringComparison.Ordinal);
        Assert.Contains("document.SemanticModel", handlerSource, StringComparison.Ordinal);
        Assert.Contains("oldState.Route", historySource, StringComparison.Ordinal);
        Assert.Contains("newState.Route", historySource, StringComparison.Ordinal);
        Assert.Contains("copiedRoute.Length == 1", historySource, StringComparison.Ordinal);
        Assert.DoesNotContain("new SemanticRelationshipSnapshot", handlerSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContextActionIsTransientTypedSceneTraceAndNeverPersistentPointIdentity()
    {
        Assert.Equal(
            ["AddPoint", "DeletePoint"],
            Enum.GetNames<Canvas2DConnectorRouteContextActionKind>());

        var properties = typeof(Canvas2DConnectorRouteContextAction).GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Contains(properties, property =>
            property.Name == "TargetVisualStateId" &&
            property.PropertyType == typeof(VisualStateId));
        Assert.Contains(properties, property =>
            property.Name == "SourceSceneObjectId" &&
            property.PropertyType == typeof(SceneObjectId));
        Assert.Contains(properties, property =>
            property.Name == "RouteIndex" && property.PropertyType == typeof(int));
        Assert.Contains(properties, property =>
            property.Name == "DocumentPoint" && property.PropertyType == typeof(PointD));
        Assert.Contains(properties, property =>
            property.Name == "RoutePoint" && property.PropertyType == typeof(PointD));
        Assert.DoesNotContain(properties, property => ContainsAny(
            property.Name,
            "SemanticSource",
            "SemanticTarget",
            "RelationshipEndpoint",
            "Dom"));
        Assert.NotNull(typeof(Canvas2DInteractionResult).GetProperty(
            nameof(Canvas2DInteractionResult.ConnectorRouteContextAction)));

        var actionSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DConnectorRouteContextAction.cs");
        Assert.Contains("TryResolveTargetRoute(", actionSource, StringComparison.Ordinal);
        Assert.Contains("item.Id == SourceSceneObjectId", actionSource, StringComparison.Ordinal);
        Assert.Contains("item.Origin.VisualStateId == TargetVisualStateId", actionSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ICommand", actionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteAsync", actionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SemanticRelationship", actionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AddUsesDocumentSpaceSegmentProjectionOrderingAndCentralizedProximityPolicy()
    {
        var controller = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var action = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DConnectorRouteContextAction.cs");
        var geometry = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DConnectorPathGeometry.cs");
        var configuration = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DConnectorInteractionConfiguration.cs");

        Assert.Contains("Canvas2DConnectorPathGeometry.FindNearest(", controller,
            StringComparison.Ordinal);
        Assert.Contains("projection.SegmentIndex", controller, StringComparison.Ordinal);
        Assert.Contains("projection.RoutePoint", controller, StringComparison.Ordinal);
        Assert.Contains("RoutePointProximityTolerance", controller, StringComparison.Ordinal);
        Assert.Contains("bestSegmentIndex = index", geometry, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(", geometry, StringComparison.Ordinal);
        Assert.Contains("Take(insertionIndex)", action, StringComparison.Ordinal);
        Assert.Contains("Append(projection)", action, StringComparison.Ordinal);
        Assert.Contains("Skip(insertionIndex)", action, StringComparison.Ordinal);
        Assert.Contains("ProjectOntoSegment(", action, StringComparison.Ordinal);
        Assert.Contains("RoutePointProximityTolerance", action, StringComparison.Ordinal);
        Assert.Contains("routePointProximityTolerance = 5d", configuration,
            StringComparison.Ordinal);
        Assert.DoesNotContain("DevicePixelRatio", action + geometry,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CssPoint", action + geometry, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstManualPointStaysSparseAndBendHandlesComeOnlyFromPersistentGuidance()
    {
        var action = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DConnectorRouteContextAction.cs");
        var composition = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Composition.cs");
        var gestures = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");
        var firstAdd = Between(
            action,
            "if (persistentRoute.IsEmpty)",
            "var editablePath = Canvas2DConnectorPathMetadata.ResolveEditable(connector)");
        var editablePath = Between(
            composition,
            "private static ImmutableArray<PointD> CreateEditableConnectorPath(",
            "private static (RectD Bounds, Matrix2D Transform) ResolveLabelGeometry(");
        var bendHandles = Between(
            gestures,
            "private static IEnumerable<Canvas2DSceneItem> CreateRouteBendHandles(",
            "private static IEnumerable<Canvas2DSceneItem> CreateConnectorEndpointHandles(");

        Assert.Contains(
            "targetRoute = [documentPath[0], projection, documentPath[^1]];",
            firstAdd,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Take(", firstAdd, StringComparison.Ordinal);
        Assert.DoesNotContain("Skip(", firstAdd, StringComparison.Ordinal);
        Assert.DoesNotContain("ToArray(", firstAdd, StringComparison.Ordinal);
        Assert.DoesNotContain("RoutingResult", action, StringComparison.Ordinal);

        Assert.Contains("path.Add(sourceAnchor);", editablePath, StringComparison.Ordinal);
        Assert.Contains(
            "persistentRoute.AsSpan(1, persistentRoute.Length - 2)",
            editablePath,
            StringComparison.Ordinal);
        Assert.Contains("path.Add(targetAnchor);", editablePath,
            StringComparison.Ordinal);
        Assert.Contains(
            "Canvas2DConnectorPathMetadata.ResolveEditable(target)",
            bendHandles,
            StringComparison.Ordinal);
        Assert.DoesNotContain("target.Geometry.Points", bendHandles, StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteAcceptsOnlyInternalBendMetadataAndHostSubmitsOneValidatedRouteCommand()
    {
        var controller = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var action = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DConnectorRouteContextAction.cs");
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");

        var resolution = Between(
            controller,
            "private static Canvas2DConnectorRouteContextAction?",
            "private static bool TryResolveContextBend(");
        var bendResolution = Between(
            controller,
            "private static bool TryResolveContextBend(",
            "private static double DistanceSquared(");

        Assert.Contains("IsConnectorEndpointHandle(hitItem)", resolution,
            StringComparison.Ordinal);
        Assert.Contains("return null;", resolution, StringComparison.Ordinal);
        Assert.Contains("Canvas2DConnectorRouteContextActionKind.DeletePoint", resolution,
            StringComparison.Ordinal);
        Assert.Contains("Canvas2DRouteGestureMetadata.BendRole", bendResolution,
            StringComparison.Ordinal);
        Assert.Contains("bendIndexValue.IntegerValue is < 1", bendResolution,
            StringComparison.Ordinal);
        Assert.Contains("bendIndex < Canvas2DConnectorPathMetadata.ResolveEditable(target).Length - 1",
            bendResolution,
            StringComparison.Ordinal);
        Assert.Contains("RouteIndex <= 0", action, StringComparison.Ordinal);
        Assert.Contains("RouteIndex >= persistentRoute.Length - 1", action,
            StringComparison.Ordinal);
        Assert.Contains("persistentRoute.RemoveAt(RouteIndex)", action,
            StringComparison.Ordinal);
        Assert.Contains("remainingRoute.Length == 2 ? [] : remainingRoute", action,
            StringComparison.Ordinal);
        Assert.Contains("ExecuteConnectorRouteContextActionAsync(", host,
            StringComparison.Ordinal);
        Assert.Contains("new UpdateConnectionRouteCommand(", host, StringComparison.Ordinal);
        Assert.Contains("session.ExecuteForSceneTargetAsync(", host,
            StringComparison.Ordinal);
        Assert.DoesNotContain("new VisualStateSnapshot(", host, StringComparison.Ordinal);
    }

    [Fact]
    public void MenuIsRoleSpecificAndRouteEditingDoesNotLeakIntoRendererJavaScriptOrScope()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var menu = component;
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

        Assert.Contains("DomId(\"properties-action\")", menu, StringComparison.Ordinal);
        Assert.Contains("DomId(\"add-connector-point-action\")", menu,
            StringComparison.Ordinal);
        Assert.Contains("DomId(\"delete-connector-point-action\")", menu,
            StringComparison.Ordinal);
        Assert.Contains("Canvas2DConnectorRouteContextActionKind.AddPoint", menu,
            StringComparison.Ordinal);
        Assert.Contains("Canvas2DConnectorRouteContextActionKind.DeletePoint", menu,
            StringComparison.Ordinal);
        Assert.DoesNotContain("InsertConnectionRoutePoint", renderer + canvasJavaScript +
            presentationJavaScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RemoveConnectionRoutePoint", renderer + canvasJavaScript +
            presentationJavaScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("route-point-proximity", canvasJavaScript + presentationJavaScript,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BpmnSemanticTypes", component + renderer,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnPluginRegistration", component + renderer,
            StringComparison.Ordinal);
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

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

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
