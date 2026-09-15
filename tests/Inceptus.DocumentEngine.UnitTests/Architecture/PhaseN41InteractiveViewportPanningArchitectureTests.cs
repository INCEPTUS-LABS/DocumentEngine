using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN41InteractiveViewportPanningArchitectureTests
{
    [Fact]
    public void PanIsOneGenericRuntimeOnlyEditingSessionOperationAndNotACommand()
    {
        var method = typeof(EditingSession).GetMethod(nameof(EditingSession.PanViewportAsync));
        Assert.NotNull(method);
        Assert.Equal(typeof(VectorD), method!.GetParameters()[0].ParameterType);
        Assert.DoesNotContain(method.GetParameters(), parameter =>
            typeof(ICommand).IsAssignableFrom(parameter.ParameterType));

        var viewport = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSession.Viewport.cs");
        Assert.Contains("current.Pan + canvasTranslation", viewport, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandProcessor", viewport, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachedDocument.Replace", viewport, StringComparison.Ordinal);
        Assert.DoesNotContain("History", viewport, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(DocumentSnapshot), viewport, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserBoundaryOwnsOnlyScalarCanvasInputAndNoTransformOrSpaceMode()
    {
        var javascript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));

        Assert.Contains("addEventListener(\"wheel\", this.#onWheel, { passive: false })",
            javascript, StringComparison.Ordinal);
        Assert.Contains("event.ctrlKey || event.metaKey", javascript, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault", javascript, StringComparison.Ordinal);
        Assert.Contains("OnCanvasWheelInput", javascript, StringComparison.Ordinal);
        Assert.DoesNotContain("keydown", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Space", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("matrix", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inverse", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("documentPoint", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("zoom", javascript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MiddlePanStructurallyPrecedesDiagramGestureDispatch()
    {
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var middle = host.IndexOf(
            "TryHandleViewportPanPointerUnderGateAsync(",
            StringComparison.Ordinal);
        var diagram = host.IndexOf(
            "controller.PointerPressedAsync(",
            StringComparison.Ordinal);

        Assert.True(middle >= 0 && diagram > middle);
        Assert.Contains("input.Button == MiddleMouseButton", host, StringComparison.Ordinal);
        Assert.Contains("state.EditorState.ActiveGesture is not null", host,
            StringComparison.Ordinal);
        Assert.Contains("\"grabbing\"", host, StringComparison.Ordinal);
    }

    [Fact]
    public void PanIsUnrestrictedNotationNeutralAndPreservesCanonicalTransformAuthority()
    {
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var viewport = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSession.Viewport.cs");
        var panSource = Between(
            host,
            "private async Task OnWheelInputAsync(",
            "private bool TryConsumePlacementPointerBoundary(") + viewport;
        string[] forbidden =
        [
            "Bpmn",
            "SequenceFlow",
            "MessageCatchEvent",
            "DocumentGeometryBoundary",
            "MinimumX",
            "MinimumY",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, panSource, StringComparison.OrdinalIgnoreCase));

        Assert.Contains("Canvas2DSceneBuilder.CalculateVisibleDocumentRegion", viewport,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Matrix2D", viewport, StringComparison.Ordinal);
    }

    [Fact]
    public void CanvasOnlyListenerCannotOwnToolboxPropertiesOrContextMenuInput()
    {
        var javascript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));
        var razor = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");

        Assert.Contains("this.#canvas.addEventListener(\"wheel\"", javascript,
            StringComparison.Ordinal);
        Assert.DoesNotContain("document.addEventListener(\"wheel\"", javascript,
            StringComparison.Ordinal);
        Assert.DoesNotContain("window.addEventListener(\"wheel\"", javascript,
            StringComparison.Ordinal);
        Assert.Contains("<ToolboxPanel", razor, StringComparison.Ordinal);
        Assert.Contains("<canvas", razor, StringComparison.Ordinal);
        Assert.Contains("class=\"object-context-menu\"", razor, StringComparison.Ordinal);
        Assert.Contains("class=\"properties-form ", razor, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewportRemainsOutsidePersistentDocumentContracts()
    {
        var contracts = ReadProductionProject("Inceptus.DocumentEngine.Contracts");
        var persistent = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot, "src", "Inceptus.DocumentEngine.Contracts"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(path =>
                    path.Contains($"{Path.DirectorySeparatorChar}Documents{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal) ||
                    path.Contains($"{Path.DirectorySeparatorChar}Visuals{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal) ||
                    path.Contains($"{Path.DirectorySeparatorChar}Semantics{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

        Assert.Contains(nameof(ViewportSnapshot), contracts, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ViewportSnapshot), persistent, StringComparison.Ordinal);
        Assert.DoesNotContain("PanViewport", persistent, StringComparison.Ordinal);
    }

    private static string ReadProductionProject(string project) =>
        string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot, "src", project),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

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
