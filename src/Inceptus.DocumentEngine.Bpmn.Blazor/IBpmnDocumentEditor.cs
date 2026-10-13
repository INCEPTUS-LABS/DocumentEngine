using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Presentation;

namespace Inceptus.DocumentEngine.Bpmn.Blazor;

/// <summary>
/// Document exchange and editing operations exposed to a host. All returned state is immutable;
/// the live Document, session, renderer and History remain owned by the component.
/// </summary>
public interface IBpmnDocumentEditor
{
    BpmnModelerDocumentResult GetDocument();
    ValueTask<BpmnModelerDocumentResult> GetDocumentAsync(CancellationToken cancellationToken = default);

    ValueTask<BpmnModelerDocumentResult> SetDocumentAsync(DocumentSnapshot? document,
        CancellationToken cancellationToken = default);

    ValueTask<BpmnModelerDocumentResult> SetDocumentAsync(DocumentSnapshot? document,
        BpmnModelerDocumentLoadOptions options, CancellationToken cancellationToken = default);

    BpmnEditorContext? CaptureEditorContext();

    BpmnModelerNativeSaveStatus? NativeSaveStatus { get; }
    BpmnModelerSaveCheckpoint? CreateSaveCheckpoint(DocumentSnapshot document);
    bool AcknowledgeNativeSave(BpmnModelerSaveCheckpoint checkpoint);

    ValueTask<EditingSessionPresentationCaptureResult?> CapturePresentationAsync(CancellationToken cancellationToken = default);

    ValueTask<BpmnModelerDocumentResult> UpdatePublicationAsync(long attachmentVersion,
        DocumentId documentId, DocumentRevision revision, DocumentPublicationSnapshot publication,
        CancellationToken cancellationToken = default);

    ValueTask<Canvas2DInteractionResult?> SelectVisualAsync(DocumentId documentId, DocumentRevision revision,
        PresentationViewContext context, VisualStateId visualStateId, CancellationToken cancellationToken = default);
}

/// <summary>Read-only observation, including transient presentation choices; never a persistent authority.</summary>
public sealed record BpmnEditorContext(long AttachmentVersion, DocumentSnapshot Document,
    EditingSessionState Presentation, bool CanReplaceDocument, Canvas2DSurfaceSize? SurfaceSize = null,
    bool HasOpenProperties = false);

/// <summary>Editor attachment guards for a prepared snapshot; does not parse, migrate or validate a file.</summary>
public sealed class BpmnModelerDocumentLoadOptions
{
    public bool VerifySavedRouting { get; init; }
    public bool ReprepareIncompatibleRouting { get; init; }
}
