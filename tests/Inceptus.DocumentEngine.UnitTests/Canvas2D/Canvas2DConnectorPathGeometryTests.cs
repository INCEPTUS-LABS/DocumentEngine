using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DConnectorPathGeometryTests
{
    [Theory]
    [InlineData(0d, 0d, 0d)]
    [InlineData(0.25d, 10d, 0d)]
    [InlineData(0.5d, 10d, 10d)]
    [InlineData(0.75d, 10d, 20d)]
    [InlineData(1d, 10d, 30d)]
    [InlineData(-1d, 0d, 0d)]
    [InlineData(2d, 10d, 30d)]
    internal void ResolvePointUsesClampedTotalPolylineLength(
        double pathPosition,
        double expectedX,
        double expectedY)
    {
        PointD[] path =
        [
            new PointD(0d, 0d),
            new PointD(10d, 0d),
            new PointD(10d, 30d),
        ];

        var point = Canvas2DConnectorPathGeometry.ResolvePoint(path, pathPosition);

        Assert.Equal(new PointD(expectedX, expectedY), point);
    }

    [Fact]
    internal void FindNearestReturnsNormalizedLengthRoutePointAndLogicalOffset()
    {
        PointD[] path =
        [
            new PointD(0d, 0d),
            new PointD(10d, 0d),
            new PointD(10d, 30d),
        ];

        var projection = Canvas2DConnectorPathGeometry.FindNearest(
            path,
            new PointD(14d, 12d));

        Assert.Equal(0.55d, projection.PathPosition, 12);
        Assert.Equal(new PointD(10d, 12d), projection.RoutePoint);
        Assert.Equal(new VectorD(4d, 0d), projection.Offset);
        Assert.Equal(1, projection.SegmentIndex);
    }

    [Fact]
    internal void DegeneratePathRemainsFiniteAndDeterministic()
    {
        PointD[] path = [new PointD(3d, 4d), new PointD(3d, 4d)];

        var point = Canvas2DConnectorPathGeometry.ResolvePoint(path, 0.5d);
        var projection = Canvas2DConnectorPathGeometry.FindNearest(
            path,
            new PointD(8d, 10d));

        Assert.Equal(new PointD(3d, 4d), point);
        Assert.Equal(0d, projection.PathPosition);
        Assert.Equal(new PointD(3d, 4d), projection.RoutePoint);
        Assert.Equal(new VectorD(5d, 6d), projection.Offset);
        Assert.Equal(0, projection.SegmentIndex);
    }

    [Fact]
    internal void FindNearestReturnsDeterministicFirstSegmentOnEqualDistanceTie()
    {
        PointD[] path =
        [
            new PointD(0d, 0d),
            new PointD(10d, 0d),
            new PointD(10d, 10d),
        ];

        var projection = Canvas2DConnectorPathGeometry.FindNearest(
            path,
            new PointD(9d, 1d));

        Assert.Equal(0, projection.SegmentIndex);
        Assert.Equal(new PointD(9d, 0d), projection.RoutePoint);
    }
}
