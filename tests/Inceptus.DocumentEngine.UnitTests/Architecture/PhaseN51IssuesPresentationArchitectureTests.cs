namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN51IssuesPresentationArchitectureTests
{
    [Fact]
    public void StatusRowUnifiesIssuesCanvasStatusAndAlwaysVisibleCounts()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var statusRow = Between(
            component,
            "<footer id=\"@DomId(\"editor-status\")\"",
            "</footer>");

        Assert.Contains("id=\"@DomId(\"issues-toggle\")\"", statusRow,
            StringComparison.Ordinal);
        Assert.Contains("class=\"canvas-presentation-status\"", statusRow,
            StringComparison.Ordinal);
        Assert.Contains("@StatusMessage", statusRow, StringComparison.Ordinal);
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
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var panelCondition = component.IndexOf(
            "@if (_issuesPanel.IsOpen)",
            StringComparison.Ordinal);
        var panel = component.IndexOf(
            "<aside id=\"@DomId(\"issues-panel\")\"",
            StringComparison.Ordinal);
        var status = component.IndexOf(
            "<footer id=\"@DomId(\"editor-status\")\"",
            StringComparison.Ordinal);

        Assert.True(panelCondition >= 0 && panel > panelCondition && status > panel);
        Assert.Contains("class=\"issues-floating-panel\"", component,
            StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"issues-panel\"", component,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FloatingPanelIsGenericScrollableAndLayeredBetweenCanvasAndProperties()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var styles = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor.css");
        var panelStyle = Between(styles, ".issues-floating-panel {", "}");
        var bodyStyle = Between(styles, ".issues-panel-body {", "}");

        Assert.Contains("position: absolute", panelStyle, StringComparison.Ordinal);
        Assert.Contains("z-index: 24", panelStyle, StringComparison.Ordinal);
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
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
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
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
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
