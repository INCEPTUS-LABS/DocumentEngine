using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseL55ConnectorPropertiesAndLabelArchitectureTests
{
    [Fact]
    public void ConnectorDataUsesTheExistingTypedSemanticPropertyCommandForRelationships()
    {
        var handler = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Commands",
            "UpdateSemanticElementPropertyCommandHandler.cs");
        var history = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "History",
            "UpdateSemanticElementPropertyHistoryPolicy.cs");
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var properties = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasProperties.cs");

        Assert.Contains("TryGetElement", handler, StringComparison.Ordinal);
        Assert.Contains("TryGetRelationship", handler, StringComparison.Ordinal);
        Assert.Contains("new SemanticRelationshipSnapshot(", handler, StringComparison.Ordinal);
        Assert.Contains("document.SemanticModel.Relationships.Select", handler,
            StringComparison.Ordinal);
        Assert.Contains("TryGetRelationship", history, StringComparison.Ordinal);
        Assert.Contains("new UpdateSemanticElementPropertyCommand(", host,
            StringComparison.Ordinal);
        Assert.Contains("currentField.Definition.MutationKind switch", host,
            StringComparison.Ordinal);
        Assert.Contains("SemanticPropertyMutationKind.Property", host,
            StringComparison.Ordinal);
        Assert.Contains("currentField.Definition.SemanticPropertyKey", host,
            StringComparison.Ordinal);
        Assert.Contains("changedDataField.TryCreateTargetValue", host,
            StringComparison.Ordinal);
        Assert.Contains("PropertyValue.FromText(EditorValue)", properties,
            StringComparison.Ordinal);
        Assert.Contains("TryGetRelationship", properties, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateConnectorNameCommand", handler + history + host,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateConnectorDescriptionCommand", handler + history + host,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectorPlacementIsRouteRelativeVisualStateOwnedAndCommandDriven()
    {
        var placement = typeof(ConnectorLabelPlacement);
        var command = typeof(MoveLabelCommand);
        var handler = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Commands",
            "MoveLabelCommandHandler.cs");

        Assert.Equal(typeof(double), placement.GetProperty("PathPosition")!.PropertyType);
        Assert.Equal(
            "Inceptus.DocumentEngine.Contracts.Geometry.VectorD",
            placement.GetProperty("Offset")!.PropertyType.FullName);
        Assert.Null(placement.GetProperty("X"));
        Assert.Null(placement.GetProperty("Y"));
        Assert.Equal(0.5d, ConnectorLabelPlacement.Default.PathPosition);
        Assert.Equal(-12d, ConnectorLabelPlacement.Default.Offset.Y);
        Assert.Equal(CommandCategory.Visual,
            new MoveLabelCommand(
                new("test:l5-5"),
                default,
                new("test:connector"),
                ConnectorLabelPlacement.Default).Category);
        Assert.Contains("existing.Properties", handler, StringComparison.Ordinal);
        Assert.Contains("ConnectorLabelPlacement.UpdateProperties", handler,
            StringComparison.Ordinal);
        Assert.Contains("document.SemanticModel", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("new SemanticRelationshipSnapshot", handler,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectionAndSceneDeriveConnectorLabelTextAndAbsolutePosition()
    {
        var demo = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            "Demo",
            "NeutralDemoPipeline.cs");
        var layout = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.TextLayout.cs");
        var path = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DConnectorPathGeometry.cs");
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

        Assert.Contains("new ProjectedLabel(", demo, StringComparison.Ordinal);
        Assert.Contains("relationship.Relationship.Properties", demo,
            StringComparison.Ordinal);
        Assert.Contains("Canvas2DTextLayoutService", layout, StringComparison.Ordinal);
        Assert.Contains("Canvas2DConnectorPathGeometry.ResolvePoint", layout,
            StringComparison.Ordinal);
        Assert.Contains("placement.Offset", layout, StringComparison.Ordinal);
        Assert.Contains("totalLength", path, StringComparison.Ordinal);
        Assert.Contains("bestLength / totalLength", path, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectorLabelPlacement", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("connector-label:path-position", canvasJavaScript,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connector-label:path-position", presentationJavaScript,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LabelDragIsSceneOnlyUntilOneMoveLabelCommandCompletes()
    {
        var interaction = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var gestures = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");
        var pointerMove = Between(
            interaction,
            "private async ValueTask<Canvas2DInteractionResult> ExecuteNormalizedPointerMoveAsync(",
            "private async ValueTask<Canvas2DInteractionResult> ExecutePendingPointerMoveUnderGateAsync(");
        var pointerRelease = Between(
            interaction,
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerReleasedAsync(",
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerCancelledAsync(");

        Assert.Contains("PersistentGestureKind.LabelMove", interaction,
            StringComparison.Ordinal);
        Assert.Contains("Canvas2DConnectorPathGeometry.FindNearest", interaction,
            StringComparison.Ordinal);
        Assert.Contains("ApplyEditorStateAsync", pointerMove, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveLabelCommand", pointerMove, StringComparison.Ordinal);
        Assert.Contains("new MoveLabelCommand(", pointerRelease, StringComparison.Ordinal);
        Assert.Contains("CompletePersistentGestureAsync", pointerRelease,
            StringComparison.Ordinal);
        Assert.Contains("ComposeConnectorLabelGesturePreview", gestures,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CommandProcessor", gestures, StringComparison.Ordinal);
        Assert.DoesNotContain("Routing", Between(
            gestures,
            "private static bool ComposeConnectorLabelGesturePreview(",
            "private static IEnumerable<KeyValuePair<string, PropertyValue>> CreateRouteProperties("),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectorPropertiesShowApprovedDataAndKeepPlacementInternal()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var data = Between(
            component,
            "<fieldset data-property-group=\"data\">",
            "</fieldset>");
        var schemas = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            "Demo",
            "NeutralDemoPropertiesSchemas.cs");
        var connectorSchema = Between(
            schemas,
            "NeutralDemoPipeline.NeutralEdgeTypeId,",
            "    ];");

        Assert.Contains("@foreach (var field in draft.DataFields)", data,
            StringComparison.Ordinal);
        Assert.Contains("data-property-field-id=\"@field.FieldId.Value\"", data,
            StringComparison.Ordinal);
        Assert.DoesNotContain("IsConnector", data, StringComparison.Ordinal);
        Assert.Contains("NameFieldId", connectorSchema, StringComparison.Ordinal);
        Assert.Contains("DescriptionFieldId", connectorSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementNumberFieldId", connectorSchema,
            StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(
            connectorSchema,
            "SemanticPropertyMutationKind.Property"));
        foreach (var id in new[]
                 {
                     "properties-label-path-position",
                     "properties-label-offset-x",
                     "properties-label-offset-y",
                 })
        {
            Assert.DoesNotContain($"id=\"@DomId(\"{id}\")\"", component, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PhaseAddsNoBpmnSerializationDocumentationOrDependencyChanges()
    {
        var phaseSources = ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "Visuals",
                "ConnectorLabelPlacement.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "Commands",
                "MoveLabelCommand.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "Scene",
                "Canvas2DConnectorPathGeometry.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "Scene",
                "Canvas2DConnectorLabelLayoutConfiguration.cs");

        Assert.DoesNotContain("Bpmn", phaseSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Json", phaseSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Serialize", phaseSources, StringComparison.OrdinalIgnoreCase);
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

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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
