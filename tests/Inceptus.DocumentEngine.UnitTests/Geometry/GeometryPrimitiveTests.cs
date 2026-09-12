using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.UnitTests.Geometry;

public sealed class GeometryPrimitiveTests
{
    public static TheoryData<double> NonFiniteValues =>
        new()
        {
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity,
        };

    [Theory]
    [MemberData(nameof(NonFiniteValues))]
    public void CoordinatesAndDimensionsMustBeFinite(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointD(value, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorD(0d, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeD(value, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectD(0d, value, 1d, 1d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Matrix2D(1d, 0d, 0d, 1d, value, 0d));
    }

    [Fact]
    public void SizesAndRectanglesRejectNegativeDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeD(-1d, 1d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeD(1d, -1d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectD(0d, 0d, -1d, 1d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RectD(0d, 0d, 1d, -1d));

        Assert.True(new SizeD(0d, 1d).IsEmpty);
        Assert.True(new RectD(0d, 0d, 1d, 0d).IsEmpty);
    }

    [Fact]
    public void RectanglesRejectNonFiniteDerivedEdges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RectD(double.MaxValue, 0d, double.MaxValue, 1d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RectD(0d, double.MaxValue, 1d, double.MaxValue));
    }

    [Fact]
    public void PointsRectanglesAndVectorsTranslateWithoutMutation()
    {
        var point = new PointD(4d, 7d);
        var rectangle = new RectD(1d, 2d, 3d, 4d);
        var translation = new VectorD(-2d, 5d);

        Assert.Equal(new PointD(2d, 12d), point.Translate(translation));
        Assert.Equal(new RectD(-1d, 7d, 3d, 4d), rectangle.Translate(translation));
        Assert.Equal(new VectorD(3d, 4d), new PointD(5d, 6d) - new PointD(2d, 2d));
        Assert.Equal(new VectorD(2d, 6d), new VectorD(1d, 3d) * 2d);
        Assert.Equal(new PointD(4d, 7d), point);
        Assert.Equal(new RectD(1d, 2d, 3d, 4d), rectangle);
    }

    [Fact]
    public void GeometryOperationsRejectNonFiniteResults()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PointD(double.MaxValue, 0d) + new VectorD(double.MaxValue, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VectorD(double.MaxValue, 0d) * 2d);
    }

    [Fact]
    public void RectangleContainmentIncludesBoundaries()
    {
        var rectangle = new RectD(10d, 20d, 30d, 40d);

        Assert.True(rectangle.Contains(new PointD(10d, 20d)));
        Assert.True(rectangle.Contains(new PointD(40d, 60d)));
        Assert.True(rectangle.Contains(new RectD(10d, 20d, 30d, 40d)));
        Assert.True(rectangle.Contains(new RectD(15d, 25d, 5d, 5d)));
        Assert.False(rectangle.Contains(new PointD(40.0001d, 60d)));
        Assert.False(rectangle.Contains(new RectD(9d, 20d, 1d, 1d)));
    }

    [Fact]
    public void RectangleIntersectionRequiresPositiveArea()
    {
        var rectangle = new RectD(0d, 0d, 10d, 10d);
        var overlap = new RectD(5d, 3d, 10d, 10d);

        Assert.True(rectangle.Intersects(overlap));
        Assert.Equal(new RectD(5d, 3d, 5d, 7d), rectangle.Intersection(overlap));
        Assert.True(rectangle.Intersects(new RectD(2d, 2d, 2d, 2d)));
        Assert.False(rectangle.Intersects(new RectD(10d, 0d, 2d, 2d)));
        Assert.False(rectangle.Intersects(new RectD(10d, 10d, 2d, 2d)));
        Assert.False(rectangle.Intersects(new RectD(2d, 2d, 0d, 2d)));
        Assert.Null(rectangle.Intersection(new RectD(20d, 20d, 1d, 1d)));
    }

    [Fact]
    public void GeometryUsesExactValueEquality()
    {
        Assert.Equal(new PointD(1d, 2d), new PointD(1d, 2d));
        Assert.NotEqual(new PointD(1d, 2d), new PointD(1d, 2.0000001d));
        Assert.Equal(new SizeD(3d, 4d).GetHashCode(), new SizeD(3d, 4d).GetHashCode());
    }
}

