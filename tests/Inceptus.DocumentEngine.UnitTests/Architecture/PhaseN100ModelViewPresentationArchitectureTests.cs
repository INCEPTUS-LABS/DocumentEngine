namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN100ModelViewPresentationArchitectureTests
{
    [Fact]
    public void EmptyCanvasMenuUsesOneManagedHitAndNotationNeutralContributions()
    {
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var contracts = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Contracts",
            "ContextMenus");
        var script = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js");

        Assert.Equal(1, CountOccurrences(host, "controller.PointerContextMenuAsync("));
        Assert.Contains(
            "if (targetVisualStateId is null && targetSemanticElementId is null)",
            host,
            StringComparison.Ordinal);
        Assert.Contains("DocumentCanvasContextMenuKind.Background", host,
            StringComparison.Ordinal);
        Assert.Contains("DocumentCanvasContextMenuKind.Element", component,
            StringComparison.Ordinal);
        Assert.Contains("CanvasBackgroundActionCatalog", contracts, StringComparison.Ordinal);
        Assert.Contains("CanvasBackgroundActionRequest", contracts, StringComparison.Ordinal);
        Assert.Contains("IDocumentCreationIdentityProvider IdentityProvider", contracts,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AllocatedScopeId", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateDocumentScopeId()", host, StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnSemanticTypes", host + component + contracts,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnPluginRegistration", host + component + contracts,
            StringComparison.Ordinal);

        Assert.Contains("#captureContextMenu(event)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelProfile", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BackgroundAction", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Process", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModelViewFormSeparatesPersistentAvailabilityFromTransientVisibility()
    {
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var css = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor.css");

        Assert.Contains("View and model properties...", component, StringComparison.Ordinal);
        Assert.Contains("Available in model", component, StringComparison.Ordinal);
        Assert.Contains("Visible in view", component, StringComparison.Ordinal);
        Assert.Contains("!profile.IsAvailable", component, StringComparison.Ordinal);
        Assert.Contains("SetModelProfileAvailabilityCommand", host, StringComparison.Ordinal);
        Assert.Contains("session.UpdateModelProfileViewStateAsync(", host,
            StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(host, "new SetModelProfileAvailabilityCommand("));
        Assert.Equal(1, CountOccurrences(host, "session.UpdateModelProfileViewStateAsync("));
        Assert.Contains("_modelViewPropertiesDraft = null", component,
            StringComparison.Ordinal);

        Assert.Contains(".model-context-submenu", css, StringComparison.Ordinal);
        Assert.Contains(".model-view-properties-form", css, StringComparison.Ordinal);
        Assert.Contains(".model-profile-row", css, StringComparison.Ordinal);
        Assert.Contains("input[type=\"checkbox\"]", css, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelViewFormClosesOnlyAfterSuccessfulCompleteApply()
    {
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var hostApply = Slice(
            host,
            "internal async ValueTask<DocumentCanvasModelViewPropertiesApplyResult>",
            "internal DocumentCanvasHostState CaptureState()");
        var componentApply = Slice(
            component,
            "private async Task ApplyModelViewPropertiesAsync()",
            "private bool TryPublishModelViewPropertiesFormState(");
        var successIndex = componentApply.IndexOf(
            "if (result.Succeeded && result.Authoritative is not null)",
            StringComparison.Ordinal);
        var staleIndex = componentApply.IndexOf(
            "if (!ReferenceEquals(_modelViewPropertiesDraft, draft)",
            StringComparison.Ordinal);

        Assert.Equal(2, CountOccurrences(
            hostApply,
            "ClearModelViewPropertiesFormUnderLock();"));
        Assert.Contains("DocumentCanvasModelViewPropertiesApplyStatus.NoChange", hostApply,
            StringComparison.Ordinal);
        Assert.Contains("DocumentCanvasModelViewPropertiesApplyStatus.Committed", hostApply,
            StringComparison.Ordinal);
        Assert.True(successIndex >= 0);
        Assert.True(successIndex < staleIndex);
        Assert.Contains("_modelViewPropertiesDraft = null;", componentApply,
            StringComparison.Ordinal);
        Assert.Contains("draft.Feedback = result.Message", componentApply,
            StringComparison.Ordinal);
        Assert.Contains("TryPublishModelViewPropertiesFormState(host, draft)", componentApply,
            StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(
            component,
            "@onclick=\"CancelModelViewProperties\""));
        Assert.Contains("id=\"@DomId(\"model-view-properties-cancel\")\"", component,
            StringComparison.Ordinal);
        Assert.Contains("id=\"@DomId(\"model-view-properties-close\")\"", component,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MenuAndModalDomSurfacesStopUnderlyingCanvasInteractions()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");

        Assert.Contains("@onpointerdown:stopPropagation", component,
            StringComparison.Ordinal);
        Assert.Contains("@onpointerup:stopPropagation", component,
            StringComparison.Ordinal);
        Assert.Contains("@onpointermove:stopPropagation", component,
            StringComparison.Ordinal);
        Assert.Contains("@onwheel:stopPropagation", component, StringComparison.Ordinal);
        Assert.Contains("@oncontextmenu:preventDefault", component,
            StringComparison.Ordinal);
        Assert.Contains("@oncontextmenu:stopPropagation", component,
            StringComparison.Ordinal);
        Assert.Contains("placementController.Cancel()", ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs"), StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
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

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not locate start marker '{start}'.");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Could not locate end marker '{end}'.");
        return source[startIndex..endIndex];
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
