using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Arrangement;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.History;

internal sealed class BpmnAutoArrangeHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(ICommand command, DocumentSnapshot before, DocumentSnapshot committed) =>
        CommandHistoryPreparationResult.Undoable(new Factory(BpmnArrangeHistoryDelta.Capture(committed, before)),
            new Factory(BpmnArrangeHistoryDelta.Capture(before, committed)));

    private sealed class Factory(BpmnArrangeHistoryDelta delta) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) => new BpmnAutoArrangeCommand(documentId, expectedRevision, delta);
    }
}

// Only changed positions, anchors, region frame fields, dimensions and route records are retained.
// Semantic objects, properties, node sizes and unrelated document fields never belong to this delta.
internal sealed record BpmnArrangeVisualDelta(VisualStateId Id, PointD? Position, ImmutableArray<ConnectorAnchor>? Anchors);
internal sealed record BpmnArrangeRoutesDelta(ImmutableArray<ConnectorRoutingRecord> Replacements,
    ImmutableArray<VisualStateId> Priority)
{
    internal static BpmnArrangeRoutesDelta Capture(ImmutableArray<ConnectorRoutingRecord> from, ImmutableArray<ConnectorRoutingRecord> to)
    {
        var prior = from.ToDictionary(record => record.VisualStateId);
        return new([.. to.Where(record => !Equals(prior.GetValueOrDefault(record.VisualStateId), record))],
            from.Select(record => record.VisualStateId).SequenceEqual(to.Select(record => record.VisualStateId))
                ? [] : [.. to.Select(record => record.VisualStateId)]);
    }

    internal ImmutableArray<ConnectorRoutingRecord> Apply(ImmutableArray<ConnectorRoutingRecord> current)
    {
        var changed = Replacements.ToDictionary(record => record.VisualStateId);
        var after = current.Select(record => changed.GetValueOrDefault(record.VisualStateId, record)).ToArray();
        if (Priority.IsEmpty) return [.. after];
        var target = Priority.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
        return [.. after.OrderBy(record => target.GetValueOrDefault(record.VisualStateId, int.MaxValue))];
    }
}

internal sealed record BpmnArrangeHistoryDelta(ImmutableArray<BpmnArrangeVisualDelta> Visuals,
    ImmutableDictionary<DocumentScopeId, ImmutableArray<BpmnPoolRegionDelta>> Regions,
    ImmutableArray<SpatialRegionHeightIntent> Heights, ImmutableArray<SpatialScopeWidthIntent> Widths,
    ImmutableDictionary<DocumentScopeId, BpmnArrangeRoutesDelta> ScopeRoutes,
    ImmutableDictionary<SemanticElementId, BpmnArrangeRoutesDelta> DocumentRoutes)
{
    internal static BpmnArrangeHistoryDelta Capture(DocumentSnapshot from, DocumentSnapshot to)
    {
        var visuals = to.VisualModel.VisualStates.Where(visual => from.VisualModel.TryGetVisualState(visual.Id, out _)).Select(visual =>
        {
            from.VisualModel.TryGetVisualState(visual.Id, out var prior);
            return new BpmnArrangeVisualDelta(visual.Id, prior!.Position != visual.Position ? visual.Position : null,
                !prior.ConnectorAnchors.AsSpan().SequenceEqual(visual.ConnectorAnchors.AsSpan()) ? visual.ConnectorAnchors : null);
        }).Where(delta => delta.Position is not null || delta.Anchors is not null).ToImmutableArray();
        var regions = ImmutableDictionary.CreateBuilder<DocumentScopeId, ImmutableArray<BpmnPoolRegionDelta>>();
        var heights = ImmutableArray.CreateBuilder<SpatialRegionHeightIntent>();
        var widths = ImmutableArray.CreateBuilder<SpatialScopeWidthIntent>();
        var routes = ImmutableDictionary.CreateBuilder<DocumentScopeId, BpmnArrangeRoutesDelta>();
        foreach (var scope in to.VisualModel.RoutingScopes ?? [])
        {
            var prior = from.VisualModel.RoutingScopes?.SingleOrDefault(item => item.ScopeId == scope.ScopeId);
            if (prior is null) continue;
            var regionDeltas = scope.Geometry.Regions.Select(region => new BpmnPoolRegionDelta(
                    prior.Geometry.Regions.Single(item => item.Id == region.Id), region)).Where(delta => delta.From != delta.To).ToImmutableArray();
            if (!regionDeltas.IsEmpty) regions.Add(scope.ScopeId, regionDeltas);
            heights.AddRange(regionDeltas.Where(delta => delta.From.ExpandedHeight != delta.To.ExpandedHeight)
                .Select(delta => new SpatialRegionHeightIntent(scope.ScopeId, delta.To.Id, delta.To.ExpandedHeight)));
            widths.AddRange(scope.Geometry.SpatialWidths.Where(width => !prior.Geometry.SpatialWidths.Contains(width))
                .Select(width => new SpatialScopeWidthIntent(scope.ScopeId, width.ProfileId, width.OuterWidth)));
            if (!prior.Connectors.AsSpan().SequenceEqual(scope.Connectors.AsSpan())) routes.Add(scope.ScopeId, BpmnArrangeRoutesDelta.Capture(prior.Connectors, scope.Connectors));
        }
        var documentRoutes = to.VisualModel.DocumentRoutingStates.Select(owner =>
            (owner.OwnerId, Old: from.VisualModel.DocumentRoutingStates.Single(item => item.OwnerId == owner.OwnerId).Connectors, owner.Connectors))
            .Where(item => !item.Old.AsSpan().SequenceEqual(item.Connectors.AsSpan()))
            .ToImmutableDictionary(item => item.OwnerId, item => BpmnArrangeRoutesDelta.Capture(item.Old, item.Connectors));
        return new(visuals, regions.ToImmutable(), heights.ToImmutable(), widths.ToImmutable(), routes.ToImmutable(), documentRoutes);
    }

    internal CommandHandlerResult Apply(DocumentSnapshot document)
    {
        var deltas = Visuals.ToDictionary(delta => delta.Id);
        var model = document.VisualModel;
        var visual = new VisualModelSnapshot(document.DocumentId, document.Revision,
            model.VisualStates.Select(state => deltas.TryGetValue(state.Id, out var delta)
                ? BpmnAutoArrangePlanner.WithGeometry(state, delta.Position ?? state.Position, delta.Anchors ?? state.ConnectorAnchors) : state),
            model.ProfileElementPresentations, model.RoutingScopes?.Select(scope =>
            {
                var geometry = scope.Geometry;
                var regions = Regions.GetValueOrDefault(scope.ScopeId, []).ToDictionary(delta => delta.From.Id);
                return new ScopeRoutingSnapshot(scope.ScopeId, new ScopeGeometrySnapshot(geometry.PolicyId, geometry.PolicyVersion,
                    geometry.LayoutAlgorithmId, geometry.Configuration, geometry.TextConfiguration, geometry.Contributors,
                    geometry.Nodes, geometry.Regions.Select(region => regions.TryGetValue(region.Id, out var delta) ? delta.Apply(region) : region),
                    geometry.Captions, geometry.TextMeasurements, geometry.SpatialWidths),
                    ScopeRoutes.TryGetValue(scope.ScopeId, out var routes) ? routes.Apply(scope.Connectors) : scope.Connectors);
            }), model.DocumentRoutingStates.Select(owner => DocumentRoutes.TryGetValue(owner.OwnerId, out var routes)
                ? new DocumentRoutingSnapshot(owner.OwnerId, routes.Apply(owner.Connectors)) : owner));
        return CommandHandlerResult.SuccessWithPreparation(new(document.SemanticModel, visual, document.Metadata, document.Publication),
            [], Heights, Widths, pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout,
            nodeGeometryImpact: Visuals.IsEmpty ? NodeGeometryPipelineImpact.PreserveAll :
                NodeGeometryPipelineImpact.ForChangedVisualStates(Visuals.Select(delta => delta.Id)));
    }
}
