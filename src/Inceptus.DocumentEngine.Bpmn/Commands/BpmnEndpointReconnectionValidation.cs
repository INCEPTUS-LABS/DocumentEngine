using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

internal sealed class BpmnSequenceFlowEndpointReconnectionValidator : ICommandValidator
{
    public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        if (command is not ReconnectBpmnSequenceFlowEndpointCommand reconnect)
        {
            return
            [
                BpmnDiagnostics.Error(
                    BpmnCommandDiagnosticCodes.InvalidCommand,
                    "The BPMN Sequence Flow endpoint-reconnection request has an invalid shape.",
                    command.TypeId.Value),
            ];
        }

        return BpmnSequenceFlowEndpointReconnectionValidation.Validate(reconnect, document);
    }
}

internal static class BpmnSequenceFlowEndpointReconnectionValidation
{
    internal static ImmutableArray<Diagnostic> Validate(
        ReconnectBpmnSequenceFlowEndpointCommand reconnect,
        DocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(reconnect);
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<Diagnostic>();
        if (!document.SemanticModel.TryGetRelationship(
                reconnect.RelationshipId,
                out var relationship) ||
            relationship is null ||
            relationship.TypeId != BpmnSemanticTypes.SequenceFlow)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.SequenceFlowEndpointReconnectionInvalid,
                $"Semantic relationship '{reconnect.RelationshipId}' is not an existing BPMN Sequence Flow.",
                reconnect.RelationshipId.Value));
            return [.. diagnostics];
        }

        if (!document.VisualModel.TryGetVisualState(
                reconnect.ConnectorVisualStateId,
                out var connectorVisual) ||
            connectorVisual is null ||
            connectorVisual.SemanticElementId != relationship.Id)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.SequenceFlowEndpointReconnectionInvalid,
                $"Visual state '{reconnect.ConnectorVisualStateId}' does not represent BPMN Sequence Flow '{relationship.Id}'.",
                reconnect.RelationshipId.Value));
            return [.. diagnostics];
        }

        var currentSemanticElementId = reconnect.EndpointKind == ConnectorEndpointKind.Source
            ? relationship.SourceId
            : relationship.TargetId;
        var currentAnchorId = reconnect.EndpointKind == ConnectorEndpointKind.Source
            ? connectorVisual.SourceAnchorId
            : connectorVisual.TargetAnchorId;
        if (currentSemanticElementId != reconnect.ExpectedCurrentSemanticElementId ||
            currentAnchorId != reconnect.ExpectedCurrentAnchorId)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.SequenceFlowEndpointStateMismatch,
                $"BPMN Sequence Flow '{relationship.Id}' no longer has the expected {reconnect.EndpointKind} endpoint state.",
                relationship.Id.Value));
        }

        if (currentSemanticElementId == reconnect.NewSemanticElementId &&
            currentAnchorId == reconnect.NewAnchorId)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.SequenceFlowEndpointReconnectionNoChange,
                $"BPMN Sequence Flow '{relationship.Id}' already uses the requested {reconnect.EndpointKind} endpoint.",
                relationship.Id.Value));
        }

        if (!document.SemanticModel.TryGetElement(
                reconnect.NewSemanticElementId,
                out var newEndpoint) ||
            newEndpoint is null)
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.EndpointMissing,
                "A BPMN Sequence Flow endpoint reconnection requires an existing endpoint element.",
                relationship.Id.Value));
            return [.. diagnostics];
        }

        var resultingSourceId = reconnect.EndpointKind == ConnectorEndpointKind.Source
            ? reconnect.NewSemanticElementId
            : relationship.SourceId;
        var resultingTargetId = reconnect.EndpointKind == ConnectorEndpointKind.Target
            ? reconnect.NewSemanticElementId
            : relationship.TargetId;
        diagnostics.AddRange(BpmnSequenceFlowCreationValidation.ValidateEndpointSemantics(
            document,
            relationship.Id,
            resultingSourceId,
            resultingTargetId,
            replacedRelationshipId: relationship.Id));

        ValidateAnchorBinding(
            reconnect,
            document,
            reconnect.EndpointKind == ConnectorEndpointKind.Source
                ? ConnectorAnchorRole.Source
                : ConnectorAnchorRole.Target,
            diagnostics);

        return [.. diagnostics];
    }

    private static void ValidateAnchorBinding(
        ReconnectBpmnSequenceFlowEndpointCommand reconnect,
        DocumentSnapshot document,
        ConnectorAnchorRole expectedRole,
        List<Diagnostic> diagnostics)
    {
        var matches = new List<(VisualStateSnapshot VisualState, ResolvedConnectorAnchor Anchor)>();
        var policyInvalid = false;
        foreach (var visualState in document.VisualModel.VisualStates)
        {
            if (!document.SemanticModel.TryGetElement(
                    visualState.SemanticElementId,
                    out var element) ||
                element is null)
            {
                continue;
            }

            try
            {
                matches.AddRange(ElementConnectorAnchorResolver.Resolve(
                        visualState,
                        element.TypeId,
                        BpmnConnectorAnchorPolicies.Provider)
                    .Where(anchor => anchor.Id == reconnect.NewAnchorId)
                    .Select(anchor => (visualState, anchor)));
            }
            catch (InvalidOperationException)
            {
                policyInvalid |= visualState.ConnectorAnchors.Any(anchor =>
                    anchor.Id == reconnect.NewAnchorId);
            }
        }

        if (policyInvalid || matches.Count != 1)
        {
            diagnostics.Add(InvalidAnchor(
                reconnect,
                expectedRole,
                "does not resolve uniquely under the active BPMN connector-anchor policy"));
            return;
        }

        var match = matches[0];
        var persistentAnchor = match.VisualState.ConnectorAnchors.SingleOrDefault(anchor =>
            anchor.Id == reconnect.NewAnchorId);
        if (persistentAnchor is null)
        {
            diagnostics.Add(InvalidAnchor(
                reconnect,
                expectedRole,
                "is not an existing persistent ConnectorAnchor"));
        }
        else if (persistentAnchor.Role != expectedRole ||
            !match.Anchor.Allows(expectedRole))
        {
            diagnostics.Add(InvalidAnchor(
                reconnect,
                expectedRole,
                $"does not allow the required {expectedRole} endpoint role"));
        }

        if (match.VisualState.SemanticElementId != reconnect.NewSemanticElementId)
        {
            diagnostics.Add(InvalidAnchor(
                reconnect,
                expectedRole,
                $"is not owned by the endpoint semantic element '{reconnect.NewSemanticElementId}'"));
        }

        if (ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
                document.VisualModel,
                reconnect.NewAnchorId,
                reconnect.ConnectorVisualStateId,
                reconnect.EndpointKind))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                CommandExecutionDiagnosticCodes.ConnectorAnchorInUse,
                $"Connector anchor '{reconnect.NewAnchorId}' is already referenced by another connector endpoint.",
                reconnect.RelationshipId.Value));
        }
    }

    private static Diagnostic InvalidAnchor(
        ReconnectBpmnSequenceFlowEndpointCommand reconnect,
        ConnectorAnchorRole expectedRole,
        string detail) =>
        BpmnDiagnostics.Error(
            BpmnCommandDiagnosticCodes.SequenceFlowAnchorBindingInvalid,
            $"BPMN Sequence Flow {expectedRole} anchor '{reconnect.NewAnchorId}' {detail}.",
            reconnect.RelationshipId.Value);
}
