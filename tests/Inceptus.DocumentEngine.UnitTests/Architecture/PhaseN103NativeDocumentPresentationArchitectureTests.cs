namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN103NativeDocumentPresentationArchitectureTests
{
    [Fact]
    public void A32ExportAndItsSharedHelpersHaveNoImportProfileDependency()
    {
        var converter = ReadProductionFile("Inceptus.Bpmn.Integration", string.Empty, "BpmnStandardConverter.cs");
        var export = converter[converter.IndexOf("public static BpmnStandardConversionResult Export", StringComparison.Ordinal)..converter.IndexOf("public static BpmnStandardConversionResult Import", StringComparison.Ordinal)];
        Assert.DoesNotContain("Profile", export, StringComparison.Ordinal);
        Assert.Contains("new BpmnXmlExporter(document).Convert()", export, StringComparison.Ordinal);
        foreach (var file in new[] { "BpmnXmlExporter.cs", "BpmnXmlExporter.Geometry.cs", "BpmnXmlConversion.cs", "BpmnXmlSchema.cs" })
        {
            var source = ReadProductionFile("Inceptus.Bpmn.Integration", string.Empty, file);
            Assert.DoesNotContain("BpmnImport", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CompatibilityProfile", source, StringComparison.Ordinal);
        }
        var collector = ReadProductionFile("Inceptus.Bpmn.Integration", string.Empty, "BpmnXmlConversion.cs");
        Assert.Contains("BpmnConversionDisposition.Omitted, \"BPMN_EXPORT_OMITTED\"", collector, StringComparison.Ordinal);
        foreach (var file in new[] { "BpmnXmlImporter.cs", "BpmnXmlImporter.Geometry.cs" })
            Assert.DoesNotContain("BpmnConversionDisposition.Ignored", ReadProductionFile("Inceptus.Bpmn.Integration", string.Empty, file), StringComparison.Ordinal);
    }

    [Fact]
    public void A31TransferAndConversionBoundariesDoNotInvokeModelValidation()
    {
        var host = ReadProductionFile("Inceptus.DocumentEngine.Blazor", "Demo", "DemoDocumentOperations.cs");
        var nativeTransfers = host[host.IndexOf("internal async ValueTask<BpmnModelerDocumentResult> ImportNativeDocumentAsync", StringComparison.Ordinal)..host.IndexOf("internal void DismissDiagnostics", StringComparison.Ordinal)];
        var standardTransfers = ReadProductionFile("Inceptus.DocumentEngine.Blazor", "Demo", "DemoDocumentOperations.Standard.cs");
        Assert.DoesNotContain("_validator", nativeTransfers + standardTransfers, StringComparison.Ordinal);
        Assert.Contains("_validator.Validate", host[host.IndexOf("ValidateAsync", StringComparison.Ordinal)..], StringComparison.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src", "Inceptus.Bpmn.Integration"), "BpmnXml*.cs")
            .Append(Path.Combine(RepositoryRoot, "src", "Inceptus.Bpmn.Integration", "BpmnStandardConverter.cs")))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("BpmnDocumentValidator", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ModelValidationEngine", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ValidationRules", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NativeDocumentUxPreparesAReadyReplacementBeforeDetachingTheActiveSession()
    {
        var host = ReadProductionFile("Inceptus.DocumentEngine.Blazor", "Demo", "DemoDocumentOperations.cs");
        var loading = ReadProductionFile("Inceptus.DocumentEngine.Bpmn.Blazor", "Presentation", "DocumentCanvasHost.Loading.cs");
        var replacement = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.SessionReplacement.cs");
        var importCall = host.IndexOf(
            "BpmnDocumentSerializer.Deserialize(",
            StringComparison.Ordinal);
        var acceptance = host.IndexOf(
            "if (!read.Succeeded)",
            importCall,
            StringComparison.Ordinal);
        var replacementAttach = replacement.IndexOf(
            "EditingSession.AttachAsync(",
            StringComparison.Ordinal);
        var detach = replacement.IndexOf(
            "oldSession.StateChanged -= HandleSessionStateChanged",
            replacementAttach,
            StringComparison.Ordinal);

        Assert.Contains("BpmnDocumentSerializer.Serialize(capture.Snapshot!)", host,
            StringComparison.Ordinal);
        Assert.True(importCall >= 0 && acceptance > importCall);
        Assert.Contains("ReplaceDocumentUnderGateAsync(", loading, StringComparison.Ordinal);
        Assert.Contains("_editor.SetDocumentAsync", host, StringComparison.Ordinal);
        Assert.True(replacementAttach >= 0 && detach > replacementAttach);
        Assert.Contains("standbyCanvasElementId", replacement, StringComparison.Ordinal);
        Assert.Contains("_nativeDocumentCanvasElementId = standbyCanvasElementId", replacement,
            StringComparison.Ordinal);
        Assert.Contains("await session.DisposeAsync()", replacement, StringComparison.Ordinal);
        Assert.Contains("_validation = null", host, StringComparison.Ordinal);
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
            "Inceptus.DocumentEngine.Blazor",
            "Demo",
            "BpmnDemoHost.razor") + ReadProductionFile("Inceptus.DocumentEngine.Blazor", "Demo", "BpmnDemoHost.razor.cs");
        var bridge = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            "Demo",
            "BrowserFileDownload.cs");
        var javascript = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            "wwwroot",
            "inceptus.documents.js");

        Assert.Contains("<InputFile", component, StringComparison.Ordinal);
        Assert.Contains("accept=\".inceptus.json,application/json\"", component,
            StringComparison.Ordinal);
        Assert.Contains("MaximumNativeDocumentFileSize", component,
            StringComparison.Ordinal);
        Assert.Contains("file.OpenReadStream(", component, StringComparison.Ordinal);
        Assert.Contains("ReadAndImportNativeDocumentFileAsync", component,
            StringComparison.Ordinal);
        var editor = ReadProductionFile("Inceptus.DocumentEngine.Bpmn.Blazor", "Components", "DocumentCanvas.razor");
        Assert.Contains("DomId(\"document-canvas-standby\")", editor,
            StringComparison.Ordinal);
        Assert.Contains("document-canvas-buffer-active", editor,
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
            "Inceptus.DocumentEngine.Blazor",
            "Demo",
            "BpmnDemoHost.razor") + ReadProductionFile("Inceptus.DocumentEngine.Blazor", "Demo", "BpmnDemoHost.razor.cs");
        var styles = ReadProductionFile(
            "Inceptus.DocumentEngine.Blazor",
            "Demo",
            "BpmnDemoHost.razor.css");
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
        Assert.Contains("aria-label=\"Import Inceptus JSON\"", component,
            StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Export Inceptus JSON\"", component,
            StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Import BPMN 2.0.2\"", component, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Export BPMN 2.0.2\"", component, StringComparison.Ordinal);
        Assert.Contains(".document-format-menu[hidden] { display: none; }", styles, StringComparison.Ordinal);
        Assert.Contains("height: 100dvh", styles, StringComparison.Ordinal);
        Assert.Contains("height: 100%", rootStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("100dvh", rootStyles, StringComparison.Ordinal);
        Assert.Contains("html, body, #app { width: 100%; height: 100%; margin: 0; }",
            demoIndex,
            StringComparison.Ordinal);
        var editorStyles = ReadProductionFile("Inceptus.DocumentEngine.Bpmn.Blazor", "Components", "DocumentCanvas.razor.css");
        Assert.Contains(".document-editor-workspace", editorStyles, StringComparison.Ordinal);
        Assert.Contains("flex-direction: column", editorStyles, StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 auto", editorStyles, StringComparison.Ordinal);
        Assert.Contains("::deep .native-document-file-input", styles,
            StringComparison.Ordinal);
        Assert.DoesNotContain("height: clamp(28rem, 66vh, 48rem)", styles,
            StringComparison.Ordinal);
        Assert.DoesNotContain("width: min(1100px, 100%)", styles,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NativeEnvelopeVersionUsesTheApprovedV2Authority()
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
        Assert.Contains("public static int FormatVersion => 2;", serializer,
            StringComparison.Ordinal);
        Assert.DoesNotContain("FormatVersion = 1", serializer + codec,
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
