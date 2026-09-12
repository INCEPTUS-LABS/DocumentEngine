using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.DocumentEngine.Contracts.History;

/// <summary>
/// Prepares immutable restoration data for one successfully proposed Command commit.
/// </summary>
public interface ICommandHistoryPolicy
{
    CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed);
}
