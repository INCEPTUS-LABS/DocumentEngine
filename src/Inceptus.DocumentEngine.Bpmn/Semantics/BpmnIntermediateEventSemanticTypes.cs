using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Semantics;

/// <summary>
/// Owns semantic classification and descriptive metadata for the supported BPMN
/// intermediate-event family. Catch versus Throw is derived solely from the canonical
/// SemanticTypeId; it is not an additional persistent property.
/// </summary>
public static class BpmnIntermediateEventSemanticTypes
{
    public static ImmutableArray<SemanticTypeId> All { get; } =
    [
        BpmnSemanticTypes.MessageCatchEvent,
        BpmnSemanticTypes.MessageThrowEvent,
        BpmnSemanticTypes.TimerCatchEvent,
        BpmnSemanticTypes.SignalCatchEvent,
        BpmnSemanticTypes.SignalThrowEvent,
    ];

    public static ImmutableArray<SemanticTypeId> Catch { get; } =
    [
        BpmnSemanticTypes.MessageCatchEvent,
        BpmnSemanticTypes.TimerCatchEvent,
        BpmnSemanticTypes.SignalCatchEvent,
    ];

    public static ImmutableArray<SemanticTypeId> Throw { get; } =
    [
        BpmnSemanticTypes.MessageThrowEvent,
        BpmnSemanticTypes.SignalThrowEvent,
    ];

    public static bool IsIntermediateEvent(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        return All.Contains(typeId);
    }

    public static bool IsCatchEvent(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        return Catch.Contains(typeId);
    }

    public static bool IsThrowEvent(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        return Throw.Contains(typeId);
    }

    internal static string DisplayName(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        if (typeId == BpmnSemanticTypes.MessageCatchEvent)
        {
            return "Message Catch Event";
        }

        if (typeId == BpmnSemanticTypes.MessageThrowEvent)
        {
            return "Message Throw Event";
        }

        if (typeId == BpmnSemanticTypes.TimerCatchEvent)
        {
            return "Timer Catch Event";
        }

        if (typeId == BpmnSemanticTypes.SignalCatchEvent)
        {
            return "Signal Catch Event";
        }

        if (typeId == BpmnSemanticTypes.SignalThrowEvent)
        {
            return "Signal Throw Event";
        }

        throw new ArgumentException(
            $"Semantic type '{typeId}' is not a supported BPMN intermediate event.",
            nameof(typeId));
    }
}
