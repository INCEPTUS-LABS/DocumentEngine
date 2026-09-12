using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Semantics;

/// <summary>
/// Owns semantic classification and descriptive metadata for supported BPMN
/// Boundary Events. Boundary Events are Catch Events but are not Intermediate Events.
/// </summary>
public static class BpmnBoundaryEventSemanticTypes
{
    public static ImmutableArray<SemanticTypeId> All { get; } =
    [
        BpmnSemanticTypes.TimerBoundaryEvent,
        BpmnSemanticTypes.MessageBoundaryEvent,
        BpmnSemanticTypes.SignalBoundaryEvent,
    ];

    public static bool IsBoundaryEvent(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        return All.Contains(typeId);
    }

    internal static string DisplayName(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        if (typeId == BpmnSemanticTypes.TimerBoundaryEvent)
        {
            return "Timer Boundary Event";
        }

        if (typeId == BpmnSemanticTypes.MessageBoundaryEvent)
        {
            return "Message Boundary Event";
        }

        return typeId == BpmnSemanticTypes.SignalBoundaryEvent
            ? "Signal Boundary Event"
            : throw new ArgumentException(
                $"Semantic type '{typeId}' is not a supported BPMN Boundary Event.",
                nameof(typeId));
    }
}
