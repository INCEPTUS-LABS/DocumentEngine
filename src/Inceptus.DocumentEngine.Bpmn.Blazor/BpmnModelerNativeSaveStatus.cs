using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Blazor;

/// <summary>Identifies the exact native snapshot exported by one modeler attachment.</summary>
public sealed record BpmnModelerSaveCheckpoint
{
    internal BpmnModelerSaveCheckpoint(Guid attachmentToken, DocumentId documentId, DocumentRevision revision)
    {
        AttachmentToken = attachmentToken;
        DocumentId = documentId;
        Revision = revision;
    }

    /// <summary>Opaque runtime incarnation; never serialized into the Document.</summary>
    public Guid AttachmentToken { get; }
    public DocumentId DocumentId { get; }
    public DocumentRevision Revision { get; }
}

/// <summary>Reports conservative saved-state comparison for the current modeler attachment.</summary>
public sealed class BpmnModelerNativeSaveStatus
{
    internal BpmnModelerNativeSaveStatus(BpmnModelerSaveCheckpoint currentCheckpoint, BpmnModelerSaveCheckpoint? savedCheckpoint)
    {
        CurrentCheckpoint = currentCheckpoint;
        SavedCheckpoint = savedCheckpoint;
    }

    public BpmnModelerSaveCheckpoint CurrentCheckpoint { get; }
    public BpmnModelerSaveCheckpoint? SavedCheckpoint { get; }
    public bool IsDirty => CurrentCheckpoint != SavedCheckpoint;
}
