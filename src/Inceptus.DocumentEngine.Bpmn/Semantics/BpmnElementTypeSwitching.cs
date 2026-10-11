using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Semantics;

/// <summary>
/// Defines compatible concrete element types. Groups are derived from TypeId and
/// never form additional semantic or persistent element state.
/// </summary>
public static class BpmnElementTypeSwitching
{
    public static ImmutableArray<SemanticTypeId> GetSwitchableTypes(SemanticTypeId currentType)
    {
        ArgumentNullException.ThrowIfNull(currentType);
        if (BpmnTaskSemanticTypes.IsTask(currentType))
        {
            return BpmnTaskSemanticTypes.All;
        }

        if (BpmnIntermediateEventSemanticTypes.IsCatchEvent(currentType))
        {
            return BpmnIntermediateEventSemanticTypes.Catch;
        }

        if (BpmnIntermediateEventSemanticTypes.IsThrowEvent(currentType))
        {
            return BpmnIntermediateEventSemanticTypes.Throw;
        }

        if (BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(currentType))
        {
            return BpmnBoundaryEventSemanticTypes.All;
        }

        return BpmnGatewaySemanticTypes.IsGateway(currentType)
            ? BpmnGatewaySemanticTypes.All
            : [];
    }

    public static bool CanSwitch(SemanticTypeId sourceType, SemanticTypeId targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        return GetSwitchableTypes(sourceType).Contains(targetType);
    }

    public static string GetDisplayName(SemanticTypeId typeId)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        if (BpmnTaskSemanticTypes.IsTask(typeId))
        {
            return BpmnTaskSemanticTypes.DisplayName(typeId);
        }

        if (BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(typeId))
        {
            return BpmnIntermediateEventSemanticTypes.DisplayName(typeId);
        }

        if (BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(typeId))
        {
            return BpmnBoundaryEventSemanticTypes.DisplayName(typeId);
        }

        if (typeId == BpmnSemanticTypes.ExclusiveGateway) { return "Exclusive Gateway"; }
        if (typeId == BpmnSemanticTypes.ParallelGateway) { return "Parallel Gateway"; }
        if (typeId == BpmnSemanticTypes.InclusiveGateway) { return "Inclusive Gateway"; }
        if (typeId == BpmnSemanticTypes.EventBasedGateway) { return "Event-Based Gateway"; }
        if (typeId == BpmnSemanticTypes.StartEvent) { return "Start Event"; }
        if (typeId == BpmnSemanticTypes.EndEvent) { return "End Event"; }
        if (typeId == BpmnSemanticTypes.SubProcess) { return "SubProcess"; }
        return typeId.Value;
    }
}
