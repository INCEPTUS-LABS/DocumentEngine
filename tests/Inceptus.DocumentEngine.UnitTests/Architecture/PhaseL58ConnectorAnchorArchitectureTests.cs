using System.Reflection;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseL58ConnectorAnchorArchitectureTests
{
    [Fact]
    public void AnchorsAreTypedPersistentVisualDataWithOneCanonicalGeometryResolver()
    {
        var id = new ConnectorAnchorId("test:anchor");
        var anchor = new ConnectorAnchor(
            id,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Target,
            0);
        var visual = new VisualStateSnapshot(
            new VisualStateId("test:node"),
            new SemanticElementId("test:semantic"),
            new PointD(100d, 200d),
            new SizeD(200d, 100d),
            VisualPlacementMode.Manual,
            connectorAnchors: [anchor]);

        Assert.Equal(anchor, Assert.Single(visual.ConnectorAnchors));
        Assert.Null(visual.SourceAnchorId);
        Assert.Null(visual.TargetAnchorId);
        Assert.Equal(
            [0.25d, 0.5d, 0.75d],
            ConnectorAnchorGeometryResolver.Distribute(3).AsEnumerable());
        Assert.Equal(
            new PointD(300d, 250d),
            ConnectorAnchorGeometryResolver.ResolvePoint(
                new RectD(100d, 200d, 200d, 100d),
                ConnectorAnchorSide.Right,
                order: 1,
                count: 3));

        var anchorProperties = typeof(ConnectorAnchor).GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(
            ["Id", "Order", "Role", "Side"],
            anchorProperties.Select(static property => property.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(anchorProperties, static property => ContainsAny(
            property.Name,
            "Position",
            "Boundary",
            "Point",
            "X",
            "Y",
            "Css",
            "Pixel"));
    }

    [Fact]
    public void AddAndRemoveAreOneGenericVisualCommandEachWithHistoryAndNoDirectUiMutation()
    {
        var documentId = new DocumentId("test:l5-8");
        var visualId = new VisualStateId("test:node");
        var anchorId = new ConnectorAnchorId("test:anchor");
        var add = new AddConnectorAnchorCommand(
            documentId,
            DocumentRevision.Zero,
            visualId,
            anchorId,
            ConnectorAnchorSide.Bottom,
            ConnectorAnchorRole.Source,
            0);
        var remove = new RemoveConnectorAnchorCommand(
            documentId,
            DocumentRevision.Zero,
            visualId,
            anchorId);

        Assert.Equal(CommandCategory.Visual, add.Category);
        Assert.Equal(CommandCategory.Visual, remove.Category);
        Assert.Equal(AuthoritativeDocumentComponent.VisualModel, add.AffectedComponents);
        Assert.Equal(AuthoritativeDocumentComponent.VisualModel, remove.AffectedComponents);

        var addHandler = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Commands",
            "AddConnectorAnchorCommandHandler.cs");
        var removeHandler = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Commands",
            "RemoveConnectorAnchorCommandHandler.cs");
        var history = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Runtime",
            "History");
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");

        Assert.Contains("ConnectorAnchorInsertion.Insert(", addHandler, StringComparison.Ordinal);
        Assert.Contains(
            "anchor.Order + 1",
            ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "Visuals",
                "ConnectorAnchorInsertion.cs"),
            StringComparison.Ordinal);
        Assert.Contains("anchor.Order - 1", removeHandler, StringComparison.Ordinal);
        Assert.Contains("AddConnectorAnchorHistoryPolicy", history, StringComparison.Ordinal);
        Assert.Contains("RemoveConnectorAnchorHistoryPolicy", history, StringComparison.Ordinal);
        Assert.Contains("ExecuteForSceneTargetAsync", host, StringComparison.Ordinal);
        Assert.Contains("new AddConnectorAnchorCommand(", host, StringComparison.Ordinal);
        Assert.Contains("new RemoveConnectorAnchorCommand(", host, StringComparison.Ordinal);
        Assert.DoesNotContain("new VisualStateSnapshot(", component, StringComparison.Ordinal);
        Assert.DoesNotContain("SemanticModelSnapshot", component + host, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionAndRoutingReusePortsAndCanonicalAnchorGeometry()
    {
        var pipeline = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            "Demo",
            "NeutralDemoPipeline.cs");
        var routingEngine = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Routing",
            "RoutingEngine.cs");

        Assert.Contains("new ProjectedPort(", pipeline, StringComparison.Ordinal);
        Assert.Contains("sourcePortId:", pipeline, StringComparison.Ordinal);
        Assert.Contains("targetPortId:", pipeline, StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorGeometryResolver.ResolvePoint(", pipeline,
            StringComparison.Ordinal);
        Assert.Contains("edge.SourcePortId", pipeline, StringComparison.Ordinal);
        Assert.Contains("edge.TargetPortId", pipeline, StringComparison.Ordinal);
        Assert.Contains("source.Right", pipeline, StringComparison.Ordinal);
        Assert.Contains("target.Left", pipeline, StringComparison.Ordinal);
        Assert.Contains("ValidatePort(", routingEngine, StringComparison.Ordinal);
        Assert.DoesNotContain("BoundaryPosition", pipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("DevicePixelRatio", pipeline, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedNodeHelpersReuseResizeEdgesAndRemainTransientAndNonDraggable()
    {
        var scene = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");
        var controller = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var action = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DConnectorAnchorContextAction.cs");

        Assert.Contains("CreateConnectorAnchorHandles(", scene, StringComparison.Ordinal);
        Assert.Contains("Canvas2DSceneObjectIdentity.ForEditorState", scene,
            StringComparison.Ordinal);
        Assert.Contains("anchor.Id.Value", scene, StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorGeometryResolver.ResolvePoint(", scene,
            StringComparison.Ordinal);
        Assert.Contains("TryResolveResizeTarget(", controller, StringComparison.Ordinal);
        Assert.Contains("Canvas2DResizeGeometry.IsCorner(direction)", controller,
            StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorGeometryResolver.ResolveEdgeParameter(", controller,
            StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorGeometryResolver.ResolveInsertionIndex(", controller,
            StringComparison.Ordinal);
        Assert.Contains("IsConnectorAnchorHandle(hitItem)", controller, StringComparison.Ordinal);
        Assert.Contains("moveTarget =", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ICommand", action, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteAsync", action, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectedArrowIsDerivedFromFinalRoutedSegmentBeforeRendererAndNormalizesToConnector()
    {
        var composition = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Composition.cs");
        var geometry = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DConnectorArrowGeometry.cs");
        var controller = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var renderer = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Canvas2D",
            "Rendering");
        var canvasJavaScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Canvas2D",
            "wwwroot",
            "inceptus.canvas2d.js"));

        Assert.Contains("Canvas2DConnectorArrowGeometry.Create(logicalPath)", composition,
            StringComparison.Ordinal);
        Assert.Contains("routedPath[^1]", geometry, StringComparison.Ordinal);
        Assert.Contains("index = routedPath.Count - 2", geometry, StringComparison.Ordinal);
        Assert.Contains("isClosed: true", geometry, StringComparison.Ordinal);
        Assert.Contains("connector-target-arrow", composition, StringComparison.Ordinal);
        Assert.Contains("Canvas2DConnectorArrowMetadata.TargetArrow", controller,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectorArrow", renderer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("arrow", canvasJavaScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SourceAnchorId", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetAnchorId", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentBoundaryEnforcesAnchorOwnershipRoleReferencesAndUsedRemoval()
    {
        var invariant = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Documents",
            "DocumentInvariantValidator.cs");
        var removal = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Commands",
            "RemoveConnectorAnchorCommandHandler.cs");
        var cloner = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Documents",
            "DocumentSnapshotCloner.cs");

        Assert.Contains("VisualConnectorAnchorIdentityDuplicateCode", invariant,
            StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorRole.Source", invariant, StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorRole.Target", invariant, StringComparison.Ordinal);
        Assert.Contains("relationship.SourceId", invariant, StringComparison.Ordinal);
        Assert.Contains("relationship.TargetId", invariant, StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorInUse", removal, StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorOccupancy.IsOccupied", removal,
            StringComparison.Ordinal);
        Assert.Contains("visualState.ConnectorAnchors", cloner, StringComparison.Ordinal);
        Assert.Contains("visualState.SourceAnchorId", cloner, StringComparison.Ordinal);
        Assert.Contains("visualState.TargetAnchorId", cloner, StringComparison.Ordinal);
    }

    [Fact]
    public void PhaseRemainsGenericCanvasIndependentUpstreamAndLeavesFrozenScopeUntouched()
    {
        var changedProduction = ChangedPaths()
            .Where(static path => path.StartsWith("src/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var contractsAndRuntime = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Contracts",
            string.Empty) + ReadProductionDirectory(
            "Inceptus.DocumentEngine.Runtime",
            string.Empty);
        var presentationJavaScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));

        Assert.DoesNotContain("Canvas2D", ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "Visuals",
            "ConnectorAnchor.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Bpmn", contractsAndRuntime, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connector-anchor", presentationJavaScript,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(changedProduction, static path =>
            path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) &&
                !ApprovedOrganizationalProjectChanges.IsApproved(path) &&
                !ApprovedN104Changes.IsApprovedProjectPath(path) &&
                !ApprovedP11PackageFoundationChanges.IsApprovedProjectPath(path) &&
                !ApprovedP12ReusableBpmnBlazorChanges.IsApprovedProjectPath(path) ||
            path.EndsWith("packages.lock.json", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Serialization", StringComparison.OrdinalIgnoreCase) &&
                !ApprovedN102Changes.IsApprovedSerializationPath(path));
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
