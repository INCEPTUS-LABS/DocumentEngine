using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN7ToolboxAcceptanceArchitectureTests
{
    [Fact]
    public void AccordionAndModeAuthorityRemainLocalAndStructurallySingleValued()
    {
        Assert.Equal(
            "Inceptus.DocumentEngine.Bpmn.Blazor.Presentation",
            typeof(ToolboxPresentationState).Namespace);
        Assert.Equal(typeof(ToolboxViewMode), typeof(ToolboxPresentationState)
            .GetProperty(
                nameof(ToolboxPresentationState.ViewMode),
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .PropertyType);
        Assert.Equal(
            typeof(ToolboxGroupId),
            typeof(ToolboxPresentationState)
                .GetProperty(
                    nameof(ToolboxPresentationState.ExpandedToolboxGroupId),
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .PropertyType);
        Assert.DoesNotContain(
            typeof(ToolboxPresentationState).GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.DeclaredOnly),
            static member => member.Name.EndsWith("Expanded", StringComparison.Ordinal) &&
                member.Name != nameof(ToolboxPresentationState.ExpandedToolboxGroupId));

        Type[] persistentStateTypes =
        [
            typeof(DocumentSnapshot),
            typeof(EditorStateSnapshot),
            typeof(VisualStateSnapshot),
            typeof(HistoryStatus),
        ];
        Assert.DoesNotContain(
            persistentStateTypes.SelectMany(PublicMembers),
            static member => MemberType(member) == typeof(ToolboxViewMode) ||
                MemberType(member) == typeof(ToolboxPresentationState));
    }

    [Fact]
    public void SectionExtensionIsOneLevelGenericDataWithOptionalGroupIcons()
    {
        Assert.Equal(
            typeof(ToolboxSectionId),
            typeof(ToolboxSectionDefinition)
                .GetProperty(nameof(ToolboxSectionDefinition.SectionId))!
                .PropertyType);
        Assert.Equal(
            typeof(ToolboxSectionId),
            typeof(ToolboxGroupDefinition)
                .GetProperty(nameof(ToolboxGroupDefinition.SectionId))!
                .PropertyType);
        Assert.Equal(
            typeof(ToolboxIconDescriptor),
            typeof(ToolboxGroupDefinition)
                .GetProperty(nameof(ToolboxGroupDefinition.Icon))!
                .PropertyType);
        Assert.DoesNotContain(
            typeof(ToolboxSectionDefinition).GetProperties(),
            static property => property.PropertyType.IsGenericType ||
                property.PropertyType.IsArray);

        var contracts = ReadProductionFiles(
            "Inceptus.DocumentEngine.Contracts",
            "Toolbox",
            "*.cs");
        Assert.DoesNotContain("Bpmn", contracts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RenderFragment", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericPanelAndLayoutContainNoNotationOrPersistenceDecisionPath()
    {
        var panel = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "ToolboxPanel.razor");
        var state = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "ToolboxPresentationState.cs");
        var panelCss = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "ToolboxPanel.razor.css");
        var canvasCss = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor.css");

        Assert.DoesNotContain("BpmnSemanticTypes", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnPluginRegistration", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBpmn", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("SemanticTypeId", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Command", state, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("History", state, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DocumentSnapshot", state, StringComparison.Ordinal);
        Assert.DoesNotContain("EditingSession", state, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: auto minmax(0, 1fr)", canvasCss,
            StringComparison.Ordinal);
        Assert.Contains("--inceptus-toolbox-expanded-width", canvasCss,
            StringComparison.Ordinal);
        Assert.Contains("--inceptus-toolbox-compact-width: 4.5rem", canvasCss,
            StringComparison.Ordinal);
        Assert.Contains("min-height: 0", panelCss, StringComparison.Ordinal);
        Assert.Contains("contain: size", panelCss, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto", panelCss, StringComparison.Ordinal);
        Assert.Contains("overscroll-behavior: contain", panelCss, StringComparison.Ordinal);
        Assert.DoesNotContain("position: fixed", panelCss, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("position: sticky", panelCss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExistingBrowserBoundaryUsesLiveBoundsResizeObservationAndDpr()
    {
        var presentationJavaScript = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js");
        var canvasJavaScript = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "wwwroot",
            "inceptus.canvas2d.js");

        Assert.True(Count(presentationJavaScript, "this.#canvas.getBoundingClientRect()") >= 2);
        Assert.Contains("new ResizeObserver", presentationJavaScript, StringComparison.Ordinal);
        Assert.Contains("window.devicePixelRatio", presentationJavaScript,
            StringComparison.Ordinal);
        Assert.Contains("surface.cssWidth * surface.devicePixelRatio", canvasJavaScript,
            StringComparison.Ordinal);
        Assert.Contains("surface.cssHeight * surface.devicePixelRatio", canvasJavaScript,
            StringComparison.Ordinal);
    }

    private static IEnumerable<MemberInfo> PublicMembers(Type type) =>
        type.GetMembers(
            BindingFlags.Public | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.DeclaredOnly);

    private static Type? MemberType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        MethodInfo method => method.ReturnType,
        EventInfo @event => @event.EventHandlerType,
        _ => null,
    };

    private static string ReadProductionFiles(
        string project,
        string directory,
        string pattern) =>
        string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot, "src", project, directory),
                    pattern,
                    SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, fileName));

    private static int Count(string source, string value)
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
