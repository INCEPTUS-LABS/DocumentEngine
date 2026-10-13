using System.Collections.Immutable;
using System.Text;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Blazor.Demo;

internal sealed partial class DemoDocumentOperations
{
    internal const string StandardDocumentFileName = "document.xml";
    internal const string StandardDocumentContentType = "application/xml;charset=utf-8";
    internal ImmutableArray<BpmnConversionLogEntry> ConversionLog { get; private set; } = [];

    internal async ValueTask<BpmnModelerDocumentResult> ImportStandardDocumentAsync(string xml,
        IConnectorRoutingStatePreparer preparer, CancellationToken cancellationToken = default)
    {
        ConversionLog = [];
        var capture = await _editor.GetDocumentAsync(cancellationToken).ConfigureAwait(false);
        var source = _editor.CaptureEditorContext();
        if (!capture.Succeeded || source?.CanReplaceDocument != true)
            return new(BpmnModelerOperationStatus.Unavailable, null,
                [Error("BPMN_IMPORT_UNAVAILABLE", "BPMN import is unavailable while the editor is not ready.")]);

        var converted = await BpmnStandardConverter.ImportAsync(xml, preparer, cancellationToken).ConfigureAwait(false);
        ConversionLog = converted.ConversionLog;
        if (!converted.Succeeded)
            return new(BpmnModelerOperationStatus.Rejected, null, converted.Diagnostics);

        // Preparation is independent of the live editor. Never replace edits made while it ran.
        var current = _editor.CaptureEditorContext();
        if (current?.AttachmentVersion != source.AttachmentVersion || current.CanReplaceDocument != true ||
            !current.Document.Equals(capture.Snapshot))
            return new(BpmnModelerOperationStatus.Unavailable, null,
                [Error("BPMN_IMPORT_DOCUMENT_CHANGED", "The document changed during BPMN import. Please retry.")]);
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = await _editor.SetDocumentAsync(converted.Document,
            new BpmnModelerDocumentLoadOptions { VerifySavedRouting = true }, cancellationToken).ConfigureAwait(false);
        if (loaded.Succeeded)
        {
            _validation = null;
            DocumentValidation = null;
        }
        return new(loaded.Status, loaded.Snapshot, converted.Diagnostics.AddRange(loaded.Diagnostics));
    }

    internal async ValueTask<BpmnModelerFileResult> ExportStandardDocumentAsync(CancellationToken cancellationToken = default)
    {
        ConversionLog = [];
        var capture = await _editor.GetDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (!capture.Succeeded || _editor.CaptureEditorContext() is not { CanReplaceDocument: true })
            return new(BpmnModelerOperationStatus.Unavailable, null, capture.Diagnostics);
        var converted = BpmnStandardConverter.Export(capture.Snapshot!);
        ConversionLog = converted.ConversionLog;
        if (!converted.Succeeded)
            return new(BpmnModelerOperationStatus.Rejected, null, converted.Diagnostics);
        cancellationToken.ThrowIfCancellationRequested();
        return new(BpmnModelerOperationStatus.Succeeded,
            new BpmnModelerFileArtifact(StandardDocumentFileName, StandardDocumentContentType,
                ImmutableArray.Create(Encoding.UTF8.GetBytes(converted.Xml!))), converted.Diagnostics);
    }
}
