using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.EndpointReconnection;

internal static class BpmnMessageFlowEndpointReconnectionContribution
{
    internal static ConnectorEndpointReconnectionRegistration Registration { get; } = Create();

    private static ConnectorEndpointReconnectionRegistration Create()
    {
        var factory = new BpmnMessageFlowEndpointReconnectionCommandFactory();
        return new(new ConnectorEndpointReconnectionId("bpmn:endpoint-reconnection/message-flow"), factory, factory);
    }
}

internal sealed class BpmnMessageFlowEndpointReconnectionCommandFactory :
    IConnectorEndpointReconnectionCommandFactory, IConnectorEndpointReconnectionCandidateEligibility
{
    public bool CanStart(ConnectorEndpointReconnectionStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.PresentationPlan is { } plan && plan.RelationshipIds.Contains(request.RelationshipId) &&
            BpmnEffectiveParticipant.TryResolve(request.Document, request.CurrentSemanticElementId, out _, out _) &&
            request.Document.SemanticModel.TryGetRelationship(request.RelationshipId, out var relationship) &&
            relationship?.TypeId == BpmnSemanticTypes.MessageFlow &&
            request.Document.VisualModel.TryGetVisualState(request.ConnectorVisualStateId, out var visual) &&
            visual?.SemanticElementId == relationship.Id &&
            (request.EndpointKind == ConnectorEndpointKind.Source
                ? relationship.SourceId == request.CurrentSemanticElementId && visual.SourceAnchorId == request.CurrentAnchorId
                : relationship.TargetId == request.CurrentSemanticElementId && visual.TargetAnchorId == request.CurrentAnchorId);
    }

    public bool IsEligible(ConnectorEndpointReconnectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CanStart(new ConnectorEndpointReconnectionStartRequest(request.PresentationContext, request.PresentationPlan,
                request.Document, request.ExpectedRevision, request.RelationshipId, request.ConnectorVisualStateId,
                request.EndpointKind, request.CurrentSemanticElementId, request.CurrentAnchorId)) &&
            request.PresentationPlan!.ElementIds.Contains(request.CandidateSemanticElementId) &&
            request.Document.SemanticModel.TryGetRelationship(request.RelationshipId, out var relationship) &&
            relationship?.TypeId == BpmnSemanticTypes.MessageFlow &&
            !BpmnMessageFlowSemantics.ValidateEndpoints(request.Document,
                request.EndpointKind == ConnectorEndpointKind.Source ? request.CandidateSemanticElementId : relationship.SourceId,
                request.EndpointKind == ConnectorEndpointKind.Target ? request.CandidateSemanticElementId : relationship.TargetId)
                .Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    public ConnectorEndpointReconnectionPlanResult CreatePlan(ConnectorEndpointReconnectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = new ReconnectBpmnMessageFlowEndpointCommand(request.Document.DocumentId, request.ExpectedRevision,
            request.RelationshipId, request.ConnectorVisualStateId, request.EndpointKind, request.CurrentSemanticElementId,
            request.CurrentAnchorId, request.CandidateSemanticElementId, request.CandidateAnchorId);
        var diagnostics = BpmnMessageFlowEndpointReconnectionValidation.Validate(command, request.Document).ToBuilder();
        if (!IsEligible(request))
            diagnostics.Add(BpmnMessageFlowSemantics.Error(BpmnMessageFlowSemantics.PresentationUnavailable,
                "The Message Flow endpoint does not belong to the selected Collaboration view.", request.RelationshipId));
        if (!request.Document.VisualModel.TryGetVisualState(request.CandidateVisualStateId, out var visual) ||
            visual?.SemanticElementId != request.CandidateSemanticElementId ||
            !visual.ConnectorAnchors.Any(anchor => anchor.Id == request.CandidateAnchorId))
            diagnostics.Add(BpmnMessageFlowSemantics.Error(BpmnMessageFlowSemantics.StateMismatch,
                "The proposed visual does not own the Message Flow endpoint anchor.", request.RelationshipId));
        return diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? ConnectorEndpointReconnectionPlanResult.Failure(diagnostics)
            : ConnectorEndpointReconnectionPlanResult.Success(new ConnectorEndpointReconnectionPlan(command,
                request.RelationshipId, request.ConnectorVisualStateId), diagnostics);
    }
}
