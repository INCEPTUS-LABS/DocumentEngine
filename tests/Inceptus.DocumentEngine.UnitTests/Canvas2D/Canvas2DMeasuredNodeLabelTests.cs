using System.Text;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DMeasuredNodeLabelTests
{
    private const string LongLabel = "Beta sales order verification process";

    [Fact]
    public async Task WrappedLinesAreIndividuallyCentredClippedAndOwnerTraced()
    {
        var built = await BuildAsync(LongLabel, new RectD(10d, 20d, 100d, 70d));
        var lines = LabelLines(built.Scene, built.Label.Id);

        Assert.True(lines.Length > 1);
        Assert.Equal(Enumerable.Range(0, lines.Length), lines.Select(static line => line.ZIndex));
        Assert.All(lines, line =>
        {
            Assert.Equal(Canvas2DTextAlignment.Center, line.Geometry.TextAlignment);
            Assert.Equal(Canvas2DTextBaseline.Middle, line.Geometry.TextBaseline);
            Assert.Equal(new RectD(18d, 28d, 84d, 54d), line.Clip);
            Assert.Equal(built.Label.Source.SemanticElementId, line.Origin.SemanticElementId);
            Assert.Equal(built.Label.Source.VisualStateId, line.Origin.VisualStateId);
            Assert.Equal(built.Label.Id, line.Origin.ProjectedObjectId);
            Assert.Contains(built.Label.OwnerId, line.Origin.RelatedProjectedObjectIds);
            Assert.StartsWith("label-line:", line.Origin.StableSourceKey, StringComparison.Ordinal);
            Assert.Equal(60d, DocumentAnchor(line).X, 9);
        });
        Assert.Equal(55d, (DocumentAnchor(lines[0]).Y + DocumentAnchor(lines[^1]).Y) / 2d, 9);

        var hitTest = new Canvas2DSceneHitTestService();
        Assert.All(lines, line =>
        {
            var hit = Assert.IsType<Canvas2DSceneHitTestResult>(
                hitTest.HitTest(built.Scene, DocumentAnchor(line)));
            Assert.Equal(built.Label.Source.VisualStateId, hit.Origin.VisualStateId);
        });
    }

    [Fact]
    public async Task EquivalentMeasuredLayoutHasStableLineIdentityAndIgnoresZoom()
    {
        var first = await BuildAsync(
            LongLabel,
            new RectD(10d, 20d, 100d, 70d),
            new EditorStateSnapshot(viewport: new ViewportSnapshot(0.75d, default)));
        var second = await BuildAsync(
            LongLabel,
            new RectD(10d, 20d, 100d, 70d),
            new EditorStateSnapshot(viewport: new ViewportSnapshot(1.5d, new VectorD(30d, -12d))));
        var firstLines = LabelLines(first.Scene, first.Label.Id);
        var secondLines = LabelLines(second.Scene, second.Label.Id);

        Assert.Equal(firstLines.Select(static line => line.Id), secondLines.Select(static line => line.Id));
        Assert.Equal(
            firstLines.Select(static line => line.Geometry.Content),
            secondLines.Select(static line => line.Geometry.Content));
        Assert.Equal(firstLines.Select(static line => line.Transform), secondLines.Select(static line => line.Transform));
        Assert.Equal(firstLines.Select(static line => line.Clip), secondLines.Select(static line => line.Clip));
    }

    [Fact]
    public async Task OutsideBelowLogicalPlacementAndWrappingIgnoreSeventyFiveAndOneFiftyPercentZoom()
    {
        var placement = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 120d);
        var first = await BuildAsync(
            LongLabel,
            new RectD(100d, 100d, 48d, 48d),
            new EditorStateSnapshot(viewport: new ViewportSnapshot(0.75d, default)),
            placement: placement);
        var second = await BuildAsync(
            LongLabel,
            new RectD(100d, 100d, 48d, 48d),
            new EditorStateSnapshot(
                viewport: new ViewportSnapshot(1.5d, new VectorD(30d, -12d))),
            placement: placement);
        var firstLines = LabelLines(first.Scene, first.Label.Id);
        var secondLines = LabelLines(second.Scene, second.Label.Id);

        Assert.Equal(
            firstLines.Select(static line => line.Id),
            secondLines.Select(static line => line.Id));
        Assert.Equal(
            firstLines.Select(static line => line.Geometry.Content),
            secondLines.Select(static line => line.Geometry.Content));
        Assert.Equal(
            firstLines.Select(static line => line.Transform),
            secondLines.Select(static line => line.Transform));
        Assert.Equal(
            firstLines.Select(static line => line.Clip),
            secondLines.Select(static line => line.Clip));
    }

    [Fact]
    public async Task OutsideBelowUsesMeasuredExternalWidthClipIdentityAndOwnerHit()
    {
        var nodeBounds = new RectD(100d, 100d, 48d, 48d);
        var placement = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 120d);
        var external = await BuildAsync(LongLabel, nodeBounds, placement: placement);
        var inside = await BuildAsync(LongLabel, nodeBounds);
        var lines = LabelLines(external.Scene, external.Label.Id);

        Assert.Equal(2, lines.Length);
        Assert.True(lines.Length < LabelLines(inside.Scene, inside.Label.Id).Length);
        Assert.Equal(Enumerable.Range(0, lines.Length), lines.Select(static line => line.ZIndex));
        var expectedClip = new RectD(64d, 156d, 120d, 28.8d);
        Assert.All(lines, line =>
        {
            AssertRectEqual(expectedClip, line.Clip);
            Assert.Equal(124d, DocumentAnchor(line).X, 9);
            Assert.Equal(Canvas2DTextAlignment.Center, line.Geometry.TextAlignment);
            Assert.Equal(Canvas2DTextBaseline.Middle, line.Geometry.TextBaseline);
            Assert.Equal(external.Label.Source.SemanticElementId, line.Origin.SemanticElementId);
            Assert.Equal(external.Label.Source.VisualStateId, line.Origin.VisualStateId);
            Assert.Equal(external.Label.Id, line.Origin.ProjectedObjectId);
            Assert.Contains(external.Label.OwnerId, line.Origin.RelatedProjectedObjectIds);
            Assert.StartsWith("label-line:", line.Origin.StableSourceKey, StringComparison.Ordinal);
            Assert.True(line.Bounds.Top >= nodeBounds.Bottom + placement.Gap - 1e-9);
            Assert.True(line.Bounds.Bottom <= expectedClip.Bottom + 1e-9);
            Assert.True(line.Bounds.Width <= placement.MaximumWidth!.Value + 1e-9);
        });

        Assert.Equal(nodeBounds.Bottom + placement.Gap, lines[0].Bounds.Top, 9);
        var hitTest = new Canvas2DSceneHitTestService();
        Assert.All(lines, line =>
        {
            var hit = Assert.IsType<Canvas2DSceneHitTestResult>(
                hitTest.HitTest(external.Scene, DocumentAnchor(line)));
            Assert.Equal(line.Id, hit.SceneObjectId);
            Assert.Equal(external.Label.Source.VisualStateId, hit.Origin.VisualStateId);
            Assert.Equal(external.Label.Id, hit.Origin.ProjectedObjectId);
        });
    }

    [Fact]
    public async Task OutsideBelowMovePreviewTranslatesMeasuredLinesAndExternalClip()
    {
        var bounds = new RectD(100d, 100d, 48d, 48d);
        var placement = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 120d);
        var initial = await BuildAsync(LongLabel, bounds, placement: placement);
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(initial.Owner.Id, "node");
        var translation = new VectorD(27d, -19d);
        var gesture = new EditorGestureSnapshot(
            "test:outside-label-move",
            Canvas2DMoveGestureMetadata.Kind,
            new PointD(0d, 0d),
            new PointD(translation.X, translation.Y),
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DMoveGestureMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(nodeId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DMoveGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(initial.Owner.Source.VisualStateId!.Value)),
            ]);
        var preview = await BuildAsync(
            LongLabel,
            bounds,
            new EditorStateSnapshot(activeGesture: gesture),
            placement: placement);
        var persistentLines = LabelLines(preview.Scene, preview.Label.Id);
        var previewLines = preview.Scene.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.ProjectedObjectId == preview.Label.Id &&
            item.Origin.StableSourceKey?.StartsWith(
                "move-preview:",
                StringComparison.Ordinal) == true).ToArray();

        Assert.Equal(persistentLines.Length, previewLines.Length);
        for (var index = 0; index < persistentLines.Length; index++)
        {
            Assert.Equal(DocumentAnchor(persistentLines[index]) + translation,
                DocumentAnchor(previewLines[index]));
            Assert.Equal(persistentLines[index].Bounds.Translate(translation),
                previewLines[index].Bounds);
            Assert.Equal(persistentLines[index].Clip!.Value.Translate(translation),
                previewLines[index].Clip);
        }
    }

    [Fact]
    public async Task OutsideBelowResizePreviewRemeasuresFromDisplayedBounds()
    {
        var bounds = new RectD(100d, 100d, 48d, 48d);
        var placement = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 120d);
        var initial = await BuildAsync(LongLabel, bounds, placement: placement);
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(initial.Owner.Id, "node");
        var delta = new VectorD(32d, 24d);
        var direction = Canvas2DResizeDirection.SouthEast;
        var gesture = new EditorGestureSnapshot(
            "test:outside-label-resize",
            Canvas2DResizeGestureMetadata.Kind,
            new PointD(0d, 0d),
            new PointD(delta.X, delta.Y),
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(nodeId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(initial.Owner.Source.VisualStateId!.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.HandleRole,
                    PropertyValue.FromText(Canvas2DResizeGeometry.Role(direction))),
            ]);
        var preview = await BuildAsync(
            LongLabel,
            bounds,
            new EditorStateSnapshot(activeGesture: gesture),
            placement: placement);
        var previewBounds = Canvas2DResizeGeometry.CalculateBounds(bounds, delta, direction);
        var previewLines = preview.Scene.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.ProjectedObjectId == preview.Label.Id &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true).ToArray();

        Assert.Equal(2, previewLines.Length);
        var expectedClip = new RectD(
            previewBounds.Left + ((previewBounds.Width - placement.MaximumWidth!.Value) / 2d),
            previewBounds.Bottom + placement.Gap,
            placement.MaximumWidth.Value,
            28.8d);
        Assert.All(previewLines, line =>
        {
            AssertRectEqual(expectedClip, line.Clip);
            Assert.Equal(previewBounds.Left + (previewBounds.Width / 2d),
                DocumentAnchor(line).X,
                9);
        });
        Assert.Equal(expectedClip.Top, previewLines[0].Bounds.Top, 9);
    }

    public static IEnumerable<object[]> ResizeCases =>
    [
        [(int)Canvas2DResizeDirection.East, new VectorD(-55d, 0d)],
        [(int)Canvas2DResizeDirection.West, new VectorD(55d, 0d)],
        [(int)Canvas2DResizeDirection.NorthWest, new VectorD(55d, 10d)],
        [(int)Canvas2DResizeDirection.SouthEast, new VectorD(-55d, -10d)],
    ];

    [Theory]
    [MemberData(nameof(ResizeCases))]
    public async Task ResizePreviewReflowsFromCurrentPreviewWidthInEveryDirection(
        int directionValue,
        VectorD delta)
    {
        var direction = (Canvas2DResizeDirection)directionValue;
        var bounds = new RectD(10d, 20d, 100d, 70d);
        var initial = await BuildAsync(LongLabel, bounds);
        var node = initial.Owner;
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node");
        var gesture = new EditorGestureSnapshot(
            $"test:wrap:{direction}",
            Canvas2DResizeGestureMetadata.Kind,
            new PointD(0d, 0d),
            new PointD(delta.X, delta.Y),
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(nodeId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(node.Source.VisualStateId!.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.HandleRole,
                    PropertyValue.FromText(Canvas2DResizeGeometry.Role(direction))),
            ]);
        var preview = await BuildAsync(
            LongLabel,
            bounds,
            new EditorStateSnapshot(
                selection: [node.Source.VisualStateId],
                activeGesture: gesture));
        var baseLines = LabelLines(preview.Scene, preview.Label.Id);
        var previewLines = preview.Scene.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.ProjectedObjectId == preview.Label.Id &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true).ToArray();
        var previewBounds = Canvas2DResizeGeometry.CalculateBounds(bounds, delta, direction);

        Assert.True(previewLines.Length > baseLines.Length);
        Assert.All(previewLines, line =>
            Assert.Equal(previewBounds.Left + (previewBounds.Width / 2d), DocumentAnchor(line).X, 9));
        Assert.Equal(
            previewBounds.Top + (previewBounds.Height / 2d),
            (DocumentAnchor(previewLines[0]).Y + DocumentAnchor(previewLines[^1]).Y) / 2d,
            9);
    }

    [Theory]
    [InlineData(0.5d, 0d, 0d, 0.5d)]
    [InlineData(0.5d, 0.1d, 0.2d, 0.5d)]
    [InlineData(0.5d, 0.25d, 0d, 0d)]
    public async Task MeasuredLinesPreserveOwnerLinearTransformAndDisplayedCentre(
        double m11,
        double m12,
        double m21,
        double m22)
    {
        var bounds = new RectD(10d, 20d, 100d, 70d);
        var transform = new Matrix2D(m11, m12, m21, m22, bounds.X, bounds.Y);
        var built = await BuildAsync(LongLabel, bounds, transform: transform);
        var lines = LabelLines(built.Scene, built.Label.Id);

        Assert.All(lines, line =>
        {
            Assert.Equal(m11, line.Transform.M11);
            Assert.Equal(m12, line.Transform.M12);
            Assert.Equal(m21, line.Transform.M21);
            Assert.Equal(m22, line.Transform.M22);
            Assert.Equal(bounds.Left + (bounds.Width / 2d), DocumentAnchor(line).X, 9);
        });
        Assert.Equal(
            bounds.Top + (bounds.Height / 2d),
            (DocumentAnchor(lines[0]).Y + DocumentAnchor(lines[^1]).Y) / 2d,
            9);
        var horizontalScale = Math.Sqrt((m11 * m11) + (m12 * m12));
        Assert.All(lines, line =>
            Assert.True((line.Geometry.Bounds.Width * horizontalScale) <= 84d + 1e-9));
    }

    [Fact]
    public async Task ManualOverrideWinsAndUsesOneStableTransparentOwnedInteractionBox()
    {
        var nodeBounds = new RectD(100d, 100d, 48d, 48d);
        var placement = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 120d);
        var visualOverride = new NodeLabelVisualOverride(
            offsetX: 70d,
            offsetY: -20d,
            width: 70d,
            height: 28.8d);
        var automatic = await BuildAsync(
            LongLabel,
            nodeBounds,
            placement: placement,
            interactionPolicy: NodeLabelInteractionPolicy.MoveAndResize);
        var manual = await BuildAsync(
            LongLabel,
            nodeBounds,
            new EditorStateSnapshot(selection: [automatic.Owner.Source.VisualStateId!]),
            placement: placement,
            interactionPolicy: NodeLabelInteractionPolicy.MoveAndResize,
            visualOverride: visualOverride);
        var expectedBounds = new RectD(159d, 89.6d, 70d, 28.8d);
        var automaticBody = NodeLabelBody(automatic.Scene, automatic.Label.Id);
        var manualBody = NodeLabelBody(manual.Scene, manual.Label.Id);
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(manual.Owner.Id, "node");
        var lines = LabelLines(manual.Scene, manual.Label.Id);

        Assert.Equal(automaticBody.Id, manualBody.Id);
        Assert.Equal(
            Canvas2DSceneObjectIdentity.ForProjected(
                manual.Label.Id,
                "label-interaction"),
            manualBody.Id);
        AssertRectEqual(expectedBounds, manualBody.Bounds);
        Assert.Equal(0d, manualBody.Style.Opacity);
        Assert.Equal(Canvas2DHitTestMode.Bounds, manualBody.HitTestPolicy.Mode);
        Assert.Equal(manual.Label.Source.SemanticElementId, manualBody.Origin.SemanticElementId);
        Assert.Equal(manual.Label.Source.VisualStateId, manualBody.Origin.VisualStateId);
        Assert.Equal(manual.Label.Id, manualBody.Origin.ProjectedObjectId);
        Assert.Contains(nodeId, manualBody.Origin.RelatedSceneObjectIds);
        Assert.All(lines, line =>
        {
            Assert.Equal(Canvas2DHitTestMode.None, line.HitTestPolicy.Mode);
            AssertRectEqual(expectedBounds, line.Clip);
        });

        var hit = Assert.IsType<Canvas2DSceneHitTestResult>(
            new Canvas2DSceneHitTestService().HitTest(manual.Scene, Center(expectedBounds)));
        Assert.Equal(manualBody.Id, hit.SceneObjectId);
        Assert.Equal(manual.Label.Source.VisualStateId, hit.Origin.VisualStateId);

        var zones = manual.Scene.Items.Where(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "node-label-resize-zone:",
                StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(8, zones.Length);
        Assert.All(zones, zone =>
        {
            Assert.Equal(Canvas2DSceneLayer.Overlay, zone.Layer);
            Assert.Equal(0d, zone.Style.Opacity);
            Assert.Equal(Canvas2DHitTestMode.Bounds, zone.HitTestPolicy.Mode);
            Assert.Contains(manualBody.Id, zone.Origin.RelatedSceneObjectIds);
            Assert.Contains(nodeId, zone.Origin.RelatedSceneObjectIds);
        });
    }

    [Fact]
    public async Task EditableLabelMovePreviewTranslatesDisplayedBoxAndRemeasuredTextOnly()
    {
        var nodeBounds = new RectD(100d, 100d, 48d, 48d);
        var placement = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 120d);
        var initial = await BuildAsync(
            LongLabel,
            nodeBounds,
            placement: placement,
            interactionPolicy: NodeLabelInteractionPolicy.MoveAndResize);
        var body = NodeLabelBody(initial.Scene, initial.Label.Id);
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(initial.Owner.Id, "node");
        var delta = new VectorD(45d, -30d);
        var gesture = NodeLabelGesture(initial, body, nodeId, delta, "move");
        var preview = await BuildAsync(
            LongLabel,
            nodeBounds,
            new EditorStateSnapshot(
                selection: [initial.Owner.Source.VisualStateId!],
                activeGesture: gesture),
            placement: placement,
            interactionPolicy: NodeLabelInteractionPolicy.MoveAndResize);
        var expectedBounds = body.Bounds.Translate(delta);
        var previewLines = NodeLabelPreviewLines(preview.Scene, preview.Label.Id);

        Assert.NotEmpty(previewLines);
        Assert.All(previewLines, line => AssertRectEqual(expectedBounds, line.Clip));
        Assert.Equal(body.Bounds, NodeLabelBody(preview.Scene, preview.Label.Id).Bounds);
        Assert.All(previewLines, line =>
            Assert.Equal(Canvas2DHitTestMode.None, line.HitTestPolicy.Mode));
    }

    [Fact]
    public async Task EditableLabelResizePreviewRewrapsAndMovesZonesWithTheLogicalBox()
    {
        var nodeBounds = new RectD(100d, 100d, 48d, 48d);
        var placement = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 120d);
        var visualOverride = new NodeLabelVisualOverride(0d, 55d, 120d, 57.6d);
        var initial = await BuildAsync(
            LongLabel,
            nodeBounds,
            placement: placement,
            interactionPolicy: NodeLabelInteractionPolicy.MoveAndResize,
            visualOverride: visualOverride);
        var body = NodeLabelBody(initial.Scene, initial.Label.Id);
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(initial.Owner.Id, "node");
        var delta = new VectorD(65d, 0d);
        var direction = Canvas2DResizeDirection.West;
        var gesture = NodeLabelGesture(
            initial,
            body,
            nodeId,
            delta,
            "resize",
            direction);
        var preview = await BuildAsync(
            LongLabel,
            nodeBounds,
            new EditorStateSnapshot(
                selection: [initial.Owner.Source.VisualStateId!],
                activeGesture: gesture),
            placement: placement,
            interactionPolicy: NodeLabelInteractionPolicy.MoveAndResize,
            visualOverride: visualOverride);
        var expectedBounds = Canvas2DResizeGeometry.CalculateBounds(
            body.Bounds,
            delta,
            direction,
            NodeLabelVisualOverride.MinimumWidth,
            NodeLabelVisualOverride.MinimumHeight);
        var previewLines = NodeLabelPreviewLines(preview.Scene, preview.Label.Id);
        var westZone = preview.Scene.Items.Single(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "node-label-resize-zone:west:",
                StringComparison.Ordinal) == true);

        Assert.True(previewLines.Length > LabelLines(initial.Scene, initial.Label.Id).Length);
        Assert.All(previewLines, line => AssertRectEqual(expectedBounds, line.Clip));
        Assert.Equal(
            Canvas2DResizeGeometry.InteractionBounds(
                expectedBounds,
                Canvas2DResizeDirection.West),
            westZone.Bounds);
    }

    [Fact]
    public async Task OwningNodeResizePreviewPreservesManualOffsetAndBoxSize()
    {
        var nodeBounds = new RectD(100d, 100d, 48d, 48d);
        var placement = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 120d);
        var visualOverride = new NodeLabelVisualOverride(60d, 45d, 90d, 43.2d);
        var initial = await BuildAsync(
            LongLabel,
            nodeBounds,
            placement: placement,
            interactionPolicy: NodeLabelInteractionPolicy.MoveAndResize,
            visualOverride: visualOverride);
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(initial.Owner.Id, "node");
        var delta = new VectorD(32d, 24d);
        var gesture = new EditorGestureSnapshot(
            "test:manual-label-owner-resize",
            Canvas2DResizeGestureMetadata.Kind,
            default,
            new PointD(delta.X, delta.Y),
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(nodeId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(initial.Owner.Source.VisualStateId!.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.HandleRole,
                    PropertyValue.FromText("southeast")),
            ]);
        var preview = await BuildAsync(
            LongLabel,
            nodeBounds,
            new EditorStateSnapshot(
                selection: [initial.Owner.Source.VisualStateId!],
                activeGesture: gesture),
            placement: placement,
            interactionPolicy: NodeLabelInteractionPolicy.MoveAndResize,
            visualOverride: visualOverride);
        var resizedNode = Canvas2DResizeGeometry.CalculateBounds(
            nodeBounds,
            delta,
            Canvas2DResizeDirection.SouthEast);
        var expectedCenter = Center(resizedNode) +
            new VectorD(visualOverride.OffsetX, visualOverride.OffsetY);
        var expectedLabelBounds = new RectD(
            expectedCenter.X - (visualOverride.Width / 2d),
            expectedCenter.Y - (visualOverride.Height / 2d),
            visualOverride.Width,
            visualOverride.Height);

        Assert.All(
            NodeLabelPreviewLines(preview.Scene, preview.Label.Id),
            line => AssertRectEqual(expectedLabelBounds, line.Clip));
    }

    [Theory]
    [InlineData(Canvas2DNodeLabelGestureMetadata.MoveOperation)]
    [InlineData(Canvas2DNodeLabelGestureMetadata.ResizeOperation)]
    public async Task PresentedMeasuredNodeLabelPreviewMapsProcessLocalPrimitivesExactlyOnce(
        string operation)
    {
        var nodeBounds = new RectD(100d, 100d, 48d, 48d);
        var translation = new VectorD(320d, 180d);
        var placement = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 120d);
        var visualOverride = new NodeLabelVisualOverride(0d, 55d, 120d, 57.6d);
        var delta = StringComparer.Ordinal.Equals(
            operation,
            Canvas2DNodeLabelGestureMetadata.MoveOperation)
                ? new VectorD(45d, -30d)
                : new VectorD(65d, 0d);
        var direction = Canvas2DResizeDirection.West;

        var localInitial = await BuildAsync(
            LongLabel,
            nodeBounds,
            placement: placement,
            interactionPolicy: NodeLabelInteractionPolicy.MoveAndResize,
            visualOverride: visualOverride);
        var localGesture = NodeLabelGesture(
            localInitial,
            NodeLabelBody(localInitial.Scene, localInitial.Label.Id),
            NodeBody(localInitial.Scene, localInitial.Owner.Id).Id,
            delta,
            operation,
            direction);
        var localPreview = await BuildAsync(
            LongLabel,
            nodeBounds,
            new EditorStateSnapshot(
                selection: [localInitial.Owner.Source.VisualStateId!],
                activeGesture: localGesture),
            placement: placement,
            interactionPolicy: NodeLabelInteractionPolicy.MoveAndResize,
            visualOverride: visualOverride);

        var presentedInitial = await BuildAsync(
            LongLabel,
            nodeBounds,
            placement: placement,
            interactionPolicy: NodeLabelInteractionPolicy.MoveAndResize,
            visualOverride: visualOverride,
            presentationTranslation: translation);
        var presentedGesture = NodeLabelGesture(
            presentedInitial,
            NodeLabelBody(presentedInitial.Scene, presentedInitial.Label.Id),
            NodeBody(presentedInitial.Scene, presentedInitial.Owner.Id).Id,
            delta,
            operation,
            direction);
        var presentedPreview = await BuildAsync(
            LongLabel,
            nodeBounds,
            new EditorStateSnapshot(
                selection: [presentedInitial.Owner.Source.VisualStateId!],
                activeGesture: presentedGesture),
            placement: placement,
            interactionPolicy: NodeLabelInteractionPolicy.MoveAndResize,
            visualOverride: visualOverride,
            presentationTranslation: translation);
        var region = Assert.Single(
            presentedPreview.Scene.SpatialPresentationPlan!.Regions);

        AssertProcessLocalPreviewMaterializedOnce(
            NodeLabelPreviewItems(localPreview.Scene, localPreview.Label.Id),
            NodeLabelPreviewItems(presentedPreview.Scene, presentedPreview.Label.Id),
            region);
        Assert.Equal(
            NodeBody(presentedInitial.Scene, presentedInitial.Owner.Id),
            NodeBody(presentedPreview.Scene, presentedPreview.Owner.Id));
        Assert.Equal(presentedInitial.Layout, presentedPreview.Layout);
        Assert.Equal(presentedInitial.VisualModel, presentedPreview.VisualModel);
    }

    [Fact]
    public async Task PresentedOwningNodeResizeMapsMeasuredLabelAndSceneBasedNodePreviewOnce()
    {
        var nodeBounds = new RectD(100d, 100d, 80d, 60d);
        var translation = new VectorD(260d, 340d);
        var placement = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 120d);
        var delta = new VectorD(35d, 25d);

        var localInitial = await BuildAsync(LongLabel, nodeBounds, placement: placement);
        var localGesture = OwningNodeResizeGesture(
            localInitial,
            NodeBody(localInitial.Scene, localInitial.Owner.Id),
            delta);
        var localPreview = await BuildAsync(
            LongLabel,
            nodeBounds,
            new EditorStateSnapshot(
                selection: [localInitial.Owner.Source.VisualStateId!],
                activeGesture: localGesture),
            placement: placement);

        var presentedInitial = await BuildAsync(
            LongLabel,
            nodeBounds,
            placement: placement,
            presentationTranslation: translation);
        var presentedGesture = OwningNodeResizeGesture(
            presentedInitial,
            NodeBody(presentedInitial.Scene, presentedInitial.Owner.Id),
            delta);
        var presentedPreview = await BuildAsync(
            LongLabel,
            nodeBounds,
            new EditorStateSnapshot(
                selection: [presentedInitial.Owner.Source.VisualStateId!],
                activeGesture: presentedGesture),
            placement: placement,
            presentationTranslation: translation);
        var region = Assert.Single(
            presentedPreview.Scene.SpatialPresentationPlan!.Regions);

        AssertProcessLocalPreviewMaterializedOnce(
            NodeLabelPreviewItems(localPreview.Scene, localPreview.Label.Id),
            NodeLabelPreviewItems(presentedPreview.Scene, presentedPreview.Label.Id),
            region);
        AssertPresentedItemMapsLocalGeometryOnce(
            Assert.Single(ResizePreviewItems(localPreview.Scene, localPreview.Owner.Id)),
            Assert.Single(ResizePreviewItems(
                presentedPreview.Scene,
                presentedPreview.Owner.Id)),
            region);
        Assert.Equal(
            NodeBody(presentedInitial.Scene, presentedInitial.Owner.Id),
            NodeBody(presentedPreview.Scene, presentedPreview.Owner.Id));
        Assert.Equal(presentedInitial.Layout, presentedPreview.Layout);
        Assert.Equal(presentedInitial.VisualModel, presentedPreview.VisualModel);
    }

    private static async Task<BuiltLabelScene> BuildAsync(
        string text,
        RectD bounds,
        EditorStateSnapshot? editorState = null,
        Matrix2D? transform = null,
        NodeLabelPlacement? placement = null,
        NodeLabelInteractionPolicy interactionPolicy = NodeLabelInteractionPolicy.Fixed,
        NodeLabelVisualOverride? visualOverride = null,
        VectorD? presentationTranslation = null)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var owner = inputs.Graph.Nodes[0];
        var label = new ProjectedLabel(
            owner.Source,
            owner.Id,
            text,
            nodePlacement: placement,
            nodeInteractionPolicy: interactionPolicy);
        var graph = new ProjectedGraph(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            [owner],
            labels: [label]);
        var layout = new LayoutResult(
            inputs.Layout.DocumentId,
            inputs.Layout.SourceRevision,
            inputs.Layout.AlgorithmId,
            new LayoutComputation(
            [
                new LayoutNodeGeometry(
                    owner.Id,
                    bounds,
                    transform ?? Matrix2D.CreateTranslation(bounds.X, bounds.Y)),
            ]));
        var routing = new RoutingResult(
            inputs.Routing.DocumentId,
            inputs.Routing.SourceRevision,
            layout.AlgorithmId,
            inputs.Routing.RoutingAlgorithmId,
            RoutingComputation.Empty);
        var metrics = new FixedMetricsService();
        var visualModel = visualOverride is null
            ? inputs.VisualModel
            : new VisualModelSnapshot(
                inputs.VisualModel.DocumentId,
                inputs.VisualModel.Revision,
                inputs.VisualModel.VisualStates.Select(visual =>
                    visual.Id == owner.Source.VisualStateId
                        ? new VisualStateSnapshot(
                            visual.Id,
                            visual.SemanticElementId,
                            visual.Position,
                            visual.Size,
                            visual.PlacementMode,
                            visual.Route,
                            NodeLabelVisualOverride.UpdateProperties(
                                visual.Properties,
                                visualOverride),
                            visual.ConnectorAnchors,
                            visual.SourceAnchorId,
                            visual.TargetAnchorId)
                        : visual));
        var builder = new Canvas2DSceneBuilder();
        Canvas2DSceneBuildResult result;
        if (presentationTranslation is { } translation)
        {
            var document = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
            var activeScopeId = new DocumentScopeId(document.DocumentId.Value);
            builder = new Canvas2DSceneBuilder(
                contributors:
                [
                    new Canvas2DSceneContributorRegistration(
                        new Canvas2DSceneContributorDescriptor(
                            new Canvas2DSceneContributorId(
                                "test:measured-preview:presentation"),
                            "1"),
                        new TranslatedSpatialPresentationContributor(
                            owner.Source.VisualStateId!,
                            translation),
                        Canvas2DSceneContributionStage.Presentation),
                ]);
            result = await builder.BuildMeasuredAsync(
                document,
                activeScopeId,
                ModelProfileViewStateSnapshot.Empty,
                ModelProfileElementViewStateSnapshot.Empty,
                graph,
                layout,
                routing,
                visualModel,
                editorState ?? EditorStateSnapshot.Empty,
                metrics,
                CreateRequest);
        }
        else
        {
            result = await builder.BuildMeasuredAsync(
                graph,
                layout,
                routing,
                visualModel,
                editorState ?? EditorStateSnapshot.Empty,
                metrics,
                CreateRequest);
        }

        Assert.True(result.Succeeded, string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        return new BuiltLabelScene(
            Assert.IsType<Canvas2DScene>(result.Scene),
            owner,
            label,
            layout,
            visualModel);
    }

    private static Canvas2DSceneItem[] LabelLines(
        Canvas2DScene scene,
        ProjectedObjectId labelId) =>
        scene.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.ProjectedObjectId == labelId &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text).ToArray();

    private static Canvas2DSceneItem NodeLabelBody(
        Canvas2DScene scene,
        ProjectedObjectId labelId) =>
        scene.Items.Single(item =>
            item.Origin.ProjectedObjectId == labelId &&
            StringComparer.Ordinal.Equals(
                item.Origin.StableSourceKey,
                $"node-label-interaction:{labelId.Value}"));

    private static Canvas2DSceneItem NodeBody(
        Canvas2DScene scene,
        ProjectedObjectId nodeId) =>
        scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.ProjectedObjectId == nodeId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));

    private static Canvas2DSceneItem[] NodeLabelPreviewItems(
        Canvas2DScene scene,
        ProjectedObjectId labelId) =>
        ResizePreviewItems(scene, labelId);

    private static Canvas2DSceneItem[] ResizePreviewItems(
        Canvas2DScene scene,
        ProjectedObjectId projectedObjectId) =>
        scene.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.ProjectedObjectId == projectedObjectId &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true).ToArray();

    private static Canvas2DSceneItem[] NodeLabelPreviewLines(
        Canvas2DScene scene,
        ProjectedObjectId labelId) =>
        scene.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.ProjectedObjectId == labelId &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true).ToArray();

    private static EditorGestureSnapshot NodeLabelGesture(
        BuiltLabelScene built,
        Canvas2DSceneItem labelBody,
        SceneObjectId nodeId,
        VectorD delta,
        string operation,
        Canvas2DResizeDirection direction = Canvas2DResizeDirection.SouthEast)
    {
        var properties = new List<KeyValuePair<string, PropertyValue>>
        {
            new(
                Canvas2DNodeLabelGestureMetadata.TargetNodeSceneObjectId,
                PropertyValue.FromText(nodeId.Value)),
            new(
                Canvas2DNodeLabelGestureMetadata.TargetLabelSceneObjectId,
                PropertyValue.FromText(labelBody.Id.Value)),
            new(
                Canvas2DNodeLabelGestureMetadata.TargetVisualStateId,
                PropertyValue.FromText(built.Owner.Source.VisualStateId!.Value)),
            new(
                Canvas2DNodeLabelGestureMetadata.TargetLabelProjectedObjectId,
                PropertyValue.FromText(built.Label.Id.Value)),
            new(
                Canvas2DNodeLabelGestureMetadata.Operation,
                PropertyValue.FromText(operation)),
        };
        if (StringComparer.Ordinal.Equals(
                operation,
                Canvas2DNodeLabelGestureMetadata.ResizeOperation))
        {
            properties.Add(new KeyValuePair<string, PropertyValue>(
                Canvas2DNodeLabelGestureMetadata.ResizeDirection,
                PropertyValue.FromText(Canvas2DResizeGeometry.Role(direction))));
        }

        return new EditorGestureSnapshot(
            $"test:node-label:{operation}",
            Canvas2DNodeLabelGestureMetadata.Kind,
            default,
            new PointD(delta.X, delta.Y),
            properties);
    }

    private static EditorGestureSnapshot OwningNodeResizeGesture(
        BuiltLabelScene built,
        Canvas2DSceneItem nodeBody,
        VectorD delta) =>
        new(
            "test:presented-owner-resize",
            Canvas2DResizeGestureMetadata.Kind,
            default,
            new PointD(delta.X, delta.Y),
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(nodeBody.Id.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(built.Owner.Source.VisualStateId!.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.HandleRole,
                    PropertyValue.FromText(Canvas2DResizeGestureMetadata.SouthEastRole)),
            ]);

    private static void AssertProcessLocalPreviewMaterializedOnce(
        IReadOnlyCollection<Canvas2DSceneItem> localItems,
        IReadOnlyCollection<Canvas2DSceneItem> presentedItems,
        Canvas2DSpatialRegion region)
    {
        Assert.NotEmpty(localItems);
        Assert.Equal(
            localItems.Select(static item => item.Origin.StableSourceKey),
            presentedItems.Select(static item => item.Origin.StableSourceKey));
        foreach (var local in localItems)
        {
            var presented = Assert.Single(presentedItems, item =>
                StringComparer.Ordinal.Equals(
                    item.Origin.StableSourceKey,
                    local.Origin.StableSourceKey));
            AssertPresentedItemMapsLocalGeometryOnce(local, presented, region);
        }
    }

    private static void AssertPresentedItemMapsLocalGeometryOnce(
        Canvas2DSceneItem local,
        Canvas2DSceneItem presented,
        Canvas2DSpatialRegion region)
    {
        Assert.Same(region, presented.SpatialRegion);
        Assert.Equal(local.Geometry, presented.Geometry);
        Assert.Equal(
            local.Transform.Then(region.LocalToSceneTransform),
            presented.Transform);
        Assert.Equal(region.MapLocalToScene(local.Bounds), presented.Bounds);
        Assert.Equal(
            local.Clip is { } clip ? region.MapLocalToScene(clip) : null,
            presented.Clip);
    }

    private static PointD Center(RectD bounds) =>
        new(bounds.Left + (bounds.Width / 2d), bounds.Top + (bounds.Height / 2d));

    private static PointD DocumentAnchor(Canvas2DSceneItem item) =>
        item.Transform.TransformPoint(item.Geometry.TextAnchor);

    private static void AssertRectEqual(RectD expected, RectD? actual)
    {
        var value = Assert.IsType<RectD>(actual);
        Assert.Equal(expected.X, value.X, 9);
        Assert.Equal(expected.Y, value.Y, 9);
        Assert.Equal(expected.Width, value.Width, 9);
        Assert.Equal(expected.Height, value.Height, 9);
    }

    private static TextMeasurementRequest CreateRequest(
        string text,
        Canvas2DSceneStyle style,
        double lineHeight) =>
        new(
            text,
            "Test Sans",
            "test:sans",
            "1",
            style.FontSize,
            lineHeight,
            400,
            TextFontStyle.Normal,
            "und",
            TextDirection.LeftToRight,
            TextWritingMode.HorizontalTopToBottom,
            1d,
            "test.metrics",
            "1");

    private sealed class FixedMetricsService : ITextMetricsService
    {
        public ValueTask<TextMeasurementResult> MeasureAsync(
            TextMeasurementRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var width = request.Text.EnumerateRunes().Count() * 5d;
            return ValueTask.FromResult(TextMeasurementResult.Success(new TextMetrics(
                width,
                9d,
                3d,
                request.LineHeight,
                new RectD(0d, -9d, width, 12d),
                $"{request.FontIdentity}@{request.FontVersion}")));
        }
    }

    private sealed record BuiltLabelScene(
        Canvas2DScene Scene,
        ProjectedNode Owner,
        ProjectedLabel Label,
        LayoutResult Layout,
        VisualModelSnapshot VisualModel);

    private sealed class TranslatedSpatialPresentationContributor(
        VisualStateId visualStateId,
        VectorD translation) :
        ICanvas2DSceneContributor,
        ICanvas2DConnectorPresentationRouter
    {
        public Canvas2DSceneContributionResult Contribute(
            Canvas2DSceneContributionContext context) =>
            Canvas2DSceneContributionResult.Success(
                new Canvas2DSceneContribution(
                    spatialPresentationPlan: new Canvas2DSpatialPresentationPlan(
                    [
                        new Canvas2DSpatialRegion(
                            new Canvas2DSpatialRegionId(
                                "test:measured-preview:region"),
                            new ModelProfileId("test:measured-preview:profile"),
                            containerSemanticElementId: null,
                            Matrix2D.CreateTranslation(translation),
                            new RectD(translation.X, translation.Y, 2000d, 2000d)),
                    ],
                    [
                        new Canvas2DSpatialVisualPlacement(
                            visualStateId,
                            new Canvas2DSpatialRegionId(
                                "test:measured-preview:region")),
                    ],
                    Matrix2D.CreateTranslation(translation))));

        public Canvas2DConnectorPresentationRoute Route(
            Canvas2DConnectorPresentationRoutingRequest request)
        {
            var logical = request.CanonicalLogicalPath
                .Select(request.CanonicalGuidanceToSceneTransform.TransformPoint)
                .ToArray();
            logical[0] = request.DisplayedSourceAnchor;
            logical[^1] = request.DisplayedTargetAnchor;
            var editable = request.CanonicalEditablePath
                .Select(request.CanonicalGuidanceToSceneTransform.TransformPoint)
                .ToArray();
            editable[0] = request.DisplayedSourceAnchor;
            editable[^1] = request.DisplayedTargetAnchor;
            return new Canvas2DConnectorPresentationRoute(
                logical,
                new Canvas2DConnectorPresentationMapping(
                    request.CanonicalEditablePath,
                    editable,
                    request.CanonicalGuidanceToSceneTransform,
                    request.SourceRegion?.Id,
                    request.TargetRegion?.Id));
        }
    }
}
