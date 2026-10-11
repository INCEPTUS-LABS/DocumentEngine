using Inceptus.DocumentEngine.Bpmn.Projection;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Presentation;

internal static class BpmnParticipantPresentationContribution
{
    internal static ProjectionRuleId ProjectionRuleId { get; } = new("bpmn:projection/participant");
    internal static ProjectionRuleRegistration ProjectionRegistration { get; } = new(ProjectionRuleId,
        ProjectionSourceKind.SemanticElement, BpmnSemanticTypes.Participant,
        new BpmnParticipantGroupProjectionRule());
    internal static PresentationViewProviderRegistration ViewRegistration { get; } =
        new(BpmnSemanticTypes.Collaboration, new BpmnCollaborationViewProvider());
    internal static ElementConnectorAnchorPolicyRegistration AnchorRegistration { get; } =
        new(BpmnSemanticTypes.Participant, new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.DynamicUnlimited(ConnectorAnchorRoleCapability.SourceOrTarget),
            EdgeConnectorAnchorPolicy.DynamicUnlimited(ConnectorAnchorRoleCapability.SourceOrTarget),
            EdgeConnectorAnchorPolicy.DynamicUnlimited(ConnectorAnchorRoleCapability.SourceOrTarget),
            EdgeConnectorAnchorPolicy.DynamicUnlimited(ConnectorAnchorRoleCapability.SourceOrTarget)));
}
