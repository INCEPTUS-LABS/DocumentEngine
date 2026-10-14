using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class GridGeometryTests
{
    [Fact]
    public void DefaultsAndIndependentFlags()
    {
        var settings = new GridSettings();
        Assert.True(settings.ShowGrid);
        Assert.True(settings.SnapToGrid);
        Assert.Equal(20, settings.PointDistance);
        Assert.Equal(new PointD(20, 40), GridGeometry.Snap(new(23, 39), settings with { ShowGrid = false }));
        Assert.Equal(new PointD(23, 39), GridGeometry.Snap(new(23, 39), settings with { SnapToGrid = false }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RejectsInvalidSpacing(double distance) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridSettings { PointDistance = distance });

    [Theory]
    [InlineData(10, 20, 20)]
    [InlineData(-10, 20, -20)]
    [InlineData(30, 20, 40)]
    [InlineData(-30, 20, -40)]
    [InlineData(9.999, 20, 0)]
    [InlineData(-9.999, 20, 0)]
    [InlineData(3.75, 2.5, 5)]
    [InlineData(-3.75, 2.5, -5)]
    public void SnapUsesAwayFromZeroMidpoints(double value, double distance, double expected) =>
        Assert.Equal(new PointD(expected, expected), GridGeometry.Snap(new(value, value), new() { PointDistance = distance }));

    [Theory]
    [InlineData(20)]
    [InlineData(12.5)]
    [InlineData(40)]
    public void PatternIsOriginAnchoredAndCulledToVisibleDiagramRectangle(double distance)
    {
        var viewport = new ViewportSnapshot(1, new(33, -17), new RectD(-47, -31, 700, 450));
        var pattern = Canvas2DPointPattern.Create(new() { PointDistance = distance }, viewport)!;
        Assert.Equal(Math.Ceiling(-47 / distance) * distance, pattern.X);
        Assert.Equal(Math.Ceiling(-31 / distance) * distance, pattern.Y);
        Assert.Equal(distance, pattern.Step);
        Assert.InRange(pattern.X + (pattern.Columns - 1) * pattern.Step, -47, 653);
        Assert.InRange(pattern.Y + (pattern.Rows - 1) * pattern.Step, -31, 419);
        Assert.Null(Canvas2DPointPattern.Create(new() { ShowGrid = false }, viewport));
        Assert.Equal(pattern, Canvas2DPointPattern.Create(new() { PointDistance = distance, SnapToGrid = false }, viewport));
    }

    [Theory]
    [InlineData(.01, 20)]
    [InlineData(.1, .000001)]
    [InlineData(5, 20)]
    [InlineData(1, double.Epsilon)]
    public void SamplingIsBoundedWithoutChangingSnapCoordinates(double zoom, double spacing)
    {
        var grid = new GridSettings { PointDistance = spacing };
        var snappedBeforeRendering = GridGeometry.Snap(new(33, -47), grid);
        var viewport = new ViewportSnapshot(zoom, default, new RectD(-500 / zoom, -300 / zoom, 1200 / zoom, 800 / zoom));
        var pattern = Canvas2DPointPattern.Create(grid, viewport);
        Assert.NotNull(pattern);
        Assert.InRange(pattern.Rows * pattern.Columns, 1, Canvas2DPointPattern.MaximumPoints);
        Assert.True(double.IsFinite(pattern.Step));
        Assert.Equal(spacing, grid.PointDistance);
        Assert.Equal(snappedBeforeRendering, GridGeometry.Snap(new(33, -47), grid));
    }
}
