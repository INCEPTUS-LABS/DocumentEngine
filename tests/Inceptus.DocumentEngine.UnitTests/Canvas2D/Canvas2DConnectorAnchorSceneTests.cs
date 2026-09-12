using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DConnectorAnchorSceneTests
{
    [Fact]
    internal void SelectedNodeShowsStableRoleAwareAnchorsAtCanonicalDistributedPoints()
    {
        var source = WithAnchors(
            new ConnectorAnchor(new ConnectorAnchorId("anchor:right:source"),
                ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
            new ConnectorAnchor(new ConnectorAnchorId("anchor:right:target"),
                ConnectorAnchorSide.Right, ConnectorAnchorRole.Target, 1),
            new ConnectorAnchor(new ConnectorAnchorId("anchor:bottom:source"),
                ConnectorAnchorSide.Bottom, ConnectorAnchorRole.Source, 0));
        var selected = SourceVisual(source);
        var state = new EditorStateSnapshot(selection: [selected.Id]);
        var builder = new Canvas2DSceneBuilder();

        var first = Scene(builder, source, state);
        var repeated = Scene(builder, source, state);
        var handles = AnchorHandles(first);

        Assert.Equal(3, handles.Length);
        Assert.Equal(
            handles.Select(static handle => handle.Id).OrderBy(static id => id.Value),
            AnchorHandles(repeated).Select(static handle => handle.Id).OrderBy(static id => id.Value));
        AssertPoint(
            new PointD(110d, 20d + (50d / 3d)),
            Center(Handle(handles, "anchor:right:source")));
        AssertPoint(
            new PointD(110d, 20d + ((50d * 2d) / 3d)),
            Center(Handle(handles, "anchor:right:target")));
        Assert.Equal(
            new PointD(60d, 70d),
            Center(Handle(handles, "anchor:bottom:source")));
        Assert.Equal("#ffffff", Handle(handles, "anchor:right:source").Style.Fill);
        Assert.Equal("#2563eb", Handle(handles, "anchor:right:target").Style.Fill);
        Assert.All(handles, handle =>
        {
            Assert.Equal(Canvas2DSceneLayer.Overlay, handle.Layer);
            Assert.Equal(Canvas2DConnectorAnchorMetadata.HandleZIndex, handle.ZIndex);
            Assert.Equal(selected.Id, handle.Origin.VisualStateId);
            Assert.Equal(Canvas2DHitTestMode.FillOrStroke, handle.HitTestPolicy.Mode);
        });
    }

    [Fact]
    internal void SelectedNodeShowsPredefinedAnchorsWithoutPersistentCopiesOrDeleteCapability()
    {
        var p1 = PredefinedId("definition:p1");
        var p2 = PredefinedId("definition:p2");
        var p3 = PredefinedId("definition:p3");
        var source = WithProjectedAnchors(
            Canvas2DSceneTestData.Create(),
            new ProjectedConnectorAnchor(
                p1,
                ConnectorAnchorSide.Right,
                ConnectorAnchorRoleCapability.Source,
                order: 0,
                sideCount: 3,
                ResolvedConnectorAnchorKind.Predefined),
            new ProjectedConnectorAnchor(
                p2,
                ConnectorAnchorSide.Right,
                ConnectorAnchorRoleCapability.SourceOrTarget,
                order: 1,
                sideCount: 3,
                ResolvedConnectorAnchorKind.Predefined),
            new ProjectedConnectorAnchor(
                p3,
                ConnectorAnchorSide.Right,
                ConnectorAnchorRoleCapability.Target,
                order: 2,
                sideCount: 3,
                ResolvedConnectorAnchorKind.Predefined));
        var selected = SourceVisual(source);
        var builder = new Canvas2DSceneBuilder();

        var scene = Scene(builder, source, new EditorStateSnapshot(selection: [selected.Id]));
        var repeated = Scene(builder, source, new EditorStateSnapshot(selection: [selected.Id]));
        var handles = AnchorHandles(scene);

        Assert.Empty(selected.ConnectorAnchors);
        Assert.Equal(3, handles.Length);
        AssertPoint(new PointD(110d, 32.5d), Center(Handle(handles, p1.Value)));
        AssertPoint(new PointD(110d, 45d), Center(Handle(handles, p2.Value)));
        AssertPoint(new PointD(110d, 57.5d), Center(Handle(handles, p3.Value)));
        Assert.Equal(
            handles.Select(static handle => handle.Id).OrderBy(static id => id.Value),
            AnchorHandles(repeated).Select(static handle => handle.Id).OrderBy(static id => id.Value));
        Assert.All(handles, handle =>
        {
            Assert.Equal(
                (long)ResolvedConnectorAnchorKind.Predefined,
                handle.Metadata[Canvas2DConnectorAnchorMetadata.AnchorKind].IntegerValue);
            Assert.False(
                handle.Metadata[Canvas2DConnectorAnchorMetadata.DeleteCapable].BooleanValue);
            Assert.False(handle.Metadata.ContainsKey(Canvas2DConnectorAnchorMetadata.Role));
        });
        Assert.Equal(
            (long)ConnectorAnchorRoleCapability.SourceOrTarget,
            Handle(handles, p2.Value)
                .Metadata[Canvas2DConnectorAnchorMetadata.RoleCapability].IntegerValue);
        Assert.Empty(AnchorHandles(Scene(builder, source, EditorStateSnapshot.Empty)));
    }

    [Fact]
    internal void AnchorsAreSelectedOnlyAndOutrankResizeEdgeZones()
    {
        var source = WithAnchors(new ConnectorAnchor(
            new ConnectorAnchorId("anchor:right"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0));
        var selected = SourceVisual(source);
        var builder = new Canvas2DSceneBuilder();
        var unselected = Scene(builder, source, EditorStateSnapshot.Empty);
        var scene = Scene(builder, source, new EditorStateSnapshot(selection: [selected.Id]));
        var handle = Assert.Single(AnchorHandles(scene));
        var edgeZone = Assert.Single(scene.Items.Where(item =>
            item.Metadata.TryGetValue(Canvas2DResizeGestureMetadata.HandleRole, out var value) &&
            value.Kind == PropertyValueKind.Text &&
            StringComparer.Ordinal.Equals(value.TextValue, Canvas2DResizeGestureMetadata.EastRole)));

        Assert.Empty(AnchorHandles(unselected));
        Assert.True(handle.ZIndex > edgeZone.ZIndex);
        Assert.Equal(
            handle.Id,
            new Canvas2DSceneHitTestService().HitTest(scene, Center(handle))?.SceneObjectId);
    }

    [Fact]
    internal void ReferencedAnchorIsMarkedNonDeletableWithoutChangingItsGeometry()
    {
        var anchor = new ConnectorAnchor(
            new ConnectorAnchorId("anchor:used"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        var source = WithAnchors(anchor);
        var visualStates = source.VisualModel.VisualStates.Select(visual =>
            visual.SemanticElementId.Value == "test:semantic:ab"
                ? Copy(visual, sourceAnchorId: anchor.Id)
                : visual);
        source = source.WithVisualModel(new VisualModelSnapshot(
            source.VisualModel.DocumentId,
            source.VisualModel.Revision,
            visualStates));
        var scene = Scene(
            new Canvas2DSceneBuilder(),
            source,
            new EditorStateSnapshot(selection: [SourceVisual(source).Id]));
        var handle = Assert.Single(AnchorHandles(scene));

        Assert.False(handle.Metadata[Canvas2DConnectorAnchorMetadata.DeleteCapable].BooleanValue);
        Assert.Equal(new PointD(110d, 45d), Center(handle));
    }

    [Fact]
    internal void ResizePreviewReResolvesAnchorFromTransientDisplayedBounds()
    {
        var source = WithAnchors(new ConnectorAnchor(
            new ConnectorAnchorId("anchor:right"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0));
        var selected = SourceVisual(source);
        var targetSceneId = Canvas2DSceneObjectIdentity.ForProjected(
            source.Graph.Nodes.Single(node => node.Source.VisualStateId == selected.Id).Id,
            "node");
        var gesture = new EditorGestureSnapshot(
            "resize:test",
            Canvas2DResizeGestureMetadata.Kind,
            new PointD(110d, 45d),
            new PointD(160d, 45d),
            [
                new(Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(targetSceneId.Value)),
                new(Canvas2DResizeGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(selected.Id.Value)),
                new(Canvas2DResizeGestureMetadata.HandleRole,
                    PropertyValue.FromText(Canvas2DResizeGestureMetadata.EastRole)),
            ]);
        var state = new EditorStateSnapshot(selection: [selected.Id], activeGesture: gesture);

        var handle = Assert.Single(AnchorHandles(Scene(
            new Canvas2DSceneBuilder(), source, state)));

        Assert.Equal(new PointD(160d, 45d), Center(handle));
    }

    [Fact]
    internal void MovePreviewTranslatesAnchorWithTransientDisplayedBounds()
    {
        var source = WithAnchors(new ConnectorAnchor(
            new ConnectorAnchorId("anchor:right"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0));
        var selected = SourceVisual(source);
        var gesture = new EditorGestureSnapshot(
            "move:test",
            Canvas2DMoveGestureMetadata.Kind,
            new PointD(10d, 20d),
            new PointD(60d, 50d),
            [
                new(Canvas2DMoveGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(selected.Id.Value)),
            ]);
        var state = new EditorStateSnapshot(selection: [selected.Id], activeGesture: gesture);

        var handle = Assert.Single(AnchorHandles(Scene(
            new Canvas2DSceneBuilder(), source, state)));

        Assert.Equal(new PointD(160d, 75d), Center(handle));
    }

    [Fact]
    internal void PredefinedAnchorReResolvesFromMoveAndResizePreviewBounds()
    {
        var source = WithProjectedAnchors(
            Canvas2DSceneTestData.Create(),
            new ProjectedConnectorAnchor(
                PredefinedId("definition:right"),
                ConnectorAnchorSide.Right,
                ConnectorAnchorRoleCapability.SourceOrTarget,
                order: 0,
                sideCount: 1,
                ResolvedConnectorAnchorKind.Predefined));
        var selected = SourceVisual(source);
        var targetSceneId = Canvas2DSceneObjectIdentity.ForProjected(
            source.Graph.Nodes.Single(node => node.Source.VisualStateId == selected.Id).Id,
            "node");
        var move = new EditorGestureSnapshot(
            "move:predefined",
            Canvas2DMoveGestureMetadata.Kind,
            new PointD(10d, 20d),
            new PointD(60d, 50d),
            [
                new(Canvas2DMoveGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(selected.Id.Value)),
            ]);
        var resize = new EditorGestureSnapshot(
            "resize:predefined",
            Canvas2DResizeGestureMetadata.Kind,
            new PointD(110d, 45d),
            new PointD(160d, 45d),
            [
                new(Canvas2DResizeGestureMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(targetSceneId.Value)),
                new(Canvas2DResizeGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(selected.Id.Value)),
                new(Canvas2DResizeGestureMetadata.HandleRole,
                    PropertyValue.FromText(Canvas2DResizeGestureMetadata.EastRole)),
            ]);

        var moved = Assert.Single(AnchorHandles(Scene(
            new Canvas2DSceneBuilder(),
            source,
            new EditorStateSnapshot(selection: [selected.Id], activeGesture: move))));
        var resized = Assert.Single(AnchorHandles(Scene(
            new Canvas2DSceneBuilder(),
            source,
            new EditorStateSnapshot(selection: [selected.Id], activeGesture: resize))));

        Assert.Equal(new PointD(160d, 75d), Center(moved));
        Assert.Equal(new PointD(160d, 45d), Center(resized));
        Assert.Empty(selected.ConnectorAnchors);
    }

    [Theory]
    [InlineData(0.75d)]
    [InlineData(1d)]
    [InlineData(1.5d)]
    internal void ViewportZoomDoesNotChangeLogicalAnchorGeometry(double zoom)
    {
        var source = WithAnchors(new ConnectorAnchor(
            new ConnectorAnchorId("anchor:right"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0));
        var selected = SourceVisual(source);
        var baseline = Assert.Single(AnchorHandles(Scene(
            new Canvas2DSceneBuilder(),
            source,
            new EditorStateSnapshot(selection: [selected.Id]))));
        var zoomed = Assert.Single(AnchorHandles(Scene(
            new Canvas2DSceneBuilder(),
            source,
            new EditorStateSnapshot(
                selection: [selected.Id],
                viewport: new ViewportSnapshot(zoom, new VectorD(19d, -7d))))));

        Assert.Equal(baseline.Geometry, zoomed.Geometry);
        Assert.Equal(baseline.Bounds, zoomed.Bounds);
        Assert.Equal(baseline.Transform, zoomed.Transform);
    }

    private static Canvas2DSceneTestData WithAnchors(params ConnectorAnchor[] anchors)
    {
        var source = Canvas2DSceneTestData.Create();
        var selected = SourceVisual(source);
        var sideCounts = anchors
            .GroupBy(static anchor => anchor.Side)
            .ToDictionary(static group => group.Key, static group => group.Count());
        var projectedAnchors = anchors.Select(anchor => new ProjectedConnectorAnchor(
            anchor.Id,
            anchor.Side,
            anchor.Role == ConnectorAnchorRole.Source
                ? ConnectorAnchorRoleCapability.Source
                : ConnectorAnchorRoleCapability.Target,
            anchor.Order,
            sideCounts[anchor.Side],
            ResolvedConnectorAnchorKind.Dynamic)).ToArray();
        var visualStates = source.VisualModel.VisualStates.Select(visual =>
            visual.Id == selected.Id ? Copy(visual, anchors) : visual);
        return WithProjectedAnchors(
            source.WithVisualModel(new VisualModelSnapshot(
                source.VisualModel.DocumentId,
                source.VisualModel.Revision,
                visualStates)),
            projectedAnchors);
    }

    private static ConnectorAnchorId PredefinedId(string definition) =>
        ConnectorAnchorReferenceIdentity.ForPredefined(
            new VisualStateId("test:scene-owner"),
            new PredefinedConnectorAnchorDefinitionId(definition));

    private static Canvas2DSceneTestData WithProjectedAnchors(
        Canvas2DSceneTestData source,
        params ProjectedConnectorAnchor[] anchors)
    {
        var selected = SourceVisual(source);
        var ownerNode = source.Graph.Nodes.Single(node =>
            node.Source.VisualStateId == selected.Id);
        var ports = anchors.Select(anchor => new ProjectedPort(
            new ProjectionSourceTrace(
                ownerNode.Source.DocumentId,
                ownerNode.Source.RuleId,
                ownerNode.Source.SourceKind,
                ownerNode.Source.SemanticElementId,
                ownerNode.Source.SemanticTypeId,
                $"connector-anchor:{anchor.Id.Value}",
                selected.Id),
            ownerNode.Id,
            routingHints: ProjectedConnectorAnchorMetadata.Encode(anchor)));
        var graph = new ProjectedGraph(
            source.Graph.DocumentId,
            source.Graph.SourceRevision,
            source.Graph.Nodes,
            source.Graph.Edges,
            source.Graph.Groups,
            source.Graph.Ports.Concat(ports),
            source.Graph.Labels);
        return source.WithGraph(graph);
    }

    private static VisualStateSnapshot SourceVisual(Canvas2DSceneTestData source) =>
        source.VisualModel.VisualStates.Single(visual =>
            visual.SemanticElementId.Value == "test:semantic:a");

    private static VisualStateSnapshot Copy(
        VisualStateSnapshot visual,
        IEnumerable<ConnectorAnchor>? anchors = null,
        ConnectorAnchorId? sourceAnchorId = null) =>
        new(
            visual.Id,
            visual.SemanticElementId,
            visual.Position,
            visual.Size,
            visual.PlacementMode,
            visual.Route,
            visual.Properties,
            anchors ?? visual.ConnectorAnchors,
            sourceAnchorId ?? visual.SourceAnchorId,
            visual.TargetAnchorId);

    private static Canvas2DScene Scene(
        Canvas2DSceneBuilder builder,
        Canvas2DSceneTestData source,
        EditorStateSnapshot state) =>
        Assert.IsType<Canvas2DScene>(builder.Build(
            source.Graph,
            source.Layout,
            source.Routing,
            source.VisualModel,
            state).Scene);

    private static Canvas2DSceneItem[] AnchorHandles(Canvas2DScene scene) =>
        scene.Items.Where(item => item.Metadata.TryGetValue(
            Canvas2DConnectorAnchorMetadata.AnchorId, out _)).ToArray();

    private static Canvas2DSceneItem Handle(
        IEnumerable<Canvas2DSceneItem> handles,
        string anchorId) =>
        Assert.Single(handles, handle => StringComparer.Ordinal.Equals(
            handle.Metadata[Canvas2DConnectorAnchorMetadata.AnchorId].TextValue,
            anchorId));

    private static PointD Center(Canvas2DSceneItem item) => new(
        item.Bounds.X + (item.Bounds.Width / 2d),
        item.Bounds.Y + (item.Bounds.Height / 2d));

    private static void AssertPoint(PointD expected, PointD actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 12);
        Assert.Equal(expected.Y, actual.Y, precision: 12);
    }
}
