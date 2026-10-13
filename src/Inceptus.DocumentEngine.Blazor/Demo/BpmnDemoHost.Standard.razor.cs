using System.Collections.Immutable;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Microsoft.AspNetCore.Components.Forms;

namespace Inceptus.DocumentEngine.Blazor.Demo;

public partial class BpmnDemoHost
{
    private string? _documentMenu;
    private bool _standardInProgress;
    private int _standardInputKey;
    private string? _standardFeedbackHeading;
    private bool _standardSucceeded;
    private ImmutableArray<BpmnConversionLogEntry> _conversionLog = [];
    private ImmutableArray<Diagnostic> _standardDiagnostics = [];

    private void ToggleDocumentMenu(string menu) => _documentMenu = _documentMenu == menu ? null : menu;
    private void CloseDocumentMenu() => _documentMenu = null;
    private int ConversionCount(BpmnConversionDisposition disposition) =>
        _conversionLog.Count(entry => entry.Disposition == disposition);
    private void DismissStandardFeedback()
    {
        _standardFeedbackHeading = null;
        _conversionLog = [];
        _standardDiagnostics = [];
    }

    private async Task ImportStandardDocumentAsync(InputFileChangeEventArgs args)
    {
        if (_disposed || !CanUseNativeDocumentTransfer) return;
        CloseDocumentMenu();
        _standardInProgress = true;
        DismissStandardFeedback();
        try
        {
            await InvokeAsync(StateHasChanged);
            if (args.File.Size > MaximumNativeDocumentFileSize)
            {
                CompleteStandardFeedback("BPMN import failed", false,
                    [new Diagnostic("BPMN_FILE_TOO_LARGE", DiagnosticSeverity.Error, "The file exceeds the 16 MiB import limit.")]);
                return;
            }
            await using var stream = args.File.OpenReadStream(MaximumNativeDocumentFileSize, _componentLifetime.Token);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var xml = await reader.ReadToEndAsync(_componentLifetime.Token);
            await using var renderer = new Canvas2DRenderer(JavaScript, DemoBpmnImportPreparation.RendererConfiguration);
            var initialized = await renderer.InitializeAsync(DomId("import-measurement"), new(1, 1, 1), _componentLifetime.Token);
            if (!initialized.Succeeded)
            {
                CompleteStandardFeedback("BPMN import failed", false, initialized.Diagnostics);
                return;
            }
            var result = await Operations.ImportStandardDocumentAsync(xml, DemoBpmnImportPreparation.Create(renderer), _componentLifetime.Token);
            _conversionLog = Operations.ConversionLog;
            CompleteStandardFeedback(result.Succeeded ? "BPMN import completed" : "BPMN import failed", result.Succeeded, result.Diagnostics);
            if (result.Succeeded && _host is { } host)
            {
                RefreshHostState(host);
                _issuesPanel.Close();
            }
        }
        catch (OperationCanceledException) when (_componentLifetime.IsCancellationRequested) { }
#pragma warning disable CA1031 // Browser failures become bounded host feedback; the current editor stays intact.
        catch (Exception exception) when (IsNonFatalNativeDocumentException(exception))
        {
            CompleteStandardFeedback("BPMN import failed", false,
                [new Diagnostic("BPMN_IMPORT_FAILED", DiagnosticSeverity.Error, "The BPMN file could not be read or prepared. The current document was not replaced.")]);
        }
#pragma warning restore CA1031
        finally { _standardInProgress = false; _standardInputKey++; }
    }

    private async Task ExportStandardDocumentAsync()
    {
        if (_disposed || !CanUseNativeDocumentTransfer) return;
        CloseDocumentMenu();
        _standardInProgress = true;
        DismissStandardFeedback();
        try
        {
            await InvokeAsync(StateHasChanged);
            var result = await Operations.ExportStandardDocumentAsync(_componentLifetime.Token);
            _conversionLog = Operations.ConversionLog;
            if (!result.Succeeded)
            {
                CompleteStandardFeedback("BPMN export failed", false, result.Diagnostics);
                return;
            }
            await BrowserFileDownload.DownloadAsync(JavaScript, result.Artifact!.Content.AsMemory(),
                result.Artifact.ContentType, result.Artifact.FileName, _componentLifetime.Token);
            CompleteStandardFeedback("BPMN export completed", true, result.Diagnostics);
        }
        catch (OperationCanceledException) when (_componentLifetime.IsCancellationRequested) { }
#pragma warning disable CA1031 // Browser failures become bounded host feedback.
        catch (Exception exception) when (IsNonFatalNativeDocumentException(exception))
        {
            CompleteStandardFeedback("BPMN export failed", false,
                [new Diagnostic("BPMN_DOWNLOAD_FAILED", DiagnosticSeverity.Error, "The BPMN file could not be downloaded.")]);
        }
#pragma warning restore CA1031
        finally { _standardInProgress = false; }
    }

    private void CompleteStandardFeedback(string heading, bool succeeded, ImmutableArray<Diagnostic> diagnostics)
    {
        if (_disposed) return;
        _standardFeedbackHeading = heading;
        _standardSucceeded = succeeded;
        _standardDiagnostics = diagnostics;
    }
}
