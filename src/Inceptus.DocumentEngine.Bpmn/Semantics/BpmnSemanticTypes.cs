using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Semantics;

public static class BpmnSemanticTypes
{
    public static SemanticTypeId Collaboration { get; } = new("BPMN.Collaboration");

    public static SemanticTypeId Participant { get; } = new("BPMN.Participant");

    public static SemanticTypeId StartEvent { get; } = new("BPMN.StartEvent");

    public static SemanticTypeId Task { get; } = new("BPMN.Task");

    public static SemanticTypeId UserTask { get; } = new("BPMN.UserTask");

    public static SemanticTypeId ManualTask { get; } = new("BPMN.ManualTask");

    public static SemanticTypeId ServiceTask { get; } = new("BPMN.ServiceTask");

    public static SemanticTypeId SendTask { get; } = new("BPMN.SendTask");

    public static SemanticTypeId ReceiveTask { get; } = new("BPMN.ReceiveTask");

    public static SemanticTypeId SubProcess { get; } = new("BPMN.SubProcess");

    public static SemanticTypeId ExclusiveGateway { get; } =
        new("BPMN.ExclusiveGateway");

    public static SemanticTypeId ParallelGateway { get; } =
        new("BPMN.ParallelGateway");

    public static SemanticTypeId InclusiveGateway { get; } =
        new("BPMN.InclusiveGateway");

    public static SemanticTypeId EventBasedGateway { get; } =
        new("BPMN.EventBasedGateway");

    public static SemanticTypeId MessageCatchEvent { get; } =
        new("BPMN.MessageCatchEvent");

    public static SemanticTypeId MessageThrowEvent { get; } =
        new("BPMN.MessageThrowEvent");

    public static SemanticTypeId TimerCatchEvent { get; } =
        new("BPMN.TimerCatchEvent");

    public static SemanticTypeId TimerBoundaryEvent { get; } =
        new("BPMN.TimerBoundaryEvent");

    public static SemanticTypeId MessageBoundaryEvent { get; } =
        new("BPMN.MessageBoundaryEvent");

    public static SemanticTypeId SignalBoundaryEvent { get; } =
        new("BPMN.SignalBoundaryEvent");

    public static SemanticTypeId SignalCatchEvent { get; } =
        new("BPMN.SignalCatchEvent");

    public static SemanticTypeId SignalThrowEvent { get; } =
        new("BPMN.SignalThrowEvent");

    public static SemanticTypeId EndEvent { get; } = new("BPMN.EndEvent");

    public static SemanticTypeId SequenceFlow { get; } = new("BPMN.SequenceFlow");

    public static bool IsFlowNode(SemanticTypeId typeId) =>
        IsEvent(typeId) ||
        BpmnActivitySemanticTypes.IsActivity(typeId) ||
        IsGateway(typeId);

    public static bool IsOrganizationalElement(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        return typeId == Collaboration || typeId == Participant;
    }

    public static bool IsEvent(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        return typeId == StartEvent ||
            BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(typeId) ||
            BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(typeId) ||
            typeId == EndEvent;
    }

    internal static bool IsGateway(SemanticTypeId typeId) =>
        typeId == ExclusiveGateway ||
        typeId == ParallelGateway ||
        typeId == InclusiveGateway ||
        typeId == EventBasedGateway;

    public static bool IsCatchEvent(SemanticTypeId typeId) =>
        BpmnIntermediateEventSemanticTypes.IsCatchEvent(typeId) ||
        BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(typeId);
}
