using System.Reflection;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
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

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DSceneBuilderTests
{
    [Fact]
    public void BuilderCombinesCompatibleFiveInputsIntoOneImmutableScene()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var graphBefore = inputs.Graph;
        var layoutBefore = inputs.Layout;
        var routingBefore = inputs.Routing;
        var visualBefore = inputs.VisualModel;
        var editorBefore = inputs.EditorState;

        var result = new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.True(result.Succeeded);
        Assert.Equal(Canvas2DSceneBuildStatus.Succeeded, result.Status);
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        Assert.Equal(inputs.Graph.DocumentId, scene.DocumentId);
        Assert.Equal(inputs.Graph.SourceRevision, scene.SourceRevision);
        Assert.Equal(inputs.Layout.AlgorithmId, scene.LayoutAlgorithmId);
        Assert.Equal(inputs.Routing.RoutingAlgorithmId, scene.RoutingAlgorithmId);
        Assert.Equal(4, scene.ItemCount);
        Assert.Same(inputs.EditorState.Viewport, scene.Viewport);
        Assert.Same(graphBefore, inputs.Graph);
        Assert.Same(layoutBefore, inputs.Layout);
        Assert.Same(routingBefore, inputs.Routing);
        Assert.Same(visualBefore, inputs.VisualModel);
        Assert.Same(editorBefore, inputs.EditorState);
    }

    [Fact]
    public void PipelineItemsPreserveGeometryAppearanceIdentityAndTraceability()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState).Scene);
        var node = inputs.Graph.Nodes[0];
        var nodeGeometry = inputs.Layout.Nodes.Single(item => item.ProjectedObjectId == node.Id);
        var nodeItem = scene.Items.Single(item => item.Origin.ProjectedObjectId == node.Id);
        var edge = Assert.Single(inputs.Graph.Edges);
        var route = Assert.Single(inputs.Routing.Routes);
        var edgeItem = scene.Items.Single(item =>
            item.Origin.ProjectedObjectId == edge.Id &&
            !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));

        Assert.Equal(Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node"), nodeItem.Id);
        Assert.Equal(nodeGeometry.Bounds, nodeItem.Bounds);
        Assert.Equal(
            new RectD(0d, 0d, nodeGeometry.Bounds.Width, nodeGeometry.Bounds.Height),
            nodeItem.Geometry.Bounds);
        Assert.Equal(nodeGeometry.Transform, nodeItem.Transform);
        Assert.Equal(
            nodeGeometry.Bounds.TopLeft,
            nodeItem.Transform.TransformPoint(nodeItem.Geometry.Bounds.TopLeft));
        Assert.Equal(node.Source.SemanticElementId, nodeItem.Origin.SemanticElementId);
        Assert.Equal(node.Source.VisualStateId, nodeItem.Origin.VisualStateId);
        Assert.Equal("blue", nodeItem.PersistentAppearance["test:color"].TextValue);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, edgeItem.Geometry.Kind);
        Assert.Equal(route.Path.AsEnumerable(), edgeItem.Geometry.Points.AsEnumerable());
        Assert.Contains(edge.SourceNodeId, edgeItem.Origin.RelatedProjectedObjectIds);
        Assert.Contains(edge.TargetNodeId, edgeItem.Origin.RelatedProjectedObjectIds);
    }

    [Fact]
    public void ProjectedLabelProducesDeterministicTextGeometryAndOwnerTrace()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var owner = inputs.Graph.Nodes[0];
        var label = new ProjectedLabel(owner.Source, owner.Id, "Neutral label");
        var graph = new ProjectedGraph(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            inputs.Graph.Nodes,
            inputs.Graph.Edges,
            inputs.Graph.Groups,
            inputs.Graph.Ports,
            [label]);

        var result = new Canvas2DSceneBuilder().Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var labelItem = Assert.Single(scene.Items, item =>
            item.Origin.ProjectedObjectId == label.Id);
        Assert.Equal(Canvas2DSceneLayer.Label, labelItem.Layer);
        Assert.Equal(Canvas2DSceneGeometryKind.Text, labelItem.Geometry.Kind);
        Assert.Equal("Neutral label", labelItem.Geometry.Content);
        Assert.Equal(inputs.Layout.Nodes[0].Bounds, labelItem.Bounds);
        Assert.Equal(
            new RectD(
                0d,
                0d,
                inputs.Layout.Nodes[0].Bounds.Width,
                inputs.Layout.Nodes[0].Bounds.Height),
            labelItem.Geometry.Bounds);
        Assert.Equal(
            labelItem.Bounds.TopLeft,
            labelItem.Transform.TransformPoint(labelItem.Geometry.Bounds.TopLeft));
        Assert.Contains(owner.Id, labelItem.Origin.RelatedProjectedObjectIds);
    }

    [Theory]
    [InlineData(100d, 100d, 200d, 100d)]
    [InlineData(37.5d, -12.25d, 83.75d, 41.5d)]
    public void AutomaticNodeLabelUsesCurrentBoundsCenterAndMiddleAnchor(
        double x,
        double y,
        double width,
        double height)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var owner = inputs.Graph.Nodes[0];
        var label = new ProjectedLabel(owner.Source, owner.Id, "Neutral label");
        var ownerBounds = new RectD(x, y, width, height);
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
                    ownerBounds,
                    Matrix2D.CreateTranslation(ownerBounds.X, ownerBounds.Y)),
            ]));
        var routing = new RoutingResult(
            inputs.Routing.DocumentId,
            inputs.Routing.SourceRevision,
            layout.AlgorithmId,
            inputs.Routing.RoutingAlgorithmId,
            RoutingComputation.Empty);

        var result = new Canvas2DSceneBuilder().Build(
            graph,
            layout,
            routing,
            inputs.VisualModel,
            inputs.EditorState);
        Assert.True(result.Succeeded, string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var labelItem = scene.Items.Single(item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label"));

        Assert.Equal(ownerBounds, labelItem.Bounds);
        Assert.Equal(new RectD(0d, 0d, width, height), labelItem.Geometry.Bounds);
        Assert.Equal(new PointD(width / 2d, height / 2d), labelItem.Geometry.TextAnchor);
        Assert.Equal(Canvas2DTextAlignment.Center, labelItem.Geometry.TextAlignment);
        Assert.Equal(Canvas2DTextBaseline.Middle, labelItem.Geometry.TextBaseline);
        AssertPointEqual(Center(ownerBounds), DocumentTextAnchor(labelItem));
    }

    [Fact]
    public void OutsideBelowCompatibilityItemUsesDerivedExternalRegionWithoutChangingNodeBounds()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var owner = inputs.Graph.Nodes[0];
        var ownerGeometry = inputs.Layout.Nodes.Single(node =>
            node.ProjectedObjectId == owner.Id);
        var placement = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 120d);
        var label = new ProjectedLabel(
            owner.Source,
            owner.Id,
            "External node label",
            nodePlacement: placement);
        var graph = new ProjectedGraph(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            inputs.Graph.Nodes,
            inputs.Graph.Edges,
            inputs.Graph.Groups,
            inputs.Graph.Ports,
            [label]);

        var result = new Canvas2DSceneBuilder().Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.True(result.Succeeded, string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var nodeItem = scene.Items.Single(item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(owner.Id, "node"));
        var labelItem = scene.Items.Single(item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label"));
        var expectedBounds = new RectD(
            ownerGeometry.Bounds.Left +
                ((ownerGeometry.Bounds.Width - placement.MaximumWidth!.Value) / 2d),
            ownerGeometry.Bounds.Bottom + placement.Gap,
            placement.MaximumWidth.Value,
            14.4d);

        Assert.Equal(ownerGeometry.Bounds, nodeItem.Bounds);
        Assert.Equal(expectedBounds.X, labelItem.Bounds.X, 9);
        Assert.Equal(expectedBounds.Y, labelItem.Bounds.Y, 9);
        Assert.Equal(expectedBounds.Width, labelItem.Bounds.Width, 9);
        Assert.Equal(expectedBounds.Height, labelItem.Bounds.Height, 9);
        var labelClip = Assert.IsType<RectD>(labelItem.Clip);
        Assert.Equal(expectedBounds.X, labelClip.X, 9);
        Assert.Equal(expectedBounds.Y, labelClip.Y, 9);
        Assert.Equal(expectedBounds.Width, labelClip.Width, 9);
        Assert.Equal(expectedBounds.Height, labelClip.Height, 9);
        Assert.Equal(Canvas2DTextAlignment.Center, labelItem.Geometry.TextAlignment);
        Assert.Equal(Canvas2DTextBaseline.Middle, labelItem.Geometry.TextBaseline);
        AssertPointEqual(Center(expectedBounds), DocumentTextAnchor(labelItem));
        Assert.Equal(owner.Source.SemanticElementId, labelItem.Origin.SemanticElementId);
        Assert.Equal(owner.Source.VisualStateId, labelItem.Origin.VisualStateId);
        Assert.Contains(owner.Id, labelItem.Origin.RelatedProjectedObjectIds);

        var hit = Assert.IsType<Canvas2DSceneHitTestResult>(
            new Canvas2DSceneHitTestService().HitTest(scene, Center(expectedBounds)));
        Assert.Equal(labelItem.Id, hit.SceneObjectId);

        var delta = new VectorD(30d, 20d);
        var direction = Canvas2DResizeDirection.SouthEast;
        var gesture = new EditorGestureSnapshot(
            "test:unmeasured-outside-resize",
            Canvas2DResizeGestureMetadata.Kind,
            new PointD(0d, 0d),
            new PointD(delta.X, delta.Y),
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(nodeItem.Id.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(owner.Source.VisualStateId!.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.HandleRole,
                    PropertyValue.FromText(Canvas2DResizeGeometry.Role(direction))),
            ]);
        var previewResult = new Canvas2DSceneBuilder().Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(activeGesture: gesture));
        var previewScene = Assert.IsType<Canvas2DScene>(previewResult.Scene);
        var previewLabel = previewScene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.ProjectedObjectId == label.Id &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true);
        var resizedNodeBounds = Canvas2DResizeGeometry.CalculateBounds(
            ownerGeometry.Bounds,
            delta,
            direction);
        var expectedPreviewBounds = new RectD(
            resizedNodeBounds.Left +
                ((resizedNodeBounds.Width - placement.MaximumWidth.Value) / 2d),
            resizedNodeBounds.Bottom + placement.Gap,
            placement.MaximumWidth.Value,
            14.4d);

        Assert.Equal(expectedPreviewBounds.X, previewLabel.Bounds.X, 9);
        Assert.Equal(expectedPreviewBounds.Y, previewLabel.Bounds.Y, 9);
        Assert.Equal(expectedPreviewBounds.Width, previewLabel.Bounds.Width, 9);
        Assert.Equal(expectedPreviewBounds.Height, previewLabel.Bounds.Height, 9);
        var previewClip = Assert.IsType<RectD>(previewLabel.Clip);
        Assert.Equal(expectedPreviewBounds.X, previewClip.X, 9);
        Assert.Equal(expectedPreviewBounds.Y, previewClip.Y, 9);
        Assert.Equal(expectedPreviewBounds.Width, previewClip.Width, 9);
        Assert.Equal(expectedPreviewBounds.Height, previewClip.Height, 9);
    }

    [Fact]
    public void AutomaticOutsideBelowLabelAtLeftBoundaryIsDerivedInsideDocument()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var owner = inputs.Graph.Nodes[0];
        var ownerBounds = new RectD(0d, 20d, 48d, 48d);
        var placement = new NodeLabelPlacement(
            NodeLabelPlacementKind.OutsideBelow,
            gap: 8d,
            maximumWidth: 160d);
        var label = new ProjectedLabel(
            owner.Source,
            owner.Id,
            "Gateway near origin",
            nodePlacement: placement);
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
                    ownerBounds,
                    Matrix2D.CreateTranslation(ownerBounds.X, ownerBounds.Y)),
            ]));
        var routing = new RoutingResult(
            inputs.Routing.DocumentId,
            inputs.Routing.SourceRevision,
            layout.AlgorithmId,
            inputs.Routing.RoutingAlgorithmId,
            RoutingComputation.Empty);

        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            graph,
            layout,
            routing,
            inputs.VisualModel,
            inputs.EditorState).Scene);

        var node = scene.Items.Single(item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(owner.Id, "node"));
        var labelItem = scene.Items.Single(item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label"));
        Assert.Equal(ownerBounds, node.Bounds);
        Assert.Equal(0d, labelItem.Bounds.Left);
        Assert.Equal(160d, labelItem.Bounds.Width);
        Assert.True(labelItem.Bounds.Top >= 0d);
    }

    [Fact]
    public void ManualEditableNodeLabelCompatibilityPathUsesStableOwnedHitBoxAndPreview()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var owner = inputs.Graph.Nodes[0];
        var label = new ProjectedLabel(
            owner.Source,
            owner.Id,
            "External node label",
            nodePlacement: new NodeLabelPlacement(
                NodeLabelPlacementKind.OutsideBelow,
                gap: 8d,
                maximumWidth: 120d),
            nodeInteractionPolicy: NodeLabelInteractionPolicy.MoveAndResize);
        var graph = WithLabel(inputs, label);
        var visualOverride = new NodeLabelVisualOverride(65d, 45d, 90d, 30d);
        var visualModel = WithNodeLabelOverride(
            inputs.VisualModel,
            owner.Source.VisualStateId!,
            visualOverride);
        var nodeBounds = inputs.Layout.Nodes.Single(node =>
            node.ProjectedObjectId == owner.Id).Bounds;
        var expectedCenter = Center(nodeBounds) + new VectorD(65d, 45d);
        var expectedBounds = new RectD(
            expectedCenter.X - 45d,
            expectedCenter.Y - 15d,
            90d,
            30d);
        var selected = new EditorStateSnapshot(selection: [owner.Source.VisualStateId!]);
        var result = new Canvas2DSceneBuilder().Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            visualModel,
            selected);
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(owner.Id, "node");
        var labelId = Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label");
        var bodyId = Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label-interaction");
        var text = scene.Items.Single(item => item.Id == labelId);
        var body = scene.Items.Single(item => item.Id == bodyId);

        Assert.Equal(expectedBounds, text.Bounds);
        Assert.Equal(expectedBounds, body.Bounds);
        Assert.Equal(Canvas2DHitTestMode.None, text.HitTestPolicy.Mode);
        Assert.Equal(Canvas2DHitTestMode.Bounds, body.HitTestPolicy.Mode);
        Assert.Equal(0d, body.Style.Opacity);
        Assert.Contains(nodeId, body.Origin.RelatedSceneObjectIds);
        Assert.Equal(
            body.Id,
            Assert.IsType<Canvas2DSceneHitTestResult>(
                new Canvas2DSceneHitTestService().HitTest(scene, Center(expectedBounds)))
                .SceneObjectId);
        Assert.Equal(8, scene.Items.Count(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "node-label-resize-zone:",
                StringComparison.Ordinal) == true));

        var delta = new VectorD(35d, -20d);
        var gesture = CreateNodeLabelGesture(
            "test:unmeasured-manual-label-move",
            owner,
            label,
            nodeId,
            bodyId,
            delta,
            Canvas2DNodeLabelGestureMetadata.MoveOperation);
        var previewResult = new Canvas2DSceneBuilder().Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            visualModel,
            new EditorStateSnapshot(
                selection: [owner.Source.VisualStateId!],
                activeGesture: gesture));
        var previewScene = Assert.IsType<Canvas2DScene>(previewResult.Scene);
        var preview = previewScene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            item.Origin.ProjectedObjectId == label.Id &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true);

        Assert.Equal(expectedBounds.Translate(delta), preview.Bounds);
        Assert.Equal(expectedBounds.Translate(delta), preview.Clip);
        Assert.Equal(Canvas2DHitTestMode.None, preview.HitTestPolicy.Mode);
        Assert.Equal(expectedBounds, previewScene.Items.Single(item => item.Id == bodyId).Bounds);
    }

    [Fact]
    public void ManualNodeLabelCompatibilityOwnerResizePreservesOffsetAndBoxSize()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var owner = inputs.Graph.Nodes[0];
        var label = new ProjectedLabel(
            owner.Source,
            owner.Id,
            "External node label",
            nodePlacement: new NodeLabelPlacement(
                NodeLabelPlacementKind.OutsideBelow,
                gap: 8d,
                maximumWidth: 120d),
            nodeInteractionPolicy: NodeLabelInteractionPolicy.MoveAndResize);
        var graph = WithLabel(inputs, label);
        var visualOverride = new NodeLabelVisualOverride(65d, 45d, 90d, 30d);
        var visualModel = WithNodeLabelOverride(
            inputs.VisualModel,
            owner.Source.VisualStateId!,
            visualOverride);
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(owner.Id, "node");
        var delta = new VectorD(40d, 30d);
        var gesture = new EditorGestureSnapshot(
            "test:unmeasured-manual-label-owner-resize",
            Canvas2DResizeGestureMetadata.Kind,
            default,
            new PointD(delta.X, delta.Y),
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(nodeId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(owner.Source.VisualStateId!.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.HandleRole,
                    PropertyValue.FromText("southeast")),
            ]);
        var result = new Canvas2DSceneBuilder().Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            visualModel,
            new EditorStateSnapshot(
                selection: [owner.Source.VisualStateId!],
                activeGesture: gesture));
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var resizedNode = Canvas2DResizeGeometry.CalculateBounds(
            inputs.Layout.Nodes.Single(node => node.ProjectedObjectId == owner.Id).Bounds,
            delta,
            Canvas2DResizeDirection.SouthEast);
        var labelCenter = Center(resizedNode) + new VectorD(
            visualOverride.OffsetX,
            visualOverride.OffsetY);
        var expected = new RectD(
            labelCenter.X - (visualOverride.Width / 2d),
            labelCenter.Y - (visualOverride.Height / 2d),
            visualOverride.Width,
            visualOverride.Height);
        var preview = scene.Items.Single(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            item.Origin.ProjectedObjectId == label.Id &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true);

        Assert.Equal(expected, preview.Bounds);
        Assert.Equal(expected, preview.Clip);
    }

    public static IEnumerable<object[]> AutomaticLabelAffineTransformCases =>
    [
        [new Matrix2D(0.5d, 0d, 0d, 0.5d, 10d, 20d)],
        [new Matrix2D(0.5d, 0.1d, 0.2d, 0.5d, 10d, 20d)],
        [Matrix2D.CreateScale(0.4d, 0.4d)
            .Then(Matrix2D.CreateRotation(Math.PI / 6d))
            .Then(Matrix2D.CreateTranslation(20d, 20d))],
        [new Matrix2D(0.5d, 0.25d, 0d, 0d, 10d, 20d)],
    ];

    [Theory]
    [MemberData(nameof(AutomaticLabelAffineTransformCases))]
    public void AutomaticNodeLabelCentersWithScaleShearAndSingularTransforms(
        Matrix2D ownerTransform)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var owner = inputs.Graph.Nodes[0];
        var label = new ProjectedLabel(owner.Source, owner.Id, "Neutral label");
        var graph = WithLabel(inputs, label);
        var layout = new LayoutResult(
            inputs.Layout.DocumentId,
            inputs.Layout.SourceRevision,
            inputs.Layout.AlgorithmId,
            new LayoutComputation(inputs.Layout.Nodes.Select(node =>
                node.ProjectedObjectId == owner.Id
                    ? new LayoutNodeGeometry(node.ProjectedObjectId, node.Bounds, ownerTransform)
                    : node)));

        var result = new Canvas2DSceneBuilder().Build(
            graph,
            layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.True(result.Succeeded, string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var labelItem = scene.Items.Single(item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label"));

        Assert.Equal(ownerTransform.M11, labelItem.Transform.M11);
        Assert.Equal(ownerTransform.M12, labelItem.Transform.M12);
        Assert.Equal(ownerTransform.M21, labelItem.Transform.M21);
        Assert.Equal(ownerTransform.M22, labelItem.Transform.M22);
        AssertPointEqual(
            Center(inputs.Layout.Nodes.Single(node => node.ProjectedObjectId == owner.Id).Bounds),
            DocumentTextAnchor(labelItem));
    }

    [Fact]
    public void SelectionDoesNotMoveAutomaticLabelAndOverlaysRemainTopmost()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var owner = inputs.Graph.Nodes[0];
        var ownerId = Canvas2DSceneObjectIdentity.ForProjected(owner.Id, "node");
        var label = new ProjectedLabel(owner.Source, owner.Id, "Neutral label");
        var labelId = Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label");
        var graph = WithLabel(inputs, label);
        var builder = new Canvas2DSceneBuilder();
        var unselected = Assert.IsType<Canvas2DScene>(builder.Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            EditorStateSnapshot.Empty).Scene);
        var selected = Assert.IsType<Canvas2DScene>(builder.Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(selection: [owner.Source.VisualStateId!])).Scene);
        var before = unselected.Items.Single(item => item.Id == labelId);
        var after = selected.Items.Single(item => item.Id == labelId);

        Assert.Equal(before.Geometry, after.Geometry);
        Assert.Equal(before.Transform, after.Transform);
        Assert.Equal(before.Bounds, after.Bounds);
        AssertPointEqual(DocumentTextAnchor(before), DocumentTextAnchor(after));
        Assert.True(selected.Items.IndexOf(selected.Items.Single(item => item.Id == ownerId)) <
            selected.Items.IndexOf(after));
        Assert.All(
            selected.Items.Where(item => item.Layer == Canvas2DSceneLayer.Overlay),
            overlay => Assert.True(selected.Items.IndexOf(after) < selected.Items.IndexOf(overlay)));

        var hitTest = new Canvas2DSceneHitTestService();
        var centerHit = Assert.IsType<Canvas2DSceneHitTestResult>(
            hitTest.HitTest(selected, DocumentTextAnchor(after)));
        Assert.Equal(labelId, centerHit.SceneObjectId);
        var eastZone = selected.Items.Single(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "resize-edge-zone:east:",
                StringComparison.Ordinal) == true);
        var edgeHit = Assert.IsType<Canvas2DSceneHitTestResult>(
            hitTest.HitTest(selected, Center(eastZone.Bounds)));
        Assert.Equal(eastZone.Id, edgeHit.SceneObjectId);
    }

    [Fact]
    public void MultiSelectionCreatesOneOverlayPerLogicalVisualAndSuppressesGestureHandles()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var node = inputs.Graph.Nodes[0];
        var edge = Assert.Single(inputs.Graph.Edges);
        var label = new ProjectedLabel(node.Source, node.Id, "Neutral label");
        var graph = WithLabel(inputs, label);
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node");
        var labelId = Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label");
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
        var nodeVisualStateId = node.Source.VisualStateId!;
        var edgeVisualStateId = edge.Source.VisualStateId!;
        var forwardState = new EditorStateSnapshot(
            selection: [nodeVisualStateId, edgeVisualStateId]);
        var reverseState = new EditorStateSnapshot(
            selection: [edgeVisualStateId, nodeVisualStateId]);
        var builder = new Canvas2DSceneBuilder();

        var forwardScene = Assert.IsType<Canvas2DScene>(builder.Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            forwardState).Scene);
        var reverseScene = Assert.IsType<Canvas2DScene>(builder.Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            reverseState).Scene);
        var overlays = forwardScene.Items.Where(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "selection:",
                StringComparison.Ordinal) == true).ToArray();

        Assert.Equal(3, overlays.Length);
        Assert.Single(overlays, item =>
            item.Origin.VisualStateId == nodeVisualStateId);
        Assert.Equal(2, overlays.Count(item =>
            item.Origin.VisualStateId == edgeVisualStateId));
        Assert.Contains(overlays, item =>
            item.Origin.RelatedSceneObjectIds.Contains(nodeId) &&
            !item.Origin.RelatedSceneObjectIds.Contains(labelId));
        Assert.Contains(overlays, item =>
            item.Origin.RelatedSceneObjectIds.Contains(connectorId));
        Assert.DoesNotContain(forwardScene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "resize-",
                StringComparison.Ordinal) == true);
        Assert.DoesNotContain(forwardScene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "route-bend-handle:",
                StringComparison.Ordinal) == true);
        Assert.Equal(forwardState, reverseState);
        Assert.Equal(forwardScene, reverseScene);
        Assert.Equal(
            overlays.Select(static item => item.Id),
            reverseScene.Items.Where(item =>
                    item.Origin.StableSourceKey?.StartsWith(
                        "selection:",
                        StringComparison.Ordinal) == true)
                .Select(static item => item.Id));
    }

    [Fact]
    public void AutomaticNodeLabelRemainsCenteredInMovePreview()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var owner = inputs.Graph.Nodes[0];
        var ownerId = Canvas2DSceneObjectIdentity.ForProjected(owner.Id, "node");
        var visualStateId = owner.Source.VisualStateId!;
        var label = new ProjectedLabel(owner.Source, owner.Id, "Neutral label");
        var labelId = Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label");
        var graph = WithLabel(inputs, label);
        var translation = new VectorD(27.5d, -17.25d);
        var gesture = new EditorGestureSnapshot(
            "test:move-centered-label",
            Canvas2DMoveGestureMetadata.Kind,
            new PointD(20d, 30d),
            new PointD(20d, 30d) + translation,
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DMoveGestureMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(ownerId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DMoveGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(visualStateId.Value)),
            ]);

        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(activeGesture: gesture)).Scene);
        var preview = FindGesturePreview(scene, "move-preview:", labelId);

        Assert.Equal(inputs.Layout.Nodes[0].Bounds.Translate(translation), preview.Bounds);
        Assert.Equal(Canvas2DTextAlignment.Center, preview.Geometry.TextAlignment);
        Assert.Equal(Canvas2DTextBaseline.Middle, preview.Geometry.TextBaseline);
        AssertPointEqual(Center(preview.Bounds), DocumentTextAnchor(preview));
    }

    [Fact]
    public void IndexedMultiMovePreviewsEverySelectedVisualItemByOneDeterministicDelta()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var firstNode = inputs.Graph.Nodes[0];
        var secondNode = inputs.Graph.Nodes[1];
        var firstLabel = new ProjectedLabel(firstNode.Source, firstNode.Id, "Alpha");
        var secondLabel = new ProjectedLabel(secondNode.Source, secondNode.Id, "Beta");
        var graph = new ProjectedGraph(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            inputs.Graph.Nodes,
            inputs.Graph.Edges,
            inputs.Graph.Groups,
            inputs.Graph.Ports,
            [firstLabel, secondLabel]);
        var firstVisualStateId = firstNode.Source.VisualStateId!;
        var secondVisualStateId = secondNode.Source.VisualStateId!;
        var firstNodeId = Canvas2DSceneObjectIdentity.ForProjected(firstNode.Id, "node");
        var secondNodeId = Canvas2DSceneObjectIdentity.ForProjected(secondNode.Id, "node");
        var origin = new PointD(35.25d, 44.5d);
        var translation = new VectorD(27.5d, -13.75d);
        var current = origin + translation;
        var forwardGesture = CreateIndexedMoveGesture(
            "test:indexed-multi-move",
            origin,
            current,
            (firstNodeId, firstVisualStateId),
            (secondNodeId, secondVisualStateId));
        var reverseGesture = CreateIndexedMoveGesture(
            "test:indexed-multi-move",
            origin,
            current,
            (secondNodeId, secondVisualStateId),
            (firstNodeId, firstVisualStateId));
        var builder = new Canvas2DSceneBuilder();
        var baseScene = Assert.IsType<Canvas2DScene>(builder.Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            EditorStateSnapshot.Empty).Scene);
        var forwardScene = Assert.IsType<Canvas2DScene>(builder.Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(
                selection: [secondVisualStateId, firstVisualStateId],
                activeGesture: forwardGesture)).Scene);
        var reverseScene = Assert.IsType<Canvas2DScene>(builder.Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(
                selection: [firstVisualStateId, secondVisualStateId],
                activeGesture: reverseGesture)).Scene);
        var selectedVisualStateIds = new HashSet<VisualStateId>
        {
            firstVisualStateId,
            secondVisualStateId,
        };
        var sources = baseScene.Items.Where(item =>
            item.Origin.VisualStateId is { } visualStateId &&
            selectedVisualStateIds.Contains(visualStateId)).ToArray();
        var previews = forwardScene.Items.Where(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "move-preview:",
                StringComparison.Ordinal) == true).ToArray();

        Assert.Equal(4, sources.Length);
        Assert.Equal(4, previews.Length);
        Assert.Equal(2, sources.Count(item => item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle));
        Assert.Equal(2, sources.Count(item => item.Geometry.Kind == Canvas2DSceneGeometryKind.Text));
        foreach (var source in sources)
        {
            var preview = Assert.Single(previews, item =>
                item.Origin.RelatedSceneObjectIds.Contains(source.Id));
            Assert.Equal(source.Origin.VisualStateId, preview.Origin.VisualStateId);
            Assert.Equal(source.Geometry, preview.Geometry);
            Assert.Equal(source.Bounds.Translate(translation), preview.Bounds);
            Assert.Equal(
                source.Transform.Then(Matrix2D.CreateTranslation(translation)),
                preview.Transform);
            if (source.Geometry.Kind == Canvas2DSceneGeometryKind.Text)
            {
                AssertPointEqual(
                    DocumentTextAnchor(source) + translation,
                    DocumentTextAnchor(preview));
            }
        }

        var reversePreviews = reverseScene.Items.Where(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "move-preview:",
                StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(
            previews.Select(static item => item.Id),
            reversePreviews.Select(static item => item.Id));
        Assert.Equal(previews, reversePreviews);
    }

    public static IEnumerable<object[]> CenteredResizePreviewCases =>
    [
        [Canvas2DResizeGestureMetadata.EastRole, 20d, 15d],
        [Canvas2DResizeGestureMetadata.WestRole, 20d, 15d],
        [Canvas2DResizeGestureMetadata.NorthRole, 20d, 15d],
        [Canvas2DResizeGestureMetadata.NorthWestRole, 20d, 15d],
        [Canvas2DResizeGestureMetadata.SouthEastRole, 20d, 15d],
    ];

    [Theory]
    [MemberData(nameof(CenteredResizePreviewCases))]
    public void AutomaticNodeLabelRemainsCenteredInResizePreview(
        string role,
        double deltaX,
        double deltaY)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var owner = inputs.Graph.Nodes[0];
        var ownerId = Canvas2DSceneObjectIdentity.ForProjected(owner.Id, "node");
        var visualStateId = owner.Source.VisualStateId!;
        var label = new ProjectedLabel(owner.Source, owner.Id, "Neutral label");
        var labelId = Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label");
        var graph = WithLabel(inputs, label);
        var gesture = new EditorGestureSnapshot(
            $"test:resize-centered-label:{role}",
            Canvas2DResizeGestureMetadata.Kind,
            new PointD(110d, 70d),
            new PointD(110d + deltaX, 70d + deltaY),
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(ownerId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(visualStateId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.HandleRole,
                    PropertyValue.FromText(role)),
            ]);

        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(activeGesture: gesture)).Scene);
        var preview = FindGesturePreview(scene, "resize-preview:", labelId);

        Assert.Equal(Canvas2DTextAlignment.Center, preview.Geometry.TextAlignment);
        Assert.Equal(Canvas2DTextBaseline.Middle, preview.Geometry.TextBaseline);
        AssertPointEqual(Center(preview.Bounds), DocumentTextAnchor(preview));
    }

    [Fact]
    public void LogicalNodeSelectionTargetsContentAndAllVisualItemsPreviewTogether()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var owner = inputs.Graph.Nodes[0];
        var visualStateId = owner.Source.VisualStateId!;
        var label = new ProjectedLabel(owner.Source, owner.Id, "Neutral label");
        var graph = new ProjectedGraph(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            inputs.Graph.Nodes,
            inputs.Graph.Edges,
            inputs.Graph.Groups,
            inputs.Graph.Ports,
            [label]);
        var labelId = Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label");
        var ownerId = Canvas2DSceneObjectIdentity.ForProjected(owner.Id, "node");
        var selected = new EditorStateSnapshot(selection: [visualStateId]);
        var selectedScene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            selected).Scene);

        var resizeItems = selectedScene.Items.Where(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "resize-",
                StringComparison.Ordinal) == true).ToArray();
        var cornerHandles = resizeItems.Where(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "resize-handle:",
                StringComparison.Ordinal) == true).ToArray();
        var edgeZones = resizeItems.Where(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "resize-edge-zone:",
                StringComparison.Ordinal) == true).ToArray();
        var selectionOverlay = Assert.Single(selectedScene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "selection:",
                StringComparison.Ordinal) == true);
        Assert.Equal(8, resizeItems.Length);
        Assert.Equal(4, cornerHandles.Length);
        Assert.Equal(4, edgeZones.Length);
        Assert.Contains(ownerId, selectionOverlay.Origin.RelatedSceneObjectIds);
        Assert.DoesNotContain(labelId, selectionOverlay.Origin.RelatedSceneObjectIds);
        Assert.All(resizeItems, item =>
        {
            Assert.True(item.IsVisible);
            Assert.Null(item.Style.Fill);
            Assert.Null(item.Style.Stroke);
            Assert.Equal(0d, item.Style.Opacity);
            Assert.Equal(Canvas2DHitTestMode.Bounds, item.HitTestPolicy.Mode);
            Assert.Equal(visualStateId, item.Origin.VisualStateId);
            Assert.Contains(ownerId, item.Origin.RelatedSceneObjectIds);
            Assert.DoesNotContain(labelId, item.Origin.RelatedSceneObjectIds);
        });
        Assert.DoesNotContain(resizeItems, static item =>
            item.IsVisible &&
            item.Style.Opacity > 0d &&
            (item.Style.Fill is not null || item.Style.Stroke is not null));
        Assert.True(cornerHandles.Min(item => item.ZIndex) > edgeZones.Max(item => item.ZIndex));
        Assert.Equal(
            ["northeast", "northwest", "southeast", "southwest"],
            cornerHandles.Select(item => item.Metadata[
                    "inceptus.canvas2d:resize-handle-role"].TextValue)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            ["east", "north", "south", "west"],
            edgeZones.Select(item => item.Metadata[
                    "inceptus.canvas2d:resize-handle-role"].TextValue)
                .Order(StringComparer.Ordinal));

        var handle = Assert.Single(cornerHandles, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "resize-handle:southeast:",
                StringComparison.Ordinal) == true);
        Assert.Same(handle, selectedScene.Items[^1]);
        Assert.Equal(visualStateId, handle.Origin.VisualStateId);
        Assert.Contains(ownerId, handle.Origin.RelatedSceneObjectIds);
        Assert.Equal(Canvas2DHitTestMode.Bounds, handle.HitTestPolicy.Mode);

        var repeatedScene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            selected).Scene);
        Assert.Equal(
            resizeItems.Select(static item => item.Id),
            repeatedScene.Items.Where(item =>
                    item.Origin.StableSourceKey?.StartsWith(
                        "resize-",
                        StringComparison.Ordinal) == true)
                .Select(static item => item.Id));

        var gesture = new EditorGestureSnapshot(
            "test:resize-label",
            "inceptus.canvas2d:resize",
            new PointD(110d, 70d),
            new PointD(135d, 90d),
            [
                new KeyValuePair<string, PropertyValue>(
                    "inceptus.canvas2d:resize-target-scene-object-id",
                    PropertyValue.FromText(ownerId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    "inceptus.canvas2d:resize-target-visual-state-id",
                    PropertyValue.FromText(visualStateId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    "inceptus.canvas2d:resize-handle-role",
                    PropertyValue.FromText("southeast")),
            ]);
        var previewState = new EditorStateSnapshot(
            selection: [visualStateId],
            activeGesture: gesture);
        var previewScene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            previewState).Scene);
        var previews = previewScene.Items.Where(item =>
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true).ToArray();

        Assert.Equal(2, previews.Length);
        Assert.All(previews, preview =>
            Assert.Equal(new RectD(10d, 20d, 125d, 70d), preview.Bounds));
        Assert.Contains(previews, preview => preview.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle);
        Assert.Contains(previews, preview => preview.Geometry.Kind == Canvas2DSceneGeometryKind.Text);
    }

    [Fact]
    public void EightResizeInteractionsHitDeterministicallyWithCornersAboveEdges()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var node = inputs.Graph.Nodes[0];
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node");
        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(selection: [node.Source.VisualStateId!])).Scene);
        var resizeItems = scene.Items.Where(item =>
            item.Origin.StableSourceKey?.StartsWith("resize-", StringComparison.Ordinal) == true)
            .ToArray();
        var target = scene.Items.Single(item => item.Id == nodeId);
        var byRole = resizeItems.ToDictionary(
            item => item.Metadata["inceptus.canvas2d:resize-handle-role"].TextValue,
            StringComparer.Ordinal);
        var hitTest = new Canvas2DSceneHitTestService();

        foreach (var role in new[]
                 {
                     "northwest", "north", "northeast", "east",
                     "southeast", "south", "southwest", "west",
                 })
        {
            var item = byRole[role];
            var center = new PointD(
                item.Bounds.X + (item.Bounds.Width / 2d),
                item.Bounds.Y + (item.Bounds.Height / 2d));
            Assert.Equal(item.Id, hitTest.HitTest(scene, center)?.SceneObjectId);
        }

        Assert.Equal(
            byRole["northwest"].Id,
            hitTest.HitTest(scene, target.Bounds.TopLeft)?.SceneObjectId);
        Assert.Equal(nodeId, hitTest.HitTest(scene, new PointD(
            target.Bounds.X + (target.Bounds.Width / 2d),
            target.Bounds.Y + (target.Bounds.Height / 2d)))?.SceneObjectId);
        var beyondNorth = hitTest.HitTest(
            scene,
            new PointD(
                target.Bounds.X + (target.Bounds.Width / 2d),
                target.Bounds.Top - 5d));
        Assert.DoesNotContain(resizeItems, item => item.Id == beyondNorth?.SceneObjectId);
    }

    [Fact]
    public void HoveringResizeRegionsChangesOnlyCursorPresentationAndAddsNoVisibleOverlay()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var node = inputs.Graph.Nodes[0];
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node");
        var builder = new Canvas2DSceneBuilder();
        var selectedScene = Assert.IsType<Canvas2DScene>(builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(selection: [node.Source.VisualStateId!])).Scene);
        var resizeItems = selectedScene.Items.Where(item =>
                item.Metadata.ContainsKey("inceptus.canvas2d:resize-handle-role"))
            .ToArray();

        Assert.Equal(8, resizeItems.Length);
        foreach (var region in resizeItems)
        {
            var hoveredScene = Assert.IsType<Canvas2DScene>(builder.Build(
                inputs.Graph,
                inputs.Layout,
                inputs.Routing,
                inputs.VisualModel,
                new EditorStateSnapshot(
                    selection: [node.Source.VisualStateId!],
                    hoveredObjectId: region.Id)).Scene);
            var currentRegion = Assert.Single(hoveredScene.Items, item => item.Id == region.Id);
            var center = new PointD(
                currentRegion.Bounds.X + (currentRegion.Bounds.Width / 2d),
                currentRegion.Bounds.Y + (currentRegion.Bounds.Height / 2d));

            Assert.DoesNotContain(hoveredScene.Items, item =>
                StringComparer.Ordinal.Equals(
                    item.Origin.StableSourceKey,
                    $"hover:{region.Id.Value}"));
            Assert.Null(currentRegion.Style.Fill);
            Assert.Null(currentRegion.Style.Stroke);
            Assert.Equal(0d, currentRegion.Style.Opacity);
            Assert.Equal(
                currentRegion.Id,
                new Canvas2DSceneHitTestService().HitTest(hoveredScene, center)?.SceneObjectId);
        }

        var normallyHoveredScene = Assert.IsType<Canvas2DScene>(builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(
                selection: [node.Source.VisualStateId!],
                hoveredObjectId: nodeId)).Scene);
        Assert.Contains(normallyHoveredScene.Items, item =>
            StringComparer.Ordinal.Equals(
                item.Origin.StableSourceKey,
                $"hover:{nodeId.Value}"));
    }

    [Fact]
    public void MinimumResizePreviewKeepsAllEightInteractionsReachableAndInteriorSelectable()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var node = inputs.Graph.Nodes[0];
        var nodeId = Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node");
        var visualStateId = node.Source.VisualStateId!;
        var gesture = new EditorGestureSnapshot(
            "test:minimum-resize",
            Canvas2DResizeGestureMetadata.Kind,
            new PointD(110d, 70d),
            new PointD(11d, 21d),
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(nodeId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(visualStateId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.HandleRole,
                    PropertyValue.FromText(Canvas2DResizeGestureMetadata.SouthEastRole)),
            ]);
        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(
                selection: [visualStateId],
                activeGesture: gesture)).Scene);
        var resizeItems = scene.Items.Where(item =>
            item.Origin.StableSourceKey is { } stableKey &&
            stableKey.StartsWith("resize-", StringComparison.Ordinal) &&
            !stableKey.StartsWith("resize-preview:", StringComparison.Ordinal))
            .ToDictionary(
                item => item.Metadata[Canvas2DResizeGestureMetadata.HandleRole].TextValue,
                StringComparer.Ordinal);
        var hitTest = new Canvas2DSceneHitTestService();

        Assert.Equal(8, resizeItems.Count);
        foreach (var item in resizeItems.Values)
        {
            var center = new PointD(
                item.Bounds.X + (item.Bounds.Width / 2d),
                item.Bounds.Y + (item.Bounds.Height / 2d));
            Assert.Equal(item.Id, hitTest.HitTest(scene, center)?.SceneObjectId);
        }

        Assert.Equal(
            nodeId,
            hitTest.HitTest(scene, new PointD(10.5d, 20.5d))?.SceneObjectId);
    }

    [Fact]
    public void ExplicitSelectedPersistentRouteGetsOnlyInternalBendHandlesAndPreview()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var edge = Assert.Single(inputs.Graph.Edges);
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
        var selected = new EditorStateSnapshot(selection: [edge.Source.VisualStateId!]);
        var selectedScene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            selected).Scene);
        var handle = Assert.Single(selectedScene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "route-bend-handle:",
                StringComparison.Ordinal) == true);

        Assert.All(
            selectedScene.Items.Where(item =>
                item.Origin.StableSourceKey?.StartsWith(
                    "connector-endpoint-handle:",
                    StringComparison.Ordinal) == true),
            endpoint => Assert.True(endpoint.ZIndex > handle.ZIndex));
        Assert.Equal(new RectD(155d, 40d, 10d, 10d), handle.Bounds);
        Assert.Equal(Canvas2DHitTestMode.FillOrStroke, handle.HitTestPolicy.Mode);
        Assert.Contains(connectorId, handle.Origin.RelatedSceneObjectIds);
        Assert.Equal(edge.Source.VisualStateId, handle.Origin.VisualStateId);

        var gesture = new EditorGestureSnapshot(
            "test:route-bend",
            "inceptus.canvas2d:route-bend",
            new PointD(160d, 45d),
            new PointD(175d, 70d),
            [
                new KeyValuePair<string, PropertyValue>(
                    "inceptus.canvas2d:route-target-scene-object-id",
                    PropertyValue.FromText(connectorId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    "inceptus.canvas2d:route-target-visual-state-id",
                    PropertyValue.FromText(edge.Source.VisualStateId!.Value)),
                new KeyValuePair<string, PropertyValue>(
                    "inceptus.canvas2d:route-bend-index",
                    PropertyValue.FromInteger(1)),
            ]);
        var previewState = new EditorStateSnapshot(
            selection: [edge.Source.VisualStateId!],
            activeGesture: gesture);
        var previewScene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            previewState).Scene);
        var preview = Assert.Single(previewScene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "route-preview:",
                StringComparison.Ordinal) == true);
        var movedHandle = Assert.Single(previewScene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "route-bend-handle:",
                StringComparison.Ordinal) == true);

        Assert.Equal(
            [new PointD(110d, 45d), new PointD(175d, 70d), new PointD(210d, 45d)],
            preview.Geometry.Points.AsEnumerable());
        Assert.Equal(new RectD(170d, 65d, 10d, 10d), movedHandle.Bounds);
        Assert.Equal(Canvas2DHitTestMode.None, preview.HitTestPolicy.Mode);
    }

    [Fact]
    public void BridgedPersistentConnectorKeepsCanonicalEndpointsAndOnlyEditableBendHandles()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute()
            .WithCrossingConnector();
        var edge = inputs.Graph.Edges.Single(candidate =>
            candidate.Source.SemanticElementId == new SemanticElementId("test:semantic:ab"));
        var route = inputs.Routing.Routes.Single(candidate =>
            candidate.ProjectedEdgeId == edge.Id);
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(selection: [edge.Source.VisualStateId!])).Scene);
        var connector = scene.Items.Single(item => item.Id == connectorId);

        Assert.True(connector.Geometry.Points.Length > route.Path.Length);
        Assert.Equal(
            route.Path.AsEnumerable(),
            Canvas2DConnectorPathMetadata.Resolve(connector).AsEnumerable());
        Assert.Equal(
            route.Path.AsEnumerable(),
            Canvas2DConnectorPathMetadata.ResolveEditable(connector).AsEnumerable());
        var bend = Assert.Single(scene.Items.Where(item =>
            item.Metadata.TryGetValue(Canvas2DRouteGestureMetadata.HandleRole, out var role) &&
            role.Kind == PropertyValueKind.Text &&
            StringComparer.Ordinal.Equals(
                role.TextValue,
                Canvas2DRouteGestureMetadata.BendRole) &&
            item.Origin.RelatedSceneObjectIds.Contains(connectorId)));
        Assert.Equal(new PointD(160d, 45d), Center(bend.Bounds));

        var endpoints = scene.Items.Where(item =>
            item.Metadata.ContainsKey(Canvas2DConnectorEndpointMetadata.HandleRole) &&
            item.Origin.RelatedSceneObjectIds.Contains(connectorId)).ToArray();
        Assert.Equal(2, endpoints.Length);
        Assert.Equal(
            [new PointD(110d, 45d), new PointD(210d, 45d)],
            endpoints.Select(endpoint => Center(endpoint.Bounds))
                .OrderBy(static point => point.X));
    }

    [Fact]
    public void LayoutDerivedConnectorEndpointsStillExposeMatchingPersistentBendHandle()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var edge = Assert.Single(inputs.Graph.Edges);
        var original = Assert.Single(inputs.Routing.Routes);
        var routed = new RoutedConnectorGeometry(
            edge.Id,
            new PointD(original.SourceAnchor.X, original.SourceAnchor.Y + 5d),
            new PointD(original.DestinationAnchor.X, original.DestinationAnchor.Y - 5d),
            original.BendPoints);
        var routing = new RoutingResult(
            inputs.Routing.DocumentId,
            inputs.Routing.SourceRevision,
            inputs.Routing.LayoutAlgorithmId,
            inputs.Routing.RoutingAlgorithmId,
            new RoutingComputation([routed]));
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");

        var result = new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            routing,
            inputs.VisualModel,
            new EditorStateSnapshot(selection: [edge.Source.VisualStateId!]));
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var handle = Assert.Single(scene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "route-bend-handle:",
                StringComparison.Ordinal) == true);

        Assert.Equal(edge.Source.VisualStateId, handle.Origin.VisualStateId);
        Assert.Contains(connectorId, handle.Origin.RelatedSceneObjectIds);
        Assert.Equal(original.BendPoints[0], new PointD(
            handle.Bounds.X + (handle.Bounds.Width / 2d),
            handle.Bounds.Y + (handle.Bounds.Height / 2d)));
    }

    [Fact]
    public void ComputedOnlyOrMismatchedConnectorRouteGetsNoBendHandles()
    {
        var computed = Canvas2DSceneTestData.Create();
        var computedConnectorId = Canvas2DSceneObjectIdentity.ForProjected(
            Assert.Single(computed.Graph.Edges).Id,
            "connector");
        var computedScene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            computed.Graph,
            computed.Layout,
            computed.Routing,
            computed.VisualModel,
            new EditorStateSnapshot(
                selection: [Assert.Single(computed.Graph.Edges).Source.VisualStateId!])).Scene);

        Assert.DoesNotContain(computedScene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "route-bend-handle:",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void GroupAndOwnedLabelShareOneLogicalNonResizableSelection()
    {
        var inputs = Canvas2DSceneTestData.CreateWithGroup();
        var group = Assert.Single(inputs.Graph.Groups);
        var label = Assert.Single(inputs.Graph.Labels);
        var groupId = Canvas2DSceneObjectIdentity.ForProjected(group.Id, "group");
        var labelId = Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label");
        var baseScene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            EditorStateSnapshot.Empty).Scene);

        var groupItem = Assert.Single(baseScene.Items, item => item.Id == groupId);
        var labelItem = Assert.Single(baseScene.Items, item => item.Id == labelId);
        foreach (var item in new[] { groupItem, labelItem })
        {
            Assert.False(item.Metadata.ContainsKey("inceptus.canvas2d:move-capable"));
            Assert.False(item.Metadata.ContainsKey("inceptus.canvas2d:resize-capable"));
        }

        var selectedVisualStateId = Assert.IsType<VisualStateId>(groupItem.Origin.VisualStateId);
        Assert.Equal(selectedVisualStateId, labelItem.Origin.VisualStateId);

        var editorState = new EditorStateSnapshot(selection: [selectedVisualStateId]);
        var firstScene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            editorState).Scene);
        var repeatedScene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            editorState).Scene);
        var selectionOverlay = Assert.Single(firstScene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "selection:",
                StringComparison.Ordinal) == true);

        Assert.Contains(groupId, selectionOverlay.Origin.RelatedSceneObjectIds);
        Assert.DoesNotContain(labelId, selectionOverlay.Origin.RelatedSceneObjectIds);
        Assert.DoesNotContain(firstScene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "resize-",
                StringComparison.Ordinal) == true);
        Assert.DoesNotContain(firstScene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "route-bend-handle:",
                StringComparison.Ordinal) == true);
        Assert.Equal(firstScene, repeatedScene);
    }

    [Fact]
    public void EquivalentSeparatelyAllocatedInputsProduceStructurallyEqualScenes()
    {
        var first = Canvas2DSceneTestData.Create(reverseCallerOrder: false);
        var second = Canvas2DSceneTestData.Create(reverseCallerOrder: true);
        var builder = new Canvas2DSceneBuilder();

        var firstScene = Assert.IsType<Canvas2DScene>(builder.Build(
            first.Graph, first.Layout, first.Routing, first.VisualModel, first.EditorState).Scene);
        var secondScene = Assert.IsType<Canvas2DScene>(builder.Build(
            second.Graph, second.Layout, second.Routing, second.VisualModel, second.EditorState).Scene);

        Assert.Equal(first.Graph, second.Graph);
        Assert.Equal(first.Layout, second.Layout);
        Assert.Equal(first.Routing, second.Routing);
        Assert.Equal(firstScene, secondScene);
        Assert.Equal(
            firstScene.Items.Select(static item => item.Id),
            secondScene.Items.Select(static item => item.Id));
    }

    [Theory]
    [InlineData(InputFault.ForeignLayout)]
    [InlineData(InputFault.StaleLayout)]
    [InlineData(InputFault.ForeignRouting)]
    [InlineData(InputFault.StaleRouting)]
    [InlineData(InputFault.RoutingLayoutAlgorithmMismatch)]
    [InlineData(InputFault.ForeignVisualModel)]
    [InlineData(InputFault.StaleVisualModel)]
    public void IncompatibleInputsFailWithoutPartialScene(InputFault fault)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var layout = inputs.Layout;
        var routing = inputs.Routing;
        var visual = inputs.VisualModel;
        switch (fault)
        {
            case InputFault.ForeignLayout:
                layout = Canvas2DSceneTestData.CopyLayout(
                    inputs.Layout,
                    documentId: new DocumentId("test:foreign"));
                break;
            case InputFault.StaleLayout:
                layout = Canvas2DSceneTestData.CopyLayout(
                    inputs.Layout,
                    revision: inputs.Graph.SourceRevision.Increment());
                break;
            case InputFault.ForeignRouting:
                routing = Canvas2DSceneTestData.CopyRouting(
                    inputs.Routing,
                    documentId: new DocumentId("test:foreign"));
                break;
            case InputFault.StaleRouting:
                routing = Canvas2DSceneTestData.CopyRouting(
                    inputs.Routing,
                    revision: inputs.Graph.SourceRevision.Increment());
                break;
            case InputFault.RoutingLayoutAlgorithmMismatch:
                routing = Canvas2DSceneTestData.CopyRouting(
                    inputs.Routing,
                    layoutAlgorithmId: new AlgorithmId("test:layout:other"));
                break;
            case InputFault.ForeignVisualModel:
                visual = new VisualModelSnapshot(
                    new DocumentId("test:foreign"),
                    inputs.Graph.SourceRevision,
                    inputs.VisualModel.VisualStates);
                break;
            case InputFault.StaleVisualModel:
                visual = new VisualModelSnapshot(
                    inputs.Graph.DocumentId,
                    inputs.Graph.SourceRevision.Increment(),
                    inputs.VisualModel.VisualStates);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
        }

        var result = new Canvas2DSceneBuilder().Build(
            inputs.Graph, layout, routing, visual, inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Equal(Canvas2DSceneBuildStatus.Failed, result.Status);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void IncompatibleVisualTraceFailsWithoutPartialScene()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var referencedVisual = inputs.VisualModel.VisualStates[0];
        var incompatibleVisual = new VisualStateSnapshot(
            referencedVisual.Id,
            new SemanticElementId("test:semantic:other"),
            referencedVisual.Position,
            referencedVisual.Size,
            referencedVisual.PlacementMode,
            referencedVisual.Route,
            referencedVisual.Properties);
        var visualModel = new VisualModelSnapshot(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            inputs.VisualModel.VisualStates.Skip(1).Append(incompatibleVisual));

        var result = new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            visualModel,
            inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.IncompatibleVisualModel);
    }

    [Fact]
    public void MissingLayoutCoverageFailsWithoutPartialScene()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var incompleteLayout = new LayoutResult(
            inputs.Layout.DocumentId,
            inputs.Layout.SourceRevision,
            inputs.Layout.AlgorithmId,
            new LayoutComputation(inputs.Layout.Nodes.Skip(1), inputs.Layout.Groups));

        var result = new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            incompleteLayout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.IncompatibleLayoutResult);
    }

    [Fact]
    public void MissingRoutingCoverageFailsWithoutPartialScene()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var incompleteRouting = new RoutingResult(
            inputs.Routing.DocumentId,
            inputs.Routing.SourceRevision,
            inputs.Routing.LayoutAlgorithmId,
            inputs.Routing.RoutingAlgorithmId,
            RoutingComputation.Empty);

        var result = new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            incompleteRouting,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.IncompatibleRoutingResult);
    }

    [Fact]
    public void ExplicitNoRouteOutcomeComposesExactFallbackAndOmitsOnlyOwnedLabelAndLineJumps()
    {
        var inputs = Canvas2DSceneTestData.Create().WithCrossingConnector();
        var noRouteEdge = inputs.Graph.Edges[0];
        var routedEdge = inputs.Graph.Edges[1];
        var connectorLabel = new ProjectedLabel(
            noRouteEdge.Source,
            noRouteEdge.Id,
            "Unroutable connector");
        var graph = new ProjectedGraph(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            inputs.Graph.Nodes,
            inputs.Graph.Edges,
            inputs.Graph.Groups,
            inputs.Graph.Ports,
            inputs.Graph.Labels.Append(connectorLabel));
        var routing = new RoutingResult(
            inputs.Routing.DocumentId,
            inputs.Routing.SourceRevision,
            inputs.Routing.LayoutAlgorithmId,
            inputs.Routing.RoutingAlgorithmId,
            new RoutingComputation(
                routes: inputs.Routing.Routes.Where(route =>
                    route.ProjectedEdgeId != noRouteEdge.Id),
                noRouteEdgeIds: [noRouteEdge.Id]));

        var result = new Canvas2DSceneBuilder().Build(
            graph,
            inputs.Layout,
            routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.True(result.Succeeded, string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var fallbackId = Canvas2DSceneObjectIdentity.ForProjected(
            noRouteEdge.Id,
            "connector");
        var fallback = Assert.Single(scene.Items, item => item.Id == fallbackId);
        var expectedFallback = ExpectedImplicitFallback(inputs, noRouteEdge);
        Assert.All(expectedFallback, point =>
            Assert.True(DocumentGeometryBoundary.Contains(point)));
        Assert.True(DocumentGeometryBoundary.Contains(
            Midpoint(expectedFallback[0], expectedFallback[1])));
        Assert.True(Canvas2DConnectorPathMetadata.IsNoRouteFallbackPath(fallback));
        Assert.Equal(
            expectedFallback,
            Canvas2DConnectorPathMetadata.Resolve(fallback).AsEnumerable());
        Assert.Equal(noRouteEdge.Id, fallback.Origin.ProjectedObjectId);
        Assert.Equal(noRouteEdge.Source.VisualStateId, fallback.Origin.VisualStateId);
        Assert.Equal(Canvas2DHitTestMode.Stroke, fallback.HitTestPolicy.Mode);
        Assert.Equal(fallbackId, new Canvas2DSceneHitTestService().HitTest(
            scene,
            Midpoint(expectedFallback[0], expectedFallback[1]))?.SceneObjectId);
        var fallbackArrow = Assert.Single(scene.Items, item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(
                noRouteEdge.Id,
                "connector-target-arrow"));
        Assert.Equal(expectedFallback[1], fallbackArrow.Geometry.Points[0]);
        Assert.Empty(routing.Routes.Where(route => route.ProjectedEdgeId == noRouteEdge.Id));
        Assert.Contains(noRouteEdge.Id, routing.NoRouteEdgeIds);
        Assert.DoesNotContain(scene.Items, item =>
            item.Origin.ProjectedObjectId == connectorLabel.Id);
        Assert.Contains(scene.Items, item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(routedEdge.Id, "connector"));
        Assert.DoesNotContain(scene.Items, Canvas2DConnectorLineJumpMetadata.HasHitTarget);
    }

    [Fact]
    public void MultipleExplicitNoRouteOutcomesComposeIndependentFallbacksAndKeepRoutedConnector()
    {
        var inputs = Canvas2DSceneTestData.Create()
            .WithCrossingConnector()
            .WithParallelConnector();
        var routedEdge = Assert.Single(inputs.Graph.Edges, edge =>
            edge.Source.LocalKey == "multiple-no-route-routed");
        var routedGeometry = Assert.Single(inputs.Routing.Routes, route =>
            route.ProjectedEdgeId == routedEdge.Id);
        var noRouteEdges = inputs.Graph.Edges
            .Where(edge => edge.Id != routedEdge.Id)
            .ToArray();
        var routing = new RoutingResult(
            inputs.Routing.DocumentId,
            inputs.Routing.SourceRevision,
            inputs.Routing.LayoutAlgorithmId,
            inputs.Routing.RoutingAlgorithmId,
            new RoutingComputation(
                routes: [routedGeometry],
                noRouteEdgeIds: noRouteEdges.Select(static edge => edge.Id)));

        var result = new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.True(result.Succeeded, string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        foreach (var noRouteEdge in noRouteEdges)
        {
            var fallback = Assert.Single(scene.Items, item => item.Id ==
                Canvas2DSceneObjectIdentity.ForProjected(noRouteEdge.Id, "connector"));
            Assert.True(Canvas2DConnectorPathMetadata.IsNoRouteFallbackPath(fallback));
            Assert.Equal(
                ExpectedImplicitFallback(inputs, noRouteEdge),
                Canvas2DConnectorPathMetadata.Resolve(fallback).AsEnumerable());
            Assert.Contains(scene.Items, item => item.Id ==
                Canvas2DSceneObjectIdentity.ForProjected(
                    noRouteEdge.Id,
                    "connector-target-arrow"));
        }

        var routedConnector = Assert.Single(scene.Items, item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(routedEdge.Id, "connector"));
        Assert.Equal(
            routedGeometry.Path.AsEnumerable(),
            Canvas2DConnectorPathMetadata.Resolve(routedConnector).AsEnumerable());
        Assert.Contains(scene.Items, item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(
                routedEdge.Id,
                "connector-target-arrow"));
        Assert.DoesNotContain(scene.Items, Canvas2DConnectorLineJumpMetadata.HasHitTarget);
    }

    [Fact]
    public void SelectedNoRouteFallbackKeepsExactEndpointsAndAuthoredWaypointHandles()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var edge = Assert.Single(inputs.Graph.Edges);
        var expectedFallback = ExpectedImplicitFallback(inputs, edge);
        var routing = new RoutingResult(
            inputs.Routing.DocumentId,
            inputs.Routing.SourceRevision,
            inputs.Routing.LayoutAlgorithmId,
            inputs.Routing.RoutingAlgorithmId,
            new RoutingComputation(noRouteEdgeIds: [edge.Id]));
        var result = new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            routing,
            inputs.VisualModel,
            new EditorStateSnapshot(selection: [edge.Source.VisualStateId!]));

        Assert.True(result.Succeeded, string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
        var fallback = Assert.Single(scene.Items, item => item.Id == connectorId);
        Assert.True(Canvas2DConnectorPathMetadata.IsNoRouteFallbackPath(fallback));
        Assert.Equal(
            expectedFallback.AsEnumerable(),
            Canvas2DConnectorPathMetadata.Resolve(fallback).AsEnumerable());
        Assert.Equal(
            new[]
            {
                expectedFallback[0],
                new PointD(160d, 45d),
                expectedFallback[1],
            },
            Canvas2DConnectorPathMetadata.ResolveEditable(fallback).AsEnumerable());

        var endpointHandles = scene.Items.Where(item =>
            item.Origin.RelatedSceneObjectIds.Contains(connectorId) &&
            item.Metadata.ContainsKey(Canvas2DConnectorEndpointMetadata.HandleRole)).ToArray();
        Assert.Equal(2, endpointHandles.Length);
        Assert.Equal(
            expectedFallback.OrderBy(static point => point.X),
            endpointHandles.Select(item => Center(item.Bounds)).OrderBy(static point => point.X));

        var bendHandle = Assert.Single(scene.Items, item =>
            item.Origin.RelatedSceneObjectIds.Contains(connectorId) &&
            item.Metadata.TryGetValue(Canvas2DRouteGestureMetadata.HandleRole, out var role) &&
            role.Kind == PropertyValueKind.Text &&
            StringComparer.Ordinal.Equals(
                role.TextValue,
                Canvas2DRouteGestureMetadata.BendRole));
        Assert.Equal(new PointD(160d, 45d), Center(bendHandle.Bounds));
        Assert.Contains(scene.Items, item =>
            item.Origin.VisualStateId == edge.Source.VisualStateId &&
            item.Origin.StableSourceKey?.StartsWith(
                "selection:",
                StringComparison.Ordinal) == true);
        Assert.Equal(connectorId, new Canvas2DSceneHitTestService().HitTest(
            scene,
            Midpoint(
                expectedFallback[0],
                Midpoint(expectedFallback[0], expectedFallback[1])))?.SceneObjectId);
        Assert.Empty(routing.Routes);
        Assert.Equal(edge.Id, Assert.Single(routing.NoRouteEdgeIds));
    }

    [Fact]
    public void ConflictingNoRouteOutcomeFailsWithoutPartialScene()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var edge = Assert.Single(inputs.Graph.Edges);
        var conflictingRouting = new RoutingResult(
            inputs.Routing.DocumentId,
            inputs.Routing.SourceRevision,
            inputs.Routing.LayoutAlgorithmId,
            inputs.Routing.RoutingAlgorithmId,
            new RoutingComputation(
                inputs.Routing.Routes,
                noRouteEdgeIds: [edge.Id]));

        var result = new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            conflictingRouting,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.IncompatibleRoutingResult);
    }

    [Fact]
    public void ContributorsAreExplicitDeterministicAndReceiveExactImmutableInputs()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var calls = new List<string>();
        var first = new RecordingContributor("test:a", calls, inputs);
        var second = new RecordingContributor("test:b", calls, inputs);
        var registrations = new[]
        {
            Registration("test:b", second),
            Registration("test:a", first),
        };
        var builder = new Canvas2DSceneBuilder(
            new Canvas2DSceneConfiguration("test:configuration", "7"),
            registrations);

        var result = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        Assert.Equal(["test:a", "test:b"], calls);
        Assert.Equal(
            ["test:a", "test:b"],
            scene.Contributors.Select(static descriptor => descriptor.ContributorId.Value));
        Assert.Contains(scene.Items, item =>
            item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.RegisteredExtension));
    }

    [Fact]
    public void DuplicateContributorRegistrationFailsDeterministically()
    {
        var contributor = new RecordingContributor("test:duplicate", [], null);

        var exception = Assert.Throws<ArgumentException>(() => new Canvas2DSceneBuilder(
            contributors:
            [
                Registration("test:duplicate", contributor),
                Registration("test:duplicate", contributor),
            ]));

        Assert.Contains("test:duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ContributorExceptionIsDiagnosticAndNeverLeaksPartialScene()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var builder = new Canvas2DSceneBuilder(
            contributors:
            [
                Registration("test:fault", new ThrowingContributor()),
            ]);

        var result = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        var diagnostic = Assert.Single(result.Diagnostics, item =>
            item.Code == Canvas2DSceneDiagnosticCodes.ContributorFailure);
        Assert.DoesNotContain("sensitive", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(typeof(InvalidOperationException).FullName, diagnostic.Context["ExceptionType"]);
    }

    [Fact]
    public void MalformedContributorResultFailsWithoutPartialScene()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var builder = new Canvas2DSceneBuilder(
            contributors:
            [
                Registration("test:null-result", new NullResultContributor()),
            ]);

        var result = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.InvalidContribution);
    }

    [Fact]
    public void ContributorCannotBypassItsStableIdentityNamespace()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var builder = new Canvas2DSceneBuilder(
            contributors:
            [
                Registration("test:wrong-id", new WrongIdentityContributor()),
            ]);

        var result = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.InvalidSourceTrace);
    }

    [Fact]
    public void ContributorCannotClaimVisualStateWithMismatchedSemanticIdentity()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var builder = new Canvas2DSceneBuilder(
            contributors:
            [
                Registration("test:mismatched-visual-trace", new MismatchedVisualTraceContributor()),
            ]);

        var result = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.InvalidSourceTrace &&
            diagnostic.Message.Contains("incompatible Visual state", StringComparison.Ordinal));
    }

    [Fact]
    public void ContributorCannotClaimSemanticIdentityWithoutTraceableInputSource()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var builder = new Canvas2DSceneBuilder(
            contributors:
            [
                Registration("test:untraceable-semantic", new UntraceableSemanticContributor()),
            ]);

        var result = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.InvalidSourceTrace &&
            diagnostic.Message.Contains("untraceable semantic source", StringComparison.Ordinal));
    }

    [Fact]
    public void NonFiniteContributorGeometryFailsWithoutPartialScene()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var builder = new Canvas2DSceneBuilder(
            contributors:
            [
                Registration("test:invalid-geometry", new InvalidGeometryContributor()),
            ]);

        var result = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.InvalidGeometry);
    }

    [Fact]
    public void ContributorBoundsMustContainTransformedGeometry()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var builder = new Canvas2DSceneBuilder(
            contributors:
            [
                Registration("test:invalid-bounds", new InvalidBoundsContributor()),
            ]);

        var result = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.InvalidGeometry &&
            diagnostic.Message.Contains("transformed geometry", StringComparison.Ordinal));
    }

    [Fact]
    public void ContributorBoundsAllowFewUlpsOfTransformedEdgeDrift()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var itemBounds = new RectD(1024d, 2048d, 32d, 16d);
        var geometryBounds = new RectD(0d, 0d, 32d, 16d);
        var driftedX = IncrementRepresentableValue(itemBounds.X, 3);
        Assert.True(driftedX + geometryBounds.Width > itemBounds.Right);
        var builder = new Canvas2DSceneBuilder(
            contributors:
            [
                Registration(
                    "test:floating-edge-drift",
                    new FixedBoundsContributor(
                        geometryBounds,
                        Matrix2D.CreateTranslation(driftedX, itemBounds.Y),
                        itemBounds)),
            ]);

        var result = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Scene);
    }

    [Fact]
    public void ContributorBoundsRejectMaterialOverflowAtLargeLegalCoordinates()
    {
        const double largeCoordinate = 1_000_000_000_000_000d;
        const double materialOverflow = 16d;
        var inputs = Canvas2DSceneTestData.Create();
        var itemBounds = new RectD(largeCoordinate, largeCoordinate, 1024d, 1024d);
        var geometryBounds = new RectD(
            0d,
            0d,
            itemBounds.Width + materialOverflow,
            itemBounds.Height);
        var builder = new Canvas2DSceneBuilder(
            contributors:
            [
                Registration(
                    "test:large-coordinate-overflow",
                    new FixedBoundsContributor(
                        geometryBounds,
                        Matrix2D.CreateTranslation(largeCoordinate, largeCoordinate),
                        itemBounds)),
            ]);

        var result = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.InvalidGeometry &&
            diagnostic.Message.Contains("transformed geometry", StringComparison.Ordinal));
    }

    [Fact]
    public void ContributorMetadataNamespacesAreUnambiguous()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var builder = new Canvas2DSceneBuilder(
            contributors:
            [
                Registration("a:b", new MetadataContributor("c", "first")),
                Registration("a", new MetadataContributor("b:c", "second")),
            ]);

        var result = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Scene!.ContributorMetadata.Count);
        Assert.Collection(
            result.Scene.ContributorMetadata.Keys,
            key => Assert.Equal("1:a3:b:c", key),
            key => Assert.Equal("3:a:b1:c", key));
    }

    [Fact]
    public void EditorKindMetadataRemainsBuilderOwnedForGestureAndFeedback()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var conflictingProperties = new[]
        {
            new KeyValuePair<string, PropertyValue>(
                "inceptus.canvas2d:editor-kind",
                PropertyValue.FromText("caller-value")),
        };
        var editorState = new EditorStateSnapshot(
            activeGesture: new EditorGestureSnapshot(
                "gesture",
                "authoritative-gesture-kind",
                new PointD(0d, 0d),
                new PointD(10d, 10d),
                conflictingProperties),
            temporaryFeedback:
            [
                new EditorFeedbackSnapshot(
                    "feedback",
                    "authoritative-feedback-kind",
                    points: [new PointD(0d, 0d), new PointD(20d, 20d)],
                    properties: conflictingProperties),
            ]);

        var result = new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            editorState);

        Assert.True(result.Succeeded);
        var editorKinds = result.Scene!.Items
            .Where(item => item.Origin.StableSourceKey is "gesture:gesture" or "feedback:feedback")
            .Select(item => item.Metadata["inceptus.canvas2d:editor-kind"].TextValue)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Collection(
            editorKinds,
            kind => Assert.Equal("authoritative-feedback-kind", kind),
            kind => Assert.Equal("authoritative-gesture-kind", kind));
    }

    [Fact]
    public void ContributorOnlyFeedbackSuppressesOnlyTheGenericFeedbackPrimitive()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var contributor = new FeedbackPresentationContributor("feedback");
        var builder = new Canvas2DSceneBuilder(
            contributors:
            [
                Registration("test:feedback-presentation", contributor),
            ]);
        var bounds = new RectD(20d, 30d, 40d, 50d);
        var defaultEditorState = new EditorStateSnapshot(
            temporaryFeedback:
            [
                new EditorFeedbackSnapshot("feedback", "test:feedback", bounds),
            ]);
        var contributorOnlyEditorState = new EditorStateSnapshot(
            temporaryFeedback:
            [
                new EditorFeedbackSnapshot(
                    "feedback",
                    "test:feedback",
                    bounds,
                    presentationMode: EditorFeedbackPresentationMode.ContributorOnly),
            ]);

        var defaultResult = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            defaultEditorState);
        var contributorOnlyResult = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            contributorOnlyEditorState);
        var clearedResult = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            EditorStateSnapshot.Empty);

        Assert.True(defaultResult.Succeeded, Diagnostics(defaultResult.Diagnostics));
        Assert.True(
            contributorOnlyResult.Succeeded,
            Diagnostics(contributorOnlyResult.Diagnostics));
        Assert.True(clearedResult.Succeeded, Diagnostics(clearedResult.Diagnostics));
        var defaultScene = Assert.IsType<Canvas2DScene>(defaultResult.Scene);
        var contributorOnlyScene = Assert.IsType<Canvas2DScene>(
            contributorOnlyResult.Scene);
        var clearedScene = Assert.IsType<Canvas2DScene>(clearedResult.Scene);

        Assert.Contains(defaultScene.Items, static item =>
            item.Origin.StableSourceKey == "feedback:feedback");
        Assert.DoesNotContain(contributorOnlyScene.Items, static item =>
            item.Origin.StableSourceKey == "feedback:feedback");
        Assert.Contains(defaultScene.Items, static item =>
            item.Origin.StableSourceKey == "contributor-feedback:feedback");
        Assert.Contains(contributorOnlyScene.Items, static item =>
            item.Origin.StableSourceKey == "contributor-feedback:feedback");
        Assert.DoesNotContain(clearedScene.Items, static item =>
            item.Origin.StableSourceKey is "feedback:feedback" or
                "contributor-feedback:feedback");
        Assert.Equal(
            [EditorFeedbackPresentationMode.Default,
                EditorFeedbackPresentationMode.ContributorOnly],
            contributor.ObservedModes);
        Assert.Equal(inputs.Graph.SourceRevision, defaultScene.SourceRevision);
        Assert.Equal(inputs.Graph.SourceRevision, contributorOnlyScene.SourceRevision);
    }

    [Fact]
    public void ContributorMustPreserveApplicableProjectedRelationships()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var builder = new Canvas2DSceneBuilder(
            contributors:
            [
                Registration("test:incomplete-relations", new IncompleteRelationshipContributor()),
            ]);

        var result = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.InvalidSourceTrace &&
            diagnostic.Message.Contains("incomplete projected relationship", StringComparison.Ordinal));
    }

    [Fact]
    public void NeutralViewportBuildsNonHittablePositiveBoundaryRaysAtTheCanvasEdges()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var editorState = new EditorStateSnapshot(viewport: new ViewportSnapshot(
            1d,
            default,
            new RectD(0d, 0d, 900d, 600d)));

        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            editorState).Scene);

        var vertical = BoundaryGuide(scene, "document-boundary:y-axis");
        var horizontal = BoundaryGuide(scene, "document-boundary:x-axis");
        Assert.Equal(
            [new PointD(0d, 0d), new PointD(0d, 600d)],
            vertical.Geometry.Points.AsEnumerable());
        Assert.Equal(
            [new PointD(0d, 0d), new PointD(900d, 0d)],
            horizontal.Geometry.Points.AsEnumerable());
        Assert.All(
            new[] { vertical, horizontal },
            guide =>
            {
                Assert.Equal(Canvas2DSceneLayer.Background, guide.Layer);
                Assert.Equal(Canvas2DHitTestMode.None, guide.HitTestPolicy.Mode);
                Assert.Equal(Canvas2DSceneOriginCategory.EditorState, guide.Origin.Categories);
                Assert.Equal("#94a3b8", guide.Style.Stroke);
                Assert.Equal(1d, guide.Style.StrokeWidth);
                Assert.Equal([4d, 4d], guide.Style.DashPattern.AsEnumerable());
                Assert.Equal(0.8d, guide.Style.Opacity);
            });
        Assert.NotEqual(
            vertical.Id,
            new Canvas2DSceneHitTestService().HitTest(scene, new PointD(0d, 100d))
                ?.SceneObjectId);
    }

    [Theory]
    [InlineData(0.7d)]
    [InlineData(1d)]
    [InlineData(1.1d)]
    public void NegativeSpacePanClipsGuidesToPositiveRaysAndKeepsScreenStyleStable(
        double zoom)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var pan = new VectorD(70d, 35d);
        var visible = new RectD(-100d, -50d, 400d, 300d);
        var editorState = new EditorStateSnapshot(viewport: new ViewportSnapshot(
            zoom,
            pan,
            visible));

        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            editorState).Scene);

        var vertical = BoundaryGuide(scene, "document-boundary:y-axis");
        var horizontal = BoundaryGuide(scene, "document-boundary:x-axis");
        Assert.Equal(
            [new PointD(0d, 0d), new PointD(0d, 250d)],
            vertical.Geometry.Points.AsEnumerable());
        Assert.Equal(
            [new PointD(0d, 0d), new PointD(300d, 0d)],
            horizontal.Geometry.Points.AsEnumerable());
        Assert.Equal(
            new PointD(pan.X, pan.Y),
            scene.ViewportTransform.TransformPoint(default));
        Assert.Equal(1d, vertical.Style.StrokeWidth * zoom, 12);
        Assert.All(vertical.Style.DashPattern, dash => Assert.Equal(4d, dash * zoom, 12));
    }

    [Theory]
    [InlineData(-100d, 20d, 50d, 100d, 0)]
    [InlineData(-10d, 20d, 20d, 100d, 1)]
    [InlineData(20d, -10d, 100d, 20d, 1)]
    [InlineData(-10d, -10d, 20d, 20d, 2)]
    public void GuidesAppearOnlyWhereVisibleViewportIntersectsPositiveBoundaryRays(
        double x,
        double y,
        double width,
        double height,
        int expectedCount)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var editorState = new EditorStateSnapshot(viewport: new ViewportSnapshot(
            1d,
            default,
            new RectD(x, y, width, height)));

        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            editorState).Scene);

        Assert.Equal(expectedCount, scene.Items.Count(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "document-boundary:",
                StringComparison.Ordinal) == true));
    }

    [Fact]
    public void VisibleDocumentRegionUsesViewportTransformAndIgnoresDevicePixelRatio()
    {
        var viewport = new ViewportSnapshot(2d, new VectorD(40d, -20d));
        var dprOne = Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(
            viewport,
            new Canvas2DSurfaceSize(800d, 600d, 1d));
        var dprOnePointTwoFive = Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(
            viewport,
            new Canvas2DSurfaceSize(800d, 600d, 1.25d));

        Assert.Equal(new RectD(-20d, 10d, 400d, 300d), dprOne);
        Assert.Equal(dprOne, dprOnePointTwoFive);
    }

    [Fact]
    public void EditorStateOnlyRebuildReusesUpstreamResultsAndChangesOnlyScenePresentation()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var builder = new Canvas2DSceneBuilder();
        var firstScene = Assert.IsType<Canvas2DScene>(builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            EditorStateSnapshot.Empty).Scene);
        var target = firstScene.Items.First(item => item.Layer == Canvas2DSceneLayer.Content);
        var updatedEditorState = new EditorStateSnapshot(
            selection: [target.Origin.VisualStateId!],
            hoveredObjectId: target.Id,
            activeToolId: "test:move",
            viewport: new ViewportSnapshot(2d, new VectorD(30d, 40d)),
            activeGesture: new EditorGestureSnapshot(
                "drag-1",
                "drag",
                new PointD(10d, 10d),
                new PointD(20d, 25d)),
            temporaryFeedback:
            [
                new EditorFeedbackSnapshot(
                    "guide-1",
                    "guide",
                    points: [new PointD(0d, 30d), new PointD(300d, 30d)]),
            ]);

        var secondScene = Assert.IsType<Canvas2DScene>(builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            updatedEditorState).Scene);

        Assert.NotEqual(firstScene, secondScene);
        Assert.Equal(firstScene.DocumentId, secondScene.DocumentId);
        Assert.Equal(firstScene.SourceRevision, secondScene.SourceRevision);
        Assert.Equal(firstScene.Items.Where(item => item.Layer != Canvas2DSceneLayer.Overlay),
            secondScene.Items.Where(item => item.Layer != Canvas2DSceneLayer.Overlay));
        Assert.Equal(12, secondScene.Items.Count(item => item.Layer == Canvas2DSceneLayer.Overlay));
        Assert.Equal(updatedEditorState.Viewport, secondScene.Viewport);
        Assert.Equal(
            new PointD(32d, 42d),
            secondScene.ViewportTransform.TransformPoint(new PointD(1d, 1d)));
        Assert.Equal("test:move", secondScene.ActiveToolId);
        Assert.Same(inputs.Graph, inputs.Graph);
        Assert.Same(inputs.Layout, inputs.Layout);
        Assert.Same(inputs.Routing, inputs.Routing);
    }

    private static Canvas2DSceneItem BoundaryGuide(
        Canvas2DScene scene,
        string stableSourceKey) =>
        Assert.Single(scene.Items, item =>
            StringComparer.Ordinal.Equals(
                item.Origin.StableSourceKey,
                stableSourceKey));

    [Fact]
    public void SelectionOnlyChangeRebuildsOnlySelectionPresentation()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var builder = new Canvas2DSceneBuilder();
        var original = Assert.IsType<Canvas2DScene>(builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            EditorStateSnapshot.Empty).Scene);
        var target = original.Items.First(item => item.Layer == Canvas2DSceneLayer.Content);

        var rebuilt = Assert.IsType<Canvas2DScene>(builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(selection: [target.Origin.VisualStateId!])).Scene);

        Assert.NotEqual(original, rebuilt);
        var overlay = Assert.Single(rebuilt.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.HitTestPolicy.Mode == Canvas2DHitTestMode.None &&
            item.Origin.StableSourceKey?.StartsWith(
                "selection:",
                StringComparison.Ordinal) == true);
        Assert.Contains(target.Id, overlay.Origin.RelatedSceneObjectIds);
        Assert.Contains("selection", overlay.Origin.StableSourceKey, StringComparison.Ordinal);
        Assert.Equal(
            original.Items,
            rebuilt.Items.Where(item => item.Layer != Canvas2DSceneLayer.Overlay));
    }

    [Fact]
    public void HoverOnlyChangeRebuildsOnlyHoverPresentation()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var builder = new Canvas2DSceneBuilder();
        var original = Assert.IsType<Canvas2DScene>(builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            EditorStateSnapshot.Empty).Scene);
        var target = original.Items.First(item => item.Layer == Canvas2DSceneLayer.Content);

        var rebuilt = Assert.IsType<Canvas2DScene>(builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(hoveredObjectId: target.Id)).Scene);

        Assert.NotEqual(original, rebuilt);
        var overlay = Assert.Single(rebuilt.Items, item => item.Layer == Canvas2DSceneLayer.Overlay);
        Assert.Contains(target.Id, overlay.Origin.RelatedSceneObjectIds);
        Assert.Contains("hover", overlay.Origin.StableSourceKey, StringComparison.Ordinal);
        Assert.Equal(
            original.Items,
            rebuilt.Items.Where(item => item.Layer != Canvas2DSceneLayer.Overlay));
    }

    [Fact]
    public void MissingProjectedSceneRepresentationIsRejectedDirectly()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState).Scene);
        var missingProjectedId = inputs.Graph.Nodes[0].Id;
        var items = scene.Items
            .Where(item => item.Origin.ProjectedObjectId != missingProjectedId)
            .ToList();
        var diagnostics = new List<Diagnostic>();
        var validate = typeof(Canvas2DSceneBuilder).GetMethod(
            "ValidateAndOrderSceneItems",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(validate);
        validate.Invoke(
            null,
            [
                inputs.Graph,
                inputs.Routing,
                inputs.VisualModel,
                items,
                diagnostics,
                null,
                null,
            ]);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.MissingProjectedObject &&
            diagnostic.SourceIdentity == missingProjectedId.Value);
    }

    [Fact]
    public void StaleEditorReferencesProduceWarningButCompleteScene()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var editorState = new EditorStateSnapshot(
            selection: [new VisualStateId("visual:missing")]);

        var result = new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            editorState);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.StaleEditorStateReference &&
            diagnostic.Severity == DiagnosticSeverity.Warning);
    }

    private static ProjectedGraph WithLabel(
        Canvas2DSceneTestData inputs,
        ProjectedLabel label) =>
        new(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            inputs.Graph.Nodes,
            inputs.Graph.Edges,
            inputs.Graph.Groups,
            inputs.Graph.Ports,
            [label]);

    private static VisualModelSnapshot WithNodeLabelOverride(
        VisualModelSnapshot source,
        VisualStateId visualStateId,
        NodeLabelVisualOverride visualOverride) =>
        new(
            source.DocumentId,
            source.Revision,
            source.VisualStates.Select(visual =>
                visual.Id == visualStateId
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

    private static EditorGestureSnapshot CreateNodeLabelGesture(
        string id,
        ProjectedNode owner,
        ProjectedLabel label,
        SceneObjectId nodeSceneObjectId,
        SceneObjectId labelSceneObjectId,
        VectorD delta,
        string operation) =>
        new(
            id,
            Canvas2DNodeLabelGestureMetadata.Kind,
            default,
            new PointD(delta.X, delta.Y),
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DNodeLabelGestureMetadata.TargetNodeSceneObjectId,
                    PropertyValue.FromText(nodeSceneObjectId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DNodeLabelGestureMetadata.TargetLabelSceneObjectId,
                    PropertyValue.FromText(labelSceneObjectId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DNodeLabelGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(owner.Source.VisualStateId!.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DNodeLabelGestureMetadata.TargetLabelProjectedObjectId,
                    PropertyValue.FromText(label.Id.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DNodeLabelGestureMetadata.Operation,
                    PropertyValue.FromText(operation)),
            ]);

    private static EditorGestureSnapshot CreateIndexedMoveGesture(
        string id,
        PointD origin,
        PointD current,
        params (SceneObjectId SceneObjectId, VisualStateId VisualStateId)[] targets)
    {
        var properties = new List<KeyValuePair<string, PropertyValue>>
        {
            new(
                Canvas2DMoveGestureMetadata.TargetCount,
                PropertyValue.FromInteger(targets.Length)),
        };
        for (var index = 0; index < targets.Length; index++)
        {
            properties.Add(new KeyValuePair<string, PropertyValue>(
                Canvas2DMoveGestureMetadata.IndexedSceneObjectId(index),
                PropertyValue.FromText(targets[index].SceneObjectId.Value)));
            properties.Add(new KeyValuePair<string, PropertyValue>(
                Canvas2DMoveGestureMetadata.IndexedVisualStateId(index),
                PropertyValue.FromText(targets[index].VisualStateId.Value)));
        }

        return new EditorGestureSnapshot(
            id,
            Canvas2DMoveGestureMetadata.Kind,
            origin,
            current,
            properties);
    }

    private static Canvas2DSceneItem FindGesturePreview(
        Canvas2DScene scene,
        string prefix,
        SceneObjectId sourceId) =>
        scene.Items.Single(item =>
            item.Origin.StableSourceKey?.StartsWith(prefix, StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(sourceId));

    private static PointD DocumentTextAnchor(Canvas2DSceneItem item) =>
        item.Transform.TransformPoint(item.Geometry.TextAnchor);

    private static PointD Center(RectD bounds) =>
        new(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static PointD Midpoint(PointD start, PointD end) =>
        new((start.X + end.X) / 2d, (start.Y + end.Y) / 2d);

    private static PointD[] ExpectedImplicitFallback(
        Canvas2DSceneTestData inputs,
        ProjectedEdge edge)
    {
        var sourceBounds = inputs.Layout.Nodes.Single(node =>
            node.ProjectedObjectId == edge.SourceNodeId).Bounds;
        var targetBounds = inputs.Layout.Nodes.Single(node =>
            node.ProjectedObjectId == edge.TargetNodeId).Bounds;
        return
        [
            new PointD(sourceBounds.Right, sourceBounds.Top + (sourceBounds.Height / 2d)),
            new PointD(targetBounds.Left, targetBounds.Top + (targetBounds.Height / 2d)),
        ];
    }

    private static void AssertPointEqual(PointD expected, PointD actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 10);
        Assert.Equal(expected.Y, actual.Y, precision: 10);
    }

    private static double IncrementRepresentableValue(double value, int count)
    {
        for (var index = 0; index < count; index++)
        {
            value = Math.BitIncrement(value);
        }

        return value;
    }

    private static Canvas2DSceneContributorRegistration Registration(
        string id,
        ICanvas2DSceneContributor contributor) =>
        new(
            new Canvas2DSceneContributorDescriptor(
                new Canvas2DSceneContributorId(id),
                "1"),
            contributor);

    public enum InputFault
    {
        ForeignLayout,
        StaleLayout,
        ForeignRouting,
        StaleRouting,
        RoutingLayoutAlgorithmMismatch,
        ForeignVisualModel,
        StaleVisualModel,
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MissingRequiredInputReturnsDiagnosticWithoutPartialScene(int missingInputIndex)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var result = new Canvas2DSceneBuilder().Build(
            missingInputIndex == 0 ? null! : inputs.Graph,
            missingInputIndex == 1 ? null! : inputs.Layout,
            missingInputIndex == 2 ? null! : inputs.Routing,
            missingInputIndex == 3 ? null! : inputs.VisualModel,
            missingInputIndex == 4 ? null! : inputs.EditorState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.InvalidInput);
    }

    private sealed class RecordingContributor(
        string id,
        List<string> calls,
        Canvas2DSceneTestData? expected) : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            if (expected is not null)
            {
                Assert.Same(expected.Graph, context.ProjectedGraph);
                Assert.Same(expected.Layout, context.LayoutResult);
                Assert.Same(expected.Routing, context.RoutingResult);
                Assert.Same(expected.VisualModel, context.VisualModel);
                Assert.Same(expected.EditorState, context.EditorState);
            }

            calls.Add(id);
            var contributorId = context.Contributor.ContributorId;
            var item = new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForExtension(contributorId, "primary"),
                Canvas2DSceneLayer.Overlay,
                10,
                Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 5d, 5d)),
                new Canvas2DSceneOriginTrace(
                    Canvas2DSceneOriginCategory.RegisteredExtension,
                    stableSourceKey: "primary"));
            return Canvas2DSceneContributionResult.Success(
                new Canvas2DSceneContribution([item]));
        }
    }

    private sealed class FeedbackPresentationContributor(string feedbackId) :
        ICanvas2DSceneContributor
    {
        internal List<EditorFeedbackPresentationMode> ObservedModes { get; } = [];

        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            var feedback = context.EditorState.TemporaryFeedback.FirstOrDefault(item =>
                StringComparer.Ordinal.Equals(item.Id, feedbackId));
            if (feedback is null)
            {
                return Canvas2DSceneContributionResult.Success(
                    new Canvas2DSceneContribution());
            }

            ObservedModes.Add(feedback.PresentationMode);
            var item = new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForExtension(
                    context.Contributor.ContributorId,
                    $"contributor-feedback:{feedback.Id}"),
                Canvas2DSceneLayer.Overlay,
                3999,
                Canvas2DSceneGeometry.Rectangle(feedback.Bounds!.Value),
                new Canvas2DSceneOriginTrace(
                    Canvas2DSceneOriginCategory.RegisteredExtension,
                    stableSourceKey: $"contributor-feedback:{feedback.Id}"));
            return Canvas2DSceneContributionResult.Success(
                new Canvas2DSceneContribution([item]));
        }
    }

    private static string Diagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class ThrowingContributor : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context) =>
            throw new InvalidOperationException("sensitive contributor exception");
    }

    private sealed class NullResultContributor : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context) =>
            null!;
    }

    private sealed class WrongIdentityContributor : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            var item = new Canvas2DSceneItem(
                new SceneObjectId("scene:not-contributor-namespaced"),
                Canvas2DSceneLayer.Overlay,
                0,
                Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 1d, 1d)),
                new Canvas2DSceneOriginTrace(
                    Canvas2DSceneOriginCategory.RegisteredExtension,
                    stableSourceKey: "primary"));
            return Canvas2DSceneContributionResult.Success(
                new Canvas2DSceneContribution([item]));
        }
    }

    private sealed class InvalidGeometryContributor : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            var geometry = Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 5d, 5d));
            object boxedBounds = geometry.Bounds;
            typeof(RectD).GetField(
                    "<X>k__BackingField",
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(boxedBounds, double.NaN);
            typeof(Canvas2DSceneGeometry).GetField(
                    "<Bounds>k__BackingField",
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(geometry, (RectD)boxedBounds);

            var contributorId = context.Contributor.ContributorId;
            var item = new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForExtension(contributorId, "invalid"),
                Canvas2DSceneLayer.Decoration,
                0,
                geometry,
                new Canvas2DSceneOriginTrace(
                    Canvas2DSceneOriginCategory.RegisteredExtension,
                    stableSourceKey: "invalid"));
            return Canvas2DSceneContributionResult.Success(
                new Canvas2DSceneContribution([item]));
        }
    }

    private sealed class MismatchedVisualTraceContributor : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            var contributorId = context.Contributor.ContributorId;
            var visualId = context.VisualModel.VisualStates[0].Id;
            var item = new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForExtension(contributorId, "mismatched-visual"),
                Canvas2DSceneLayer.Decoration,
                0,
                Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 5d, 5d)),
                new Canvas2DSceneOriginTrace(
                    Canvas2DSceneOriginCategory.RegisteredExtension |
                    Canvas2DSceneOriginCategory.SemanticElement |
                    Canvas2DSceneOriginCategory.VisualState,
                    new SemanticElementId("test:semantic:not-the-visual-owner"),
                    visualId,
                    stableSourceKey: "mismatched-visual"));
            return Canvas2DSceneContributionResult.Success(
                new Canvas2DSceneContribution([item]));
        }
    }

    private sealed class UntraceableSemanticContributor : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            var contributorId = context.Contributor.ContributorId;
            var item = new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForExtension(contributorId, "untraceable-semantic"),
                Canvas2DSceneLayer.Decoration,
                0,
                Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 5d, 5d)),
                new Canvas2DSceneOriginTrace(
                    Canvas2DSceneOriginCategory.RegisteredExtension |
                    Canvas2DSceneOriginCategory.SemanticElement,
                    new SemanticElementId("test:semantic:not-in-scene-inputs"),
                    stableSourceKey: "untraceable-semantic"));
            return Canvas2DSceneContributionResult.Success(
                new Canvas2DSceneContribution([item]));
        }
    }

    private sealed class InvalidBoundsContributor : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            var contributorId = context.Contributor.ContributorId;
            var item = new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForExtension(contributorId, "invalid-bounds"),
                Canvas2DSceneLayer.Decoration,
                0,
                Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 20d, 20d)),
                new Canvas2DSceneOriginTrace(
                    Canvas2DSceneOriginCategory.RegisteredExtension,
                    stableSourceKey: "invalid-bounds"),
                transform: Matrix2D.CreateTranslation(100d, 100d),
                bounds: new RectD(0d, 0d, 1d, 1d));
            return Canvas2DSceneContributionResult.Success(
                new Canvas2DSceneContribution([item]));
        }
    }

    private sealed class FixedBoundsContributor(
        RectD geometryBounds,
        Matrix2D transform,
        RectD itemBounds) : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            var contributorId = context.Contributor.ContributorId;
            var item = new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForExtension(contributorId, "fixed-bounds"),
                Canvas2DSceneLayer.Decoration,
                0,
                Canvas2DSceneGeometry.Rectangle(geometryBounds),
                new Canvas2DSceneOriginTrace(
                    Canvas2DSceneOriginCategory.RegisteredExtension,
                    stableSourceKey: "fixed-bounds"),
                transform: transform,
                bounds: itemBounds);
            return Canvas2DSceneContributionResult.Success(
                new Canvas2DSceneContribution([item]));
        }
    }

    private sealed class MetadataContributor(string key, string value) : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context) =>
            Canvas2DSceneContributionResult.Success(
                new Canvas2DSceneContribution(
                    metadata:
                    [
                        new KeyValuePair<string, PropertyValue>(
                            key,
                            PropertyValue.FromText(value)),
                    ]));
    }

    private sealed class IncompleteRelationshipContributor : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            var edge = context.ProjectedGraph.Edges[0];
            var contributorId = context.Contributor.ContributorId;
            var categories = Canvas2DSceneOriginCategory.RegisteredExtension |
                Canvas2DSceneOriginCategory.SemanticElement |
                Canvas2DSceneOriginCategory.ProjectedRuntimeObject;
            if (edge.Source.VisualStateId is not null)
            {
                categories |= Canvas2DSceneOriginCategory.VisualState;
            }

            var item = new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForExtension(contributorId, "incomplete-relations"),
                Canvas2DSceneLayer.Decoration,
                0,
                Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 5d, 5d)),
                new Canvas2DSceneOriginTrace(
                    categories,
                    edge.Source.SemanticElementId,
                    edge.Source.VisualStateId,
                    edge.Id,
                    "incomplete-relations"));
            return Canvas2DSceneContributionResult.Success(
                new Canvas2DSceneContribution([item]));
        }
    }
}

internal sealed class Canvas2DSceneTestData
{
    private static readonly DocumentId DocumentId = new("test:scene-document");
    private static readonly DocumentRevision Revision = new(3);
    private static readonly AlgorithmId LayoutAlgorithmId = new("test:layout");
    private static readonly AlgorithmId RoutingAlgorithmId = new("test:routing");
    private static readonly ProjectionRuleId NodeRuleId = new("test:projection:node");
    private static readonly ProjectionRuleId EdgeRuleId = new("test:projection:edge");

    private Canvas2DSceneTestData(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingResult routing,
        VisualModelSnapshot visualModel,
        EditorStateSnapshot editorState)
    {
        Graph = graph;
        Layout = layout;
        Routing = routing;
        VisualModel = visualModel;
        EditorState = editorState;
    }

    internal ProjectedGraph Graph { get; }
    internal LayoutResult Layout { get; }
    internal RoutingResult Routing { get; }
    internal VisualModelSnapshot VisualModel { get; }
    internal EditorStateSnapshot EditorState { get; }

    internal Canvas2DSceneTestData WithVisualModel(VisualModelSnapshot visualModel) =>
        new(Graph, Layout, Routing, visualModel, EditorState);

    internal Canvas2DSceneTestData WithGraph(ProjectedGraph graph) =>
        new(graph, Layout, Routing, VisualModel, EditorState);

    internal Canvas2DSceneTestData WithCrossingConnector()
    {
        var semanticC = new SemanticElementId("test:semantic:crossing-c");
        var semanticD = new SemanticElementId("test:semantic:crossing-d");
        var semanticEdge = new SemanticElementId("test:semantic:crossing-cd");
        var visualC = new VisualStateId("test:visual:crossing-c");
        var visualD = new VisualStateId("test:visual:crossing-d");
        var visualEdge = new VisualStateId("test:visual:crossing-cd");
        var nodeC = new ProjectedNode(Source("crossing-c", semanticC, NodeRuleId, visualC));
        var nodeD = new ProjectedNode(Source("crossing-d", semanticD, NodeRuleId, visualD));
        var edge = new ProjectedEdge(
            Source(
                "crossing-cd",
                semanticEdge,
                EdgeRuleId,
                visualEdge,
                ProjectionSourceKind.SemanticRelationship),
            nodeC.Id,
            nodeD.Id);
        var graph = new ProjectedGraph(
            Graph.DocumentId,
            Graph.SourceRevision,
            Graph.Nodes.Concat([nodeC, nodeD]),
            Graph.Edges.Append(edge),
            Graph.Groups,
            Graph.Ports,
            Graph.Labels);
        var layout = new LayoutResult(
            Layout.DocumentId,
            Layout.SourceRevision,
            Layout.AlgorithmId,
            new LayoutComputation(Layout.Nodes.Concat(
            [
                new LayoutNodeGeometry(
                    nodeC.Id,
                    new RectD(170d, 0d, 20d, 20d),
                    Matrix2D.CreateTranslation(170d, 0d)),
                new LayoutNodeGeometry(
                    nodeD.Id,
                    new RectD(170d, 70d, 20d, 20d),
                    Matrix2D.CreateTranslation(170d, 70d)),
            ])));
        var crossingRoute = new RoutedConnectorGeometry(
            edge.Id,
            new PointD(180d, 20d),
            new PointD(180d, 70d));
        var routing = new RoutingResult(
            Routing.DocumentId,
            Routing.SourceRevision,
            Routing.LayoutAlgorithmId,
            Routing.RoutingAlgorithmId,
            new RoutingComputation(Routing.Routes.Append(crossingRoute)));
        var visualModel = new VisualModelSnapshot(
            VisualModel.DocumentId,
            VisualModel.Revision,
            VisualModel.VisualStates.Concat(
            [
                new VisualStateSnapshot(
                    visualC,
                    semanticC,
                    new PointD(170d, 0d),
                    new SizeD(20d, 20d),
                    VisualPlacementMode.Pinned),
                new VisualStateSnapshot(
                    visualD,
                    semanticD,
                    new PointD(170d, 70d),
                    new SizeD(20d, 20d),
                    VisualPlacementMode.Pinned),
                new VisualStateSnapshot(
                    visualEdge,
                    semanticEdge,
                    default,
                    default,
                    VisualPlacementMode.Automatic),
            ]));
        return new Canvas2DSceneTestData(
            graph,
            layout,
            routing,
            visualModel,
            EditorState);
    }

    internal Canvas2DSceneTestData WithParallelConnector()
    {
        var semanticEdge = new SemanticElementId(
            "test:semantic:multiple-no-route-routed");
        var visualEdge = new VisualStateId("test:visual:multiple-no-route-routed");
        var edge = new ProjectedEdge(
            Source(
                "multiple-no-route-routed",
                semanticEdge,
                EdgeRuleId,
                visualEdge,
                ProjectionSourceKind.SemanticRelationship),
            Graph.Nodes[0].Id,
            Graph.Nodes[1].Id);
        var baseline = Routing.Routes[0];
        var route = new RoutedConnectorGeometry(
            edge.Id,
            baseline.SourceAnchor,
            baseline.DestinationAnchor,
            baseline.BendPoints,
            baseline.SourcePortId,
            baseline.TargetPortId,
            baseline.Metadata);
        var graph = new ProjectedGraph(
            Graph.DocumentId,
            Graph.SourceRevision,
            Graph.Nodes,
            Graph.Edges.Append(edge),
            Graph.Groups,
            Graph.Ports,
            Graph.Labels);
        var routing = new RoutingResult(
            Routing.DocumentId,
            Routing.SourceRevision,
            Routing.LayoutAlgorithmId,
            Routing.RoutingAlgorithmId,
            new RoutingComputation(Routing.Routes.Append(route)));
        var visualModel = new VisualModelSnapshot(
            VisualModel.DocumentId,
            VisualModel.Revision,
            VisualModel.VisualStates.Append(new VisualStateSnapshot(
                visualEdge,
                semanticEdge,
                default,
                default,
                VisualPlacementMode.Automatic)));
        return new Canvas2DSceneTestData(
            graph,
            Layout,
            routing,
            visualModel,
            EditorState);
    }

    internal static Canvas2DSceneTestData Create(bool reverseCallerOrder = false)
    {
        var semanticA = new SemanticElementId("test:semantic:a");
        var semanticB = new SemanticElementId("test:semantic:b");
        var visualA = new VisualStateId("test:visual:a");
        var visualB = new VisualStateId("test:visual:b");
        var visualEdge = new VisualStateId("test:visual:ab");
        var nodeA = new ProjectedNode(Source("a", semanticA, NodeRuleId, visualA));
        var nodeB = new ProjectedNode(Source("b", semanticB, NodeRuleId, visualB));
        var edge = new ProjectedEdge(
            Source("ab", new SemanticElementId("test:semantic:ab"), EdgeRuleId, visualEdge,
                ProjectionSourceKind.SemanticRelationship),
            nodeA.Id,
            nodeB.Id);
        var graph = new ProjectedGraph(
            DocumentId,
            Revision,
            reverseCallerOrder ? [nodeB, nodeA] : [nodeA, nodeB],
            [edge]);
        var nodeGeometries = new[]
        {
            new LayoutNodeGeometry(
                nodeA.Id,
                new RectD(10d, 20d, 100d, 50d),
                Matrix2D.CreateTranslation(10d, 20d)),
            new LayoutNodeGeometry(
                nodeB.Id,
                new RectD(210d, 20d, 100d, 50d),
                Matrix2D.CreateTranslation(210d, 20d)),
        };
        if (reverseCallerOrder)
        {
            Array.Reverse(nodeGeometries);
        }

        var layout = new LayoutResult(
            DocumentId,
            Revision,
            LayoutAlgorithmId,
            new LayoutComputation(nodeGeometries));
        var routing = new RoutingResult(
            DocumentId,
            Revision,
            LayoutAlgorithmId,
            RoutingAlgorithmId,
            new RoutingComputation(
            [
                new RoutedConnectorGeometry(
                    edge.Id,
                    new PointD(110d, 45d),
                    new PointD(210d, 45d),
                    [new PointD(160d, 45d)]),
            ]));
        var visualStates = new[]
        {
            new VisualStateSnapshot(
                visualA,
                semanticA,
                new PointD(10d, 20d),
                new SizeD(100d, 50d),
                VisualPlacementMode.Manual,
                properties:
                [
                    new KeyValuePair<string, PropertyValue>(
                        "test:color",
                        PropertyValue.FromText("blue")),
                ]),
            new VisualStateSnapshot(
                visualB,
                semanticB,
                new PointD(210d, 20d),
                new SizeD(100d, 50d),
                VisualPlacementMode.Pinned),
            new VisualStateSnapshot(
                visualEdge,
                new SemanticElementId("test:semantic:ab"),
                default,
                default,
                VisualPlacementMode.Automatic),
        };
        if (reverseCallerOrder)
        {
            Array.Reverse(visualStates);
        }

        return new Canvas2DSceneTestData(
            graph,
            layout,
            routing,
            new VisualModelSnapshot(DocumentId, Revision, visualStates),
            EditorStateSnapshot.Empty);
    }

    internal static Canvas2DSceneTestData CreateWithPersistentRoute()
    {
        var source = Create();
        var existing = Assert.Single(source.Graph.Edges);
        var route = Assert.Single(source.Routing.Routes).Path;
        var edge = new ProjectedEdge(
            existing.Source,
            existing.SourceNodeId,
            existing.TargetNodeId,
            existing.SourcePortId,
            existing.TargetPortId,
            route,
            existing.SemanticProperties,
            existing.ProjectedProperties,
            existing.LayoutHints,
            existing.RoutingHints,
            existing.AlgorithmMetadata);
        var graph = new ProjectedGraph(
            source.Graph.DocumentId,
            source.Graph.SourceRevision,
            source.Graph.Nodes,
            [edge],
            source.Graph.Groups,
            source.Graph.Ports,
            source.Graph.Labels);
        var visualModel = new VisualModelSnapshot(
            source.VisualModel.DocumentId,
            source.VisualModel.Revision,
            source.VisualModel.VisualStates.Select(visual =>
                visual.Id == edge.Source.VisualStateId
                    ? new VisualStateSnapshot(
                        visual.Id,
                        visual.SemanticElementId,
                        visual.Position,
                        visual.Size,
                        visual.PlacementMode,
                        route,
                        visual.Properties)
                    : visual));
        return new Canvas2DSceneTestData(
            graph,
            source.Layout,
            source.Routing,
            visualModel,
            source.EditorState);
    }

    internal static Canvas2DSceneTestData CreateWithConnectorAnchors(
        bool referenceSourceAnchor = false)
    {
        var source = Create();
        var nodeVisualId = source.Graph.Nodes[0].Source.VisualStateId!;
        var edgeVisualId = Assert.Single(source.Graph.Edges).Source.VisualStateId!;
        var sourceAnchorId = new ConnectorAnchorId("test:anchor:a:source");
        var anchors = new[]
        {
            new ConnectorAnchor(
                sourceAnchorId,
                ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Source,
                0),
            new ConnectorAnchor(
                new ConnectorAnchorId("test:anchor:a:target"),
                ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Target,
                1),
        };
        var ownerNode = source.Graph.Nodes[0];
        var ports = anchors.Select(anchor => new ProjectedPort(
            Source(
                $"connector-anchor:{anchor.Id.Value}",
                ownerNode.Source.SemanticElementId,
                NodeRuleId,
                nodeVisualId),
            ownerNode.Id,
            routingHints: ProjectedConnectorAnchorMetadata.Encode(
                new ProjectedConnectorAnchor(
                    anchor.Id,
                    anchor.Side,
                    anchor.Role == ConnectorAnchorRole.Source
                        ? ConnectorAnchorRoleCapability.Source
                        : ConnectorAnchorRoleCapability.Target,
                    anchor.Order,
                    anchors.Count(candidate => candidate.Side == anchor.Side),
                    ResolvedConnectorAnchorKind.Dynamic))));
        var graph = new ProjectedGraph(
            source.Graph.DocumentId,
            source.Graph.SourceRevision,
            source.Graph.Nodes,
            source.Graph.Edges,
            source.Graph.Groups,
            source.Graph.Ports.Concat(ports),
            source.Graph.Labels);
        var visualModel = new VisualModelSnapshot(
            source.VisualModel.DocumentId,
            source.VisualModel.Revision,
            source.VisualModel.VisualStates.Select(visual => new VisualStateSnapshot(
                visual.Id,
                visual.SemanticElementId,
                visual.Position,
                visual.Size,
                visual.PlacementMode,
                visual.Route,
                visual.Properties,
                visual.Id == nodeVisualId ? anchors : visual.ConnectorAnchors,
                visual.Id == edgeVisualId && referenceSourceAnchor
                    ? sourceAnchorId
                    : visual.SourceAnchorId,
                visual.TargetAnchorId)));
        return new Canvas2DSceneTestData(
            graph,
            source.Layout,
            source.Routing,
            visualModel,
            source.EditorState);
    }

    internal static Canvas2DSceneTestData CreateWithPredefinedConnectorAnchor(
        ConnectorAnchorRoleCapability roleCapability =
            ConnectorAnchorRoleCapability.SourceOrTarget)
    {
        var source = Create();
        var ownerNode = source.Graph.Nodes[0];
        var ownerVisualStateId = ownerNode.Source.VisualStateId!;
        var anchorId = ConnectorAnchorReferenceIdentity.ForPredefined(
            ownerVisualStateId,
            new PredefinedConnectorAnchorDefinitionId("test:predefined-anchor:a"));
        var port = new ProjectedPort(
            Source(
                $"connector-anchor:{anchorId.Value}",
                ownerNode.Source.SemanticElementId,
                NodeRuleId,
                ownerVisualStateId),
            ownerNode.Id,
            routingHints: ProjectedConnectorAnchorMetadata.Encode(
                new ProjectedConnectorAnchor(
                    anchorId,
                    ConnectorAnchorSide.Right,
                    roleCapability,
                    order: 0,
                    sideCount: 1,
                    ResolvedConnectorAnchorKind.Predefined)));
        var graph = new ProjectedGraph(
            source.Graph.DocumentId,
            source.Graph.SourceRevision,
            source.Graph.Nodes,
            source.Graph.Edges,
            source.Graph.Groups,
            source.Graph.Ports.Append(port),
            source.Graph.Labels);
        return new Canvas2DSceneTestData(
            graph,
            source.Layout,
            source.Routing,
            source.VisualModel,
            source.EditorState);
    }

    internal static Canvas2DSceneTestData CreateWithNodeLabels()
    {
        var source = Create();
        var labels = source.Graph.Nodes.Select(node => new ProjectedLabel(
            node.Source,
            node.Id,
            node.Source.SemanticElementId.Value)).ToArray();
        var graph = new ProjectedGraph(
            source.Graph.DocumentId,
            source.Graph.SourceRevision,
            source.Graph.Nodes,
            source.Graph.Edges,
            source.Graph.Groups,
            source.Graph.Ports,
            labels);
        return new Canvas2DSceneTestData(
            graph,
            source.Layout,
            source.Routing,
            source.VisualModel,
            source.EditorState);
    }

    internal static Canvas2DSceneTestData CreateWithGroup()
    {
        var source = Create();
        var semanticGroup = new SemanticElementId("test:semantic:group");
        var visualGroup = new VisualStateId("test:visual:group");
        var group = new ProjectedGroup(
            Source("group", semanticGroup, NodeRuleId, visualGroup),
            source.Graph.Nodes.Select(static node => node.Id));
        var label = new ProjectedLabel(group.Source, group.Id, "Neutral group");
        var graph = new ProjectedGraph(
            source.Graph.DocumentId,
            source.Graph.SourceRevision,
            source.Graph.Nodes,
            source.Graph.Edges,
            [group],
            source.Graph.Ports,
            [label]);
        var layout = new LayoutResult(
            source.Layout.DocumentId,
            source.Layout.SourceRevision,
            source.Layout.AlgorithmId,
            new LayoutComputation(
                source.Layout.Nodes,
                [new LayoutGroupGeometry(group.Id, new RectD(0d, 0d, 330d, 100d))]),
            source.Layout.Diagnostics);
        var visualModel = new VisualModelSnapshot(
            source.VisualModel.DocumentId,
            source.VisualModel.Revision,
            source.VisualModel.VisualStates.Append(new VisualStateSnapshot(
                visualGroup,
                semanticGroup,
                default,
                new SizeD(330d, 100d),
                VisualPlacementMode.Manual)));
        return new Canvas2DSceneTestData(
            graph,
            layout,
            source.Routing,
            visualModel,
            source.EditorState);
    }

    internal static LayoutResult CopyLayout(
        LayoutResult source,
        DocumentId? documentId = null,
        DocumentRevision? revision = null) =>
        new(
            documentId ?? source.DocumentId,
            revision ?? source.SourceRevision,
            source.AlgorithmId,
            source.Computation,
            source.Diagnostics);

    internal static RoutingResult CopyRouting(
        RoutingResult source,
        DocumentId? documentId = null,
        DocumentRevision? revision = null,
        AlgorithmId? layoutAlgorithmId = null) =>
        new(
            documentId ?? source.DocumentId,
            revision ?? source.SourceRevision,
            layoutAlgorithmId ?? source.LayoutAlgorithmId,
            source.RoutingAlgorithmId,
            source.Computation,
            source.Diagnostics);

    private static ProjectionSourceTrace Source(
        string localKey,
        SemanticElementId semanticId,
        ProjectionRuleId ruleId,
        VisualStateId? visualId,
        ProjectionSourceKind kind = ProjectionSourceKind.SemanticElement) =>
        new(
            DocumentId,
            ruleId,
            kind,
            semanticId,
            new SemanticTypeId(kind == ProjectionSourceKind.SemanticElement
                ? "test:node"
                : "test:edge"),
            localKey,
            visualId);
}
