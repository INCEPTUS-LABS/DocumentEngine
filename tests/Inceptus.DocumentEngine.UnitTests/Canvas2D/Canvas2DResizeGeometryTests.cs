using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DResizeGeometryTests
{
    public static IEnumerable<object[]> DirectionCases =>
    [
        [Canvas2DResizeDirection.NorthWest, new RectD(15d, 27d, 95d, 43d), "nwse-resize"],
        [Canvas2DResizeDirection.North, new RectD(10d, 27d, 100d, 43d), "ns-resize"],
        [Canvas2DResizeDirection.NorthEast, new RectD(10d, 27d, 105d, 43d), "nesw-resize"],
        [Canvas2DResizeDirection.East, new RectD(10d, 20d, 105d, 50d), "ew-resize"],
        [Canvas2DResizeDirection.SouthEast, new RectD(10d, 20d, 105d, 57d), "nwse-resize"],
        [Canvas2DResizeDirection.South, new RectD(10d, 20d, 100d, 57d), "ns-resize"],
        [Canvas2DResizeDirection.SouthWest, new RectD(15d, 20d, 95d, 57d), "nesw-resize"],
        [Canvas2DResizeDirection.West, new RectD(15d, 20d, 95d, 50d), "ew-resize"],
    ];

    [Theory]
    [MemberData(nameof(DirectionCases))]
    internal void EveryDirectionPreservesOppositeEdgesAndMapsStableCursor(
        Canvas2DResizeDirection direction,
        RectD expected,
        string expectedCursor)
    {
        var actual = Canvas2DResizeGeometry.CalculateBounds(
            new RectD(10d, 20d, 100d, 50d),
            new VectorD(5d, 7d),
            direction);

        Assert.Equal(expected, actual);
        Assert.Equal(expectedCursor, Canvas2DResizeGeometry.CssCursor(direction));
        Assert.True(Canvas2DResizeGeometry.TryParseRole(
            Canvas2DResizeGeometry.Role(direction),
            out var parsed));
        Assert.Equal(direction, parsed);
    }

    [Theory]
    [InlineData(Canvas2DResizeDirection.NorthWest, 1000d, 1000d, 109d, 69d, 1d, 1d)]
    [InlineData(Canvas2DResizeDirection.North, 0d, 1000d, 10d, 69d, 100d, 1d)]
    [InlineData(Canvas2DResizeDirection.NorthEast, -1000d, 1000d, 10d, 69d, 1d, 1d)]
    [InlineData(Canvas2DResizeDirection.East, -1000d, 0d, 10d, 20d, 1d, 50d)]
    [InlineData(Canvas2DResizeDirection.SouthEast, -1000d, -1000d, 10d, 20d, 1d, 1d)]
    [InlineData(Canvas2DResizeDirection.South, 0d, -1000d, 10d, 20d, 100d, 1d)]
    [InlineData(Canvas2DResizeDirection.SouthWest, 1000d, -1000d, 109d, 20d, 1d, 1d)]
    [InlineData(Canvas2DResizeDirection.West, 1000d, 0d, 109d, 20d, 1d, 50d)]
    internal void EveryDirectionClampsAtSharedMinimumWithoutCrossingOppositeEdge(
        Canvas2DResizeDirection direction,
        double deltaX,
        double deltaY,
        double x,
        double y,
        double width,
        double height)
    {
        var actual = Canvas2DResizeGeometry.CalculateBounds(
            new RectD(10d, 20d, 100d, 50d),
            new VectorD(deltaX, deltaY),
            direction);

        Assert.Equal(new RectD(x, y, width, height), actual);
    }

    [Fact]
    public void CornersAndEdgesUseErgonomicInteractionBoundsWithCornerPrecedence()
    {
        var bounds = new RectD(10d, 20d, 100d, 50d);

        Assert.Equal(new RectD(5d, 15d, 10d, 10d),
            Canvas2DResizeGeometry.InteractionBounds(bounds, Canvas2DResizeDirection.NorthWest));
        Assert.Equal(new RectD(10d, 16d, 100d, 8d),
            Canvas2DResizeGeometry.InteractionBounds(bounds, Canvas2DResizeDirection.North));
        Assert.Equal(new RectD(106d, 20d, 8d, 50d),
            Canvas2DResizeGeometry.InteractionBounds(bounds, Canvas2DResizeDirection.East));
        Assert.True(Canvas2DResizeGeometry.ZIndex(Canvas2DResizeDirection.NorthWest) >
            Canvas2DResizeGeometry.ZIndex(Canvas2DResizeDirection.North));
        Assert.True(Canvas2DResizeGeometry.ZIndex(Canvas2DResizeDirection.SouthEast) >
            Canvas2DResizeGeometry.ZIndex(Canvas2DResizeDirection.East));
    }

    [Fact]
    public void MinimumBoundsAdaptInteractionsWithoutCoveringTheInterior()
    {
        var minimum = Canvas2DInteractionController.MinimumVisualExtent;
        var bounds = new RectD(10d, 20d, minimum, minimum);
        var center = new PointD(10d + (minimum / 2d), 20d + (minimum / 2d));

        Assert.Equal(1d, minimum);

        foreach (var direction in Canvas2DResizeGeometry.Directions)
        {
            var interaction = Canvas2DResizeGeometry.InteractionBounds(bounds, direction);
            Assert.False(interaction.IsEmpty);
            Assert.False(interaction.Contains(center));
            if (Canvas2DResizeGeometry.IsCorner(direction))
            {
                Assert.Equal(new SizeD(0.5d, 0.5d), interaction.Size);
            }
        }

        Assert.Equal(
            new RectD(10d, 19.75d, 1d, 0.5d),
            Canvas2DResizeGeometry.InteractionBounds(bounds, Canvas2DResizeDirection.North));
        Assert.Equal(
            new RectD(10.75d, 20d, 0.5d, 1d),
            Canvas2DResizeGeometry.InteractionBounds(bounds, Canvas2DResizeDirection.East));
    }

    [Fact]
    public void NorthWestAdjustmentMovesAnchorAndScalesRelatedBounds()
    {
        var original = new RectD(10d, 20d, 100d, 50d);
        var resized = new RectD(0d, 5d, 110d, 65d);
        var related = new RectD(20d, 30d, 40d, 10d);
        var expected = new RectD(11d, 18d, 44d, 13d);

        Assert.Equal(expected, Canvas2DResizeGeometry.ScaleBounds(related, original, resized));
        Assert.Equal(
            expected.TopLeft,
            Canvas2DResizeGeometry.CreateAdjustment(original, resized)
                .TransformPoint(related.TopLeft));
    }

    [Fact]
    public void CallerSpecificMinimumsClampEachAxisWithoutDirectionFlipping()
    {
        var original = new RectD(10d, 20d, 100d, 50d);

        Assert.Equal(
            new RectD(85d, 55d, 25d, 15d),
            Canvas2DResizeGeometry.CalculateBounds(
                original,
                new VectorD(1000d, 1000d),
                Canvas2DResizeDirection.NorthWest,
                minimumWidth: 25d,
                minimumHeight: 15d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Canvas2DResizeGeometry.CalculateBounds(
                original,
                default,
                Canvas2DResizeDirection.East,
                minimumWidth: 0d,
                minimumHeight: 1d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Canvas2DResizeGeometry.CalculateBounds(
                original,
                default,
                Canvas2DResizeDirection.South,
                minimumWidth: 1d,
                minimumHeight: double.NaN));
    }
}
