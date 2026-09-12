using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Documents;

public interface IDocumentView
{
    DocumentId DocumentId { get; }

    DocumentRevision Revision { get; }

    ISemanticModelView SemanticModel { get; }

    IVisualModelView VisualModel { get; }

    IDocumentMetadataView Metadata { get; }

    DocumentPublicationSnapshot? Publication { get; }
}
