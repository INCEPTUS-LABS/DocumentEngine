using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Semantics;

/// <summary>Owns the supported concrete BPMN Gateway classification.</summary>
public static class BpmnGatewaySemanticTypes
{
    public static ImmutableArray<SemanticTypeId> All { get; } =
    [
        BpmnSemanticTypes.ExclusiveGateway,
        BpmnSemanticTypes.ParallelGateway,
        BpmnSemanticTypes.InclusiveGateway,
        BpmnSemanticTypes.EventBasedGateway,
    ];

    public static bool IsGateway(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        return All.Contains(typeId);
    }
}
