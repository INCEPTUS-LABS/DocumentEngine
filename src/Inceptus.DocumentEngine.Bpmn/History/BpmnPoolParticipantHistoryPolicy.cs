using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Contracts.Routing;
using System.Collections.Immutable;

namespace Inceptus.DocumentEngine.Bpmn.History;

internal sealed class BpmnPoolParticipantHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        var assignment = (SetBpmnPoolParticipantCommand)command;
        before.SemanticModel.TryGetElement(assignment.PoolId, out var previous);
        committed.SemanticModel.TryGetElement(assignment.PoolId, out var current);
        var previousRegions = (before.VisualModel.RoutingScopes ?? []).SelectMany(scope => scope.Geometry.Regions)
            .ToDictionary(region => region.Id);
        var changed = (committed.VisualModel.RoutingScopes ?? []).SelectMany(scope => scope.Geometry.Regions)
            .Where(region => previousRegions.TryGetValue(region.Id, out var prior) && prior != region).ToArray();
        return CommandHistoryPreparationResult.Undoable(
            new Factory(assignment.PoolId, OrganizationalSemantics.GetParticipantId(previous!),
                [.. changed.Select(region => new BpmnPoolRegionDelta(region, previousRegions[region.Id]))]),
            new Factory(assignment.PoolId, OrganizationalSemantics.GetParticipantId(current!),
                [.. changed.Select(region => new BpmnPoolRegionDelta(previousRegions[region.Id], region))]));
    }
    private sealed class Factory(SemanticElementId pool, SemanticElementId? participant,
        ImmutableArray<BpmnPoolRegionDelta> regions) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new SetBpmnPoolParticipantCommand(documentId, expectedRevision, pool, participant) { RegionDeltas = regions };
    }
}
