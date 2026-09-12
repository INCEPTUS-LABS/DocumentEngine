using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.Bpmn.Toolbox;

internal static class BpmnToolboxContribution
{
    internal static ToolboxGroupId GroupId { get; } = new("bpmn:toolbox:flow-elements");

    internal static ToolboxSectionId SectionId { get; } = new("bpmn:toolbox:bpmn");

    internal static ToolboxGroupId EventsGroupId { get; } = new("bpmn:toolbox:events");

    internal static ToolboxGroupId TasksGroupId { get; } = new("bpmn:toolbox:tasks");

    internal static ToolboxGroupId ActivitiesGroupId { get; } =
        new("bpmn:toolbox:activities");

    internal static ToolboxGroupId GatewaysGroupId { get; } = new("bpmn:toolbox:gateways");

    internal static ToolboxItemId StartEventItemId { get; } =
        new("bpmn:toolbox:start-event");

    internal static ToolboxItemId TaskItemId { get; } = new("bpmn:toolbox:task");

    internal static ToolboxItemId UserTaskItemId { get; } =
        new("bpmn:toolbox:user-task");

    internal static ToolboxItemId ManualTaskItemId { get; } =
        new("bpmn:toolbox:manual-task");

    internal static ToolboxItemId ServiceTaskItemId { get; } =
        new("bpmn:toolbox:service-task");

    internal static ToolboxItemId SendTaskItemId { get; } =
        new("bpmn:toolbox:send-task");

    internal static ToolboxItemId ReceiveTaskItemId { get; } =
        new("bpmn:toolbox:receive-task");

    internal static ToolboxItemId SubProcessItemId { get; } =
        new("bpmn:toolbox:sub-process");

    internal static ToolboxItemId ExclusiveGatewayItemId { get; } =
        new("bpmn:toolbox:exclusive-gateway");

    internal static ToolboxItemId ParallelGatewayItemId { get; } =
        new("bpmn:toolbox:parallel-gateway");

    internal static ToolboxItemId InclusiveGatewayItemId { get; } =
        new("bpmn:toolbox:inclusive-gateway");

    internal static ToolboxItemId EventBasedGatewayItemId { get; } =
        new("bpmn:toolbox:event-based-gateway");

    internal static ToolboxItemId MessageCatchEventItemId { get; } =
        new("bpmn:toolbox:message-catch-event");

    internal static ToolboxItemId MessageThrowEventItemId { get; } =
        new("bpmn:toolbox:message-throw-event");

    internal static ToolboxItemId MessageBoundaryEventItemId { get; } =
        new("bpmn:toolbox:message-boundary-event");

    internal static ToolboxItemId TimerCatchEventItemId { get; } =
        new("bpmn:toolbox:timer-catch-event");

    internal static ToolboxItemId TimerBoundaryEventItemId { get; } =
        new("bpmn:toolbox:timer-boundary-event");

    internal static ToolboxItemId SignalCatchEventItemId { get; } =
        new("bpmn:toolbox:signal-catch-event");

    internal static ToolboxItemId SignalThrowEventItemId { get; } =
        new("bpmn:toolbox:signal-throw-event");

    internal static ToolboxItemId SignalBoundaryEventItemId { get; } =
        new("bpmn:toolbox:signal-boundary-event");

    internal static ToolboxItemId EndEventItemId { get; } = new("bpmn:toolbox:end-event");

    internal static ToolboxContribution Definition { get; } = new(
        groups:
        [
            new ToolboxGroupDefinition(GroupId, "BPMN", 0),
        ],
        items:
        [
            new ToolboxItemDefinition(
                StartEventItemId,
                BpmnSemanticTypes.StartEvent,
                GroupId,
                "Start Event",
                0,
                new ToolboxIconDescriptor("bpmn:start-event", "○")),
            new ToolboxItemDefinition(
                TaskItemId,
                BpmnSemanticTypes.Task,
                GroupId,
                "Task",
                1,
                new ToolboxIconDescriptor("bpmn:task", "▭")),
            new ToolboxItemDefinition(
                EndEventItemId,
                BpmnSemanticTypes.EndEvent,
                GroupId,
                "End Event",
                2,
                new ToolboxIconDescriptor("bpmn:end-event", "◎")),
        ]);

    internal static ToolboxContribution M32Definition { get; } = new(
        groups:
        [
            new ToolboxGroupDefinition(GroupId, "BPMN", 0),
        ],
        items:
        [
            new ToolboxItemDefinition(
                StartEventItemId,
                BpmnSemanticTypes.StartEvent,
                GroupId,
                "Start Event",
                0,
                new ToolboxIconDescriptor("bpmn:start-event", "○")),
            new ToolboxItemDefinition(
                TaskItemId,
                BpmnSemanticTypes.Task,
                GroupId,
                "Task",
                1,
                new ToolboxIconDescriptor("bpmn:task", "▭")),
            new ToolboxItemDefinition(
                ExclusiveGatewayItemId,
                BpmnSemanticTypes.ExclusiveGateway,
                GroupId,
                "Exclusive Gateway",
                2,
                new ToolboxIconDescriptor("bpmn:exclusive-gateway", "◇")),
            new ToolboxItemDefinition(
                EndEventItemId,
                BpmnSemanticTypes.EndEvent,
                GroupId,
                "End Event",
                3,
                new ToolboxIconDescriptor("bpmn:end-event", "◎")),
        ]);

    internal static ToolboxContribution M33Definition { get; } = new(
        groups:
        [
            new ToolboxGroupDefinition(GroupId, "BPMN", 0),
        ],
        items:
        [
            new ToolboxItemDefinition(
                StartEventItemId,
                BpmnSemanticTypes.StartEvent,
                GroupId,
                "Start Event",
                0,
                new ToolboxIconDescriptor("bpmn:start-event", "○")),
            new ToolboxItemDefinition(
                TaskItemId,
                BpmnSemanticTypes.Task,
                GroupId,
                "Task",
                1,
                new ToolboxIconDescriptor("bpmn:task", "▭")),
            new ToolboxItemDefinition(
                ExclusiveGatewayItemId,
                BpmnSemanticTypes.ExclusiveGateway,
                GroupId,
                "Exclusive Gateway",
                2,
                new ToolboxIconDescriptor("bpmn:exclusive-gateway", "◇")),
            new ToolboxItemDefinition(
                ParallelGatewayItemId,
                BpmnSemanticTypes.ParallelGateway,
                GroupId,
                "Parallel Gateway",
                3,
                new ToolboxIconDescriptor("bpmn:parallel-gateway", "◇+")),
            new ToolboxItemDefinition(
                EndEventItemId,
                BpmnSemanticTypes.EndEvent,
                GroupId,
                "End Event",
                4,
                new ToolboxIconDescriptor("bpmn:end-event", "◎")),
        ]);

    internal static ToolboxContribution M34Definition { get; } = new(
        groups:
        [
            new ToolboxGroupDefinition(GroupId, "BPMN", 0),
        ],
        items:
        [
            new ToolboxItemDefinition(
                StartEventItemId,
                BpmnSemanticTypes.StartEvent,
                GroupId,
                "Start Event",
                0,
                new ToolboxIconDescriptor("bpmn:start-event", "○")),
            new ToolboxItemDefinition(
                TaskItemId,
                BpmnSemanticTypes.Task,
                GroupId,
                "Task",
                1,
                new ToolboxIconDescriptor("bpmn:task", "▭")),
            new ToolboxItemDefinition(
                ExclusiveGatewayItemId,
                BpmnSemanticTypes.ExclusiveGateway,
                GroupId,
                "Exclusive Gateway",
                2,
                new ToolboxIconDescriptor("bpmn:exclusive-gateway", "◇")),
            new ToolboxItemDefinition(
                ParallelGatewayItemId,
                BpmnSemanticTypes.ParallelGateway,
                GroupId,
                "Parallel Gateway",
                3,
                new ToolboxIconDescriptor("bpmn:parallel-gateway", "◇+")),
            new ToolboxItemDefinition(
                InclusiveGatewayItemId,
                BpmnSemanticTypes.InclusiveGateway,
                GroupId,
                "Inclusive Gateway",
                4,
                new ToolboxIconDescriptor("bpmn:inclusive-gateway", "◇O")),
            new ToolboxItemDefinition(
                EndEventItemId,
                BpmnSemanticTypes.EndEvent,
                GroupId,
                "End Event",
                5,
                new ToolboxIconDescriptor("bpmn:end-event", "◎")),
        ]);

    internal static ToolboxContribution N4Definition { get; } = new(
        groups:
        [
            new ToolboxGroupDefinition(GroupId, "BPMN", 0),
        ],
        items:
        [
            new ToolboxItemDefinition(
                StartEventItemId,
                BpmnSemanticTypes.StartEvent,
                GroupId,
                "Start Event",
                0,
                new ToolboxIconDescriptor("bpmn:start-event", "○")),
            new ToolboxItemDefinition(
                MessageCatchEventItemId,
                BpmnSemanticTypes.MessageCatchEvent,
                GroupId,
                "Message Catch Event",
                1,
                new ToolboxIconDescriptor("bpmn:message-catch-event", "◎▱")),
            new ToolboxItemDefinition(
                TimerCatchEventItemId,
                BpmnSemanticTypes.TimerCatchEvent,
                GroupId,
                "Timer Catch Event",
                2,
                new ToolboxIconDescriptor("bpmn:timer-catch-event", "◎◷")),
            new ToolboxItemDefinition(
                EndEventItemId,
                BpmnSemanticTypes.EndEvent,
                GroupId,
                "End Event",
                3,
                new ToolboxIconDescriptor("bpmn:end-event", "◎")),
            new ToolboxItemDefinition(
                TaskItemId,
                BpmnSemanticTypes.Task,
                GroupId,
                "Task",
                4,
                new ToolboxIconDescriptor("bpmn:task", "▭")),
            new ToolboxItemDefinition(
                ExclusiveGatewayItemId,
                BpmnSemanticTypes.ExclusiveGateway,
                GroupId,
                "Exclusive Gateway",
                5,
                new ToolboxIconDescriptor("bpmn:exclusive-gateway", "◇")),
            new ToolboxItemDefinition(
                ParallelGatewayItemId,
                BpmnSemanticTypes.ParallelGateway,
                GroupId,
                "Parallel Gateway",
                6,
                new ToolboxIconDescriptor("bpmn:parallel-gateway", "◇+")),
            new ToolboxItemDefinition(
                InclusiveGatewayItemId,
                BpmnSemanticTypes.InclusiveGateway,
                GroupId,
                "Inclusive Gateway",
                7,
                new ToolboxIconDescriptor("bpmn:inclusive-gateway", "◇O")),
            new ToolboxItemDefinition(
                EventBasedGatewayItemId,
                BpmnSemanticTypes.EventBasedGateway,
                GroupId,
                "Event-Based Gateway",
                8,
                new ToolboxIconDescriptor("bpmn:event-based-gateway", "◇◎")),
        ]);

    internal static ToolboxContribution N6Definition { get; } = new(
        groups:
        [
            new ToolboxGroupDefinition(GroupId, "BPMN", 0),
        ],
        items:
        [
            new ToolboxItemDefinition(
                StartEventItemId,
                BpmnSemanticTypes.StartEvent,
                GroupId,
                "Start Event",
                0,
                new ToolboxIconDescriptor("bpmn:start-event", "○")),
            new ToolboxItemDefinition(
                MessageCatchEventItemId,
                BpmnSemanticTypes.MessageCatchEvent,
                GroupId,
                "Message Catch Event",
                1,
                new ToolboxIconDescriptor("bpmn:message-catch-event", "◎▱")),
            new ToolboxItemDefinition(
                TimerCatchEventItemId,
                BpmnSemanticTypes.TimerCatchEvent,
                GroupId,
                "Timer Catch Event",
                2,
                new ToolboxIconDescriptor("bpmn:timer-catch-event", "◎◷")),
            new ToolboxItemDefinition(
                EndEventItemId,
                BpmnSemanticTypes.EndEvent,
                GroupId,
                "End Event",
                3,
                new ToolboxIconDescriptor("bpmn:end-event", "◎")),
            new ToolboxItemDefinition(
                TaskItemId,
                BpmnSemanticTypes.Task,
                GroupId,
                "Task",
                4,
                new ToolboxIconDescriptor("bpmn:task", "▭")),
            new ToolboxItemDefinition(
                UserTaskItemId,
                BpmnSemanticTypes.UserTask,
                GroupId,
                "User Task",
                5,
                new ToolboxIconDescriptor("bpmn:user-task", "▭U")),
            new ToolboxItemDefinition(
                ManualTaskItemId,
                BpmnSemanticTypes.ManualTask,
                GroupId,
                "Manual Task",
                6,
                new ToolboxIconDescriptor("bpmn:manual-task", "▭M")),
            new ToolboxItemDefinition(
                ServiceTaskItemId,
                BpmnSemanticTypes.ServiceTask,
                GroupId,
                "Service Task",
                7,
                new ToolboxIconDescriptor("bpmn:service-task", "▭⚙")),
            new ToolboxItemDefinition(
                SendTaskItemId,
                BpmnSemanticTypes.SendTask,
                GroupId,
                "Send Task",
                8,
                new ToolboxIconDescriptor("bpmn:send-task", "▭▶")),
            new ToolboxItemDefinition(
                ReceiveTaskItemId,
                BpmnSemanticTypes.ReceiveTask,
                GroupId,
                "Receive Task",
                9,
                new ToolboxIconDescriptor("bpmn:receive-task", "▭▷")),
            new ToolboxItemDefinition(
                ExclusiveGatewayItemId,
                BpmnSemanticTypes.ExclusiveGateway,
                GroupId,
                "Exclusive Gateway",
                10,
                new ToolboxIconDescriptor("bpmn:exclusive-gateway", "◇")),
            new ToolboxItemDefinition(
                ParallelGatewayItemId,
                BpmnSemanticTypes.ParallelGateway,
                GroupId,
                "Parallel Gateway",
                11,
                new ToolboxIconDescriptor("bpmn:parallel-gateway", "◇+")),
            new ToolboxItemDefinition(
                InclusiveGatewayItemId,
                BpmnSemanticTypes.InclusiveGateway,
                GroupId,
                "Inclusive Gateway",
                12,
                new ToolboxIconDescriptor("bpmn:inclusive-gateway", "◇O")),
            new ToolboxItemDefinition(
                EventBasedGatewayItemId,
                BpmnSemanticTypes.EventBasedGateway,
                GroupId,
                "Event-Based Gateway",
                13,
                new ToolboxIconDescriptor("bpmn:event-based-gateway", "◇◎")),
        ]);

    internal static ToolboxContribution N7Definition { get; } = new(
        groups:
        [
            new ToolboxGroupDefinition(
                EventsGroupId,
                "Events",
                0,
                SectionId,
                new ToolboxIconDescriptor("bpmn:events", "◎")),
            new ToolboxGroupDefinition(
                TasksGroupId,
                "Tasks",
                1,
                SectionId,
                new ToolboxIconDescriptor("bpmn:tasks", "▭")),
            new ToolboxGroupDefinition(
                GatewaysGroupId,
                "Gateways",
                2,
                SectionId,
                new ToolboxIconDescriptor("bpmn:gateways", "◇")),
        ],
        items:
        [
            new ToolboxItemDefinition(
                StartEventItemId,
                BpmnSemanticTypes.StartEvent,
                EventsGroupId,
                "Start Event",
                0,
                new ToolboxIconDescriptor("bpmn:start-event", "○")),
            new ToolboxItemDefinition(
                MessageCatchEventItemId,
                BpmnSemanticTypes.MessageCatchEvent,
                EventsGroupId,
                "Message Catch Event",
                1,
                new ToolboxIconDescriptor("bpmn:message-catch-event", "◎▱")),
            new ToolboxItemDefinition(
                MessageThrowEventItemId,
                BpmnSemanticTypes.MessageThrowEvent,
                EventsGroupId,
                "Message Throw Event",
                2,
                new ToolboxIconDescriptor("bpmn:message-throw-event", "◎▰")),
            new ToolboxItemDefinition(
                TimerCatchEventItemId,
                BpmnSemanticTypes.TimerCatchEvent,
                EventsGroupId,
                "Timer Catch Event",
                3,
                new ToolboxIconDescriptor("bpmn:timer-catch-event", "◎◷")),
            new ToolboxItemDefinition(
                SignalCatchEventItemId,
                BpmnSemanticTypes.SignalCatchEvent,
                EventsGroupId,
                "Signal Catch Event",
                4,
                new ToolboxIconDescriptor("bpmn:signal-catch-event", "◎△")),
            new ToolboxItemDefinition(
                SignalThrowEventItemId,
                BpmnSemanticTypes.SignalThrowEvent,
                EventsGroupId,
                "Signal Throw Event",
                5,
                new ToolboxIconDescriptor("bpmn:signal-throw-event", "◎▲")),
            new ToolboxItemDefinition(
                EndEventItemId,
                BpmnSemanticTypes.EndEvent,
                EventsGroupId,
                "End Event",
                6,
                new ToolboxIconDescriptor("bpmn:end-event", "◎")),
            new ToolboxItemDefinition(
                TaskItemId,
                BpmnSemanticTypes.Task,
                TasksGroupId,
                "Task",
                0,
                new ToolboxIconDescriptor("bpmn:task", "▭")),
            new ToolboxItemDefinition(
                UserTaskItemId,
                BpmnSemanticTypes.UserTask,
                TasksGroupId,
                "User Task",
                1,
                new ToolboxIconDescriptor("bpmn:user-task", "▭U")),
            new ToolboxItemDefinition(
                ManualTaskItemId,
                BpmnSemanticTypes.ManualTask,
                TasksGroupId,
                "Manual Task",
                2,
                new ToolboxIconDescriptor("bpmn:manual-task", "▭M")),
            new ToolboxItemDefinition(
                ServiceTaskItemId,
                BpmnSemanticTypes.ServiceTask,
                TasksGroupId,
                "Service Task",
                3,
                new ToolboxIconDescriptor("bpmn:service-task", "▭⚙")),
            new ToolboxItemDefinition(
                SendTaskItemId,
                BpmnSemanticTypes.SendTask,
                TasksGroupId,
                "Send Task",
                4,
                new ToolboxIconDescriptor("bpmn:send-task", "▭▶")),
            new ToolboxItemDefinition(
                ReceiveTaskItemId,
                BpmnSemanticTypes.ReceiveTask,
                TasksGroupId,
                "Receive Task",
                5,
                new ToolboxIconDescriptor("bpmn:receive-task", "▭▷")),
            new ToolboxItemDefinition(
                ExclusiveGatewayItemId,
                BpmnSemanticTypes.ExclusiveGateway,
                GatewaysGroupId,
                "Exclusive Gateway",
                0,
                new ToolboxIconDescriptor("bpmn:exclusive-gateway", "◇")),
            new ToolboxItemDefinition(
                ParallelGatewayItemId,
                BpmnSemanticTypes.ParallelGateway,
                GatewaysGroupId,
                "Parallel Gateway",
                1,
                new ToolboxIconDescriptor("bpmn:parallel-gateway", "◇+")),
            new ToolboxItemDefinition(
                InclusiveGatewayItemId,
                BpmnSemanticTypes.InclusiveGateway,
                GatewaysGroupId,
                "Inclusive Gateway",
                2,
                new ToolboxIconDescriptor("bpmn:inclusive-gateway", "◇O")),
            new ToolboxItemDefinition(
                EventBasedGatewayItemId,
                BpmnSemanticTypes.EventBasedGateway,
                GatewaysGroupId,
                "Event-Based Gateway",
                3,
                new ToolboxIconDescriptor("bpmn:event-based-gateway", "◇◎")),
        ],
        sections:
        [
            new ToolboxSectionDefinition(SectionId, "BPMN", 0),
        ]);

    internal static ToolboxContribution N81Definition { get; } = new(
        groups:
        [
            new ToolboxGroupDefinition(
                EventsGroupId,
                "Events",
                0,
                SectionId,
                new ToolboxIconDescriptor("bpmn:events", "◎")),
            new ToolboxGroupDefinition(
                ActivitiesGroupId,
                "Activities",
                1,
                SectionId,
                new ToolboxIconDescriptor("bpmn:activities", "▭")),
            new ToolboxGroupDefinition(
                GatewaysGroupId,
                "Gateways",
                2,
                SectionId,
                new ToolboxIconDescriptor("bpmn:gateways", "◇")),
        ],
        items:
        [
            new ToolboxItemDefinition(
                StartEventItemId,
                BpmnSemanticTypes.StartEvent,
                EventsGroupId,
                "Start Event",
                0,
                new ToolboxIconDescriptor("bpmn:start-event", "○")),
            new ToolboxItemDefinition(
                MessageCatchEventItemId,
                BpmnSemanticTypes.MessageCatchEvent,
                EventsGroupId,
                "Message Catch Event",
                1,
                new ToolboxIconDescriptor("bpmn:message-catch-event", "◎▱")),
            new ToolboxItemDefinition(
                MessageThrowEventItemId,
                BpmnSemanticTypes.MessageThrowEvent,
                EventsGroupId,
                "Message Throw Event",
                2,
                new ToolboxIconDescriptor("bpmn:message-throw-event", "◎▰")),
            new ToolboxItemDefinition(
                TimerCatchEventItemId,
                BpmnSemanticTypes.TimerCatchEvent,
                EventsGroupId,
                "Timer Catch Event",
                3,
                new ToolboxIconDescriptor("bpmn:timer-catch-event", "◎◷")),
            new ToolboxItemDefinition(
                SignalCatchEventItemId,
                BpmnSemanticTypes.SignalCatchEvent,
                EventsGroupId,
                "Signal Catch Event",
                4,
                new ToolboxIconDescriptor("bpmn:signal-catch-event", "◎△")),
            new ToolboxItemDefinition(
                SignalThrowEventItemId,
                BpmnSemanticTypes.SignalThrowEvent,
                EventsGroupId,
                "Signal Throw Event",
                5,
                new ToolboxIconDescriptor("bpmn:signal-throw-event", "◎▲")),
            new ToolboxItemDefinition(
                EndEventItemId,
                BpmnSemanticTypes.EndEvent,
                EventsGroupId,
                "End Event",
                6,
                new ToolboxIconDescriptor("bpmn:end-event", "◎")),
            new ToolboxItemDefinition(
                TaskItemId,
                BpmnSemanticTypes.Task,
                ActivitiesGroupId,
                "Task",
                0,
                new ToolboxIconDescriptor("bpmn:task", "▭")),
            new ToolboxItemDefinition(
                UserTaskItemId,
                BpmnSemanticTypes.UserTask,
                ActivitiesGroupId,
                "User Task",
                1,
                new ToolboxIconDescriptor("bpmn:user-task", "▭U")),
            new ToolboxItemDefinition(
                ManualTaskItemId,
                BpmnSemanticTypes.ManualTask,
                ActivitiesGroupId,
                "Manual Task",
                2,
                new ToolboxIconDescriptor("bpmn:manual-task", "▭M")),
            new ToolboxItemDefinition(
                ServiceTaskItemId,
                BpmnSemanticTypes.ServiceTask,
                ActivitiesGroupId,
                "Service Task",
                3,
                new ToolboxIconDescriptor("bpmn:service-task", "▭⚙")),
            new ToolboxItemDefinition(
                SendTaskItemId,
                BpmnSemanticTypes.SendTask,
                ActivitiesGroupId,
                "Send Task",
                4,
                new ToolboxIconDescriptor("bpmn:send-task", "▭▶")),
            new ToolboxItemDefinition(
                ReceiveTaskItemId,
                BpmnSemanticTypes.ReceiveTask,
                ActivitiesGroupId,
                "Receive Task",
                5,
                new ToolboxIconDescriptor("bpmn:receive-task", "▭▷")),
            new ToolboxItemDefinition(
                SubProcessItemId,
                BpmnSemanticTypes.SubProcess,
                ActivitiesGroupId,
                "SubProcess",
                6,
                new ToolboxIconDescriptor("bpmn:sub-process", "▭⊞")),
            new ToolboxItemDefinition(
                ExclusiveGatewayItemId,
                BpmnSemanticTypes.ExclusiveGateway,
                GatewaysGroupId,
                "Exclusive Gateway",
                0,
                new ToolboxIconDescriptor("bpmn:exclusive-gateway", "◇")),
            new ToolboxItemDefinition(
                ParallelGatewayItemId,
                BpmnSemanticTypes.ParallelGateway,
                GatewaysGroupId,
                "Parallel Gateway",
                1,
                new ToolboxIconDescriptor("bpmn:parallel-gateway", "◇+")),
            new ToolboxItemDefinition(
                InclusiveGatewayItemId,
                BpmnSemanticTypes.InclusiveGateway,
                GatewaysGroupId,
                "Inclusive Gateway",
                2,
                new ToolboxIconDescriptor("bpmn:inclusive-gateway", "◇O")),
            new ToolboxItemDefinition(
                EventBasedGatewayItemId,
                BpmnSemanticTypes.EventBasedGateway,
                GatewaysGroupId,
                "Event-Based Gateway",
                3,
                new ToolboxIconDescriptor("bpmn:event-based-gateway", "◇◎")),
        ],
        sections:
        [
            new ToolboxSectionDefinition(SectionId, "BPMN", 0),
        ]);

    internal static ToolboxContribution N90Definition { get; } =
        CreateN90Definition();

    internal static ToolboxContribution N91Definition { get; } =
        CreateN91Definition();

    private static ToolboxContribution CreateN90Definition()
    {
        var items = new List<ToolboxItemDefinition>();
        foreach (var item in N81Definition.Items)
        {
            if (item.GroupId == EventsGroupId && item.Order == 4)
            {
                items.Add(new ToolboxItemDefinition(
                    TimerBoundaryEventItemId,
                    BpmnSemanticTypes.TimerBoundaryEvent,
                    EventsGroupId,
                    "Timer Boundary Event",
                    4,
                    new ToolboxIconDescriptor(
                        "bpmn:timer-boundary-event",
                        "▭◎◷")));
            }

            items.Add(item.GroupId == EventsGroupId && item.Order >= 4
                ? new ToolboxItemDefinition(
                    item.ItemId,
                    item.ElementTypeId,
                    item.GroupId,
                    item.DisplayName,
                    item.Order + 1,
                    item.Icon)
                : item);
        }

        return new ToolboxContribution(
            N81Definition.Groups,
            items,
            N81Definition.Sections);
    }

    private static ToolboxContribution CreateN91Definition()
    {
        var eventOrders = new Dictionary<ToolboxItemId, int>
        {
            [StartEventItemId] = 0,
            [MessageCatchEventItemId] = 1,
            [MessageThrowEventItemId] = 2,
            [TimerCatchEventItemId] = 4,
            [TimerBoundaryEventItemId] = 5,
            [SignalCatchEventItemId] = 6,
            [SignalThrowEventItemId] = 7,
            [EndEventItemId] = 9,
        };
        var items = new List<ToolboxItemDefinition>();
        foreach (var item in N90Definition.Items)
        {
            if (item.ItemId == TimerCatchEventItemId)
            {
                items.Add(new ToolboxItemDefinition(
                    MessageBoundaryEventItemId,
                    BpmnSemanticTypes.MessageBoundaryEvent,
                    EventsGroupId,
                    "Message Boundary Event",
                    3,
                    new ToolboxIconDescriptor(
                        "bpmn:message-boundary-event",
                        "▭◎▱")));
            }

            if (item.ItemId == EndEventItemId)
            {
                items.Add(new ToolboxItemDefinition(
                    SignalBoundaryEventItemId,
                    BpmnSemanticTypes.SignalBoundaryEvent,
                    EventsGroupId,
                    "Signal Boundary Event",
                    8,
                    new ToolboxIconDescriptor(
                        "bpmn:signal-boundary-event",
                        "▭◎△")));
            }

            items.Add(item.GroupId == EventsGroupId
                ? new ToolboxItemDefinition(
                    item.ItemId,
                    item.ElementTypeId,
                    item.GroupId,
                    item.DisplayName,
                    eventOrders[item.ItemId],
                    item.Icon)
                : item);
        }

        return new ToolboxContribution(
            N90Definition.Groups,
            items,
            N90Definition.Sections);
    }
}
