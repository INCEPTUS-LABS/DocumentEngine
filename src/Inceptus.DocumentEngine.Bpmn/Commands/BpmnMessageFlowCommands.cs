using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

public sealed class CreateBpmnMessageFlowCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-message-flow");

    public CreateBpmnMessageFlowCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId relationshipId,
        VisualStateId visualStateId,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId,
        IEnumerable<PointD>? route = null,
        string? name = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(relationshipId);
        ArgumentNullException.ThrowIfNull(visualStateId);
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(targetId);
        ArgumentNullException.ThrowIfNull(sourceAnchorId);
        ArgumentNullException.ThrowIfNull(targetAnchorId);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        RelationshipId = relationshipId;
        VisualStateId = visualStateId;
        SourceId = sourceId;
        TargetId = targetId;
        SourceAnchorId = sourceAnchorId;
        TargetAnchorId = targetAnchorId;
        Route = route?.ToImmutableArray() ?? [];
        Name = name;
        Description = description;
    }

    public CreateBpmnMessageFlowCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId relationshipId,
        VisualStateId visualStateId,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId,
        ConnectorRoutingType routingType,
        IEnumerable<PointD>? route = null,
        string? name = null,
        string? description = null)
        : this(targetDocumentId, expectedRevision, relationshipId, visualStateId, sourceId,
            targetId, sourceAnchorId, targetAnchorId, route, name, description)
    {
        if (!Enum.IsDefined(routingType)) throw new ArgumentOutOfRangeException(nameof(routingType));
        RoutingType = routingType;
    }

    public ConnectorRoutingType RoutingType { get; } = ConnectorRoutingType.Automatic;

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

    public VisualStateId VisualStateId { get; }

    public SemanticElementId SourceId { get; }

    public SemanticElementId TargetId { get; }

    public ConnectorAnchorId SourceAnchorId { get; }

    public ConnectorAnchorId TargetAnchorId { get; }

    public ImmutableArray<PointD> Route { get; }

    public string? Name { get; }

    public string? Description { get; }
}

/// <summary>
/// Atomically creates a BPMN Message Flow and the persistent Target anchor required by it.
/// </summary>
public sealed class CreateBpmnMessageFlowWithTargetAnchorCommand :
    ICommand,
    ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/create-message-flow-with-target-anchor");

    public CreateBpmnMessageFlowWithTargetAnchorCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId relationshipId,
        VisualStateId visualStateId,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        ConnectorAnchorId sourceAnchorId,
        VisualStateId targetVisualStateId,
        ConnectorAnchorId targetAnchorId,
        ConnectorAnchorSide targetSide,
        int targetInsertionIndex,
        IEnumerable<PointD>? route = null,
        string? name = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(relationshipId);
        ArgumentNullException.ThrowIfNull(visualStateId);
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(targetId);
        ArgumentNullException.ThrowIfNull(sourceAnchorId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        ArgumentNullException.ThrowIfNull(targetAnchorId);
        if (!Enum.IsDefined(targetSide))
        {
            throw new ArgumentOutOfRangeException(nameof(targetSide));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(targetInsertionIndex);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        RelationshipId = relationshipId;
        VisualStateId = visualStateId;
        SourceId = sourceId;
        TargetId = targetId;
        SourceAnchorId = sourceAnchorId;
        TargetVisualStateId = targetVisualStateId;
        TargetAnchorId = targetAnchorId;
        TargetSide = targetSide;
        TargetInsertionIndex = targetInsertionIndex;
        Route = route?.ToImmutableArray() ?? [];
        Name = name;
        Description = description;
    }

    public CreateBpmnMessageFlowWithTargetAnchorCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        SemanticElementId relationshipId,
        VisualStateId visualStateId,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        ConnectorAnchorId sourceAnchorId,
        VisualStateId targetVisualStateId,
        ConnectorAnchorId targetAnchorId,
        ConnectorAnchorSide targetSide,
        int targetInsertionIndex,
        ConnectorRoutingType routingType,
        IEnumerable<PointD>? route = null,
        string? name = null,
        string? description = null)
        : this(targetDocumentId, expectedRevision, relationshipId, visualStateId, sourceId,
            targetId, sourceAnchorId, targetVisualStateId, targetAnchorId, targetSide,
            targetInsertionIndex, route, name, description)
    {
        if (!Enum.IsDefined(routingType)) throw new ArgumentOutOfRangeException(nameof(routingType));
        RoutingType = routingType;
    }

    public ConnectorRoutingType RoutingType { get; } = ConnectorRoutingType.Automatic;

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
    public VisualStateId VisualStateId { get; }
    public SemanticElementId SourceId { get; }
    public SemanticElementId TargetId { get; }
    public ConnectorAnchorId SourceAnchorId { get; }
    public VisualStateId TargetVisualStateId { get; }
    public ConnectorAnchorId TargetAnchorId { get; }
    public ConnectorAnchorSide TargetSide { get; }
    public int TargetInsertionIndex { get; }
    public ImmutableArray<PointD> Route { get; }
    public string? Name { get; }
    public string? Description { get; }
}

public sealed class ReconnectBpmnMessageFlowEndpointCommand :
    ICommand,
    ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/reconnect-message-flow-endpoint");

    public ReconnectBpmnMessageFlowEndpointCommand(
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

public sealed class DeleteBpmnMessageFlowCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("bpmn:command/delete-message-flow");

    public DeleteBpmnMessageFlowCommand(
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


public sealed class UpdateBpmnMessageFlowNameCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } = new("bpmn:command/update-message-flow-name");

    public UpdateBpmnMessageFlowNameCommand(
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
