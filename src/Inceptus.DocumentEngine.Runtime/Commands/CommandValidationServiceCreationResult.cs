using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Runtime.Commands;

/// <summary>
/// Represents the atomic outcome of creating a configured Command validation service.
/// </summary>
internal sealed class CommandValidationServiceCreationResult
{
    private CommandValidationServiceCreationResult(
        bool succeeded,
        CommandValidationService? service,
        ImmutableArray<Diagnostic> diagnostics)
    {
        Succeeded = succeeded;
        Service = service;
        Diagnostics = diagnostics;
    }

    internal bool Succeeded { get; }

    internal CommandValidationService? Service { get; }

    internal ImmutableArray<Diagnostic> Diagnostics { get; }

    internal static CommandValidationServiceCreationResult Success(
        CommandValidationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return new CommandValidationServiceCreationResult(true, service, []);
    }

    internal static CommandValidationServiceCreationResult Failure(
        IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var copiedDiagnostics = diagnostics.ToImmutableArray();

        if (copiedDiagnostics.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A failed Command validation service creation must include a diagnostic.",
                nameof(diagnostics));
        }

        if (copiedDiagnostics.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "Command validation service creation diagnostics cannot contain null values.",
                nameof(diagnostics));
        }

        if (!copiedDiagnostics.Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error))
        {
            throw new ArgumentException(
                "A failed Command validation service creation must include an error diagnostic.",
                nameof(diagnostics));
        }

        return new CommandValidationServiceCreationResult(false, null, copiedDiagnostics);
    }
}
