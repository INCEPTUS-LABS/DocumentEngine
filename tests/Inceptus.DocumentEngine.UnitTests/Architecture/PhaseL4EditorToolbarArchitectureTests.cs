using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseL4EditorToolbarArchitectureTests
{
    [Fact]
    public void EditingSessionExposesOneTargetedTransientViewportOperationAndNoZoomCommand()
    {
        var method = Assert.Single(
            typeof(EditingSession).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            static candidate => candidate.Name == nameof(EditingSession.UpdateViewportAsync));

        Assert.Equal(
            typeof(ValueTask<EditingSessionOperationResult>),
            method.ReturnType);
        Assert.Equal(
            [typeof(ViewportSnapshot), typeof(CancellationToken)],
            method.GetParameters().Select(static parameter => parameter.ParameterType));

        var commandTypes = typeof(ICommand).Assembly.GetExportedTypes()
            .Where(type => typeof(ICommand).IsAssignableFrom(type))
            .ToArray();
        Assert.DoesNotContain(commandTypes, static type =>
            type.Name.Contains("Zoom", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Viewport", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ToolbarReadsCanonicalHistoryAndViewportAndAllControlsRespectActiveGesture()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var undo = component.IndexOf("DomId(\"undo\")", StringComparison.Ordinal);
        var redo = component.IndexOf("DomId(\"redo\")", StringComparison.Ordinal);
        var zoomOut = component.IndexOf("DomId(\"zoom-out\")", StringComparison.Ordinal);
        var actualSize = component.IndexOf("DomId(\"zoom-100\")", StringComparison.Ordinal);
        var zoomIn = component.IndexOf("DomId(\"zoom-in\")", StringComparison.Ordinal);

        Assert.True(undo >= 0 && undo < redo && redo < zoomOut && zoomOut < actualSize &&
            actualSize < zoomIn);
        Assert.Contains("role=\"toolbar\"", component, StringComparison.Ordinal);
        Assert.Contains("session.HistoryStatus.CanUndo", component, StringComparison.Ordinal);
        Assert.Contains("session.HistoryStatus.CanRedo", component, StringComparison.Ordinal);
        Assert.Contains("session.EditorState.Viewport.Zoom", component, StringComparison.Ordinal);
        Assert.Contains("EditingSessionStatus.RuntimeFaulted", component, StringComparison.Ordinal);
        Assert.Contains("data-viewport-zoom", component, StringComparison.Ordinal);
        Assert.Contains("data-zoom-percentage", component, StringComparison.Ordinal);

        var canUndo = Between(component, "private bool CanUndo =>", "private bool CanRedo =>");
        var canRedo = Between(component, "private bool CanRedo =>", "private string CanUndoValue");
        var canUseZoom = Between(component, "private bool CanUseZoom =>", "private bool CanZoomOut");
        Assert.Contains("ActiveGesture is null", canUndo, StringComparison.Ordinal);
        Assert.Contains("ActiveGesture is null", canRedo, StringComparison.Ordinal);
        Assert.Contains("EditorState.ActiveGesture: null", canUseZoom, StringComparison.Ordinal);

        string[] forbiddenComponentOwnership =
        [
            "HistoryManager",
            "HistoryStore",
            "VisualModel",
            "SemanticModel",
            "CommandProcessor",
            "new MoveVisual",
            "new ResizeVisual",
            "new UpdateConnectionRoute",
            "Canvas2DRenderer",
        ];
        Assert.DoesNotContain(forbiddenComponentOwnership, fragment =>
            component.Contains(fragment, StringComparison.Ordinal));
    }

    [Fact]
    public void HostDelegatesHistoryAndViewportWithoutOwningPersistentStateOrPipelineStages()
    {
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var historyBody = Between(
            host,
            "private async ValueTask ExecuteHistoryOperationAsync(",
            "private async ValueTask ExecuteZoomOperationAsync(");
        var zoomBody = Between(
            host,
            "private async ValueTask ExecuteZoomOperationAsync(",
            "private async ValueTask<bool> TryEnterInteractionGateAsync(");

        Assert.Contains("session.UndoAsync(", historyBody, StringComparison.Ordinal);
        Assert.Contains("session.RedoAsync(", historyBody, StringComparison.Ordinal);
        Assert.Contains("state.HistoryStatus.CanUndo", historyBody, StringComparison.Ordinal);
        Assert.Contains("state.HistoryStatus.CanRedo", historyBody, StringComparison.Ordinal);
        Assert.Contains("state.EditorState.ActiveGesture is not null", historyBody,
            StringComparison.Ordinal);
        Assert.Contains("session.UpdateViewportAsync(", zoomBody, StringComparison.Ordinal);
        Assert.Contains("Canvas2DRenderer.ConvertCssToDocument(", zoomBody,
            StringComparison.Ordinal);
        Assert.Contains("surfaceSize.Value.CssWidth / 2d", zoomBody, StringComparison.Ordinal);
        Assert.Contains("surfaceSize.Value.CssHeight / 2d", zoomBody, StringComparison.Ordinal);
        Assert.DoesNotContain("DevicePixelRatio", zoomBody, StringComparison.Ordinal);
        Assert.DoesNotContain("session.UpdateEditorStateAsync(", host, StringComparison.Ordinal);

        string[] forbiddenHostMeaning =
        [
            "HistoryManager",
            "HistoryStore",
            "MoveVisualStateCommand",
            "MoveVisualStatesCommand",
            "ResizeVisualStateCommand",
            "UpdateConnectionRouteCommand",
            "ProjectionEngine",
            "LayoutEngine",
            "RoutingEngine",
            "VisualModel",
            "SemanticModel",
        ];
        Assert.DoesNotContain(forbiddenHostMeaning, fragment =>
            (historyBody + zoomBody).Contains(fragment, StringComparison.Ordinal));

        var fields = typeof(DocumentCanvasHost).GetFields(
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(fields, static field =>
            field.FieldType.Name.Contains("History", StringComparison.Ordinal) ||
            field.FieldType.Name is "Document" or "VisualModel" or "SemanticModel" ||
            field.Name.Contains("zoom", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ViewportUpdateCopiesOnlyEditorStateAndRunsTheCompatibleScenePath()
    {
        var source = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSession.Viewport.cs");

        Assert.Contains("CopyEditorStateWithViewport", source, StringComparison.Ordinal);
        Assert.Contains("EditorState.TryUpdate", source, StringComparison.Ordinal);
        Assert.Contains("_artifacts", source, StringComparison.Ordinal);
        Assert.Contains("artifacts.IsCompatibleWith", source, StringComparison.Ordinal);
        Assert.Contains("BeginRun(", source, StringComparison.Ordinal);
        Assert.Contains("source.Selection", source, StringComparison.Ordinal);
        Assert.Contains("source.HoveredObjectId", source, StringComparison.Ordinal);
        Assert.Contains("source.ActiveToolId", source, StringComparison.Ordinal);
        Assert.Contains("source.FocusTargetId", source, StringComparison.Ordinal);
        Assert.Contains("source.TemporaryFeedback", source, StringComparison.Ordinal);
        Assert.Contains("source.ToolState", source, StringComparison.Ordinal);

        string[] forbiddenViewportPath =
        [
            "History.",
            "HistoryManager",
            "HistoryStore",
            "CommandProcessor",
            "ICommand",
            "new MoveVisual",
            "new ResizeVisual",
            "new UpdateConnectionRoute",
            "ProjectionEngine",
            "LayoutEngine",
            "RoutingEngine",
            "RunFullAsync",
            "VisualModel.",
            "SemanticModel.",
            "RenderCurrentAsync",
            "Canvas2DRenderer",
        ];
        Assert.DoesNotContain(forbiddenViewportPath, fragment =>
            source.Contains(fragment, StringComparison.Ordinal));
    }

    [Fact]
    public void ZoomPolicyIsBoundedDeterministicAndContainsNoRuntimeOwnership()
    {
        Assert.Equal(25, DocumentCanvasZoomPolicy.MinimumPercentage);
        Assert.Equal(400, DocumentCanvasZoomPolicy.MaximumPercentage);
        Assert.Equal(10, DocumentCanvasZoomPolicy.StepPercentage);
        Assert.Equal(100, DocumentCanvasZoomPolicy.ActualSizePercentage);
        Assert.Equal(1d, DocumentCanvasZoomPolicy.ActualSize);
        Assert.Equal(0.25d, DocumentCanvasZoomPolicy.ZoomOut(0.25d));
        Assert.Equal(4d, DocumentCanvasZoomPolicy.ZoomIn(4d));
        Assert.Equal(1.18d, DocumentCanvasZoomPolicy.ZoomIn(1.08d));
        Assert.Equal(0.98d, DocumentCanvasZoomPolicy.ZoomOut(1.08d));

        var policy = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasZoomPolicy.cs");
        string[] forbiddenPolicyOwnership =
        [
            "DocumentSnapshot",
            "VisualModel",
            "SemanticModel",
            "HistoryManager",
            "HistoryStore",
            "ICommand",
            "CommandProcessor",
            "Projection",
            "Layout",
            "Routing",
            "Renderer",
            "IJSRuntime",
        ];
        Assert.DoesNotContain(forbiddenPolicyOwnership, fragment =>
            policy.Contains(fragment, StringComparison.Ordinal));
    }

    [Fact]
    public void RendererAndJavaScriptContainNoToolbarHistoryOrZoomControlMeaning()
    {
        var renderer = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Canvas2D",
            "Rendering");
        var javaScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));
        string[] forbiddenControlMeaning =
        [
            "DocumentCanvasZoomPolicy",
            "ZoomIn",
            "ZoomOut",
            "ActualSize",
            "inceptus-zoom",
            "inceptus-undo",
            "inceptus-redo",
            "HistoryStatus",
            "HistoryManager",
            "UpdateViewportAsync",
        ];

        Assert.DoesNotContain(forbiddenControlMeaning, fragment =>
            renderer.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(forbiddenControlMeaning, fragment =>
            javaScript.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PhaseL4AddsNoBpmnMeaningAndLeavesFrozenDocumentationUnchanged()
    {
        var sources = ReadProductionFile(
                "Inceptus.DocumentEngine.Bpmn.Blazor",
                "Presentation",
                "DocumentCanvasHost.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Bpmn.Blazor",
                "Presentation",
                "DocumentCanvasZoomPolicy.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Bpmn.Blazor",
                "Components",
                "DocumentCanvas.razor") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "EditingSession",
                "EditingSession.Viewport.cs");

        Assert.DoesNotContain("BpmnSemanticTypes", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnPluginRegistration", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBpmn", sources, StringComparison.Ordinal);
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
