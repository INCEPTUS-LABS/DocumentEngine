using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Runtime.Commands;

internal sealed class CreateTopLevelDocumentScopeCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not CreateTopLevelDocumentScopeCommand create)
        {
            return ValueTask.FromResult(TopLevelScopeCommandSupport.InvalidShape(command));
        }

        return ValueTask.FromResult(TopLevelScopeCommandSupport.SetPresence(
            document,
            create.ScopeId,
            shouldExist: true));
    }
}

internal sealed class RestoreTopLevelDocumentScopeCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not RestoreTopLevelDocumentScopeCommand restore)
        {
            return ValueTask.FromResult(TopLevelScopeCommandSupport.InvalidShape(command));
        }

        return ValueTask.FromResult(TopLevelScopeCommandSupport.SetPresence(
            document,
            restore.ScopeId,
            restore.ShouldExist));
    }
}

internal sealed record RestoreTopLevelDocumentScopeCommand : ICommand, ICommandPipelineInvalidation
{
    internal static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/restore-top-level-document-scope");

    internal RestoreTopLevelDocumentScopeCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        DocumentScopeId scopeId,
        bool shouldExist)
    {
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        ScopeId = scopeId;
        ShouldExist = shouldExist;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Semantic;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel;

    public DocumentScopeId ScopeId { get; }

    public bool ShouldExist { get; }

    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        PipelineInvalidation.Scene;
}

internal sealed class TopLevelDocumentScopeCommandEnvelopeValidator : ICommandEnvelopeValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command is CreateTopLevelDocumentScopeCommand or
            RestoreTopLevelDocumentScopeCommand
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

internal static class TopLevelScopeCommandSupport
{
    internal static CommandHandlerResult SetPresence(
        DocumentSnapshot document,
        DocumentScopeId scopeId,
        bool shouldExist)
    {
        var semanticModel = document.SemanticModel;
        var isImplicitRoot = scopeId == semanticModel.RootScopeId;
        var existing = semanticModel.NestedScopes.FirstOrDefault(scope => scope.Id == scopeId);
        if (shouldExist && (isImplicitRoot || existing is not null))
        {
            return Failure(
                CommandExecutionDiagnosticCodes.DocumentScopeIdentityExists,
                $"Document scope '{scopeId}' already exists.",
                scopeId.Value);
        }

        if (!shouldExist &&
            (existing is null ||
                existing.ParentScopeId is not null ||
                existing.OwnerSemanticElementId is not null))
        {
            return Failure(
                CommandExecutionDiagnosticCodes.DocumentScopeNotFound,
                $"Explicit peer top-level Document scope '{scopeId}' does not exist.",
                scopeId.Value);
        }

        if (!shouldExist &&
            (semanticModel.ScopeMemberships.Any(membership => membership.ScopeId == scopeId) ||
                semanticModel.NestedScopes.Any(scope => scope.ParentScopeId == scopeId)))
        {
            return Failure(
                CommandExecutionDiagnosticCodes.DocumentScopeNotEmpty,
                $"Explicit peer top-level Document scope '{scopeId}' is not empty.",
                scopeId.Value);
        }

        IEnumerable<DocumentScopeSnapshot> scopes = shouldExist
            ? semanticModel.NestedScopes.Append(new DocumentScopeSnapshot(scopeId))
            : semanticModel.NestedScopes.Where(scope => scope.Id != scopeId);
        var proposedSemanticModel = new SemanticModelSnapshot(
            document.DocumentId,
            document.Revision,
            semanticModel.Elements,
            semanticModel.Relationships,
            scopes,
            semanticModel.ScopeMemberships,
            semanticModel.ModelProfiles,
            semanticModel.ProfileAssignments);
        return CommandHandlerResult.Success(
            new DocumentSnapshot(
                proposedSemanticModel,
                document.VisualModel,
                document.Metadata,
                document.Publication),
            pipelineInvalidation: PipelineInvalidation.Scene);
    }

    internal static CommandHandlerResult InvalidShape(ICommand command) =>
        Failure(
            CommandExecutionDiagnosticCodes.HandlerFailure,
            $"Command type '{command.TypeId}' does not use the expected immutable request shape.",
            command.TypeId.Value);

    private static CommandHandlerResult Failure(
        string code,
        string message,
        string sourceIdentity) =>
        CommandHandlerResult.Failure(
        [
            new Diagnostic(code, DiagnosticSeverity.Error, message, sourceIdentity),
        ]);
}
