using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Runtime.Commands;

/// <summary>
/// Orchestrates deterministic, read-only Command validation against one Document snapshot.
/// </summary>
internal sealed class CommandValidationService
{
    private readonly CommandValidatorRegistry _registry;

    private CommandValidationService(CommandValidatorRegistry registry) => _registry = registry;

    internal static CommandValidationServiceCreationResult Create(
        IEnumerable<CommandValidatorRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var copiedRegistrations = registrations.ToArray();

        if (Array.Exists(copiedRegistrations, static registration => registration is null))
        {
            throw new ArgumentException(
                "Command validator registrations cannot contain null values.",
                nameof(registrations));
        }

        Array.Sort(copiedRegistrations, CompareRegistrations);

        var diagnostics = FindDuplicateRegistrations(copiedRegistrations);

        if (diagnostics.Count != 0)
        {
            return CommandValidationServiceCreationResult.Failure(diagnostics);
        }

        var registry = new CommandValidatorRegistry([.. copiedRegistrations]);
        return CommandValidationServiceCreationResult.Success(new CommandValidationService(registry));
    }

    internal CommandValidationResult Validate(ICommand command, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(command.TypeId);
        ArgumentNullException.ThrowIfNull(command.TargetDocumentId);

        var hasValidators = HasValidators(command.TypeId);
        var envelope = ValidateEnvelopeCore(
            command,
            snapshot,
            hasValidators,
            envelopeValidator: null);
        if (!envelope.IsValid)
        {
            return envelope;
        }

        return ValidateDelegated(command, snapshot, CancellationToken.None);
    }

    internal bool HasValidators(CommandTypeId typeId) =>
        _registry.Contains(typeId);

    internal CommandValidationResult ValidateEnvelope(
        ICommand command,
        DocumentSnapshot snapshot,
        CommandHandlerRegistration? handlerRegistration)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(command.TypeId);
        ArgumentNullException.ThrowIfNull(command.TargetDocumentId);

        return ValidateEnvelopeCore(
            command,
            snapshot,
            handlerRegistration is not null,
            handlerRegistration?.EnvelopeValidator);
    }

    private CommandValidationResult ValidateEnvelopeCore(
        ICommand command,
        DocumentSnapshot snapshot,
        bool hasExecutionSupport,
        ICommandEnvelopeValidator? envelopeValidator)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(command.TypeId);
        ArgumentNullException.ThrowIfNull(command.TargetDocumentId);

        var diagnostics = new List<Diagnostic>();

        ValidateTargetDocument(command, snapshot, diagnostics);
        ValidateExpectedRevision(command, snapshot, diagnostics);
        ValidateAffectedComponents(command, diagnostics);

        if (!hasExecutionSupport)
        {
            var hasValidators = HasValidators(command.TypeId);
            diagnostics.Add(Error(
                hasValidators
                    ? CommandExecutionDiagnosticCodes.MissingHandler
                    : CommandValidationDiagnosticCodes.UnsupportedCommandType,
                hasValidators
                    ? $"Command type '{command.TypeId}' has no registered execution handler."
                    : $"Command type '{command.TypeId}' has no registered execution support.",
                command.TypeId.Value,
                new KeyValuePair<string, string>("CommandTypeId", command.TypeId.Value)));
        }

        if (diagnostics.Count == 0)
        {
            ValidateWithEnvelopePolicy(command, envelopeValidator, diagnostics);
        }

        return Result(command, snapshot, diagnostics);
    }

    internal CommandValidationResult ValidateDelegated(
        ICommand command,
        DocumentSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(command.TypeId);
        ArgumentNullException.ThrowIfNull(command.TargetDocumentId);

        var diagnostics = new List<Diagnostic>();
        var validators = _registry.Find(command.TypeId);

        foreach (var registration in validators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateWithRegistration(
                registration,
                command,
                snapshot,
                diagnostics,
                cancellationToken);
        }

        return Result(command, snapshot, diagnostics);
    }

    private static int CompareRegistrations(
        CommandValidatorRegistration left,
        CommandValidatorRegistration right)
    {
        var typeComparison = StringComparer.Ordinal.Compare(
            left.TypeId.Value,
            right.TypeId.Value);

        return typeComparison != 0
            ? typeComparison
            : StringComparer.Ordinal.Compare(left.ValidatorId.Value, right.ValidatorId.Value);
    }

    private static List<Diagnostic> FindDuplicateRegistrations(
        CommandValidatorRegistration[] registrations)
    {
        var diagnostics = new List<Diagnostic>();

        for (var index = 1; index < registrations.Length; index++)
        {
            var previous = registrations[index - 1];
            var current = registrations[index];

            if (previous.TypeId != current.TypeId || previous.ValidatorId != current.ValidatorId)
            {
                continue;
            }

            if (index > 1)
            {
                var beforePrevious = registrations[index - 2];

                if (beforePrevious.TypeId == current.TypeId &&
                    beforePrevious.ValidatorId == current.ValidatorId)
                {
                    continue;
                }
            }

            diagnostics.Add(Error(
                CommandValidationDiagnosticCodes.DuplicateValidatorRegistration,
                $"Validator '{current.ValidatorId}' is registered more than once for Command type '{current.TypeId}'.",
                current.ValidatorId.Value,
                new KeyValuePair<string, string>("CommandTypeId", current.TypeId.Value),
                new KeyValuePair<string, string>("ValidatorId", current.ValidatorId.Value)));
        }

        return diagnostics;
    }

    private static void ValidateTargetDocument(
        ICommand command,
        DocumentSnapshot snapshot,
        List<Diagnostic> diagnostics)
    {
        if (command.TargetDocumentId == snapshot.DocumentId)
        {
            return;
        }

        diagnostics.Add(Error(
            CommandValidationDiagnosticCodes.TargetDocumentMismatch,
            $"Command '{command.TypeId}' targets a different Document.",
            command.TypeId.Value,
            new KeyValuePair<string, string>(
                "ActualDocumentId",
                snapshot.DocumentId.Value),
            new KeyValuePair<string, string>(
                "TargetDocumentId",
                command.TargetDocumentId.Value)));
    }

    private static void ValidateExpectedRevision(
        ICommand command,
        DocumentSnapshot snapshot,
        List<Diagnostic> diagnostics)
    {
        var expectedRevision = command.ExpectedRevision;

        if (expectedRevision == snapshot.Revision)
        {
            return;
        }

        diagnostics.Add(Error(
            CommandValidationDiagnosticCodes.StaleRevision,
            $"Command '{command.TypeId}' expects a different Document revision.",
            command.TypeId.Value,
            new KeyValuePair<string, string>(
                "ActualRevision",
                snapshot.Revision.ToString()),
            new KeyValuePair<string, string>(
                "ExpectedRevision",
                expectedRevision.ToString())));
    }

    private static void ValidateAffectedComponents(
        ICommand command,
        List<Diagnostic> diagnostics)
    {
        var affectedComponents = command.AffectedComponents;
        var knownComponents =
            AuthoritativeDocumentComponent.SemanticModel |
            AuthoritativeDocumentComponent.VisualModel |
            AuthoritativeDocumentComponent.Metadata |
            AuthoritativeDocumentComponent.Publication;
        var hasOnlyKnownComponents =
            affectedComponents != AuthoritativeDocumentComponent.None &&
            (affectedComponents & ~knownComponents) == AuthoritativeDocumentComponent.None;
        var categoryIsDefined = Enum.IsDefined(command.Category);
        var categoryMatches = categoryIsDefined &&
            hasOnlyKnownComponents &&
            CategoryMatches(command.Category, affectedComponents);

        if (categoryMatches)
        {
            return;
        }

        diagnostics.Add(Error(
            CommandValidationDiagnosticCodes.InvalidAffectedComponentDeclaration,
            $"Command '{command.TypeId}' has an invalid category or affected-component scope.",
            command.TypeId.Value,
            new KeyValuePair<string, string>("AffectedComponents", affectedComponents.ToString()),
            new KeyValuePair<string, string>("Category", command.Category.ToString())));
    }

    private static void ValidateWithEnvelopePolicy(
        ICommand command,
        ICommandEnvelopeValidator? envelopeValidator,
        List<Diagnostic> diagnostics)
    {
        if (envelopeValidator is null)
        {
            return;
        }

#pragma warning disable CA1031 // A policy fault is converted to deterministic envelope failure.
        try
        {
            var policyDiagnostics = envelopeValidator.Validate(command);
            if (policyDiagnostics.IsDefault ||
                policyDiagnostics.Any(static diagnostic => diagnostic is null))
            {
                diagnostics.Add(InvalidCommandStructure(command));
                return;
            }

            diagnostics.AddRange(policyDiagnostics);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            diagnostics.Add(InvalidCommandStructure(
                command,
                exception.GetType().FullName ?? exception.GetType().Name));
        }
#pragma warning restore CA1031
    }

    private static Diagnostic InvalidCommandStructure(
        ICommand command,
        string? exceptionType = null)
    {
        var context = new List<KeyValuePair<string, string>>
        {
            new("CommandTypeId", command.TypeId.Value),
        };

        if (exceptionType is not null)
        {
            context.Add(new("ExceptionType", exceptionType));
        }

        return Error(
            CommandExecutionDiagnosticCodes.InvalidCommandStructure,
            $"Command type '{command.TypeId}' failed its registered immutable envelope policy.",
            command.TypeId.Value,
            [.. context]);
    }

    private static bool CategoryMatches(
        CommandCategory category,
        AuthoritativeDocumentComponent components) =>
        category switch
        {
            CommandCategory.Semantic =>
                components == AuthoritativeDocumentComponent.SemanticModel,
            CommandCategory.Visual =>
                components == AuthoritativeDocumentComponent.VisualModel,
            CommandCategory.Metadata =>
                components == AuthoritativeDocumentComponent.Metadata,
            CommandCategory.Publication =>
                components == AuthoritativeDocumentComponent.Publication,
            CommandCategory.Document or CommandCategory.Compound =>
                HasMultipleComponents(components),
            _ => false,
        };

    private static bool HasMultipleComponents(AuthoritativeDocumentComponent components) =>
        components != AuthoritativeDocumentComponent.SemanticModel &&
        components != AuthoritativeDocumentComponent.VisualModel &&
        components != AuthoritativeDocumentComponent.Metadata &&
        components != AuthoritativeDocumentComponent.Publication;

    private static void ValidateWithRegistration(
        CommandValidatorRegistration registration,
        ICommand command,
        DocumentSnapshot snapshot,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
#pragma warning disable CA1031 // A validator fault is converted to a deterministic validation failure.
        try
        {
            var validatorDiagnostics = registration.Validator.Validate(command, snapshot);

            if (validatorDiagnostics.IsDefault ||
                validatorDiagnostics.Any(static diagnostic => diagnostic is null))
            {
                diagnostics.Add(ValidatorFailure(
                    registration));
                return;
            }

            if (validatorDiagnostics.Any(static diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics.Add(ValidatorFailure(
                    registration));
            }

            foreach (var diagnostic in validatorDiagnostics)
            {
                diagnostics.Add(diagnostic);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            diagnostics.Add(ValidatorFailure(
                registration,
                exception.GetType().FullName ?? exception.GetType().Name));
        }
#pragma warning restore CA1031
    }

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;

    private static Diagnostic ValidatorFailure(
        CommandValidatorRegistration registration,
        string? exceptionType = null)
    {
        var context = new List<KeyValuePair<string, string>>
        {
            new("CommandTypeId", registration.TypeId.Value),
            new("ValidatorId", registration.ValidatorId.Value),
        };

        if (exceptionType is not null)
        {
            context.Add(new KeyValuePair<string, string>("ExceptionType", exceptionType));
        }

        return Error(
            CommandValidationDiagnosticCodes.ValidatorFailure,
            $"Validator '{registration.ValidatorId}' failed while validating Command type '{registration.TypeId}'.",
            registration.ValidatorId.Value,
            [.. context]);
    }

    private static CommandValidationResult Result(
        ICommand command,
        DocumentSnapshot snapshot,
        IEnumerable<Diagnostic> diagnostics) =>
        new(
            snapshot.DocumentId,
            snapshot.Revision,
            command.TypeId,
            diagnostics);

    private static Diagnostic Error(
        string code,
        string message,
        string sourceIdentity,
        params KeyValuePair<string, string>[] context) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity, context);
}
