using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.ScopeNavigation;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN82ScopeNavigationArchitectureTests
{
    [Fact]
    public void ScopeNavigationContributionIsSmallTypedAndNotationNeutral()
    {
        Assert.Equal(
            typeof(SemanticTypeId),
            typeof(ScopeNavigationRegistration)
                .GetProperty(nameof(ScopeNavigationRegistration.SemanticTypeId))?.PropertyType);
        Assert.Equal(
            typeof(IScopeNavigationContribution),
            typeof(ScopeNavigationRegistration)
                .GetProperty(nameof(ScopeNavigationRegistration.Contribution))?.PropertyType);
        Assert.Single(typeof(IScopeNavigationContribution).GetMethods(), method =>
            method.Name == nameof(IScopeNavigationContribution.TryResolveTargetScope));
        Assert.Single(typeof(IScopeNavigationContribution).GetMethods(), method =>
            method.Name == nameof(IScopeNavigationContribution.ResolveBreadcrumbLabel));

        var source = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "ScopeNavigation",
            "IScopeNavigationContribution.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "ScopeNavigation",
                "ScopeNavigationRegistration.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "ScopeNavigation",
                "ScopeNavigationCatalog.cs");
        Assert.DoesNotContain("Bpmn", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SubProcess", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenericPresentationKeepsOpenBeforePropertiesAndJavaScriptScalarOnly()
    {
        var razor = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var javascript = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js");

        var openIndex = razor.IndexOf("DomId(\"open-scope-action\")", StringComparison.Ordinal);
        var propertiesIndex = razor.IndexOf("DomId(\"properties-action\")", StringComparison.Ordinal);
        Assert.True(openIndex >= 0 && openIndex < propertiesIndex);
        Assert.Contains("NavigateToScopeUnderGateAsync", host, StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnSemanticTypes", host, StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnPluginRegistration", host, StringComparison.Ordinal);
        Assert.Contains("addEventListener(\"dblclick\"", javascript, StringComparison.Ordinal);
        Assert.Contains("kind: 6", javascript, StringComparison.Ordinal);
        Assert.DoesNotContain("SubProcess", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SemanticElement", javascript, StringComparison.Ordinal);
    }

    [Fact]
    public void EditingSessionIsTheOnlyActiveScopeAuthority()
    {
        var session = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSession.cs");
        var sessionState = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSessionState.cs");
        var editorState = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "EditorState",
            "EditorStateSnapshot.cs");
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var razor = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");

        Assert.Contains("private DocumentScopeId _activeScopeId;", session,
            StringComparison.Ordinal);
        Assert.Contains("_activeScopeId = document.SemanticModel.RootScopeId;", session,
            StringComparison.Ordinal);
        Assert.Contains("public DocumentScopeId ActiveScopeId { get; }", sessionState,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ActiveScope", editorState, StringComparison.Ordinal);
        Assert.DoesNotContain("_activeScopeId", host, StringComparison.Ordinal);
        Assert.DoesNotContain("_activeScopeId", razor, StringComparison.Ordinal);
        Assert.Contains("_state?.Session?.ActiveScopeId", razor, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveScopeAndViewportCacheStayOutOfPersistentSnapshots()
    {
        var persistentSnapshots = string.Concat(
            ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "Documents",
                "DocumentSnapshot.cs"),
            ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "Semantics",
                "SemanticModelSnapshot.cs"),
            ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "Visuals",
                "VisualModelSnapshot.cs"));
        var session = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSession.cs");

        Assert.DoesNotContain("ActiveScope", persistentSnapshots,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ViewportSnapshot", persistentSnapshots,
            StringComparison.Ordinal);
        Assert.DoesNotContain("inactiveScopeViewports", persistentSnapshots,
            StringComparison.Ordinal);
        Assert.Contains(
            "Dictionary<DocumentScopeId, ScopeViewRuntimeState>",
            session,
            StringComparison.Ordinal);
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
