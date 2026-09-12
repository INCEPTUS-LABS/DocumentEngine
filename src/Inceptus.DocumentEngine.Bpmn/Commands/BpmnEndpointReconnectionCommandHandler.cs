using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

internal sealed class BpmnSequenceFlowEndpointReconnectionCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(
        ICommand command,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not ReconnectBpmnSequenceFlowEndpointCommand reconnect)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidCommand,
                    "The BPMN Sequence Flow endpoint-reconnection request has an invalid shape.",
                    command.TypeId.Value),
            ]));
        }

        var diagnostics = BpmnSequenceFlowEndpointReconnectionValidation.Validate(
            reconnect,
            document);
        if (!diagnostics.IsEmpty)
        {
            return ValueTask.FromResult(CommandHandlerResult.Failure(diagnostics));
        }

        _ = document.SemanticModel.TryGetRelationship(
            reconnect.RelationshipId,
            out var relationship);
        _ = document.VisualModel.TryGetVisualState(
            reconnect.ConnectorVisualStateId,
            out var connectorVisual);

        var relationshipReplacement = new SemanticRelationshipSnapshot(
            relationship!.Id,
            relationship.TypeId,
            reconnect.EndpointKind == ConnectorEndpointKind.Source
                ? reconnect.NewSemanticElementId
                : relationship.SourceId,
            reconnect.EndpointKind == ConnectorEndpointKind.Target
                ? reconnect.NewSemanticElementId
                : relationship.TargetId,
            relationship.Properties);
        var visualReplacement = new VisualStateSnapshot(
            connectorVisual!.Id,
            connectorVisual.SemanticElementId,
            connectorVisual.Position,
            connectorVisual.Size,
            connectorVisual.PlacementMode,
            connectorVisual.Route,
            connectorVisual.Properties,
            connectorVisual.ConnectorAnchors,
            reconnect.EndpointKind == ConnectorEndpointKind.Source
                ? reconnect.NewAnchorId
                : connectorVisual.SourceAnchorId,
            reconnect.EndpointKind == ConnectorEndpointKind.Target
                ? reconnect.NewAnchorId
                : connectorVisual.TargetAnchorId);
        var semanticModel = new SemanticModelSnapshot(
            document.DocumentId,
            document.Revision,
            document.SemanticModel.Elements,
            document.SemanticModel.Relationships.Select(candidate =>
                candidate.Id == relationshipReplacement.Id
                    ? relationshipReplacement
                    : candidate),
            document.SemanticModel.NestedScopes,
            document.SemanticModel.ScopeMemberships,
            document.SemanticModel.ModelProfiles,
            document.SemanticModel.ProfileAssignments);
        var visualModel = new VisualModelSnapshot(
            document.DocumentId,
            document.Revision,
            document.VisualModel.VisualStates.Select(candidate =>
                candidate.Id == visualReplacement.Id
                    ? visualReplacement
                    : candidate),
            document.VisualModel.ProfileElementPresentations);

        return ValueTask.FromResult(CommandHandlerResult.Success(
            BpmnDocumentReplacement.Replace(document, semanticModel, visualModel)));
    }
}
