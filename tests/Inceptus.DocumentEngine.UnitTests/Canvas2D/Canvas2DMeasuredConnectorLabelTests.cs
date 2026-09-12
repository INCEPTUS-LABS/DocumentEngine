using System.Text;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DMeasuredConnectorLabelTests
{
    [Fact]
    public async Task DefaultPlacementUsesTotalRouteMidpointAndCentralOffset()
    {
        var built = await BuildAsync("Approved");
        var line = Assert.Single(LabelLines(built.Scene, built.Label.Id));
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(
            built.Edge.Id,
            "connector");

        Assert.Equal(new PointD(160d, 33d), Anchor(line));
        Assert.Equal(built.Label.Source.SemanticElementId, line.Origin.SemanticElementId);
        Assert.Equal(built.Label.Source.VisualStateId, line.Origin.VisualStateId);
        Assert.Equal(built.Label.Id, line.Origin.ProjectedObjectId);
        Assert.Contains(built.Edge.Id, line.Origin.RelatedProjectedObjectIds);
        Assert.Contains(connectorId, line.Origin.RelatedSceneObjectIds);
        Assert.True(line.Metadata[Canvas2DLabelGestureMetadata.LabelMoveCapable].BooleanValue);

        var hit = Assert.IsType<Canvas2DSceneHitTestResult>(
            new Canvas2DSceneHitTestService().HitTest(built.Scene, Anchor(line)));
        Assert.Equal(built.Edge.Source.VisualStateId, hit.Origin.VisualStateId);
    }

    [Fact]
    public async Task CompatibilityBuildUsesRouteRelativePlacementAndDragMetadata()
    {
        PointD[] unequalRoute =
        [
            new PointD(110d, 45d),
            new PointD(110d, 95d),
            new PointD(210d, 95d),
            new PointD(210d, 45d),
        ];
        var built = await BuildAsync(
            "Approved",
            routePath: unequalRoute,
            useMeasuredLayout: false);
        var line = Assert.Single(LabelLines(built.Scene, built.Label.Id));

        Assert.Equal(new PointD(160d, 83d), Anchor(line));
        Assert.True(line.Metadata[Canvas2DLabelGestureMetadata.LabelMoveCapable].BooleanValue);
        Assert.Contains(
            Canvas2DSceneObjectIdentity.ForProjected(built.Edge.Id, "connector"),
            line.Origin.RelatedSceneObjectIds);
    }

    [Fact]
    public async Task ExplicitPlacementAndOffsetAreResolvedFromVisualProperties()
    {
        var placement = new ConnectorLabelPlacement(0.25d, new VectorD(5d, -7d));
        var built = await BuildAsync("Approved", placement);

        var line = Assert.Single(LabelLines(built.Scene, built.Label.Id));

        Assert.Equal(new PointD(140d, 38d), Anchor(line));
    }

    [Fact]
    public async Task LongTextReusesMeasuredWrappingWithStableZoomInvariantLines()
    {
        const string text = "Approved customer sales order after manager verification";
        var first = await BuildAsync(
            text,
            editorState: new EditorStateSnapshot(
                viewport: new ViewportSnapshot(0.75d, default)));
        var second = await BuildAsync(
            text,
            editorState: new EditorStateSnapshot(
                viewport: new ViewportSnapshot(1.5d, new VectorD(30d, -12d))));
        var firstLines = LabelLines(first.Scene, first.Label.Id);
        var secondLines = LabelLines(second.Scene, second.Label.Id);

        Assert.True(firstLines.Length > 1);
        Assert.Equal(
            firstLines.Select(static line => line.Id),
            secondLines.Select(static line => line.Id));
        Assert.Equal(
            firstLines.Select(static line => line.Geometry.Content),
            secondLines.Select(static line => line.Geometry.Content));
        Assert.Equal(
            firstLines.Select(Anchor),
            secondLines.Select(Anchor));
        Assert.All(firstLines, line =>
            Assert.True(line.Geometry.Bounds.Width <= 160d + 1e-9));
    }

    [Fact]
    public async Task ActiveLabelGestureProducesTranslatedNonPersistentPreviewLines()
    {
        var initial = await BuildAsync("Approved");
        var visualId = initial.Edge.Source.VisualStateId!;
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(
            initial.Edge.Id,
            "connector");
        var gesture = new EditorGestureSnapshot(
            "test:connector-label-preview",
            Canvas2DLabelGestureMetadata.Kind,
            new PointD(160d, 33d),
            new PointD(185d, 43d),
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DLabelGestureMetadata.TargetConnectorSceneObjectId,
                    PropertyValue.FromText(connectorId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DLabelGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(visualId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DLabelGestureMetadata.TargetLabelProjectedObjectId,
                    PropertyValue.FromText(initial.Label.Id.Value)),
            ]);
        var preview = await BuildAsync(
            "Approved",
            editorState: new EditorStateSnapshot(
                selection: [visualId],
                activeGesture: gesture));
        var persistent = Assert.Single(LabelLines(preview.Scene, preview.Label.Id));
        var transient = Assert.Single(preview.Scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.ProjectedObjectId == preview.Label.Id &&
            item.Origin.StableSourceKey?.StartsWith(
                "connector-label-preview:",
                StringComparison.Ordinal) == true);

        Assert.Equal(new PointD(160d, 33d), Anchor(persistent));
        Assert.Equal(new PointD(185d, 43d), Anchor(transient));
        Assert.Equal(Canvas2DHitTestMode.None, transient.HitTestPolicy.Mode);
        Assert.Contains(persistent.Id, transient.Origin.RelatedSceneObjectIds);
    }

    [Fact]
    public async Task SelectedBendHandleRemainsTopmostWhenItOverlapsLabel()
    {
        var initial = await BuildAsync("Approved");
        var visualId = initial.Edge.Source.VisualStateId!;
        var selected = await BuildAsync(
            "Approved",
            new ConnectorLabelPlacement(0.5d, default),
            new EditorStateSnapshot(selection: [visualId]));

        var hit = Assert.IsType<Canvas2DSceneHitTestResult>(
            new Canvas2DSceneHitTestService().HitTest(
                selected.Scene,
                new PointD(160d, 45d)));

        Assert.StartsWith(
            "route-bend-handle:",
            hit.Origin.StableSourceKey,
            StringComparison.Ordinal);
        Assert.Equal(
            Canvas2DSceneLayer.Overlay,
            selected.Scene.Items.Single(item => item.Id == hit.SceneObjectId).Layer);
    }

    [Fact]
    public async Task SelectedEndpointHandleRemainsTopmostWhenItOverlapsLabel()
    {
        var initial = await BuildAsync("Approved");
        var visualId = initial.Edge.Source.VisualStateId!;
        var selected = await BuildAsync(
            "Approved",
            new ConnectorLabelPlacement(0d, default),
            new EditorStateSnapshot(selection: [visualId]));
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(
            selected.Edge.Id,
            "connector");
        var start = Assert.Single(selected.Scene.Items, item =>
            item.Origin.StableSourceKey?.StartsWith(
                "connector-endpoint-handle:",
                StringComparison.Ordinal) == true &&
            item.Metadata[Canvas2DConnectorEndpointMetadata.HandleRole].TextValue ==
                Canvas2DConnectorEndpointMetadata.StartEndpointRole &&
            item.Origin.RelatedSceneObjectIds.Contains(connectorId));

        var hit = Assert.IsType<Canvas2DSceneHitTestResult>(
            new Canvas2DSceneHitTestService().HitTest(
                selected.Scene,
                new PointD(
                    start.Bounds.X + (start.Bounds.Width / 2d),
                    start.Bounds.Y + (start.Bounds.Height / 2d))));

        Assert.Equal(start.Id, hit.SceneObjectId);
    }

    [Fact]
    public async Task RouteBendPreviewRecomputesLabelFromSamePathPosition()
    {
        var initial = await BuildAsync("Approved");
        var visualId = initial.Edge.Source.VisualStateId!;
        var connectorId = Canvas2DSceneObjectIdentity.ForProjected(
            initial.Edge.Id,
            "connector");
        var gesture = new EditorGestureSnapshot(
            "test:route-with-label",
            Canvas2DRouteGestureMetadata.Kind,
            new PointD(160d, 45d),
            new PointD(160d, 70d),
            [
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DRouteGestureMetadata.TargetSceneObjectId,
                    PropertyValue.FromText(connectorId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DRouteGestureMetadata.TargetVisualStateId,
                    PropertyValue.FromText(visualId.Value)),
                new KeyValuePair<string, PropertyValue>(
                    Canvas2DRouteGestureMetadata.BendIndex,
                    PropertyValue.FromInteger(1)),
            ]);
        var preview = await BuildAsync(
            "Approved",
            editorState: new EditorStateSnapshot(
                selection: [visualId],
                activeGesture: gesture));
        var transient = Assert.Single(preview.Scene.Items, item =>
            item.Origin.ProjectedObjectId == preview.Label.Id &&
            item.Origin.StableSourceKey?.StartsWith(
                "route-label-preview:",
                StringComparison.Ordinal) == true);

        Assert.Equal(new PointD(160d, 58d), Anchor(transient));
        Assert.Equal(Canvas2DHitTestMode.None, transient.HitTestPolicy.Mode);
    }

    [Fact]
    public async Task AuthoritativeRouteChangeRecomputesAbsoluteAnchorFromSamePlacement()
    {
        var placement = new ConnectorLabelPlacement(0.25d, new VectorD(5d, -7d));
        var first = await BuildAsync("Approved", placement);
        PointD[] changedRoute =
        [
            new PointD(110d, 45d),
            new PointD(110d, 95d),
            new PointD(210d, 95d),
            new PointD(210d, 45d),
        ];
        var changed = await BuildAsync(
            "Approved",
            placement,
            routePath: changedRoute);

        Assert.Equal(
            new PointD(140d, 38d),
            Anchor(Assert.Single(LabelLines(first.Scene, first.Label.Id))));
        Assert.Equal(
            new PointD(115d, 88d),
            Anchor(Assert.Single(LabelLines(changed.Scene, changed.Label.Id))));
    }

    private static async Task<BuiltConnectorLabelScene> BuildAsync(
        string text,
        ConnectorLabelPlacement? placement = null,
        EditorStateSnapshot? editorState = null,
        PointD[]? routePath = null,
        bool useMeasuredLayout = true)
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var edge = Assert.Single(inputs.Graph.Edges);
        var label = new ProjectedLabel(edge.Source, edge.Id, text);
        var graph = new ProjectedGraph(
            inputs.Graph.DocumentId,
            inputs.Graph.SourceRevision,
            inputs.Graph.Nodes,
            inputs.Graph.Edges,
            inputs.Graph.Groups,
            inputs.Graph.Ports,
            [label]);
        var visualModel = placement is null
            ? inputs.VisualModel
            : new VisualModelSnapshot(
                inputs.VisualModel.DocumentId,
                inputs.VisualModel.Revision,
                inputs.VisualModel.VisualStates.Select(visual =>
                    visual.Id == edge.Source.VisualStateId
                        ? new VisualStateSnapshot(
                            visual.Id,
                            visual.SemanticElementId,
                            visual.Position,
                            visual.Size,
                            visual.PlacementMode,
                            visual.Route,
                            ConnectorLabelPlacement.UpdateProperties(
                                visual.Properties,
                                placement))
                        : visual));
        var routing = inputs.Routing;
        if (routePath is not null)
        {
            var route = new RoutedConnectorGeometry(
                edge.Id,
                routePath[0],
                routePath[^1],
                routePath.Skip(1).SkipLast(1));
            routing = new RoutingResult(
                inputs.Routing.DocumentId,
                inputs.Routing.SourceRevision,
                inputs.Routing.LayoutAlgorithmId,
                inputs.Routing.RoutingAlgorithmId,
                new RoutingComputation([route]));
        }

        var builder = new Canvas2DSceneBuilder();
        var result = useMeasuredLayout
            ? await builder.BuildMeasuredAsync(
                graph,
                inputs.Layout,
                routing,
                visualModel,
                editorState ?? EditorStateSnapshot.Empty,
                new FixedMetricsService(),
                CreateRequest)
            : builder.Build(
                graph,
                inputs.Layout,
                routing,
                visualModel,
                editorState ?? EditorStateSnapshot.Empty);
        Assert.True(result.Succeeded, string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        return new BuiltConnectorLabelScene(
            Assert.IsType<Canvas2DScene>(result.Scene),
            edge,
            label);
    }

    private static Canvas2DSceneItem[] LabelLines(
        Canvas2DScene scene,
        ProjectedObjectId labelId) =>
        scene.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.ProjectedObjectId == labelId).ToArray();

    private static PointD Anchor(Canvas2DSceneItem line) =>
        line.Transform.TransformPoint(line.Geometry.TextAnchor);

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

    private sealed record BuiltConnectorLabelScene(
        Canvas2DScene Scene,
        ProjectedEdge Edge,
        ProjectedLabel Label);
}
