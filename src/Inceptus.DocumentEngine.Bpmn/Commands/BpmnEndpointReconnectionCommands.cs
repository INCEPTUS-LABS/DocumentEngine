using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

/// <summary>
/// Atomically updates one existing BPMN Sequence Flow semantic endpoint and its
/// corresponding persistent connector-anchor reference.
/// </summary>
public sealed class ReconnectBpmnSequenceFlowEndpointCommand :
    ICommand,
    ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/reconnect-sequence-flow-endpoint");

    public ReconnectBpmnSequenceFlowEndpointCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId relationshipId,
        VisualStateId connectorVisualStateId,
        ConnectorEndpointKind endpointKind,
        SemanticElementId expectedCurrentSemanticElementId,
        ConnectorAnchorId expectedCurrentAnchorId,
        SemanticElementId newSemanticElementId,
        ConnectorAnchorId newAnchorId)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(relationshipId);
        ArgumentNullException.ThrowIfNull(connectorVisualStateId);
        ArgumentNullException.ThrowIfNull(expectedCurrentSemanticElementId);
        ArgumentNullException.ThrowIfNull(expectedCurrentAnchorId);
        ArgumentNullException.ThrowIfNull(newSemanticElementId);
        ArgumentNullException.ThrowIfNull(newAnchorId);
        if (!Enum.IsDefined(endpointKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(endpointKind),
                endpointKind,
                "The connector endpoint kind must be defined.");
        }

        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        RelationshipId = relationshipId;
        ConnectorVisualStateId = connectorVisualStateId;
        EndpointKind = endpointKind;
        ExpectedCurrentSemanticElementId = expectedCurrentSemanticElementId;
        ExpectedCurrentAnchorId = expectedCurrentAnchorId;
        NewSemanticElementId = newSemanticElementId;
        NewAnchorId = newAnchorId;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Document;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel |
        AuthoritativeDocumentComponent.VisualModel;

    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        CommandPipelineInvalidation.ConnectorOnly;

    public SemanticElementId RelationshipId { get; }

    public VisualStateId ConnectorVisualStateId { get; }

    public ConnectorEndpointKind EndpointKind { get; }

    public SemanticElementId ExpectedCurrentSemanticElementId { get; }

    public ConnectorAnchorId ExpectedCurrentAnchorId { get; }

    public SemanticElementId NewSemanticElementId { get; }

    public ConnectorAnchorId NewAnchorId { get; }
}
