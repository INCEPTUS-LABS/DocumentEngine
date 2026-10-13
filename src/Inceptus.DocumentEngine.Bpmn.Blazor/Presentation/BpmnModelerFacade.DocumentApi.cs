using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Presentation;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class BpmnModelerFacade
{
    internal event Action? ViewChanged;
    private BpmnEditorContext? _lastViewContext;
    private void OnViewChanged()
    {
        var context = CaptureEditorContext();
        if (context?.Presentation.IsPanPresentationPending == true) return;
        var previous = _lastViewContext;
        if (context is not null && previous is not null &&
            context.AttachmentVersion == previous.AttachmentVersion && context.Document == previous.Document &&
            context.Presentation.PresentationContext == previous.Presentation.PresentationContext &&
            context.CanReplaceDocument == previous.CanReplaceDocument && context.SurfaceSize == previous.SurfaceSize)
            return;
        _lastViewContext = context;
        ViewChanged?.Invoke();
    }

    public BpmnModelerDocumentResult GetDocument() => CaptureDocumentSnapshot();

    public ValueTask<BpmnModelerDocumentResult> GetDocumentAsync(CancellationToken cancellationToken = default) =>
        ExecuteDocumentOperationAsync(BpmnModelerOperation.Capture, (host, token) => host.GetDocumentAsync(token), cancellationToken);

    public ValueTask<BpmnModelerDocumentResult> SetDocumentAsync(DocumentSnapshot? document,
        CancellationToken cancellationToken = default) =>
        SetDocumentAsync(document, new BpmnModelerDocumentLoadOptions(), cancellationToken);

    public ValueTask<BpmnModelerDocumentResult> SetDocumentAsync(DocumentSnapshot? document,
        BpmnModelerDocumentLoadOptions options, CancellationToken cancellationToken = default) =>
        ExecuteDocumentOperationAsync(BpmnModelerOperation.Load,
            (host, token) => host.SetDocumentAsync(document, options, token), cancellationToken);

    public BpmnEditorContext? CaptureEditorContext() => CaptureHost()?.CaptureEditorContext();

    public BpmnModelerSaveCheckpoint? CreateSaveCheckpoint(DocumentSnapshot document) => CaptureHost()?.CreateSaveCheckpoint(document);

    public ValueTask<EditingSessionPresentationCaptureResult?> CapturePresentationAsync(CancellationToken cancellationToken = default) =>
        CaptureHost()?.CapturePresentationAsync(cancellationToken) ?? ValueTask.FromResult<EditingSessionPresentationCaptureResult?>(null);

    public ValueTask<BpmnModelerDocumentResult> UpdatePublicationAsync(long attachmentVersion,
        DocumentId documentId, DocumentRevision revision, DocumentPublicationSnapshot publication,
        CancellationToken cancellationToken = default) =>
        ExecuteDocumentOperationAsync(BpmnModelerOperation.Publish,
            (host, token) => host.UpdatePublicationAsync(attachmentVersion, documentId, revision, publication, token), cancellationToken);

    public ValueTask<Canvas2DInteractionResult?> SelectVisualAsync(DocumentId documentId, DocumentRevision revision,
        PresentationViewContext context, VisualStateId visualStateId, CancellationToken cancellationToken = default) =>
        CaptureHost()?.SelectVisualAsync(documentId, revision, context, visualStateId, cancellationToken) ?? ValueTask.FromResult<Canvas2DInteractionResult?>(null);
}
