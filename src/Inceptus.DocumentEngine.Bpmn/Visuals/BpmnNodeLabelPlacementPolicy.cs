using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.Bpmn.Visuals;

internal static class BpmnNodeLabelPlacementPolicy
{
    internal static NodeLabelPlacement OutsideBelow { get; } =
        new(NodeLabelPlacementKind.OutsideBelow, gap: 8d, maximumWidth: 160d);

    internal static NodeLabelPlacement Resolve(SemanticTypeId type) =>
        BpmnSemanticTypes.IsGateway(type) ||
        BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(type) ||
        BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(type)
            ? OutsideBelow
            : NodeLabelPlacement.InsideCentered;
}
