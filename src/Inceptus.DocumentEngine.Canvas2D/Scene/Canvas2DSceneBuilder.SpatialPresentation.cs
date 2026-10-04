using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    private static void ApplySpatialPresentation(
        ProjectedGraph graph,
        RoutingResult routing,
        VisualModelSnapshot visualModel,
        List<Canvas2DSceneItem> items,
        Canvas2DSpatialPresentationPlan plan,
        ICanvas2DConnectorPresentationRouter connectorRouter,
        List<Diagnostic> diagnostics)
    {
        var projectedObjects = EnumerateProjectedObjects(graph)
            .Where(static projected => projected is not null)
            .Cast<IProjectedObject>()
            .ToDictionary(static projected => projected.Id);
        var nodesById = graph.Nodes.ToDictionary(static node => node.Id);
        var edgesById = graph.Edges.ToDictionary(static edge => edge.Id);
        var labelsById = graph.Labels.ToDictionary(static label => label.Id);
        var portsById = graph.Ports.ToDictionary(static port => port.Id);
        var visualsById = visualModel.VisualStates.ToDictionary(static visual => visual.Id);

        ValidateSpatialPlan(graph, plan, projectedObjects, diagnostics);
        foreach (var target in plan.ResizeTargets)
        {
            var border = items.FirstOrDefault(item => item.Id == target.BorderSceneObjectId);
            if (border is null || !border.IsVisible || border.Bounds != target.PaintedBounds ||
                border.Style.Stroke is null || border.Style.StrokeWidth <= 0d ||
                (border.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0)
                diagnostics.Add(Error(Canvas2DSceneDiagnosticCodes.InvalidContribution,
                    "A spatial resize target must identify its actual visible painted border.", target.BorderSceneObjectId.Value));
        }
        if (HasErrors(diagnostics))
        {
            return;
        }

        // Canonical Process primitives are moved exactly once. Presentation-contributor items
        // (Pool frames, headers, and other profile decoration) have no primary Process placement
        // and are already expressed in final Scene coordinates.
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (IsFrameworkConnectorFamilyItem(item, edgesById) ||
                !TryResolveSpatialPlacement(
                    item,
                    projectedObjects,
                    nodesById,
                    portsById,
                    plan,
                    out var placement,
                    out var region))
            {
                continue;
            }

            items[index] = MaterializeSpatialItem(item, placement!, region!);
        }

        var finalRoutes = new Dictionary<ProjectedObjectId, PresentedConnector>();
        var presentedObstacles = items
            .Where(item =>
                item.IsVisible &&
                item.Layer == Canvas2DSceneLayer.Content &&
                item.Origin.ProjectedObjectId is { } projectedId &&
                nodesById.ContainsKey(projectedId))
            .Select(static item => item.Bounds)
            .ToArray();
        foreach (var edge in graph.Edges)
        {
            var connectorId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
            var connector = items.FirstOrDefault(item => item.Id == connectorId);
            if (connector is null)
            {
                continue;
            }

            var sourcePlacement = ResolveNodePlacement(edge.SourceNodeId, nodesById, plan);
            var targetPlacement = ResolveNodePlacement(edge.TargetNodeId, nodesById, plan);
            var isVisible = connector.IsVisible &&
                (sourcePlacement?.IsVisible ?? true) &&
                (targetPlacement?.IsVisible ?? true);
            var sourceRegion = ResolveRegion(sourcePlacement, plan);
            var targetRegion = ResolveRegion(targetPlacement, plan);
            var canonicalLogicalPath = Canvas2DConnectorPathMetadata.Resolve(connector);
            if (routing.LogicalGeometry is { } logical)
            {
                var saved = visualModel.RoutingScopes?.FirstOrDefault(scope => scope.ScopeId == logical.ScopeId)
                    ?.Connectors.FirstOrDefault(record => record.VisualStateId == edge.Source.VisualStateId);
                if (saved is null || (saved.Outcome == ConnectorRoutingOutcome.Path &&
                        !saved.Path.AsSpan().SequenceEqual(canonicalLogicalPath.AsSpan())))
                {
                    diagnostics.Add(Error(Canvas2DSceneDiagnosticCodes.InvalidContribution,
                        $"Saved expanded connector geometry is missing or incompatible for '{edge.Id}'.", edge.Id.Value));
                    continue;
                }
                var logicalEditable = saved.RoutingType == ConnectorRoutingType.Manual
                    ? saved.Path
                    : ImmutableArray.Create(canonicalLogicalPath[0], canonicalLogicalPath[^1]);
                var displayedEditable = logicalEditable.Select(plan.MapLogicalToScene).ToImmutableArray();
                var displayedPath = saved.Outcome == ConnectorRoutingOutcome.NoRoute ||
                    saved.RoutingType != ConnectorRoutingType.Automatic
                        ? canonicalLogicalPath.Select(plan.MapLogicalToScene).ToImmutableArray()
                        : plan.CoordinateMap.MapPath(saved.Path);
                var mapping = new Canvas2DConnectorPresentationMapping(logicalEditable, displayedEditable,
                    plan.CoordinateMap, sourceRegion?.Id, targetRegion?.Id);
                finalRoutes.Add(edge.Id, new PresentedConnector(connector,
                    new Canvas2DConnectorPresentationRoute(displayedPath, mapping), isVisible,
                    saved.Outcome == ConnectorRoutingOutcome.NoRoute));
                continue;
            }
            var canonicalEditablePath = edge.PersistentRoute.Length >= 2
                ? CreateEditableConnectorPath(
                    edge.PersistentRoute,
                    canonicalLogicalPath[0],
                    canonicalLogicalPath[^1])
                : ImmutableArray.Create(
                    canonicalLogicalPath[0],
                    canonicalLogicalPath[^1]);
            var displayedSourceAnchor = sourceRegion?.MapLocalToScene(
                    canonicalLogicalPath[0]) ??
                canonicalLogicalPath[0];
            var displayedTargetAnchor = targetRegion?.MapLocalToScene(
                    canonicalLogicalPath[^1]) ??
                canonicalLogicalPath[^1];
            var guidanceTransform = ResolveGuidanceTransform(
                sourceRegion,
                targetRegion,
                plan.CanonicalGuidanceToSceneTransform);
            var request = new Canvas2DConnectorPresentationRoutingRequest(
                edge,
                canonicalLogicalPath,
                canonicalEditablePath,
                displayedSourceAnchor,
                displayedTargetAnchor,
                sourceRegion,
                targetRegion,
                guidanceTransform,
                presentedObstacles,
                Canvas2DConnectorPathMetadata.IsNoRouteFallbackPath(connector));

            Canvas2DConnectorPresentationRoute? presentedRoute;
#pragma warning disable CA1031 // Registered presentation-router faults become diagnostics.
            try
            {
                presentedRoute = connectorRouter.Route(request);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.ContributorFailure,
                    $"Connector presentation routing failed for '{edge.Id}'.",
                    edge.Id.Value,
                    new KeyValuePair<string, string>(
                        "ExceptionType",
                        exception.GetType().FullName ?? exception.GetType().Name)));
                continue;
            }
#pragma warning restore CA1031

            if (!IsValidPresentedRoute(request, presentedRoute))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.InvalidContribution,
                    $"Connector presentation router returned invalid or non-reversible " +
                    $"geometry for '{edge.Id}'.",
                    edge.Id.Value));
                continue;
            }

            finalRoutes.Add(
                edge.Id,
                new PresentedConnector(
                    connector,
                    presentedRoute!,
                    isVisible,
                    request.IsNoRouteFallback));
        }

        if (HasErrors(diagnostics))
        {
            return;
        }

        ReplaceConnectorPresentations(
            graph,
            visualsById,
            labelsById,
            items,
            finalRoutes);
        HideCompleteConnectorFamilies(graph, items, finalRoutes);
    }

    private static void ValidateSpatialPlan(
        ProjectedGraph graph,
        Canvas2DSpatialPresentationPlan plan,
        Dictionary<ProjectedObjectId, IProjectedObject> projectedObjects,
        List<Diagnostic> diagnostics)
    {
        var currentVisualIds = projectedObjects.Values
            .Select(static projected => projected.Source.VisualStateId)
            .Where(static visualStateId => visualStateId is not null)
            .Cast<VisualStateId>()
            .ToHashSet();
        foreach (var placement in plan.VisualPlacements)
        {
            if (!currentVisualIds.Contains(placement.VisualStateId))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.InvalidContribution,
                    $"Spatial placement references Visual State '{placement.VisualStateId}' " +
                    "outside the current projected Process.",
                    placement.VisualStateId.Value));
            }
        }

        var primaryVisualIds = graph.Nodes
            .Select(static node => node.Source.VisualStateId)
            .Where(static visualStateId => visualStateId is not null)
            .Cast<VisualStateId>()
            .ToArray();
        if (primaryVisualIds.Length != primaryVisualIds.Distinct().Count())
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidContribution,
                "The current Process projection contains a duplicate primary Visual State.",
                graph.DocumentId.Value));
        }
    }

    private static bool TryResolveSpatialPlacement(
        Canvas2DSceneItem item,
        Dictionary<ProjectedObjectId, IProjectedObject> projectedObjects,
        Dictionary<ProjectedObjectId, ProjectedNode> nodesById,
        Dictionary<ProjectedObjectId, ProjectedPort> portsById,
        Canvas2DSpatialPresentationPlan plan,
        out Canvas2DSpatialVisualPlacement? placement,
        out Canvas2DSpatialRegion? region)
    {
        placement = null;
        region = null;
        if (item.Origin.ProjectedObjectId is not { } projectedObjectId ||
            !projectedObjects.TryGetValue(projectedObjectId, out var projectedObject))
        {
            return false;
        }

        VisualStateId? ownerVisualStateId = projectedObject switch
        {
            ProjectedLabel label when nodesById.TryGetValue(label.OwnerId, out var ownerNode) =>
                ownerNode.Source.VisualStateId,
            ProjectedLabel label when portsById.TryGetValue(label.OwnerId, out var ownerPort) &&
                nodesById.TryGetValue(ownerPort.OwnerNodeId, out var portOwnerNode) =>
                portOwnerNode.Source.VisualStateId,
            ProjectedLabel => null,
            ProjectedPort port when nodesById.TryGetValue(port.OwnerNodeId, out var ownerNode) =>
                ownerNode.Source.VisualStateId,
            _ => projectedObject.Source.VisualStateId,
        };
        return ownerVisualStateId is not null &&
            plan.TryGetPlacement(ownerVisualStateId, out placement) &&
            plan.TryGetRegion(placement.RegionId, out region);
    }

    private static Canvas2DSceneItem MaterializeSpatialItem(
        Canvas2DSceneItem item,
        Canvas2DSpatialVisualPlacement placement,
        Canvas2DSpatialRegion region)
    {
        var translation = new VectorD(
            region.LocalToSceneTransform.OffsetX,
            region.LocalToSceneTransform.OffsetY);
        return new Canvas2DSceneItem(
            item.Id,
            item.Layer,
            item.ZIndex,
            item.Geometry,
            item.Origin,
            item.Transform.Then(region.LocalToSceneTransform),
            item.Clip?.Translate(translation),
            item.Style,
            item.IsVisible && placement.IsVisible,
            item.HitTestPolicy,
            item.PersistentAppearance,
            item.Metadata,
            item.Bounds.Translate(translation),
            region,
            item.ConnectorPresentationMapping);
    }

    private static Canvas2DSceneItem MaterializeProcessLocalEditorOverlay(
        Canvas2DSceneItem item,
        Canvas2DSpatialRegion? region)
    {
        if (region is null)
        {
            return item;
        }

        var translation = new VectorD(
            region.LocalToSceneTransform.OffsetX,
            region.LocalToSceneTransform.OffsetY);
        return new Canvas2DSceneItem(
            item.Id,
            item.Layer,
            item.ZIndex,
            item.Geometry,
            item.Origin,
            item.Transform.Then(region.LocalToSceneTransform),
            item.Clip?.Translate(translation),
            item.Style,
            item.IsVisible,
            item.HitTestPolicy,
            item.PersistentAppearance,
            item.Metadata,
            item.Bounds.Translate(translation),
            region,
            item.ConnectorPresentationMapping);
    }

    private static void AssociateEditorOverlaysWithSpatialPresentation(
        List<Canvas2DSceneItem> items,
        int editorOverlayStartIndex)
    {
        var persistentItems = items.Take(editorOverlayStartIndex).ToArray();
        var persistentById = persistentItems.ToDictionary(static item => item.Id);
        for (var index = editorOverlayStartIndex; index < items.Count; index++)
        {
            var overlay = items[index];
            if ((overlay.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0)
            {
                continue;
            }

            var related = overlay.Origin.RelatedSceneObjectIds
                .Select(id => persistentById.TryGetValue(id, out var item) ? item : null)
                .Where(static item => item is not null)
                .Cast<Canvas2DSceneItem>()
                .Concat(overlay.Origin.VisualStateId is { } visualStateId
                    ? persistentItems.Where(item =>
                        item.Origin.VisualStateId == visualStateId)
                    : [])
                .ToArray();
            var relatedRegions = related
                .Select(static item => item.SpatialRegion)
                .Where(static candidate => candidate is not null)
                .Cast<Canvas2DSpatialRegion>()
                .Distinct()
                .Take(2)
                .ToArray();
            var relatedMappings = related
                .Select(static item => item.ConnectorPresentationMapping)
                .Where(static candidate => candidate is not null)
                .Cast<Canvas2DConnectorPresentationMapping>()
                .Distinct()
                .Take(2)
                .ToArray();
            var region = overlay.SpatialRegion ??
                (relatedRegions.Length == 1 ? relatedRegions[0] : null);
            var mapping = overlay.ConnectorPresentationMapping ??
                (relatedMappings.Length == 1 ? relatedMappings[0] : null);
            var isVisible = overlay.IsVisible && (related.Length == 0 || related.Any(static item => item.IsVisible));
            if (region is null && mapping is null && isVisible == overlay.IsVisible)
            {
                continue;
            }

            items[index] = new Canvas2DSceneItem(
                overlay.Id,
                overlay.Layer,
                overlay.ZIndex,
                overlay.Geometry,
                overlay.Origin,
                overlay.Transform,
                overlay.Clip,
                overlay.Style,
                isVisible,
                isVisible ? overlay.HitTestPolicy : Canvas2DHitTestPolicy.None,
                overlay.PersistentAppearance,
                overlay.Metadata,
                overlay.Bounds,
                region,
                mapping);
        }
    }

    private static Canvas2DSpatialVisualPlacement? ResolveNodePlacement(
        ProjectedObjectId nodeId,
        Dictionary<ProjectedObjectId, ProjectedNode> nodesById,
        Canvas2DSpatialPresentationPlan plan) =>
        nodesById.TryGetValue(nodeId, out var node) &&
        node.Source.VisualStateId is { } visualStateId &&
        plan.TryGetPlacement(visualStateId, out var placement)
            ? placement
            : null;

    private static Canvas2DSpatialRegion? ResolveRegion(
        Canvas2DSpatialVisualPlacement? placement,
        Canvas2DSpatialPresentationPlan plan) =>
        placement is not null && plan.TryGetRegion(placement.RegionId, out var region)
            ? region
            : null;

    private static Matrix2D ResolveGuidanceTransform(
        Canvas2DSpatialRegion? sourceRegion,
        Canvas2DSpatialRegion? targetRegion,
        Matrix2D crossRegionGuidanceTransform) =>
        sourceRegion is null && targetRegion is null
            ? Matrix2D.Identity
            : sourceRegion is not null && targetRegion is not null &&
              sourceRegion.Id == targetRegion.Id
                ? sourceRegion.LocalToSceneTransform
                : crossRegionGuidanceTransform;

    private static bool IsValidPresentedRoute(
        Canvas2DConnectorPresentationRoutingRequest request,
        Canvas2DConnectorPresentationRoute? route) =>
        route is not null &&
        route.DisplayedLogicalPath[0] == request.DisplayedSourceAnchor &&
        route.DisplayedLogicalPath[^1] == request.DisplayedTargetAnchor &&
        route.Mapping.CanonicalEditablePath.AsSpan().SequenceEqual(
            request.CanonicalEditablePath.AsSpan()) &&
        route.Mapping.DisplayedEditablePath[0] == request.DisplayedSourceAnchor &&
        route.Mapping.DisplayedEditablePath[^1] == request.DisplayedTargetAnchor &&
        route.Mapping.CanonicalGuidanceToSceneTransform ==
            request.CanonicalGuidanceToSceneTransform &&
        route.Mapping.SourceRegionId == request.SourceRegion?.Id &&
        route.Mapping.TargetRegionId == request.TargetRegion?.Id;

    private static void ReplaceConnectorPresentations(
        ProjectedGraph graph,
        IReadOnlyDictionary<VisualStateId, VisualStateSnapshot> visualsById,
        IReadOnlyDictionary<ProjectedObjectId, ProjectedLabel> labelsById,
        List<Canvas2DSceneItem> items,
        IReadOnlyDictionary<ProjectedObjectId, PresentedConnector> finalRoutes)
    {
        var edgeIds = graph.Edges.Select(static edge => edge.Id).ToHashSet();
        items.RemoveAll(item =>
            item.Origin.ProjectedObjectId is { } projectedId &&
            edgeIds.Contains(projectedId) &&
            IsFrameworkDerivedConnectorItem(item));

        foreach (var entry in finalRoutes.OrderBy(static entry => entry.Key.Value,
                     StringComparer.Ordinal))
        {
            var edge = graph.Edges.Single(candidate => candidate.Id == entry.Key);
            var presented = entry.Value;
            var route = presented.Route;
            var crossingPaths = finalRoutes
                .Where(candidate =>
                    candidate.Key != entry.Key &&
                    candidate.Value.IsVisible)
                .Select(static candidate =>
                    (IReadOnlyList<PointD>)candidate.Value.Route.DisplayedLogicalPath);
            var lineJumps = presented.IsVisible && !presented.IsNoRouteFallback
                ? Canvas2DConnectorLineJumpGeometry.CreatePresentation(
                    route.DisplayedLogicalPath,
                    crossingPaths)
                : new Canvas2DConnectorLineJumpPresentation(
                    route.DisplayedLogicalPath,
                    []);
            var connector = presented.CanonicalItem;
            var connectorId = connector.Id;
            for (var index = 0; index < lineJumps.JumpPaths.Length; index++)
            {
                var jumpPath = lineJumps.JumpPaths[index];
                var maskKey = $"connector-line-jump-mask:{index}";
                items.Add(new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForProjected(edge.Id, maskKey),
                    Canvas2DSceneLayer.Connector,
                    ConnectorLineJumpMaskZIndex,
                    Canvas2DSceneGeometry.Path(jumpPath),
                    CreateDerivedConnectorOrigin(connector.Origin, maskKey, connectorId),
                    style: new Canvas2DSceneStyle(
                        stroke: "#ffffff",
                        strokeWidth: ConnectorLineJumpMaskStrokeWidth),
                    isVisible: presented.IsVisible,
                    hitTestPolicy: Canvas2DHitTestPolicy.None,
                    connectorPresentationMapping: route.Mapping));

                var jumpKey = $"connector-line-jump:{index}";
                items.Add(new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForProjected(edge.Id, jumpKey),
                    Canvas2DSceneLayer.Connector,
                    ConnectorLineJumpZIndex,
                    Canvas2DSceneGeometry.Path(jumpPath),
                    CreateDerivedConnectorOrigin(connector.Origin, jumpKey, connectorId),
                    style: connector.Style,
                    isVisible: presented.IsVisible,
                    hitTestPolicy: connector.HitTestPolicy,
                    persistentAppearance: connector.PersistentAppearance,
                    metadata: Canvas2DConnectorLineJumpMetadata.Create(connectorId),
                    connectorPresentationMapping: route.Mapping));
            }

            ReplaceItem(
                items,
                connector.Id,
                new Canvas2DSceneItem(
                    connector.Id,
                    connector.Layer,
                    connector.ZIndex,
                    Canvas2DSceneGeometry.Path(lineJumps.DisplayPath),
                    connector.Origin,
                    style: connector.Style,
                    isVisible: presented.IsVisible,
                    hitTestPolicy: connector.HitTestPolicy,
                    persistentAppearance: connector.PersistentAppearance,
                    metadata: CreatePresentedConnectorMetadata(
                        connector.Metadata,
                        route.DisplayedLogicalPath,
                        route.Mapping.DisplayedEditablePath,
                        presented.IsNoRouteFallback),
                    connectorPresentationMapping: route.Mapping));

            var targetArrow = Canvas2DConnectorArrowGeometry.Create(
                route.DisplayedLogicalPath);
            if (targetArrow is not null)
            {
                items.Add(new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForProjected(
                        edge.Id,
                        "connector-target-arrow"),
                    Canvas2DSceneLayer.Connector,
                    ConnectorBaseTargetArrowZIndex,
                    targetArrow,
                    connector.Origin,
                    style: new Canvas2DSceneStyle("#000000", "#000000"),
                    isVisible: presented.IsVisible,
                    hitTestPolicy: new Canvas2DHitTestPolicy(
                        Canvas2DHitTestMode.FillOrStroke,
                        1d),
                    persistentAppearance: connector.PersistentAppearance,
                    metadata:
                    [
                        new KeyValuePair<string, PropertyValue>(
                            Canvas2DConnectorArrowMetadata.TargetArrow,
                            Canvas2DConnectorArrowMetadata.TargetArrowValue),
                    ],
                    connectorPresentationMapping: route.Mapping));
            }

            RepositionConnectorLabels(
                edge,
                visualsById,
                labelsById,
                items,
                presented);
        }
    }

    private static void RepositionConnectorLabels(
        ProjectedEdge edge,
        IReadOnlyDictionary<VisualStateId, VisualStateSnapshot> visualsById,
        IReadOnlyDictionary<ProjectedObjectId, ProjectedLabel> labelsById,
        List<Canvas2DSceneItem> items,
        PresentedConnector presented)
    {
        ConnectorLabelPlacement? placement = null;
        if (edge.Source.VisualStateId is { } connectorVisualStateId &&
            visualsById.TryGetValue(connectorVisualStateId, out var connectorVisual))
        {
            ConnectorLabelPlacement.TryRead(connectorVisual.Properties, out placement);
        }

        var translations = new Dictionary<ProjectedObjectId, VectorD>();
        foreach (var projectedLabel in labelsById.Values.Where(label => label.OwnerId == edge.Id))
        {
            var lines = items.Where(item =>
                item.Origin.ProjectedObjectId == projectedLabel.Id).ToArray();
            if (lines.Length == 0)
            {
                continue;
            }

            var bounds = Canvas2DConnectorLabelResolver.PresentedBounds(lines);
            var currentAnchor = new PointD(
                bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));
            var displayedAnchor = Canvas2DConnectorLabelResolver.Resolve(
                projectedLabel, presented.Route.DisplayedLogicalPath, placement, bounds.Size);
            translations.Add(projectedLabel.Id, displayedAnchor - currentAnchor);
        }
        for (var index = 0; index < items.Count; index++)
        {
            var label = items[index];
            if (label.Origin.ProjectedObjectId is not { } labelId ||
                !translations.TryGetValue(labelId, out var translation))
            {
                continue;
            }

            var transform = label.Transform.Then(Matrix2D.CreateTranslation(translation));
            var translatedBounds = label.Bounds.Translate(translation);
            if (TryCalculateTransformedBounds(label.Geometry.Bounds, transform, out var geometryBounds))
            {
                var left = Math.Min(translatedBounds.Left, geometryBounds.Left);
                var top = Math.Min(translatedBounds.Top, geometryBounds.Top);
                translatedBounds = new RectD(left, top,
                    Math.Max(translatedBounds.Right, geometryBounds.Right) - left,
                    Math.Max(translatedBounds.Bottom, geometryBounds.Bottom) - top);
            }
            items[index] = new Canvas2DSceneItem(
                label.Id,
                label.Layer,
                label.ZIndex,
                label.Geometry,
                label.Origin,
                transform,
                label.Clip?.Translate(translation),
                label.Style,
                label.IsVisible && presented.IsVisible,
                label.HitTestPolicy,
                label.PersistentAppearance,
                label.Metadata,
                translatedBounds,
                connectorPresentationMapping: presented.Route.Mapping);
        }
    }

    private static IEnumerable<KeyValuePair<string, PropertyValue>>
        CreatePresentedConnectorMetadata(
            PropertyMap canonicalMetadata,
            IReadOnlyList<PointD> displayedLogicalPath,
            IReadOnlyList<PointD> displayedEditablePath,
            bool isNoRouteFallback)
    {
        foreach (var entry in canonicalMetadata)
        {
            if (!Canvas2DConnectorPathMetadata.IsReservedKey(entry.Key))
            {
                yield return entry;
            }
        }

        foreach (var entry in Canvas2DConnectorPathMetadata.CreateProperties(
                     displayedLogicalPath,
                     displayedEditablePath,
                     isNoRouteFallback))
        {
            yield return entry;
        }
    }

    private static bool IsFrameworkConnectorFamilyItem(
        Canvas2DSceneItem item,
        Dictionary<ProjectedObjectId, ProjectedEdge> edgesById) =>
        item.Origin.ProjectedObjectId is { } projectedId &&
        edgesById.ContainsKey(projectedId) &&
        (item.Id == Canvas2DSceneObjectIdentity.ForProjected(projectedId, "connector") ||
         IsFrameworkDerivedConnectorItem(item));

    private static bool IsFrameworkDerivedConnectorItem(Canvas2DSceneItem item) =>
        item.Origin.ProjectedObjectId is { } projectedId &&
        (item.Id == Canvas2DSceneObjectIdentity.ForProjected(
             projectedId,
             "connector-target-arrow") ||
         item.Origin.StableSourceKey?.StartsWith(
             "connector-line-jump",
             StringComparison.Ordinal) == true);

    private static void ReplaceItem(
        List<Canvas2DSceneItem> items,
        SceneObjectId id,
        Canvas2DSceneItem replacement)
    {
        var index = items.FindIndex(item => item.Id == id);
        if (index < 0)
        {
            throw new InvalidOperationException($"Scene item '{id}' is missing.");
        }

        items[index] = replacement;
    }

    private sealed record PresentedConnector(
        Canvas2DSceneItem CanonicalItem,
        Canvas2DConnectorPresentationRoute Route,
        bool IsVisible,
        bool IsNoRouteFallback);

    private static void HideCompleteConnectorFamilies(
        ProjectedGraph graph,
        List<Canvas2DSceneItem> items,
        IReadOnlyDictionary<ProjectedObjectId, PresentedConnector> routes)
    {
        var hiddenEdges = routes.Where(static entry => !entry.Value.IsVisible)
            .Select(static entry => entry.Key).ToHashSet();
        var hiddenProjected = graph.Labels.Where(label => hiddenEdges.Contains(label.OwnerId))
            .Select(static label => label.Id).Concat(hiddenEdges).ToHashSet();
        var hiddenItems = items.Where(item => item.Origin.ProjectedObjectId is { } id && hiddenProjected.Contains(id))
            .Select(static item => item.Id).ToHashSet();
        bool added;
        do
        {
            added = false;
            foreach (var item in items)
                if (item.Origin.RelatedSceneObjectIds.Any(hiddenItems.Contains))
                    added |= hiddenItems.Add(item.Id);
        }
        while (added);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (!hiddenItems.Contains(item.Id)) continue;
            items[index] = new Canvas2DSceneItem(item.Id, item.Layer, item.ZIndex, item.Geometry,
                item.Origin, item.Transform, item.Clip, item.Style, isVisible: false,
                Canvas2DHitTestPolicy.None, item.PersistentAppearance, item.Metadata, item.Bounds,
                item.SpatialRegion, item.ConnectorPresentationMapping);
        }
    }
}
