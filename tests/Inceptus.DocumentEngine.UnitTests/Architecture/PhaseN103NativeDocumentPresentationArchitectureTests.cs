namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN103NativeDocumentPresentationArchitectureTests
{
    [Fact]
    public void NativeDocumentUxPreparesAReadyReplacementBeforeDetachingTheActiveSession()
    {
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.NativeDocuments.cs");
        var replacement = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.SessionReplacement.cs");
        var importCall = host.IndexOf(
            "NativeDocumentSerializer.Import(",
            StringComparison.Ordinal);
        var acceptance = host.IndexOf(
            "if (!import.Succeeded)",
            importCall,
            StringComparison.Ordinal);
        var replacementCall = host.IndexOf(
            "ReplaceDocumentUnderGateAsync(",
            acceptance,
            StringComparison.Ordinal);
        var replacementAttach = replacement.IndexOf(
            "EditingSession.AttachAsync(",
            StringComparison.Ordinal);
        var detach = replacement.IndexOf(
            "oldSession.StateChanged -= HandleSessionStateChanged",
            replacementAttach,
            StringComparison.Ordinal);

        Assert.Contains("NativeDocumentSerializer.Export(snapshot)", host,
            StringComparison.Ordinal);
        Assert.True(importCall >= 0 && acceptance > importCall);
        Assert.True(replacementCall > acceptance);
        Assert.True(replacementAttach >= 0 && detach > replacementAttach);
        Assert.Contains("standbyCanvasElementId", replacement, StringComparison.Ordinal);
        Assert.Contains("_nativeDocumentCanvasElementId = standbyCanvasElementId", replacement,
            StringComparison.Ordinal);
        Assert.Contains("await session.DisposeAsync()", replacement, StringComparison.Ordinal);
        Assert.Contains("_validationSnapshot = null", replacement, StringComparison.Ordinal);
        Assert.Contains("_toolboxSelection.Clear()", replacement, StringComparison.Ordinal);
        Assert.DoesNotContain("ICommand", host + replacement, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonDocument", host + replacement, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", host + replacement, StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnSemanticTypes", host + replacement,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnPluginRegistration", host + replacement,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Organizational", host + replacement,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrowserSurfacesCarryOnlyBytesMimeFilenameAndTransientOperationState()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var bridge = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "BrowserFileDownload.cs");
        var javascript = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js");

        Assert.Contains("<InputFile", component, StringComparison.Ordinal);
        Assert.Contains("accept=\".inceptus.json,application/json\"", component,
            StringComparison.Ordinal);
        Assert.Contains("MaximumNativeDocumentFileSize", component,
            StringComparison.Ordinal);
        Assert.Contains("file.OpenReadStream(", component, StringComparison.Ordinal);
        Assert.Contains("ReadAndImportNativeDocumentFileAsync", component,
            StringComparison.Ordinal);
        Assert.Contains("DomId(\"document-canvas-standby\")", component,
            StringComparison.Ordinal);
        Assert.Contains("document-canvas-buffer-active", component,
            StringComparison.Ordinal);
        Assert.Contains("document.inceptus.json", component, StringComparison.Ordinal);
        Assert.Contains("application/json", component, StringComparison.Ordinal);
        Assert.Contains("ReadOnlyMemory<byte> bytes", bridge, StringComparison.Ordinal);
        Assert.Contains("string contentType", bridge, StringComparison.Ordinal);
        Assert.Contains("string fileName", bridge, StringComparison.Ordinal);
        Assert.Contains("new Blob([bytes], { type: contentType })", javascript,
            StringComparison.Ordinal);
        Assert.Contains("anchor.download = fileName", javascript, StringComparison.Ordinal);

        var boundary = bridge + javascript;
        string[] forbidden =
        [
            "\"Inceptus.Document\"",
            "formatVersion",
            "JSON.parse",
            "DocumentId",
            "ModelProfile",
            "BpmnSemanticTypes",
            "BpmnPluginRegistration",
            "CreateBpmn",
            "Organizational",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, boundary, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DemoChromeKeepsNativeFileActionsCompactAndFillsTheViewport()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var styles = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor.css");
        var rootStyles = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "InceptusBpmnModeler.razor.css");
        var demoIndex = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            "wwwroot",
            "index.html");

        Assert.DoesNotContain("document-canvas-header", component,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Persistent visual editing pipeline", component,
            StringComparison.Ordinal);
        Assert.Contains("class=\"native-document-action-icon\" aria-hidden=\"true\"",
            component,
            StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@Text[\"Toolbar_Import\"]\"", component,
            StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@Text[\"Toolbar_Export\"]\"", component,
            StringComparison.Ordinal);
        Assert.Contains("height: 100%", styles, StringComparison.Ordinal);
        Assert.Contains("height: 100%", rootStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("100dvh", styles + rootStyles, StringComparison.Ordinal);
        Assert.Contains("html, body, #app { width: 100%; height: 100%; margin: 0; }",
            demoIndex,
            StringComparison.Ordinal);
        Assert.Contains(".document-editor-workspace", styles, StringComparison.Ordinal);
        Assert.Contains("flex-direction: column", styles, StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 auto", styles, StringComparison.Ordinal);
        Assert.Contains("::deep .native-document-file-input", styles,
            StringComparison.Ordinal);
        Assert.DoesNotContain("height: clamp(28rem, 66vh, 48rem)", styles,
            StringComparison.Ordinal);
        Assert.DoesNotContain("width: min(1100px, 100%)", styles,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NativeEnvelopeVersionRemainsTheN102Authority()
    {
        var serializer = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Documents",
            "NativeDocumentSerializer.cs");
        var codec = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Documents",
            "NativeDocumentJsonCodec.cs");

        Assert.Contains("public static string FormatIdentifier => \"Inceptus.Document\";",
            serializer,
            StringComparison.Ordinal);
        Assert.Contains("public static int FormatVersion => 1;", serializer,
            StringComparison.Ordinal);
        Assert.DoesNotContain("FormatVersion = 2", serializer + codec,
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
