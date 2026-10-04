using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DSpatialCoordinateMapTests
{
    private static readonly Canvas2DSpatialRegionId PoolA = new("pool:A");
    private static readonly Canvas2DSpatialRegionId PoolB = new("pool:B");
    private static readonly Canvas2DSpatialRegionId Unassigned = new("unassigned");

    [Theory]
    [InlineData(20, 20)]
    [InlineData(40, 40)]
    [InlineData(240, 52.8)]
    [InlineData(440, 65.6)]
    [InlineData(460, 85.6)]
    [InlineData(592, 217.6)]
    [InlineData(872, 497.6)]
    [InlineData(1060, 685.6)]
    [InlineData(1500, 1125.6)]
    public void CompactRowsAndOverflowHaveOneReversibleMap(double logicalY, double displayedY)
    {
        var map = CollapseA();
        var logical = new PointD(123, logicalY);
        var displayed = map.MapLogicalToScene(logical);
        Assert.Equal(123, displayed.X);
        Assert.Equal(displayedY, displayed.Y, precision: 10);
        var restored = map.MapSceneToLogical(displayed);
        Assert.Equal(logical.X, restored.X);
        Assert.Equal(logical.Y, restored.Y, precision: 10);
    }

    [Fact]
    public void AutomaticSegmentsSplitWithoutMovingAuthoredVerticesOrChangingOrthogonality()
    {
        var source = new[] { new PointD(300, 20), new PointD(300, 500), new PointD(500, 500) };
        var original = source.ToArray();
        var map = CollapseA();
        var path = map.MapPath(source);
        Assert.Equal(original, source);
        Assert.Contains(new PointD(300, 40), path);
        Assert.Contains(new PointD(300, 65.6), path);
        Assert.Equal(map.MapLogicalToScene(source[0]), path[0]);
        Assert.Equal(map.MapLogicalToScene(source[^1]), path[^1]);
        Assert.All(path.Zip(path.Skip(1)), segment =>
            Assert.True(segment.First.X == segment.Second.X || segment.First.Y == segment.Second.Y));
        Assert.Equal(source, Canvas2DSpatialCoordinateMap.Identity.MapPath(source));
    }

    [Fact]
    public void ManualVertexMappingAndStraightEndpointsDoNotAcquireAutomaticBreakpoints()
    {
        var map = CollapseA();
        var source = new[] { new PointD(100, 20), new PointD(300, 240), new PointD(500, 900) };
        var displayed = source.Select(map.MapLogicalToScene).ToArray();
        var mapping = new Canvas2DConnectorPresentationMapping(source, displayed, map, PoolA, PoolB);
        Assert.Equal(Canvas2DConnectorPresentationSourceSpace.ScopeLogical, mapping.SourceSpace);
        Assert.Equal(3, mapping.DisplayedEditablePath.Length);
        Assert.True(mapping.TryMapDisplayedEditablePointToCanonical(1, displayed[1], out var logical));
        Assert.Equal(source[1], logical);
        Assert.Equal(source[1], mapping.MapSceneToLogical(displayed[1]));
        Assert.False(mapping.TryMapDisplayedEditablePointToCanonical(0, displayed[0], out _));
        Assert.False(mapping.TryGetCanonicalGuidanceToSceneTransform(out _));
        Assert.Throws<InvalidOperationException>(() => mapping.CanonicalGuidanceToSceneTransform);
        var straight = new Canvas2DConnectorPresentationMapping(
            [source[0], source[^1]], [displayed[0], displayed[^1]], map, PoolA, PoolB);
        Assert.Equal(2, straight.DisplayedEditablePath.Length);
    }

    [Fact]
    public void LegacyTranslationMappingRetainsItsOriginalContract()
    {
        var transform = Matrix2D.CreateTranslation(50, 80);
        var source = new[] { new PointD(10, 20), new PointD(30, 40), new PointD(80, 100) };
        var displayed = new[] { new PointD(900, 900), transform.TransformPoint(source[1]), new PointD(950, 950) };
        var mapping = new Canvas2DConnectorPresentationMapping(source, displayed, transform, PoolA, PoolB);
        Assert.Equal(Canvas2DConnectorPresentationSourceSpace.CanonicalProcess, mapping.SourceSpace);
        Assert.Equal(transform, mapping.CanonicalGuidanceToSceneTransform);
        Assert.True(mapping.TryGetCanonicalGuidanceToSceneTransform(out var actual));
        Assert.Equal(transform, actual);
        Assert.Equal(source[1], mapping.MapSceneToLogical(displayed[1]));
    }

    [Fact]
    public void NonuniformMappingsHaveStructuralEqualityAndRejectInconsistentVertices()
    {
        var source = new[] { new PointD(100, 240), new PointD(300, 900) };
        var map = CollapseA();
        var one = new Canvas2DConnectorPresentationMapping(source, source.Select(map.MapLogicalToScene), map, PoolA, PoolB);
        var two = new Canvas2DConnectorPresentationMapping(source, source.Select(map.MapLogicalToScene), CollapseA(), PoolA, PoolB);
        Assert.Equal(one, two);
        Assert.Equal(one.GetHashCode(), two.GetHashCode());
        Assert.Throws<ArgumentException>(() => new Canvas2DConnectorPresentationMapping(source, source, map, PoolA, PoolB));
    }

    [Fact]
    public void InvalidBandsCannotIntroduceDiscontinuityOverlapExpansionOrAmbiguousRegions()
    {
        Assert.Throws<ArgumentException>(() => new Canvas2DSpatialCoordinateBand(PoolA, 0, 10, 0, 11));
        Assert.Throws<ArgumentException>(() => new Canvas2DSpatialCoordinateBand(PoolA, 0, 10, 0, 0));
        Assert.Throws<ArgumentException>(() => new Canvas2DSpatialCoordinateMap([new(PoolA, 0, 10, 1, 5)]));
        Assert.Throws<ArgumentException>(() => new Canvas2DSpatialCoordinateMap([new(PoolA, 0, 10, 0, 5), new(PoolB, 9, 20, 5, 10)]));
        Assert.Throws<ArgumentException>(() => new Canvas2DSpatialCoordinateMap([new(PoolA, 0, 10, 0, 5), new(PoolB, 20, 30, 16, 20)]));
        Assert.Throws<ArgumentException>(() => new Canvas2DSpatialCoordinateMap([new(PoolA, 0, 10, 0, 5), new(PoolA, 20, 30, 15, 20)]));
    }

    [Fact]
    public void ManyCompactRowsRetainContinuousGapsDespiteFractionalHeightRounding()
    {
        var bands = new List<Canvas2DSpatialCoordinateBand>();
        var logicalTop = 40d;
        var displayedTop = 40d;
        for (var index = 0; index < 500; index++)
        {
            var displayedHeight = index % 2 == 0 ? 25.6d : 224d;
            bands.Add(new Canvas2DSpatialCoordinateBand(new Canvas2DSpatialRegionId($"row:{index}"),
                logicalTop, logicalTop + 224d, displayedTop, displayedTop + displayedHeight));
            logicalTop += 264d;
            displayedTop += displayedHeight + 40d;
        }
        var map = new Canvas2DSpatialCoordinateMap(bands);
        var below = new PointD(20d, logicalTop + 50d);
        Assert.Equal(40d + (250d * (25.6d + 224d + 80d)) + 50d,
            map.MapLogicalToScene(below).Y, precision: 6);
        Assert.Equal(below.Y, map.MapSceneToLogical(map.MapLogicalToScene(below)).Y, precision: 6);
    }

    [Fact]
    public void PlanUsesOnlyItsCurrentDeclaredUnassignedBottomAndIncludesMapInEquality()
    {
        var map = CollapseA();
        var regions = map.Bands.Select(band => new Canvas2DSpatialRegion(
            band.RegionId, new ModelProfileId("profile"), band.RegionId == Unassigned ? null : new SemanticElementId(band.RegionId.Value),
            Matrix2D.CreateTranslation(110, band.DisplayedTop + 32), new RectD(78, band.DisplayedTop, 482, band.DisplayedHeight))).ToArray();
        var plan = new Canvas2DSpatialPresentationPlan(regions, [], Matrix2D.Identity, map, Unassigned);
        Assert.Equal(Unassigned, plan.MovementBottomBoundaryRegionId);
        Assert.True(plan.TryGetRegion(Unassigned, out var boundary));
        Assert.Equal(685.6, boundary!.Bounds.Bottom, precision: 10);
        var old = new Canvas2DSpatialPresentationPlan(regions, [], Matrix2D.CreateTranslation(12, 34));
        Assert.Null(old.MovementBottomBoundaryRegionId);
        Assert.True(old.CoordinateMap.IsIdentity);
        Assert.Equal(new PointD(13, 36), old.MapCanonicalGuidanceToScene(new PointD(1, 2)));
        Assert.NotEqual(plan, old);
        Assert.Throws<ArgumentException>(() => new Canvas2DSpatialPresentationPlan(regions, [], Matrix2D.Identity, map, PoolA));
        Assert.Throws<ArgumentException>(() => new Canvas2DSpatialPresentationPlan(regions.Take(2), [], Matrix2D.Identity, map, Unassigned));
    }

    private static Canvas2DSpatialCoordinateMap CollapseA() => new([
        new(PoolA, 40, 440, 40, 65.6),
        new(PoolB, 480, 720, 105.6, 345.6),
        new(Unassigned, 760, 1060, 385.6, 685.6),
    ]);
}
