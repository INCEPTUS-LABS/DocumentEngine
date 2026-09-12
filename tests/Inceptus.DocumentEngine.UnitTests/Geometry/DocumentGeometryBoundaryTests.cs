using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.UnitTests.Geometry;

public sealed class DocumentGeometryBoundaryTests
{
    [Theory]
    [InlineData(0d, 0d, true)]
    [InlineData(1d, 1d, true)]
    [InlineData(-1d, 0d, false)]
    [InlineData(0d, -1d, false)]
    public void PointContainmentIncludesTheBoundary(
        double x,
        double y,
        bool expected) =>
        Assert.Equal(expected, DocumentGeometryBoundary.Contains(new PointD(x, y)));

    [Theory]
    [InlineData(0d, 0d, true)]
    [InlineData(1d, 1d, true)]
    [InlineData(-1d, 0d, false)]
    [InlineData(0d, -1d, false)]
    public void BoundsContainmentRequiresANonnegativeTopLeft(
        double x,
        double y,
        bool expected) =>
        Assert.Equal(
            expected,
            DocumentGeometryBoundary.Contains(new RectD(x, y, 20d, 10d)));

    [Fact]
    public void PointAndBoundsClampingPreserveLegalCoordinatesAndExtent()
    {
        Assert.Equal(
            new PointD(0d, 0d),
            DocumentGeometryBoundary.Clamp(new PointD(-12d, -8d)));
        Assert.Equal(
            new RectD(0d, 0d, 20d, 10d),
            DocumentGeometryBoundary.Clamp(new RectD(-12d, -8d, 20d, 10d)));
        Assert.Equal(
            new RectD(12d, 8d, 20d, 10d),
            DocumentGeometryBoundary.Clamp(new RectD(12d, 8d, 20d, 10d)));
    }

    [Fact]
    public void GroupTranslationUsesOneDeltaAndPreservesRelativeGeometry()
    {
        var requested = new VectorD(-30d, -40d);

        var actual = DocumentGeometryBoundary.ClampTranslation(
            [new RectD(10d, 15d, 20d, 10d), new RectD(50d, 70d, 30d, 20d)],
            requested);

        Assert.Equal(new VectorD(-10d, -15d), actual);
        Assert.Equal(new RectD(0d, 0d, 20d, 10d),
            new RectD(10d, 15d, 20d, 10d).Translate(actual));
        Assert.Equal(new RectD(40d, 55d, 30d, 20d),
            new RectD(50d, 70d, 30d, 20d).Translate(actual));
    }
}
