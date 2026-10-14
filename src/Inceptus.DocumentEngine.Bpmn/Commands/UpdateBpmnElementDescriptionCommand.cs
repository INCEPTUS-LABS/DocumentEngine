using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

/// <summary>
/// Sets an activity or gateway's optional Description. Null removes the property;
/// a non-null string is preserved exactly, including an explicitly empty value.
/// </summary>
public sealed class UpdateBpmnElementDescriptionCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } = new("bpmn:command/update-element-description");

    public UpdateBpmnElementDescriptionCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        string? targetDescription)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(elementId);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        ElementId = elementId;
        TargetDescription = targetDescription;
    }

    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId { get; }
    public DocumentRevision ExpectedRevision { get; }
    public SemanticElementId ElementId { get; }
    public string? TargetDescription { get; }
    public CommandCategory Category => CommandCategory.Semantic;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.SemanticModel;
    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        PipelineInvalidation.Projection | PipelineInvalidation.Scene;
}
