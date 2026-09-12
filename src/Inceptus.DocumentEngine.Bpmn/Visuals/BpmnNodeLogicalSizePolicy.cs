using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Visuals;

/// <summary>
/// Owns the canonical fallback logical sizes for the supported BPMN flow nodes.
/// </summary>
internal static class BpmnNodeLogicalSizePolicy
{
    internal static SizeD EventSize { get; } = new(36d, 36d);

    internal static SizeD TaskSize { get; } = new(120d, 80d);

    internal static SizeD GatewaySize { get; } = new(48d, 48d);

    internal static SizeD Resolve(SemanticTypeId semanticTypeId)
    {
        ArgumentNullException.ThrowIfNull(semanticTypeId);

        if (BpmnActivitySemanticTypes.IsActivity(semanticTypeId))
        {
            return TaskSize;
        }

        if (BpmnSemanticTypes.IsGateway(semanticTypeId))
        {
            return GatewaySize;
        }

        if (semanticTypeId == BpmnSemanticTypes.StartEvent ||
            semanticTypeId == BpmnSemanticTypes.EndEvent ||
            BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(semanticTypeId) ||
            BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(semanticTypeId))
        {
            return EventSize;
        }

        throw new ArgumentException(
            $"Semantic type '{semanticTypeId}' is not a supported BPMN flow node.",
            nameof(semanticTypeId));
    }
}
