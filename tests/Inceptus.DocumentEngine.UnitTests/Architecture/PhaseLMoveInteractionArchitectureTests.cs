using System.Reflection;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseLMoveInteractionArchitectureTests
{
    [Fact]
    public void MovePreviewUsesOnlyTransientSessionStateAndCreatesNoCommand()
    {
        var controllerSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var pressBody = Between(
            controllerSource,
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerPressedAsync(",
            "private async ValueTask<Canvas2DInteractionResult> ExecuteNormalizedPointerMoveAsync(");
        var moveBody = Between(
            controllerSource,
            "private async ValueTask<Canvas2DInteractionResult> ExecuteNormalizedPointerMoveAsync(",
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerReleasedAsync(");
        string[] forbiddenPreviewOperations =
        [
            "CommandProcessor",
            "History.ExecuteAsync",
            "MoveVisualStateCommand",
            "CompleteMoveGestureAsync",
            "DocumentState",
            "Interlocked.CompareExchange",
            "new SemanticModelSnapshot",
            "new VisualModelSnapshot",
        ];

        Assert.DoesNotContain(forbiddenPreviewOperations, operation =>
            pressBody.Contains(operation, StringComparison.Ordinal) ||
            moveBody.Contains(operation, StringComparison.Ordinal));
        Assert.Contains("ApplyEditorStateAsync(", pressBody, StringComparison.Ordinal);
        Assert.Contains("ApplyEditorStateAsync(", moveBody, StringComparison.Ordinal);
    }

    [Fact]
    public void MoveCompletionSubmitsOneCommandThroughTheActiveEditingSessionProcessor()
    {
        var controller = typeof(Canvas2DInteractionController);
        var sessionFields = controller.GetFields(
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Single(sessionFields, field => field.FieldType == typeof(EditingSession));
        Assert.DoesNotContain(sessionFields, field => field.FieldType == typeof(CommandProcessor));
        Assert.Single(
            typeof(EditingSession).GetFields(BindingFlags.NonPublic | BindingFlags.Instance),
            field => field.FieldType == typeof(CommandProcessor));

        var controllerSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var releaseBody = Between(
            controllerSource,
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerReleasedAsync(",
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerCancelledAsync(");
        var sessionInteractionSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSession.Interaction.cs");

        Assert.Equal(1, Count(releaseBody, "new MoveVisualStateCommand("));
        Assert.Equal(1, Count(releaseBody, "_session.CompleteMoveGestureAsync("));
        Assert.DoesNotContain("new CommandProcessor(", controllerSource, StringComparison.Ordinal);
        Assert.Contains("History.ExecuteAsync(", sessionInteractionSource, StringComparison.Ordinal);
        Assert.Contains("_commandProcessor,", sessionInteractionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistentMoveCommandHasNoSceneEditorBrowserOrRuntimeDependency()
    {
        var signatureTypes = GetPublicSignatureTypes(typeof(MoveVisualStateCommand))
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
    public void SceneBuilderOwnsMovePreviewAndRendererExposesNoEditingApi()
    {
        var sceneGestureSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");
        var controllerSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var rendererMethods = typeof(Canvas2DRenderer).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsSpecialName)
            .Select(static method => method.Name)
            .ToArray();

        Assert.Contains("ComposeMoveGesturePreview(", sceneGestureSource, StringComparison.Ordinal);
        Assert.Contains("move-preview:", sceneGestureSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new Canvas2DSceneItem(", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain(rendererMethods, method => ContainsAny(
            method,
            "Command",
            "Gesture",
            "MoveVisual",
            "Pointer",
            "VisualState"));
    }

    [Fact]
    public void BrowserPointerWiringContainsNoEditingMeaningAndPhaseLContainsNoBpmn()
    {
        var javaScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));
        var interactionSource = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction");
        var editingSessionInteraction = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSession.Interaction.cs");
        var sceneGestureSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");
        string[] forbiddenJavaScriptMeaning =
        [
            "CommandProcessor",
            "DocumentRevision",
            "EditorState",
            "MoveVisualStateCommand",
            "SceneObjectId",
            "SemanticElement",
            "VisualState",
            "hitTest",
            "move-preview",
        ];

        Assert.DoesNotContain(forbiddenJavaScriptMeaning, fragment =>
            javaScript.Contains(fragment, StringComparison.Ordinal));
        Assert.Contains("setPointerCapture", javaScript, StringComparison.Ordinal);
        Assert.Contains("releasePointerCapture", javaScript, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Bpmn",
            interactionSource + editingSessionInteraction + sceneGestureSource + javaScript,
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

        foreach (var method in type.GetMethods(
                     BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            foreach (var expanded in ExpandType(method.ReturnType))
            {
                yield return expanded;
            }

            foreach (var parameter in method.GetParameters())
            {
                foreach (var expanded in ExpandType(parameter.ParameterType))
                {
                    yield return expanded;
                }
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
