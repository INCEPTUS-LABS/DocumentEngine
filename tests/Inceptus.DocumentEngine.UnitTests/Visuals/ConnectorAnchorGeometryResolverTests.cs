using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Visuals;

public sealed class ConnectorAnchorGeometryResolverTests
{
    public static TheoryData<int, double[]> Distributions => new()
    {
        { 0, [] },
        { 1, [0.5d] },
        { 2, [1d / 3d, 2d / 3d] },
        { 3, [0.25d, 0.5d, 0.75d] },
        { 4, [0.2d, 0.4d, 0.6d, 0.8d] },
    };

    [Theory]
    [MemberData(nameof(Distributions))]
    public void DistributionUsesOneCanonicalEvenSpacingRule(int count, double[] expected)
    {
        Assert.Equal(expected, ConnectorAnchorGeometryResolver.Distribute(count));
    }

    [Theory]
    [InlineData(ConnectorAnchorSide.Top, 150d, 200d)]
    [InlineData(ConnectorAnchorSide.Right, 300d, 225d)]
    [InlineData(ConnectorAnchorSide.Bottom, 150d, 300d)]
    [InlineData(ConnectorAnchorSide.Left, 100d, 225d)]
    public void EverySideUsesCanonicalUserFacingOrientation(
        ConnectorAnchorSide side,
        double expectedX,
        double expectedY)
    {
        var point = ConnectorAnchorGeometryResolver.ResolvePoint(
            new RectD(100d, 200d, 200d, 100d),
            side,
            order: 0,
            count: 3);

        Assert.Equal(new PointD(expectedX, expectedY), point);
    }

    [Fact]
    public void MixedRolesUseOneSharedOrderAndClickParameterDeterminesInsertionIndex()
    {
        ConnectorAnchor[] anchors =
        [
            Anchor("a1", ConnectorAnchorRole.Source, 0),
            Anchor("a2", ConnectorAnchorRole.Target, 1),
            Anchor("a3", ConnectorAnchorRole.Source, 2),
        ];

        Assert.Equal(0, ConnectorAnchorGeometryResolver.ResolveInsertionIndex(
            anchors, ConnectorAnchorSide.Right, 0.10d));
        Assert.Equal(1, ConnectorAnchorGeometryResolver.ResolveInsertionIndex(
            anchors, ConnectorAnchorSide.Right, 0.40d));
        Assert.Equal(2, ConnectorAnchorGeometryResolver.ResolveInsertionIndex(
            anchors, ConnectorAnchorSide.Right, 0.60d));
        Assert.Equal(3, ConnectorAnchorGeometryResolver.ResolveInsertionIndex(
            anchors, ConnectorAnchorSide.Right, 0.90d));
        Assert.Equal(2, ConnectorAnchorGeometryResolver.ResolveInsertionIndex(
            anchors, ConnectorAnchorSide.Right, 0.50d));
    }

    [Theory]
    [InlineData(ConnectorAnchorSide.Top, 200d, 200d, 0.5d)]
    [InlineData(ConnectorAnchorSide.Bottom, 300d, 300d, 1d)]
    [InlineData(ConnectorAnchorSide.Left, 100d, 225d, 0.25d)]
    [InlineData(ConnectorAnchorSide.Right, 300d, 175d, 0d)]
    public void EdgeParameterUsesDocumentGeometryAndClampsSafely(
        ConnectorAnchorSide side,
        double x,
        double y,
        double expected)
    {
        var parameter = ConnectorAnchorGeometryResolver.ResolveEdgeParameter(
            new RectD(100d, 200d, 200d, 100d),
            side,
            new PointD(x, y));

        Assert.Equal(expected, parameter);
    }

    [Fact]
    public void ZeroExtentAndInvalidInputsRemainDeterministic()
    {
        Assert.Equal(0.5d, ConnectorAnchorGeometryResolver.ResolveEdgeParameter(
            new RectD(10d, 20d, 0d, 0d),
            ConnectorAnchorSide.Top,
            new PointD(10d, 20d)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConnectorAnchorGeometryResolver.Distribute(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConnectorAnchorGeometryResolver.ResolveInsertionIndex(
                [], ConnectorAnchorSide.Right, double.NaN));
    }

    [Fact]
    public void MoveAndResizeChangeOnlyDerivedPointNotPersistentAnchor()
    {
        var anchor = Anchor("stable", ConnectorAnchorRole.Target, 0);

        var original = ConnectorAnchorGeometryResolver.ResolvePoint(
            new RectD(100d, 200d, 200d, 100d), anchor.Side, anchor.Order, 1);
        var moved = ConnectorAnchorGeometryResolver.ResolvePoint(
            new RectD(140d, 250d, 200d, 100d), anchor.Side, anchor.Order, 1);
        var resized = ConnectorAnchorGeometryResolver.ResolvePoint(
            new RectD(100d, 200d, 300d, 160d), anchor.Side, anchor.Order, 1);

        Assert.Equal(new PointD(300d, 250d), original);
        Assert.Equal(new PointD(340d, 300d), moved);
        Assert.Equal(new PointD(400d, 280d), resized);
        Assert.Equal(new ConnectorAnchorId("stable"), anchor.Id);
        Assert.Equal(0, anchor.Order);
    }

    private static ConnectorAnchor Anchor(
        string id,
        ConnectorAnchorRole role,
        int order) =>
        new(new ConnectorAnchorId(id), ConnectorAnchorSide.Right, role, order);
}
