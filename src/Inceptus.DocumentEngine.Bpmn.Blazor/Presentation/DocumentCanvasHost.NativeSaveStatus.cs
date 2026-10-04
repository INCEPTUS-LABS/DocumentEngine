using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private Guid _nativeSaveAttachmentToken;
    private BpmnModelerSaveCheckpoint? _acknowledgedNativeSave;
    private readonly HashSet<BpmnModelerSaveCheckpoint> _issuedNativeSaves = [];

    private void ResetNativeSaveAttachmentUnderLock()
    {
        _nativeSaveAttachmentToken = Guid.NewGuid();
        _acknowledgedNativeSave = null;
        _issuedNativeSaves.Clear();
    }

    internal BpmnModelerNativeSaveStatus? CaptureNativeSaveStatus()
    {
        lock (_sync)
        {
            if (_disposed || !_initialized || _session is not { } session ||
                !session.TryCaptureDocumentSnapshot(out var snapshot))
                return null;
            return new BpmnModelerNativeSaveStatus(CreateNativeSaveCheckpointUnderLock(snapshot), _acknowledgedNativeSave);
        }
    }

    internal bool AcknowledgeNativeSave(BpmnModelerSaveCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        lock (_sync)
        {
            if (_disposed || !_initialized || checkpoint.AttachmentToken != _nativeSaveAttachmentToken ||
                !_issuedNativeSaves.Contains(checkpoint))
                return false;
            // Do not move the acknowledged revision backwards when asynchronous stores finish out of order.
            if (_acknowledgedNativeSave is null || _acknowledgedNativeSave.Revision.Value < checkpoint.Revision.Value)
                _acknowledgedNativeSave = checkpoint;
            return true;
        }
    }

    private BpmnModelerSaveCheckpoint CreateNativeSaveCheckpointUnderLock(DocumentSnapshot snapshot) =>
        new(_nativeSaveAttachmentToken, snapshot.DocumentId, snapshot.Revision);

    private BpmnModelerSaveCheckpoint IssueNativeSaveCheckpoint(DocumentSnapshot snapshot)
    {
        lock (_sync)
        {
            var checkpoint = CreateNativeSaveCheckpointUnderLock(snapshot);
            _issuedNativeSaves.Add(checkpoint);
            return checkpoint;
        }
    }

    private void MarkImportedNativeSnapshotSaved(DocumentSnapshot snapshot)
    {
        lock (_sync)
        {
            _acknowledgedNativeSave = CreateNativeSaveCheckpointUnderLock(snapshot);
        }
    }
}
