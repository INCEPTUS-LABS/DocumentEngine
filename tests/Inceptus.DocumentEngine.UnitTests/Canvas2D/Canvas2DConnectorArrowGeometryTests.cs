using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DConnectorArrowGeometryTests
{
    public static TheoryData<PointD[], PointD, PointD, PointD> Directions => new()
    {
        {
            [new PointD(0d, 0d), new PointD(20d, 0d)],
            new PointD(20d, 0d),
            new PointD(10d, 4d),
            new PointD(10d, -4d)
        },
        {
            [new PointD(20d, 0d), new PointD(0d, 0d)],
            new PointD(0d, 0d),
            new PointD(10d, -4d),
            new PointD(10d, 4d)
        },
        {
            [new PointD(0d, 0d), new PointD(0d, 20d)],
            new PointD(0d, 20d),
            new PointD(-4d, 10d),
            new PointD(4d, 10d)
        },
        {
            [new PointD(0d, 20d), new PointD(0d, 0d)],
            new PointD(0d, 0d),
            new PointD(4d, 10d),
            new PointD(-4d, 10d)
        },
    };

    [Theory]
    [MemberData(nameof(Directions))]
    internal void CreateUsesFinalSegmentAndKeepsTipAtTarget(
        PointD[] path,
        PointD expectedTip,
        PointD expectedFirstWing,
        PointD expectedSecondWing)
    {
        var arrow = Assert.IsType<Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneGeometry>(
            Canvas2DConnectorArrowGeometry.Create(path));

        Assert.True(arrow.IsClosed);
        Assert.Equal(expectedTip, arrow.Points[0]);
        Assert.Equal(expectedFirstWing, arrow.Points[1]);
        Assert.Equal(expectedSecondWing, arrow.Points[2]);
    }

    [Fact]
    internal void CreateUsesBentRouteFinalSegmentInsteadOfEndpointCentreVector()
    {
        PointD[] path =
        [
            new PointD(0d, 0d),
            new PointD(30d, 0d),
            new PointD(30d, 20d),
        ];

        var arrow = Assert.IsType<Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneGeometry>(
            Canvas2DConnectorArrowGeometry.Create(path));

        Assert.Equal(new PointD(30d, 20d), arrow.Points[0]);
        Assert.Equal(new PointD(26d, 10d), arrow.Points[1]);
        Assert.Equal(new PointD(34d, 10d), arrow.Points[2]);
    }

    [Fact]
    internal void CreateOrientsDiagonalArrowAlongFinalSegment()
    {
        PointD[] path = [new PointD(0d, 0d), new PointD(20d, 20d)];

        var arrow = Assert.IsType<Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneGeometry>(
            Canvas2DConnectorArrowGeometry.Create(path));
        var wingMidpoint = new PointD(
            (arrow.Points[1].X + arrow.Points[2].X) / 2d,
            (arrow.Points[1].Y + arrow.Points[2].Y) / 2d);
        var direction = arrow.Points[0] - wingMidpoint;

        Assert.Equal(new PointD(20d, 20d), arrow.Points[0]);
        Assert.Equal(direction.X, direction.Y, precision: 12);
        Assert.Equal(10d, Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y)), precision: 12);
    }

    [Fact]
    internal void CreateSearchesPastZeroLengthFinalSegments()
    {
        PointD[] path =
        [
            new PointD(5d, 5d),
            new PointD(25d, 5d),
            new PointD(25d, 5d),
            new PointD(25d, 5d),
        ];

        var arrow = Assert.IsType<Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneGeometry>(
            Canvas2DConnectorArrowGeometry.Create(path));

        Assert.Equal(new PointD(25d, 5d), arrow.Points[0]);
        Assert.Equal(new PointD(15d, 9d), arrow.Points[1]);
        Assert.Equal(new PointD(15d, 1d), arrow.Points[2]);
    }

    [Fact]
    internal void CreateReturnsNullWhenNoDirectionCanBeResolved()
    {
        Assert.Null(Canvas2DConnectorArrowGeometry.Create(
            [new PointD(3d, 4d), new PointD(3d, 4d), new PointD(3d, 4d)]));
        Assert.Null(Canvas2DConnectorArrowGeometry.Create([new PointD(3d, 4d)]));
    }

    [Fact]
    internal void ConfigurationUsesLogicalUnitsAndRejectsInvalidValues()
    {
        var configuration = new Canvas2DConnectorArrowConfiguration(12d, 5d);

        Assert.Equal(12d, configuration.Length);
        Assert.Equal(5d, configuration.HalfWidth);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Canvas2DConnectorArrowConfiguration(0d, 5d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Canvas2DConnectorArrowConfiguration(12d, double.NaN));
    }
}
