using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.UnitTests.Canvas2D;

namespace Inceptus.DocumentEngine.UnitTests.Organizational;

public sealed class OrganizationalPoolConnectorPresentationRouterTests
{
    private static readonly RectD UpperRightNode =
        new(291.2d, 578.725d, 120d, 80d);
    private static readonly RectD LowerLeftNode =
        new(110d, 762.725d, 120d, 80d);

    [Fact]
    public void RightSourceToLeftTargetBelowLeftEscapesBothEndpointOwners()
    {
        var source = new PointD(UpperRightNode.Right, 618.725d);
        var target = new PointD(LowerLeftNode.Left, 802.725d);

        var route = Route(
            source,
            target,
            source + new VectorD(20d, 0d),
            target + new VectorD(-20d, 0d));

        Assert.Equal(source, route.DisplayedLogicalPath[0]);
        Assert.Equal(target, route.DisplayedLogicalPath[^1]);
        Assert.Equal(source + new VectorD(16d, 0d), route.DisplayedLogicalPath[1]);
        Assert.Equal(target + new VectorD(-16d, 0d), route.DisplayedLogicalPath[^2]);
        AssertObstacleFreeOrthogonal(route.DisplayedLogicalPath);
    }

    [Fact]
    public void LeftSourceToRightTargetAboveRightEscapesBothEndpointOwners()
    {
        var source = new PointD(LowerLeftNode.Left, 802.725d);
        var target = new PointD(UpperRightNode.Right, 618.725d);

        var route = Route(
            source,
            target,
            source + new VectorD(-20d, 0d),
            target + new VectorD(20d, 0d));

        Assert.Equal(source, route.DisplayedLogicalPath[0]);
        Assert.Equal(target, route.DisplayedLogicalPath[^1]);
        Assert.Equal(source + new VectorD(-16d, 0d), route.DisplayedLogicalPath[1]);
        Assert.Equal(target + new VectorD(16d, 0d), route.DisplayedLogicalPath[^2]);
        AssertObstacleFreeOrthogonal(route.DisplayedLogicalPath);
    }

    private static Canvas2DConnectorPresentationRoute Route(
        PointD source,
        PointD target,
        PointD canonicalSourceTangent,
        PointD canonicalTargetPredecessor)
    {
        var edge = Assert.Single(Canvas2DSceneTestData.Create().Graph.Edges);
        var sourceRegion = Region("source");
        var targetRegion = Region("target");
        return new OrganizationalPoolSceneContributor(
            new OrganizationalElementEligibilityPolicy(static _ => true)).Route(
            new Canvas2DConnectorPresentationRoutingRequest(
                edge,
                [
                    source,
                    canonicalSourceTangent,
                    canonicalTargetPredecessor,
                    target,
                ],
                [source, target],
                source,
                target,
                sourceRegion,
                targetRegion,
                Matrix2D.Identity,
                [UpperRightNode, LowerLeftNode]));
    }

    private static Canvas2DSpatialRegion Region(string suffix) =>
        new(
            new Canvas2DSpatialRegionId($"test:organizational:routing:{suffix}"),
            new ModelProfileId("test:organizational:routing"),
            containerSemanticElementId: null,
            Matrix2D.Identity,
            new RectD(0d, 0d, 1000d, 1000d));

    private static void AssertObstacleFreeOrthogonal(IReadOnlyList<PointD> path)
    {
        Assert.All(path.Zip(path.Skip(1)), segment =>
        {
            Assert.True(
                segment.First.X == segment.Second.X ||
                segment.First.Y == segment.Second.Y);
            Assert.DoesNotContain(
                new[] { UpperRightNode, LowerLeftNode },
                obstacle => SegmentIntersectsInterior(
                    segment.First,
                    segment.Second,
                    obstacle));
        });
    }

    private static bool SegmentIntersectsInterior(
        PointD start,
        PointD end,
        RectD obstacle)
    {
        if (start.X == end.X)
        {
            return start.X > obstacle.Left && start.X < obstacle.Right &&
                Math.Max(start.Y, end.Y) > obstacle.Top &&
                Math.Min(start.Y, end.Y) < obstacle.Bottom;
        }

        return start.Y > obstacle.Top && start.Y < obstacle.Bottom &&
            Math.Max(start.X, end.X) > obstacle.Left &&
            Math.Min(start.X, end.X) < obstacle.Right;
    }
}
