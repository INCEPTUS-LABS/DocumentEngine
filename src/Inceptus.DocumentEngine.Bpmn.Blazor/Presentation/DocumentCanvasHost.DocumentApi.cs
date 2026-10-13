using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    internal event Action? ViewChanged;

    public BpmnModelerDocumentResult GetDocument() => CaptureDocumentSnapshot();

    public async ValueTask<BpmnModelerDocumentResult> GetDocumentAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        try { await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { return new(BpmnModelerOperationStatus.Cancelled, null, []); }
        try
        {
            if (_session is { } session)
            {
                var snapshot = await session.CaptureDocumentSnapshotAsync(linked.Token).ConfigureAwait(false);
                if (snapshot is not null && session.CaptureState().Status is EditingSessionStatus.Ready or EditingSessionStatus.Rebuilding)
                    return new(BpmnModelerOperationStatus.Succeeded, snapshot, []);
            }
            return GetDocument();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { return new(BpmnModelerOperationStatus.Cancelled, null, []); }
        finally { await CompleteModelerOperationAsync().ConfigureAwait(false); }
    }

    public BpmnModelerNativeSaveStatus? NativeSaveStatus => CaptureNativeSaveStatus();

    public BpmnModelerSaveCheckpoint? CreateSaveCheckpoint(DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_sync)
        {
            return !_disposed && _initialized && _session is { } session &&
                session.TryCaptureDocumentSnapshot(out var current) && document.Equals(current)
                ? IssueNativeSaveCheckpoint(document) : null;
        }
    }

    public ValueTask<BpmnModelerDocumentResult> SetDocumentAsync(DocumentSnapshot? document,
        CancellationToken cancellationToken = default) =>
        SetDocumentAsync(document, new BpmnModelerDocumentLoadOptions(), cancellationToken);

    public async ValueTask<BpmnModelerDocumentResult> SetDocumentAsync(DocumentSnapshot? document,
        BpmnModelerDocumentLoadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var result = await LoadDocumentAsync(document, options, cancellationToken).ConfigureAwait(false);
        return new(ModelerStatus(result.Status), result.Snapshot, result.Diagnostics);
    }

    public BpmnEditorContext? CaptureEditorContext()
    {
        lock (_sync)
        {
            if (_disposed || !_initialized || _session is not { } session ||
                !session.TryCaptureDocumentSnapshot(out var document)) return null;
            var state = session.CaptureState();
            if (document.DocumentId != state.DocumentId || document.Revision != state.DocumentRevision) return null;
            return new(_documentSessionVersion, document, state,
                state.Status == EditingSessionStatus.Ready && state.EditorState.ActiveGesture is null &&
                !_propertiesFormOpen && !_modelViewPropertiesFormOpen, _surfaceSize,
                _propertiesFormOpen || _modelViewPropertiesFormOpen);
        }
    }

    /// <summary>Read-only presentation capture; creates no publication package or validation report.</summary>
    public async ValueTask<EditingSessionPresentationCaptureResult?> CapturePresentationAsync(
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var context = CaptureEditorContext();
            if (context?.CanReplaceDocument != true || _session is not { } session) return null;
            return await session.CapturePresentationAsync(context.Presentation.ModelProfileViewState,
                context.Presentation.ModelProfileElementViewState, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Edits persistent publication metadata through the same command and History path as other edits.</summary>
    public async ValueTask<BpmnModelerDocumentResult> UpdatePublicationAsync(long attachmentVersion,
        DocumentId documentId, DocumentRevision revision, DocumentPublicationSnapshot publication,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(publication);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        try { await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { return new(BpmnModelerOperationStatus.Cancelled, null, []); }
        try
        {
            var context = CaptureEditorContext();
            if (context?.CanReplaceDocument != true || _session is not { } session)
                return new(BpmnModelerOperationStatus.Unavailable, null,
                    [Error("CANVAS_PUBLICATION_UNAVAILABLE", "Publication metadata cannot be edited while the editor is not ready.")]);
            if (context.AttachmentVersion != attachmentVersion || context.Document.DocumentId != documentId ||
                context.Document.Revision != revision)
                return new(BpmnModelerOperationStatus.Rejected, null,
                    [Error("CANVAS_PUBLICATION_STALE", "The active Document changed after the Publication dialog opened.")]);
            if (Equals(context.Document.Publication, publication)) return GetDocument();
            var result = await session.ExecuteAsync(new UpdateDocumentPublicationCommand(documentId, revision, publication),
                linked.Token).ConfigureAwait(false);
            if (!result.IsCommitted) return new(BpmnModelerOperationStatus.Rejected, null, result.Diagnostics);
            await session.WaitForIdleAsync(linked.Token).ConfigureAwait(false);
            return GetDocument();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { return new(BpmnModelerOperationStatus.Cancelled, null, []); }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
        }
    }
}
