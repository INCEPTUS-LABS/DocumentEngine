using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.UnitTests.Canvas2D;

namespace Inceptus.DocumentEngine.UnitTests.Organizational;

public sealed partial class OrganizationalPoolSceneContributorBoundsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EmptySingleAndSeveralSmallElementsKeepEveryDestinationFloor(int destinationIndex)
    {
        Canvas2DSpatialPresentationPlan? emptyPlan = null;
        for (var count = 0; count <= 3; count++)
        {
            var fixture = CreateDestinationFixture(destinationIndex, count);
            var documentBefore = fixture.Document;
            var visualsBefore = documentBefore.VisualModel.VisualStates.ToArray();
            var assignmentsBefore = documentBefore.SemanticModel.ProfileAssignments.ToArray();

            var contribution = fixture.Contribute(fixture.BaseItems);
            var plan = Assert.IsType<Canvas2DSpatialPresentationPlan>(contribution.SpatialPresentationPlan);
            emptyPlan ??= plan;

            Assert.Equal(emptyPlan.Regions.ToArray(), plan.Regions.ToArray());
            foreach (var region in plan.Regions)
            {
                Assert.Equal(564d, region.Bounds.Width);
                Assert.Equal(224d, region.Bounds.Height);
                var reusablePoint = region.MapLocalToScene(new PointD(440d, 170d));
                Assert.True(region.Bounds.Contains(reusablePoint));
                Assert.Equal(new PointD(440d, 170d), region.MapSceneToLocal(reusablePoint));
            }
            var destination = Destination(plan, destinationIndex);
            Assert.Equal(count, plan.VisualPlacements.Length);
            Assert.All(plan.VisualPlacements, placement => Assert.Equal(destination.Id, placement.RegionId));
            Assert.Equal(count, plan.VisualPlacements.Select(placement => placement.VisualStateId).Distinct().Count());
            Assert.Equal(602d, PoolBackground(contribution, PoolAId).Bounds.Width);
            Assert.Equal(PoolBackground(contribution, PoolAId).Bounds.Width,
                PoolBackground(contribution, PoolBId).Bounds.Width);

            Assert.Equal(contribution, fixture.Contribute(fixture.BaseItems));
            Assert.Equal(contribution, fixture.Contribute(fixture.BaseItems.Concat(contribution.Items)));
            Assert.Same(documentBefore, fixture.Document);
            Assert.Equal(visualsBefore, fixture.Document.VisualModel.VisualStates.ToArray());
            Assert.Equal(assignmentsBefore, fixture.Document.SemanticModel.ProfileAssignments.ToArray());
            Assert.Equal(documentBefore.Revision, fixture.Document.Revision);
            if (destinationIndex == 2)
            {
                Assert.Null(destination.ContainerSemanticElementId);
                Assert.Empty(fixture.Document.SemanticModel.ProfileAssignments);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DestinationContentGrowthKeepsCanonicalOriginAndOnlyReflowsLowerRows(int destinationIndex)
    {
        var fixture = CreateDestinationFixture(destinationIndex, 1);
        var documentBefore = fixture.Document;
        var before = fixture.Contribute(fixture.BaseItems);
        var beforePlan = Assert.IsType<Canvas2DSpatialPresentationPlan>(before.SpatialPresentationPlan);
        var originalDestination = Destination(beforePlan, destinationIndex);
        var expandedCanonicalBounds = new RectD(900d, 400d, 160d, 100d);
        var expandedItems = new[] { NodeItem(fixture.Graph.Nodes[0], expandedCanonicalBounds) };

        var expanded = fixture.Contribute(expandedItems);

        var expandedPlan = Assert.IsType<Canvas2DSpatialPresentationPlan>(expanded.SpatialPresentationPlan);
        var expandedDestination = Destination(expandedPlan, destinationIndex);
        Assert.Equal(originalDestination.Id, expandedDestination.Id);
        Assert.Equal(originalDestination.LocalToSceneTransform, expandedDestination.LocalToSceneTransform);
        Assert.Equal(1124d, expandedDestination.Bounds.Width);
        Assert.Equal(564d, expandedDestination.Bounds.Height);
        Assert.True(expandedDestination.Bounds.Contains(expandedDestination.MapLocalToScene(expandedCanonicalBounds)));
        Assert.Equal(PoolBackground(expanded, PoolAId).Bounds.Width,
            PoolBackground(expanded, PoolBId).Bounds.Width);

        var heightGrowth = expandedDestination.Bounds.Height - originalDestination.Bounds.Height;
        foreach (var previousRegion in beforePlan.Regions)
        {
            var currentRegion = expandedPlan.Regions.Single(region => region.Id == previousRegion.Id);
            var expectedTranslation = previousRegion.Bounds.Top > originalDestination.Bounds.Top
                ? new VectorD(0d, heightGrowth)
                : default;
            Assert.Equal(previousRegion.MapLocalToScene(new PointD(0d, 0d)) + expectedTranslation,
                currentRegion.MapLocalToScene(new PointD(0d, 0d)));
        }
        Assert.Equal(expanded, fixture.Contribute(expandedItems));
        Assert.Equal(expanded, fixture.Contribute(expandedItems.Concat(expanded.Items)));
        Assert.Equal(before, fixture.Contribute(fixture.BaseItems));
        Assert.Same(documentBefore, fixture.Document);
        Assert.Equal(documentBefore.VisualModel, fixture.Document.VisualModel);
        Assert.Equal(documentBefore.SemanticModel, fixture.Document.SemanticModel);
    }

    private static Canvas2DSpatialRegion Destination(Canvas2DSpatialPresentationPlan plan, int index) =>
        plan.Regions.Single(region => region.ContainerSemanticElementId == DestinationPoolId(index));

    private static SemanticElementId? DestinationPoolId(int index) =>
        index == 0 ? PoolAId : index == 1 ? PoolBId : null;

    private static DestinationFixture CreateDestinationFixture(int destinationIndex, int count)
    {
        var inputs = Canvas2DSceneTestData.CreateWithPersistentRoute().WithCrossingConnector();
        var graph = new ProjectedGraph(inputs.Graph.DocumentId, inputs.Graph.SourceRevision,
            inputs.Graph.Nodes.Take(count));
        var source = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        var semanticIds = graph.Nodes.Select(node => node.Source.SemanticElementId).ToHashSet();
        var visualIds = graph.Nodes.Select(node => node.Source.VisualStateId).ToHashSet();
        var poolId = DestinationPoolId(destinationIndex);
        var document = new DocumentSnapshot(
            new SemanticModelSnapshot(source.DocumentId, source.Revision,
                source.SemanticModel.Elements.Where(element => semanticIds.Contains(element.Id)).Concat(
                [
                    OrganizationalSemanticFactory.CreatePool(PoolAId, "Pool A"),
                    OrganizationalSemanticFactory.CreatePool(PoolBId, "Pool B"),
                ]),
                modelProfiles: new ModelProfileStateSnapshot([OrganizationalModelProfile.Id]),
                profileAssignments: poolId is null ? [] : semanticIds.Select(id => Assignment(id!, poolId))),
            new VisualModelSnapshot(source.DocumentId, source.Revision,
                source.VisualModel.VisualStates.Where(visual => visualIds.Contains(visual.Id)),
                [
                    new ModelProfileElementPresentationSnapshot(OrganizationalModelProfile.Id, PoolAId, 0),
                    new ModelProfileElementPresentationSnapshot(OrganizationalModelProfile.Id, PoolBId, 1),
                ]),
            source.Metadata);
        var registration = OrganizationalPoolSceneContributor.CreateRegistration(
            new OrganizationalElementEligibilityPolicy(static _ => true));
        var contributor = Assert.IsType<OrganizationalPoolSceneContributor>(registration.Contributor);
        var items = graph.Nodes.Select((node, index) =>
            NodeItem(node, new RectD(40d + (index * 80d), 30d, 36d, 36d))).ToArray();
        return new DestinationFixture(inputs, graph, document, registration.Descriptor, contributor, items);
    }

    private sealed record DestinationFixture(
        Canvas2DSceneTestData Inputs,
        ProjectedGraph Graph,
        DocumentSnapshot Document,
        Canvas2DSceneContributorDescriptor Descriptor,
        OrganizationalPoolSceneContributor Contributor,
        Canvas2DSceneItem[] BaseItems)
    {
        internal Canvas2DSceneContribution Contribute(IEnumerable<Canvas2DSceneItem> baseItems)
        {
            var context = new Canvas2DSceneContributionContext(
                Graph, Inputs.Layout, Inputs.Routing, Document.VisualModel,
                EditorStateSnapshot.Empty, Canvas2DSceneConfiguration.Default, Descriptor,
                new Canvas2DScenePresentationContext(Document, Document.SemanticModel.RootScopeId,
                    ModelProfileViewStateSnapshot.Empty, baseItems));
            var result = Contributor.Contribute(context);
            Assert.True(result.Succeeded);
            return Assert.IsType<Canvas2DSceneContribution>(result.Contribution);
        }
    }
}
