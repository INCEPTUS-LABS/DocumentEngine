using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DConnectorLabelResolverTests
{
    [Fact]
    public void NearbyTurningBranchesPlaceTextOnTheirOwnOutwardSidesBeforeTheBend()
    {
        var label = Label();
        label = new ProjectedLabel(label.Source, label.OwnerId, label.Text,
            connectorPlacement: new ConnectorLabelPlacementIntent(32, 8));
        PointD[] upper = [new(100, 100), new(164, 100), new(164, 36), new(180, 36)];
        PointD[] lower = [new(100, 116), new(164, 116), new(164, 180), new(180, 180)];
        var a = Canvas2DConnectorLabelResolver.Resolve(label, upper, null, new SizeD(60, 16));
        var b = Canvas2DConnectorLabelResolver.Resolve(label, lower, null, new SizeD(60, 16));
        Assert.Equal(new PointD(132, 84), a);
        Assert.Equal(new PointD(132, 132), b);
        Assert.True(a.X + 30 < 164 && b.X + 30 < 164);
        Assert.True(a.Y + 8 < 100 && b.Y - 8 > 116);
    }

    [Fact]
    public void EarlyVerticalCrossRegionTurnPlacesTextBeyondTheAdjacentBranchLabel()
    {
        var label = Label();
        label = new ProjectedLabel(label.Source, label.OwnerId, label.Text,
            connectorPlacement: new ConnectorLabelPlacementIntent(32, 8));
        PointD[] path = [new(100, 100), new(116, 100), new(116, 600), new(200, 600)];
        var anchor = Canvas2DConnectorLabelResolver.Resolve(label, path, null, new SizeD(60, 16));
        Assert.Equal(new PointD(154, 116), anchor);
        Assert.True(anchor.Y - 8 > path[0].Y);
    }

    [Theory]
    [InlineData(100, 100, 500, 100, 148, 82)]
    [InlineData(100, 100, 100, 500, 143, 148)]
    [InlineData(500, 100, 100, 100, 452, 118)]
    [InlineData(100, 500, 100, 100, 57, 452)]
    [InlineData(100, 100, 110, 100, 105, 82)]
    [InlineData(100, 100, 100, 100, 100, 82)]
    [InlineData(0, 0, 400, 0, 48, 18)]
    [InlineData(0, 100, 0, 0, 43, 52)]
    public void NearSourceUsesArcLengthTangentMeasuredClearanceAndPositiveBoundary(
        double x1, double y1, double x2, double y2, double expectedX, double expectedY)
    {
        var anchor = Canvas2DConnectorLabelResolver.Resolve(Label(),
            [new PointD(x1, y1), new PointD(x2, y2)], null, new SizeD(70d, 20d));
        Assert.Equal(new PointD(expectedX, expectedY), anchor);
    }

    [Fact]
    public void MultiSegmentPathSkipsDegenerateSegmentsAndWalksFromSource()
    {
        PointD[] path = [new(100, 100), new(100, 100), new(120, 100), new(120, 300)];
        Assert.Equal(new PointD(163, 128),
            Canvas2DConnectorLabelResolver.Resolve(Label(), path, null, new SizeD(70, 20)));
    }

    [Fact]
    public void DiagonalTextBoxClearsStrokeByRequestedGap()
    {
        var anchor = Canvas2DConnectorLabelResolver.Resolve(Label(),
            [new(100, 100), new(500, 500)], null, new SizeD(70, 20));
        var distance = (anchor.X - anchor.Y) / Math.Sqrt(2);
        Assert.Equal(45 / Math.Sqrt(2) + 8, distance, 9);
    }

    [Fact]
    public void ExplicitOldDefaultOverridesNearSourceAndNullIntentRetainsMidpoint()
    {
        PointD[] path = [new(100, 100), new(500, 100)];
        var automatic = Canvas2DConnectorLabelResolver.Resolve(Label(), path, null, new SizeD(70, 20));
        var manual = Canvas2DConnectorLabelResolver.Resolve(Label(), path,
            ConnectorLabelPlacement.Default, new SizeD(70, 20));
        var generic = Canvas2DConnectorLabelResolver.Resolve(Label(false), path, null, new SizeD(70, 20));
        Assert.Equal(new PointD(148, 82), automatic);
        Assert.Equal(new PointD(300, 88), manual);
        Assert.Equal(manual, generic);
        Assert.NotEqual(Label(), Label(false));
        Assert.Equal(Label(), Label());
    }

    [Theory]
    [InlineData(-1, 8)]
    [InlineData(48, -1)]
    [InlineData(double.NaN, 8)]
    [InlineData(48, double.PositiveInfinity)]
    public void InvalidIntentIsRejected(double distance, double gap) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectorLabelPlacementIntent(distance, gap));

    private static ProjectedLabel Label(bool nearSource = true)
    {
        var edge = Assert.Single(Canvas2DSceneTestData.CreateWithPersistentRoute().Graph.Edges);
        return new ProjectedLabel(edge.Source, edge.Id, "Approved",
            connectorPlacement: nearSource ? new ConnectorLabelPlacementIntent(48, 8) : null);
    }
}
