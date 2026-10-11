using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Toolbox;
using Inceptus.DocumentEngine.Bpmn.Placement;
using Inceptus.DocumentEngine.Bpmn.ConnectionCreation;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Commands;

/// <summary>Concrete registered placement types eligible for a new, same-context Sequence Flow target.</summary>
public static class BpmnSmartNextCatalog
{
    public static bool SupportsElementActions(DocumentSnapshot document, SemanticElementId elementId) =>
        document.SemanticModel.TryGetElement(elementId, out var element) && BpmnSemanticTypes.IsFlowNode(element!.TypeId);

    public static ImmutableArray<ToolboxItemDefinition> GetCandidates(DocumentSnapshot document,
        SemanticElementId sourceId, DocumentScopeId scopeId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.SemanticModel.TryGetElement(sourceId, out var source) || source is null ||
            !document.SemanticModel.TryGetScope(sourceId, out var scope) || scope?.Id != scopeId)
            return [];
        var visuals = document.VisualModel.VisualStates.Where(visual => visual.SemanticElementId == sourceId).ToArray();
        if (visuals.Length != 1 ||
            !BpmnElementConnectionCreation.CandidateSides(visuals[0], source.TypeId, ConnectorAnchorRole.Source).Any() &&
            !ElementConnectorAnchorResolver.Resolve(visuals[0], source.TypeId, BpmnConnectorAnchorPolicies.Provider).Any(anchor =>
                anchor.Allows(ConnectorAnchorRole.Source) && !ConnectorAnchorOccupancy.IsOccupied(document.VisualModel, anchor.Id))) return [];
        return [.. BpmnToolboxContribution.N91Definition.Items
            .Where(item => BpmnToolboxPlacementContribution.N91Registrations.Any(registration =>
                registration.ToolboxItemId == item.ItemId && registration.CommandFactory is BpmnToolboxPlacementCommandFactory) &&
                BpmnSequenceFlowConfigurationRules.CanAppendTargetType(document, sourceId, item.ElementTypeId) && DefaultsAvailable(item))
            .OrderBy(item => item.GroupId == BpmnToolboxContribution.EventsGroupId ? 0 :
                item.GroupId == BpmnToolboxContribution.GatewaysGroupId ? 2 : 1)
            .ThenBy(item => item.Order).ThenBy(item => item.ItemId.Value, StringComparer.Ordinal)];
        bool DefaultsAvailable(ToolboxItemDefinition item)
        {
            BpmnToolboxCreationDefaults.Resolve(document, item.ElementTypeId, out var failure);
            return failure is null;
        }
    }
}
