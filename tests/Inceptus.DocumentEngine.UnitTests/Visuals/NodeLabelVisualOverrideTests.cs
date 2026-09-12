using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Visuals;

public sealed class NodeLabelVisualOverrideTests
{
    [Fact]
    public void ConstructorPreservesLogicalCenterOffsetAndLayoutBoxSize()
    {
        var visualOverride = new NodeLabelVisualOverride(
            -24d,
            18d,
            NodeLabelVisualOverride.MinimumWidth,
            NodeLabelVisualOverride.MinimumHeight);

        Assert.Equal(-24d, visualOverride.OffsetX);
        Assert.Equal(18d, visualOverride.OffsetY);
        Assert.Equal(1d, visualOverride.Width);
        Assert.Equal(1d, visualOverride.Height);
    }

    [Theory]
    [InlineData(double.NaN, 0d, 10d, 10d)]
    [InlineData(double.PositiveInfinity, 0d, 10d, 10d)]
    [InlineData(0d, double.NegativeInfinity, 10d, 10d)]
    [InlineData(0d, 0d, double.NaN, 10d)]
    [InlineData(0d, 0d, 0.999d, 10d)]
    [InlineData(0d, 0d, 10d, double.PositiveInfinity)]
    [InlineData(0d, 0d, 10d, 0d)]
    public void NonFiniteOffsetsOrSubminimumDimensionsAreRejected(
        double offsetX,
        double offsetY,
        double width,
        double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NodeLabelVisualOverride(offsetX, offsetY, width, height));
    }

    [Fact]
    public void CompletePropertyQuartetRoundTripsAndRemovalPreservesUnrelatedProperties()
    {
        var original = new PropertyMap(
        [
            new("test:retained", PropertyValue.FromText("value")),
        ]);
        var expected = new NodeLabelVisualOverride(-12d, 28d, 160d, 36d);

        var updated = NodeLabelVisualOverride.UpdateProperties(original, expected);

        Assert.True(NodeLabelVisualOverride.TryRead(updated, out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal("value", updated["test:retained"].TextValue);

        var removed = NodeLabelVisualOverride.UpdateProperties(updated, visualOverride: null);
        Assert.False(NodeLabelVisualOverride.TryRead(removed, out _));
        Assert.Equal("value", removed["test:retained"].TextValue);
        Assert.Single(removed);
    }

    [Fact]
    public void MissingPartialWrongKindOrSubminimumOverrideCannotBeRead()
    {
        var partial = new PropertyMap(
        [
            new(
                NodeLabelVisualOverride.OffsetXPropertyKey,
                PropertyValue.FromNumber(2d)),
        ]);
        var wrongKind = CompleteProperties(
            PropertyValue.FromText("2"),
            PropertyValue.FromNumber(3d),
            PropertyValue.FromNumber(80d),
            PropertyValue.FromNumber(20d));
        var subminimum = CompleteProperties(
            PropertyValue.FromNumber(2d),
            PropertyValue.FromNumber(3d),
            PropertyValue.FromNumber(0.5d),
            PropertyValue.FromNumber(20d));

        Assert.False(NodeLabelVisualOverride.TryRead(PropertyMap.Empty, out _));
        Assert.False(NodeLabelVisualOverride.TryRead(partial, out _));
        Assert.False(NodeLabelVisualOverride.TryRead(wrongKind, out _));
        Assert.False(NodeLabelVisualOverride.TryRead(subminimum, out _));
    }

    [Fact]
    public void EqualityIncludesEveryOffsetAndDimension()
    {
        var expected = new NodeLabelVisualOverride(1d, 2d, 80d, 20d);

        Assert.Equal(expected, new NodeLabelVisualOverride(1d, 2d, 80d, 20d));
        Assert.NotEqual(expected, new NodeLabelVisualOverride(2d, 2d, 80d, 20d));
        Assert.NotEqual(expected, new NodeLabelVisualOverride(1d, 3d, 80d, 20d));
        Assert.NotEqual(expected, new NodeLabelVisualOverride(1d, 2d, 81d, 20d));
        Assert.NotEqual(expected, new NodeLabelVisualOverride(1d, 2d, 80d, 21d));
    }

    private static PropertyMap CompleteProperties(
        PropertyValue offsetX,
        PropertyValue offsetY,
        PropertyValue width,
        PropertyValue height) =>
        new(
        [
            new(NodeLabelVisualOverride.OffsetXPropertyKey, offsetX),
            new(NodeLabelVisualOverride.OffsetYPropertyKey, offsetY),
            new(NodeLabelVisualOverride.WidthPropertyKey, width),
            new(NodeLabelVisualOverride.HeightPropertyKey, height),
        ]);
}
