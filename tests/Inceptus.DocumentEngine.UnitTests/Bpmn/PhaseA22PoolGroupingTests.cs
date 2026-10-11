using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Organizational.Spatial;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class PhaseA22PoolGroupingTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void GroupsByAuthoredPositionThenIdentityAndRetainsUnrelatedRegions(int count)
    {
        var pools = Enumerable.Range(0, count).Select(index => Pool("lane" + index, "company")).Append(Pool("other", null)).ToArray();
        var regions = Enumerable.Range(0, count).Select(index => Region("lane" + index, 40 + index * 220, 180))
            .Append(Region("other", 1600, 190)).Reverse().ToArray();
        var after = OrganizationalParticipantGrouping.Normalize(pools.Reverse(), regions);
        for (var index = 0; index < count; index++)
        {
            var lane = after.Single(region => region.ContainerSemanticElementId == new SemanticElementId("lane" + index));
            Assert.Equal(40 + index * 180, lane.ContentBounds!.Value.Top);
            Assert.Equal(180, lane.ExpandedHeight);
        }
        Assert.Equal(regions.Single(region => region.ContainerSemanticElementId == new SemanticElementId("other")),
            after.Single(region => region.ContainerSemanticElementId == new SemanticElementId("other")));
        Assert.Equal(after, OrganizationalParticipantGrouping.Normalize(pools, after));
    }

    [Fact]
    public void JoiningSeparatedPoolsMovesOnlyTheGroupAndDoesNotCoverAnUnrelatedPool()
    {
        SemanticElementSnapshot[] pools = [Pool("one", "company"), Pool("two", "company"), Pool("other", null)];
        SpatialRegionGeometrySnapshot[] regions = [Region("one", 40, 180), Region("other", 260, 180), Region("two", 480, 180)];
        var after = OrganizationalParticipantGrouping.Normalize(pools, regions);
        Assert.Equal(regions[1], after[1]);
        Assert.Equal(after[0].ContentBounds!.Value.Bottom, after[2].ContentBounds!.Value.Top);
        Assert.True(after[0].ContentBounds!.Value.Top >= after[1].ContentBounds!.Value.Bottom);
    }

    [Fact]
    public void GeometryNeverAssignsParticipantAndStableIdentityBreaksEqualPositionTies()
    {
        SemanticElementSnapshot[] pools = [Pool("b", "company"), Pool("a", "company"), Pool("none", null)];
        SpatialRegionGeometrySnapshot[] regions = [Region("b", 40, 180), Region("a", 40, 180), Region("none", 900, 180)];
        var after = OrganizationalParticipantGrouping.Normalize(pools, regions);
        Assert.Equal(40, after[1].ContentBounds!.Value.Top);
        Assert.Equal(220, after[0].ContentBounds!.Value.Top);
        Assert.Null(OrganizationalSemantics.GetParticipantId(pools[2]));
        Assert.Equal(regions[2], after[2]);
    }

    [Fact]
    public void NormalizationOnlyChangesExplicitlyAffectedParticipantGroups()
    {
        SemanticElementSnapshot[] pools = [Pool("a1", "a"), Pool("a2", "a"), Pool("b1", "b"), Pool("b2", "b")];
        SpatialRegionGeometrySnapshot[] regions = [Region("a1", 40, 180), Region("a2", 260, 180),
            Region("b1", 900, 180), Region("b2", 1120, 180)];
        var after = OrganizationalParticipantGrouping.Normalize(pools, regions, new HashSet<SemanticElementId> { new("a") });
        Assert.Equal(after[0].ContentBounds!.Value.Bottom, after[1].ContentBounds!.Value.Top);
        Assert.Equal(regions[2], after[2]);
        Assert.Equal(regions[3], after[3]);
    }

    [Fact]
    public void ExpandingNeighborRepairsOnlyOverlappingGroupAndPreservesTheNeighbor()
    {
        SemanticElementSnapshot[] pools = [Pool("a1", "a"), Pool("a2", "a"), Pool("neighbor", null), Pool("untouched", "b")];
        SpatialRegionGeometrySnapshot[] regions = [Region("a1", 40, 180), Region("a2", 220, 180),
            Region("neighbor", 0, 100), Region("untouched", 1200, 180)];
        var after = OrganizationalParticipantGrouping.Normalize(pools, regions, new HashSet<SemanticElementId>());
        Assert.Equal(140, after[0].ContentBounds!.Value.Top);
        Assert.Equal(after[0].ContentBounds!.Value.Bottom, after[1].ContentBounds!.Value.Top);
        Assert.Equal(regions[2], after[2]);
        Assert.Equal(regions[3], after[3]);
    }

    private static SemanticElementSnapshot Pool(string id, string? participant) => new(new(id), OrganizationalSemanticTypes.Pool,
        participant is null ? [] : [new(OrganizationalSemanticProperties.ParticipantId, PropertyValue.FromText(participant))]);
    private static SpatialRegionGeometrySnapshot Region(string id, double top, double height) =>
        new(new("region:" + id), OrganizationalModelProfile.Id, new(id), height,
            Matrix2D.CreateTranslation(110, top + 32), new RectD(78, top, 520, height));
}
