using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    private static Canvas2DSceneStyle NodeLabelStyle { get; } =
        new(fill: "#000000");

    private static Canvas2DSceneStyle ConnectorLabelStyle { get; } =
        new(fill: "#000000");

    private async ValueTask<IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredNodeLabel>?>
        CreateMeasuredNodeLabelLayoutsAsync(
            ProjectedGraph graph,
            LayoutResult layout,
            VisualModelSnapshot visualModel,
            EditorStateSnapshot editorState,
            Canvas2DTextLayoutService layoutService,
        CancellationToken cancellationToken)
    {
        var nodes = layout.Nodes.ToDictionary(static node => node.ProjectedObjectId);
        var visuals = visualModel.VisualStates.ToDictionary(static visual => visual.Id);
        var layouts = new Dictionary<ProjectedObjectId, Canvas2DMeasuredNodeLabel>();
        var activeGesture = editorState.ActiveGesture;
        VisualStateId? resizeVisualStateId = null;
        var resizeDirection = default(Canvas2DResizeDirection);
        var hasResizePreview = activeGesture is not null &&
            StringComparer.Ordinal.Equals(
                activeGesture.Kind,
                Canvas2DResizeGestureMetadata.Kind) &&
            TryGetResizeTarget(
                activeGesture,
                out _,
                out resizeVisualStateId,
                out resizeDirection);
        foreach (var label in graph.Labels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!nodes.TryGetValue(label.OwnerId, out var ownerNode))
            {
                continue;
            }

            var placement = label.NodePlacement ?? NodeLabelPlacement.InsideCentered;
            NodeLabelVisualOverride? manualOverride = null;
            var hasManualOverride = label.Source.VisualStateId is { } labelVisualStateId &&
                visuals.TryGetValue(labelVisualStateId, out var labelVisualState) &&
                NodeLabelVisualOverride.TryRead(
                    labelVisualState.Properties,
                    out manualOverride);
            var current = hasManualOverride
                ? await CreateMeasuredManualLabelBoxAsync(
                    label.Text,
                    ownerNode.Bounds,
                    ownerNode.Transform,
                    manualOverride!,
                    layoutService,
                    cancellationToken).ConfigureAwait(false)
                : await CreateMeasuredLabelBoxAsync(
                    label.Text,
                    ownerNode.Bounds,
                    ownerNode.Transform,
                    placement,
                    layoutService,
                    cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                return null;
            }

            Canvas2DMeasuredLabelBox? preview = null;
            Canvas2DMeasuredLabelBox? nodeLabelPreview = null;
            if (activeGesture is not null &&
                StringComparer.Ordinal.Equals(
                    activeGesture.Kind,
                    Canvas2DNodeLabelGestureMetadata.Kind) &&
                TryGetNodeLabelGestureTarget(
                    activeGesture,
                    out _,
                    out _,
                    out var targetLabelId,
                    out var targetVisualId,
                    out var operation,
                    out var labelResizeDirection) &&
                targetLabelId == label.Id &&
                label.Source.VisualStateId == targetVisualId)
            {
                var previewBounds = operation == Canvas2DNodeLabelGestureOperation.Move
                    ? current.PlacementBounds.Translate(
                        activeGesture.Current - activeGesture.Origin)
                    : Canvas2DResizeGeometry.CalculateBounds(
                        current.PlacementBounds,
                        activeGesture.Current - activeGesture.Origin,
                        labelResizeDirection,
                        NodeLabelVisualOverride.MinimumWidth,
                        NodeLabelVisualOverride.MinimumHeight);
                nodeLabelPreview = await CreateMeasuredManualLabelBoxAsync(
                    label.Text,
                    previewBounds,
                    ownerNode.Transform,
                    layoutService,
                    cancellationToken).ConfigureAwait(false);
                if (nodeLabelPreview is null)
                {
                    return null;
                }
            }
            else if (hasResizePreview &&
                ownerNode.ProjectedObjectId == label.OwnerId &&
                label.Source.VisualStateId == resizeVisualStateId)
            {
                var previewBounds = Canvas2DResizeGeometry.CalculateBounds(
                    ownerNode.Bounds,
                    activeGesture!.Current - activeGesture.Origin,
                    resizeDirection);
                preview = hasManualOverride
                    ? await CreateMeasuredManualLabelBoxAsync(
                        label.Text,
                        previewBounds,
                        ownerNode.Transform,
                        manualOverride!,
                        layoutService,
                        cancellationToken).ConfigureAwait(false)
                    : await CreateMeasuredLabelBoxAsync(
                        label.Text,
                        previewBounds,
                        ownerNode.Transform,
                        placement,
                        layoutService,
                        cancellationToken).ConfigureAwait(false);
                if (preview is null)
                {
                    return null;
                }
            }

            layouts.Add(
                label.Id,
                new Canvas2DMeasuredNodeLabel(
                    label,
                    current,
                    preview,
                    nodeLabelPreview,
                    hasManualOverride));
        }

        return layouts;
    }

    private async ValueTask<Canvas2DMeasuredLabelBox?>
        CreateMeasuredManualLabelBoxAsync(
            string text,
            RectD ownerBounds,
            Matrix2D ownerTransform,
            NodeLabelVisualOverride visualOverride,
            Canvas2DTextLayoutService layoutService,
            CancellationToken cancellationToken)
    {
        return await CreateMeasuredManualLabelBoxAsync(
            text,
            DocumentGeometryBoundary.Clamp(visualOverride.ResolveBounds(ownerBounds)),
            ownerTransform,
            layoutService,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Canvas2DMeasuredLabelBox?>
        CreateMeasuredManualLabelBoxAsync(
            string text,
            RectD labelBounds,
            Matrix2D ownerTransform,
            Canvas2DTextLayoutService layoutService,
            CancellationToken cancellationToken)
    {
        var horizontalScale = Length(
            ownerTransform.TransformVector(new VectorD(1d, 0d)));
        var requestedLineHeight = NodeLabelStyle.FontSize *
            _nodeLabelLayout.LineHeightMultiplier;
        var textLayout = await layoutService.LayoutAsync(
            text,
            NodeLabelStyle,
            requestedLineHeight,
            labelBounds.Width,
            horizontalScale,
            cancellationToken).ConfigureAwait(false);
        return textLayout is null
            ? null
            : new Canvas2DMeasuredLabelBox(
                labelBounds,
                labelBounds,
                ownerTransform,
                textLayout);
    }

    private async ValueTask<Canvas2DMeasuredLabelBox?> CreateMeasuredLabelBoxAsync(
        string text,
        RectD ownerBounds,
        Matrix2D ownerTransform,
        NodeLabelPlacement placement,
        Canvas2DTextLayoutService layoutService,
        CancellationToken cancellationToken)
    {
        var contentBounds = placement.Kind == NodeLabelPlacementKind.InsideCentered
            ? InsetSafely(
                ownerBounds,
                _nodeLabelLayout.HorizontalPadding,
                _nodeLabelLayout.VerticalPadding)
            : new RectD(
                ownerBounds.Left +
                    ((ownerBounds.Width - placement.MaximumWidth!.Value) / 2d),
                ownerBounds.Bottom + placement.Gap,
                placement.MaximumWidth.Value,
                0d);
        if (placement.Kind == NodeLabelPlacementKind.OutsideBelow)
        {
            contentBounds = DocumentGeometryBoundary.Clamp(contentBounds);
        }
        var horizontalBasis = ownerTransform.TransformVector(new VectorD(1d, 0d));
        var horizontalScale = Length(horizontalBasis);
        var requestedLineHeight = NodeLabelStyle.FontSize *
            _nodeLabelLayout.LineHeightMultiplier;
        var textLayout = await layoutService.LayoutAsync(
            text,
            NodeLabelStyle,
            requestedLineHeight,
            contentBounds.Width,
            horizontalScale,
            cancellationToken).ConfigureAwait(false);
        if (textLayout is null)
        {
            return null;
        }

        var verticalBasis = ownerTransform.TransformVector(new VectorD(0d, 1d));
        var verticalScale = Length(verticalBasis);
        var measuredBlockHeight = textLayout.Lines.Sum(line =>
            line.Metrics.LineHeight * verticalScale);
        var placementBounds = placement.Kind == NodeLabelPlacementKind.InsideCentered
            ? ownerBounds
            : new RectD(
                contentBounds.X,
                contentBounds.Y,
                contentBounds.Width,
                measuredBlockHeight);
        if (placement.Kind == NodeLabelPlacementKind.OutsideBelow)
        {
            placementBounds = DocumentGeometryBoundary.Clamp(placementBounds);
        }
        return new Canvas2DMeasuredLabelBox(
            placementBounds,
            placement.Kind == NodeLabelPlacementKind.InsideCentered
                ? contentBounds
                : placementBounds,
            ownerTransform,
            textLayout);
    }

    private static RectD InsetSafely(
        RectD bounds,
        double horizontalPadding,
        double verticalPadding)
    {
        var horizontalInset = Math.Min(horizontalPadding, bounds.Width / 2d);
        var verticalInset = Math.Min(verticalPadding, bounds.Height / 2d);
        return new RectD(
            bounds.X + horizontalInset,
            bounds.Y + verticalInset,
            Math.Max(0d, bounds.Width - (2d * horizontalInset)),
            Math.Max(0d, bounds.Height - (2d * verticalInset)));
    }

    private static double Length(VectorD vector)
    {
        var scale = Math.Max(Math.Abs(vector.X), Math.Abs(vector.Y));
        if (scale == 0d)
        {
            return 0d;
        }

        var x = vector.X / scale;
        var y = vector.Y / scale;
        return scale * Math.Sqrt((x * x) + (y * y));
    }

    private static IEnumerable<Canvas2DSceneItem> CreateMeasuredNodeLabelItems(
        Canvas2DMeasuredNodeLabel measuredLabel,
        Canvas2DMeasuredLabelBox box,
        IEnumerable<KeyValuePair<string, PropertyValue>> persistentAppearance)
    {
        var persistentAppearanceMap = new PropertyMap(persistentAppearance);
        var interactionCapable =
            measuredLabel.Label.NodeInteractionPolicy ==
                NodeLabelInteractionPolicy.MoveAndResize &&
            measuredLabel.Label.Source.VisualStateId is not null;
        var lines = box.TextLayout.Lines;
        var verticalBasis = box.OwnerTransform.TransformVector(new VectorD(0d, 1d));
        var verticalScale = Length(verticalBasis);
        var documentLineHeights = lines
            .Select(line => line.Metrics.LineHeight * verticalScale)
            .ToArray();
        var totalHeight = documentLineHeights.Sum();
        var nextTop = box.PlacementBounds.Top +
            ((box.PlacementBounds.Height - totalHeight) / 2d);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var documentLineHeight = documentLineHeights[index];
            var documentAnchor = new PointD(
                box.PlacementBounds.Left + (box.PlacementBounds.Width / 2d),
                nextTop + (documentLineHeight / 2d));
            nextTop += documentLineHeight;
            var localAnchor = new PointD(0d, 0d);
            var localBounds = new RectD(
                -(line.Metrics.Width / 2d),
                -(line.Metrics.LineHeight / 2d),
                line.Metrics.Width,
                line.Metrics.LineHeight);
            var localKey = lines.Length == 1 ? "label" : $"label-line:{index}";
            var id = Canvas2DSceneObjectIdentity.ForProjected(
                measuredLabel.Label.Id,
                localKey);
            var origin = CreateOrigin(measuredLabel.Label, [measuredLabel.Label.OwnerId]);
            yield return new Canvas2DSceneItem(
                id,
                Canvas2DSceneLayer.Label,
                index,
                Canvas2DSceneGeometry.Text(
                    localBounds,
                    line.Text,
                    localAnchor,
                    Canvas2DTextAlignment.Center,
                    Canvas2DTextBaseline.Middle),
                new Canvas2DSceneOriginTrace(
                    origin.Categories,
                    origin.SemanticElementId,
                    origin.VisualStateId,
                    origin.ProjectedObjectId,
                    $"label-line:{index}:{measuredLabel.Label.Id.Value}",
                    origin.RelatedProjectedObjectIds),
                transform: CreateMeasuredLineTransform(
                    box.OwnerTransform,
                    localAnchor,
                    documentAnchor),
                clip: box.ClipBounds,
                style: NodeLabelStyle,
                hitTestPolicy: interactionCapable
                    ? Canvas2DHitTestPolicy.None
                    : new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Bounds),
                persistentAppearance: persistentAppearanceMap,
                metadata: interactionCapable
                    ? null
                    : Canvas2DResizeGestureMetadata.MoveAndResizeCapability);
        }

        if (interactionCapable)
        {
            yield return CreateNodeLabelInteractionBox(
                measuredLabel.Label,
                box.PlacementBounds,
                box.ClipBounds,
                persistentAppearanceMap);
        }
    }

    private static Canvas2DSceneItem CreateNodeLabelInteractionBox(
        ProjectedLabel label,
        RectD bounds,
        RectD clipBounds,
        PropertyMap persistentAppearance)
    {
        var nodeSceneObjectId = Canvas2DSceneObjectIdentity.ForProjected(
            label.OwnerId,
            "node");
        var id = Canvas2DSceneObjectIdentity.ForProjected(
            label.Id,
            "label-interaction");
        var origin = CreateOrigin(label, [label.OwnerId]);
        return new Canvas2DSceneItem(
            id,
            Canvas2DSceneLayer.Label,
            1,
            Canvas2DSceneGeometry.Rectangle(bounds),
            new Canvas2DSceneOriginTrace(
                origin.Categories,
                origin.SemanticElementId,
                origin.VisualStateId,
                origin.ProjectedObjectId,
                $"node-label-interaction:{label.Id.Value}",
                origin.RelatedProjectedObjectIds,
                [nodeSceneObjectId]),
            clip: clipBounds,
            style: new Canvas2DSceneStyle(opacity: 0d),
            hitTestPolicy: new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Bounds),
            persistentAppearance: persistentAppearance,
            metadata: CreateNodeLabelInteractionMetadata(
                nodeSceneObjectId,
                id,
                label),
            bounds: bounds);
    }

    private static IEnumerable<KeyValuePair<string, PropertyValue>>
        CreateNodeLabelInteractionMetadata(
            SceneObjectId nodeSceneObjectId,
            SceneObjectId labelSceneObjectId,
            ProjectedLabel label) =>
        [
            new KeyValuePair<string, PropertyValue>(
                Canvas2DNodeLabelGestureMetadata.InteractionCapable,
                Canvas2DNodeLabelGestureMetadata.InteractionCapableValue),
            new KeyValuePair<string, PropertyValue>(
                Canvas2DNodeLabelGestureMetadata.TargetNodeSceneObjectId,
                PropertyValue.FromText(nodeSceneObjectId.Value)),
            new KeyValuePair<string, PropertyValue>(
                Canvas2DNodeLabelGestureMetadata.TargetLabelSceneObjectId,
                PropertyValue.FromText(labelSceneObjectId.Value)),
            new KeyValuePair<string, PropertyValue>(
                Canvas2DNodeLabelGestureMetadata.TargetVisualStateId,
                PropertyValue.FromText(label.Source.VisualStateId!.Value)),
            new KeyValuePair<string, PropertyValue>(
                Canvas2DNodeLabelGestureMetadata.TargetLabelProjectedObjectId,
                PropertyValue.FromText(label.Id.Value)),
        ];

    private static Matrix2D CreateMeasuredLineTransform(
        Matrix2D ownerTransform,
        PointD localAnchor,
        PointD documentAnchor) =>
        new(
            ownerTransform.M11,
            ownerTransform.M12,
            ownerTransform.M21,
            ownerTransform.M22,
            documentAnchor.X - ((ownerTransform.M11 * localAnchor.X) +
                (ownerTransform.M21 * localAnchor.Y)),
            documentAnchor.Y - ((ownerTransform.M12 * localAnchor.X) +
                (ownerTransform.M22 * localAnchor.Y)));

    private async ValueTask<
        IReadOnlyDictionary<ProjectedObjectId, Canvas2DMeasuredConnectorLabel>?>
        CreateMeasuredConnectorLabelLayoutsAsync(
            ProjectedGraph graph,
            RoutingResult routing,
            VisualModelSnapshot visualModel,
            Canvas2DTextLayoutService layoutService,
            CancellationToken cancellationToken)
    {
        var edges = graph.Edges.ToDictionary(static edge => edge.Id);
        var routes = routing.Routes.ToDictionary(static route => route.ProjectedEdgeId);
        var visuals = visualModel.VisualStates.ToDictionary(static visual => visual.Id);
        var layouts = new Dictionary<ProjectedObjectId, Canvas2DMeasuredConnectorLabel>();
        foreach (var label in graph.Labels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!edges.TryGetValue(label.OwnerId, out var edge) ||
                !routes.TryGetValue(edge.Id, out var route))
            {
                continue;
            }

            var placement = edge.Source.VisualStateId is { } visualStateId &&
                visuals.TryGetValue(visualStateId, out var visual)
                    ? ConnectorLabelPlacement.Resolve(visual.Properties)
                    : ConnectorLabelPlacement.Default;
            var requestedLineHeight = ConnectorLabelStyle.FontSize *
                _connectorLabelLayout.LineHeightMultiplier;
            var textLayout = await layoutService.LayoutAsync(
                label.Text,
                ConnectorLabelStyle,
                requestedLineHeight,
                _connectorLabelLayout.MaximumWidth,
                1d,
                cancellationToken).ConfigureAwait(false);
            if (textLayout is null)
            {
                return null;
            }

            var routePoint = Canvas2DConnectorPathGeometry.ResolvePoint(
                route.Path,
                placement.PathPosition);
            layouts.Add(
                label.Id,
                new Canvas2DMeasuredConnectorLabel(
                    label,
                    route,
                    placement,
                    routePoint + placement.Offset,
                    textLayout));
        }

        return layouts;
    }

    private static IEnumerable<Canvas2DSceneItem> CreateMeasuredConnectorLabelItems(
        Canvas2DMeasuredConnectorLabel measuredLabel,
        SceneObjectId connectorSceneObjectId,
        IEnumerable<KeyValuePair<string, PropertyValue>> persistentAppearance)
    {
        var lines = measuredLabel.TextLayout.Lines;
        var totalHeight = lines.Sum(static line => line.Metrics.LineHeight);
        var nextTop = measuredLabel.Anchor.Y - (totalHeight / 2d);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var documentAnchor = new PointD(
                measuredLabel.Anchor.X,
                nextTop + (line.Metrics.LineHeight / 2d));
            nextTop += line.Metrics.LineHeight;
            var localAnchor = new PointD(0d, 0d);
            var localBounds = new RectD(
                -(line.Metrics.Width / 2d),
                -(line.Metrics.LineHeight / 2d),
                line.Metrics.Width,
                line.Metrics.LineHeight);
            var localKey = lines.Length == 1 ? "label" : $"label-line:{index}";
            var origin = CreateOrigin(
                measuredLabel.Label,
                [measuredLabel.Label.OwnerId]);
            var stableKey = $"connector-label-line:{index}:{measuredLabel.Label.Id.Value}";
            yield return new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForProjected(
                    measuredLabel.Label.Id,
                    localKey),
                Canvas2DSceneLayer.Label,
                index,
                Canvas2DSceneGeometry.Text(
                    localBounds,
                    line.Text,
                    localAnchor,
                    Canvas2DTextAlignment.Center,
                    Canvas2DTextBaseline.Middle),
                new Canvas2DSceneOriginTrace(
                    origin.Categories,
                    origin.SemanticElementId,
                    origin.VisualStateId,
                    origin.ProjectedObjectId,
                    stableKey,
                    origin.RelatedProjectedObjectIds,
                    [connectorSceneObjectId]),
                transform: Matrix2D.CreateTranslation(
                    documentAnchor.X,
                    documentAnchor.Y),
                style: ConnectorLabelStyle,
                hitTestPolicy: new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Bounds),
                persistentAppearance: persistentAppearance,
                metadata: measuredLabel.Label.Source.VisualStateId is null
                    ? null
                    : CreateConnectorLabelMetadata(
                        connectorSceneObjectId,
                        measuredLabel.Label));
        }
    }

    private static Canvas2DSceneItem CreateUnmeasuredConnectorLabelItem(
        ProjectedLabel label,
        RoutedConnectorGeometry route,
        ConnectorLabelPlacement placement,
        SceneObjectId connectorSceneObjectId,
        IEnumerable<KeyValuePair<string, PropertyValue>> persistentAppearance)
    {
        var anchor = Canvas2DConnectorPathGeometry.ResolvePoint(
            route.Path,
            placement.PathPosition) + placement.Offset;
        var lineHeight = ConnectorLabelStyle.FontSize *
            Canvas2DConnectorLabelLayoutConfiguration.Default.LineHeightMultiplier;
        var localBounds = new RectD(
            -(Canvas2DConnectorLabelLayoutConfiguration.Default.MaximumWidth / 2d),
            -(lineHeight / 2d),
            Canvas2DConnectorLabelLayoutConfiguration.Default.MaximumWidth,
            lineHeight);
        var localAnchor = new PointD(0d, 0d);
        var origin = CreateOrigin(label, [label.OwnerId]);
        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label"),
            Canvas2DSceneLayer.Label,
            0,
            Canvas2DSceneGeometry.Text(
                localBounds,
                label.Text,
                localAnchor,
                Canvas2DTextAlignment.Center,
                Canvas2DTextBaseline.Middle),
            new Canvas2DSceneOriginTrace(
                origin.Categories,
                origin.SemanticElementId,
                origin.VisualStateId,
                origin.ProjectedObjectId,
                $"connector-label-line:0:{label.Id.Value}",
                origin.RelatedProjectedObjectIds,
                [connectorSceneObjectId]),
            transform: Matrix2D.CreateTranslation(anchor.X, anchor.Y),
            style: ConnectorLabelStyle,
            hitTestPolicy: new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Bounds),
            persistentAppearance: persistentAppearance,
            metadata: label.Source.VisualStateId is null
                ? null
                : CreateConnectorLabelMetadata(connectorSceneObjectId, label));
    }

    private static IEnumerable<KeyValuePair<string, PropertyValue>>
        CreateConnectorLabelMetadata(
            SceneObjectId connectorSceneObjectId,
            ProjectedLabel label) =>
        [
            new KeyValuePair<string, PropertyValue>(
                Canvas2DLabelGestureMetadata.LabelMoveCapable,
                Canvas2DLabelGestureMetadata.MoveCapableValue),
            new KeyValuePair<string, PropertyValue>(
                Canvas2DLabelGestureMetadata.TargetConnectorSceneObjectId,
                PropertyValue.FromText(connectorSceneObjectId.Value)),
            new KeyValuePair<string, PropertyValue>(
                Canvas2DLabelGestureMetadata.TargetVisualStateId,
                PropertyValue.FromText(label.Source.VisualStateId!.Value)),
            new KeyValuePair<string, PropertyValue>(
                Canvas2DLabelGestureMetadata.TargetLabelProjectedObjectId,
                PropertyValue.FromText(label.Id.Value)),
        ];
}

internal sealed record Canvas2DMeasuredNodeLabel(
    ProjectedLabel Label,
    Canvas2DMeasuredLabelBox Current,
    Canvas2DMeasuredLabelBox? ResizePreview,
    Canvas2DMeasuredLabelBox? NodeLabelPreview,
    bool HasManualOverride);

internal sealed record Canvas2DMeasuredLabelBox(
    RectD PlacementBounds,
    RectD ClipBounds,
    Matrix2D OwnerTransform,
    Canvas2DTextLayout TextLayout);

internal sealed record Canvas2DMeasuredConnectorLabel(
    ProjectedLabel Label,
    RoutedConnectorGeometry Route,
    ConnectorLabelPlacement Placement,
    PointD Anchor,
    Canvas2DTextLayout TextLayout);
