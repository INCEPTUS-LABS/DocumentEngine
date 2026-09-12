using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

/// <summary>
/// Atomically deletes one existing BPMN Sequence Flow and its connector Visual State.
/// Endpoint anchors are deliberately not part of this Command.
/// </summary>
public sealed class DeleteBpmnSequenceFlowCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/delete-sequence-flow");

    public DeleteBpmnSequenceFlowCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId relationshipId,
        VisualStateId connectorVisualStateId,
        SemanticElementId expectedSourceId,
        SemanticElementId expectedTargetId,
        ConnectorAnchorId expectedSourceAnchorId,
        ConnectorAnchorId expectedTargetAnchorId)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(relationshipId);
        ArgumentNullException.ThrowIfNull(connectorVisualStateId);
        ArgumentNullException.ThrowIfNull(expectedSourceId);
        ArgumentNullException.ThrowIfNull(expectedTargetId);
        ArgumentNullException.ThrowIfNull(expectedSourceAnchorId);
        ArgumentNullException.ThrowIfNull(expectedTargetAnchorId);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        RelationshipId = relationshipId;
        ConnectorVisualStateId = connectorVisualStateId;
        ExpectedSourceId = expectedSourceId;
        ExpectedTargetId = expectedTargetId;
        ExpectedSourceAnchorId = expectedSourceAnchorId;
        ExpectedTargetAnchorId = expectedTargetAnchorId;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Document;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel |
        AuthoritativeDocumentComponent.VisualModel;

    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        CommandPipelineInvalidation.WithoutNodeLayout;

    public SemanticElementId RelationshipId { get; }

    public VisualStateId ConnectorVisualStateId { get; }

    public SemanticElementId ExpectedSourceId { get; }

    public SemanticElementId ExpectedTargetId { get; }

    public ConnectorAnchorId ExpectedSourceAnchorId { get; }

    public ConnectorAnchorId ExpectedTargetAnchorId { get; }
}

/// <summary>
/// Atomically deletes one BPMN flow node, all of its Visual State and anchor data,
/// and every incident BPMN Sequence Flow with its connector Visual State. For a
/// BPMN SubProcess, the same command recursively removes its owned scope subtree.
/// </summary>
public sealed class DeleteBpmnFlowNodeCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/delete-flow-node");

    public DeleteBpmnFlowNodeCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId elementId,
        VisualStateId visualStateId)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(elementId);
        ArgumentNullException.ThrowIfNull(visualStateId);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        ElementId = elementId;
        VisualStateId = visualStateId;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Document;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel |
        AuthoritativeDocumentComponent.VisualModel;

    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        CommandPipelineInvalidation.WithoutNodeLayout;

    public SemanticElementId ElementId { get; }

    public VisualStateId VisualStateId { get; }
}

internal sealed class RestoreBpmnDeletionCommand : ICommand, ICommandPipelineInvalidation
{
    internal static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/restore-deletion-snapshot");

    internal RestoreBpmnDeletionCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticModelSnapshot semanticModel,
        VisualModelSnapshot visualModel,
        NodeGeometryPipelineImpact nodeGeometryImpact)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(semanticModel);
        ArgumentNullException.ThrowIfNull(visualModel);
        ArgumentNullException.ThrowIfNull(nodeGeometryImpact);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        SemanticModel = semanticModel;
        VisualModel = visualModel;
        NodeGeometryImpact = nodeGeometryImpact;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Document;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel |
        AuthoritativeDocumentComponent.VisualModel;

    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        CommandPipelineInvalidation.WithoutNodeLayout;

    internal SemanticModelSnapshot SemanticModel { get; }

    internal VisualModelSnapshot VisualModel { get; }

    internal NodeGeometryPipelineImpact NodeGeometryImpact { get; }
}
