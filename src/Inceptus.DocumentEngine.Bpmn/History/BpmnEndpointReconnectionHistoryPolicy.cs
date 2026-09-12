using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.History;

internal sealed class BpmnSequenceFlowEndpointReconnectionHistoryPolicy :
    ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);
        if (command is not ReconnectBpmnSequenceFlowEndpointCommand reconnect ||
            !before.SemanticModel.TryGetRelationship(
                reconnect.RelationshipId,
                out var beforeRelationship) ||
            beforeRelationship is null ||
            !committed.SemanticModel.TryGetRelationship(
                reconnect.RelationshipId,
                out var committedRelationship) ||
            committedRelationship is null ||
            !before.VisualModel.TryGetVisualState(
                reconnect.ConnectorVisualStateId,
                out var beforeVisual) ||
            beforeVisual is null ||
            !committed.VisualModel.TryGetVisualState(
                reconnect.ConnectorVisualStateId,
                out var committedVisual) ||
            committedVisual is null)
        {
            return Failure(command);
        }

        var beforeSemanticElementId = reconnect.EndpointKind == ConnectorEndpointKind.Source
            ? beforeRelationship.SourceId
            : beforeRelationship.TargetId;
        var committedSemanticElementId = reconnect.EndpointKind == ConnectorEndpointKind.Source
            ? committedRelationship.SourceId
            : committedRelationship.TargetId;
        var beforeAnchorId = reconnect.EndpointKind == ConnectorEndpointKind.Source
            ? beforeVisual.SourceAnchorId
            : beforeVisual.TargetAnchorId;
        var committedAnchorId = reconnect.EndpointKind == ConnectorEndpointKind.Source
            ? committedVisual.SourceAnchorId
            : committedVisual.TargetAnchorId;
        if (beforeSemanticElementId != reconnect.ExpectedCurrentSemanticElementId ||
            beforeAnchorId != reconnect.ExpectedCurrentAnchorId ||
            committedSemanticElementId != reconnect.NewSemanticElementId ||
            committedAnchorId != reconnect.NewAnchorId ||
            !ContainsOnlyRequestedEndpointChange(
                before,
                committed,
                reconnect,
                beforeRelationship,
                beforeVisual))
        {
            return Failure(command);
        }

        return CommandHistoryPreparationResult.Undoable(
            new BpmnSequenceFlowEndpointReconnectionHistoryCommandFactory(
                reconnect.RelationshipId,
                reconnect.ConnectorVisualStateId,
                reconnect.EndpointKind,
                reconnect.NewSemanticElementId,
                reconnect.NewAnchorId,
                reconnect.ExpectedCurrentSemanticElementId,
                reconnect.ExpectedCurrentAnchorId),
            new BpmnSequenceFlowEndpointReconnectionHistoryCommandFactory(
                reconnect.RelationshipId,
                reconnect.ConnectorVisualStateId,
                reconnect.EndpointKind,
                reconnect.ExpectedCurrentSemanticElementId,
                reconnect.ExpectedCurrentAnchorId,
                reconnect.NewSemanticElementId,
                reconnect.NewAnchorId));
    }

    private static bool ContainsOnlyRequestedEndpointChange(
        DocumentSnapshot before,
        DocumentSnapshot committed,
        ReconnectBpmnSequenceFlowEndpointCommand reconnect,
        SemanticRelationshipSnapshot beforeRelationship,
        VisualStateSnapshot beforeVisual)
    {
        var expectedRelationship = new SemanticRelationshipSnapshot(
            beforeRelationship.Id,
            beforeRelationship.TypeId,
            reconnect.EndpointKind == ConnectorEndpointKind.Source
                ? reconnect.NewSemanticElementId
                : beforeRelationship.SourceId,
            reconnect.EndpointKind == ConnectorEndpointKind.Target
                ? reconnect.NewSemanticElementId
                : beforeRelationship.TargetId,
            beforeRelationship.Properties);
        var expectedVisual = new VisualStateSnapshot(
            beforeVisual.Id,
            beforeVisual.SemanticElementId,
            beforeVisual.Position,
            beforeVisual.Size,
            beforeVisual.PlacementMode,
            beforeVisual.Route,
            beforeVisual.Properties,
            beforeVisual.ConnectorAnchors,
            reconnect.EndpointKind == ConnectorEndpointKind.Source
                ? reconnect.NewAnchorId
                : beforeVisual.SourceAnchorId,
            reconnect.EndpointKind == ConnectorEndpointKind.Target
                ? reconnect.NewAnchorId
                : beforeVisual.TargetAnchorId);
        var expectedRelationships = before.SemanticModel.Relationships.Select(candidate =>
            candidate.Id == expectedRelationship.Id ? expectedRelationship : candidate);
        var expectedVisuals = before.VisualModel.VisualStates.Select(candidate =>
            candidate.Id == expectedVisual.Id ? expectedVisual : candidate);

        return before.DocumentId == committed.DocumentId &&
            before.SemanticModel.Elements.AsSpan().SequenceEqual(
                committed.SemanticModel.Elements.AsSpan()) &&
            expectedRelationships.SequenceEqual(committed.SemanticModel.Relationships) &&
            before.SemanticModel.NestedScopes.AsSpan().SequenceEqual(
                committed.SemanticModel.NestedScopes.AsSpan()) &&
            before.SemanticModel.ScopeMemberships.AsSpan().SequenceEqual(
                committed.SemanticModel.ScopeMemberships.AsSpan()) &&
            before.SemanticModel.ModelProfiles.Equals(
                committed.SemanticModel.ModelProfiles) &&
            BpmnSequenceFlowDeletionHistoryPolicy.ProfileRecordsMatch(before, committed) &&
            expectedVisuals.SequenceEqual(committed.VisualModel.VisualStates) &&
            before.Metadata.SystemManagedProperties.Equals(
                committed.Metadata.SystemManagedProperties) &&
            before.Metadata.ExtensionProperties.Equals(committed.Metadata.ExtensionProperties);
    }

    private static CommandHistoryPreparationResult Failure(ICommand command) =>
        CommandHistoryPreparationResult.Failure(
        [
            new Diagnostic(
                BpmnCommandDiagnosticCodes.HistoryInvalid,
                DiagnosticSeverity.Error,
                "BPMN Sequence Flow endpoint-reconnection History requires exactly the requested semantic endpoint and connector-anchor update.",
                command.TypeId.Value),
        ]);
}

internal sealed class BpmnSequenceFlowEndpointReconnectionHistoryCommandFactory :
    IHistoryCommandFactory
{
    private readonly SemanticElementId _relationshipId;
    private readonly VisualStateId _connectorVisualStateId;
    private readonly ConnectorEndpointKind _endpointKind;
    private readonly SemanticElementId _expectedCurrentSemanticElementId;
    private readonly ConnectorAnchorId _expectedCurrentAnchorId;
    private readonly SemanticElementId _newSemanticElementId;
    private readonly ConnectorAnchorId _newAnchorId;

    internal BpmnSequenceFlowEndpointReconnectionHistoryCommandFactory(
        SemanticElementId relationshipId,
        VisualStateId connectorVisualStateId,
        ConnectorEndpointKind endpointKind,
        SemanticElementId expectedCurrentSemanticElementId,
        ConnectorAnchorId expectedCurrentAnchorId,
        SemanticElementId newSemanticElementId,
        ConnectorAnchorId newAnchorId)
    {
        ArgumentNullException.ThrowIfNull(relationshipId);
        ArgumentNullException.ThrowIfNull(connectorVisualStateId);
        ArgumentNullException.ThrowIfNull(expectedCurrentSemanticElementId);
        ArgumentNullException.ThrowIfNull(expectedCurrentAnchorId);
        ArgumentNullException.ThrowIfNull(newSemanticElementId);
        ArgumentNullException.ThrowIfNull(newAnchorId);
        if (!Enum.IsDefined(endpointKind))
        {
            throw new ArgumentOutOfRangeException(nameof(endpointKind));
        }

        _relationshipId = relationshipId;
        _connectorVisualStateId = connectorVisualStateId;
        _endpointKind = endpointKind;
        _expectedCurrentSemanticElementId = expectedCurrentSemanticElementId;
        _expectedCurrentAnchorId = expectedCurrentAnchorId;
        _newSemanticElementId = newSemanticElementId;
        _newAnchorId = newAnchorId;
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new ReconnectBpmnSequenceFlowEndpointCommand(
            documentId,
            expectedRevision,
            _relationshipId,
            _connectorVisualStateId,
            _endpointKind,
            _expectedCurrentSemanticElementId,
            _expectedCurrentAnchorId,
            _newSemanticElementId,
            _newAnchorId);
}
