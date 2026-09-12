namespace Inceptus.DocumentEngine.Bpmn.Commands;

public static class BpmnCommandDiagnosticCodes
{
    public const string InvalidCommand = "BPMN_CMD_INVALID";
    public const string DuplicateSemanticId = "BPMN_SEMANTIC_ID_DUPLICATE";
    public const string DuplicateVisualId = "BPMN_VISUAL_ID_DUPLICATE";
    public const string ElementTargetScopeInvalid =
        "BPMN_ELEMENT_TARGET_SCOPE_INVALID";
    public const string InvalidTask = "BPMN_TASK_INVALID";
    public const string InvalidSubProcess = "BPMN_SUBPROCESS_INVALID";
    public const string SubProcessParentScopeInvalid =
        "BPMN_SUBPROCESS_PARENT_SCOPE_INVALID";
    public const string SubProcessChildScopeInvalid =
        "BPMN_SUBPROCESS_CHILD_SCOPE_INVALID";
    public const string InvalidExclusiveGateway = "BPMN_EXCLUSIVE_GATEWAY_INVALID";
    public const string InvalidParallelGateway = "BPMN_PARALLEL_GATEWAY_INVALID";
    public const string InvalidInclusiveGateway = "BPMN_INCLUSIVE_GATEWAY_INVALID";
    public const string InvalidEventBasedGateway = "BPMN_EVENT_BASED_GATEWAY_INVALID";
    public const string InvalidCatchEvent = "BPMN_CATCH_EVENT_INVALID";
    public const string InvalidIntermediateEvent = "BPMN_INTERMEDIATE_EVENT_INVALID";
    public const string InvalidBoundaryEvent = "BPMN_BOUNDARY_EVENT_INVALID";
    public const string BoundaryEventAttachmentOwnerInvalid =
        "BPMN_BOUNDARY_EVENT_ATTACHMENT_OWNER_INVALID";
    public const string BoundaryEventAttachmentScopeMismatch =
        "BPMN_BOUNDARY_EVENT_ATTACHMENT_SCOPE_MISMATCH";
    public const string BoundaryEventAttachmentGeometryInvalid =
        "BPMN_BOUNDARY_EVENT_ATTACHMENT_GEOMETRY_INVALID";
    public const string EndpointMissing = "BPMN_SEQUENCE_FLOW_ENDPOINT_MISSING";
    public const string EndpointTypeInvalid = "BPMN_SEQUENCE_FLOW_ENDPOINT_TYPE_INVALID";
    public const string SequenceFlowAnchorBindingInvalid =
        "BPMN_SEQUENCE_FLOW_ANCHOR_BINDING_INVALID";
    public const string SequenceFlowEndpointReconnectionInvalid =
        "BPMN_SEQUENCE_FLOW_ENDPOINT_RECONNECTION_INVALID";
    public const string SequenceFlowEndpointStateMismatch =
        "BPMN_SEQUENCE_FLOW_ENDPOINT_STATE_MISMATCH";
    public const string SequenceFlowEndpointReconnectionNoChange =
        "BPMN_SEQUENCE_FLOW_ENDPOINT_RECONNECTION_NO_CHANGE";
    public const string SequenceFlowDeletionInvalid =
        "BPMN_SEQUENCE_FLOW_DELETION_INVALID";
    public const string FlowNodeDeletionInvalid = "BPMN_FLOW_NODE_DELETION_INVALID";
    public const string IncomingStartEvent = "BPMN_SEQUENCE_FLOW_INCOMING_START_EVENT";
    public const string IncomingBoundaryEvent =
        "BPMN_SEQUENCE_FLOW_INCOMING_BOUNDARY_EVENT";
    public const string OutgoingEndEvent = "BPMN_SEQUENCE_FLOW_OUTGOING_END_EVENT";
    public const string SequenceFlowCrossesScope =
        "BPMN_SEQUENCE_FLOW_CROSSES_SCOPE";
    public const string EventBasedGatewayTargetInvalid =
        "BPMN_EVENT_BASED_GATEWAY_TARGET_INVALID";
    public const string EventBasedGatewayMixedMessageReceptionModes =
        "BPMN_EVENT_BASED_GATEWAY_MIXED_MESSAGE_RECEPTION_MODES";
    public const string EventBasedTargetAdditionalIncoming =
        "BPMN_EVENT_BASED_TARGET_ADDITIONAL_INCOMING";
    public const string HistoryInvalid = "BPMN_HISTORY_INVALID";
    public const string OrganizationalProfileUnavailable =
        "BPMN_ORGANIZATIONAL_PROFILE_UNAVAILABLE";
    public const string OrganizationalSemanticInvalid =
        "BPMN_ORGANIZATIONAL_SEMANTIC_INVALID";
    public const string OrganizationalSemanticUnchanged =
        "BPMN_ORGANIZATIONAL_SEMANTIC_UNCHANGED";
    public const string OrganizationalStructuralReferenceInvalid =
        "BPMN_ORGANIZATIONAL_STRUCTURAL_REFERENCE_INVALID";
    public const string ParticipantCollaborationInvalid =
        "BPMN_PARTICIPANT_COLLABORATION_INVALID";
    public const string ParticipantProcessScopeInvalid =
        "BPMN_PARTICIPANT_PROCESS_SCOPE_INVALID";
}
