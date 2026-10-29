using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class PhaseA22ConnectorVisualTests
{
    private static readonly ConnectorVisualDescriptor MessageStyle = new([6d, 4d],
        ConnectorEndpointDecoration.OpenCircle, ConnectorEndpointDecoration.OpenArrow);

    [Fact]
    public void DescriptorRoundTripsAsDerivedMetadataAndDefaultPreservesSolidFilledArrow()
    {
        Assert.Equal(MessageStyle, ConnectorVisualMetadata.Resolve(new PropertyMap(
            ConnectorVisualMetadata.Encode(MessageStyle))));
        var standard = ConnectorVisualMetadata.Resolve(PropertyMap.Empty);
        Assert.Empty(standard.DashPattern);
        Assert.Equal(ConnectorEndpointDecoration.None, standard.SourceDecoration);
        Assert.Equal(ConnectorEndpointDecoration.FilledArrow, standard.TargetDecoration);
        var inputs = Canvas2DSceneTestData.Create();
        var scene = Build(inputs);
        Assert.Equal(4, scene.ItemCount);
        var connector = Connector(scene, inputs);
        Assert.Empty(connector.Style.DashPattern);
        var arrow = Assert.Single(scene.Items, item => item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));
        Assert.True(arrow.Geometry.IsClosed);
        Assert.Equal("#000000", arrow.Style.Fill);
    }

    [Fact]
    public void DescriptorRoundTripsEveryAcceptedDashEntryWithoutAnArbitraryLengthLimit()
    {
        var descriptor = new ConnectorVisualDescriptor(Enumerable.Range(1, 80).Select(static value => (double)value),
            ConnectorEndpointDecoration.OpenCircle, ConnectorEndpointDecoration.OpenArrow);
        var decoded = ConnectorVisualMetadata.Resolve(new PropertyMap(ConnectorVisualMetadata.Encode(descriptor)));
        Assert.Equal(descriptor, decoded);
        Assert.Equal(80, decoded.DashPattern.Length);
    }

    [Fact]
    public void CanonicalDescriptorProducesDashedPathOpenCircleAndOpenArrowWithExactConnectorIdentity()
    {
        var inputs = WithDescriptor();
        var scene = Build(inputs);
        var connector = Connector(scene, inputs);
        Assert.Equal<double>([6d, 4d], connector.Style.DashPattern);
        Assert.Equal(inputs.Routing.Routes[0].Path.AsEnumerable(), connector.Geometry.Points);
        var circle = Assert.Single(scene.Items, item => item.Layer == Canvas2DSceneLayer.Connector &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Ellipse);
        var arrow = Assert.Single(scene.Items, item => item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));
        Assert.Equal(connector.Origin, circle.Origin);
        Assert.Equal(connector.Origin, arrow.Origin);
        Assert.Equal(connector.Geometry.Points[0], Center(circle.Bounds));
        Assert.Equal("#ffffff", circle.Style.Fill);
        Assert.Equal("#ffffff", arrow.Style.Fill);
        Assert.True(arrow.Geometry.IsClosed);
        Assert.Equal(new[] { new PointD(210d, 45d), new PointD(200d, 49d), new PointD(200d, 41d) }, arrow.Geometry.Points);
        Assert.Equal(connector.Geometry.Points[^1], arrow.Geometry.Points[0]);
        Assert.Empty(circle.Style.DashPattern);
        Assert.Empty(arrow.Style.DashPattern);
    }

    [Fact]
    public void SelectionAndHoverKeepOpenDecorationsAndDashedConnector()
    {
        var inputs = WithDescriptor();
        var canonical = Build(inputs);
        var circle = canonical.Items.Single(item => item.Geometry.Kind == Canvas2DSceneGeometryKind.Ellipse);
        var scene = Build(inputs, new EditorStateSnapshot(selection: [inputs.Graph.Edges[0].Source.VisualStateId!],
            hoveredObjectId: circle.Id));
        var targetArrow = canonical.Items.Single(item => item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));
        var arrowOverlays = scene.Items.Where(item => item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.RelatedSceneObjectIds.Contains(targetArrow.Id)).ToArray();
        Assert.Single(arrowOverlays);
        Assert.All(arrowOverlays, item =>
        {
            Assert.Equal("#ffffff", item.Style.Fill);
            Assert.Equal("#2563eb", item.Style.Stroke);
            Assert.True(item.Geometry.IsClosed);
        });
        var decorationOverlays = scene.Items.Where(item => item.Layer == Canvas2DSceneLayer.Overlay &&
            (item.Geometry.Kind == Canvas2DSceneGeometryKind.Ellipse ||
             item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow))).ToArray();
        Assert.NotEmpty(decorationOverlays);
        Assert.All(decorationOverlays.Where(item => item.Geometry.Kind == Canvas2DSceneGeometryKind.Path), item =>
        {
            Assert.Equal("#ffffff", item.Style.Fill);
            Assert.True(item.Geometry.IsClosed);
        });
        Assert.Contains(scene.Items, item => item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Style.DashPattern.SequenceEqual(MessageStyle.DashPattern));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreationPreviewUsesSelectedDescriptorAndNeverMutatesPersistentInputs(bool message)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var source = inputs.Graph.Nodes[0].Source;
        var descriptor = message ? MessageStyle : ConnectorVisualDescriptor.Default;
        var gesture = new EditorGestureSnapshot("a22-create", Canvas2DAnchorConnectionGestureMetadata.Kind,
            new PointD(110, 45), new PointD(190, 90),
            Canvas2DAnchorConnectionGestureMetadata.CreateProperties(source.SemanticElementId, source.VisualStateId!,
                new ConnectorAnchorId("a22-source"), new AnchorConnectionCreationId("a22-create"))
                .Concat(ConnectorVisualMetadata.Encode(descriptor)));
        var scene = Build(inputs, new EditorStateSnapshot(activeGesture: gesture));
        var preview = scene.Items.Single(item => item.Origin.StableSourceKey == "anchor-connection-preview:a22-create");
        Assert.Equal(descriptor.DashPattern.AsEnumerable(), preview.Style.DashPattern);
        Assert.Equal(new[] { gesture.Origin, gesture.Current }, preview.Geometry.Points);
        var decorations = scene.Items.Where(item => item.Origin.StableSourceKey?.StartsWith(
            "anchor-connection-preview:a22-create:", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(message ? 2 : 0, decorations.Length);
        Assert.All(decorations, item => Assert.Equal(Canvas2DHitTestMode.None, item.HitTestPolicy.Mode));
        if (message) Assert.All(decorations, item => Assert.True(item.ZIndex > Canvas2DConnectorAnchorMetadata.HandleZIndex));
        if (message)
        {
            var arrow = decorations.Single(item => item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));
            Assert.True(arrow.Geometry.IsClosed);
            Assert.Equal("#ffffff", arrow.Style.Fill);
        }
        Assert.Equal(inputs.Routing.Routes[0].Path.AsEnumerable(), Connector(scene, inputs).Geometry.Points);
    }

    [Theory]
    [InlineData(ConnectorEndpointKind.Source)]
    [InlineData(ConnectorEndpointKind.Target)]
    public void ReconnectionPreviewMovesOnlyRequestedOpenDecoration(ConnectorEndpointKind endpoint)
    {
        var inputs = WithDescriptor();
        var edge = inputs.Graph.Edges[0];
        var connector = Connector(Build(inputs), inputs);
        var gesture = new EditorGestureSnapshot("a22-reconnect", Canvas2DConnectorEndpointReconnectionGestureMetadata.Kind,
            connector.Geometry.Points[endpoint == ConnectorEndpointKind.Source ? 0 : ^1], new PointD(180, 95),
            Canvas2DConnectorEndpointReconnectionGestureMetadata.CreateProperties(edge.Source.SemanticElementId,
                edge.Source.VisualStateId!, endpoint, inputs.Graph.Nodes[endpoint == ConnectorEndpointKind.Source ? 0 : 1].Source.SemanticElementId,
                new ConnectorAnchorId("a22-original"), new ConnectorEndpointReconnectionId("a22-reconnect"), connector.Id));
        var scene = Build(inputs, new EditorStateSnapshot(activeGesture: gesture));
        var key = $"connector-endpoint-reconnection-preview:{gesture.Id}:{connector.Id.Value}";
        var preview = scene.Items.Single(item => item.Origin.StableSourceKey == key);
        Assert.Equal(MessageStyle.DashPattern.AsEnumerable(), preview.Style.DashPattern);
        var decoration = Assert.Single(scene.Items, item => item.Origin.StableSourceKey?.StartsWith(key + ":", StringComparison.Ordinal) == true);
        Assert.Equal(Canvas2DHitTestMode.None, decoration.HitTestPolicy.Mode);
        Assert.True(decoration.ZIndex > Canvas2DConnectorAnchorMetadata.HandleZIndex);
        if (endpoint == ConnectorEndpointKind.Source)
        {
            Assert.Equal(gesture.Current, Center(decoration.Bounds));
            Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, decoration.Geometry.Kind);
            Assert.DoesNotContain(scene.Items, item => item.Layer == Canvas2DSceneLayer.Connector &&
                item.Geometry.Kind == Canvas2DSceneGeometryKind.Ellipse);
        }
        else
        {
            Assert.Equal("#ffffff", decoration.Style.Fill);
            Assert.True(decoration.Geometry.IsClosed);
            Assert.Equal(gesture.Current, decoration.Geometry.Points[0]);
            Assert.DoesNotContain(scene.Items, item => item.Layer == Canvas2DSceneLayer.Connector &&
                item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));
        }
        Assert.Equal(connector.Geometry.Points.AsEnumerable(), Connector(scene, inputs).Geometry.Points);
    }

    [Fact]
    public void CandidateCaptureIsDeterministicAndEmptySetIsDistinctFromLegacyUnfilteredState()
    {
        var allowed = new VisualStateId("a22:allowed");
        var other = new VisualStateId("a22:other");
        var encoded = new PropertyMap(ConnectionCandidateMetadata.EncodeAllowedVisuals([allowed, allowed]));
        Assert.True(ConnectionCandidateMetadata.AllowsVisual(encoded, allowed));
        Assert.False(ConnectionCandidateMetadata.AllowsVisual(encoded, other));
        Assert.False(ConnectionCandidateMetadata.AllowsVisual(new PropertyMap(
            ConnectionCandidateMetadata.EncodeAllowedVisuals([])), allowed));
        Assert.True(ConnectionCandidateMetadata.AllowsVisual(PropertyMap.Empty, allowed));
        Assert.Equal(encoded, new PropertyMap(ConnectionCandidateMetadata.Capture(encoded)));
        var sources = new PropertyMap(ConnectionCandidateMetadata.EncodeAllowedSources([allowed]));
        Assert.True(ConnectionCandidateMetadata.AllowsSource(sources, allowed));
        Assert.False(ConnectionCandidateMetadata.AllowsSource(sources, other));
        Assert.False(ConnectionCandidateMetadata.AllowsSource(PropertyMap.Empty, allowed));
        Assert.Empty(ConnectionCandidateMetadata.WithoutSources(sources));
    }

    private static Canvas2DSceneTestData WithDescriptor()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var edge = inputs.Graph.Edges[0];
        var changed = new ProjectedEdge(edge.Source, edge.SourceNodeId, edge.TargetNodeId,
            edge.SourcePortId, edge.TargetPortId, edge.PersistentRoute, edge.SemanticProperties,
            ConnectorVisualMetadata.Encode(MessageStyle), edge.LayoutHints, edge.RoutingHints, edge.AlgorithmMetadata);
        return inputs.WithGraph(new ProjectedGraph(inputs.Graph.DocumentId, inputs.Graph.SourceRevision,
            inputs.Graph.Nodes, [changed], inputs.Graph.Groups, inputs.Graph.Ports, inputs.Graph.Labels));
    }

    private static Canvas2DScene Build(Canvas2DSceneTestData inputs, EditorStateSnapshot? editor = null)
    {
        var result = new Canvas2DSceneBuilder().Build(inputs.Graph, inputs.Layout, inputs.Routing,
            inputs.VisualModel, editor ?? inputs.EditorState);
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return Assert.IsType<Canvas2DScene>(result.Scene);
    }

    private static Canvas2DSceneItem Connector(Canvas2DScene scene, Canvas2DSceneTestData inputs) =>
        scene.Items.Single(item => item.Id == Canvas2DSceneObjectIdentity.ForProjected(inputs.Graph.Edges[0].Id, "connector"));

    private static PointD Center(RectD bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
}
