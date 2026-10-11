using Microsoft.Extensions.Localization;

namespace Inceptus.DocumentEngine.Bpmn.Blazor;

/// <summary>Localizes modeler diagnostics at presentation time; diagnostic contracts stay unchanged.</summary>
internal static class ModelerDiagnosticLabels
{
    internal static string Message(IStringLocalizer<ModelerStrings> text, string code, string fallback) =>
        ResourceKey(code) is { } key ? text[key].Value : fallback;

    internal static string? ResourceKey(string code) => code switch
    {
        "BPMN_AUTO_ARRANGE_FAILED" => "Diagnostic_AutoArrangeFailed",
        "BPMN_ELEMENT_TOOLBOX_FAILED" or "BPMN_SMART_CREATE_STALE" or "BPMN_SMART_CREATE_HISTORY_CONFLICT" => "ElementToolbox_Failed",
        "BPMN_AUTO_ARRANGE_STALE" => "Diagnostic_AutoArrangeStale",
        "BPMN_ELEMENT_FLOW_SOURCE_INVALID" => "Diagnostic_FlowSourceInvalid",
        "BPMN_ELEMENT_FLOW_TARGET_INVALID" => "Diagnostic_FlowTargetInvalid",
        "BPMN_ELEMENT_FLOW_POINT_PLAN_INVALID" => "Diagnostic_FlowPointPlanInvalid",
        "CANVAS2D_ELEMENT_CONNECTION_NO_ROUTE" => "Diagnostic_FlowNoRoute",
        "BPMN_COLLABORATION_VIEW_INVALID" => "Diagnostic_CollaborationInvalid",
        "BPMN_PARTICIPANT_VISUAL_MISSING" => "Diagnostic_ParticipantVisualMissing",
        "BPMN_PARTICIPANT_OWNERSHIP_INVALID" => "Diagnostic_ParticipantOwnershipInvalid",
        "BPMN_PARTICIPANT_OWNERSHIP_MISSING" => "Diagnostic_ParticipantOwnershipMissing",
        "BPMN_PARTICIPANT_OWNERSHIP_AMBIGUOUS" => "Diagnostic_ParticipantOwnershipAmbiguous",
        "PUBLISH_DOCUMENT_PRESENTATION_UNSUPPORTED" => "Diagnostic_CollaborationPublicationUnsupported",
        "PRESENTATION_CONTEXT_INVALID" => "Diagnostic_PresentationContextInvalid",
        "BPMN_MESSAGE_FLOW_ENDPOINT_MISSING" => "Diagnostic_MessageFlowEndpointMissing",
        "BPMN_MESSAGE_FLOW_SOURCE_INVALID" => "Diagnostic_MessageFlowSourceInvalid",
        "BPMN_MESSAGE_FLOW_TARGET_INVALID" => "Diagnostic_MessageFlowTargetInvalid",
        "BPMN_MESSAGE_FLOW_SAME_PARTICIPANT" => "Diagnostic_MessageFlowSameParticipant",
        "BPMN_MESSAGE_FLOW_DIFFERENT_COLLABORATIONS" => "Diagnostic_MessageFlowDifferentCollaborations",
        "BPMN_MESSAGE_FLOW_BOUNDARY_ATTACHMENT_INVALID" => "Diagnostic_MessageFlowBoundaryAttachmentInvalid",
        "BPMN_MESSAGE_FLOW_PRESENTATION_UNAVAILABLE" => "Diagnostic_MessageFlowPresentationUnavailable",
        "BPMN_MESSAGE_FLOW_ANCHOR_INVALID" => "Diagnostic_MessageFlowAnchorInvalid",
        "BPMN_MESSAGE_FLOW_STATE_MISMATCH" => "Diagnostic_MessageFlowStateMismatch",
        "BPMN_MESSAGE_FLOW_COMMAND_INVALID" => "Diagnostic_MessageFlowCommandInvalid",
        "BPMN_MESSAGE_FLOW_TYPE_CHANGE_INVALID" => "Diagnostic_MessageFlowTypeChangeInvalid",
        _ => null,
    };
}
