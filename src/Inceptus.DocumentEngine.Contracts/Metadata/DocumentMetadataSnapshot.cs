using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Metadata;

public sealed class DocumentMetadataSnapshot :
    IDocumentMetadataView,
    IEquatable<DocumentMetadataSnapshot>
{
    public DocumentMetadataSnapshot(
        DocumentId documentId,
        DocumentRevision revision,
        IEnumerable<KeyValuePair<string, PropertyValue>>? systemManagedProperties = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? extensionProperties = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);

        DocumentId = documentId;
        Revision = revision;
        SystemManagedProperties = new PropertyMap(systemManagedProperties);
        ExtensionProperties = new PropertyMap(extensionProperties);
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision Revision { get; }

    public PropertyMap SystemManagedProperties { get; }

    public PropertyMap ExtensionProperties { get; }

    public bool Equals(DocumentMetadataSnapshot? other) =>
        ReferenceEquals(this, other) ||
        (other is not null &&
         DocumentId == other.DocumentId &&
         Revision == other.Revision &&
         SystemManagedProperties.Equals(other.SystemManagedProperties) &&
         ExtensionProperties.Equals(other.ExtensionProperties));

    public override bool Equals(object? obj) => Equals(obj as DocumentMetadataSnapshot);

    public override int GetHashCode() =>
        HashCode.Combine(DocumentId, Revision, SystemManagedProperties, ExtensionProperties);
}
