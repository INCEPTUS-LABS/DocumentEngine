using System.Reflection;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseLRouteInteractionArchitectureTests
{
    [Fact]
    public void BendHandlesAndRoutePreviewAreSceneBuilderOwnedAndTransient()
    {
        var sceneSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");
        var controllerSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");

        Assert.Contains("CreateRouteBendHandles(", sceneSource, StringComparison.Ordinal);
        Assert.Contains("ComposeRouteGesturePreview(", sceneSource, StringComparison.Ordinal);
        Assert.Contains("route-bend-handle:", sceneSource, StringComparison.Ordinal);
        Assert.Contains("route-preview:", sceneSource, StringComparison.Ordinal);
        Assert.Contains("Canvas2DSceneLayer.Overlay", sceneSource, StringComparison.Ordinal);
        Assert.Contains("Canvas2DHitTestPolicy.None", sceneSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new Canvas2DSceneItem(", controllerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RoutePreviewTraversalExecutesNoCommandOrModelMutation()
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
            "UpdateConnectionRouteCommand",
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
    public void RouteCompletionUsesOneCommandThroughActiveEditingSessionProcessor()
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

        Assert.Equal(1, Count(releaseBody, "new UpdateConnectionRouteCommand("));
        Assert.Equal(1, Count(releaseBody, "_session.CompletePersistentGestureAsync("));
        Assert.DoesNotContain("new CommandProcessor(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RouteCommandChangesOnlyVisualRouteAndExposesNoEndpointSemantics()
    {
        var properties = typeof(UpdateConnectionRouteCommand).GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Contains(properties, property => property.Name == "TargetRoute");
        Assert.DoesNotContain(properties, property => ContainsAny(
            property.Name,
            "Endpoint",
            "Port",
            "Relationship",
            "Semantic",
            "Source",
            "TargetElement"));
        var signatureTypes = properties.SelectMany(property => ExpandType(property.PropertyType))
            .Concat(typeof(UpdateConnectionRouteCommand)
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .SelectMany(parameter => ExpandType(parameter.ParameterType)))
            .Distinct()
            .ToArray();
        Assert.DoesNotContain(signatureTypes, type =>
            type.Assembly == typeof(CommandProcessor).Assembly ||
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Canvas2D", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Blazor", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Contracts.EditorState", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void L3AddsNoRendererJavaScriptBpmnGenericToolOrEndpointMeaning()
    {
        var rendererMethods = typeof(Canvas2DRenderer).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsSpecialName)
            .Select(static method => method.Name)
            .ToArray();
        var javaScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));
        var phaseLSource = ReadProductionDirectory(
                "Inceptus.DocumentEngine.Canvas2D",
                "Interaction") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "Scene",
                "Canvas2DSceneBuilder.Gestures.cs");
        string[] forbiddenJavaScriptMeaning =
        [
            "UpdateConnectionRouteCommand",
            "VisualState",
            "EditorState",
            "SceneObjectId",
            "route-bend-handle",
            "route-preview",
            "hitTest",
        ];
        string[] forbiddenFrameworks =
        [
            "GestureFramework",
            "InputDeviceManager",
            "ToolManager",
            "ToolRegistry",
            "ToolSelector",
            "ReconnectEndpoint",
            "ChangeRelationshipEndpoint",
        ];

        Assert.DoesNotContain(rendererMethods, method => ContainsAny(
            method,
            "Gesture",
            "Pointer",
            "RouteBend",
            "VisualState"));
        Assert.DoesNotContain(forbiddenJavaScriptMeaning, fragment =>
            javaScript.Contains(fragment, StringComparison.Ordinal));
        Assert.DoesNotContain(forbiddenFrameworks, fragment =>
            phaseLSource.Contains(fragment, StringComparison.Ordinal));
        Assert.DoesNotContain("Bpmn", phaseLSource + javaScript, StringComparison.OrdinalIgnoreCase);
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
