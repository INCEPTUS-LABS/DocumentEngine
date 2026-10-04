using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DScopeGeometryContributorContractTests
{
    [Theory]
    [InlineData("compact")]
    [InlineData("profile")]
    [InlineData("container")]
    public void PersistentPreparationRejectsCompactOrForeignPresentationPlans(string invalidDimension)
    {
        var id = new Canvas2DSpatialRegionId("pool:A");
        var profile = new ModelProfileId("profile");
        var owner = new SemanticElementId("A");
        var bounds = new RectD(78, 40, 482, 200);
        var transform = Matrix2D.CreateTranslation(110, 72);
        var saved = new SpatialRegionGeometrySnapshot(id, profile, owner, 200, transform, bounds);
        var region = new Canvas2DSpatialRegion(id,
            invalidDimension == "profile" ? new ModelProfileId("foreign") : profile,
            invalidDimension == "container" ? new SemanticElementId("foreign") : owner,
            transform, bounds);
        var map = invalidDimension == "compact"
            ? new Canvas2DSpatialCoordinateMap([new(id, 40, 440, 40, 240)])
            : Canvas2DSpatialCoordinateMap.Identity;
        var plan = new Canvas2DSpatialPresentationPlan([region], [], Matrix2D.Identity, map, null);
        Assert.Throws<ArgumentException>(() => Canvas2DScopeGeometryPresentationResult.Success([saved], plan));
    }

    [Fact]
    public void DormantPreparedCapacityHasNoActivePlanAndPreservesItsAuthoredHeight()
    {
        var saved = new SpatialRegionGeometrySnapshot(new Canvas2DSpatialRegionId("unassigned"),
            new ModelProfileId("profile"), null, 374, null, null);
        var result = Canvas2DScopeGeometryPresentationResult.Success([saved]);
        Assert.True(result.Succeeded);
        Assert.Null(result.Plan);
        Assert.Same(saved, Assert.Single(result.Regions));
    }
}
