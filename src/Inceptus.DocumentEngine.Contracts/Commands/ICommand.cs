using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

public interface ICommand
{
    CommandTypeId TypeId { get; }

    DocumentId TargetDocumentId { get; }

    DocumentRevision ExpectedRevision { get; }

    CommandCategory Category { get; }

    AuthoritativeDocumentComponent AffectedComponents { get; }
}
