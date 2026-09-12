using System.Reflection;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseLResizeInteractionArchitectureTests
{
    [Fact]
    public void ResizeInteractionRegionsAndPreviewsAreOwnedBySceneBuilderAndRemainTransient()
    {
        var sceneSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");
        var resizeRegionSource = Between(
            sceneSource,
            "private static IEnumerable<Canvas2DSceneItem> CreateResizeHandles(",
            "private static bool ComposeMoveGesturePreview(");
        var interactionSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");

        Assert.Contains("CreateResizeHandles(", sceneSource, StringComparison.Ordinal);
        Assert.Contains("Canvas2DResizeGeometry.Directions", sceneSource, StringComparison.Ordinal);
        Assert.Contains("Canvas2DResizeGeometry.InteractionBounds(", sceneSource, StringComparison.Ordinal);
        Assert.Contains("ComposeResizeGesturePreview(", sceneSource, StringComparison.Ordinal);
        Assert.Contains("resize-handle:", sceneSource, StringComparison.Ordinal);
        Assert.Contains("resize-preview:", sceneSource, StringComparison.Ordinal);
        Assert.Contains("Canvas2DSceneLayer.Overlay", sceneSource, StringComparison.Ordinal);
        Assert.Contains("Canvas2DHitTestPolicy.None", sceneSource, StringComparison.Ordinal);
        Assert.Contains(
            "target.Origin.Categories | Canvas2DSceneOriginCategory.EditorState",
            resizeRegionSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Bounds)",
            resizeRegionSource,
            StringComparison.Ordinal);
        Assert.Equal(2, Count(resizeRegionSource, "new Canvas2DSceneStyle(opacity: 0d)"));
        Assert.Contains("CreateNodeLabelResizeZones(", resizeRegionSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("style: isCorner", resizeRegionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("fill:", resizeRegionSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stroke:", resizeRegionSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#ffffff", resizeRegionSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#2563eb", resizeRegionSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new Canvas2DSceneItem(", interactionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ResizeUsesOneTransientDirectionTypeAndOneSharedGeometryAlgorithm()
    {
        var directions = Enum.GetValues<Canvas2DResizeDirection>();
        Assert.Equal(8, directions.Length);
        Assert.False(typeof(Canvas2DResizeDirection).IsPublic);
        Assert.Equal(
            "Inceptus.DocumentEngine.Canvas2D.Interaction",
            typeof(Canvas2DResizeDirection).Namespace);

        var geometrySource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DResizeGeometry.cs");
        var interactionSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var sceneSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");

        Assert.Equal(2, Count(geometrySource, "internal static RectD CalculateBounds("));
        Assert.Contains("double minimumWidth", geometrySource, StringComparison.Ordinal);
        Assert.Contains("double minimumHeight", geometrySource, StringComparison.Ordinal);
        Assert.Contains("Canvas2DResizeGeometry.CalculateBounds(", interactionSource, StringComparison.Ordinal);
        Assert.Contains("Canvas2DResizeGeometry.CalculateBounds(", sceneSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CalculateResizeBounds(", interactionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CalculateResizeBounds(", sceneSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResizeNorth", interactionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResizeSouth", interactionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResizeEast", interactionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ResizeWest", interactionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactlyOnePersistentResizeCommandExistsAndDirectionDoesNotLeakIntoItOrVisualState()
    {
        var commands = typeof(ICommand).Assembly.GetTypes()
            .Where(type =>
                !type.IsAbstract &&
                typeof(ICommand).IsAssignableFrom(type) &&
                type.Name.Contains("Resize", StringComparison.Ordinal))
            .ToArray();
        var directionType = typeof(Canvas2DResizeDirection);

        Assert.Equal([typeof(ResizeVisualStateCommand)], commands);
        Assert.DoesNotContain(
            GetPublicSignatureTypes(typeof(ResizeVisualStateCommand)),
            type => type == directionType);
        Assert.DoesNotContain(
            GetPublicSignatureTypes(typeof(VisualStateSnapshot)),
            type => type == directionType);

        var commandSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "Commands",
            "ResizeVisualStateCommand.cs");
        var visualSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "Visuals",
            "VisualStateSnapshot.cs");
        Assert.DoesNotContain(nameof(Canvas2DResizeDirection), commandSource, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(Canvas2DResizeDirection), visualSource, StringComparison.Ordinal);
        Assert.DoesNotContain("resize-handle", commandSource + visualSource, StringComparison.Ordinal);
        Assert.DoesNotContain("resize-edge-zone", commandSource + visualSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CornerZOrderWinsOverEdgeZonesAndCursorMeaningStaysInDotNet()
    {
        foreach (var corner in new[]
                 {
                     Canvas2DResizeDirection.NorthWest,
                     Canvas2DResizeDirection.NorthEast,
                     Canvas2DResizeDirection.SouthWest,
                     Canvas2DResizeDirection.SouthEast,
                 })
        {
            Assert.True(Canvas2DResizeGeometry.IsCorner(corner));
            Assert.True(Canvas2DResizeGeometry.ZIndex(corner) >
                Canvas2DResizeGeometry.ZIndex(Canvas2DResizeDirection.North));
            Assert.True(Canvas2DResizeGeometry.ZIndex(corner) >
                Canvas2DResizeGeometry.ZIndex(Canvas2DResizeDirection.East));
            Assert.True(Canvas2DResizeGeometry.ZIndex(corner) >
                Canvas2DResizeGeometry.ZIndex(Canvas2DResizeDirection.South));
            Assert.True(Canvas2DResizeGeometry.ZIndex(corner) >
                Canvas2DResizeGeometry.ZIndex(Canvas2DResizeDirection.West));
        }

        Assert.Equal("ns-resize", Canvas2DResizeGeometry.CssCursor(Canvas2DResizeDirection.North));
        Assert.Equal("ns-resize", Canvas2DResizeGeometry.CssCursor(Canvas2DResizeDirection.South));
        Assert.Equal("ew-resize", Canvas2DResizeGeometry.CssCursor(Canvas2DResizeDirection.East));
        Assert.Equal("ew-resize", Canvas2DResizeGeometry.CssCursor(Canvas2DResizeDirection.West));
        Assert.Equal("nesw-resize", Canvas2DResizeGeometry.CssCursor(Canvas2DResizeDirection.NorthEast));
        Assert.Equal("nesw-resize", Canvas2DResizeGeometry.CssCursor(Canvas2DResizeDirection.SouthWest));
        Assert.Equal("nwse-resize", Canvas2DResizeGeometry.CssCursor(Canvas2DResizeDirection.NorthWest));
        Assert.Equal("nwse-resize", Canvas2DResizeGeometry.CssCursor(Canvas2DResizeDirection.SouthEast));

        var javaScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));
        string[] forbiddenDirectionMeaning =
        [
            "Canvas2DResizeDirection",
            "northwest",
            "northeast",
            "southwest",
            "southeast",
            "ns-resize",
            "ew-resize",
            "nesw-resize",
            "nwse-resize",
        ];
        Assert.DoesNotContain(forbiddenDirectionMeaning, fragment =>
            javaScript.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("style.cursor", javaScript, StringComparison.Ordinal);
    }

    [Fact]
    public void LogicalResizeGeometryHasNoDprBrowserModelBpmnOrSerializationDependency()
    {
        var geometrySource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DResizeGeometry.cs");
        var directionSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DResizeDirection.cs");
        var combined = geometrySource + directionSource;
        string[] forbidden =
        [
            "DevicePixelRatio",
            "PixelRatio",
            "CanvasRenderingContext2D",
            "HTMLCanvasElement",
            "ClientX",
            "ClientY",
            "VisualModel",
            "SemanticModel",
            "System.Text.Json",
            "JsonConverter",
            "Bpmn",
        ];

        Assert.DoesNotContain(forbidden, fragment =>
            combined.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResizePreviewTraversalCreatesNoCommandOrAuthoritativeMutation()
    {
        var source = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var moveBody = Between(
            source,
            "private async ValueTask<Canvas2DInteractionResult> ExecuteNormalizedPointerMoveAsync(",
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerReleasedAsync(");
        string[] forbidden =
        [
            "CommandProcessor",
            "CompletePersistentGestureAsync",
            "History.ExecuteAsync",
            "ResizeVisualStateCommand",
            "DocumentState",
            "Interlocked.CompareExchange",
            "new SemanticModelSnapshot",
            "new VisualModelSnapshot",
        ];

        Assert.DoesNotContain(forbidden, fragment =>
            moveBody.Contains(fragment, StringComparison.Ordinal));
        Assert.Contains("ApplyEditorStateAsync(", moveBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ResizeCompletionUsesOneCommandThroughActiveEditingSessionProcessor()
    {
        var fields = typeof(Canvas2DInteractionController).GetFields(
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Single(fields, field => field.FieldType == typeof(EditingSession));
        Assert.DoesNotContain(fields, field => field.FieldType == typeof(CommandProcessor));
        Assert.Single(
            typeof(EditingSession).GetFields(BindingFlags.NonPublic | BindingFlags.Instance),
            field => field.FieldType == typeof(CommandProcessor));

        var source = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var releaseBody = Between(
            source,
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerReleasedAsync(",
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerCancelledAsync(");
        var sessionSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSession.Interaction.cs");

        Assert.Equal(1, Count(releaseBody, "new ResizeVisualStateCommand("));
        Assert.Equal(1, Count(releaseBody, "_session.CompletePersistentGestureAsync("));
        Assert.DoesNotContain("new CommandProcessor(", source, StringComparison.Ordinal);
        Assert.Contains("History.ExecuteAsync(", sessionSource, StringComparison.Ordinal);
        Assert.Contains("_commandProcessor,", sessionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ResizeCommandHasNoCanvasEditorBrowserOrRuntimeDependency()
    {
        var signatureTypes = GetPublicSignatureTypes(typeof(ResizeVisualStateCommand))
            .Distinct()
            .ToArray();

        Assert.DoesNotContain(signatureTypes, type =>
            type.Assembly == typeof(CommandProcessor).Assembly ||
            type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Canvas2D",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Blazor",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Contracts.EditorState",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "Microsoft.AspNetCore.Components",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void CursorOnlyResizeAddsNoRendererJavaScriptBpmnDocumentationOrGenericToolMeaning()
    {
        var rendererMethods = typeof(Canvas2DRenderer).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsSpecialName)
            .Select(static method => method.Name)
            .ToArray();
        var presentationJavaScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));
        var canvasJavaScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Canvas2D",
            "wwwroot",
            "inceptus.canvas2d.js"));
        var rendererSource = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Canvas2D",
            "Rendering");
        var phaseLSource = ReadProductionDirectory(
                "Inceptus.DocumentEngine.Canvas2D",
                "Interaction") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "Scene",
                "Canvas2DSceneBuilder.Gestures.cs");
        string[] forbiddenJavaScriptMeaning =
        [
            "ResizeVisualStateCommand",
            "VisualState",
            "EditorState",
            "SceneObjectId",
            "resize-handle",
            "resize-preview",
            "hitTest",
        ];
        string[] forbiddenRendererResizeMeaning =
        [
            "Canvas2DResizeDirection",
            "Canvas2DResizeGestureMetadata",
            "resize-handle",
            "resize-edge-zone",
            "ns-resize",
            "ew-resize",
            "nesw-resize",
            "nwse-resize",
        ];
        string[] forbiddenGenericTools =
        [
            "GestureFramework",
            "InputDeviceManager",
            "ToolManager",
            "ToolRegistry",
            "ToolSelector",
        ];

        Assert.DoesNotContain(rendererMethods, method => ContainsAny(
            method,
            "Gesture",
            "Pointer",
            "ResizeVisual",
            "VisualState"));
        Assert.DoesNotContain(forbiddenJavaScriptMeaning, fragment =>
            presentationJavaScript.Contains(fragment, StringComparison.Ordinal));
        Assert.DoesNotContain(forbiddenRendererResizeMeaning, fragment =>
            rendererSource.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(forbiddenRendererResizeMeaning, fragment =>
            canvasJavaScript.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(forbiddenRendererResizeMeaning, fragment =>
            presentationJavaScript.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(forbiddenGenericTools, fragment =>
            phaseLSource.Contains(fragment, StringComparison.Ordinal));
        Assert.DoesNotContain(
            "Bpmn",
            phaseLSource + rendererSource + canvasJavaScript + presentationJavaScript,
            StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Type> GetPublicSignatureTypes(Type type)
    {
        yield return type;
        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                foreach (var expanded in ExpandType(parameter.ParameterType))
                {
                    yield return expanded;
                }
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var expanded in ExpandType(property.PropertyType))
            {
                yield return expanded;
            }
        }
    }

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;
        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var expanded in ExpandType(argument))
            {
                yield return expanded;
            }
        }
    }

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate '{startMarker}'.");
        return source[start..end];
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
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
