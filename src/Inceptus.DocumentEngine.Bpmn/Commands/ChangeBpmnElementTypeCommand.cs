using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

/// <summary>Changes one existing element's concrete TypeId within its BPMN switch group.</summary>
public sealed class ChangeBpmnElementTypeCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } = new("bpmn:command/change-element-type");

    public ChangeBpmnElementTypeCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        SemanticTypeId targetSemanticTypeId)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(elementId);
        ArgumentNullException.ThrowIfNull(targetSemanticTypeId);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        ElementId = elementId;
        TargetSemanticTypeId = targetSemanticTypeId;
    }

    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId { get; }
    public DocumentRevision ExpectedRevision { get; }
    public SemanticElementId ElementId { get; }
    public SemanticTypeId TargetSemanticTypeId { get; }
    public CommandCategory Category => CommandCategory.Semantic;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.SemanticModel;
    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        PipelineInvalidation.Projection | PipelineInvalidation.Scene;
}
