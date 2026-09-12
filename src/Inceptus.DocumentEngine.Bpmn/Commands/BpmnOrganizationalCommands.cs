using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

public abstract class BpmnOrganizationalCommand : ICommand, ICommandPipelineInvalidation
{
    protected BpmnOrganizationalCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
    }

    public abstract CommandTypeId TypeId { get; }

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Semantic;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel;

    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        PipelineInvalidation.Scene;
}

public sealed class CreateBpmnCollaborationCommand : BpmnOrganizationalCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-collaboration");

    public CreateBpmnCollaborationCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId collaborationId,
        string? name = null,
        string? description = null)
        : base(targetDocumentId, expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(collaborationId);
        CollaborationId = collaborationId;
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public SemanticElementId CollaborationId { get; }

    public string? Name { get; }

    public string? Description { get; }
}

public sealed class CreateBpmnParticipantCommand : BpmnOrganizationalCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-participant");

    public CreateBpmnParticipantCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId participantId,
        SemanticElementId collaborationId,
        DocumentScopeId? processScopeId = null,
        string? name = null,
        string? description = null)
        : base(targetDocumentId, expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(participantId);
        ArgumentNullException.ThrowIfNull(collaborationId);
        ParticipantId = participantId;
        CollaborationId = collaborationId;
        ProcessScopeId = processScopeId;
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public SemanticElementId ParticipantId { get; }

    public SemanticElementId CollaborationId { get; }

    public DocumentScopeId? ProcessScopeId { get; }

    public string? Name { get; }

    public string? Description { get; }
}

public sealed class UpdateBpmnCollaborationCommand : BpmnOrganizationalCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/update-collaboration");

    public UpdateBpmnCollaborationCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId collaborationId,
        string? name = null,
        string? description = null)
        : base(targetDocumentId, expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(collaborationId);
        CollaborationId = collaborationId;
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public SemanticElementId CollaborationId { get; }

    public string? Name { get; }

    public string? Description { get; }
}

public sealed class UpdateBpmnParticipantCommand : BpmnOrganizationalCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/update-participant");

    public UpdateBpmnParticipantCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId participantId,
        SemanticElementId collaborationId,
        DocumentScopeId? processScopeId = null,
        string? name = null,
        string? description = null)
        : base(targetDocumentId, expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(participantId);
        ArgumentNullException.ThrowIfNull(collaborationId);
        ParticipantId = participantId;
        CollaborationId = collaborationId;
        ProcessScopeId = processScopeId;
        Name = name;
        Description = description;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public SemanticElementId ParticipantId { get; }

    public SemanticElementId CollaborationId { get; }

    public DocumentScopeId? ProcessScopeId { get; }

    public string? Name { get; }

    public string? Description { get; }
}

public sealed class DeleteBpmnParticipantCommand : BpmnOrganizationalCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/delete-participant");

    public DeleteBpmnParticipantCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId participantId)
        : base(targetDocumentId, expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(participantId);
        ParticipantId = participantId;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public SemanticElementId ParticipantId { get; }
}

public sealed class DeleteBpmnCollaborationCommand : BpmnOrganizationalCommand
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/delete-collaboration");

    public DeleteBpmnCollaborationCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId collaborationId)
        : base(targetDocumentId, expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(collaborationId);
        CollaborationId = collaborationId;
    }

    public override CommandTypeId TypeId => KnownTypeId;

    public SemanticElementId CollaborationId { get; }
}
