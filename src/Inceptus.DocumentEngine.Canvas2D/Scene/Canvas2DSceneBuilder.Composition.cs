using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Canvas2D.Interaction;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    private const string EditorKindKey = "inceptus.canvas2d:editor-kind";
    private const int ConnectorBaseZIndex = 0;
    private const int ConnectorSelectionZIndex = 10;
    private const int ConnectorHoverZIndex = 11;
    private const int ConnectorLineJumpMaskZIndex = 20;
    private const int ConnectorLineJumpZIndex = 21;
    private const int ConnectorLineJumpSelectionZIndex = 22;
    private const int ConnectorLineJumpHoverZIndex = 23;
    private const int ConnectorBaseTargetArrowZIndex = 1;
    private const double ConnectorLineJumpMaskStrokeWidth = 3d;

    private static void ComposePipelineItems(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingResult routing,
        VisualModelSnapshot visualModel,
        List<Canvas2DSceneItem> items,
        IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredNodeLabel>? measuredNodeLabels,
        IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredConnectorLabel>?
            measuredConnectorLabels)
    {
        var visualsById = visualModel.VisualStates.ToDictionary(static visual => visual.Id);
        var nodeGeometry = layout.Nodes.ToDictionary(static geometry => geometry.ProjectedObjectId);
        var groupGeometry = layout.Groups.ToDictionary(static geometry => geometry.ProjectedObjectId);
        var routes = routing.Routes.ToDictionary(static route => route.ProjectedEdgeId);
        var noRouteEdgeIds = routing.NoRouteEdgeIds.ToHashSet();
        var portsById = graph.Ports.ToDictionary(static port => port.Id);

        foreach (var group in graph.Groups)
        {
            var geometry = groupGeometry[group.Id];
            items.Add(new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForProjected(group.Id, "group"),
                Canvas2DSceneLayer.Content,
                -100,
                Canvas2DSceneGeometry.Rectangle(new RectD(
                    0d,
                    0d,
                    geometry.Bounds.Width,
                    geometry.Bounds.Height)),
                CreateOrigin(group, group.MemberNodeIds),
                transform: Matrix2D.CreateTranslation(geometry.Bounds.X, geometry.Bounds.Y),
                style: new Canvas2DSceneStyle(stroke: "#808080"),
                hitTestPolicy: new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Bounds),
                persistentAppearance: GetPersistentAppearance(group, visualsById),
                bounds: geometry.Bounds));
        }

        foreach (var node in graph.Nodes)
        {
            var geometry = nodeGeometry[node.Id];
            var isAttachedFixedSize = node.GeometryInteractionPolicy ==
                NodeGeometryInteractionPolicy.AttachedBoundaryMoveFixedSize;
            items.Add(new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node"),
                Canvas2DSceneLayer.Content,
                isAttachedFixedSize ? 1 : 0,
                Canvas2DSceneGeometry.Rectangle(new RectD(
                    0d,
                    0d,
                    geometry.Bounds.Width,
                    geometry.Bounds.Height)),
                CreateOrigin(node),
                transform: geometry.Transform,
                style: new Canvas2DSceneStyle("#ffffff", "#000000"),
                hitTestPolicy: new Canvas2DHitTestPolicy(Canvas2DHitTestMode.FillOrStroke),
                persistentAppearance: GetPersistentAppearance(node, visualsById),
                metadata: isAttachedFixedSize
                    ? Canvas2DNodeBodyMetadata.AttachedMoveAndNodeBodyCapability
                    : Canvas2DNodeBodyMetadata.MoveResizeAndNodeBodyCapability,
                bounds: geometry.Bounds));
        }

        foreach (var edge in graph.Edges)
        {
            var isRouted = routes.TryGetValue(edge.Id, out var route);
            var isNoRouteFallback = !isRouted && noRouteEdgeIds.Contains(edge.Id);
            if (!isRouted && !isNoRouteFallback)
            {
                continue;
            }

            var logicalPath = isRouted
                ? route!.Path
                : CreateNoRouteFallbackPath(edge, nodeGeometry, portsById);
            var sourceAnchor = logicalPath[0];
            var targetAnchor = logicalPath[^1];

            var relatedIds = new List<ProjectedObjectId>
            {
                edge.SourceNodeId,
                edge.TargetNodeId,
            };
            if (edge.SourcePortId is not null)
            {
                relatedIds.Add(edge.SourcePortId);
            }

            if (edge.TargetPortId is not null)
            {
                relatedIds.Add(edge.TargetPortId);
            }

            var origin = CreateOrigin(edge, relatedIds);
            var persistentAppearance = GetPersistentAppearance(edge, visualsById);
            var lineJumpPresentation = isRouted
                ? Canvas2DConnectorLineJumpGeometry.CreatePresentation(
                    logicalPath,
                    routes
                        .Where(candidate => candidate.Key != edge.Id)
                        .Select(static candidate =>
                            (IReadOnlyList<PointD>)candidate.Value.Path))
                : new Canvas2DConnectorLineJumpPresentation(logicalPath, []);
            var connectorId = Canvas2DSceneObjectIdentity.ForProjected(
                edge.Id,
                "connector");
            for (var jumpIndex = 0;
                 jumpIndex < lineJumpPresentation.JumpPaths.Length;
                 jumpIndex++)
            {
                var jumpPath = lineJumpPresentation.JumpPaths[jumpIndex];
                var stableKey = $"connector-line-jump-mask:{jumpIndex}";
                items.Add(new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForProjected(edge.Id, stableKey),
                    Canvas2DSceneLayer.Connector,
                    ConnectorLineJumpMaskZIndex,
                    Canvas2DSceneGeometry.Path(jumpPath),
                    CreateDerivedConnectorOrigin(origin, stableKey, connectorId),
                    style: new Canvas2DSceneStyle(
                        stroke: "#ffffff",
                        strokeWidth: ConnectorLineJumpMaskStrokeWidth),
                    hitTestPolicy: Canvas2DHitTestPolicy.None));

                stableKey = $"connector-line-jump:{jumpIndex}";
                items.Add(new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForProjected(edge.Id, stableKey),
                    Canvas2DSceneLayer.Connector,
                    ConnectorLineJumpZIndex,
                    Canvas2DSceneGeometry.Path(jumpPath),
                    CreateDerivedConnectorOrigin(origin, stableKey, connectorId),
                    style: new Canvas2DSceneStyle(stroke: "#000000"),
                    hitTestPolicy: new Canvas2DHitTestPolicy(
                        Canvas2DHitTestMode.Stroke,
                        Canvas2DConnectorInteractionConfiguration.Default.PathHitTolerance),
                    persistentAppearance: persistentAppearance,
                    metadata: Canvas2DConnectorLineJumpMetadata.Create(connectorId)));
            }

            items.Add(new Canvas2DSceneItem(
                connectorId,
                Canvas2DSceneLayer.Connector,
                ConnectorBaseZIndex,
                Canvas2DSceneGeometry.Path(lineJumpPresentation.DisplayPath),
                origin,
                style: new Canvas2DSceneStyle(stroke: "#000000"),
                hitTestPolicy: new Canvas2DHitTestPolicy(
                    Canvas2DHitTestMode.Stroke,
                    Canvas2DConnectorInteractionConfiguration.Default.PathHitTolerance),
                persistentAppearance: persistentAppearance,
                metadata: GetConnectorMetadata(
                    edge,
                    logicalPath,
                    sourceAnchor,
                    targetAnchor,
                    route?.Metadata,
                    isNoRouteFallback)));

            var targetArrow = Canvas2DConnectorArrowGeometry.Create(logicalPath);
            if (targetArrow is not null)
            {
                items.Add(new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForProjected(
                        edge.Id,
                        "connector-target-arrow"),
                    Canvas2DSceneLayer.Connector,
                    ConnectorBaseTargetArrowZIndex,
                    targetArrow,
                    origin,
                    style: new Canvas2DSceneStyle("#000000", "#000000"),
                    hitTestPolicy: new Canvas2DHitTestPolicy(
                        Canvas2DHitTestMode.FillOrStroke,
                        1d),
                    persistentAppearance: persistentAppearance,
                    metadata:
                    [
                        new KeyValuePair<string, PropertyValue>(
                            Canvas2DConnectorArrowMetadata.TargetArrow,
                            Canvas2DConnectorArrowMetadata.TargetArrowValue),
                    ]));
            }
        }

        foreach (var label in graph.Labels)
        {
            if (noRouteEdgeIds.Contains(label.OwnerId))
            {
                continue;
            }

            var persistentAppearance = new PropertyMap(
                GetPersistentAppearance(label, visualsById));
            if (measuredNodeLabels is not null &&
                measuredNodeLabels.TryGetValue(label.Id, out var measuredNodeLabel))
            {
                items.AddRange(CreateMeasuredNodeLabelItems(
                    measuredNodeLabel,
                    measuredNodeLabel.Current,
                    persistentAppearance));
                continue;
            }

            if (measuredConnectorLabels is not null &&
                measuredConnectorLabels.TryGetValue(label.Id, out var measuredConnectorLabel))
            {
                items.AddRange(CreateMeasuredConnectorLabelItems(
                    measuredConnectorLabel,
                    Canvas2DSceneObjectIdentity.ForProjected(label.OwnerId, "connector"),
                    persistentAppearance));
                continue;
            }

            if (routes.TryGetValue(label.OwnerId, out var connectorRoute))
            {
                var placement = label.Source.VisualStateId is { } connectorVisualStateId &&
                    visualsById.TryGetValue(connectorVisualStateId, out var connectorVisual)
                        ? ConnectorLabelPlacement.Resolve(connectorVisual.Properties)
                        : ConnectorLabelPlacement.Default;
                items.Add(CreateUnmeasuredConnectorLabelItem(
                    label,
                    connectorRoute,
                    placement,
                    Canvas2DSceneObjectIdentity.ForProjected(label.OwnerId, "connector"),
                    persistentAppearance));
                continue;
            }

            var (bounds, transform) = ResolveLabelGeometry(
                label,
                graph,
                nodeGeometry,
                groupGeometry,
                routes,
                portsById);
            var localBounds = new RectD(0d, 0d, bounds.Width, bounds.Height);
            var isNodeLabel = nodeGeometry.TryGetValue(label.OwnerId, out var ownerNode);
            if (ownerNode is not null &&
                NodeLabelVisualOverride.TryRead(
                    persistentAppearance,
                    out var manualOverride))
            {
                var manualItem = CreateUnmeasuredManualNodeLabelItem(
                    label,
                    ownerNode,
                    manualOverride!,
                    persistentAppearance);
                items.Add(manualItem);
                if (label.NodeInteractionPolicy ==
                    NodeLabelInteractionPolicy.MoveAndResize &&
                    label.Source.VisualStateId is not null)
                {
                    items.Add(CreateNodeLabelInteractionBox(
                        label,
                        manualItem.Bounds,
                        manualItem.Clip ?? manualItem.Bounds,
                        persistentAppearance));
                }

                continue;
            }

            if (ownerNode is not null &&
                label.NodePlacement?.Kind == NodeLabelPlacementKind.OutsideBelow)
            {
                var outsideItem = CreateUnmeasuredOutsideNodeLabelItem(
                    label,
                    ownerNode,
                    persistentAppearance);
                items.Add(outsideItem);
                if (label.NodeInteractionPolicy ==
                    NodeLabelInteractionPolicy.MoveAndResize &&
                    label.Source.VisualStateId is not null)
                {
                    items.Add(CreateNodeLabelInteractionBox(
                        label,
                        outsideItem.Bounds,
                        outsideItem.Clip ?? outsideItem.Bounds,
                        persistentAppearance));
                }

                continue;
            }

            var textAnchor = new PointD(localBounds.Width / 2d, localBounds.Height / 2d);
            var textGeometry = isNodeLabel
                ? Canvas2DSceneGeometry.Text(
                    localBounds,
                    label.Text,
                    textAnchor,
                    Canvas2DTextAlignment.Center,
                    Canvas2DTextBaseline.Middle)
                : Canvas2DSceneGeometry.Text(localBounds, label.Text);
            if (ownerNode is not null)
            {
                transform = CreateAutomaticNodeLabelTransform(ownerNode, textAnchor);
            }

            var nodeInteractionCapable = isNodeLabel &&
                label.NodeInteractionPolicy == NodeLabelInteractionPolicy.MoveAndResize &&
                label.Source.VisualStateId is not null;
            var labelItem = new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label"),
                Canvas2DSceneLayer.Label,
                0,
                textGeometry,
                CreateOrigin(label, [label.OwnerId]),
                transform: transform,
                style: new Canvas2DSceneStyle(fill: "#000000"),
                hitTestPolicy: nodeInteractionCapable
                    ? Canvas2DHitTestPolicy.None
                    : new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Bounds),
                persistentAppearance: persistentAppearance,
                metadata: isNodeLabel && !nodeInteractionCapable
                    ? Canvas2DResizeGestureMetadata.MoveAndResizeCapability
                    : null,
                bounds: bounds);
            items.Add(labelItem);
            if (nodeInteractionCapable)
            {
                items.Add(CreateNodeLabelInteractionBox(
                    label,
                    labelItem.Bounds,
                    labelItem.Bounds,
                    persistentAppearance));
            }
        }
    }

    private static void ComposeEditorOverlays(
        EditorStateSnapshot editorState,
        ProjectedGraph graph,
        VisualModelSnapshot visualModel,
        List<Canvas2DSceneItem> items,
        List<Diagnostic> diagnostics,
        IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredNodeLabel>? measuredLabels,
        IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredConnectorLabel>?
            measuredConnectorLabels)
    {
        ComposeDocumentBoundaryGuides(editorState, items);

        if (editorState.ActiveGesture is { } activeGesture &&
            StringComparer.Ordinal.Equals(
                activeGesture.Kind,
                Canvas2DConnectorEndpointReconnectionGestureMetadata.Kind) &&
            Canvas2DConnectorEndpointReconnectionGestureMetadata.TryRead(
                activeGesture.Properties,
                out var activeReconnection) &&
            activeReconnection?.EndpointKind == ConnectorEndpointKind.Target)
        {
            items.RemoveAll(item =>
                item.Layer == Canvas2DSceneLayer.Connector &&
                item.Origin.VisualStateId == activeReconnection.ConnectorVisualStateId &&
                item.Metadata.TryGetValue(
                    Canvas2DConnectorArrowMetadata.TargetArrow,
                    out var targetArrow) &&
                targetArrow.Kind == PropertyValueKind.Boolean &&
                targetArrow.BooleanValue);
        }

        var itemsById = new Dictionary<SceneObjectId, Canvas2DSceneItem>();
        foreach (var item in items)
        {
            if (item is not null && item.Id is not null && !itemsById.TryAdd(item.Id, item))
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.DuplicateSceneObjectId,
                    $"Scene object ID '{item.Id}' occurs more than once.",
                    item.Id.Value));
            }
        }

        if (HasErrors(diagnostics))
        {
            return;
        }

        var persistentItems = items.ToArray();
        var hasLineJumps = persistentItems.Any(
            Canvas2DConnectorLineJumpMetadata.HasHitTarget);
        var visualStatesById = visualModel.VisualStates.ToDictionary(static visual => visual.Id);
        var connectorAnchorsByNode = graph.Ports
            .Select(static port => new
            {
                Port = port,
                Anchor = ProjectedConnectorAnchorMetadata.TryDecode(port, out var anchor)
                    ? anchor
                    : null,
            })
            .Where(static candidate => candidate.Anchor is not null)
            .GroupBy(static candidate => candidate.Port.OwnerNodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static candidate => candidate.Anchor!)
                    .OrderBy(static anchor => anchor.Side)
                    .ThenBy(static anchor => anchor.Order)
                    .ThenBy(static anchor => anchor.Id.Value, StringComparer.Ordinal)
                    .ToArray());
        var referencedAnchorIds = ConnectorAnchorOccupancy
            .EnumerateEndpointReferences(visualModel)
            .ToHashSet();

        for (var index = 0; index < editorState.Selection.Length; index++)
        {
            var visualStateId = editorState.Selection[index];
            var target = ResolveLogicalSelectionTarget(persistentItems, visualStateId);
            if (target is null)
            {
                diagnostics.Add(Warning(
                    Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                    $"Selected Visual State '{visualStateId}' is not selectable in this scene.",
                    visualStateId.Value));
                continue;
            }

            items.AddRange(CreateTargetOverlays(
                target,
                "selection",
                index,
                "#2563eb",
                hasLineJumps,
                persistentItems));

            if (visualStatesById.TryGetValue(visualStateId, out var visualState))
            {
                var resolvedAnchors = target.Origin.ProjectedObjectId is { } nodeId &&
                    connectorAnchorsByNode.TryGetValue(nodeId, out var nodeAnchors)
                        ? nodeAnchors
                        : [];
                foreach (var anchorHandle in CreateConnectorAnchorHandles(
                             target,
                             visualState,
                             resolvedAnchors,
                             referencedAnchorIds,
                             editorState.ActiveGesture))
                {
                    items.Add(anchorHandle);
                    itemsById.Add(anchorHandle.Id, anchorHandle);
                }
            }

            foreach (var endpointHandle in CreateConnectorEndpointHandles(
                         target,
                         editorState.ActiveGesture))
            {
                items.Add(endpointHandle);
                itemsById.Add(endpointHandle.Id, endpointHandle);
            }

            if (editorState.Selection.Length != 1)
            {
                continue;
            }

            foreach (var resizeHandle in CreateResizeHandles(target, editorState.ActiveGesture))
            {
                items.Add(resizeHandle);
                itemsById.Add(resizeHandle.Id, resizeHandle);
            }

            foreach (var labelResizeZone in CreateNodeLabelResizeZones(
                         target,
                         persistentItems,
                         editorState.ActiveGesture))
            {
                items.Add(labelResizeZone);
                itemsById.Add(labelResizeZone.Id, labelResizeZone);
            }

            foreach (var routeHandle in CreateRouteBendHandles(
                         target,
                         editorState.ActiveGesture))
            {
                items.Add(routeHandle);
                itemsById.Add(routeHandle.Id, routeHandle);
            }
        }

        if (editorState.ActiveGesture is { } connectionGesture &&
            StringComparer.Ordinal.Equals(
                connectionGesture.Kind,
                Canvas2DAnchorConnectionGestureMetadata.Kind) &&
            Canvas2DAnchorConnectionGestureMetadata.TryRead(
                connectionGesture.Properties,
                out _))
        {
            foreach (var visualState in visualStatesById.Values
                         .Where(visual => !editorState.Selection.Contains(visual.Id))
                         .OrderBy(static visual => visual.Id.Value, StringComparer.Ordinal))
            {
                var target = ResolveLogicalSelectionTarget(persistentItems, visualState.Id);
                if (target is null ||
                    target.Layer != Canvas2DSceneLayer.Content ||
                    target.Origin.SemanticElementId != visualState.SemanticElementId ||
                    target.Origin.ProjectedObjectId is not { } nodeId ||
                    !connectorAnchorsByNode.TryGetValue(nodeId, out var nodeAnchors))
                {
                    continue;
                }

                var targetAnchors = ExistingDynamicAnchors(
                    visualState,
                    nodeAnchors,
                    ConnectorAnchorRole.Target);
                foreach (var anchorHandle in CreateConnectorAnchorHandles(
                             target,
                             visualState,
                             targetAnchors,
                             referencedAnchorIds,
                             activeGesture: null,
                             isConnectionTargetCandidate: true))
                {
                    items.Add(anchorHandle);
                    itemsById.Add(anchorHandle.Id, anchorHandle);
                }
            }
        }

        if (editorState.ActiveGesture is { } reconnectionGesture &&
            StringComparer.Ordinal.Equals(
                reconnectionGesture.Kind,
                Canvas2DConnectorEndpointReconnectionGestureMetadata.Kind) &&
            Canvas2DConnectorEndpointReconnectionGestureMetadata.TryRead(
                reconnectionGesture.Properties,
                out var reconnection) &&
            reconnection is not null)
        {
            var requiredRole = reconnection.EndpointKind == ConnectorEndpointKind.Source
                ? ConnectorAnchorRole.Source
                : ConnectorAnchorRole.Target;
            foreach (var visualState in visualStatesById.Values
                         .Where(visual => !editorState.Selection.Contains(visual.Id))
                         .OrderBy(static visual => visual.Id.Value, StringComparer.Ordinal))
            {
                var target = ResolveLogicalSelectionTarget(persistentItems, visualState.Id);
                if (target is null ||
                    target.Layer != Canvas2DSceneLayer.Content ||
                    target.Origin.SemanticElementId != visualState.SemanticElementId ||
                    target.Origin.ProjectedObjectId is not { } nodeId ||
                    !connectorAnchorsByNode.TryGetValue(nodeId, out var nodeAnchors))
                {
                    continue;
                }

                var availableAnchors = ExistingDynamicAnchors(
                        visualState,
                        nodeAnchors,
                        requiredRole)
                    .Where(anchor => !ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
                        visualModel,
                        anchor.Id,
                        reconnection.ConnectorVisualStateId,
                        reconnection.EndpointKind))
                    .ToArray();
                foreach (var anchorHandle in CreateConnectorAnchorHandles(
                             target,
                             visualState,
                             availableAnchors,
                             referencedAnchorIds,
                             activeGesture: null,
                             isConnectionTargetCandidate: true,
                             referencedCandidateAnchorExemption: reconnection.OriginalAnchorId))
                {
                    items.Add(anchorHandle);
                    itemsById.Add(anchorHandle.Id, anchorHandle);
                }
            }
        }

        if (editorState.HoveredObjectId is not null)
        {
            if (itemsById.TryGetValue(editorState.HoveredObjectId, out var hovered))
            {
                var hoverItems = items.ToArray();
                hovered = ResolveConnectorHoverTarget(hovered, hoverItems);
                if (!IsResizeInteractionRegion(hovered))
                {
                    items.AddRange(CreateTargetOverlays(
                        hovered,
                        "hover",
                        2000,
                        "#f59e0b",
                        hasLineJumps,
                        hoverItems));
                }
            }
            else
            {
                diagnostics.Add(Warning(
                    Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                    $"Hovered scene object '{editorState.HoveredObjectId}' is not present in this scene.",
                    editorState.HoveredObjectId.Value));
            }
        }

        if (editorState.ActiveGesture is not null)
        {
            var gesture = editorState.ActiveGesture;
            if (!ComposeConnectorEndpointReconnectionGesturePreview(
                    gesture,
                    persistentItems,
                    items,
                    diagnostics) &&
                !ComposeAnchorConnectionGesturePreview(gesture, items, diagnostics) &&
                !ComposeMoveGesturePreview(gesture, persistentItems, items, diagnostics) &&
                !ComposeResizeGesturePreview(
                    gesture,
                    graph,
                    persistentItems,
                    items,
                    diagnostics,
                    measuredLabels) &&
                !ComposeRouteGesturePreview(
                    gesture,
                    persistentItems,
                    items,
                    diagnostics,
                    measuredConnectorLabels) &&
                !ComposeNodeLabelGesturePreview(
                    gesture,
                    graph,
                    persistentItems,
                    items,
                    diagnostics,
                    measuredLabels) &&
                !ComposeConnectorLabelGesturePreview(
                    gesture,
                    persistentItems,
                    items,
                    diagnostics))
            {
                items.Add(new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForEditorState($"gesture:{gesture.Id}"),
                    Canvas2DSceneLayer.Overlay,
                    3000,
                    Canvas2DSceneGeometry.Path([gesture.Origin, gesture.Current]),
                    new Canvas2DSceneOriginTrace(
                        Canvas2DSceneOriginCategory.EditorState,
                        stableSourceKey: $"gesture:{gesture.Id}"),
                    style: new Canvas2DSceneStyle(stroke: "#2563eb", dashPattern: [4d, 2d]),
                    hitTestPolicy: Canvas2DHitTestPolicy.None,
                    metadata: WithEditorKind(gesture.Kind, gesture.Properties)));
            }
        }

        for (var index = 0; index < editorState.TemporaryFeedback.Length; index++)
        {
            var feedback = editorState.TemporaryFeedback[index];
            if (feedback.PresentationMode == EditorFeedbackPresentationMode.ContributorOnly)
            {
                continue;
            }

            Canvas2DSceneGeometry? geometry = null;
            if (feedback.Points.Length >= 2)
            {
                geometry = Canvas2DSceneGeometry.Path(feedback.Points);
            }
            else if (feedback.Bounds is not null)
            {
                geometry = Canvas2DSceneGeometry.Rectangle(feedback.Bounds.Value);
            }
            else if (feedback.Points.Length == 1)
            {
                var point = feedback.Points[0];
                geometry = Canvas2DSceneGeometry.Ellipse(new RectD(point.X, point.Y, 0d, 0d));
            }

            if (geometry is null)
            {
                diagnostics.Add(Error(
                    Canvas2DSceneDiagnosticCodes.InvalidGeometry,
                    $"Editor feedback '{feedback.Id}' has no usable scene geometry.",
                    feedback.Id));
                continue;
            }

            items.Add(new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForEditorState($"feedback:{feedback.Id}"),
                Canvas2DSceneLayer.Overlay,
                4000 + index,
                geometry,
                new Canvas2DSceneOriginTrace(
                    Canvas2DSceneOriginCategory.EditorState,
                    stableSourceKey: $"feedback:{feedback.Id}"),
                style: new Canvas2DSceneStyle(stroke: "#2563eb", dashPattern: [3d, 2d]),
                hitTestPolicy: Canvas2DHitTestPolicy.None,
                metadata: WithEditorKind(feedback.Kind, feedback.Properties)));
        }
    }

    private static void ComposeDocumentBoundaryGuides(
        EditorStateSnapshot editorState,
        List<Canvas2DSceneItem> items)
    {
        if (editorState.Viewport.VisibleDocumentRegion is not { } visibleRegion)
        {
            return;
        }

        var zoom = editorState.Viewport.Zoom;
        var style = new Canvas2DSceneStyle(
            stroke: "#94a3b8",
            strokeWidth: 1d / zoom,
            dashPattern: [4d / zoom, 4d / zoom],
            opacity: 0.8d);
        if (visibleRegion.Left <= DocumentGeometryBoundary.MinimumX &&
            visibleRegion.Right >= DocumentGeometryBoundary.MinimumX)
        {
            var startY = Math.Max(DocumentGeometryBoundary.MinimumY, visibleRegion.Top);
            if (visibleRegion.Bottom > startY)
            {
                items.Add(CreateDocumentBoundaryGuide(
                    "document-boundary:y-axis",
                    new PointD(DocumentGeometryBoundary.MinimumX, startY),
                    new PointD(DocumentGeometryBoundary.MinimumX, visibleRegion.Bottom),
                    style));
            }
        }

        if (visibleRegion.Top <= DocumentGeometryBoundary.MinimumY &&
            visibleRegion.Bottom >= DocumentGeometryBoundary.MinimumY)
        {
            var startX = Math.Max(DocumentGeometryBoundary.MinimumX, visibleRegion.Left);
            if (visibleRegion.Right > startX)
            {
                items.Add(CreateDocumentBoundaryGuide(
                    "document-boundary:x-axis",
                    new PointD(startX, DocumentGeometryBoundary.MinimumY),
                    new PointD(visibleRegion.Right, DocumentGeometryBoundary.MinimumY),
                    style));
            }
        }
    }

    private static Canvas2DSceneItem CreateDocumentBoundaryGuide(
        string stableKey,
        PointD start,
        PointD end,
        Canvas2DSceneStyle style) =>
        new(
            Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
            Canvas2DSceneLayer.Background,
            0,
            Canvas2DSceneGeometry.Path([start, end]),
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.EditorState,
                stableSourceKey: stableKey),
            style: style,
            hitTestPolicy: Canvas2DHitTestPolicy.None);

    private static ProjectedConnectorAnchor[] ExistingDynamicAnchors(
        VisualStateSnapshot visualState,
        IEnumerable<ProjectedConnectorAnchor> projectedAnchors,
        ConnectorAnchorRole requiredRole)
    {
        var persistentById = visualState.ConnectorAnchors.ToDictionary(static anchor => anchor.Id);
        var sideCounts = visualState.ConnectorAnchors
            .GroupBy(static anchor => anchor.Side)
            .ToDictionary(static group => group.Key, static group => group.Count());
        return projectedAnchors
            .Where(anchor =>
                anchor.Kind == ResolvedConnectorAnchorKind.Dynamic &&
                anchor.Allows(requiredRole) &&
                persistentById.TryGetValue(anchor.Id, out var persistent) &&
                persistent.Role == requiredRole &&
                persistent.Side == anchor.Side &&
                persistent.Order == anchor.Order &&
                sideCounts.TryGetValue(anchor.Side, out var sideCount) &&
                sideCount == anchor.SideCount)
            .OrderBy(static anchor => anchor.Side)
            .ThenBy(static anchor => anchor.Order)
            .ThenBy(static anchor => anchor.Id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<Canvas2DSceneItem> CreateTargetOverlays(
        Canvas2DSceneItem target,
        string role,
        int zIndex,
        string stroke,
        bool hasLineJumps,
        IReadOnlyList<Canvas2DSceneItem> persistentItems)
    {
        var relatedProjectedIds = target.Origin.RelatedProjectedObjectIds.ToHashSet();
        if (target.Origin.ProjectedObjectId is not null)
        {
            relatedProjectedIds.Add(target.Origin.ProjectedObjectId);
        }

        var categories = Canvas2DSceneOriginCategory.EditorState;
        if (target.Origin.SemanticElementId is not null)
        {
            categories |= Canvas2DSceneOriginCategory.SemanticElement;
        }

        if (target.Origin.VisualStateId is not null)
        {
            categories |= Canvas2DSceneOriginCategory.VisualState;
        }

        if (relatedProjectedIds.Count > 0)
        {
            categories |= Canvas2DSceneOriginCategory.ProjectedRuntimeObject;
        }

        var stableKey = $"{role}:{target.Id.Value}";
        var isConnector = IsCanonicalConnector(target);
        var overlayLayer = isConnector && hasLineJumps
            ? Canvas2DSceneLayer.Connector
            : Canvas2DSceneLayer.Overlay;
        var overlayZIndex = isConnector && hasLineJumps
            ? role switch
            {
                "hover" => ConnectorHoverZIndex,
                _ => ConnectorSelectionZIndex,
            }
            : zIndex;
        yield return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
            overlayLayer,
            overlayZIndex,
            target.Geometry,
            new Canvas2DSceneOriginTrace(
                categories,
                target.Origin.SemanticElementId,
                target.Origin.VisualStateId,
                stableSourceKey: stableKey,
                relatedProjectedObjectIds: relatedProjectedIds,
                relatedSceneObjectIds: [target.Id]),
            transform: target.Transform,
            clip: target.Clip,
            style: new Canvas2DSceneStyle(stroke: stroke, strokeWidth: 2d),
            hitTestPolicy: Canvas2DHitTestPolicy.None,
            bounds: target.Bounds);

        if (!isConnector)
        {
            yield break;
        }

        var targetArrow = persistentItems.SingleOrDefault(item =>
            item.Origin.ProjectedObjectId == target.Origin.ProjectedObjectId &&
            HasBooleanMetadata(item, Canvas2DConnectorArrowMetadata.TargetArrow));
        if (targetArrow is not null)
        {
            yield return CreateConnectorTargetArrowOverlay(
                target,
                targetArrow,
                role,
                stroke,
                hasLineJumps
                    ? role == "hover"
                        ? ConnectorHoverZIndex
                        : ConnectorSelectionZIndex
                    : zIndex,
                hasLineJumps
                    ? Canvas2DSceneLayer.Connector
                    : Canvas2DSceneLayer.Overlay);
        }

        if (!hasLineJumps)
        {
            yield break;
        }

        var lineJumpZIndex = role == "hover"
            ? ConnectorLineJumpHoverZIndex
            : ConnectorLineJumpSelectionZIndex;
        foreach (var lineJump in persistentItems
                     .Where(item =>
                         Canvas2DConnectorLineJumpMetadata.TryResolveHitTarget(
                             item,
                             out var hitTargetId) &&
                         hitTargetId == target.Id)
                     .OrderBy(static item => item.Id.Value, StringComparer.Ordinal))
        {
            yield return CreateConnectorLineJumpOverlay(
                target,
                lineJump,
                role,
                stroke,
                lineJumpZIndex);
        }
    }

    private static bool IsCanonicalConnector(Canvas2DSceneItem item) =>
        item.Layer == Canvas2DSceneLayer.Connector &&
        item.Geometry.Kind == Canvas2DSceneGeometryKind.Path &&
        item.Metadata.ContainsKey(Canvas2DConnectorPathMetadata.LogicalPathPointCount);

    private static Canvas2DSceneItem ResolveConnectorHoverTarget(
        Canvas2DSceneItem hovered,
        IReadOnlyList<Canvas2DSceneItem> persistentItems)
    {
        if (!HasBooleanMetadata(hovered, Canvas2DConnectorArrowMetadata.TargetArrow))
        {
            return hovered;
        }

        return persistentItems.FirstOrDefault(item =>
            IsCanonicalConnector(item) &&
            item.Origin.VisualStateId == hovered.Origin.VisualStateId &&
            item.Origin.ProjectedObjectId == hovered.Origin.ProjectedObjectId) ?? hovered;
    }

    private static Canvas2DSceneItem CreateConnectorLineJumpOverlay(
        Canvas2DSceneItem connector,
        Canvas2DSceneItem lineJump,
        string role,
        string stroke,
        int zIndex)
    {
        var stableKey = $"{role}:{lineJump.Id.Value}";
        var categories = Canvas2DSceneOriginCategory.EditorState;
        if (connector.Origin.SemanticElementId is not null)
        {
            categories |= Canvas2DSceneOriginCategory.SemanticElement;
        }

        if (connector.Origin.VisualStateId is not null)
        {
            categories |= Canvas2DSceneOriginCategory.VisualState;
        }

        if (connector.Origin.ProjectedObjectId is not null)
        {
            categories |= Canvas2DSceneOriginCategory.ProjectedRuntimeObject;
        }

        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
            Canvas2DSceneLayer.Connector,
            zIndex,
            lineJump.Geometry,
            new Canvas2DSceneOriginTrace(
                categories,
                connector.Origin.SemanticElementId,
                connector.Origin.VisualStateId,
                connector.Origin.ProjectedObjectId,
                stableSourceKey: stableKey,
                relatedProjectedObjectIds: connector.Origin.RelatedProjectedObjectIds,
                relatedSceneObjectIds: [connector.Id, lineJump.Id]),
            transform: lineJump.Transform,
            clip: lineJump.Clip,
            style: new Canvas2DSceneStyle(stroke: stroke, strokeWidth: 2d),
            hitTestPolicy: Canvas2DHitTestPolicy.None,
            bounds: lineJump.Bounds);
    }

    private static Canvas2DSceneItem CreateConnectorTargetArrowOverlay(
        Canvas2DSceneItem connector,
        Canvas2DSceneItem targetArrow,
        string role,
        string color,
        int zIndex,
        Canvas2DSceneLayer layer)
    {
        var stableKey = $"{role}:{targetArrow.Id.Value}";
        var categories = Canvas2DSceneOriginCategory.EditorState;
        if (connector.Origin.SemanticElementId is not null)
        {
            categories |= Canvas2DSceneOriginCategory.SemanticElement;
        }

        if (connector.Origin.VisualStateId is not null)
        {
            categories |= Canvas2DSceneOriginCategory.VisualState;
        }

        if (connector.Origin.ProjectedObjectId is not null)
        {
            categories |= Canvas2DSceneOriginCategory.ProjectedRuntimeObject;
        }

        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
            layer,
            zIndex,
            targetArrow.Geometry,
            new Canvas2DSceneOriginTrace(
                categories,
                connector.Origin.SemanticElementId,
                connector.Origin.VisualStateId,
                connector.Origin.ProjectedObjectId,
                stableSourceKey: stableKey,
                relatedProjectedObjectIds: connector.Origin.RelatedProjectedObjectIds,
                relatedSceneObjectIds: [connector.Id, targetArrow.Id]),
            transform: targetArrow.Transform,
            clip: targetArrow.Clip,
            style: new Canvas2DSceneStyle(color, color, 2d),
            hitTestPolicy: Canvas2DHitTestPolicy.None,
            bounds: targetArrow.Bounds);
    }

    private static Canvas2DSceneItem? ResolveLogicalSelectionTarget(
        IEnumerable<Canvas2DSceneItem> persistentItems,
        VisualStateId visualStateId) =>
        persistentItems
            .Where(item =>
                item.Origin.VisualStateId == visualStateId &&
                (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
                item.IsVisible &&
                item.HitTestPolicy.Mode != Canvas2DHitTestMode.None)
            .OrderBy(static item => SelectionTargetRank(item.Layer))
            .ThenBy(static item => item.ZIndex)
            .ThenBy(static item => item.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();

    private static int SelectionTargetRank(Canvas2DSceneLayer layer) => layer switch
    {
        Canvas2DSceneLayer.Content => 0,
        Canvas2DSceneLayer.Connector => 1,
        Canvas2DSceneLayer.Label => 2,
        Canvas2DSceneLayer.Decoration => 3,
        Canvas2DSceneLayer.Background => 4,
        _ => 5,
    };

    private static bool IsResizeInteractionRegion(Canvas2DSceneItem item)
    {
        if ((item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 ||
            !TryGetTextMetadata(item, EditorKindKey, out var editorKind))
        {
            return false;
        }

        if (StringComparer.Ordinal.Equals(
                editorKind,
                Canvas2DNodeLabelGestureMetadata.ResizeZoneKind))
        {
            return TryGetTextMetadata(
                    item,
                    Canvas2DNodeLabelGestureMetadata.ResizeDirection,
                    out var labelResizeDirection) &&
                Canvas2DResizeGeometry.TryParseRole(labelResizeDirection, out _) &&
                TryGetTextMetadata(
                    item,
                    Canvas2DNodeLabelGestureMetadata.TargetNodeSceneObjectId,
                    out _) &&
                TryGetTextMetadata(
                    item,
                    Canvas2DNodeLabelGestureMetadata.TargetLabelSceneObjectId,
                    out _) &&
                TryGetTextMetadata(
                    item,
                    Canvas2DNodeLabelGestureMetadata.TargetVisualStateId,
                    out _) &&
                TryGetTextMetadata(
                    item,
                    Canvas2DNodeLabelGestureMetadata.TargetLabelProjectedObjectId,
                    out _);
        }

        return (StringComparer.Ordinal.Equals(
                    editorKind,
                    Canvas2DResizeGestureMetadata.HandleKind) ||
                StringComparer.Ordinal.Equals(
                    editorKind,
                    Canvas2DResizeGestureMetadata.EdgeHitZoneKind)) &&
            TryGetTextMetadata(
                item,
                Canvas2DResizeGestureMetadata.HandleRole,
                out var resizeDirection) &&
            Canvas2DResizeGeometry.TryParseRole(resizeDirection, out _) &&
            TryGetTextMetadata(
                item,
                Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                out _) &&
            TryGetTextMetadata(
                item,
                Canvas2DResizeGestureMetadata.TargetVisualStateId,
                out _);
    }

    private static Canvas2DSceneOriginTrace CreateOrigin(
        IProjectedObject projectedObject,
        IEnumerable<ProjectedObjectId>? relatedProjectedObjectIds = null)
    {
        var categories = Canvas2DSceneOriginCategory.SemanticElement |
            Canvas2DSceneOriginCategory.ProjectedRuntimeObject;
        if (projectedObject.Source.VisualStateId is not null)
        {
            categories |= Canvas2DSceneOriginCategory.VisualState;
        }

        var canonicalRelatedIds = relatedProjectedObjectIds?
            .Where(id => id != projectedObject.Id)
            .Distinct()
            .ToArray();

        return new Canvas2DSceneOriginTrace(
            categories,
            projectedObject.Source.SemanticElementId,
            projectedObject.Source.VisualStateId,
            projectedObject.Id,
            relatedProjectedObjectIds: canonicalRelatedIds);
    }

    private static Canvas2DSceneOriginTrace CreateDerivedConnectorOrigin(
        Canvas2DSceneOriginTrace connectorOrigin,
        string stableKey,
        SceneObjectId connectorId) =>
        new(
            connectorOrigin.Categories | Canvas2DSceneOriginCategory.Configuration,
            connectorOrigin.SemanticElementId,
            connectorOrigin.VisualStateId,
            connectorOrigin.ProjectedObjectId,
            stableSourceKey: stableKey,
            relatedProjectedObjectIds: connectorOrigin.RelatedProjectedObjectIds,
            relatedSceneObjectIds: [connectorId]);

    private static PropertyMap GetPersistentAppearance(
        IProjectedObject projectedObject,
        Dictionary<VisualStateId, VisualStateSnapshot> visualsById) =>
        projectedObject.Source.VisualStateId is not null &&
        visualsById.TryGetValue(projectedObject.Source.VisualStateId, out var visual)
            ? visual.Properties
            : PropertyMap.Empty;

    private static IEnumerable<KeyValuePair<string, PropertyValue>> GetConnectorMetadata(
        ProjectedEdge edge,
        IReadOnlyList<PointD> logicalPath,
        PointD sourceAnchor,
        PointD targetAnchor,
        PropertyMap? routeMetadata,
        bool isNoRouteFallback)
    {
        if (routeMetadata is not null)
        {
            foreach (var entry in routeMetadata)
            {
                if (!StringComparer.Ordinal.Equals(
                        entry.Key,
                        Canvas2DRouteGestureMetadata.RouteEditable) &&
                    !Canvas2DConnectorPathMetadata.IsReservedKey(entry.Key))
                {
                    yield return entry;
                }
            }
        }

        IReadOnlyList<PointD>? editablePath = null;
        if (edge.PersistentRoute.Length >= 2)
        {
            editablePath = CreateEditableConnectorPath(
                edge.PersistentRoute,
                sourceAnchor,
                targetAnchor);
        }

        foreach (var entry in Canvas2DConnectorPathMetadata.CreateProperties(
                     logicalPath,
                     editablePath,
                     isNoRouteFallback))
        {
            yield return entry;
        }

        if (edge.Source.VisualStateId is not null && editablePath is not null)
        {
            yield return new KeyValuePair<string, PropertyValue>(
                Canvas2DRouteGestureMetadata.RouteEditable,
                Canvas2DRouteGestureMetadata.EditableValue);
        }
    }

    private static ImmutableArray<PointD> CreateEditableConnectorPath(
        ImmutableArray<PointD> persistentRoute,
        PointD sourceAnchor,
        PointD targetAnchor)
    {
        var path = ImmutableArray.CreateBuilder<PointD>(persistentRoute.Length);
        path.Add(sourceAnchor);
        if (persistentRoute.Length > 2)
        {
            path.AddRange(persistentRoute.AsSpan(1, persistentRoute.Length - 2));
        }

        path.Add(targetAnchor);
        return path.MoveToImmutable();
    }

    private static ImmutableArray<PointD> CreateNoRouteFallbackPath(
        ProjectedEdge edge,
        IReadOnlyDictionary<ProjectedObjectId, LayoutNodeGeometry> nodeGeometry,
        IReadOnlyDictionary<ProjectedObjectId, ProjectedPort> portsById)
    {
        var sourceAnchor = ResolveCurrentConnectorAnchor(
            edge.SourceNodeId,
            edge.SourcePortId,
            ConnectorAnchorRole.Source,
            nodeGeometry,
            portsById);
        var targetAnchor = ResolveCurrentConnectorAnchor(
            edge.TargetNodeId,
            edge.TargetPortId,
            ConnectorAnchorRole.Target,
            nodeGeometry,
            portsById);
        return [sourceAnchor, targetAnchor];
    }

    private static PointD ResolveCurrentConnectorAnchor(
        ProjectedObjectId nodeId,
        ProjectedObjectId? portId,
        ConnectorAnchorRole role,
        IReadOnlyDictionary<ProjectedObjectId, LayoutNodeGeometry> nodeGeometry,
        IReadOnlyDictionary<ProjectedObjectId, ProjectedPort> portsById)
    {
        var bounds = nodeGeometry[nodeId].Bounds;
        PointD point;
        if (portId is null)
        {
            point = role == ConnectorAnchorRole.Source
                ? new PointD(bounds.Right, bounds.Top + (bounds.Height / 2d))
                : new PointD(bounds.Left, bounds.Top + (bounds.Height / 2d));
        }
        else if (portsById.TryGetValue(portId, out var port) &&
                 port.OwnerNodeId == nodeId &&
                 ProjectedConnectorAnchorMetadata.TryDecode(port, out var anchor) &&
                 anchor is not null &&
                 anchor.Allows(role))
        {
            point = ConnectorAnchorGeometryResolver.ResolvePoint(
                bounds,
                anchor.Side,
                anchor.Order,
                anchor.SideCount);
        }
        else
        {
            throw new InvalidOperationException(
                $"NoRoute connector endpoint '{portId}' has invalid projected anchor metadata.");
        }

        if (!DocumentGeometryBoundary.Contains(point))
        {
            throw new InvalidOperationException(
                $"NoRoute connector endpoint '{portId?.Value ?? "implicit"}' resolved outside valid Document geometry.");
        }

        return point;
    }

    private static (RectD Bounds, Matrix2D Transform) ResolveLabelGeometry(
        ProjectedLabel label,
        ProjectedGraph graph,
        Dictionary<ProjectedObjectId, LayoutNodeGeometry> nodes,
        Dictionary<ProjectedObjectId, LayoutGroupGeometry> groups,
        Dictionary<ProjectedObjectId, RoutedConnectorGeometry> routes,
        Dictionary<ProjectedObjectId, ProjectedPort> ports)
    {
        if (nodes.TryGetValue(label.OwnerId, out var node))
        {
            return (node.Bounds, node.Transform);
        }

        if (groups.TryGetValue(label.OwnerId, out var group))
        {
            return (
                group.Bounds,
                Matrix2D.CreateTranslation(group.Bounds.X, group.Bounds.Y));
        }

        if (routes.TryGetValue(label.OwnerId, out var route))
        {
            var bounds = Canvas2DSceneGeometry.Path(route.Path).Bounds;
            return (bounds, Matrix2D.CreateTranslation(bounds.X, bounds.Y));
        }

        if (ports.TryGetValue(label.OwnerId, out var port) &&
            nodes.TryGetValue(port.OwnerNodeId, out var owner))
        {
            return (owner.Bounds, owner.Transform);
        }

        throw new ArgumentException(
            $"Projected label '{label.Id}' has no compatible owner geometry.",
            nameof(graph));
    }

    private static Matrix2D CreateAutomaticNodeLabelTransform(
        LayoutNodeGeometry node,
        PointD localAnchor)
    {
        var center = new PointD(
            node.Bounds.X + (node.Bounds.Width / 2d),
            node.Bounds.Y + (node.Bounds.Height / 2d));
        if (node.Transform.TransformPoint(localAnchor) == center)
        {
            return node.Transform;
        }

        return new Matrix2D(
            node.Transform.M11,
            node.Transform.M12,
            node.Transform.M21,
            node.Transform.M22,
            center.X - ((node.Transform.M11 * localAnchor.X) +
                (node.Transform.M21 * localAnchor.Y)),
            center.Y - ((node.Transform.M12 * localAnchor.X) +
                (node.Transform.M22 * localAnchor.Y)));
    }

    private static Canvas2DSceneItem CreateUnmeasuredOutsideNodeLabelItem(
        ProjectedLabel label,
        LayoutNodeGeometry ownerNode,
        IEnumerable<KeyValuePair<string, PropertyValue>> persistentAppearance)
    {
        var placement = label.NodePlacement!;
        var maximumWidth = placement.MaximumWidth!.Value;
        var horizontalScale = Length(
            ownerNode.Transform.TransformVector(new VectorD(1d, 0d)));
        var verticalScale = Length(
            ownerNode.Transform.TransformVector(new VectorD(0d, 1d)));
        var localWidth = horizontalScale == 0d
            ? maximumWidth
            : maximumWidth / horizontalScale;
        var localLineHeight = NodeLabelStyle.FontSize *
            Canvas2DNodeLabelLayoutConfiguration.Default.LineHeightMultiplier;
        var documentLineHeight = localLineHeight * verticalScale;
        var bounds = DocumentGeometryBoundary.Clamp(new RectD(
            ownerNode.Bounds.Left + ((ownerNode.Bounds.Width - maximumWidth) / 2d),
            ownerNode.Bounds.Bottom + placement.Gap,
            maximumWidth,
            documentLineHeight));
        var localAnchor = new PointD(0d, 0d);
        var documentAnchor = new PointD(
            bounds.Left + (bounds.Width / 2d),
            bounds.Top + (bounds.Height / 2d));
        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label"),
            Canvas2DSceneLayer.Label,
            0,
            Canvas2DSceneGeometry.Text(
                new RectD(
                    -(localWidth / 2d),
                    -(localLineHeight / 2d),
                    localWidth,
                    localLineHeight),
                label.Text,
                localAnchor,
                Canvas2DTextAlignment.Center,
                Canvas2DTextBaseline.Middle),
            CreateOrigin(label, [label.OwnerId]),
            transform: CreateMeasuredLineTransform(
                ownerNode.Transform,
                localAnchor,
                documentAnchor),
            clip: bounds,
            style: NodeLabelStyle,
            hitTestPolicy: label.NodeInteractionPolicy ==
                NodeLabelInteractionPolicy.MoveAndResize &&
                label.Source.VisualStateId is not null
                    ? Canvas2DHitTestPolicy.None
                    : new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Bounds),
            persistentAppearance: persistentAppearance,
            metadata: label.NodeInteractionPolicy ==
                NodeLabelInteractionPolicy.MoveAndResize &&
                label.Source.VisualStateId is not null
                    ? null
                    : Canvas2DResizeGestureMetadata.MoveAndResizeCapability,
            bounds: bounds);
    }

    private static Canvas2DSceneItem CreateUnmeasuredManualNodeLabelItem(
        ProjectedLabel label,
        LayoutNodeGeometry ownerNode,
        NodeLabelVisualOverride visualOverride,
        IEnumerable<KeyValuePair<string, PropertyValue>> persistentAppearance)
    {
        var horizontalScale = Length(
            ownerNode.Transform.TransformVector(new VectorD(1d, 0d)));
        var localWidth = horizontalScale == 0d
            ? visualOverride.Width
            : visualOverride.Width / horizontalScale;
        var localLineHeight = NodeLabelStyle.FontSize *
            Canvas2DNodeLabelLayoutConfiguration.Default.LineHeightMultiplier;
        var bounds = DocumentGeometryBoundary.Clamp(
            visualOverride.ResolveBounds(ownerNode.Bounds));
        var center = new PointD(
            bounds.Left + (bounds.Width / 2d),
            bounds.Top + (bounds.Height / 2d));
        var localAnchor = new PointD(0d, 0d);
        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label"),
            Canvas2DSceneLayer.Label,
            0,
            Canvas2DSceneGeometry.Text(
                new RectD(
                    -(localWidth / 2d),
                    -(localLineHeight / 2d),
                    localWidth,
                    localLineHeight),
                label.Text,
                localAnchor,
                Canvas2DTextAlignment.Center,
                Canvas2DTextBaseline.Middle),
            CreateOrigin(label, [label.OwnerId]),
            transform: CreateMeasuredLineTransform(
                ownerNode.Transform,
                localAnchor,
                center),
            clip: bounds,
            style: NodeLabelStyle,
            hitTestPolicy: label.NodeInteractionPolicy ==
                NodeLabelInteractionPolicy.MoveAndResize &&
                label.Source.VisualStateId is not null
                    ? Canvas2DHitTestPolicy.None
                    : new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Bounds),
            persistentAppearance: persistentAppearance,
            metadata: label.NodeInteractionPolicy ==
                NodeLabelInteractionPolicy.MoveAndResize &&
                label.Source.VisualStateId is not null
                    ? null
                    : Canvas2DResizeGestureMetadata.MoveAndResizeCapability,
            bounds: bounds);
    }

    private static Matrix2D CreateViewportTransform(EditorStateSnapshot editorState) =>
        CreateViewportTransform(editorState.Viewport);

    private static IEnumerable<KeyValuePair<string, PropertyValue>> WithEditorKind(
        string kind,
        PropertyMap properties)
    {
        yield return new KeyValuePair<string, PropertyValue>(
            EditorKindKey,
            PropertyValue.FromText(kind));
        foreach (var entry in properties)
        {
            if (!StringComparer.Ordinal.Equals(entry.Key, EditorKindKey))
            {
                yield return entry;
            }
        }
    }
}
