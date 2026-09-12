using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    private static IEnumerable<Canvas2DSceneItem> CreateConnectorAnchorHandles(
        Canvas2DSceneItem target,
        VisualStateSnapshot visualState,
        ProjectedConnectorAnchor[] resolvedAnchors,
        HashSet<ConnectorAnchorId> referencedAnchorIds,
        EditorGestureSnapshot? activeGesture,
        bool isConnectionTargetCandidate = false,
        ConnectorAnchorId? referencedCandidateAnchorExemption = null)
    {
        ArgumentNullException.ThrowIfNull(resolvedAnchors);
        if (target.Layer != Canvas2DSceneLayer.Content ||
            target.Origin.VisualStateId != visualState.Id ||
            target.Origin.ProjectedObjectId is null ||
            resolvedAnchors.Length == 0 ||
            target.Bounds.IsEmpty)
        {
            yield break;
        }

        var displayedBounds = ResolveConnectorAnchorBounds(
            target,
            visualState.Id,
            activeGesture);
        var halfExtent = Canvas2DConnectorAnchorMetadata.HandleExtent / 2d;
        foreach (var anchor in resolvedAnchors)
        {
            if (isConnectionTargetCandidate &&
                referencedAnchorIds.Contains(anchor.Id) &&
                anchor.Id != referencedCandidateAnchorExemption)
            {
                continue;
            }

            var point = ConnectorAnchorGeometryResolver.ResolvePoint(
                displayedBounds,
                anchor.Side,
                anchor.Order,
                anchor.SideCount);
            var bounds = new RectD(
                point.X - halfExtent,
                point.Y - halfExtent,
                Canvas2DConnectorAnchorMetadata.HandleExtent,
                Canvas2DConnectorAnchorMetadata.HandleExtent);
            var stableKey = $"connector-anchor-handle:{target.Id.Value}:{anchor.Id.Value}";
            var categories = target.Origin.Categories | Canvas2DSceneOriginCategory.EditorState;
            var style = ResolveConnectorAnchorStyle(anchor);
            var metadata = new List<KeyValuePair<string, PropertyValue>>
            {
                new(
                    Canvas2DConnectorAnchorMetadata.AnchorId,
                    PropertyValue.FromText(anchor.Id.Value)),
                new(
                    Canvas2DConnectorAnchorMetadata.Side,
                    PropertyValue.FromText(anchor.Side.ToString())),
                new(
                    Canvas2DConnectorAnchorMetadata.RoleCapability,
                    PropertyValue.FromInteger((long)anchor.RoleCapability)),
                new(
                    Canvas2DConnectorAnchorMetadata.AnchorKind,
                    PropertyValue.FromInteger((long)anchor.Kind)),
                new(
                    Canvas2DConnectorAnchorMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(target.Id.Value)),
                new(
                    Canvas2DConnectorAnchorMetadata.TargetVisualStateId,
                    PropertyValue.FromText(visualState.Id.Value)),
                new(
                    Canvas2DConnectorAnchorMetadata.DeleteCapable,
                    PropertyValue.FromBoolean(
                        anchor.Kind == ResolvedConnectorAnchorKind.Dynamic &&
                        !referencedAnchorIds.Contains(anchor.Id))),
            };
            if (anchor.Kind == ResolvedConnectorAnchorKind.Dynamic &&
                anchor.AllowsSource != anchor.AllowsTarget)
            {
                metadata.Add(new KeyValuePair<string, PropertyValue>(
                    Canvas2DConnectorAnchorMetadata.Role,
                    PropertyValue.FromText(anchor.AllowsSource
                        ? ConnectorAnchorRole.Source.ToString()
                        : ConnectorAnchorRole.Target.ToString())));
            }

            if (isConnectionTargetCandidate)
            {
                metadata.Add(new KeyValuePair<string, PropertyValue>(
                    Canvas2DConnectorAnchorMetadata.ConnectionTargetCandidate,
                    PropertyValue.FromBoolean(true)));
            }

            yield return new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
                Canvas2DSceneLayer.Overlay,
                Canvas2DConnectorAnchorMetadata.HandleZIndex,
                Canvas2DSceneGeometry.Ellipse(bounds),
                new Canvas2DSceneOriginTrace(
                    categories,
                    target.Origin.SemanticElementId,
                    visualState.Id,
                    target.Origin.ProjectedObjectId,
                    stableKey,
                    target.Origin.RelatedProjectedObjectIds,
                    [target.Id]),
                style: style,
                hitTestPolicy: new Canvas2DHitTestPolicy(
                    Canvas2DHitTestMode.FillOrStroke,
                    2d),
                metadata: WithEditorKind(
                    Canvas2DConnectorAnchorMetadata.HandleKind,
                    new PropertyMap(metadata)),
                bounds: bounds);
        }
    }

    private static bool ComposeAnchorConnectionGesturePreview(
        EditorGestureSnapshot gesture,
        List<Canvas2DSceneItem> items,
        List<Diagnostic> diagnostics)
    {
        if (!StringComparer.Ordinal.Equals(
                gesture.Kind,
                Canvas2DAnchorConnectionGestureMetadata.Kind))
        {
            return false;
        }

        if (!Canvas2DAnchorConnectionGestureMetadata.TryRead(
                gesture.Properties,
                out var connection) ||
            connection is null)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidGeometry,
                $"Anchor-connection gesture '{gesture.Id}' has invalid transient identity metadata.",
                gesture.Id));
            return true;
        }

        var stableKey = $"anchor-connection-preview:{gesture.Id}";
        items.Add(new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
            Canvas2DSceneLayer.Overlay,
            Canvas2DAnchorConnectionGestureMetadata.PreviewZIndex,
            Canvas2DSceneGeometry.Path([gesture.Origin, gesture.Current]),
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.SemanticElement |
                Canvas2DSceneOriginCategory.VisualState |
                Canvas2DSceneOriginCategory.EditorState,
                connection.SourceSemanticElementId,
                connection.SourceVisualStateId,
                stableSourceKey: stableKey),
            style: new Canvas2DSceneStyle(
                stroke: "#2563eb",
                strokeWidth: 2d,
                dashPattern: [4d, 2d]),
            hitTestPolicy: Canvas2DHitTestPolicy.None,
            metadata: WithEditorKind(gesture.Kind, gesture.Properties)));
        if (connection.HasProposedTarget)
        {
            const double extent = Canvas2DConnectorAnchorMetadata.HandleExtent;
            var proposedKey = $"anchor-connection-proposed-target:{gesture.Id}";
            items.Add(new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForEditorState(proposedKey),
                Canvas2DSceneLayer.Overlay,
                Canvas2DAnchorConnectionGestureMetadata.PreviewZIndex + 1,
                Canvas2DSceneGeometry.Ellipse(new RectD(
                    gesture.Current.X - (extent / 2d),
                    gesture.Current.Y - (extent / 2d),
                    extent,
                    extent)),
                new Canvas2DSceneOriginTrace(
                    Canvas2DSceneOriginCategory.SemanticElement |
                    Canvas2DSceneOriginCategory.VisualState |
                    Canvas2DSceneOriginCategory.EditorState,
                    connection.TargetSemanticElementId,
                    connection.TargetVisualStateId,
                    stableSourceKey: proposedKey),
                style: new Canvas2DSceneStyle(
                    fill: "#dbeafe",
                    stroke: "#2563eb",
                    strokeWidth: 2d,
                    dashPattern: [2d, 2d]),
                hitTestPolicy: Canvas2DHitTestPolicy.None,
                metadata: WithEditorKind(gesture.Kind, gesture.Properties)));
        }

        return true;
    }

    private static Canvas2DSceneStyle ResolveConnectorAnchorStyle(
        ProjectedConnectorAnchor anchor)
    {
        if (anchor.AllowsSource && anchor.AllowsTarget)
        {
            return new Canvas2DSceneStyle("#f5f3ff", "#6d28d9", 2d);
        }

        return anchor.AllowsSource
            ? new Canvas2DSceneStyle("#ffffff", "#2563eb", 2d)
            : new Canvas2DSceneStyle("#2563eb", "#1e3a8a", 2d);
    }

    private static RectD ResolveConnectorAnchorBounds(
        Canvas2DSceneItem target,
        VisualStateId visualStateId,
        EditorGestureSnapshot? activeGesture)
    {
        if (activeGesture is not null &&
            StringComparer.Ordinal.Equals(
                activeGesture.Kind,
                Canvas2DMoveGestureMetadata.Kind) &&
            TryGetMoveTargets(activeGesture, out var moveTargets) &&
            moveTargets.Contains(visualStateId))
        {
            return target.Bounds.Translate(activeGesture.Current - activeGesture.Origin);
        }

        if (activeGesture is not null &&
            StringComparer.Ordinal.Equals(
                activeGesture.Kind,
                Canvas2DResizeGestureMetadata.Kind) &&
            TryGetResizeTarget(
                activeGesture,
                out var targetSceneObjectId,
                out var targetVisualStateId,
                out var direction) &&
            targetSceneObjectId == target.Id &&
            targetVisualStateId == visualStateId)
        {
            return Canvas2DResizeGeometry.CalculateBounds(
                target.Bounds,
                activeGesture.Current - activeGesture.Origin,
                direction);
        }

        return target.Bounds;
    }

    private static IEnumerable<Canvas2DSceneItem> CreateResizeHandles(
        Canvas2DSceneItem target,
        EditorGestureSnapshot? activeGesture)
    {
        if (target.Origin.VisualStateId is not { } visualStateId ||
            target.Bounds.IsEmpty ||
            !target.Metadata.TryGetValue(
                Canvas2DResizeGestureMetadata.ResizeCapable,
                out var capability) ||
            capability.Kind != PropertyValueKind.Boolean ||
            !capability.BooleanValue)
        {
            yield break;
        }

        var targetBounds = target.Bounds;
        if (activeGesture is not null &&
            StringComparer.Ordinal.Equals(
                activeGesture.Kind,
                Canvas2DResizeGestureMetadata.Kind) &&
            IsGestureTarget(activeGesture, target.Id, visualStateId) &&
            TryGetResizeTarget(
                activeGesture,
                out _,
                out _,
                out var activeDirection))
        {
            targetBounds = Canvas2DResizeGeometry.CalculateBounds(
                target.Bounds,
                activeGesture.Current - activeGesture.Origin,
                activeDirection);
        }

        var categories = target.Origin.Categories | Canvas2DSceneOriginCategory.EditorState;
        foreach (var direction in Canvas2DResizeGeometry.Directions)
        {
            var role = Canvas2DResizeGeometry.Role(direction);
            var handleBounds = Canvas2DResizeGeometry.InteractionBounds(targetBounds, direction);
            var isCorner = Canvas2DResizeGeometry.IsCorner(direction);
            var stableKey = isCorner
                ? $"resize-handle:{role}:{target.Id.Value}"
                : $"resize-edge-zone:{role}:{target.Id.Value}";
            yield return new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
                Canvas2DSceneLayer.Overlay,
                Canvas2DResizeGeometry.ZIndex(direction),
                Canvas2DSceneGeometry.Rectangle(handleBounds),
                new Canvas2DSceneOriginTrace(
                    categories,
                    target.Origin.SemanticElementId,
                    visualStateId,
                    target.Origin.ProjectedObjectId,
                    stableKey,
                    target.Origin.RelatedProjectedObjectIds,
                    [target.Id]),
                style: new Canvas2DSceneStyle(opacity: 0d),
                hitTestPolicy: new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Bounds),
                metadata: WithEditorKind(
                    isCorner
                        ? Canvas2DResizeGestureMetadata.HandleKind
                        : Canvas2DResizeGestureMetadata.EdgeHitZoneKind,
                    new PropertyMap(CreateResizeProperties(
                        target.Id,
                        visualStateId,
                        direction))),
                bounds: handleBounds);
        }
    }

    private static IEnumerable<Canvas2DSceneItem> CreateNodeLabelResizeZones(
        Canvas2DSceneItem nodeTarget,
        IEnumerable<Canvas2DSceneItem> persistentItems,
        EditorGestureSnapshot? activeGesture)
    {
        if (nodeTarget.Origin.VisualStateId is not { } visualStateId ||
            nodeTarget.Layer != Canvas2DSceneLayer.Content)
        {
            yield break;
        }

        foreach (var labelTarget in persistentItems.Where(item =>
                     item.Layer == Canvas2DSceneLayer.Label &&
                     item.Origin.VisualStateId == visualStateId &&
                     HasBooleanMetadata(
                         item,
                         Canvas2DNodeLabelGestureMetadata.InteractionCapable) &&
                     TryGetTextMetadata(
                         item,
                         Canvas2DNodeLabelGestureMetadata.TargetNodeSceneObjectId,
                         out var targetNodeId) &&
                     StringComparer.Ordinal.Equals(targetNodeId, nodeTarget.Id.Value)))
        {
            var displayedBounds = ResolveNodeLabelInteractionBounds(
                labelTarget,
                activeGesture);
            foreach (var direction in Canvas2DResizeGeometry.Directions)
            {
                var role = Canvas2DResizeGeometry.Role(direction);
                var bounds = Canvas2DResizeGeometry.InteractionBounds(
                    displayedBounds,
                    direction);
                var stableKey =
                    $"node-label-resize-zone:{role}:{labelTarget.Id.Value}";
                var categories = labelTarget.Origin.Categories |
                    Canvas2DSceneOriginCategory.EditorState;
                yield return new Canvas2DSceneItem(
                    Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
                    Canvas2DSceneLayer.Overlay,
                    Canvas2DNodeLabelGestureMetadata.ResizeZoneZIndex(direction),
                    Canvas2DSceneGeometry.Rectangle(bounds),
                    new Canvas2DSceneOriginTrace(
                        categories,
                        labelTarget.Origin.SemanticElementId,
                        visualStateId,
                        labelTarget.Origin.ProjectedObjectId,
                        stableKey,
                        labelTarget.Origin.RelatedProjectedObjectIds,
                        [labelTarget.Id, nodeTarget.Id]),
                    style: new Canvas2DSceneStyle(opacity: 0d),
                    hitTestPolicy: new Canvas2DHitTestPolicy(
                        Canvas2DHitTestMode.Bounds),
                    metadata: WithEditorKind(
                        Canvas2DNodeLabelGestureMetadata.ResizeZoneKind,
                        new PropertyMap(CreateNodeLabelResizeZoneMetadata(
                            nodeTarget,
                            labelTarget,
                            direction))),
                    bounds: bounds);
            }
        }
    }

    private static RectD ResolveNodeLabelInteractionBounds(
        Canvas2DSceneItem labelTarget,
        EditorGestureSnapshot? activeGesture)
    {
        if (activeGesture is null ||
            !StringComparer.Ordinal.Equals(
                activeGesture.Kind,
                Canvas2DNodeLabelGestureMetadata.Kind) ||
            !TryGetNodeLabelGestureTarget(
                activeGesture,
                out _,
                out var targetLabelSceneObjectId,
                out var labelId,
                out var visualStateId,
                out var operation,
                out var direction) ||
            labelTarget.Id != targetLabelSceneObjectId ||
            labelTarget.Origin.ProjectedObjectId != labelId ||
            labelTarget.Origin.VisualStateId != visualStateId)
        {
            return labelTarget.Bounds;
        }

        var delta = activeGesture.Current - activeGesture.Origin;
        return operation == Canvas2DNodeLabelGestureOperation.Move
            ? labelTarget.Bounds.Translate(delta)
            : Canvas2DResizeGeometry.CalculateBounds(
                labelTarget.Bounds,
                delta,
                direction,
                NodeLabelVisualOverride.MinimumWidth,
                NodeLabelVisualOverride.MinimumHeight);
    }

    private static IEnumerable<KeyValuePair<string, PropertyValue>>
        CreateNodeLabelResizeZoneMetadata(
            Canvas2DSceneItem nodeTarget,
            Canvas2DSceneItem labelTarget,
            Canvas2DResizeDirection direction)
    {
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DNodeLabelGestureMetadata.InteractionCapable,
            Canvas2DNodeLabelGestureMetadata.InteractionCapableValue);
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DNodeLabelGestureMetadata.TargetNodeSceneObjectId,
            PropertyValue.FromText(nodeTarget.Id.Value));
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DNodeLabelGestureMetadata.TargetLabelSceneObjectId,
            PropertyValue.FromText(labelTarget.Id.Value));
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DNodeLabelGestureMetadata.TargetVisualStateId,
            PropertyValue.FromText(labelTarget.Origin.VisualStateId!.Value));
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DNodeLabelGestureMetadata.TargetLabelProjectedObjectId,
            PropertyValue.FromText(labelTarget.Origin.ProjectedObjectId!.Value));
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DNodeLabelGestureMetadata.Operation,
            PropertyValue.FromText(Canvas2DNodeLabelGestureMetadata.ResizeOperation));
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DNodeLabelGestureMetadata.ResizeDirection,
            PropertyValue.FromText(Canvas2DResizeGeometry.Role(direction)));
    }

    private static bool ComposeMoveGesturePreview(
        EditorGestureSnapshot gesture,
        Canvas2DSceneItem[] persistentItems,
        List<Canvas2DSceneItem> items,
        List<Diagnostic> diagnostics)
    {
        if (!StringComparer.Ordinal.Equals(gesture.Kind, Canvas2DMoveGestureMetadata.Kind))
        {
            return false;
        }

        if (!TryGetMoveTargets(gesture, out var visualStateIds))
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidGeometry,
                $"Move gesture '{gesture.Id}' does not identify persistent Visual States.",
                gesture.Id));
            return true;
        }

        var translation = gesture.Current - gesture.Origin;
        var automaticLabelTranslations = persistentItems
            .Where(item =>
                item.Layer == Canvas2DSceneLayer.Label &&
                item.Origin.VisualStateId is { } visualStateId &&
                visualStateIds.Contains(visualStateId) &&
                item.Origin.ProjectedObjectId is not null &&
                !NodeLabelVisualOverride.TryRead(item.PersistentAppearance, out _))
            .GroupBy(item => item.Origin.ProjectedObjectId!)
            .ToDictionary(
                static group => group.Key,
                group => DocumentGeometryBoundary.ClampTranslation(
                    group.Select(static item => item.SpatialRegion is { } region
                        ? region.MapSceneToLocal(item.Bounds)
                        : item.Bounds),
                    translation));
        var translated = 0;
        var translatedVisualStates = new HashSet<VisualStateId>();
        for (var index = 0; index < persistentItems.Length; index++)
        {
            var target = persistentItems[index];
            if (target.Origin.VisualStateId is not { } visualStateId ||
                !visualStateIds.Contains(visualStateId))
            {
                continue;
            }

            var targetTranslation = target.Layer == Canvas2DSceneLayer.Label &&
                target.Origin.ProjectedObjectId is { } projectedObjectId &&
                automaticLabelTranslations.TryGetValue(projectedObjectId, out var labelTranslation)
                    ? labelTranslation
                    : translation;
            items.Add(CreateMovePreviewItem(
                target,
                gesture,
                targetTranslation,
                translated));
            translatedVisualStates.Add(visualStateId);
            translated++;
        }

        foreach (var missing in visualStateIds.Except(translatedVisualStates))
        {
            diagnostics.Add(Warning(
                Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                $"Move gesture '{gesture.Id}' references a Visual State absent from the scene.",
                missing.Value));
        }

        return true;
    }

    private static bool TryGetMoveTargets(
        EditorGestureSnapshot gesture,
        out HashSet<VisualStateId> visualStateIds)
    {
        visualStateIds = [];
        if (gesture.Properties.TryGetValue(
                Canvas2DMoveGestureMetadata.TargetCount,
                out var countValue))
        {
            if (countValue.Kind != PropertyValueKind.Integer ||
                countValue.IntegerValue is < 1 or > int.MaxValue)
            {
                return false;
            }

            for (var index = 0; index < (int)countValue.IntegerValue; index++)
            {
                if (!gesture.Properties.TryGetValue(
                        Canvas2DMoveGestureMetadata.IndexedVisualStateId(index),
                        out var visualValue) ||
                    visualValue.Kind != PropertyValueKind.Text ||
                    !visualStateIds.Add(new VisualStateId(visualValue.TextValue)))
                {
                    return false;
                }
            }

            return true;
        }

        if (!gesture.Properties.TryGetValue(
                Canvas2DMoveGestureMetadata.TargetVisualStateId,
                out var targetValue) ||
            targetValue.Kind != PropertyValueKind.Text)
        {
            return false;
        }

        visualStateIds.Add(new VisualStateId(targetValue.TextValue));
        return true;
    }

    private static Canvas2DSceneItem CreateMovePreviewItem(
        Canvas2DSceneItem target,
        EditorGestureSnapshot gesture,
        VectorD translation,
        int ordinal)
    {
        var stableKey = $"move-preview:{gesture.Id}:{target.Id.Value}";
        var categories = target.Origin.Categories | Canvas2DSceneOriginCategory.EditorState;
        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
            Canvas2DSceneLayer.Overlay,
            3000 + ordinal,
            target.Geometry,
            new Canvas2DSceneOriginTrace(
                categories,
                target.Origin.SemanticElementId,
                target.Origin.VisualStateId,
                target.Origin.ProjectedObjectId,
                stableKey,
                target.Origin.RelatedProjectedObjectIds,
                [target.Id]),
            target.Transform.Then(Matrix2D.CreateTranslation(translation)),
            target.Clip?.Translate(translation),
            CreateMovePreviewStyle(target.Style),
            target.IsVisible,
            Canvas2DHitTestPolicy.None,
            target.PersistentAppearance,
            metadata: WithEditorKind(gesture.Kind, target.Metadata));
    }

    private static Canvas2DSceneStyle CreateMovePreviewStyle(Canvas2DSceneStyle source) =>
        new(
            source.Fill,
            source.Stroke,
            source.StrokeWidth,
            source.DashPattern,
            Math.Min(source.Opacity, 0.72d),
            source.FontFamily,
            source.FontSize);

    private static bool ComposeResizeGesturePreview(
        EditorGestureSnapshot gesture,
        ProjectedGraph graph,
        Canvas2DSceneItem[] persistentItems,
        List<Canvas2DSceneItem> items,
        List<Diagnostic> diagnostics,
        IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredNodeLabel>? measuredLabels)
    {
        if (!StringComparer.Ordinal.Equals(gesture.Kind, Canvas2DResizeGestureMetadata.Kind))
        {
            return false;
        }

        if (!TryGetResizeTarget(
                gesture,
                out var sceneObjectId,
                out var visualStateId,
                out var direction))
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidGeometry,
                $"Resize gesture '{gesture.Id}' does not identify a persistent Scene target.",
                gesture.Id));
            return true;
        }

        var target = persistentItems.FirstOrDefault(item =>
            item.Id == sceneObjectId && item.Origin.VisualStateId == visualStateId);
        if (target is null || target.Bounds.IsEmpty)
        {
            diagnostics.Add(Warning(
                Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                $"Resize gesture '{gesture.Id}' references a Scene target absent from the scene.",
                sceneObjectId.Value));
            return true;
        }

        var resizedTargetBounds = Canvas2DResizeGeometry.CalculateBounds(
            target.Bounds,
            gesture.Current - gesture.Origin,
            direction);
        var ordinal = 0;
        var emittedMeasuredLabels = new HashSet<ProjectedObjectId>();
        var labelsById = graph.Labels.ToDictionary(static label => label.Id);
        foreach (var visualItem in persistentItems.Where(item =>
                     item.Origin.VisualStateId == visualStateId))
        {
            if (visualItem.Layer == Canvas2DSceneLayer.Label &&
                visualItem.Origin.ProjectedObjectId is { } labelId &&
                measuredLabels is not null &&
                measuredLabels.TryGetValue(labelId, out var measuredLabel) &&
                measuredLabel.ResizePreview is { } measuredPreview)
            {
                if (emittedMeasuredLabels.Add(labelId))
                {
                    var relatedLabelIds = persistentItems
                        .Where(item => item.Origin.ProjectedObjectId == labelId)
                        .Select(static item => item.Id)
                        .Distinct()
                        .ToArray();
                    foreach (var previewLine in CreateMeasuredNodeLabelItems(
                                 measuredLabel,
                                 measuredPreview,
                                 visualItem.PersistentAppearance))
                    {
                        items.Add(MaterializeProcessLocalEditorOverlay(
                            CreateMeasuredResizePreviewItem(
                                previewLine,
                                gesture,
                                relatedLabelIds,
                                ordinal),
                            visualItem.SpatialRegion));
                        ordinal++;
                    }
                }

                continue;
            }

            if (visualItem.Layer == Canvas2DSceneLayer.Label &&
                visualItem.Origin.ProjectedObjectId is { } unmeasuredLabelId &&
                labelsById.TryGetValue(unmeasuredLabelId, out var unmeasuredLabel))
            {
                var hasManualOverride = NodeLabelVisualOverride.TryRead(
                    visualItem.PersistentAppearance,
                    out var manualOverride);
                if (hasManualOverride ||
                    unmeasuredLabel.NodePlacement?.Kind ==
                        NodeLabelPlacementKind.OutsideBelow)
                {
                    if (emittedMeasuredLabels.Add(unmeasuredLabelId))
                    {
                        var relatedLabelIds = persistentItems
                            .Where(item => item.Origin.ProjectedObjectId == unmeasuredLabelId)
                            .Select(static item => item.Id)
                            .Distinct()
                            .ToArray();
                        var resizedOwner = new LayoutNodeGeometry(
                            unmeasuredLabel.OwnerId,
                            resizedTargetBounds,
                            target.Transform);
                        var previewLine = hasManualOverride
                            ? CreateUnmeasuredManualNodeLabelItem(
                                unmeasuredLabel,
                                resizedOwner,
                                manualOverride!,
                                visualItem.PersistentAppearance)
                            : CreateUnmeasuredOutsideNodeLabelItem(
                                unmeasuredLabel,
                                resizedOwner,
                                visualItem.PersistentAppearance);
                        items.Add(CreateMeasuredResizePreviewItem(
                            previewLine,
                            gesture,
                            relatedLabelIds,
                            ordinal));
                        ordinal++;
                    }

                    continue;
                }
            }

            items.Add(CreateResizePreviewItem(
                visualItem,
                gesture,
                target.Bounds,
                resizedTargetBounds,
                ordinal));
            ordinal++;
        }

        if (target.Origin.SemanticElementId is { } ownerSemanticElementId)
        {
            foreach (var attachedNode in graph.Nodes
                         .Where(node =>
                             node.PlacementHint?.BoundaryAttachment?.AttachedToElementId ==
                                 ownerSemanticElementId &&
                             node.Source.VisualStateId is not null)
                         .OrderBy(static node => node.Source.VisualStateId!.Value,
                             StringComparer.Ordinal))
            {
                var attachedVisualStateId = attachedNode.Source.VisualStateId!;
                var attachedBody = persistentItems.FirstOrDefault(item =>
                    item.Layer == Canvas2DSceneLayer.Content &&
                    item.Origin.ProjectedObjectId == attachedNode.Id &&
                    item.Origin.VisualStateId == attachedVisualStateId);
                if (attachedBody is null || attachedBody.Bounds.IsEmpty)
                {
                    diagnostics.Add(Warning(
                        Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                        $"Resize gesture '{gesture.Id}' cannot preview attached node '{attachedNode.Id}'.",
                        attachedNode.Id.Value));
                    continue;
                }

                var attachedSize = new SizeD(
                    attachedBody.Bounds.Width,
                    attachedBody.Bounds.Height);
                var placement = attachedNode.PlacementHint!.BoundaryAttachment!.Placement;
                var previewBounds = placement.ResolveBounds(
                    resizedTargetBounds,
                    attachedSize);
                if (!DocumentGeometryBoundary.Contains(previewBounds))
                {
                    diagnostics.Add(Warning(
                        Canvas2DSceneDiagnosticCodes.InvalidGeometry,
                        $"Resize gesture '{gesture.Id}' would move attached node '{attachedNode.Id}' outside the Document boundary.",
                        attachedNode.Id.Value));
                    continue;
                }

                var translation = previewBounds.TopLeft - attachedBody.Bounds.TopLeft;
                foreach (var attachedItem in persistentItems.Where(item =>
                             item.Origin.VisualStateId == attachedVisualStateId))
                {
                    items.Add(CreateMovePreviewItem(
                        attachedItem,
                        gesture,
                        translation,
                        ordinal));
                    ordinal++;
                }
            }
        }

        return true;
    }

    private static Canvas2DSceneItem CreateMeasuredResizePreviewItem(
        Canvas2DSceneItem line,
        EditorGestureSnapshot gesture,
        IReadOnlyCollection<SceneObjectId> relatedSceneObjectIds,
        int ordinal)
    {
        var stableKey = $"resize-preview:{gesture.Id}:{line.Id.Value}";
        var categories = line.Origin.Categories | Canvas2DSceneOriginCategory.EditorState;
        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
            Canvas2DSceneLayer.Overlay,
            3500 + ordinal,
            line.Geometry,
            new Canvas2DSceneOriginTrace(
                categories,
                line.Origin.SemanticElementId,
                line.Origin.VisualStateId,
                line.Origin.ProjectedObjectId,
                stableKey,
                line.Origin.RelatedProjectedObjectIds,
                relatedSceneObjectIds),
            line.Transform,
            line.Clip,
            CreateMovePreviewStyle(line.Style),
            line.IsVisible,
            Canvas2DHitTestPolicy.None,
            line.PersistentAppearance,
            WithEditorKind(gesture.Kind, line.Metadata),
            line.Bounds);
    }

    private static Canvas2DSceneItem CreateResizePreviewItem(
        Canvas2DSceneItem target,
        EditorGestureSnapshot gesture,
        RectD anchorBounds,
        RectD resizedAnchorBounds,
        int ordinal)
    {
        var stableKey = $"resize-preview:{gesture.Id}:{target.Id.Value}";
        var categories = target.Origin.Categories | Canvas2DSceneOriginCategory.EditorState;
        var adjustment = Canvas2DResizeGeometry.CreateAdjustment(
            anchorBounds,
            resizedAnchorBounds);
        var resizedBounds = ScaleDocumentBounds(
            target.Bounds,
            anchorBounds,
            resizedAnchorBounds)!.Value;
        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
            Canvas2DSceneLayer.Overlay,
            3500 + ordinal,
            target.Geometry,
            new Canvas2DSceneOriginTrace(
                categories,
                target.Origin.SemanticElementId,
                target.Origin.VisualStateId,
                target.Origin.ProjectedObjectId,
                stableKey,
                target.Origin.RelatedProjectedObjectIds,
                [target.Id]),
            target.Transform.Then(adjustment),
            ScaleDocumentBounds(target.Clip, anchorBounds, resizedAnchorBounds),
            CreateMovePreviewStyle(target.Style),
            target.IsVisible,
            Canvas2DHitTestPolicy.None,
            target.PersistentAppearance,
            WithEditorKind(gesture.Kind, target.Metadata),
            resizedBounds);
    }

    private static RectD? ScaleDocumentBounds(
        RectD? source,
        RectD original,
        RectD resized)
    {
        if (source is not { } bounds)
        {
            return null;
        }

        return Canvas2DResizeGeometry.ScaleBounds(bounds, original, resized);
    }

    private static bool TryGetResizeTarget(
        EditorGestureSnapshot gesture,
        out SceneObjectId sceneObjectId,
        out VisualStateId visualStateId,
        out Canvas2DResizeDirection direction)
    {
        sceneObjectId = null!;
        visualStateId = null!;
        direction = default;
        if (!gesture.Properties.TryGetValue(
                Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                out var sceneValue) ||
            sceneValue.Kind != PropertyValueKind.Text ||
            !gesture.Properties.TryGetValue(
                Canvas2DResizeGestureMetadata.TargetVisualStateId,
                out var visualValue) ||
            visualValue.Kind != PropertyValueKind.Text ||
            !gesture.Properties.TryGetValue(
                Canvas2DResizeGestureMetadata.HandleRole,
                out var directionValue) ||
            directionValue.Kind != PropertyValueKind.Text ||
            !Canvas2DResizeGeometry.TryParseRole(directionValue.TextValue, out direction))
        {
            return false;
        }

        sceneObjectId = new SceneObjectId(sceneValue.TextValue);
        visualStateId = new VisualStateId(visualValue.TextValue);
        return true;
    }

    private static bool IsGestureTarget(
        EditorGestureSnapshot gesture,
        SceneObjectId sceneObjectId,
        VisualStateId visualStateId) =>
        TryGetResizeTarget(
            gesture,
            out var gestureSceneId,
            out var gestureVisualId,
            out _) &&
        gestureSceneId == sceneObjectId &&
        gestureVisualId == visualStateId;

    private static IEnumerable<KeyValuePair<string, PropertyValue>> CreateResizeProperties(
        SceneObjectId sceneObjectId,
        VisualStateId visualStateId,
        Canvas2DResizeDirection direction)
    {
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DResizeGestureMetadata.TargetSceneObjectId,
            PropertyValue.FromText(sceneObjectId.Value));
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DResizeGestureMetadata.TargetVisualStateId,
            PropertyValue.FromText(visualStateId.Value));
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DResizeGestureMetadata.HandleRole,
            PropertyValue.FromText(Canvas2DResizeGeometry.Role(direction)));
    }

    private static IEnumerable<Canvas2DSceneItem> CreateRouteBendHandles(
        Canvas2DSceneItem target,
        EditorGestureSnapshot? activeGesture)
    {
        if (target.Origin.VisualStateId is not { } visualStateId ||
            target.Geometry.Kind != Canvas2DSceneGeometryKind.Path ||
            !target.Metadata.TryGetValue(
                Canvas2DRouteGestureMetadata.RouteEditable,
                out var editable) ||
            editable.Kind != PropertyValueKind.Boolean ||
            !editable.BooleanValue)
        {
            yield break;
        }

        var points = Canvas2DConnectorPathMetadata.ResolveEditable(target).ToArray();
        if (points.Length < 3)
        {
            yield break;
        }

        if (activeGesture is not null &&
            StringComparer.Ordinal.Equals(activeGesture.Kind, Canvas2DRouteGestureMetadata.Kind) &&
            TryGetRouteTarget(
                activeGesture,
                out var gestureTargetId,
                out var gestureVisualId,
                out var gestureBendIndex) &&
            gestureTargetId == target.Id &&
            gestureVisualId == visualStateId &&
            gestureBendIndex > 0 &&
            gestureBendIndex < points.Length - 1)
        {
            points[gestureBendIndex] += activeGesture.Current - activeGesture.Origin;
        }

        var halfExtent = Canvas2DRouteGestureMetadata.HandleExtent / 2d;
        for (var index = 1; index < points.Length - 1; index++)
        {
            var point = target.Transform.TransformPoint(points[index]);
            var bounds = new RectD(
                point.X - halfExtent,
                point.Y - halfExtent,
                Canvas2DRouteGestureMetadata.HandleExtent,
                Canvas2DRouteGestureMetadata.HandleExtent);
            var stableKey = $"route-bend-handle:{target.Id.Value}:{index}";
            var categories = target.Origin.Categories | Canvas2DSceneOriginCategory.EditorState;
            yield return new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
                Canvas2DSceneLayer.Overlay,
                Canvas2DRouteGestureMetadata.HandleZIndex,
                Canvas2DSceneGeometry.Ellipse(bounds),
                new Canvas2DSceneOriginTrace(
                    categories,
                    target.Origin.SemanticElementId,
                    visualStateId,
                    target.Origin.ProjectedObjectId,
                    stableKey,
                    target.Origin.RelatedProjectedObjectIds,
                    [target.Id]),
                style: new Canvas2DSceneStyle("#ffffff", "#7c3aed", 2d),
                hitTestPolicy: new Canvas2DHitTestPolicy(Canvas2DHitTestMode.FillOrStroke, 2d),
                metadata: WithEditorKind(
                    Canvas2DRouteGestureMetadata.HandleKind,
                    new PropertyMap(CreateRouteProperties(
                        target.Id,
                        visualStateId,
                        index,
                        includeHandleRole: true))),
                bounds: bounds);
        }
    }

    private static IEnumerable<Canvas2DSceneItem> CreateConnectorEndpointHandles(
        Canvas2DSceneItem target,
        EditorGestureSnapshot? activeGesture)
    {
        if (target.Layer != Canvas2DSceneLayer.Connector ||
            target.Origin.VisualStateId is not { } visualStateId ||
            target.Geometry.Kind != Canvas2DSceneGeometryKind.Path)
        {
            yield break;
        }

        var logicalPath = Canvas2DConnectorPathMetadata.Resolve(target);
        if (logicalPath.Length < 2)
        {
            yield break;
        }

        Canvas2DConnectorEndpointReconnectionGestureData? reconnection = null;
        if (activeGesture is not null &&
            StringComparer.Ordinal.Equals(
                activeGesture.Kind,
                Canvas2DConnectorEndpointReconnectionGestureMetadata.Kind) &&
            Canvas2DConnectorEndpointReconnectionGestureMetadata.TryRead(
                activeGesture.Properties,
                out var decoded) &&
            decoded is not null &&
            decoded.RelationshipId == target.Origin.SemanticElementId &&
            decoded.ConnectorVisualStateId == visualStateId &&
            decoded.ConnectorSceneObjectId == target.Id)
        {
            reconnection = decoded;
        }

        if (reconnection?.EndpointKind != ConnectorEndpointKind.Source)
        {
            yield return CreateConnectorEndpointHandle(
                target,
                visualStateId,
                logicalPath,
                isStart: true);
        }

        if (reconnection?.EndpointKind != ConnectorEndpointKind.Target)
        {
            yield return CreateConnectorEndpointHandle(
                target,
                visualStateId,
                logicalPath,
                isStart: false);
        }
    }

    private static Canvas2DSceneItem CreateConnectorEndpointHandle(
        Canvas2DSceneItem target,
        VisualStateId visualStateId,
        IReadOnlyList<PointD> logicalPath,
        bool isStart)
    {
        var role = isStart
            ? Canvas2DConnectorEndpointMetadata.StartEndpointRole
            : Canvas2DConnectorEndpointMetadata.EndEndpointRole;
        var point = target.Transform.TransformPoint(
            isStart ? logicalPath[0] : logicalPath[^1]);
        var halfExtent = Canvas2DConnectorEndpointMetadata.HandleExtent / 2d;
        var bounds = new RectD(
            point.X - halfExtent,
            point.Y - halfExtent,
            Canvas2DConnectorEndpointMetadata.HandleExtent,
            Canvas2DConnectorEndpointMetadata.HandleExtent);
        var stableKey = $"connector-endpoint-handle:{target.Id.Value}:{role}";
        var categories = target.Origin.Categories | Canvas2DSceneOriginCategory.EditorState;
        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
            Canvas2DSceneLayer.Overlay,
            isStart
                ? Canvas2DConnectorEndpointMetadata.StartEndpointZIndex
                : Canvas2DConnectorEndpointMetadata.EndEndpointZIndex,
            Canvas2DSceneGeometry.Ellipse(bounds),
            new Canvas2DSceneOriginTrace(
                categories,
                target.Origin.SemanticElementId,
                visualStateId,
                target.Origin.ProjectedObjectId,
                stableKey,
                target.Origin.RelatedProjectedObjectIds,
                [target.Id]),
            style: new Canvas2DSceneStyle("#ffffff", "#7c3aed", 2d),
            hitTestPolicy: new Canvas2DHitTestPolicy(Canvas2DHitTestMode.FillOrStroke, 2d),
            metadata: WithEditorKind(
                Canvas2DConnectorEndpointMetadata.HandleKind,
                new PropertyMap(
                [
                    new KeyValuePair<string, PropertyValue>(
                        Canvas2DConnectorEndpointMetadata.HandleRole,
                        Canvas2DConnectorEndpointMetadata.RoleValue(isStart)),
                    new KeyValuePair<string, PropertyValue>(
                        Canvas2DConnectorEndpointMetadata.TargetSceneObjectId,
                        PropertyValue.FromText(target.Id.Value)),
                    new KeyValuePair<string, PropertyValue>(
                        Canvas2DConnectorEndpointMetadata.TargetVisualStateId,
                        PropertyValue.FromText(visualStateId.Value)),
                ])),
            bounds: bounds);
    }

    private static bool ComposeConnectorEndpointReconnectionGesturePreview(
        EditorGestureSnapshot gesture,
        Canvas2DSceneItem[] persistentItems,
        List<Canvas2DSceneItem> items,
        List<Diagnostic> diagnostics)
    {
        if (!StringComparer.Ordinal.Equals(
                gesture.Kind,
                Canvas2DConnectorEndpointReconnectionGestureMetadata.Kind))
        {
            return false;
        }

        if (!Canvas2DConnectorEndpointReconnectionGestureMetadata.TryRead(
                gesture.Properties,
                out var reconnection) ||
            reconnection is null)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidGeometry,
                $"Connector endpoint-reconnection gesture '{gesture.Id}' has invalid transient identity metadata.",
                gesture.Id));
            return true;
        }

        var target = persistentItems.FirstOrDefault(item =>
            item.Id == reconnection.ConnectorSceneObjectId &&
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.SemanticElementId == reconnection.RelationshipId &&
            item.Origin.VisualStateId == reconnection.ConnectorVisualStateId);
        if (target is null ||
            target.Geometry.Kind != Canvas2DSceneGeometryKind.Path)
        {
            diagnostics.Add(Warning(
                Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                $"Connector endpoint-reconnection gesture '{gesture.Id}' references a connector absent from the scene.",
                reconnection.ConnectorSceneObjectId.Value));
            return true;
        }

        var displayedPoints = Canvas2DConnectorPathMetadata.Resolve(target)
            .Select(target.Transform.TransformPoint)
            .ToArray();
        if (displayedPoints.Length < 2)
        {
            diagnostics.Add(Warning(
                Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                $"Connector endpoint-reconnection gesture '{gesture.Id}' references a connector absent from the scene.",
                reconnection.ConnectorSceneObjectId.Value));
            return true;
        }

        if (reconnection.EndpointKind == ConnectorEndpointKind.Source)
        {
            displayedPoints[0] = gesture.Current;
        }
        else
        {
            displayedPoints[^1] = gesture.Current;
        }

        var stableKey =
            $"connector-endpoint-reconnection-preview:{gesture.Id}:{target.Id.Value}";
        var categories = target.Origin.Categories | Canvas2DSceneOriginCategory.EditorState;
        items.Add(new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
            Canvas2DSceneLayer.Overlay,
            Canvas2DConnectorEndpointReconnectionGestureMetadata.PreviewZIndex,
            Canvas2DSceneGeometry.Path(displayedPoints, target.Geometry.IsClosed),
            new Canvas2DSceneOriginTrace(
                categories,
                reconnection.RelationshipId,
                reconnection.ConnectorVisualStateId,
                target.Origin.ProjectedObjectId,
                stableKey,
                target.Origin.RelatedProjectedObjectIds,
                [target.Id]),
            clip: target.Clip,
            style: new Canvas2DSceneStyle(
                stroke: "#2563eb",
                strokeWidth: 2d,
                dashPattern: [4d, 2d],
                opacity: Math.Min(target.Style.Opacity, 0.8d)),
            isVisible: target.IsVisible,
            hitTestPolicy: Canvas2DHitTestPolicy.None,
            persistentAppearance: target.PersistentAppearance,
            metadata: WithEditorKind(gesture.Kind, gesture.Properties)));

        if (reconnection.EndpointKind == ConnectorEndpointKind.Target &&
            Canvas2DConnectorArrowGeometry.Create(displayedPoints) is { } targetArrow)
        {
            var arrowStableKey =
                $"connector-endpoint-reconnection-target-arrow:{gesture.Id}:{target.Id.Value}";
            items.Add(new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForEditorState(arrowStableKey),
                Canvas2DSceneLayer.Overlay,
                Canvas2DConnectorEndpointReconnectionGestureMetadata.PreviewZIndex + 1,
                targetArrow,
                new Canvas2DSceneOriginTrace(
                    categories,
                    reconnection.RelationshipId,
                    reconnection.ConnectorVisualStateId,
                    target.Origin.ProjectedObjectId,
                    arrowStableKey,
                    target.Origin.RelatedProjectedObjectIds,
                    [target.Id]),
                style: new Canvas2DSceneStyle(
                    fill: "#2563eb",
                    stroke: "#2563eb",
                    strokeWidth: 1d,
                    opacity: Math.Min(target.Style.Opacity, 0.8d)),
                isVisible: target.IsVisible,
                hitTestPolicy: Canvas2DHitTestPolicy.None,
                persistentAppearance: target.PersistentAppearance,
                metadata: WithEditorKind(
                    gesture.Kind,
                    new PropertyMap(gesture.Properties.Append(
                        new KeyValuePair<string, PropertyValue>(
                            Canvas2DConnectorArrowMetadata.TargetArrow,
                            Canvas2DConnectorArrowMetadata.TargetArrowValue))))));
        }

        return true;
    }

    private static bool ComposeRouteGesturePreview(
        EditorGestureSnapshot gesture,
        Canvas2DSceneItem[] persistentItems,
        List<Canvas2DSceneItem> items,
        List<Diagnostic> diagnostics,
        IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredConnectorLabel>?
            measuredConnectorLabels)
    {
        if (!StringComparer.Ordinal.Equals(gesture.Kind, Canvas2DRouteGestureMetadata.Kind))
        {
            return false;
        }

        if (!TryGetRouteTarget(
                gesture,
                out var sceneObjectId,
                out var visualStateId,
                out var bendIndex))
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidGeometry,
                $"Route gesture '{gesture.Id}' does not identify one persistent bend.",
                gesture.Id));
            return true;
        }

        var target = persistentItems.FirstOrDefault(item =>
            item.Id == sceneObjectId && item.Origin.VisualStateId == visualStateId);
        if (target is null ||
            target.Geometry.Kind != Canvas2DSceneGeometryKind.Path)
        {
            diagnostics.Add(Warning(
                Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                $"Route gesture '{gesture.Id}' references a bend absent from the scene.",
                sceneObjectId.Value));
            return true;
        }

        var points = Canvas2DConnectorPathMetadata.ResolveEditable(target).ToArray();
        if (bendIndex <= 0 || bendIndex >= points.Length - 1)
        {
            diagnostics.Add(Warning(
                Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                $"Route gesture '{gesture.Id}' references a bend absent from the scene.",
                sceneObjectId.Value));
            return true;
        }

        points[bendIndex] += gesture.Current - gesture.Origin;
        var stableKey = $"route-preview:{gesture.Id}:{target.Id.Value}";
        var categories = target.Origin.Categories | Canvas2DSceneOriginCategory.EditorState;
        items.Add(new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
            Canvas2DSceneLayer.Overlay,
            3600,
            Canvas2DSceneGeometry.Path(points, target.Geometry.IsClosed),
            new Canvas2DSceneOriginTrace(
                categories,
                target.Origin.SemanticElementId,
                visualStateId,
                target.Origin.ProjectedObjectId,
                stableKey,
                target.Origin.RelatedProjectedObjectIds,
                [target.Id]),
            target.Transform,
            target.Clip,
            CreateMovePreviewStyle(target.Style),
            target.IsVisible,
            Canvas2DHitTestPolicy.None,
            target.PersistentAppearance,
            WithEditorKind(gesture.Kind, target.Metadata)));

        if (target.Origin.ProjectedObjectId is { } connectorId &&
            measuredConnectorLabels is not null)
        {
            foreach (var measuredLabel in measuredConnectorLabels.Values.Where(label =>
                         label.Label.OwnerId == connectorId))
            {
                var previewAnchor = Canvas2DConnectorPathGeometry.ResolvePoint(
                    points,
                    measuredLabel.Placement.PathPosition) + measuredLabel.Placement.Offset;
                var translation = previewAnchor - measuredLabel.Anchor;
                var labelItems = persistentItems.Where(item =>
                    item.Layer == Canvas2DSceneLayer.Label &&
                    item.Origin.ProjectedObjectId == measuredLabel.Label.Id).ToArray();
                for (var index = 0; index < labelItems.Length; index++)
                {
                    items.Add(CreateTranslatedLabelPreview(
                        labelItems[index],
                        gesture,
                        translation,
                        "route-label-preview",
                        3650 + index));
                }
            }
        }

        return true;
    }

    private static bool ComposeConnectorLabelGesturePreview(
        EditorGestureSnapshot gesture,
        Canvas2DSceneItem[] persistentItems,
        List<Canvas2DSceneItem> items,
        List<Diagnostic> diagnostics)
    {
        if (!StringComparer.Ordinal.Equals(gesture.Kind, Canvas2DLabelGestureMetadata.Kind))
        {
            return false;
        }

        if (!gesture.Properties.TryGetValue(
                Canvas2DLabelGestureMetadata.TargetLabelProjectedObjectId,
                out var labelIdValue) ||
            labelIdValue.Kind != PropertyValueKind.Text ||
            !gesture.Properties.TryGetValue(
                Canvas2DLabelGestureMetadata.TargetVisualStateId,
                out var visualIdValue) ||
            visualIdValue.Kind != PropertyValueKind.Text)
        {
            diagnostics.Add(Error(
                Canvas2DSceneDiagnosticCodes.InvalidGeometry,
                $"Connector label gesture '{gesture.Id}' does not identify one label target.",
                gesture.Id));
            return true;
        }

        var labelId = new ProjectedObjectId(labelIdValue.TextValue);
        var visualId = new VisualStateId(visualIdValue.TextValue);
        var targets = persistentItems.Where(item =>
            item.Origin.ProjectedObjectId == labelId &&
            item.Origin.VisualStateId == visualId &&
            item.Layer == Canvas2DSceneLayer.Label).ToArray();
        if (targets.Length == 0)
        {
            diagnostics.Add(Warning(
                Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                $"Connector label gesture '{gesture.Id}' references a label absent from the scene.",
                labelId.Value));
            return true;
        }

        var translation = gesture.Current - gesture.Origin;
        for (var index = 0; index < targets.Length; index++)
        {
            items.Add(CreateTranslatedLabelPreview(
                targets[index],
                gesture,
                translation,
                "connector-label-preview",
                3700 + index));
        }

        return true;
    }

    private static bool ComposeNodeLabelGesturePreview(
        EditorGestureSnapshot gesture,
        ProjectedGraph graph,
        Canvas2DSceneItem[] persistentItems,
        List<Canvas2DSceneItem> items,
        List<Diagnostic> diagnostics,
        IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredNodeLabel>?
            measuredLabels)
    {
        if (!StringComparer.Ordinal.Equals(
                gesture.Kind,
                Canvas2DNodeLabelGestureMetadata.Kind))
        {
            return false;
        }

        if (!TryGetNodeLabelGestureTarget(
                gesture,
                out var nodeSceneObjectId,
                out var labelSceneObjectId,
                out var labelId,
                out var visualStateId,
                out var operation,
                out var direction))
        {
            diagnostics.Add(Warning(
                Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                $"Node-label gesture '{gesture.Id}' has invalid target metadata.",
                gesture.Id));
            return true;
        }

        var persistentLabelItems = persistentItems.Where(item =>
            item.Origin.ProjectedObjectId == labelId &&
            item.Origin.VisualStateId == visualStateId &&
            item.Layer == Canvas2DSceneLayer.Label).ToArray();
        if (persistentLabelItems.Length == 0)
        {
            diagnostics.Add(Warning(
                Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                $"Node-label gesture '{gesture.Id}' has no persistent Scene representation.",
                labelId.Value));
            return true;
        }

        var labelInteractionTarget = persistentLabelItems.SingleOrDefault(item =>
            item.Id == labelSceneObjectId &&
            HasBooleanMetadata(
                item,
                Canvas2DNodeLabelGestureMetadata.InteractionCapable));
        var nodeTarget = persistentItems.SingleOrDefault(item =>
            item.Id == nodeSceneObjectId &&
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId);
        if (labelInteractionTarget is null ||
            nodeTarget is null ||
            !labelInteractionTarget.Origin.RelatedSceneObjectIds.Contains(nodeTarget.Id))
        {
            diagnostics.Add(Warning(
                Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                $"Node-label gesture '{gesture.Id}' references stale Scene ownership.",
                labelId.Value));
            return true;
        }

        var relatedIds = persistentLabelItems
            .Select(static item => item.Id)
            .Append(nodeTarget.Id)
            .Distinct()
            .ToArray();
        var persistentAppearance = persistentLabelItems[0].PersistentAppearance;
        if (measuredLabels is null)
        {
            var projectedLabel = graph.Labels.SingleOrDefault(label =>
                label.Id == labelId &&
                label.OwnerId == nodeTarget.Origin.ProjectedObjectId &&
                label.Source.VisualStateId == visualStateId);
            if (projectedLabel is null)
            {
                diagnostics.Add(Warning(
                    Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                    $"Node-label gesture '{gesture.Id}' references a projected label absent from the graph.",
                    labelId.Value));
                return true;
            }

            var delta = gesture.Current - gesture.Origin;
            var previewBounds = operation == Canvas2DNodeLabelGestureOperation.Move
                ? labelInteractionTarget.Bounds.Translate(delta)
                : Canvas2DResizeGeometry.CalculateBounds(
                    labelInteractionTarget.Bounds,
                    delta,
                    direction,
                    NodeLabelVisualOverride.MinimumWidth,
                    NodeLabelVisualOverride.MinimumHeight);
            var labelCenter = new PointD(
                previewBounds.Left + (previewBounds.Width / 2d),
                previewBounds.Top + (previewBounds.Height / 2d));
            var ownerCenter = new PointD(
                nodeTarget.Bounds.Left + (nodeTarget.Bounds.Width / 2d),
                nodeTarget.Bounds.Top + (nodeTarget.Bounds.Height / 2d));
            var previewOverride = new NodeLabelVisualOverride(
                labelCenter.X - ownerCenter.X,
                labelCenter.Y - ownerCenter.Y,
                previewBounds.Width,
                previewBounds.Height);
            var previewItem = CreateUnmeasuredManualNodeLabelItem(
                projectedLabel,
                new LayoutNodeGeometry(
                    projectedLabel.OwnerId,
                    nodeTarget.Bounds,
                    nodeTarget.Transform),
                previewOverride,
                persistentAppearance);
            items.Add(CreateMeasuredResizePreviewItem(
                previewItem,
                gesture,
                relatedIds,
                0));
            return true;
        }

        if (!measuredLabels.TryGetValue(labelId, out var measuredLabel) ||
            measuredLabel.Label.Source.VisualStateId != visualStateId ||
            measuredLabel.NodeLabelPreview is not { } preview)
        {
            diagnostics.Add(Warning(
                Canvas2DSceneDiagnosticCodes.StaleEditorStateReference,
                $"Node-label gesture '{gesture.Id}' references a label absent from the measured scene.",
                labelId.Value));
            return true;
        }

        var ordinal = 0;
        foreach (var previewItem in CreateMeasuredNodeLabelItems(
                     measuredLabel,
                     preview,
                     persistentAppearance))
        {
            items.Add(MaterializeProcessLocalEditorOverlay(
                CreateMeasuredResizePreviewItem(
                    previewItem,
                    gesture,
                    relatedIds,
                    ordinal),
                labelInteractionTarget.SpatialRegion));
            ordinal++;
        }

        return true;
    }

    private static Canvas2DSceneItem CreateTranslatedLabelPreview(
        Canvas2DSceneItem target,
        EditorGestureSnapshot gesture,
        VectorD translation,
        string role,
        int zIndex)
    {
        var stableKey = $"{role}:{gesture.Id}:{target.Id.Value}";
        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForEditorState(stableKey),
            Canvas2DSceneLayer.Overlay,
            zIndex,
            target.Geometry,
            new Canvas2DSceneOriginTrace(
                target.Origin.Categories | Canvas2DSceneOriginCategory.EditorState,
                target.Origin.SemanticElementId,
                target.Origin.VisualStateId,
                target.Origin.ProjectedObjectId,
                stableKey,
                target.Origin.RelatedProjectedObjectIds,
                [target.Id]),
            target.Transform.Then(Matrix2D.CreateTranslation(translation)),
            target.Clip?.Translate(translation),
            CreateMovePreviewStyle(target.Style),
            target.IsVisible,
            Canvas2DHitTestPolicy.None,
            target.PersistentAppearance,
            WithEditorKind(gesture.Kind, target.Metadata));
    }

    private static IEnumerable<KeyValuePair<string, PropertyValue>> CreateRouteProperties(
        SceneObjectId sceneObjectId,
        VisualStateId visualStateId,
        int bendIndex,
        bool includeHandleRole)
    {
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DRouteGestureMetadata.TargetSceneObjectId,
            PropertyValue.FromText(sceneObjectId.Value));
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DRouteGestureMetadata.TargetVisualStateId,
            PropertyValue.FromText(visualStateId.Value));
        yield return new KeyValuePair<string, PropertyValue>(
            Canvas2DRouteGestureMetadata.BendIndex,
            PropertyValue.FromInteger(bendIndex));
        if (includeHandleRole)
        {
            yield return new KeyValuePair<string, PropertyValue>(
                Canvas2DRouteGestureMetadata.HandleRole,
                PropertyValue.FromText(Canvas2DRouteGestureMetadata.BendRole));
        }
    }

    private static bool TryGetRouteTarget(
        EditorGestureSnapshot gesture,
        out SceneObjectId sceneObjectId,
        out VisualStateId visualStateId,
        out int bendIndex)
    {
        sceneObjectId = null!;
        visualStateId = null!;
        bendIndex = -1;
        if (!gesture.Properties.TryGetValue(
                Canvas2DRouteGestureMetadata.TargetSceneObjectId,
                out var sceneValue) ||
            sceneValue.Kind != PropertyValueKind.Text ||
            !gesture.Properties.TryGetValue(
                Canvas2DRouteGestureMetadata.TargetVisualStateId,
                out var visualValue) ||
            visualValue.Kind != PropertyValueKind.Text ||
            !gesture.Properties.TryGetValue(
                Canvas2DRouteGestureMetadata.BendIndex,
                out var indexValue) ||
            indexValue.Kind != PropertyValueKind.Integer ||
            indexValue.IntegerValue is < 1 or > int.MaxValue)
        {
            return false;
        }

        sceneObjectId = new SceneObjectId(sceneValue.TextValue);
        visualStateId = new VisualStateId(visualValue.TextValue);
        bendIndex = (int)indexValue.IntegerValue;
        return true;
    }

    private static bool TryGetNodeLabelGestureTarget(
        EditorGestureSnapshot gesture,
        out SceneObjectId nodeSceneObjectId,
        out SceneObjectId labelSceneObjectId,
        out ProjectedObjectId labelId,
        out VisualStateId visualStateId,
        out Canvas2DNodeLabelGestureOperation operation,
        out Canvas2DResizeDirection resizeDirection)
    {
        nodeSceneObjectId = null!;
        labelSceneObjectId = null!;
        labelId = null!;
        visualStateId = null!;
        operation = default;
        resizeDirection = Canvas2DResizeDirection.SouthEast;
        if (!gesture.Properties.TryGetValue(
                Canvas2DNodeLabelGestureMetadata.TargetNodeSceneObjectId,
                out var nodeSceneValue) ||
            nodeSceneValue.Kind != PropertyValueKind.Text ||
            string.IsNullOrWhiteSpace(nodeSceneValue.TextValue) ||
            !gesture.Properties.TryGetValue(
                Canvas2DNodeLabelGestureMetadata.TargetLabelSceneObjectId,
                out var labelSceneValue) ||
            labelSceneValue.Kind != PropertyValueKind.Text ||
            string.IsNullOrWhiteSpace(labelSceneValue.TextValue) ||
            !gesture.Properties.TryGetValue(
                Canvas2DNodeLabelGestureMetadata.TargetLabelProjectedObjectId,
                out var labelValue) ||
            labelValue.Kind != PropertyValueKind.Text ||
            string.IsNullOrWhiteSpace(labelValue.TextValue) ||
            !gesture.Properties.TryGetValue(
                Canvas2DNodeLabelGestureMetadata.TargetVisualStateId,
                out var visualValue) ||
            visualValue.Kind != PropertyValueKind.Text ||
            string.IsNullOrWhiteSpace(visualValue.TextValue) ||
            !gesture.Properties.TryGetValue(
                Canvas2DNodeLabelGestureMetadata.Operation,
                out var operationValue) ||
            operationValue.Kind != PropertyValueKind.Text)
        {
            return false;
        }

        if (StringComparer.Ordinal.Equals(
                operationValue.TextValue,
                Canvas2DNodeLabelGestureMetadata.MoveOperation))
        {
            operation = Canvas2DNodeLabelGestureOperation.Move;
        }
        else if (StringComparer.Ordinal.Equals(
                     operationValue.TextValue,
                     Canvas2DNodeLabelGestureMetadata.ResizeOperation) &&
                 gesture.Properties.TryGetValue(
                     Canvas2DNodeLabelGestureMetadata.ResizeDirection,
                     out var directionValue) &&
                 directionValue.Kind == PropertyValueKind.Text &&
                 Canvas2DResizeGeometry.TryParseRole(
                     directionValue.TextValue,
                     out resizeDirection))
        {
            operation = Canvas2DNodeLabelGestureOperation.Resize;
        }
        else
        {
            return false;
        }

        nodeSceneObjectId = new SceneObjectId(nodeSceneValue.TextValue);
        labelSceneObjectId = new SceneObjectId(labelSceneValue.TextValue);
        labelId = new ProjectedObjectId(labelValue.TextValue);
        visualStateId = new VisualStateId(visualValue.TextValue);
        return true;
    }

    private static bool HasBooleanMetadata(Canvas2DSceneItem item, string key) =>
        item.Metadata.TryGetValue(key, out var value) &&
        value.Kind == PropertyValueKind.Boolean &&
        value.BooleanValue;

    private static bool TryGetTextMetadata(
        Canvas2DSceneItem item,
        string key,
        out string value)
    {
        if (item.Metadata.TryGetValue(key, out var property) &&
            property.Kind == PropertyValueKind.Text &&
            !string.IsNullOrWhiteSpace(property.TextValue))
        {
            value = property.TextValue;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
