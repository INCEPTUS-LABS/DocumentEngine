using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Visuals;

public static class BpmnGridPolicy
{
    public static bool Supports(SemanticTypeId type) =>
        BpmnSemanticTypes.IsFlowNode(type) && !BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(type);
}
