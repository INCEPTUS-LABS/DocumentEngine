using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

/// <summary>
/// Sets the optional descriptive Name of one Sequence Flow. Null removes the property;
/// a non-null string is preserved exactly, including an explicitly empty value.
/// </summary>
public sealed class UpdateBpmnSequenceFlowNameCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } = new("bpmn:command/update-sequence-flow-name");

    public UpdateBpmnSequenceFlowNameCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId relationshipId,
        string? targetName)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(relationshipId);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        RelationshipId = relationshipId;
        TargetName = targetName;
    }

    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId { get; }
    public DocumentRevision ExpectedRevision { get; }
    public SemanticElementId RelationshipId { get; }
    public string? TargetName { get; }
    public CommandCategory Category => CommandCategory.Semantic;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.SemanticModel;
    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation => CommandPipelineInvalidation.ConnectorOnly;
}
