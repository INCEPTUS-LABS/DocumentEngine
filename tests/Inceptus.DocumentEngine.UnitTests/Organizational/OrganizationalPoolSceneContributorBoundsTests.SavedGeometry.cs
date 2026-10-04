using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Organizational;

public sealed partial class OrganizationalPoolSceneContributorBoundsTests
{
    [Fact]
    public void PoolNameMeasurementIsHorizontalSingleLineAndHasTypographyBasedMinimum()
    {
        var fixture = CreateDestinationFixture(0, 0);
        var source = fixture.Document;
        const string name = "A long Pool name\r\nwith another line\u2028and a third line";
        var semantic = new SemanticModelSnapshot(source.DocumentId, source.Revision,
            source.SemanticModel.Elements.Select(element => element.Id == PoolAId
                ? OrganizationalSemanticFactory.CreatePool(PoolAId, name) : element),
            modelProfiles: source.SemanticModel.ModelProfiles);
        fixture = fixture with { Document = new DocumentSnapshot(semantic, source.VisualModel, source.Metadata) };
        var prepared = PrepareGeometry(fixture, measuredLineHeight: 200d);
        var request = prepared.Base.TextRequests.Values.Single(value => value.Text.StartsWith("A long", StringComparison.Ordinal));
        Assert.Equal("A long Pool name with another line and a third line", request.Text);
        Assert.Equal(TextWritingMode.HorizontalTopToBottom, request.WritingMode);
        var pool = prepared.Result.Regions.Single(region => region.ContainerSemanticElementId == PoolAId);
        Assert.False(PrepareGeometry(fixture, existing: prepared.Result.Regions,
            requested: [KeyValuePair.Create(pool.Id, 209d)], measuredLineHeight: 200d).Result.Succeeded);
        var exact = PrepareGeometry(fixture, existing: prepared.Result.Regions,
            requested: [KeyValuePair.Create(pool.Id, 210d)], measuredLineHeight: 200d);
        Assert.True(exact.Result.Succeeded);
        var compact = ContributeAccepted(fixture, InstallGeometry(fixture, exact), collapsed: true);
        Assert.Equal(210d, Destination(compact.SpatialPresentationPlan!, 0).Bounds.Height);
        Assert.Equal(224d, pool.ExpandedHeight);
    }

    [Fact]
    public void AcceptedHeightsIgnoreLaterCaptionGrowthAndAcceptExactBodyBottom()
    {
        var fixture = CreateDestinationFixture(0, 1);
        var initial = PrepareGeometry(fixture);
        Assert.True(initial.Result.Succeeded);
        Assert.All(initial.Result.Regions, region => Assert.Equal(224d, region.ExpandedHeight));
        var pool = initial.Result.Regions.Single(region => region.ContainerSemanticElementId == PoolAId);
        var body = new Canvas2DScopeGeometryNodeBounds(fixture.Graph.Nodes[0].Source.VisualStateId!,
            new RectD(40d, 156d, 36d, 36d), new RectD(-400d, 600d, 1200d, 400d));

        var exactBottom = PrepareGeometry(fixture, nodes: [body], existing: initial.Result.Regions,
            requested: [KeyValuePair.Create(pool.Id, 224d)]);

        Assert.True(exactBottom.Result.Succeeded);
        Assert.Equal(224d, exactBottom.Result.Regions.Single(region => region.Id == pool.Id).ExpandedHeight);
        var overflow = PrepareGeometry(fixture,
            nodes: [new Canvas2DScopeGeometryNodeBounds(body.VisualStateId, body.BodyBounds.Translate(new VectorD(0d, 1d)))],
            existing: initial.Result.Regions);
        Assert.False(overflow.Result.Succeeded);
        Assert.Empty(overflow.Result.Regions);
        Assert.Null(overflow.Result.Plan);
        Assert.Equal("INCEPTUS.SPATIAL.DIMENSION.CAPACITY", Assert.Single(overflow.Result.Diagnostics).Code);

        var grown = PrepareGeometry(fixture, nodes: [body], existing: initial.Result.Regions,
            requested: [KeyValuePair.Create(pool.Id, 400d)]);
        Assert.True(grown.Result.Succeeded);
        Assert.Equal(400d, grown.Result.Regions.Single(region => region.Id == pool.Id).ExpandedHeight);
        Assert.Equal(224d, pool.ExpandedHeight);
    }

    [Fact]
    public void InitialCaptionGeometryCanSetDefaultButDoesNotSetTheMinimumHeight()
    {
        var fixture = CreateDestinationFixture(0, 1);
        var measured = new Canvas2DScopeGeometryNodeBounds(fixture.Graph.Nodes[0].Source.VisualStateId!,
            new RectD(40d, 30d, 36d, 36d), new RectD(0d, 300d, 100d, 100d));
        var initial = PrepareGeometry(fixture, nodes: [measured]);
        var pool = initial.Result.Regions.Single(region => region.ContainerSemanticElementId == PoolAId);
        Assert.Equal(464d, pool.ExpandedHeight);

        var shrunk = PrepareGeometry(fixture, nodes: [measured], existing: initial.Result.Regions,
            requested: [KeyValuePair.Create(pool.Id, 144d)]);

        Assert.True(shrunk.Result.Succeeded);
        Assert.Equal(144d, shrunk.Result.Regions.Single(region => region.Id == pool.Id).ExpandedHeight);
    }

    [Fact]
    public void DormantCapacitiesRetainIdentityAndValidateBodiesWhenReactivated()
    {
        var fixture = CreateDestinationFixture(0, 1);
        var initial = PrepareGeometry(fixture);
        var disabled = fixture with { Document = WithAvailability(fixture.Document, false) };
        var dormant = PrepareGeometry(disabled, existing: initial.Result.Regions);
        Assert.True(dormant.Result.Succeeded);
        Assert.Null(dormant.Result.Plan);
        Assert.Equal(initial.Result.Regions.Select(static region => (region.Id, region.ExpandedHeight)),
            dormant.Result.Regions.Select(static region => (region.Id, region.ExpandedHeight)));
        Assert.All(dormant.Result.Regions, region =>
        {
            Assert.False(region.IsActive);
            Assert.Null(region.ContentBounds);
            Assert.Null(region.LocalToScopeTransform);
        });
        var activated = PrepareGeometry(fixture, existing: dormant.Result.Regions);
        Assert.True(activated.Result.Succeeded);
        Assert.Equal(initial.Result.Regions.ToArray(), activated.Result.Regions.ToArray());
        var oversized = PrepareGeometry(fixture, existing: dormant.Result.Regions,
            nodes: [new Canvas2DScopeGeometryNodeBounds(fixture.Graph.Nodes[0].Source.VisualStateId!,
                new RectD(40d, 300d, 36d, 36d))]);
        Assert.False(oversized.Result.Succeeded);
    }

    [Fact]
    public void DeletingLastPoolRetainsOnlyDormantUnassignedCapacity()
    {
        var fixture = CreateDestinationFixture(2, 0);
        var initial = PrepareGeometry(fixture);
        var before = fixture.Document;
        var semantic = before.SemanticModel;
        var withoutPools = new DocumentSnapshot(new SemanticModelSnapshot(before.DocumentId, before.Revision,
            [], modelProfiles: semantic.ModelProfiles), before.VisualModel, before.Metadata);
        var removed = PrepareGeometry(fixture with { Document = withoutPools }, existing: initial.Result.Regions);

        Assert.True(removed.Result.Succeeded);
        var unassigned = Assert.Single(removed.Result.Regions);
        Assert.Null(unassigned.ContainerSemanticElementId);
        Assert.False(unassigned.IsActive);
        Assert.Equal(initial.Result.Regions.Single(region => region.ContainerSemanticElementId is null).ExpandedHeight,
            unassigned.ExpandedHeight);
    }

    [Fact]
    public void CompactRowsUseMeasuredTypographyAndOnlyTranslateFollowingVisibleRegions()
    {
        var fixture = CreateDestinationFixture(1, 2);
        var prepared = PrepareGeometry(fixture, measuredLineHeight: 19d);
        var accepted = InstallGeometry(fixture, prepared);
        var before = accepted.VisualModel;
        var expanded = ContributeAccepted(fixture, accepted, collapsed: false);
        var compact = ContributeAccepted(fixture, accepted, collapsed: true);
        var expandedPlan = Assert.IsType<Canvas2DSpatialPresentationPlan>(expanded.SpatialPresentationPlan);
        var compactPlan = Assert.IsType<Canvas2DSpatialPresentationPlan>(compact.SpatialPresentationPlan);
        var expandedA = Destination(expandedPlan, 0);
        var compactA = Destination(compactPlan, 0);
        Assert.Equal(29d, compactA.Bounds.Height);
        Assert.Equal(expandedA.Bounds.Top, compactA.Bounds.Top);
        var delta = 29d - expandedA.Bounds.Height;
        foreach (var index in new[] { 1, 2 })
        {
            var oldRegion = Destination(expandedPlan, index);
            var current = Destination(compactPlan, index);
            Assert.Equal(oldRegion.Bounds.Translate(new VectorD(0d, delta)), current.Bounds);
            var local = new PointD(40d, 30d);
            Assert.Equal(current.MapLocalToScene(local), compactPlan.MapLogicalToScene(oldRegion.MapLocalToScene(local)));
        }
        Assert.All(compactPlan.VisualPlacements, placement => Assert.True(placement.IsVisible));
        var compactName = compact.Items.Single(item => item.Id.Value.EndsWith($"{PoolAId.Value}:pool-name", StringComparison.Ordinal));
        Assert.Equal(Matrix2D.Identity, compactName.Transform);
        Assert.Equal(Canvas2DTextAlignment.Start, compactName.Geometry.TextAlignment);
        Assert.Equal(new PointD(48d, 45d), compactName.Geometry.TextAnchor);
        Assert.Equal(19d, compactName.Geometry.Bounds.Height);
        Assert.True(compactName.Geometry.Bounds.Width > compactA.Bounds.Width);
        Assert.Null(compactName.Clip);
        var exclusion = compact.Items.Single(item => item.Bounds.Height == 29d &&
            Canvas2DSemanticSceneInteractionMetadata.BlocksPlacement(item));
        Assert.Equal(29d, exclusion.Bounds.Height);
        Assert.Equal(Destination(compactPlan, 2).Id, compactPlan.MovementBottomBoundaryRegionId);
        Assert.Equal(expanded, ContributeAccepted(fixture, accepted, collapsed: false));
        Assert.Same(before, accepted.VisualModel);
    }

    [Fact]
    public void CollapsedMembersAreHiddenAndUnpreparedCollapseFailsExplicitly()
    {
        var fixture = CreateDestinationFixture(0, 2);
        var prepared = PrepareGeometry(fixture);
        var accepted = InstallGeometry(fixture, prepared);
        var compact = ContributeAccepted(fixture, accepted, collapsed: true);
        Assert.All(compact.SpatialPresentationPlan!.VisualPlacements, placement => Assert.False(placement.IsVisible));
        var context = AcceptedContext(fixture, fixture.Document, collapsed: true);
        var rejected = fixture.Contributor.Contribute(context);
        Assert.False(rejected.Succeeded);
        Assert.Equal("organizational.scene.incompatible-saved-geometry", Assert.Single(rejected.Diagnostics).Code);
    }

    private static GeometryPreparation PrepareGeometry(
        DestinationFixture fixture,
        IEnumerable<Canvas2DScopeGeometryNodeBounds>? nodes = null,
        IEnumerable<SpatialRegionGeometrySnapshot>? existing = null,
        IEnumerable<KeyValuePair<Canvas2DSpatialRegionId, double>>? requested = null,
        double measuredLineHeight = 15.6d)
    {
        var document = fixture.Document;
        var inputs = new ScopeGeometryInputs(document.SemanticModel.RootScopeId, document.SemanticModel.Elements,
            document.SemanticModel.Relationships, document.SemanticModel.ScopeMemberships,
            document.VisualModel.VisualStates, document.SemanticModel.ModelProfiles,
            document.SemanticModel.ProfileAssignments, document.VisualModel.ProfileElementPresentations);
        var text = new TextMeasurementRequest("", "Arial", "arial", "1", 12d, 14.4d, 400,
            TextFontStyle.Normal, "en", TextDirection.LeftToRight, TextWritingMode.HorizontalTopToBottom,
            1d, "test", "1");
        var baseContext = new Canvas2DScopeGeometryBaseContext(inputs, fixture.Graph, fixture.Inputs.Layout,
            Canvas2DSceneConfiguration.Default, fixture.Descriptor, text);
        var baseResult = fixture.Contributor.PrepareBase(baseContext);
        Assert.True(baseResult.Succeeded);
        var measurements = baseResult.TextRequests.Select(entry => KeyValuePair.Create(entry.Key,
            new TextMetrics(1500d, 10d, 3d, measuredLineHeight, new RectD(0d, 0d, 1500d, measuredLineHeight), "arial")))
            .ToImmutableDictionary();
        var measuredNodes = nodes?.ToImmutableArray() ?? fixture.BaseItems.Select(item =>
            new Canvas2DScopeGeometryNodeBounds(item.Origin.VisualStateId!, item.Bounds)).ToImmutableArray();
        var result = fixture.Contributor.PreparePresentation(new Canvas2DScopeGeometryPresentationContext(
            baseContext, measuredNodes, measurements, existing ?? [], requested));
        return new GeometryPreparation(baseContext, baseResult, measurements, measuredNodes, result);
    }

    private static DocumentSnapshot InstallGeometry(DestinationFixture fixture, GeometryPreparation prepared)
    {
        Assert.True(prepared.Result.Succeeded);
        var placements = prepared.Result.Plan?.VisualPlacements.ToDictionary(static item => item.VisualStateId);
        var geometry = new ScopeGeometrySnapshot("test", "1", fixture.Inputs.Layout.AlgorithmId,
            prepared.Context.Configuration, prepared.Context.TextConfiguration,
            [new ScopeGeometryContributorSnapshot(fixture.Descriptor, Canvas2DSceneContributionStage.Presentation)],
            prepared.Nodes.Select(node => new ScopeNodeGeometrySnapshot(node.VisualStateId, node.BodyBounds,
                Matrix2D.Identity, placements?.GetValueOrDefault(node.VisualStateId)?.RegionId)),
            prepared.Result.Regions, [], prepared.Base.TextRequests.Select(entry =>
                new ScopeTextMeasurementSnapshot(entry.Value, prepared.Measurements[entry.Key])), prepared.Result.SpatialWidths);
        var source = fixture.Document;
        return new DocumentSnapshot(source.SemanticModel,
            new VisualModelSnapshot(source.DocumentId, source.Revision, source.VisualModel.VisualStates,
                source.VisualModel.ProfileElementPresentations,
                [new ScopeRoutingSnapshot(source.SemanticModel.RootScopeId, geometry, [])]), source.Metadata);
    }

    private static DocumentSnapshot WithAvailability(DocumentSnapshot source, bool available) =>
        new(new SemanticModelSnapshot(source.DocumentId, source.Revision, source.SemanticModel.Elements,
            source.SemanticModel.Relationships, source.SemanticModel.NestedScopes, source.SemanticModel.ScopeMemberships,
            available ? new ModelProfileStateSnapshot([OrganizationalModelProfile.Id]) : ModelProfileStateSnapshot.Empty,
            source.SemanticModel.ProfileAssignments), source.VisualModel, source.Metadata);

    private static Canvas2DSceneContribution ContributeAccepted(DestinationFixture fixture, DocumentSnapshot document, bool collapsed)
    {
        var result = fixture.Contributor.Contribute(AcceptedContext(fixture, document, collapsed));
        Assert.True(result.Succeeded);
        return result.Contribution!;
    }

    private static Canvas2DSceneContributionContext AcceptedContext(
        DestinationFixture fixture, DocumentSnapshot document, bool collapsed) =>
        new(fixture.Graph, fixture.Inputs.Layout, fixture.Inputs.Routing, document.VisualModel,
            EditorStateSnapshot.Empty, Canvas2DSceneConfiguration.Default, fixture.Descriptor,
            new Canvas2DScenePresentationContext(document, document.SemanticModel.RootScopeId,
                ModelProfileViewStateSnapshot.Empty,
                ModelProfileElementViewStateSnapshot.Empty.WithCollapsed(OrganizationalModelProfile.Id, PoolAId, collapsed),
                fixture.BaseItems));

    private sealed record GeometryPreparation(Canvas2DScopeGeometryBaseContext Context,
        Canvas2DScopeGeometryBaseResult Base, ImmutableDictionary<SceneObjectId, TextMetrics> Measurements,
        ImmutableArray<Canvas2DScopeGeometryNodeBounds> Nodes, Canvas2DScopeGeometryPresentationResult Result);
}
