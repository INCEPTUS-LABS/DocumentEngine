using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DConnectorEndpointTests
{
    [Fact]
    public void SelectedStraightConnectorHasExactlyTwoStableEndpointHandlesFromRoutedGeometry()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var edge = Assert.Single(inputs.Graph.Edges);
        var visualStateId = edge.Source.VisualStateId!;
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
        var originalRoute = Assert.Single(inputs.Routing.Routes);
        var route = new RoutedConnectorGeometry(
            edge.Id,
            originalRoute.SourceAnchor,
            originalRoute.DestinationAnchor);
        var routing = new RoutingResult(
            inputs.Routing.DocumentId,
            inputs.Routing.SourceRevision,
            inputs.Routing.LayoutAlgorithmId,
            inputs.Routing.RoutingAlgorithmId,
            new RoutingComputation([route]));
        var editorState = new EditorStateSnapshot(selection: [visualStateId]);
        var builder = new Canvas2DSceneBuilder();

        var first = Assert.IsType<Canvas2DScene>(builder.Build(
            inputs.Graph,
            inputs.Layout,
            routing,
            inputs.VisualModel,
            editorState).Scene);
        var repeated = Assert.IsType<Canvas2DScene>(builder.Build(
            inputs.Graph,
            inputs.Layout,
            routing,
            inputs.VisualModel,
            editorState).Scene);
        var endpoints = Endpoints(first, connectorId);

        Assert.Equal(2, endpoints.Length);
        Assert.Empty(Bends(first, connectorId));
        Assert.Equal(
            [Canvas2DConnectorEndpointMetadata.EndEndpointRole,
             Canvas2DConnectorEndpointMetadata.StartEndpointRole],
            endpoints.Select(Role).Order(StringComparer.Ordinal));
        Assert.Equal(
            [route.SourceAnchor, route.DestinationAnchor],
            endpoints.OrderBy(item => StringComparer.Ordinal.Equals(
                    Role(item),
                    Canvas2DConnectorEndpointMetadata.StartEndpointRole)
                    ? 0
                    : 1)
                .Select(Center));
        Assert.Equal(
            endpoints.Select(static item => item.Id).OrderBy(static id => id.Value),
            Endpoints(repeated, connectorId)
                .Select(static item => item.Id)
                .OrderBy(static id => id.Value));
        Assert.All(endpoints, endpoint =>
        {
            Assert.Equal(Canvas2DSceneLayer.Overlay, endpoint.Layer);
            Assert.Equal(Canvas2DHitTestMode.FillOrStroke, endpoint.HitTestPolicy.Mode);
            Assert.Equal(visualStateId, endpoint.Origin.VisualStateId);
            Assert.Equal(edge.Id, endpoint.Origin.ProjectedObjectId);
            Assert.Contains(connectorId, endpoint.Origin.RelatedSceneObjectIds);
            Assert.Equal("#ffffff", endpoint.Style.Fill);
            Assert.Equal("#7c3aed", endpoint.Style.Stroke);
            Assert.Equal(Canvas2DConnectorEndpointMetadata.HandleExtent, endpoint.Bounds.Width);
        });
    }

    [Fact]
    public void EndpointHandlesAreSelectedOnlyAndAppearForConnectorWithinMultiSelection()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var edge = Assert.Single(inputs.Graph.Edges);
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
        var builder = new Canvas2DSceneBuilder();
        var unselected = Assert.IsType<Canvas2DScene>(builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            EditorStateSnapshot.Empty).Scene);
        var multiSelected = Assert.IsType<Canvas2DScene>(builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(selection:
            [
                inputs.Graph.Nodes[0].Source.VisualStateId!,
                edge.Source.VisualStateId!,
            ])).Scene);

        Assert.Empty(Endpoints(unselected, connectorId));
        Assert.Equal(2, Endpoints(multiSelected, connectorId).Length);
        Assert.Empty(Bends(multiSelected, connectorId));
    }

    [Fact]
    public void EndpointHandlesOutrankInternalBendsAndConnectorPath()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var edge = Assert.Single(inputs.Graph.Edges);
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            new EditorStateSnapshot(selection: [edge.Source.VisualStateId!])).Scene);
        var endpoints = Endpoints(scene, connectorId);
        var bend = Assert.Single(Bends(scene, connectorId));
        var hitTest = new Canvas2DSceneHitTestService();

        Assert.All(endpoints, endpoint =>
        {
            Assert.True(endpoint.ZIndex > bend.ZIndex);
            Assert.Equal(endpoint.Id, hitTest.HitTest(scene, Center(endpoint))?.SceneObjectId);
        });
        Assert.Equal(Canvas2DRouteGestureMetadata.HandleZIndex, bend.ZIndex);
        Assert.Equal(
            Canvas2DConnectorEndpointMetadata.StartEndpointZIndex,
            Assert.Single(endpoints, item => StringComparer.Ordinal.Equals(
                Role(item),
                Canvas2DConnectorEndpointMetadata.StartEndpointRole)).ZIndex);
        Assert.Equal(
            Canvas2DConnectorEndpointMetadata.EndEndpointZIndex,
            Assert.Single(endpoints, item => StringComparer.Ordinal.Equals(
                Role(item),
                Canvas2DConnectorEndpointMetadata.EndEndpointRole)).ZIndex);
        Assert.Equal(bend.Id, hitTest.HitTest(scene, Center(bend))?.SceneObjectId);
    }

    private static Canvas2DSceneItem[] Endpoints(
        Canvas2DScene scene,
        Inceptus.DocumentEngine.Contracts.Primitives.SceneObjectId connectorId) =>
        scene.Items.Where(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "connector-endpoint-handle:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(connectorId)).ToArray();

    private static Canvas2DSceneItem[] Bends(
        Canvas2DScene scene,
        Inceptus.DocumentEngine.Contracts.Primitives.SceneObjectId connectorId) =>
        scene.Items.Where(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "route-bend-handle:",
                StringComparison.Ordinal) == true &&
            item.Origin.RelatedSceneObjectIds.Contains(connectorId)).ToArray();

    private static string Role(Canvas2DSceneItem item) =>
        item.Metadata[Canvas2DConnectorEndpointMetadata.HandleRole].TextValue;

    private static PointD Center(Canvas2DSceneItem item) => new(
        item.Bounds.X + (item.Bounds.Width / 2d),
        item.Bounds.Y + (item.Bounds.Height / 2d));
}
