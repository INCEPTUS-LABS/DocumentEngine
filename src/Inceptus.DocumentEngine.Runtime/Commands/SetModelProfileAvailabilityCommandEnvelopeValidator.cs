using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class SetModelProfileAvailabilityCommandEnvelopeValidator :
    ICommandEnvelopeValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command is SetModelProfileAvailabilityCommand
            ? []
            :
            [
                new Diagnostic(
                    CommandExecutionDiagnosticCodes.InvalidCommandStructure,
                    DiagnosticSeverity.Error,
                    $"Command type '{command.TypeId}' does not use its registered immutable request shape.",
                    command.TypeId.Value),
            ];
    }
}
