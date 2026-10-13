using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Components;

public partial class InceptusBpmnModeler
{
    /// <summary>Notifies the host that immutable editor context can be recaptured, including navigation changes.</summary>
    [Parameter]
    public EventCallback ViewChanged { get; set; }

    private void HandleViewChanged() => _ = InvokeAsync(() => ViewChanged.InvokeAsync());

    /// <summary>Captures the canonical document without serialization, validation or file operations.</summary>
    public BpmnModelerDocumentResult GetDocument() => Facade.GetDocument();

    public ValueTask<BpmnModelerDocumentResult> GetDocumentAsync(CancellationToken cancellationToken = default) => Facade.GetDocumentAsync(cancellationToken);

    /// <summary>Installs a prepared snapshot atomically, with fresh editor state and History.</summary>
    public ValueTask<BpmnModelerDocumentResult> SetDocumentAsync(DocumentSnapshot? document,
        CancellationToken cancellationToken = default) => Facade.SetDocumentAsync(document, cancellationToken);

    public ValueTask<BpmnModelerDocumentResult> SetDocumentAsync(DocumentSnapshot? document,
        BpmnModelerDocumentLoadOptions options, CancellationToken cancellationToken = default) =>
        Facade.SetDocumentAsync(document, options, cancellationToken);

    public BpmnEditorContext? CaptureEditorContext() => Facade.CaptureEditorContext();

    public BpmnModelerSaveCheckpoint? CreateSaveCheckpoint(DocumentSnapshot document) => Facade.CreateSaveCheckpoint(document);

    public ValueTask<EditingSessionPresentationCaptureResult?> CapturePresentationAsync(CancellationToken cancellationToken = default) =>
        Facade.CapturePresentationAsync(cancellationToken);

    public ValueTask<BpmnModelerDocumentResult> UpdatePublicationAsync(long attachmentVersion,
        DocumentId documentId, DocumentRevision revision, DocumentPublicationSnapshot publication,
        CancellationToken cancellationToken = default) =>
        Facade.UpdatePublicationAsync(attachmentVersion, documentId, revision, publication, cancellationToken);

    public ValueTask<Canvas2DInteractionResult?> SelectVisualAsync(DocumentId documentId, DocumentRevision revision,
        PresentationViewContext context, VisualStateId visualStateId, CancellationToken cancellationToken = default) =>
        Facade.SelectVisualAsync(documentId, revision, context, visualStateId, cancellationToken);
}
