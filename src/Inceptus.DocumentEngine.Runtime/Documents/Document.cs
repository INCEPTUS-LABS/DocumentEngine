using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Documents;

/// <summary>
/// Owns one coherent, authoritative Document state and exposes it read-only.
/// </summary>
public sealed class Document : IDocumentView
{
    private readonly DocumentId _documentId;
    private readonly DocumentExecutionCoordinator _executionCoordinator;
    private DocumentState _state;

    internal Document(
        DocumentSnapshot snapshot,
        IDocumentDispatchScheduler? dispatchScheduler = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _documentId = snapshot.DocumentId;
        _executionCoordinator = new DocumentExecutionCoordinator(dispatchScheduler);
        _state = new DocumentState(snapshot);
    }

    public DocumentId DocumentId => _documentId;

    public DocumentRevision Revision => CaptureState().Snapshot.Revision;

    public ISemanticModelView SemanticModel => CaptureState().Snapshot.SemanticModel;

    public IVisualModelView VisualModel => CaptureState().Snapshot.VisualModel;

    public IDocumentMetadataView Metadata => CaptureState().Snapshot.Metadata;

    public DocumentPublicationSnapshot? Publication => CaptureState().Snapshot.Publication;

    /// <summary>
    /// Captures all authoritative components from the same immutable state reference.
    /// </summary>
    public DocumentSnapshot CaptureSnapshot() => CaptureState().Snapshot;

    internal DocumentExecutionCoordinator ExecutionCoordinator => _executionCoordinator;

    internal DocumentState CaptureState() => Volatile.Read(ref _state);

    internal bool TryInstallState(DocumentState expectedState, DocumentState proposedState)
    {
        ArgumentNullException.ThrowIfNull(expectedState);
        ArgumentNullException.ThrowIfNull(proposedState);

        if (proposedState.Snapshot.DocumentId != _documentId)
        {
            throw new ArgumentException(
                "The proposed state must belong to this Document.",
                nameof(proposedState));
        }

        if (proposedState.Snapshot.Revision != expectedState.Snapshot.Revision.Increment())
        {
            throw new ArgumentException(
                "The proposed state must advance the expected state revision exactly once.",
                nameof(proposedState));
        }

        return ReferenceEquals(
            Interlocked.CompareExchange(ref _state, proposedState, expectedState),
            expectedState);
    }
}
