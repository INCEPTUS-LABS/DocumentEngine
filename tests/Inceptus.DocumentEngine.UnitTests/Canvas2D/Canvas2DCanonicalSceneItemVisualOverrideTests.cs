using System.Reflection;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DCanonicalSceneItemVisualOverrideTests
{
    [Fact]
    public void EllipseOverridePreservesCanonicalIdentityTraceInteractionAndAdditiveItems()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var node = inputs.Graph.Nodes[0];
        var targetId = Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node");
        var baseline = Scene(new Canvas2DSceneBuilder(), inputs);
        var baselineItem = baseline.Items.Single(item => item.Id == targetId);
        var replacementStyle = new Canvas2DSceneStyle("#f8fafc", "#334155", 3d);
        var contributor = new DelegateContributor(context =>
        {
            var layout = context.LayoutResult.Nodes.Single(item =>
                item.ProjectedObjectId == node.Id);
            var visualOverride = new Canvas2DCanonicalSceneItemVisualOverride(
                targetId,
                Canvas2DSceneGeometry.Ellipse(new RectD(
                    0d,
                    0d,
                    layout.Bounds.Width,
                    layout.Bounds.Height)),
                replacementStyle);
            return new Canvas2DSceneContribution(
                items: [CreateAdditiveItem(context.Contributor.ContributorId)],
                canonicalItemVisualOverrides: [visualOverride]);
        });

        var scene = Scene(
            new Canvas2DSceneBuilder(contributors: [Registration("test:ellipse", contributor)]),
            inputs);
        var item = Assert.Single(scene.Items, candidate => candidate.Id == targetId);

        Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, item.Geometry.Kind);
        Assert.Same(replacementStyle, item.Style);
        Assert.Equal(baselineItem.HitTestPolicy, item.HitTestPolicy);
        Assert.Equal(baselineItem.Id, item.Id);
        Assert.Equal(baselineItem.Layer, item.Layer);
        Assert.Equal(baselineItem.ZIndex, item.ZIndex);
        Assert.Equal(baselineItem.Transform, item.Transform);
        Assert.Equal(baselineItem.Bounds, item.Bounds);
        Assert.Equal(baselineItem.Clip, item.Clip);
        Assert.Equal(baselineItem.IsVisible, item.IsVisible);
        Assert.Equal(baselineItem.Origin, item.Origin);
        Assert.Equal(baselineItem.PersistentAppearance, item.PersistentAppearance);
        Assert.Equal(baselineItem.Metadata, item.Metadata);
        Assert.Equal(node.Source.SemanticElementId, item.Origin.SemanticElementId);
        Assert.Equal(node.Source.VisualStateId, item.Origin.VisualStateId);
        Assert.Equal(node.Id, item.Origin.ProjectedObjectId);
        Assert.True(item.Metadata[Canvas2DMoveGestureMetadata.MoveCapable].BooleanValue);
        Assert.True(item.Metadata[Canvas2DResizeGestureMetadata.ResizeCapable].BooleanValue);

        var logicalNodeItems = scene.Items.Where(candidate =>
            candidate.Origin.VisualStateId == node.Source.VisualStateId &&
            candidate.Layer == Canvas2DSceneLayer.Content &&
            candidate.IsVisible &&
            candidate.HitTestPolicy.Mode != Canvas2DHitTestMode.None).ToArray();
        Assert.Equal(item, Assert.Single(logicalNodeItems));
        var center = new PointD(
            item.Bounds.Left + (item.Bounds.Width / 2d),
            item.Bounds.Top + (item.Bounds.Height / 2d));
        Assert.Equal(
            targetId,
            new Canvas2DSceneHitTestService().HitTest(scene, center)?.SceneObjectId);
        Assert.Contains(scene.Items, candidate =>
            candidate.Origin.Categories.HasFlag(
                Canvas2DSceneOriginCategory.RegisteredExtension));
    }

    [Fact]
    public void ClosedPathOverrideKeepsLabelSelectionMoveResizeAndAnchorsOnCanonicalOwner()
    {
        var inputs = Canvas2DSceneTestData.CreateWithConnectorAnchors();
        var node = inputs.Graph.Nodes[0];
        var visualStateId = node.Source.VisualStateId!;
        var targetId = Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node");
        var label = new ProjectedLabel(node.Source, node.Id, "Canonical owner");
        var graph = new ProjectedGraph(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            inputs.Graph.Nodes,
            inputs.Graph.Edges,
            inputs.Graph.Groups,
            inputs.Graph.Ports,
            [label]);
        var path = Canvas2DSceneGeometry.Path(
        [
            new PointD(0d, 5d),
            new PointD(5d, 0d),
            new PointD(95d, 0d),
            new PointD(100d, 5d),
            new PointD(100d, 45d),
            new PointD(95d, 50d),
            new PointD(5d, 50d),
            new PointD(0d, 45d),
        ], true);
        var builder = new Canvas2DSceneBuilder(contributors:
        [
            Registration(
                "test:closed-path",
                OverrideContributor(targetId, path)),
        ]);
        var selectedScene = Assert.IsType<Canvas2DScene>(builder.Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(selection: [visualStateId])).Scene);
        var target = selectedScene.Items.Single(item => item.Id == targetId);
        var labelItem = selectedScene.Items.Single(item =>
            item.Origin.ProjectedObjectId == label.Id);
        var selectionOverlay = Assert.Single(selectedScene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "selection:",
                StringComparison.Ordinal) == true);

        Assert.Equal(path, target.Geometry);
        Assert.Equal(target.Geometry, selectionOverlay.Geometry);
        Assert.Equal(target.Transform, selectionOverlay.Transform);
        Assert.Equal(target.Bounds, selectionOverlay.Bounds);
        Assert.Contains(targetId, selectionOverlay.Origin.RelatedSceneObjectIds);
        Assert.Equal(visualStateId, labelItem.Origin.VisualStateId);
        Assert.Same(target, ResolveCanonicalSceneTarget(selectedScene, visualStateId));

        var resizeRegions = selectedScene.Items.Where(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "resize-",
                StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(8, resizeRegions.Length);
        Assert.All(resizeRegions, item =>
            Assert.Contains(targetId, item.Origin.RelatedSceneObjectIds));
        var anchorHandles = selectedScene.Items.Where(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "connector-anchor-handle:",
                StringComparison.Ordinal) == true &&
            item.Origin.VisualStateId == visualStateId).ToArray();
        Assert.NotEmpty(anchorHandles);
        Assert.All(anchorHandles, item =>
            Assert.Contains(targetId, item.Origin.RelatedSceneObjectIds));

        var moveGesture = new EditorGestureSnapshot(
            "test:move-override",
            Canvas2DMoveGestureMetadata.Kind,
            new PointD(20d, 30d),
            new PointD(40d, 45d),
            GestureTargetProperties(targetId, visualStateId));
        var moveScene = Assert.IsType<Canvas2DScene>(builder.Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(activeGesture: moveGesture)).Scene);
        var movePreview = Assert.Single(moveScene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "move-preview:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(targetId));
        Assert.Equal(path, movePreview.Geometry);

        var resizeGesture = new EditorGestureSnapshot(
            "test:resize-override",
            Canvas2DResizeGestureMetadata.Kind,
            new PointD(110d, 70d),
            new PointD(135d, 90d),
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(targetId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(visualStateId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DResizeGestureMetadata.HandleRole,
                    PropertyValue.FromText(Canvas2DResizeGestureMetadata.SouthEastRole)),
            ]);
        var resizeScene = Assert.IsType<Canvas2DScene>(builder.Build(
            graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(
                selection: [visualStateId],
                activeGesture: resizeGesture)).Scene);
        var resizePreview = Assert.Single(resizeScene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "resize-preview:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(targetId));
        Assert.Equal(Canvas2DSceneGeometryKind.Path, resizePreview.Geometry.Kind);
        Assert.True(resizePreview.Geometry.IsClosed);
    }

    [Fact]
    public void ConflictingOverridesAreRejectedInContributorIdentityOrder()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var targetId = Canvas2DSceneObjectIdentity.ForProjected(
            inputs.Graph.Nodes[0].Id,
            "node");
        var geometry = Canvas2DSceneGeometry.Ellipse(new RectD(0d, 0d, 100d, 50d));
        var builder = new Canvas2DSceneBuilder(contributors:
        [
            Registration("test:z", OverrideContributor(targetId, geometry)),
            Registration("test:a", OverrideContributor(targetId, geometry)),
        ]);

        var result = Build(builder, inputs);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        var diagnostic = Assert.Single(result.Diagnostics, item =>
            item.Code ==
                Canvas2DSceneDiagnosticCodes.ConflictingCanonicalItemVisualOverride);
        Assert.Equal("test:a", diagnostic.Context["FirstContributorId"]);
        Assert.Equal("test:z", diagnostic.Context["ConflictingContributorId"]);
    }

    [Fact]
    public void MissingCanonicalOverrideTargetIsRejected()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var missingId = new SceneObjectId("scene:test:missing");
        var builder = new Canvas2DSceneBuilder(contributors:
        [
            Registration(
                "test:missing",
                OverrideContributor(
                    missingId,
                    Canvas2DSceneGeometry.Ellipse(new RectD(0d, 0d, 1d, 1d)))),
        ]);

        var result = Build(builder, inputs);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == Canvas2DSceneDiagnosticCodes.InvalidCanonicalItemVisualOverride &&
            item.SourceIdentity == missingId.Value);
    }

    [Fact]
    public void IncompatibleGeometryCategoryIsRejected()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var targetId = Canvas2DSceneObjectIdentity.ForProjected(
            inputs.Graph.Nodes[0].Id,
            "node");
        var builder = new Canvas2DSceneBuilder(contributors:
        [
            Registration(
                "test:open-path",
                OverrideContributor(
                    targetId,
                    Canvas2DSceneGeometry.Path(
                        [new PointD(0d, 0d), new PointD(100d, 50d)]))),
        ]);

        var result = Build(builder, inputs);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == Canvas2DSceneDiagnosticCodes.InvalidCanonicalItemVisualOverride &&
            item.Message.Contains("incompatible", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplacementGeometryMustRemainInsideCanonicalBounds()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var targetId = Canvas2DSceneObjectIdentity.ForProjected(
            inputs.Graph.Nodes[0].Id,
            "node");
        var builder = new Canvas2DSceneBuilder(contributors:
        [
            Registration(
                "test:outside-bounds",
                OverrideContributor(
                    targetId,
                    Canvas2DSceneGeometry.Ellipse(new RectD(0d, 0d, 200d, 100d)))),
        ]);

        var result = Build(builder, inputs);

        Assert.False(result.Succeeded);
        Assert.Null(result.Scene);
        Assert.Contains(result.Diagnostics, item =>
            item.Code == Canvas2DSceneDiagnosticCodes.InvalidGeometry &&
            item.Message.Contains("transformed geometry", StringComparison.Ordinal));
    }

    private static Canvas2DSceneBuildResult Build(
        Canvas2DSceneBuilder builder,
        Canvas2DSceneTestData inputs) =>
        builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

    private static Canvas2DScene Scene(
        Canvas2DSceneBuilder builder,
        Canvas2DSceneTestData inputs) =>
        Assert.IsType<Canvas2DScene>(Build(builder, inputs).Scene);

    private static Canvas2DSceneContributorRegistration Registration(
        string id,
        ICanvas2DSceneContributor contributor) =>
        new(
            new Canvas2DSceneContributorDescriptor(
                new Canvas2DSceneContributorId(id),
                "1"),
            contributor);

    private static DelegateContributor OverrideContributor(
        SceneObjectId targetId,
        Canvas2DSceneGeometry geometry) =>
        new(_ => new Canvas2DSceneContribution(
            canonicalItemVisualOverrides:
            [
                new Canvas2DCanonicalSceneItemVisualOverride(
                    targetId,
                    geometry,
                    new Canvas2DSceneStyle("#ffffff", "#000000")),
            ]));

    private static Canvas2DSceneItem CreateAdditiveItem(
        Canvas2DSceneContributorId contributorId)
    {
        const string Key = "additive";
        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForExtension(contributorId, Key),
            Canvas2DSceneLayer.Decoration,
            10,
            Canvas2DSceneGeometry.Ellipse(new RectD(400d, 20d, 10d, 10d)),
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.RegisteredExtension,
                stableSourceKey: Key),
            style: new Canvas2DSceneStyle("#ffffff", "#000000"),
            hitTestPolicy: new Canvas2DHitTestPolicy(Canvas2DHitTestMode.FillOrStroke));
    }

    private static KeyValuePair<string, PropertyValue>[] GestureTargetProperties(
        SceneObjectId targetId,
        VisualStateId visualStateId) =>
    [
        new KeyValuePair<string, PropertyValue>(
            Canvas2DMoveGestureMetadata.TargetSceneObjectId,
            PropertyValue.FromText(targetId.Value)),
        new KeyValuePair<string, PropertyValue>(
            Canvas2DMoveGestureMetadata.TargetVisualStateId,
            PropertyValue.FromText(visualStateId.Value)),
    ];

    private static Canvas2DSceneItem ResolveCanonicalSceneTarget(
        Canvas2DScene scene,
        VisualStateId visualStateId)
    {
        var method = typeof(Canvas2DInteractionController).GetMethod(
            "ResolveCanonicalSceneTarget",
            BindingFlags.Static | BindingFlags.NonPublic,
            [
                typeof(Canvas2DScene),
                typeof(VisualStateId),
                typeof(bool),
            ]);
        Assert.NotNull(method);
        return Assert.IsType<Canvas2DSceneItem>(method.Invoke(
            null,
            [scene, visualStateId, false]));
    }

    private sealed class DelegateContributor(
        Func<Canvas2DSceneContributionContext, Canvas2DSceneContribution> contribute) :
        ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(
            Canvas2DSceneContributionContext context) =>
            Canvas2DSceneContributionResult.Success(contribute(context));
    }
}
