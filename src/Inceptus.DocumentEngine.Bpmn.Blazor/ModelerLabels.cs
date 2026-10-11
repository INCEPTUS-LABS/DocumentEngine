using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Microsoft.Extensions.Localization;

namespace Inceptus.DocumentEngine.Bpmn.Blazor;

// Presentation adapters for the modeler's built-in definitions. Definitions and their IDs
// remain authoritative; translated labels are never written back into a catalog or Document.
internal static class ModelerLabels
{
    internal static string? ValidationTargetName(DocumentSnapshot document, SemanticElementId id)
    {
        if (!document.SemanticModel.TryGetRelationship(id, out var relationship) ||
            relationship?.TypeId != BpmnSemanticTypes.MessageFlow ||
            !relationship.Properties.TryGetValue(BpmnSemanticProperties.Name, out var name) ||
            name.Kind != PropertyValueKind.Text || string.IsNullOrWhiteSpace(name.TextValue)) return null;
        return name.TextValue;
    }

    internal static string ScopeActionLabel(
        IStringLocalizer<ModelerStrings> text, SemanticTypeId typeId, string fallback) =>
        typeId == BpmnSemanticTypes.SubProcess ? text["Context_OpenSubProcess"] : fallback;

    internal static string? ScopeFallbackKey(SemanticElementSnapshot owner) =>
        owner.TypeId == BpmnSemanticTypes.SubProcess &&
        !HasText(owner, BpmnSemanticProperties.Name) && !HasText(owner, BpmnSemanticProperties.Code)
            ? "Element_SubProcess"
            : null;

    private static bool HasText(SemanticElementSnapshot owner, string key) =>
        owner.Properties.TryGetValue(key, out var value) &&
        value.Kind == PropertyValueKind.Text && !string.IsNullOrWhiteSpace(value.TextValue);

    internal static string ToolboxLabel(IStringLocalizer<ModelerStrings> text, ToolboxItemDefinition item) =>
        Resolve(text, item.DisplayName switch
        {
            "Catch Event" => "Toolbox_CatchEvent",
            "Throw Event" => "Toolbox_ThrowEvent",
            "Boundary Event" => "Toolbox_BoundaryEvent",
            "Gateway" => "Toolbox_Gateway",
            _ => ToolboxKey(item.ItemId.Value),
        }, item.DisplayName);

    internal static string SemanticTypeLabel(IStringLocalizer<ModelerStrings> text, SemanticTypeId typeId) =>
        Resolve(text, typeId.Value.StartsWith("BPMN.", StringComparison.Ordinal)
            ? string.Concat("Element_", typeId.Value[5..]) : null,
            BpmnElementTypeSwitching.GetDisplayName(typeId));

    internal static string ToolboxLabel(IStringLocalizer<ModelerStrings> text, ToolboxGroupDefinition group) =>
        Resolve(text, ToolboxKey(group.GroupId.Value), group.DisplayName);

    internal static string ToolboxLabel(IStringLocalizer<ModelerStrings> text, ToolboxConnectionItemDefinition item) =>
        Resolve(text, ToolboxKey(item.ItemId.Value), item.DisplayName);

    internal static string ToolboxLabel(IStringLocalizer<ModelerStrings> text, ToolboxSectionDefinition section) =>
        Resolve(text, ToolboxKey(section.SectionId.Value), section.DisplayName);

    internal static string? ToolboxKey(string id) => id switch
    {
        "bpmn:toolbox:bpmn" or "bpmn:toolbox:flow-elements" => "Toolbox_Bpmn",
        "bpmn:toolbox:events" => "Toolbox_Events",
        "bpmn:toolbox:activities" => "Toolbox_Activities",
        "bpmn:toolbox:tasks" => "Toolbox_Tasks",
        "bpmn:toolbox:gateways" => "Toolbox_Gateways",
        "bpmn:toolbox:flow" => "Toolbox_Flow",
        "bpmn:toolbox:sequence-flow" => "Element_SequenceFlow",
        "bpmn:toolbox:message-flow" => "Element_MessageFlow",
        "bpmn:toolbox:start-event" => "Element_StartEvent",
        "bpmn:toolbox:end-event" => "Element_EndEvent",
        "bpmn:toolbox:message-catch-event" => "Element_MessageCatchEvent",
        "bpmn:toolbox:message-throw-event" => "Element_MessageThrowEvent",
        "bpmn:toolbox:timer-catch-event" => "Element_TimerCatchEvent",
        "bpmn:toolbox:signal-catch-event" => "Element_SignalCatchEvent",
        "bpmn:toolbox:signal-throw-event" => "Element_SignalThrowEvent",
        "bpmn:toolbox:timer-boundary-event" => "Element_TimerBoundaryEvent",
        "bpmn:toolbox:message-boundary-event" => "Element_MessageBoundaryEvent",
        "bpmn:toolbox:signal-boundary-event" => "Element_SignalBoundaryEvent",
        "bpmn:toolbox:task" => "Element_Task",
        "bpmn:toolbox:user-task" => "Element_UserTask",
        "bpmn:toolbox:manual-task" => "Element_ManualTask",
        "bpmn:toolbox:service-task" => "Element_ServiceTask",
        "bpmn:toolbox:send-task" => "Element_SendTask",
        "bpmn:toolbox:receive-task" => "Element_ReceiveTask",
        "bpmn:toolbox:sub-process" => "Element_SubProcess",
        "bpmn:toolbox:exclusive-gateway" => "Element_ExclusiveGateway",
        "bpmn:toolbox:parallel-gateway" => "Element_ParallelGateway",
        "bpmn:toolbox:inclusive-gateway" => "Element_InclusiveGateway",
        "bpmn:toolbox:event-based-gateway" => "Element_EventBasedGateway",
        _ => null,
    };

    internal static string PropertyFieldLabel(
        IStringLocalizer<ModelerStrings> text, ElementPropertyFieldDefinition definition) =>
        Resolve(text, definition.SemanticPropertyKey switch
        {
            BpmnSemanticProperties.Name or OrganizationalSemanticProperties.Name => "Field_Name",
            BpmnSemanticProperties.Description or OrganizationalSemanticProperties.Description => "Field_Description",
            BpmnSemanticProperties.Code => "Field_Code",
            BpmnSemanticProperties.ElementNumber => "Field_ElementNumber",
            BpmnSemanticProperties.TimerDefinition => "Field_TimerDefinition",
            BpmnSemanticProperties.CancelActivity => "Field_Interrupting",
            OrganizationalSemanticProperties.ParticipantId => "Field_Participant",
            _ => null,
        }, definition.DisplayName);

    internal static string ProfileLabel(IStringLocalizer<ModelerStrings> text, ModelProfileDefinition profile) =>
        Resolve(text, profile.Id.Value switch
        {
            "bpmn:profile:organizational" => "Profile_Organizational",
            "bpmn:profile:stage" => "Profile_Stage",
            _ => null,
        }, profile.DisplayName);

    internal static string? ActionKey(string id) => id switch
    {
        "bpmn:background-action/add-process" => "Context_Process",
        "inceptus:organizational/background-action/add-pool" => "Context_Pool",
        "inceptus:organizational/scene-view-action/expand-pool" => "Context_Expand",
        "inceptus:organizational/scene-view-action/collapse-pool" => "Context_Collapse",
        "inceptus:organizational/scene-command-action/move-pool-up" => "Context_MoveUp",
        "inceptus:organizational/scene-command-action/move-pool-down" => "Context_MoveDown",
        "inceptus:organizational/scene-command-action/move-lane-up" => "Context_MoveLaneUp",
        "inceptus:organizational/scene-command-action/move-lane-down" => "Context_MoveLaneDown",
        _ => null,
    };

    internal static string ActionLabel(IStringLocalizer<ModelerStrings> text, string id, string fallback) =>
        Resolve(text, ActionKey(id), fallback);

    private static string Resolve(IStringLocalizer<ModelerStrings> text, string? key, string fallback) =>
        key is null ? fallback : text[key];
}
