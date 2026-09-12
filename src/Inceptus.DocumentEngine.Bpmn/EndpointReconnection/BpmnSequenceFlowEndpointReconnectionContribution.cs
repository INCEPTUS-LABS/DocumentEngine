using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.EndpointReconnection;

internal static class BpmnSequenceFlowEndpointReconnectionContribution
{
    internal static ConnectorEndpointReconnectionId SequenceFlowReconnectionId { get; } =
        new("bpmn:endpoint-reconnection/sequence-flow");

    internal static ImmutableArray<ConnectorEndpointReconnectionRegistration>
        Registrations
    { get; } =
    [
        new ConnectorEndpointReconnectionRegistration(
            SequenceFlowReconnectionId,
            new BpmnSequenceFlowEndpointReconnectionCommandFactory()),
    ];
}

internal sealed class BpmnSequenceFlowEndpointReconnectionCommandFactory :
    IConnectorEndpointReconnectionCommandFactory
{
    public bool CanStart(ConnectorEndpointReconnectionStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MatchesCurrentEndpoint(
            request.Document,
            request.RelationshipId,
            request.ConnectorVisualStateId,
            request.EndpointKind,
            request.CurrentSemanticElementId,
            request.CurrentAnchorId);
    }

    public ConnectorEndpointReconnectionPlanResult CreatePlan(
        ConnectorEndpointReconnectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new ReconnectBpmnSequenceFlowEndpointCommand(
            request.Document.DocumentId,
            request.ExpectedRevision,
            request.RelationshipId,
            request.ConnectorVisualStateId,
            request.EndpointKind,
            request.CurrentSemanticElementId,
            request.CurrentAnchorId,
            request.CandidateSemanticElementId,
            request.CandidateAnchorId);
        var diagnostics = BpmnSequenceFlowEndpointReconnectionValidation.Validate(
            command,
            request.Document).ToList();
        if (!CandidateVisualOwnsAnchor(request))
        {
            diagnostics.Add(BpmnDiagnostics.Error(
                BpmnCommandDiagnosticCodes.SequenceFlowAnchorBindingInvalid,
                $"Candidate visual state '{request.CandidateVisualStateId}' does not own connector anchor '{request.CandidateAnchorId}' for semantic element '{request.CandidateSemanticElementId}'.",
                request.RelationshipId.Value));
        }

        if (diagnostics.Any(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return ConnectorEndpointReconnectionPlanResult.Failure(diagnostics);
        }

        return ConnectorEndpointReconnectionPlanResult.Success(
            new ConnectorEndpointReconnectionPlan(
                command,
                request.RelationshipId,
                request.ConnectorVisualStateId),
            diagnostics);
    }

    private static bool MatchesCurrentEndpoint(
        DocumentSnapshot document,
        SemanticElementId relationshipId,
        VisualStateId connectorVisualStateId,
        ConnectorEndpointKind endpointKind,
        SemanticElementId currentSemanticElementId,
        ConnectorAnchorId currentAnchorId)
    {
        if (!document.SemanticModel.TryGetRelationship(relationshipId, out var relationship) ||
            relationship is null ||
            relationship.TypeId != BpmnSemanticTypes.SequenceFlow ||
            !document.VisualModel.TryGetVisualState(
                connectorVisualStateId,
                out var connectorVisual) ||
            connectorVisual is null ||
            connectorVisual.SemanticElementId != relationship.Id)
        {
            return false;
        }

        return endpointKind switch
        {
            ConnectorEndpointKind.Source =>
                relationship.SourceId == currentSemanticElementId &&
                connectorVisual.SourceAnchorId == currentAnchorId,
            ConnectorEndpointKind.Target =>
                relationship.TargetId == currentSemanticElementId &&
                connectorVisual.TargetAnchorId == currentAnchorId,
            _ => false,
        };
    }

    private static bool CandidateVisualOwnsAnchor(
        ConnectorEndpointReconnectionRequest request)
    {
        if (!request.Document.SemanticModel.TryGetElement(
                request.CandidateSemanticElementId,
                out var element) ||
            element is null ||
            !request.Document.VisualModel.TryGetVisualState(
                request.CandidateVisualStateId,
                out var visualState) ||
            visualState is null ||
            visualState.SemanticElementId != element.Id)
        {
            return false;
        }

        try
        {
            var expectedRole = request.EndpointKind == ConnectorEndpointKind.Source
                ? ConnectorAnchorRole.Source
                : ConnectorAnchorRole.Target;
            var isExactPersistentAnchor = visualState.ConnectorAnchors.Any(anchor =>
                anchor.Id == request.CandidateAnchorId &&
                anchor.Role == expectedRole);
            return isExactPersistentAnchor && ElementConnectorAnchorResolver.Resolve(
                    visualState,
                    element.TypeId,
                    BpmnConnectorAnchorPolicies.Provider)
                .Any(anchor =>
                    anchor.Id == request.CandidateAnchorId &&
                    anchor.Allows(expectedRole));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
