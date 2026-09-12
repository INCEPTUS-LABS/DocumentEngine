using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Visuals;

public sealed class ConnectorLabelPlacementTests
{
    [Fact]
    public void DefaultUsesRouteMidpointAndOneCentralizedLogicalOffset()
    {
        Assert.Equal(0.5d, ConnectorLabelPlacement.Default.PathPosition);
        Assert.Equal(new VectorD(0d, -12d), ConnectorLabelPlacement.Default.Offset);
    }

    [Theory]
    [InlineData(-1d, 0d)]
    [InlineData(0d, 0d)]
    [InlineData(0.25d, 0.25d)]
    [InlineData(0.5d, 0.5d)]
    [InlineData(0.75d, 0.75d)]
    [InlineData(1d, 1d)]
    [InlineData(2d, 1d)]
    public void PathPositionIsDeterministicallyClamped(double input, double expected)
    {
        var placement = new ConnectorLabelPlacement(input, new VectorD(3d, -4d));

        Assert.Equal(expected, placement.PathPosition);
        Assert.Equal(new VectorD(3d, -4d), placement.Offset);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFinitePathPositionIsRejected(double input)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ConnectorLabelPlacement(input, default));
    }

    [Fact]
    public void CompletePropertyTripletRoundTripsAndPreservesUnrelatedProperties()
    {
        var original = new PropertyMap(
        [
            new("test:retained", PropertyValue.FromText("value")),
        ]);
        var expected = new ConnectorLabelPlacement(0.75d, new VectorD(-3d, 8d));

        var updated = ConnectorLabelPlacement.UpdateProperties(original, expected);

        Assert.True(ConnectorLabelPlacement.TryRead(updated, out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal("value", updated["test:retained"].TextValue);
        Assert.Equal(expected, ConnectorLabelPlacement.Resolve(updated));

        var removed = ConnectorLabelPlacement.UpdateProperties(updated, null);
        Assert.False(ConnectorLabelPlacement.TryRead(removed, out _));
        Assert.Equal(ConnectorLabelPlacement.Default, ConnectorLabelPlacement.Resolve(removed));
        Assert.Equal("value", removed["test:retained"].TextValue);
    }

    [Fact]
    public void MissingPartialOrWrongKindPlacementResolvesToDefault()
    {
        var partial = new PropertyMap(
        [
            new(
                ConnectorLabelPlacement.PathPositionPropertyKey,
                PropertyValue.FromNumber(0.75d)),
        ]);
        var wrongKind = new PropertyMap(
        [
            new(
                ConnectorLabelPlacement.PathPositionPropertyKey,
                PropertyValue.FromText("0.75")),
            new(
                ConnectorLabelPlacement.OffsetXPropertyKey,
                PropertyValue.FromNumber(1d)),
            new(
                ConnectorLabelPlacement.OffsetYPropertyKey,
                PropertyValue.FromNumber(2d)),
        ]);

        Assert.False(ConnectorLabelPlacement.TryRead(PropertyMap.Empty, out _));
        Assert.False(ConnectorLabelPlacement.TryRead(partial, out _));
        Assert.False(ConnectorLabelPlacement.TryRead(wrongKind, out _));
        Assert.Equal(ConnectorLabelPlacement.Default, ConnectorLabelPlacement.Resolve(partial));
        Assert.Equal(ConnectorLabelPlacement.Default, ConnectorLabelPlacement.Resolve(wrongKind));
    }
}
