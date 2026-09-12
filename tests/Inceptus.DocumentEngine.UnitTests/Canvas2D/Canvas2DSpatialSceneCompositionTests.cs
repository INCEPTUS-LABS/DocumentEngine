using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DSpatialSceneCompositionTests
{
    [Fact]
    public void SplitPresentationKeepsOneVisualAndOneConnectorWithFinalEndpointGeometry()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var source = inputs.Graph.Nodes[0];
        var target = inputs.Graph.Nodes[1];
        var contributor = new SplitSpatialContributor(
            source.Source.VisualStateId!,
            target.Source.VisualStateId!,
            sourceTranslation: new VectorD(100d, 200d),
            targetTranslation: new VectorD(400d, 500d));
        var builder = Builder(contributor);

        var result = builder.Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState);

        Assert.True(result.Succeeded);
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        Assert.Same(contributor.Plan, scene.SpatialPresentationPlan);
        var sourceItem = Assert.Single(scene.Items, item =>
            item.Origin.VisualStateId == source.Source.VisualStateId &&
            item.Layer == Canvas2DSceneLayer.Content);
        var targetItem = Assert.Single(scene.Items, item =>
            item.Origin.VisualStateId == target.Source.VisualStateId &&
            item.Layer == Canvas2DSceneLayer.Content);
        Assert.Equal(new RectD(110d, 220d, 100d, 50d), sourceItem.Bounds);
        Assert.Equal(new RectD(610d, 520d, 100d, 50d), targetItem.Bounds);

        var edge = Assert.Single(inputs.Graph.Edges);
        var connector = Assert.Single(scene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector"));
        var displayedPath = Canvas2DConnectorPathMetadata.Resolve(connector);
        Assert.Equal(new PointD(210d, 245d), displayedPath[0]);
        Assert.Equal(new PointD(610d, 545d), displayedPath[^1]);
        var mapping = Assert.IsType<Canvas2DConnectorPresentationMapping>(
            connector.ConnectorPresentationMapping);
        Assert.Equal(
            edge.PersistentRoute.AsEnumerable(),
            mapping.CanonicalEditablePath.AsEnumerable());
        Assert.Equal(edge.PersistentRoute[1], mapping.DisplayedEditablePath[1]);
        Assert.True(mapping.TryMapDisplayedEditablePointToCanonical(
            1,
            mapping.DisplayedEditablePath[1] + new VectorD(12d, 8d),
            out var canonical));
        Assert.Equal(edge.PersistentRoute[1] + new VectorD(12d, 8d), canonical);
        Assert.False(mapping.TryMapDisplayedEditablePointToCanonical(
            0,
            displayedPath[0],
            out _));
        Assert.Equal(scene.Items.Length, scene.Items.Select(static item => item.Id).Distinct().Count());
        Assert.Equal(
            Assert.Single(inputs.Routing.Routes).Path.AsEnumerable(),
            edge.PersistentRoute.AsEnumerable());
    }

    [Fact]
    public void SameRegionPresentationTranslatesCanonicalRouteExactly()
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute();
        var translation = new VectorD(160d, 280d);
        var contributor = new SplitSpatialContributor(
            inputs.Graph.Nodes[0].Source.VisualStateId!,
            inputs.Graph.Nodes[1].Source.VisualStateId!,
            translation,
            translation,
            sameRegion: true);

        var scene = Assert.IsType<Canvas2DScene>(Builder(contributor).Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState).Scene);
        var connector = scene.Items.Single(item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                Assert.Single(inputs.Graph.Edges).Id,
                "connector"));

        Assert.Equal(
            Assert.Single(inputs.Routing.Routes).Path.Select(point => point + translation),
            Canvas2DConnectorPathMetadata.Resolve(connector));
        Assert.True(connector.ConnectorPresentationMapping!.IsSameRegion);
    }

    [Fact]
    public void HiddenPlacementHidesItsNodeAndConnectedPresentationWithoutDeletingEither()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var source = inputs.Graph.Nodes[0];
        var target = inputs.Graph.Nodes[1];
        var contributor = new SplitSpatialContributor(
            source.Source.VisualStateId!,
            target.Source.VisualStateId!,
            new VectorD(100d, 100d),
            new VectorD(300d, 300d),
            hideTarget: true);

        var scene = Assert.IsType<Canvas2DScene>(Builder(contributor).Build(
            inputs.Graph,
            inputs.Layout,
            inputs.Routing,
            inputs.VisualModel,
            inputs.EditorState).Scene);

        Assert.False(scene.Items.Single(item =>
            item.Origin.VisualStateId == target.Source.VisualStateId &&
            item.Layer == Canvas2DSceneLayer.Content).IsVisible);
        Assert.False(scene.Items.Single(item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                Assert.Single(inputs.Graph.Edges).Id,
                "connector")).IsVisible);
        Assert.Equal(2, inputs.Graph.Nodes.Length);
        Assert.Single(inputs.Graph.Edges);
    }

    [Fact]
    public void ContractsRejectDuplicateVisualPlacementAndAmbiguousEndpointMapping()
    {
        var visualStateId = new VisualStateId("test:spatial:visual");
        var region = new Canvas2DSpatialRegion(
            new Canvas2DSpatialRegionId("test:spatial:region"),
            new ModelProfileId("test:spatial:profile"),
            containerSemanticElementId: null,
            Matrix2D.Identity,
            new RectD(0d, 0d, 100d, 100d));
        Assert.Throws<ArgumentException>(() => new Canvas2DSpatialPresentationPlan(
            [region],
            [
                new Canvas2DSpatialVisualPlacement(visualStateId, region.Id),
                new Canvas2DSpatialVisualPlacement(visualStateId, region.Id),
            ],
            Matrix2D.Identity));

        var mapping = new Canvas2DConnectorPresentationMapping(
            [new PointD(0d, 0d), new PointD(10d, 10d), new PointD(20d, 20d)],
            [new PointD(100d, 100d), new PointD(10d, 10d), new PointD(200d, 200d)],
            Matrix2D.Identity,
            region.Id,
            region.Id);
        Assert.False(mapping.TryMapDisplayedEditablePointToCanonical(
            0,
            new PointD(101d, 101d),
            out _));
        Assert.False(mapping.TryMapDisplayedEditablePointToCanonical(
            2,
            new PointD(201d, 201d),
            out _));
    }

    [Fact]
    public void CrossRegionAutomaticRouteIgnoresObsoleteCanonicalBendsAndAvoidsObstacle()
    {
        var edge = Assert.Single(Canvas2DSceneTestData.Create().Graph.Edges);
        var sourceRegion = Region("automatic-source");
        var targetRegion = Region("automatic-target");
        var obsoleteCanonicalBend = new PointD(50d, 25d);
        var request = new Canvas2DConnectorPresentationRoutingRequest(
            edge,
            [new PointD(0d, 0d), obsoleteCanonicalBend, new PointD(100d, 100d)],
            [new PointD(0d, 0d), new PointD(100d, 100d)],
            new PointD(0d, 0d),
            new PointD(100d, 100d),
            sourceRegion,
            targetRegion,
            Matrix2D.Identity,
            [new RectD(25d, -10d, 50d, 80d)]);

        var route = Router().Route(request);

        Assert.Equal(
            new[]
            {
                new PointD(0d, 0d),
                new PointD(0d, 100d),
                new PointD(100d, 100d),
            },
            route.DisplayedLogicalPath.AsEnumerable());
        Assert.DoesNotContain(obsoleteCanonicalBend, route.DisplayedLogicalPath);
    }

    [Fact]
    public void CrossRegionManualRouteRetainsEveryAuthoredWaypointAndExactInverseMapping()
    {
        var edge = Assert.Single(Canvas2DSceneTestData.Create().Graph.Edges);
        var sourceRegion = Region("manual-source");
        var targetRegion = Region("manual-target");
        var guidanceTransform = Matrix2D.CreateTranslation(new VectorD(100d, 100d));
        var canonicalEditable = new[]
        {
            new PointD(0d, 0d),
            new PointD(20d, 20d),
            new PointD(40d, 20d),
            new PointD(100d, 100d),
        };
        var request = new Canvas2DConnectorPresentationRoutingRequest(
            edge,
            [
                new PointD(0d, 0d),
                new PointD(10d, 90d),
                new PointD(100d, 100d),
            ],
            canonicalEditable,
            new PointD(0d, 0d),
            new PointD(200d, 200d),
            sourceRegion,
            targetRegion,
            guidanceTransform);

        var route = Router().Route(request);

        Assert.Equal(
            new[]
            {
                new PointD(0d, 0d),
                new PointD(120d, 0d),
                new PointD(120d, 120d),
                new PointD(140d, 120d),
                new PointD(200d, 120d),
                new PointD(200d, 200d),
            },
            route.DisplayedLogicalPath.AsEnumerable());
        Assert.Equal(
            new PointD(120d, 120d),
            route.Mapping.DisplayedEditablePath[1]);
        Assert.Equal(
            new PointD(140d, 120d),
            route.Mapping.DisplayedEditablePath[2]);
        Assert.True(route.Mapping.TryMapDisplayedEditablePointToCanonical(
            2,
            new PointD(145d, 127d),
            out var canonical));
        Assert.Equal(new PointD(45d, 27d), canonical);
    }

    private static OrganizationalPoolSceneContributor Router() =>
        new(new OrganizationalElementEligibilityPolicy(static _ => true));

    private static Canvas2DSpatialRegion Region(string suffix) =>
        new(
            new Canvas2DSpatialRegionId($"test:spatial:{suffix}"),
            new ModelProfileId("test:spatial:profile"),
            containerSemanticElementId: null,
            Matrix2D.Identity,
            new RectD(0d, 0d, 1000d, 1000d));

    private static Canvas2DSceneBuilder Builder(SplitSpatialContributor contributor) =>
        new(contributors:
        [
            new Canvas2DSceneContributorRegistration(
                new Canvas2DSceneContributorDescriptor(
                    new Canvas2DSceneContributorId("test:spatial:contributor"),
                    "1"),
                contributor,
                Canvas2DSceneContributionStage.Presentation),
        ]);

    private sealed class SplitSpatialContributor :
        ICanvas2DSceneContributor,
        ICanvas2DConnectorPresentationRouter
    {
        internal SplitSpatialContributor(
            VisualStateId sourceVisualStateId,
            VisualStateId targetVisualStateId,
            VectorD sourceTranslation,
            VectorD targetTranslation,
            bool sameRegion = false,
            bool hideTarget = false)
        {
            var profileId = new ModelProfileId("test:spatial:profile");
            var sourceRegion = new Canvas2DSpatialRegion(
                new Canvas2DSpatialRegionId("test:spatial:source"),
                profileId,
                containerSemanticElementId: null,
                Matrix2D.CreateTranslation(sourceTranslation),
                new RectD(sourceTranslation.X, sourceTranslation.Y, 1000d, 1000d));
            var targetRegion = sameRegion
                ? sourceRegion
                : new Canvas2DSpatialRegion(
                    new Canvas2DSpatialRegionId("test:spatial:target"),
                    profileId,
                    containerSemanticElementId: null,
                    Matrix2D.CreateTranslation(targetTranslation),
                    new RectD(targetTranslation.X, targetTranslation.Y, 1000d, 1000d));
            Plan = new Canvas2DSpatialPresentationPlan(
                ReferenceEquals(sourceRegion, targetRegion)
                    ? [sourceRegion]
                    : [sourceRegion, targetRegion],
                [
                    new Canvas2DSpatialVisualPlacement(
                        sourceVisualStateId,
                        sourceRegion.Id),
                    new Canvas2DSpatialVisualPlacement(
                        targetVisualStateId,
                        targetRegion.Id,
                        !hideTarget),
                ],
                Matrix2D.Identity);
        }

        internal Canvas2DSpatialPresentationPlan Plan { get; }

        public Canvas2DSceneContributionResult Contribute(
            Canvas2DSceneContributionContext context) =>
            Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(
                spatialPresentationPlan: Plan));

        public Canvas2DConnectorPresentationRoute Route(
            Canvas2DConnectorPresentationRoutingRequest request)
        {
            var logical = request.CanonicalLogicalPath
                .Select((point, index) => index switch
                {
                    0 => request.DisplayedSourceAnchor,
                    _ when index == request.CanonicalLogicalPath.Length - 1 =>
                        request.DisplayedTargetAnchor,
                    _ => request.CanonicalGuidanceToSceneTransform.TransformPoint(point),
                })
                .ToArray();
            var editable = request.CanonicalEditablePath
                .Select((point, index) => index switch
                {
                    0 => request.DisplayedSourceAnchor,
                    _ when index == request.CanonicalEditablePath.Length - 1 =>
                        request.DisplayedTargetAnchor,
                    _ => request.CanonicalGuidanceToSceneTransform.TransformPoint(point),
                })
                .ToArray();
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
