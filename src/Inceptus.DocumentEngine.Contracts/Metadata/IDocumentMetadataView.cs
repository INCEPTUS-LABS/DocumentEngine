using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Metadata;

public interface IDocumentMetadataView
{
    DocumentId DocumentId { get; }

    /// <summary>
    /// Gets the revision of the complete Document represented by this component view.
    /// </summary>
    DocumentRevision Revision { get; }

    PropertyMap SystemManagedProperties { get; }

    /// <summary>
    /// Gets persistent application- and plugin-defined document metadata.
    /// </summary>
    PropertyMap ExtensionProperties { get; }
}
