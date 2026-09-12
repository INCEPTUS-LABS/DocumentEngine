using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.DocumentEngine.Contracts.Commands;

public interface ICommandValidator
{
    ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document);
}
