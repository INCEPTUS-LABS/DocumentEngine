using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

/// <summary>
/// Sets a BPMN Event's optional Name. Null removes the property;
/// a non-null string is preserved exactly, including an explicitly empty value.
/// </summary>
public sealed class UpdateBpmnEventNameCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } = new("bpmn:command/update-event-name");

    public UpdateBpmnEventNameCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        string? targetName)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(elementId);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        ElementId = elementId;
        TargetName = targetName;
    }

    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId { get; }
    public DocumentRevision ExpectedRevision { get; }
    public SemanticElementId ElementId { get; }
    public string? TargetName { get; }
    public CommandCategory Category => CommandCategory.Semantic;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.SemanticModel;
    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        PipelineInvalidation.Projection | PipelineInvalidation.Scene;
}
