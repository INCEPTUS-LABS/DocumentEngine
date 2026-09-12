using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Visuals;

public sealed class ConnectorAnchorTests
{
    [Fact]
    public void VisualStateDefensivelyCopiesAndCanonicallyOrdersAnchors()
    {
        var mutable = new List<ConnectorAnchor>
        {
            Anchor("bottom-source", ConnectorAnchorSide.Bottom, ConnectorAnchorRole.Source, 0),
            Anchor("right-target", ConnectorAnchorSide.Right, ConnectorAnchorRole.Target, 1),
            Anchor("right-source", ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
        };
        var sourceReference = new ConnectorAnchorId("anchor:source");
        var targetReference = new ConnectorAnchorId("anchor:target");
        var visual = new VisualStateSnapshot(
            new VisualStateId("visual"),
            new SemanticElementId("semantic"),
            new PointD(10d, 20d),
            new SizeD(100d, 60d),
            VisualPlacementMode.Manual,
            connectorAnchors: mutable,
            sourceAnchorId: sourceReference,
            targetAnchorId: targetReference);

        mutable.Clear();

        Assert.Equal(
            ["right-source", "right-target", "bottom-source"],
            visual.ConnectorAnchors.Select(anchor => anchor.Id.Value));
        Assert.Equal(sourceReference, visual.SourceAnchorId);
        Assert.Equal(targetReference, visual.TargetAnchorId);
        Assert.True(((IList<ConnectorAnchor>)visual.ConnectorAnchors).IsReadOnly);
    }

    [Fact]
    public void AnchorIdentitySideRoleAndOrderParticipateInStructuralEquality()
    {
        var first = Anchor("anchor", ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        var same = Anchor("anchor", ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);

        Assert.Equal(first, same);
        Assert.True(first == same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, Anchor("other", ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0));
        Assert.NotEqual(first, Anchor("anchor", ConnectorAnchorSide.Left, ConnectorAnchorRole.Source, 0));
        Assert.NotEqual(first, Anchor("anchor", ConnectorAnchorSide.Right, ConnectorAnchorRole.Target, 0));
        Assert.NotEqual(first, Anchor("anchor", ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 1));
    }

    [Fact]
    public void UndefinedEnumsNegativeOrderDuplicateIdentityAndNonContiguousOrderAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Anchor("anchor", (ConnectorAnchorSide)99, ConnectorAnchorRole.Source, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Anchor("anchor", ConnectorAnchorSide.Right, (ConnectorAnchorRole)99, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Anchor("anchor", ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, -1));
        Assert.Throws<ArgumentException>(() => Visual(
        [
            Anchor("duplicate", ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
            Anchor("duplicate", ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0),
        ]));
        Assert.Throws<ArgumentException>(() => Visual(
        [
            Anchor("first", ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
            Anchor("gap", ConnectorAnchorSide.Right, ConnectorAnchorRole.Target, 2),
        ]));
    }

    [Fact]
    public void OmittedAnchorStateIsBackwardCompatible()
    {
        var visual = Visual();

        Assert.Empty(visual.ConnectorAnchors);
        Assert.Null(visual.SourceAnchorId);
        Assert.Null(visual.TargetAnchorId);
    }

    private static ConnectorAnchor Anchor(
        string id,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role,
        int order) =>
        new(new ConnectorAnchorId(id), side, role, order);

    private static VisualStateSnapshot Visual(
        IEnumerable<ConnectorAnchor>? anchors = null) =>
        new(
            new VisualStateId("visual"),
            new SemanticElementId("semantic"),
            new PointD(10d, 20d),
            new SizeD(100d, 60d),
            VisualPlacementMode.Manual,
            connectorAnchors: anchors);
}
