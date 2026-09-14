namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN105NewDiagramArchitectureTests
{
    [Fact]
    public void NewDiagramUsesTheExistingEmptyDocumentAuthorityWithoutACommandOrDemoState()
    {
        var lifecycle = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.NewDiagram.cs");
        var factory = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Documents",
            "DocumentFactory.cs");

        Assert.Contains("DocumentFactory.CreateEmpty(", lifecycle, StringComparison.Ordinal);
        Assert.Contains("CreateFreshDocumentId", lifecycle, StringComparison.Ordinal);
        Assert.Contains("DocumentRevision.Zero", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("Command", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("History", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("Delete", lifecycle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Demo", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnSemanticTypes", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnPluginRegistration", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("Organizational", lifecycle, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void ImportAndNewDiagramShareOneStandbyCanvasReplacementAuthority(string newLine)
    {
        var import = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.NativeDocuments.cs");
        var newDiagram = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.NewDiagram.cs");
        var replacement = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.SessionReplacement.cs").ReplaceLineEndings(newLine);

        Assert.Contains("ReplaceDocumentUnderGateAsync(", import, StringComparison.Ordinal);
        Assert.Contains("ReplaceDocumentUnderGateAsync(", newDiagram, StringComparison.Ordinal);
        Assert.DoesNotContain("_session =", import, StringComparison.Ordinal);
        Assert.DoesNotContain("_session =", newDiagram, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(replacement, "_session = candidateSession;"));
        Assert.Contains("standbyCanvasElementId", replacement, StringComparison.Ordinal);
        Assert.Contains("attachment.Status != EditingSessionAttachStatus.Ready", replacement,
            StringComparison.Ordinal);
        Assert.Contains("HasError(candidateState.PresentationDiagnostics)", replacement,
            StringComparison.Ordinal);
        AssertSessionReplacementOrdering(replacement);
    }

    [Theory]
    [InlineData("_session = candidateSession;")]
    [InlineData("oldSession.StateChanged -=")]
    [InlineData("DisposeDocumentSessionResourcesAsync(\n                oldPointerObserver")]
    public void ReplacementOrderingGuardRejectsMissingStages(string missingStage)
    {
        var replacement = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.SessionReplacement.cs").ReplaceLineEndings("\n");

        Assert.Contains(missingStage, replacement, StringComparison.Ordinal);
        var incomplete = replacement.Replace(missingStage, string.Empty, StringComparison.Ordinal);

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertSessionReplacementOrdering(incomplete));
    }

    [Fact]
    public void ReplacementOrderingGuardRejectsRetirementBeforePromotion()
    {
        const string reversed = "oldSession.StateChanged -= HandleSessionStateChanged;\n" +
            "_session = candidateSession;\n" +
            "DisposeDocumentSessionResourcesAsync(\n                oldPointerObserver";

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertSessionReplacementOrdering(reversed));
    }

    [Fact]
    public void NewDiagramControlAndConfirmationHaveNativeAccessibleButtonSemantics()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");

        Assert.Contains("id=\"@DomId(\"new-diagram\")\"", component,
            StringComparison.Ordinal);
        Assert.Contains("type=\"button\"", component, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@Text[\"Toolbar_NewDiagram\"]\"", component, StringComparison.Ordinal);
        Assert.Contains("title=\"@Text[\"Toolbar_NewDiagram\"]\"", component, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", component, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", component, StringComparison.Ordinal);
        Assert.Contains("@Text[\"Dialog_NewDocument_Title\"]", component, StringComparison.Ordinal);
        Assert.Contains("@Text[\"Dialog_NewDocument_Description\"]", component,
            StringComparison.Ordinal);
        Assert.Contains("id=\"@DomId(\"new-diagram-cancel\")\"", component,
            StringComparison.Ordinal);
        Assert.Matches(
            "id=\"@DomId\\(\"new-diagram-cancel\"\\)\"[\\s\\S]*?autofocus",
            component);
        Assert.Contains("@ref=\"_newDiagramCancelButton\"", component,
            StringComparison.Ordinal);
        Assert.Contains("_newDiagramCancelButton.FocusAsync(preventScroll: true)", component,
            StringComparison.Ordinal);
        Assert.Contains("id=\"@DomId(\"new-diagram-confirm\")\"", component,
            StringComparison.Ordinal);
        Assert.Contains("@onclick=\"ConfirmNewDiagramAsync\"", component,
            StringComparison.Ordinal);
        Assert.Contains("host.NewDiagramAsync", component, StringComparison.Ordinal);
    }

    [Fact]
    public void NewDiagramDoesNotIntroducePersistenceOrDeferredPhaseMechanisms()
    {
        var phase = string.Concat(
            ReadProductionFile(
                "Inceptus.DocumentEngine.Bpmn.Blazor",
                "Presentation",
                "DocumentCanvasHost.NewDiagram.cs"),
            ReadProductionFile(
                "Inceptus.DocumentEngine.Bpmn.Blazor",
                "Presentation",
                "DocumentCanvasHost.SessionReplacement.cs"),
            ReadProductionFile(
                "Inceptus.DocumentEngine.Bpmn.Blazor",
                "Components",
                "DocumentCanvas.razor"));

        string[] forbidden =
        [
            "ResetCommand",
            "ClearHistory",
            "localStorage",
            "Autosave",
            "dirtyDocument",
            "SimulationSession",
            "ProcessInstance",
            "Lane",
        ];
        Assert.All(forbidden, token => Assert.DoesNotContain(
            token,
            phase,
            StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertSessionReplacementOrdering(string source)
    {
        // Git checkouts may use LF or CRLF; source retirement order must not depend on that encoding.
        var normalized = source.ReplaceLineEndings("\n");
        string[] stages =
        [
            "_session = candidateSession;",
            "oldSession.StateChanged -=",
            "DisposeDocumentSessionResourcesAsync(\n                oldPointerObserver",
        ];
        var previousPosition = -1;
        foreach (var stage in stages)
        {
            var position = normalized.IndexOf(stage, StringComparison.Ordinal);
            Assert.True(position >= 0, $"Missing replacement stage: {stage}");
            Assert.True(position > previousPosition, $"Replacement stage is out of order: {stage}");
            previousPosition = position;
        }
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string ReadProductionFile(params string[] path) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, "src", .. path]));

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
