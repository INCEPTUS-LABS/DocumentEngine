using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DSceneHitTestServiceTests
{
    [Fact]
    public void ConfigurationIsImmutableValidatedAndStructurallyComparable()
    {
        var first = new Canvas2DSceneHitTestConfiguration(3.5d);
        var equivalent = new Canvas2DSceneHitTestConfiguration(3.5d);

        Assert.Equal(3.5d, first.MinimumStrokeTolerance);
        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.Equal(0d, Canvas2DSceneHitTestConfiguration.Default.MinimumStrokeTolerance);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Canvas2DSceneHitTestConfiguration(-1d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Canvas2DSceneHitTestConfiguration(double.NaN));
    }

    [Fact]
    public void ConnectorInteractionConfigurationIsLogicalValidatedAndStructurallyComparable()
    {
        var first = new Canvas2DConnectorInteractionConfiguration(5d);
        var equivalent = new Canvas2DConnectorInteractionConfiguration(5d);

        Assert.Equal(5d, Canvas2DConnectorInteractionConfiguration.Default.PathHitTolerance);
        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Canvas2DConnectorInteractionConfiguration(-1d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Canvas2DConnectorInteractionConfiguration(double.PositiveInfinity));
    }

    [Fact]
    public void EmptySceneReturnsNoHitWithoutMutation()
    {
        var scene = BuildScene([]);
        var before = scene.GetHashCode();

        var result = new Canvas2DSceneHitTestService().HitTest(scene, new PointD(1d, 2d));

        Assert.Null(result);
        Assert.Equal(before, scene.GetHashCode());
        Assert.Empty(scene.Items);
    }

    [Fact]
    public void OrphanLineJumpProxyNeverBecomesAnIndependentHitTarget()
    {
        var missingTargetId = new SceneObjectId("scene:missing-connector");
        var proxy = new Canvas2DSceneItem(
            new SceneObjectId("scene:orphan-line-jump"),
            Canvas2DSceneLayer.Connector,
            21,
            Canvas2DSceneGeometry.Path(
                [new PointD(0d, 0d), new PointD(10d, 0d)]),
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.Configuration,
                stableSourceKey: "orphan-line-jump",
                relatedSceneObjectIds: [missingTargetId]),
            style: new Canvas2DSceneStyle(stroke: "#000000"),
            hitTestPolicy: new Canvas2DHitTestPolicy(
                Canvas2DHitTestMode.Stroke,
                5d),
            metadata: Canvas2DConnectorLineJumpMetadata.Create(missingTargetId));
        var scene = BuildScene([proxy]);

        Assert.Null(new Canvas2DSceneHitTestService().HitTest(
            scene,
            new PointD(5d, 0d)));
    }

    [Theory]
    [InlineData(Canvas2DSceneGeometryKind.Rectangle, 5d, 5d, true)]
    [InlineData(Canvas2DSceneGeometryKind.Rectangle, 11d, 5d, false)]
    [InlineData(Canvas2DSceneGeometryKind.Text, 5d, 5d, true)]
    [InlineData(Canvas2DSceneGeometryKind.Image, 5d, 5d, true)]
    public void BoundsAndFillGeometryUseImmutableShapeData(
        Canvas2DSceneGeometryKind kind,
        double x,
        double y,
        bool expected)
    {
        var geometry = kind switch
        {
            Canvas2DSceneGeometryKind.Rectangle =>
                Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 10d, 10d)),
            Canvas2DSceneGeometryKind.Text =>
                Canvas2DSceneGeometry.Text(new RectD(0d, 0d, 10d, 10d), "text"),
            Canvas2DSceneGeometryKind.Image =>
                Canvas2DSceneGeometry.Image(new RectD(0d, 0d, 10d, 10d), "image:test"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var scene = BuildScene([Item("shape", geometry, Canvas2DHitTestMode.Fill)]);

        var hit = new Canvas2DSceneHitTestService().HitTest(scene, new PointD(x, y));

        Assert.Equal(expected, hit is not null);
    }

    [Fact]
    public void EllipseFillAndStrokeRespectCurvedGeometryAndTransform()
    {
        var fillScene = BuildScene(
        [
            Item(
                "ellipse-fill",
                Canvas2DSceneGeometry.Ellipse(new RectD(0d, 0d, 20d, 10d)),
                Canvas2DHitTestMode.Fill,
                transform: Matrix2D.CreateRotation(Math.PI / 2d)
                    .Then(Matrix2D.CreateTranslation(30d, 40d))),
        ]);
        var strokeScene = BuildScene(
        [
            Item(
                "ellipse-stroke",
                Canvas2DSceneGeometry.Ellipse(new RectD(0d, 0d, 20d, 10d)),
                Canvas2DHitTestMode.Stroke,
                strokeWidth: 2d),
        ]);

        Assert.NotNull(new Canvas2DSceneHitTestService().HitTest(
            fillScene,
            fillScene.Items[0].Transform.TransformPoint(new PointD(10d, 5d))));
        Assert.Null(new Canvas2DSceneHitTestService().HitTest(
            fillScene,
            fillScene.Items[0].Transform.TransformPoint(new PointD(1d, 1d))));
        Assert.NotNull(new Canvas2DSceneHitTestService().HitTest(
            strokeScene,
            new PointD(10d, 0.75d)));
        Assert.Null(new Canvas2DSceneHitTestService().HitTest(
            strokeScene,
            new PointD(10d, 3d)));
    }

    [Fact]
    public void OpenAndClosedPathsUseFillAndStrokeSemantics()
    {
        var openPath = Canvas2DSceneGeometry.Path(
            [new PointD(0d, 0d), new PointD(10d, 0d), new PointD(5d, 10d)]);
        var closedPath = Canvas2DSceneGeometry.Path(
            [new PointD(0d, 0d), new PointD(10d, 0d), new PointD(5d, 10d)],
            true);
        var openFill = BuildScene([Item("open-fill", openPath, Canvas2DHitTestMode.Fill)]);
        var openStroke = BuildScene([Item("open-stroke", openPath, Canvas2DHitTestMode.Stroke)]);
        var closedStroke = BuildScene([Item("closed-stroke", closedPath, Canvas2DHitTestMode.Stroke)]);

        Assert.NotNull(new Canvas2DSceneHitTestService().HitTest(openFill, new PointD(5d, 3d)));
        Assert.Null(new Canvas2DSceneHitTestService().HitTest(openStroke, new PointD(2.5d, 5d)));
        Assert.NotNull(new Canvas2DSceneHitTestService().HitTest(closedStroke, new PointD(2.5d, 5d)));
    }

    [Fact]
    public void StrokeReachCombinesHalfWidthWithGreatestDeclaredTolerance()
    {
        var path = Canvas2DSceneGeometry.Path([new PointD(0d, 0d), new PointD(20d, 0d)]);
        var policyScene = BuildScene(
            [Item("policy", path, Canvas2DHitTestMode.Stroke, 3d, strokeWidth: 2d)]);
        var serviceScene = BuildScene(
            [Item("service", path, Canvas2DHitTestMode.Stroke, 1d, strokeWidth: 2d)]);

        Assert.NotNull(new Canvas2DSceneHitTestService(
            new Canvas2DSceneHitTestConfiguration(1d)).HitTest(
                policyScene,
                new PointD(10d, 3.9d)));
        Assert.NotNull(new Canvas2DSceneHitTestService(
            new Canvas2DSceneHitTestConfiguration(4d)).HitTest(
                serviceScene,
                new PointD(10d, 4.9d)));
        Assert.Null(new Canvas2DSceneHitTestService(
            new Canvas2DSceneHitTestConfiguration(4d)).HitTest(
                serviceScene,
                new PointD(10d, 5.1d)));
    }

    [Theory]
    [InlineData(50d, 20d, true)]
    [InlineData(0d, 20d, true)]
    [InlineData(100d, 20d, true)]
    [InlineData(50d, 25.5d, true)]
    [InlineData(50d, 25.5001d, false)]
    [InlineData(-5.5d, 20d, true)]
    [InlineData(-5.5001d, 20d, false)]
    [InlineData(105.5d, 20d, true)]
    [InlineData(105.5001d, 20d, false)]
    public void ExactlyTwoPointConnectorPathIsHittableAlongItsEntireStrokeReach(
        double x,
        double y,
        bool expected)
    {
        var geometry = Canvas2DSceneGeometry.Path(
            [new PointD(0d, 20d), new PointD(100d, 20d)]);
        var scene = BuildScene(
        [
            Item(
                "straight-connector",
                geometry,
                Canvas2DHitTestMode.Stroke,
                Canvas2DConnectorInteractionConfiguration.Default.PathHitTolerance,
                strokeWidth: 1d),
        ]);

        var hit = new Canvas2DSceneHitTestService().HitTest(scene, new PointD(x, y));

        Assert.Equal(expected, hit is not null);
    }

    [Theory]
    [InlineData(10d, 0d, true)]
    [InlineData(20d, 10d, true)]
    [InlineData(30d, 20d, true)]
    [InlineData(40d, 30d, true)]
    [InlineData(25d, 16d, false)]
    public void SeveralSegmentConnectorPathTestsEverySegment(
        double x,
        double y,
        bool expected)
    {
        var geometry = Canvas2DSceneGeometry.Path(
        [
            new PointD(0d, 0d),
            new PointD(20d, 0d),
            new PointD(20d, 20d),
            new PointD(40d, 20d),
            new PointD(40d, 40d),
        ]);
        var scene = BuildScene(
        [
            Item(
                "several-segment-connector",
                geometry,
                Canvas2DHitTestMode.Stroke,
                1d,
                strokeWidth: 2d),
        ]);

        var hit = new Canvas2DSceneHitTestService().HitTest(scene, new PointD(x, y));

        Assert.Equal(expected, hit is not null);
    }

    [Fact]
    public void SceneBuilderAppliesCanonicalToleranceToStraightTwoPointConnector()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var edge = Assert.Single(inputs.Graph.Edges);
        var original = Assert.Single(inputs.Routing.Routes);
        var routing = new RoutingResult(
            inputs.Routing.DocumentId,
            inputs.Routing.SourceRevision,
            inputs.Routing.LayoutAlgorithmId,
            inputs.Routing.RoutingAlgorithmId,
            new RoutingComputation(
            [
                new RoutedConnectorGeometry(
                    edge.Id,
                    original.SourceAnchor,
                    original.DestinationAnchor),
            ]));
        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            inputs.Graph,
            inputs.Layout,
            routing,
            inputs.VisualModel,
            EditorStateSnapshot.Empty).Scene);
        var connector = Assert.Single(
            scene.Items,
            item => item.Layer == Canvas2DSceneLayer.Connector &&
                !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));
        var service = new Canvas2DSceneHitTestService();

        Assert.Equal(2, connector.Geometry.Points.Length);
        Assert.Equal(
            Canvas2DConnectorInteractionConfiguration.Default.PathHitTolerance,
            connector.HitTestPolicy.StrokeTolerance);
        Assert.Equal(
            connector.Id,
            service.HitTest(scene, new PointD(160d, 50.5d))?.SceneObjectId);
        Assert.Null(service.HitTest(scene, new PointD(160d, 50.5001d)));
    }

    [Theory]
    [InlineData(30d, 40d, true)]
    [InlineData(33d, 44d, true)]
    [InlineData(35.5d, 40d, true)]
    [InlineData(35.5001d, 40d, false)]
    public void DegenerateTwoPointConnectorPathUsesFinitePointReachWithoutDivisionByZero(
        double x,
        double y,
        bool expected)
    {
        var geometry = Canvas2DSceneGeometry.Path(
            [new PointD(30d, 40d), new PointD(30d, 40d)]);
        var scene = BuildScene(
        [
            Item(
                "degenerate-connector",
                geometry,
                Canvas2DHitTestMode.Stroke,
                Canvas2DConnectorInteractionConfiguration.Default.PathHitTolerance,
                strokeWidth: 1d),
        ]);

        var hit = new Canvas2DSceneHitTestService().HitTest(scene, new PointD(x, y));

        Assert.Equal(expected, hit is not null);
    }

    [Theory]
    [InlineData(0d, 0d, 20d, 0d, 20d, 5.5d, true)]
    [InlineData(0d, 0d, 20d, 0d, 20d, 6.1d, false)]
    [InlineData(0d, 0d, 0d, 30d, 3.5d, 15d, true)]
    [InlineData(0d, 0d, 0d, 30d, 4.1d, 15d, false)]
    public void NonUniformTransformScalesVisibleStrokePerpendicularToEachSegment(
        double startX,
        double startY,
        double endX,
        double endY,
        double hitX,
        double hitY,
        bool expected)
    {
        var inverseScaledStart = new PointD(startX / 2d, startY / 3d);
        var inverseScaledEnd = new PointD(endX / 2d, endY / 3d);
        var scene = BuildScene(
        [
            Item(
                "scaled-path",
                Canvas2DSceneGeometry.Path([inverseScaledStart, inverseScaledEnd]),
                Canvas2DHitTestMode.Stroke,
                strokeWidth: 4d,
                transform: Matrix2D.CreateScale(2d, 3d)),
        ]);

        var result = new Canvas2DSceneHitTestService().HitTest(
            scene,
            new PointD(hitX, hitY));

        Assert.Equal(expected, result is not null);
    }

    [Fact]
    public void NonUniformTransformScalesVisibleEllipseStrokeAtTheClosestTangent()
    {
        var scene = BuildScene(
        [
            Item(
                "scaled-ellipse",
                Canvas2DSceneGeometry.Ellipse(new RectD(0d, 0d, 20d, 10d)),
                Canvas2DHitTestMode.Stroke,
                strokeWidth: 4d,
                transform: Matrix2D.CreateScale(2d, 3d)),
        ]);
        var service = new Canvas2DSceneHitTestService();

        Assert.NotNull(service.HitTest(scene, new PointD(20d, -5.5d)));
        Assert.Null(service.HitTest(scene, new PointD(20d, -6.1d)));
    }

    [Fact]
    public void VisibilityPolicyAndFinalDocumentClipControlEligibility()
    {
        var geometry = Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 10d, 10d));
        var scene = BuildScene(
        [
            Item("visible", geometry, Canvas2DHitTestMode.Fill, clip: new RectD(102d, 52d, 4d, 4d),
                transform: Matrix2D.CreateTranslation(100d, 50d)),
            Item("none", geometry, Canvas2DHitTestMode.None,
                transform: Matrix2D.CreateTranslation(100d, 50d)),
            Item("invisible", geometry, Canvas2DHitTestMode.Fill, isVisible: false,
                transform: Matrix2D.CreateTranslation(100d, 50d)),
        ]);
        var service = new Canvas2DSceneHitTestService();

        Assert.Equal("scene:visible", service.HitTest(scene, new PointD(103d, 53d))?.SceneObjectId.Value);
        Assert.Null(service.HitTest(scene, new PointD(108d, 58d)));
    }

    [Fact]
    public void CanonicalTopmostEligibleItemWinsAndResultCarriesTraceAndPoint()
    {
        var geometry = Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 20d, 20d));
        var bottom = Item("bottom", geometry, Canvas2DHitTestMode.Bounds, zIndex: 0);
        var top = Item("top", geometry, Canvas2DHitTestMode.Bounds, zIndex: 1);
        var scene = BuildScene([top, bottom]);
        var point = new PointD(5d, 5d);
        var service = new Canvas2DSceneHitTestService();

        var results = Enumerable.Range(0, 10).Select(_ => service.HitTest(scene, point)).ToArray();

        Assert.All(results, result => Assert.Equal(top.Id, result?.SceneObjectId));
        Assert.Same(top.Origin, results[0]!.Origin);
        Assert.Equal(point, results[0]!.DocumentPoint);
    }

    [Fact]
    public void FillOrStrokeHitsEitherRegionEvenWhenPaintIsTransparent()
    {
        var scene = BuildScene(
        [
            Item(
                "transparent",
                Canvas2DSceneGeometry.Rectangle(new RectD(0d, 0d, 10d, 10d)),
                Canvas2DHitTestMode.FillOrStroke,
                strokeWidth: 2d,
                opacity: 0d),
        ]);
        var service = new Canvas2DSceneHitTestService();

        Assert.NotNull(service.HitTest(scene, new PointD(5d, 5d)));
        Assert.NotNull(service.HitTest(scene, new PointD(10.75d, 5d)));
        Assert.Null(service.HitTest(scene, new PointD(12d, 5d)));
    }

    [Fact]
    public void NullSceneIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new Canvas2DSceneHitTestService().HitTest(null!, default));
    }

    private static Canvas2DScene BuildScene(IEnumerable<Canvas2DSceneItem> items)
    {
        var ordered = items
            .OrderBy(static item => item.Layer)
            .ThenBy(static item => item.ZIndex)
            .ThenBy(static item => item.Id.Value, StringComparer.Ordinal);
        var documentId = new DocumentId("test:hit-document");
        var layoutId = new AlgorithmId("test:layout");
        var routingId = new AlgorithmId("test:routing");
        return new Canvas2DScene(
            documentId,
            DocumentRevision.Zero,
            layoutId,
            routingId,
            Canvas2DSceneConfiguration.Default,
            [],
            ViewportSnapshot.Default,
            Matrix2D.Identity,
            null,
            null,
            Contracts.Properties.PropertyMap.Empty,
            Contracts.Properties.PropertyMap.Empty,
            ordered);
    }

    private static Canvas2DSceneItem Item(
        string key,
        Canvas2DSceneGeometry geometry,
        Canvas2DHitTestMode mode,
        double tolerance = 0d,
        double strokeWidth = 1d,
        int zIndex = 0,
        Matrix2D? transform = null,
        RectD? clip = null,
        bool isVisible = true,
        double opacity = 1d)
    {
        var actualTransform = transform ?? Matrix2D.Identity;
        return new Canvas2DSceneItem(
            new SceneObjectId($"scene:{key}"),
            Canvas2DSceneLayer.Content,
            zIndex,
            geometry,
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.Configuration,
                stableSourceKey: key),
            actualTransform,
            clip,
            new Canvas2DSceneStyle(
                fill: "#000000",
                stroke: "#000000",
                strokeWidth: strokeWidth,
                opacity: opacity),
            isVisible,
            new Canvas2DHitTestPolicy(mode, tolerance));
    }
}
