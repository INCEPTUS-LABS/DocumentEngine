using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Semantics;

/// <summary>
/// Owns the semantic classification and descriptive metadata of the supported BPMN Task family.
/// </summary>
public static class BpmnTaskSemanticTypes
{
    public static ImmutableArray<SemanticTypeId> All { get; } =
    [
        BpmnSemanticTypes.Task,
        BpmnSemanticTypes.UserTask,
        BpmnSemanticTypes.ManualTask,
        BpmnSemanticTypes.ServiceTask,
        BpmnSemanticTypes.SendTask,
        BpmnSemanticTypes.ReceiveTask,
    ];

    public static bool IsTask(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        return All.Contains(typeId);
    }

    internal static string DisplayName(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        if (typeId == BpmnSemanticTypes.Task)
        {
            return "Task";
        }

        if (typeId == BpmnSemanticTypes.UserTask)
        {
            return "User Task";
        }

        if (typeId == BpmnSemanticTypes.ManualTask)
        {
            return "Manual Task";
        }

        if (typeId == BpmnSemanticTypes.ServiceTask)
        {
            return "Service Task";
        }

        if (typeId == BpmnSemanticTypes.SendTask)
        {
            return "Send Task";
        }

        if (typeId == BpmnSemanticTypes.ReceiveTask)
        {
            return "Receive Task";
        }

        throw new ArgumentException(
            $"Semantic type '{typeId}' is not a supported BPMN Task.",
            nameof(typeId));
    }

    internal static string DefaultCodePrefix(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        if (typeId == BpmnSemanticTypes.Task)
        {
            return "TASK";
        }

        if (typeId == BpmnSemanticTypes.UserTask)
        {
            return "USER_TASK";
        }

        if (typeId == BpmnSemanticTypes.ManualTask)
        {
            return "MANUAL_TASK";
        }

        if (typeId == BpmnSemanticTypes.ServiceTask)
        {
            return "SERVICE_TASK";
        }

        if (typeId == BpmnSemanticTypes.SendTask)
        {
            return "SEND_TASK";
        }

        if (typeId == BpmnSemanticTypes.ReceiveTask)
        {
            return "RECEIVE_TASK";
        }

        throw new ArgumentException(
            $"Semantic type '{typeId}' is not a supported BPMN Task.",
            nameof(typeId));
    }
}
