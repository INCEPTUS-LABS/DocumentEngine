using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DConnectorArrowSceneTests
{
    [Fact]
    internal void SceneComposesStableClosedTargetArrowWithConnectorTraceAndHitOwnership()
    {
        var source = Canvas2DSceneTestData.Create();
        var edge = Assert.Single(source.Graph.Edges);
        var builder = new Canvas2DSceneBuilder();
        var first = Scene(builder, source, EditorStateSnapshot.Empty);
        var repeated = Scene(builder, source, EditorStateSnapshot.Empty);
        var arrow = Arrow(first);

        Assert.Equal(
            Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector-target-arrow"),
            arrow.Id);
        Assert.Equal(arrow.Id, Arrow(repeated).Id);
        Assert.Equal(Canvas2DSceneLayer.Connector, arrow.Layer);
        Assert.Equal(1, arrow.ZIndex);
        Assert.True(arrow.Geometry.IsClosed);
        Assert.Equal(new PointD(210d, 45d), arrow.Geometry.Points[0]);
        Assert.Equal(edge.Id, arrow.Origin.ProjectedObjectId);
        Assert.Equal(edge.Source.VisualStateId, arrow.Origin.VisualStateId);
        Assert.True(arrow.Metadata[Canvas2DConnectorArrowMetadata.TargetArrow].BooleanValue);
        Assert.Equal(Canvas2DHitTestMode.FillOrStroke, arrow.HitTestPolicy.Mode);
        Assert.Equal(
            arrow.Id,
            new Canvas2DSceneHitTestService().HitTest(first, new PointD(205d, 45d))?.SceneObjectId);
    }

    [Fact]
    internal void SelectedConnectorEndpointHandleOutranksTargetArrow()
    {
        var source = Canvas2DSceneTestData.Create();
        var edge = Assert.Single(source.Graph.Edges);
        var scene = Scene(
            new Canvas2DSceneBuilder(),
            source,
            new EditorStateSnapshot(selection: [edge.Source.VisualStateId!]));
        var arrow = Arrow(scene);
        var endHandle = Assert.Single(scene.Items.Where(item =>
            item.Metadata.TryGetValue(
                Canvas2DConnectorEndpointMetadata.HandleRole,
                out var role) &&
            role.Kind == PropertyValueKind.Text &&
            StringComparer.Ordinal.Equals(
                role.TextValue,
                Canvas2DConnectorEndpointMetadata.EndEndpointRole)));

        Assert.True(endHandle.Layer > arrow.Layer);
        Assert.Equal(
            endHandle.Id,
            new Canvas2DSceneHitTestService().HitTest(
                scene,
                arrow.Geometry.Points[0])?.SceneObjectId);
    }

    [Theory]
    [InlineData(0.75d)]
    [InlineData(1d)]
    [InlineData(1.5d)]
    internal void ViewportZoomDoesNotChangeLogicalArrowGeometry(double zoom)
    {
        var source = Canvas2DSceneTestData.Create();
        var baseline = Arrow(Scene(
            new Canvas2DSceneBuilder(), source, EditorStateSnapshot.Empty));
        var zoomedState = new EditorStateSnapshot(
            viewport: new ViewportSnapshot(zoom, new VectorD(37d, -19d)));
        var zoomed = Arrow(Scene(new Canvas2DSceneBuilder(), source, zoomedState));

        Assert.Equal(baseline.Geometry, zoomed.Geometry);
        Assert.Equal(baseline.Bounds, zoomed.Bounds);
        Assert.Equal(baseline.Transform, zoomed.Transform);
    }

    private static Canvas2DSceneItem Arrow(Canvas2DScene scene) =>
        Assert.Single(scene.Items, item =>
            item.Metadata.TryGetValue(
                Canvas2DConnectorArrowMetadata.TargetArrow,
                out var value) &&
            value.Kind == PropertyValueKind.Boolean &&
            value.BooleanValue);

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
}
