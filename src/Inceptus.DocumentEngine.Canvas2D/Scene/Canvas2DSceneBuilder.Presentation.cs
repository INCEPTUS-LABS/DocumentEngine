using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    internal ValueTask<Canvas2DSceneBuildResult> BuildMeasuredAsync(DocumentSnapshot document,
        PresentationViewContext context, ModelProfileViewStateSnapshot profiles,
        ModelProfileElementViewStateSnapshot elements, ProjectedGraph graph, LayoutResult layout,
        RoutingResult routing, VisualModelSnapshot visuals, EditorStateSnapshot editor,
        ITextMetricsService textMetrics,
        Func<string, Canvas2DSceneStyle, double, TextMeasurementRequest> requestFactory,
        CancellationToken cancellationToken, PresentationViewPlan? plan = null) => BuildMeasuredCoreAsync(graph, layout, routing, visuals,
            editor, textMetrics, requestFactory, new ScenePresentationInput(document, context, profiles, elements, plan), cancellationToken);

    internal Canvas2DSceneBuildResult Build(DocumentSnapshot document, PresentationViewContext context,
        ModelProfileViewStateSnapshot profiles, ModelProfileElementViewStateSnapshot elements,
        ProjectedGraph graph, LayoutResult layout, RoutingResult routing, VisualModelSnapshot visuals,
        EditorStateSnapshot editor, PresentationViewPlan? plan = null) => BuildCore(graph, layout, routing, visuals, editor,
            presentation: new ScenePresentationInput(document, context, profiles, elements, plan));

    private static void ApplyPresentationCoordinates(List<Canvas2DSceneItem> items, ScenePresentationInput? presentation)
    {
        if (presentation?.Plan is not { } plan || plan.Context is not DocumentPresentationContext) return;
        var document = presentation.Document;
        var placements = plan.ScopePlacements.ToDictionary(static placement => placement.ScopeId);
        var savedScopes = (document.VisualModel.RoutingScopes ?? []).ToDictionary(static state => state.ScopeId);
        var records = (document.VisualModel.RoutingScopes ?? [])
            .SelectMany(static scope => scope.Connectors).ToDictionary(static record => record.VisualStateId);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Origin.SemanticElementId is not { } id ||
                !document.SemanticModel.TryGetScope(id, out var scope) || scope is null ||
                !placements.TryGetValue(scope.Id, out var placement)) continue;
            if (document.SemanticModel.TryGetRelationship(id, out var relationship) && relationship is not null &&
                (!document.SemanticModel.TryGetScope(relationship.TargetId, out var targetScope) || targetScope?.Id != scope.Id)) continue;
            // Node commands use authored region-local coordinates. Saved connector paths already
            // use the expanded scope basis, so only node-origin items include this inner translation.
            var localPlacement = placement;
            if (document.SemanticModel.TryGetElement(id, out _) && item.Origin.VisualStateId is { } nodeVisualId &&
                savedScopes.TryGetValue(scope.Id, out var savedScope) &&
                savedScope.Geometry.Nodes.SingleOrDefault(node => node.VisualStateId == nodeVisualId)?.RegionId is { } regionId &&
                savedScope.Geometry.Regions.Single(region => region.Id == regionId).LocalToScopeTransform is { } regionTransform)
            {
                localPlacement = new PresentationScopePlacement(placement.ScopeId,
                    placement.Translation + new VectorD(regionTransform.OffsetX, regionTransform.OffsetY),
                    placement.BoundaryVisualStateId);
            }
            var mapping = item.ConnectorPresentationMapping;
            var metadata = item.Metadata;
            if (item.Layer == Canvas2DSceneLayer.Connector && item.Origin.VisualStateId is { } visualId &&
                records.TryGetValue(visualId, out var record) && record.Path.Length >= 2 &&
                item.Metadata.ContainsKey(Canvas2DConnectorPathMetadata.LogicalPathPointCount))
            {
                var canonical = record.RoutingType == ConnectorRoutingType.Manual ? record.Path
                    : System.Collections.Immutable.ImmutableArray.Create(record.Path[0], record.Path[^1]);
                var displayed = canonical.Select(placement.MapLocalToView).ToArray();
                mapping = new Canvas2DConnectorPresentationMapping(canonical, displayed,
                    Matrix2D.CreateTranslation(placement.Translation), null, null);
                metadata = new(CreatePresentedConnectorMetadata(item.Metadata, Canvas2DConnectorPathMetadata.Resolve(item),
                    displayed, Canvas2DConnectorPathMetadata.IsNoRouteFallbackPath(item)));
            }
            items[index] = new Canvas2DSceneItem(localPlacement, item.Id, item.Layer, item.ZIndex, item.Geometry, item.Origin,
                item.Transform, item.Clip, item.Style, item.IsVisible, item.HitTestPolicy, item.PersistentAppearance,
                metadata, item.Bounds, item.SpatialRegion, mapping);
        }
    }
}
