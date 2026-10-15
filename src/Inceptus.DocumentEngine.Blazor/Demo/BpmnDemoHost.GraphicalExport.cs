using Inceptus.DocumentEngine.Canvas2D.Export;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Blazor.Demo;

public partial class BpmnDemoHost
{
    private bool _graphicalExportInProgress;

    private async Task ExportGraphicalAsync(GraphicalExportFormat format)
    {
        CloseDocumentMenu();
        if (_disposed || !CanUseNativeDocumentTransfer || _host is not { } editor) return;
        var attachmentVersion = editor.CaptureEditorContext()?.AttachmentVersion;
        _graphicalExportInProgress = true;
        DismissStandardFeedback();
        CompleteStandardFeedback($"Preparing {format.ToString().ToUpperInvariant()} image…", true, []);
        try
        {
            await InvokeAsync(StateHasChanged);
            var capture = await editor.CapturePresentationAsync(_componentLifetime.Token);
            if (capture?.Succeeded != true)
            {
                CompleteStandardFeedback("Image export unavailable", false, capture?.Diagnostics ??
                    [new Diagnostic("GRAPHICAL_EXPORT_UNAVAILABLE", DiagnosticSeverity.Error, "The active diagram is not ready for capture.")]);
                return;
            }
            var exporter = new Canvas2DGraphicalExporter(JavaScript, DemoBpmnImportPreparation.RendererConfiguration);
            var result = await exporter.ExportAsync(capture.Capture!, format, cancellationToken: _componentLifetime.Token);
            if (result.Status == GraphicalExportStatus.Cancelled) return;
            if (!result.Succeeded)
            {
                CompleteStandardFeedback("Image export failed", false, result.Diagnostics);
                return;
            }
            // A detached capture remains valid if editing continues. Never deliver it to a replaced/disposed host.
            if (_disposed || !ReferenceEquals(editor, _host) || editor.CaptureEditorContext() is not { } current ||
                current.AttachmentVersion != attachmentVersion || current.Document.DocumentId != capture.Capture!.Document.DocumentId) return;
            await BrowserFileDownload.DownloadAsync(JavaScript, result.Artifact!.Content.AsMemory(),
                result.Artifact.ContentType, result.Artifact.FileName, _componentLifetime.Token);
            CompleteStandardFeedback("Image export completed", true, result.Diagnostics);
        }
        catch (OperationCanceledException) when (_componentLifetime.IsCancellationRequested) { }
#pragma warning disable CA1031 // Bound browser/capture failures to operation feedback without changing editor state.
        catch (Exception exception) when (IsNonFatalNativeDocumentException(exception))
        {
            CompleteStandardFeedback("Image export failed", false,
                [new Diagnostic("GRAPHICAL_EXPORT_FAILED", DiagnosticSeverity.Error, "The image could not be generated or downloaded.")]);
        }
#pragma warning restore CA1031
        finally { _graphicalExportInProgress = false; }
    }
}
