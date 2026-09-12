using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseL3MultiSelectionArchitectureTests
{
    [Fact]
    public void SelectionHasOneImmutableVisualStateIdentitySourceAndRemainsTransient()
    {
        var selection = Assert.Single(typeof(EditorStateSnapshot).GetProperties(),
            static property => property.Name == nameof(EditorStateSnapshot.Selection));
        Assert.Equal(typeof(ImmutableArray<VisualStateId>), selection.PropertyType);

        var documentContracts = ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Documents") +
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Semantics") +
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Visuals") +
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Metadata");
        Assert.DoesNotContain("EditorStateSnapshot", documentContracts, StringComparison.Ordinal);
        Assert.DoesNotContain("Selection", documentContracts, StringComparison.Ordinal);

        var editorStateSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "EditorState",
            "EditorStateSnapshot.cs");
        Assert.Contains("ImmutableArray<VisualStateId>", editorStateSource, StringComparison.Ordinal);
        Assert.Contains("duplicate", editorStateSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "ImmutableArray<SceneObjectId> Selection",
            editorStateSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CtrlAndCursorMeaningStayInDotNetWhileJavaScriptTransportsScalarsOnly()
    {
        var pointerInput = typeof(Canvas2DPointerInput);
        Assert.Equal(typeof(bool), pointerInput.GetProperty("ControlKey")?.PropertyType);

        var hostSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var interactionSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var javaScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));

        Assert.Contains("input.ControlKey", hostSource, StringComparison.Ordinal);
        Assert.Contains("ControlKey", interactionSource, StringComparison.Ordinal);
        Assert.Contains("ctrlKey", javaScript, StringComparison.Ordinal);
        Assert.Contains("style.cursor", javaScript, StringComparison.Ordinal);
        string[] forbiddenJavaScriptMeaning =
        [
            "EditorState",
            "Selection",
            "SelectedTargets",
            "VisualStateId",
            "SceneObjectId",
            "MoveVisualStatesCommand",
            "controlKey ?",
            "ctrlKey ?",
            "grabbing",
            "grab",
            "hitTest",
        ];
        Assert.DoesNotContain(forbiddenJavaScriptMeaning, fragment =>
            javaScript.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BlazorAcceptanceSurfaceExposesCanonicalCollectionAndSingularCompatibility()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");

        Assert.Contains("data-selection-count", component, StringComparison.Ordinal);
        Assert.Contains("data-selected-visual-state-ids", component, StringComparison.Ordinal);
        Assert.Contains("data-selected-visual-state-id", component, StringComparison.Ordinal);
        Assert.Contains("data-selected-scene-object-id", component, StringComparison.Ordinal);
        Assert.Contains("OrderBy(static value => value, StringComparer.Ordinal)",
            component,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GroupPreviewIsCommandFreeAndCompletionUsesOneTopLevelSessionOperation()
    {
        var source = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var moveBody = Between(
            source,
            "private async ValueTask<Canvas2DInteractionResult> ExecuteNormalizedPointerMoveAsync(",
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerReleasedAsync(");
        var releaseBody = Between(
            source,
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerReleasedAsync(",
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerCancelledAsync(");

        Assert.DoesNotContain("MoveVisualStatesCommand", moveBody, StringComparison.Ordinal);
        Assert.DoesNotContain("History.ExecuteAsync", moveBody, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandProcessor", moveBody, StringComparison.Ordinal);
        Assert.Contains("ApplyEditorStateAsync(", moveBody, StringComparison.Ordinal);
        Assert.Equal(1, Count(releaseBody, "new MoveVisualStatesCommand("));
        Assert.Equal(1, Count(releaseBody, "CompletePersistentGestureAsync("));
        Assert.DoesNotContain("new CommandProcessor(", source, StringComparison.Ordinal);

        var controllerFields = typeof(Canvas2DInteractionController).GetFields(
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Single(controllerFields, field => field.FieldType == typeof(EditingSession));
        Assert.DoesNotContain(controllerFields, field => field.FieldType == typeof(CommandProcessor));
    }

    [Fact]
    public void CtrlClickAndHoverCursorAreTransientInteractionOnly()
    {
        var source = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var clickBody = Between(
            source,
            "private async ValueTask<Canvas2DInteractionResult> CompletePendingPointerClickUnderGateAsync(",
            "private static bool ShouldActivatePendingMove(");
        var hoverBody = Between(
            source,
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointInteractionUnderGateAsync(",
            "private async ValueTask<Canvas2DInteractionResult> ExecutePointerLeaveAsync(");

        Assert.Contains("WithToggledSelection(", clickBody, StringComparison.Ordinal);
        Assert.Contains("WithPlainSelection(", clickBody, StringComparison.Ordinal);
        Assert.Contains("CssCursor(", clickBody, StringComparison.Ordinal);
        foreach (var body in new[] { clickBody, hoverBody })
        {
            Assert.DoesNotContain("new Move", body, StringComparison.Ordinal);
            Assert.DoesNotContain("ExecuteAsync(", body, StringComparison.Ordinal);
            Assert.DoesNotContain("History", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Projection", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Layout", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Routing", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AtomicMultiMoveCommandUsesPersistentVisualIdentityAndNoCanvasState()
    {
        var command = typeof(MoveVisualStatesCommand);
        Assert.True(typeof(ICommand).IsAssignableFrom(command));
        Assert.Equal(
            typeof(ImmutableArray<VisualStateMove>),
            command.GetProperty(nameof(MoveVisualStatesCommand.Moves))?.PropertyType);
        Assert.Equal(
            typeof(VisualStateId),
            typeof(VisualStateMove).GetProperty(nameof(VisualStateMove.VisualStateId))?.PropertyType);

        var signatureTypes = GetPublicSignatureTypes(command)
            .Concat(GetPublicSignatureTypes(typeof(VisualStateMove)))
            .Distinct()
            .ToArray();
        Assert.DoesNotContain(typeof(SceneObjectId), signatureTypes);
        Assert.DoesNotContain(signatureTypes, type =>
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Canvas2D", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Contracts.EditorState", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void SceneOwnsSelectionFeedbackAndRendererHasNoMultiSelectionMeaning()
    {
        var sceneSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Composition.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "Scene",
                "Canvas2DSceneBuilder.Gestures.cs");
        var rendererSource = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Canvas2D",
            "Rendering");
        var rendererMethods = typeof(Canvas2DRenderer).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsSpecialName)
            .Select(static method => method.Name)
            .ToArray();

        Assert.Contains("editorState.Selection", sceneSource, StringComparison.Ordinal);
        Assert.Contains("selection", sceneSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(rendererMethods, name =>
            name.Contains("Selection", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("MultiMove", StringComparison.OrdinalIgnoreCase));
        string[] forbiddenRendererMeaning =
        [
            "MoveVisualStatesCommand",
            "EditorStateSnapshot",
            "multi-selection",
            "grabbing",
            "CssCursor",
        ];
        Assert.DoesNotContain(forbiddenRendererMeaning, fragment =>
            rendererSource.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PhaseL3AddsNoBpmnOrFrozenDocumentationChange()
    {
        var sources = ReadProductionDirectory("Inceptus.DocumentEngine.Canvas2D", "Interaction") +
            ReadProductionDirectory("Inceptus.DocumentEngine.Canvas2D", "EditingSession") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "Commands",
                "MoveVisualStatesCommand.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Bpmn.Blazor",
                "Presentation",
                "DocumentCanvasHost.cs");

        Assert.DoesNotContain("BpmnSemanticTypes", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnPluginRegistration", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBpmn", sources, StringComparison.Ordinal);
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
