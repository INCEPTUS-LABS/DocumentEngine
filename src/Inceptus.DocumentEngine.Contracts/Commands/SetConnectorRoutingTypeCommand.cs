using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>Changes only the routing mode. Current paths and priority are evaluated at commit.</summary>
public sealed record SetConnectorRoutingTypeCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } = new("inceptus:command/set-connector-routing-type");
    public SetConnectorRoutingTypeCommand(DocumentId targetDocumentId, DocumentRevision expectedRevision,
        VisualStateId targetVisualStateId, ConnectorRoutingType routingType)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        if (!Enum.IsDefined(routingType)) { throw new ArgumentOutOfRangeException(nameof(routingType)); }
        TargetDocumentId = targetDocumentId; ExpectedRevision = expectedRevision;
        TargetVisualStateId = targetVisualStateId; RoutingType = routingType;
    }
    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId { get; }
    public DocumentRevision ExpectedRevision { get; }
    public CommandCategory Category => CommandCategory.Visual;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.VisualModel;
    public PipelineInvalidation PipelineInvalidation => CommandPipelineInvalidation.ConnectorOnly;
    public VisualStateId TargetVisualStateId { get; }
    public ConnectorRoutingType RoutingType { get; }
}
