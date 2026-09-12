using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.History;

/// <summary>
/// Holds immutable restoration data and constructs a Command for the current revision.
/// </summary>
public interface IHistoryCommandFactory
{
    ICommand Create(DocumentId documentId, DocumentRevision expectedRevision);
}
