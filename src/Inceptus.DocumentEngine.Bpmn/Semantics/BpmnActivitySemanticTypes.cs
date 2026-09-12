using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Semantics;

/// <summary>
/// Owns the semantic classification and descriptive metadata of the supported BPMN
/// Activity family. Task classification remains the narrower authority for Tasks.
/// </summary>
public static class BpmnActivitySemanticTypes
{
    public static ImmutableArray<SemanticTypeId> All { get; } =
    [
        .. BpmnTaskSemanticTypes.All,
        BpmnSemanticTypes.SubProcess,
    ];

    public static bool IsActivity(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        return All.Contains(typeId);
    }

    internal static string DisplayName(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        return BpmnTaskSemanticTypes.IsTask(typeId)
            ? BpmnTaskSemanticTypes.DisplayName(typeId)
            : typeId == BpmnSemanticTypes.SubProcess
                ? "SubProcess"
                : throw new ArgumentException(
                    $"Semantic type '{typeId}' is not a supported BPMN Activity.",
                    nameof(typeId));
    }

    internal static string DefaultCodePrefix(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        return BpmnTaskSemanticTypes.IsTask(typeId)
            ? BpmnTaskSemanticTypes.DefaultCodePrefix(typeId)
            : typeId == BpmnSemanticTypes.SubProcess
                ? "SUBPROCESS"
                : throw new ArgumentException(
                    $"Semantic type '{typeId}' is not a supported BPMN Activity.",
                    nameof(typeId));
    }
}
