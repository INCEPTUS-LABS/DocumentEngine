using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DSavedSpatialPresentationTests
{
    [Theory]
    [InlineData(ConnectorRoutingType.Automatic)]
    [InlineData(ConnectorRoutingType.Straight)]
    [InlineData(ConnectorRoutingType.Manual)]
    public void SavedPathsUseModeSpecificCompactionWithoutCallingPresentationRouter(ConnectorRoutingType type)
    {
        var fixture = Create(type, hiddenSource: false);
        var before = fixture.Visuals;

        var result = fixture.Builder.Build(fixture.Graph, fixture.Layout, fixture.Routing,
            fixture.Visuals, EditorStateSnapshot.Empty);

        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var connector = result.Scene!.Items.Single(item => item.Id ==
            Canvas2DSceneObjectIdentity.ForProjected(fixture.Graph.Edges[0].Id, "connector"));
        var mapping = Assert.IsType<Canvas2DConnectorPresentationMapping>(connector.ConnectorPresentationMapping);
        Assert.Equal(Canvas2DConnectorPresentationSourceSpace.ScopeLogical, mapping.SourceSpace);
        Assert.Equal(fixture.Map, mapping.CoordinateMap);
        var expected = type == ConnectorRoutingType.Automatic
            ? fixture.Map.MapPath(fixture.Record.Path)
            : fixture.Record.Path.Select(fixture.Map.MapLogicalToScene).ToImmutableArray();
        Assert.Equal(expected.ToArray(), Canvas2DConnectorPathMetadata.Resolve(connector).ToArray());
        Assert.Equal(type == ConnectorRoutingType.Manual ? fixture.Record.Path.Length : 2,
            mapping.DisplayedEditablePath.Length);
        Assert.True(connector.IsVisible);
        Assert.Equal(0, fixture.Contributor.RouteCalls);
        Assert.Same(before, fixture.Visuals);
        Assert.Equal(fixture.Record, fixture.Visuals.RoutingScopes!.Value[0].Connectors[0]);
    }

    [Fact]
    public void HiddenEndpointSuppressesConnectorLabelsArrowsAndInteractionFamily()
    {
        var fixture = Create(ConnectorRoutingType.Manual, hiddenSource: true);
        var result = fixture.Builder.Build(fixture.Graph, fixture.Layout, fixture.Routing,
            fixture.Visuals, EditorStateSnapshot.Empty);
        Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        var edgeId = fixture.Graph.Edges[0].Id;
        var labelId = fixture.Graph.Labels[0].Id;
        var family = result.Scene!.Items.Where(item => item.Origin.ProjectedObjectId == edgeId ||
            item.Origin.ProjectedObjectId == labelId).ToArray();
        Assert.True(family.Length >= 3);
        Assert.All(family, item =>
        {
            Assert.False(item.IsVisible);
            Assert.Equal(Canvas2DHitTestMode.None, item.HitTestPolicy.Mode);
        });
        Assert.Equal(0, fixture.Contributor.RouteCalls);
    }

    private static Fixture Create(ConnectorRoutingType type, bool hiddenSource)
    {
        var inputs = Canvas2DSceneTestData.Create();
        var edge = inputs.Graph.Edges[0];
        var graph = new ProjectedGraph(inputs.Graph.DocumentId, inputs.Graph.SourceRevision,
            inputs.Graph.Nodes, inputs.Graph.Edges, ports: inputs.Graph.Ports,
            labels: [new ProjectedLabel(edge.Source, edge.Id, "Connector label")]);
        var scopeId = new DocumentScopeId(graph.DocumentId.Value);
        var profile = new ModelProfileId("test:compact");
        var bands = new[]
        {
            new Canvas2DSpatialCoordinateBand(new("test:empty-row"), 40d, 440d, 40d, 65.6d),
            new Canvas2DSpatialCoordinateBand(new("test:source-row"), 480d, 720d, 105.6d, 345.6d),
            new Canvas2DSpatialCoordinateBand(new("test:target-row"), 760d, 1060d, 385.6d, 685.6d),
        };
        var map = new Canvas2DSpatialCoordinateMap(bands);
        var savedRegions = bands.Select(band => new SpatialRegionGeometrySnapshot(band.RegionId, profile,
            null, band.LogicalHeight, Matrix2D.CreateTranslation(0d, band.LogicalTop),
            new RectD(0d, band.LogicalTop, 1000d, band.LogicalHeight))).ToArray();
        var regions = bands.Select(band => new Canvas2DSpatialRegion(band.RegionId, profile, null,
            Matrix2D.CreateTranslation(0d, band.DisplayedTop),
            new RectD(0d, band.DisplayedTop, 1000d, band.DisplayedHeight))).ToArray();
        var descriptor = new Canvas2DSceneContributorDescriptor(new("test:compact-contributor"), "1");
        var placements = graph.Nodes.Select((node, index) => new Canvas2DSpatialVisualPlacement(
            node.Source.VisualStateId!, bands[index + 1].RegionId, index != 0 || !hiddenSource)).ToArray();
        var geometry = new ScopeGeometrySnapshot("test:compact", "1", inputs.Layout.AlgorithmId,
            Canvas2DSceneConfiguration.Default,
            new TextMeasurementRequest("", "Arial", "arial", "1", 12d, 14.4d, 400, TextFontStyle.Normal,
                "en", TextDirection.LeftToRight, TextWritingMode.HorizontalTopToBottom, 1d, "test", "1"),
            [new ScopeGeometryContributorSnapshot(descriptor, Canvas2DSceneContributionStage.Presentation)],
            graph.Nodes.Select((node, index) => new ScopeNodeGeometrySnapshot(node.Source.VisualStateId!,
                inputs.Layout.Nodes.Single(layout => layout.ProjectedObjectId == node.Id).Bounds,
                Matrix2D.Identity, placements[index].RegionId)), savedRegions, []);
        PointD[] fullPath = [new(110d, 525d), new(120d, 525d), new(120d, 240d),
            new(180d, 240d), new(180d, 805d), new(210d, 805d)];
        var path = type == ConnectorRoutingType.Straight ? new[] { fullPath[0], fullPath[^1] } : fullPath;
        var proof = type == ConnectorRoutingType.Automatic ? new ConnectorAutomaticRouteProof(
            inputs.Routing.RoutingAlgorithmId, "1", 16d, 16d,
            new ConnectorRoutingEndpointObservation(graph.Nodes[0].Source.SemanticElementId,
                graph.Nodes[0].Source.VisualStateId!, null, ConnectorAnchorRole.Source,
                ConnectorAnchorSide.Right, 0, 1, path[0], new VectorD(1d, 0d)),
            new ConnectorRoutingEndpointObservation(graph.Nodes[1].Source.SemanticElementId,
                graph.Nodes[1].Source.VisualStateId!, null, ConnectorAnchorRole.Target,
                ConnectorAnchorSide.Left, 0, 1, path[^1], new VectorD(-1d, 0d))) : null;
        var record = new ConnectorRoutingRecord(edge.Source.VisualStateId!, type, ConnectorRoutingOutcome.Path,
            path, type == ConnectorRoutingType.Manual ? path.Skip(1).SkipLast(1) : null, proof);
        var visuals = new VisualModelSnapshot(inputs.VisualModel.DocumentId, inputs.VisualModel.Revision,
            inputs.VisualModel.VisualStates, [], [new ScopeRoutingSnapshot(scopeId, geometry, [record])]);
        var logicalLayout = new LayoutResult(inputs.Layout.DocumentId, inputs.Layout.SourceRevision,
            inputs.Layout.AlgorithmId, new LayoutComputation(inputs.Layout.Nodes.Select(node =>
            {
                var owner = graph.Nodes.Single(projected => projected.Id == node.ProjectedObjectId);
                var region = savedRegions.Single(saved => saved.Id ==
                    placements.Single(placement => placement.VisualStateId == owner.Source.VisualStateId).RegionId);
                var transform = region.LocalToScopeTransform!.Value;
                return new LayoutNodeGeometry(node.ProjectedObjectId,
                    node.Bounds.Translate(new VectorD(transform.OffsetX, transform.OffsetY)), node.Transform.Then(transform));
            })));
        var routing = new RoutingResult(graph.DocumentId, graph.SourceRevision, inputs.Layout.AlgorithmId,
            inputs.Routing.RoutingAlgorithmId, new RoutingComputation([new RoutedConnectorGeometry(edge.Id,
                path[0], path[^1], path.Skip(1).SkipLast(1), edge.SourcePortId, edge.TargetPortId)]),
            diagnostics: null, new RoutingLogicalGeometry(scopeId, geometry, logicalLayout));
        var contributor = new FixedSpatialContributor(new Canvas2DSpatialPresentationPlan(regions, placements,
            Matrix2D.Identity, map, regions[^1].Id));
        var builder = new Canvas2DSceneBuilder(contributors:
            [new Canvas2DSceneContributorRegistration(descriptor, contributor, Canvas2DSceneContributionStage.Presentation)]);
        return new Fixture(builder, graph, inputs.Layout, routing, visuals, record, map, contributor);
    }

    private sealed class FixedSpatialContributor(Canvas2DSpatialPresentationPlan plan) :
        ICanvas2DSceneContributor, ICanvas2DConnectorPresentationRouter
    {
        internal int RouteCalls { get; private set; }
        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context) =>
            Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(spatialPresentationPlan: plan));

        public Canvas2DConnectorPresentationRoute Route(Canvas2DConnectorPresentationRoutingRequest request)
        {
            RouteCalls++;
            throw new InvalidOperationException("Saved paths must not invoke a presentation router.");
        }
    }

    private sealed record Fixture(Canvas2DSceneBuilder Builder, ProjectedGraph Graph, LayoutResult Layout,
        RoutingResult Routing, VisualModelSnapshot Visuals, ConnectorRoutingRecord Record,
        Canvas2DSpatialCoordinateMap Map, FixedSpatialContributor Contributor);
}
