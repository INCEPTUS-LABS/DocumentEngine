namespace Inceptus.DocumentEngine.Bpmn.Validation;

public static class BpmnModelValidationCodes
{
    public const string ProcessElementContainmentInvalid =
        "BPMN_PROCESS_ELEMENT_CONTAINMENT_INVALID";

    public const string NoStartEvent = "BPMN_NO_START_EVENT";
    public const string NoEndEvent = "BPMN_NO_END_EVENT";
    public const string StartEventNoOutgoing = "BPMN_START_EVENT_NO_OUTGOING";
    public const string EndEventNoIncoming = "BPMN_END_EVENT_NO_INCOMING";
    public const string FlowNodeIsolated = "BPMN_FLOW_NODE_ISOLATED";
    public const string GatewayNoIncoming = "BPMN_GATEWAY_NO_INCOMING";
    public const string GatewayNoOutgoing = "BPMN_GATEWAY_NO_OUTGOING";
    public const string EventBasedGatewayIncomplete =
        "BPMN_EVENT_BASED_GATEWAY_INCOMPLETE";
    public const string EventBasedGatewayInvalidTarget =
        "BPMN_EVENT_BASED_GATEWAY_INVALID_TARGET";
    public const string EventBasedGatewayMixedMessageReceptionModes =
        "BPMN_EVENT_BASED_GATEWAY_MIXED_MESSAGE_RECEPTION_MODES";
    public const string EventBasedTargetAdditionalIncoming =
        "BPMN_EVENT_BASED_TARGET_ADDITIONAL_INCOMING";
    public const string SequenceFlowCrossesScope =
        "BPMN_SEQUENCE_FLOW_CROSSES_SCOPE";
    public const string BoundaryEventAttachmentMissing =
        "BPMN_BOUNDARY_EVENT_ATTACHMENT_MISSING";
    public const string BoundaryEventAttachmentTargetMissing =
        "BPMN_BOUNDARY_EVENT_ATTACHMENT_TARGET_MISSING";
    public const string BoundaryEventAttachmentOwnerInvalid =
        "BPMN_BOUNDARY_EVENT_ATTACHMENT_OWNER_INVALID";
    public const string BoundaryEventAttachmentScopeMismatch =
        "BPMN_BOUNDARY_EVENT_ATTACHMENT_SCOPE_MISMATCH";
    public const string BoundaryEventIncomingSequenceFlow =
        "BPMN_BOUNDARY_EVENT_INCOMING_SEQUENCE_FLOW";
    public const string SubProcessChildScopeMissing =
        "BPMN_SUBPROCESS_CHILD_SCOPE_MISSING";
    public const string ProcessScopeOwnerInvalid =
        "BPMN_PROCESS_SCOPE_OWNER_INVALID";
    public const string NodeUnreachableFromStart =
        "BPMN_NODE_UNREACHABLE_FROM_START";
    public const string NodeCannotReachEnd = "BPMN_NODE_CANNOT_REACH_END";
    public const string CollaborationContainmentInvalid =
        "BPMN_COLLABORATION_CONTAINMENT_INVALID";
    public const string CollaborationOwnershipContradictory =
        "BPMN_COLLABORATION_OWNERSHIP_CONTRADICTORY";
    public const string ParticipantContainmentInvalid =
        "BPMN_PARTICIPANT_CONTAINMENT_INVALID";
    public const string ParticipantOwnershipContradictory =
        "BPMN_PARTICIPANT_OWNERSHIP_CONTRADICTORY";
    public const string ParticipantCollaborationMissing =
        "BPMN_PARTICIPANT_COLLABORATION_MISSING";
    public const string ParticipantProcessScopeMissing =
        "BPMN_PARTICIPANT_PROCESS_SCOPE_MISSING";
    public const string ParticipantProcessScopeNested =
        "BPMN_PARTICIPANT_PROCESS_SCOPE_NESTED";
}
