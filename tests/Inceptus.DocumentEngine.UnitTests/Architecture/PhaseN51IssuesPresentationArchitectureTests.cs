namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN51IssuesPresentationArchitectureTests
{
    [Fact]
    public void HostToolbarContainsConditionalCountsAsTheIssuesAction()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            "Demo",
            "BpmnDemoHost.razor") + ReadProductionFile("Inceptus.DocumentEngine.Blazor", "Demo", "BpmnDemoHost.razor.cs");
        var statusRow = Between(
            component,
            "<div class=\"editor-toolbar host-toolbar\"",
            "@if (Editor is null)");

        Assert.Contains("id=\"@DomId(\"issues-toggle\")\"", statusRow,
            StringComparison.Ordinal);
        Assert.Contains("@if (CurrentValidation is null)", statusRow, StringComparison.Ordinal);
        Assert.Contains("class=\"validation-currency status-issue-counts\"", statusRow, StringComparison.Ordinal);
        Assert.Contains("class=\"issues-toggle issues-counts status-issue-counts\"", statusRow, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>@Text[\"Status_Issues\"]</span>", statusRow, StringComparison.Ordinal);
        var editor = ReadProductionFile("Inceptus.DocumentEngine.Bpmn.Blazor", "Components", "DocumentCanvas.razor");
        Assert.Contains("@StatusMessage", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("issues-toggle", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("<footer", component, StringComparison.Ordinal);
        Assert.Contains("@Text[\"Status_Errors\"] @ValidationErrorCount", statusRow,
            StringComparison.Ordinal);
        Assert.Contains("@Text[\"Status_Warnings\"] @ValidationWarningCount", statusRow,
            StringComparison.Ordinal);
        Assert.Contains("@Text[\"Status_Info\"] @ValidationInfoCount", statusRow,
            StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"presentation-status\"", component,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedPanelLeavesNoIssueListInNormalWorkspaceFlow()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            "Demo",
            "BpmnDemoHost.razor") + ReadProductionFile("Inceptus.DocumentEngine.Blazor", "Demo", "BpmnDemoHost.razor.cs");
        var panelCondition = component.IndexOf(
            "@if (_issuesPanel.IsOpen)",
            StringComparison.Ordinal);
        var panel = component.IndexOf(
            "<aside id=\"@DomId(\"issues-panel\")\"",
            StringComparison.Ordinal);
        var toggle = component.IndexOf(
            "id=\"@DomId(\"issues-toggle\")\"",
            StringComparison.Ordinal);

        Assert.True(toggle >= 0 && panelCondition > toggle && panel > panelCondition);
        Assert.Contains("class=\"issues-floating-panel\"", component,
            StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"issues-panel\"", component,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FeedbackUsesAnOverlayAndOneFixedHeightEditorRow()
    {
        var hostStyles = ReadProductionFile("Inceptus.DocumentEngine.Blazor", "Demo", "BpmnDemoHost.razor.css");
        Assert.Contains("grid-template-rows: auto minmax(0, 1fr);", hostStyles, StringComparison.Ordinal);
        Assert.Contains("position: absolute", Between(hostStyles, ".operation-feedback-panel {", "}"), StringComparison.Ordinal);
        Assert.Contains("overflow: auto", Between(hostStyles, ".operation-feedback-body {", "}"), StringComparison.Ordinal);
        var editor = ReadProductionFile("Inceptus.DocumentEngine.Bpmn.Blazor", "Components", "DocumentCanvas.razor");
        var footer = Between(editor, "<footer id=\"@DomId(\"editor-status\")\"", "</footer>");
        Assert.Contains("@StatusMessage", footer, StringComparison.Ordinal);
        Assert.Contains("@ErrorMessage", footer, StringComparison.Ordinal);
        Assert.Contains("new-diagram-error", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("canvas-diagnostics", editor, StringComparison.Ordinal);
        var editorStyles = ReadProductionFile("Inceptus.DocumentEngine.Bpmn.Blazor", "Components", "DocumentCanvas.razor.css");
        Assert.Contains("flex: 0 0 2rem", Between(editorStyles, ".editor-status-row {", "}"), StringComparison.Ordinal);
    }

    [Fact]
    public void FloatingHostPanelIsScrollableAboveEditorToolbarAndBelowProperties()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            "Demo",
            "BpmnDemoHost.razor") + ReadProductionFile("Inceptus.DocumentEngine.Blazor", "Demo", "BpmnDemoHost.razor.cs");
        var styles = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            "Demo",
            "BpmnDemoHost.razor.css");
        var panelStyle = Between(styles, ".issues-floating-panel {", "}");
        var bodyStyle = Between(styles, ".issues-panel-body {", "}");

        Assert.Contains("position: absolute", panelStyle, StringComparison.Ordinal);
        Assert.Contains("z-index: 31", panelStyle, StringComparison.Ordinal);
        var editorStyles = ReadProductionFile("Inceptus.DocumentEngine.Bpmn.Blazor", "Components", "DocumentCanvas.razor.css");
        Assert.Contains("z-index: 31", Between(editorStyles, ".editor-toolbar {", "}"), StringComparison.Ordinal);
        Assert.Contains("z-index: 32", Between(editorStyles, ".properties-form {", "}"), StringComparison.Ordinal);
        Assert.True(component.IndexOf("<InceptusBpmnModeler", StringComparison.Ordinal) <
            component.IndexOf("<aside id=\"@DomId(\"issues-panel\")\"", StringComparison.Ordinal));
        Assert.Contains("overflow-y: auto", bodyStyle, StringComparison.Ordinal);
        Assert.Contains("overflow-x: hidden", bodyStyle, StringComparison.Ordinal);
        Assert.Contains("overscroll-behavior: contain", bodyStyle,
            StringComparison.Ordinal);
        Assert.Contains("@onwheel:stopPropagation", component, StringComparison.Ordinal);
        Assert.Contains("@oncontextmenu:preventDefault", component,
            StringComparison.Ordinal);
        Assert.Contains("@oncontextmenu:stopPropagation", component,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Bpmn", PanelSection(component),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HeaderOwnsPointerCaptureAndBodyCannotStartMovement()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            "Demo",
            "BpmnDemoHost.razor") + ReadProductionFile("Inceptus.DocumentEngine.Blazor", "Demo", "BpmnDemoHost.razor.cs");
        var header = Between(
            component,
            "<header id=\"@DomId(\"issues-panel-header\")\"",
            "</header>");
        var body = Between(component, "<div class=\"issues-panel-body\">", "</div>");
        var javascript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));

        Assert.Contains("@onpointerdown=\"StartIssuesPanelMove\"", header,
            StringComparison.Ordinal);
        Assert.Contains("@onpointermove=\"MoveIssuesPanel\"", header,
            StringComparison.Ordinal);
        Assert.Contains("@onpointerup=\"EndIssuesPanelMove\"", header,
            StringComparison.Ordinal);
        Assert.DoesNotContain("DomId(\"issues-close\")", header,
            StringComparison.Ordinal);
        Assert.Contains("id=\"@DomId(\"issues-close\")\"", PanelSection(component),
            StringComparison.Ordinal);
        Assert.DoesNotContain("StartIssuesPanelMove", body, StringComparison.Ordinal);
        Assert.Contains("export function createDomPointerCaptureObserver", javascript,
            StringComparison.Ordinal);
        Assert.Contains("setPointerCapture(event.pointerId)", javascript,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PanelStateHasNoValidationDocumentCommandOrViewportAuthority()
    {
        var state = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            "Demo",
            "FloatingIssuesPanelState.cs");
        string[] forbidden =
        [
            "BpmnSemanticTypes",
            "BpmnPluginRegistration",
            "CreateBpmn",
            "ValidationSnapshot",
            "DocumentRevision",
            "CommandProcessor",
            "ICommand",
            "ViewportSnapshot",
            "PanViewport",
            "Zoom",
        ];

        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, state, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("private MoveGesture?", state, StringComparison.Ordinal);
        Assert.Contains("internal void Close()", state, StringComparison.Ordinal);
        Assert.DoesNotContain("Left = 0d", state, StringComparison.Ordinal);
    }

    private static string PanelSection(string component) => Between(
        component,
        "<aside id=\"@DomId(\"issues-panel\")\"",
        "</aside>");

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, fileName));

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source[startIndex..endIndex];
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName,
                        "Inceptus.DocumentEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
