using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Organizational.History;

internal sealed class OrganizationalHistoryPolicy<TCommand> : ICommandHistoryPolicy
    where TCommand : OrganizationalCommand
{
    private readonly IOrganizationalElementEligibilityPolicy _eligibilityPolicy;

    internal OrganizationalHistoryPolicy(
        IOrganizationalElementEligibilityPolicy eligibilityPolicy)
    {
        ArgumentNullException.ThrowIfNull(eligibilityPolicy);
        _eligibilityPolicy = eligibilityPolicy;
    }

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);
        if (command is not TCommand ||
            !OrganizationalCommandSupport.TryApply(
                command,
                before,
                _eligibilityPolicy,
                out var expected,
                out _) ||
            expected is null ||
            !ContentMatches(expected, committed))
        {
            return CommandHistoryPreparationResult.Failure(
            [
                OrganizationalCommandSupport.Error(
                    OrganizationalCommandDiagnosticCodes.HistoryInvalid,
                    "Organizational History requires the exact proposed semantic and presentation mutation.",
                    command.TypeId.Value),
            ]);
        }

        return CommandHistoryPreparationResult.Undoable(
            new OrganizationalSnapshotRestoreCommandFactory(
                before.SemanticModel,
                before.VisualModel),
            new OrganizationalSnapshotRestoreCommandFactory(
                committed.SemanticModel,
                committed.VisualModel));
    }

    private static bool ContentMatches(
        DocumentSnapshot expected,
        DocumentSnapshot committed) =>
        expected.DocumentId == committed.DocumentId &&
        expected.SemanticModel.Elements.AsSpan().SequenceEqual(
            committed.SemanticModel.Elements.AsSpan()) &&
        expected.SemanticModel.Relationships.AsSpan().SequenceEqual(
            committed.SemanticModel.Relationships.AsSpan()) &&
        expected.SemanticModel.NestedScopes.AsSpan().SequenceEqual(
            committed.SemanticModel.NestedScopes.AsSpan()) &&
        expected.SemanticModel.ScopeMemberships.AsSpan().SequenceEqual(
            committed.SemanticModel.ScopeMemberships.AsSpan()) &&
        expected.SemanticModel.ModelProfiles.Equals(
            committed.SemanticModel.ModelProfiles) &&
        expected.SemanticModel.ProfileAssignments.AsSpan().SequenceEqual(
            committed.SemanticModel.ProfileAssignments.AsSpan()) &&
        expected.VisualModel.VisualStates.AsSpan().SequenceEqual(
            committed.VisualModel.VisualStates.AsSpan()) &&
        expected.VisualModel.ProfileElementPresentations.AsSpan().SequenceEqual(
            committed.VisualModel.ProfileElementPresentations.AsSpan()) &&
        expected.Metadata.SystemManagedProperties.Equals(
            committed.Metadata.SystemManagedProperties) &&
        expected.Metadata.ExtensionProperties.Equals(
            committed.Metadata.ExtensionProperties);
}

internal sealed class OrganizationalSnapshotRestoreCommandFactory : IHistoryCommandFactory
{
    private readonly SemanticModelSnapshot _semanticModel;
    private readonly VisualModelSnapshot _visualModel;

    internal OrganizationalSnapshotRestoreCommandFactory(
        SemanticModelSnapshot semanticModel,
        VisualModelSnapshot visualModel)
    {
        ArgumentNullException.ThrowIfNull(semanticModel);
        ArgumentNullException.ThrowIfNull(visualModel);
        _semanticModel = semanticModel;
        _visualModel = visualModel;
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new RestoreOrganizationalSnapshotCommand(
            documentId,
            expectedRevision,
            new SemanticModelSnapshot(
                documentId,
                expectedRevision,
                _semanticModel.Elements,
                _semanticModel.Relationships,
                _semanticModel.NestedScopes,
                _semanticModel.ScopeMemberships,
                _semanticModel.ModelProfiles,
                _semanticModel.ProfileAssignments),
            new VisualModelSnapshot(
                documentId,
                expectedRevision,
                _visualModel.VisualStates,
                _visualModel.ProfileElementPresentations));
}

internal sealed class RestoreOrganizationalSnapshotCommand : ICommand,
    ICommandPipelineInvalidation
{
    internal static CommandTypeId KnownTypeId { get; } =
        new("inceptus:organizational/command/restore-snapshot");

    internal RestoreOrganizationalSnapshotCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticModelSnapshot semanticModel,
        VisualModelSnapshot visualModel)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(semanticModel);
        ArgumentNullException.ThrowIfNull(visualModel);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        SemanticModel = semanticModel;
        VisualModel = visualModel;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Document;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel |
        AuthoritativeDocumentComponent.VisualModel;

    public PipelineInvalidation PipelineInvalidation => PipelineInvalidation.Scene;

    internal SemanticModelSnapshot SemanticModel { get; }

    internal VisualModelSnapshot VisualModel { get; }
}

internal sealed class RestoreOrganizationalSnapshotCommandHandler :
    ICommandHandler,
    ICommandEnvelopeValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command is RestoreOrganizationalSnapshotCommand restore &&
            restore.SemanticModel.DocumentId == command.TargetDocumentId &&
            restore.SemanticModel.Revision == command.ExpectedRevision &&
            restore.VisualModel.DocumentId == command.TargetDocumentId &&
            restore.VisualModel.Revision == command.ExpectedRevision
            ? []
            : [OrganizationalCommandSupport.Error(
                OrganizationalCommandDiagnosticCodes.HistoryInvalid,
                "The Organizational History restore envelope is invalid.",
                command.TypeId.Value)];
    }

    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not RestoreOrganizationalSnapshotCommand restore)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(Validate(command)));
        }

        return ValueTask.FromResult(CommandHandlerResult.Success(
            new DocumentSnapshot(
                restore.SemanticModel,
                restore.VisualModel,
                document.Metadata,
                document.Publication),
            pipelineInvalidation: PipelineInvalidation.Scene));
    }
}
