using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Documents;

/// <summary>
/// Captures all authoritative persistent state for one Document revision.
/// </summary>
public sealed class DocumentSnapshot : IDocumentView, IEquatable<DocumentSnapshot>
{
    public DocumentSnapshot(
        SemanticModelSnapshot semanticModel,
        VisualModelSnapshot visualModel,
        DocumentMetadataSnapshot metadata,
        DocumentPublicationSnapshot? publication = null)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);
        ArgumentNullException.ThrowIfNull(visualModel);
        ArgumentNullException.ThrowIfNull(metadata);

        EnsureCoherent(semanticModel.DocumentId, semanticModel.Revision, visualModel, nameof(visualModel));
        EnsureCoherent(semanticModel.DocumentId, semanticModel.Revision, metadata, nameof(metadata));

        DocumentId = semanticModel.DocumentId;
        Revision = semanticModel.Revision;
        SemanticModel = semanticModel;
        VisualModel = visualModel;
        Metadata = metadata;
        Publication = publication;
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision Revision { get; }

    public SemanticModelSnapshot SemanticModel { get; }

    public VisualModelSnapshot VisualModel { get; }

    public DocumentMetadataSnapshot Metadata { get; }

    public DocumentPublicationSnapshot? Publication { get; }

    ISemanticModelView IDocumentView.SemanticModel => SemanticModel;

    IVisualModelView IDocumentView.VisualModel => VisualModel;

    IDocumentMetadataView IDocumentView.Metadata => Metadata;

    DocumentPublicationSnapshot? IDocumentView.Publication => Publication;

    public bool Equals(DocumentSnapshot? other) =>
        ReferenceEquals(this, other) ||
        (other is not null &&
         DocumentId == other.DocumentId &&
         Revision == other.Revision &&
         SemanticModel.Equals(other.SemanticModel) &&
         VisualModel.Equals(other.VisualModel) &&
         Metadata.Equals(other.Metadata) &&
         Equals(Publication, other.Publication));

    public override bool Equals(object? obj) => Equals(obj as DocumentSnapshot);

    public override int GetHashCode() =>
        HashCode.Combine(
            DocumentId,
            Revision,
            SemanticModel,
            VisualModel,
            Metadata,
            Publication);

    private static void EnsureCoherent(
        DocumentId expectedDocumentId,
        DocumentRevision expectedRevision,
        VisualModelSnapshot component,
        string parameterName)
    {
        EnsureCoherent(
            expectedDocumentId,
            expectedRevision,
            component.DocumentId,
            component.Revision,
            parameterName);
    }

    private static void EnsureCoherent(
        DocumentId expectedDocumentId,
        DocumentRevision expectedRevision,
        DocumentMetadataSnapshot component,
        string parameterName)
    {
        EnsureCoherent(
            expectedDocumentId,
            expectedRevision,
            component.DocumentId,
            component.Revision,
            parameterName);
    }

    private static void EnsureCoherent(
        DocumentId expectedDocumentId,
        DocumentRevision expectedRevision,
        DocumentId actualDocumentId,
        DocumentRevision actualRevision,
        string parameterName)
    {
        if (actualDocumentId != expectedDocumentId)
        {
            throw new ArgumentException(
                "All Document snapshot components must belong to the same Document.",
                parameterName);
        }

        if (actualRevision != expectedRevision)
        {
            throw new ArgumentException(
                "All Document snapshot components must describe the same Document revision.",
                parameterName);
        }
    }
}
