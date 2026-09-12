using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Verifies the immutable request shape for one registered Command type before
/// transaction creation. Envelope validation does not observe Document state.
/// </summary>
public interface ICommandEnvelopeValidator
{
    ImmutableArray<Diagnostic> Validate(ICommand command);
}
