using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Visuals;

public sealed class BoundaryAttachmentPlacementTests
{
    private static readonly RectD OwnerBounds = new(10d, 20d, 100d, 60d);
    private static readonly SizeD AttachedSize = new(20d, 10d);

    [Theory]
    [InlineData(BoundaryAttachmentSide.Top, 0d, 0d, 15d)]
    [InlineData(BoundaryAttachmentSide.Right, 0.5d, 100d, 45d)]
    [InlineData(BoundaryAttachmentSide.Bottom, 1d, 100d, 75d)]
    [InlineData(BoundaryAttachmentSide.Left, 0.5d, 0d, 45d)]
    public void BoundsAreDerivedFromOwnerSideAndNormalizedPosition(
        BoundaryAttachmentSide side,
        double position,
        double expectedX,
        double expectedY)
    {
        var placement = new BoundaryAttachmentPlacement(side, position);

        var bounds = placement.ResolveBounds(OwnerBounds, AttachedSize);

        Assert.Equal(new RectD(expectedX, expectedY, 20d, 10d), bounds);
        Assert.Equal(
            placement.ResolveCenter(OwnerBounds),
            new PointD(bounds.Left + 10d, bounds.Top + 5d));
    }

    [Fact]
    public void PlacementRejectsUndefinedOrNonNormalizedState()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundaryAttachmentPlacement((BoundaryAttachmentSide)99, 0.5d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Top, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Top, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Top, -0.01d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Top, 1.01d));
    }

    [Theory]
    [InlineData(10d, 20d, BoundaryAttachmentSide.Top)]
    [InlineData(110d, 20d, BoundaryAttachmentSide.Top)]
    [InlineData(110d, 80d, BoundaryAttachmentSide.Right)]
    [InlineData(10d, 80d, BoundaryAttachmentSide.Bottom)]
    public void PerimeterProjectionUsesDeterministicSideTieOrder(
        double x,
        double y,
        BoundaryAttachmentSide expectedSide)
    {
        var projected = BoundaryAttachmentPlacement.ProjectToBoundary(
            OwnerBounds,
            new PointD(x, y));

        Assert.Equal(expectedSide, projected.Side);
        Assert.InRange(projected.PositionOnSide, 0d, 1d);
    }

    [Fact]
    public void LegalProjectionClampsToTheNearestPerimeterSegmentInsideTheDocument()
    {
        var owner = new RectD(0d, 0d, 100d, 60d);

        var succeeded = BoundaryAttachmentPlacement.TryProjectToBoundary(
            owner,
            new PointD(0d, 0d),
            new SizeD(36d, 36d),
            out var placement);

        Assert.True(succeeded);
        Assert.NotNull(placement);
        Assert.Equal(BoundaryAttachmentSide.Bottom, placement.Side);
        Assert.Equal(0.18d, placement.PositionOnSide, 12);
        Assert.True(DocumentGeometryBoundary.Contains(
            placement.ResolveBounds(owner, new SizeD(36d, 36d))));
    }

    [Fact]
    public void LegalProjectionReturnsNoCandidateWhenEverySideWouldCrossTheDocumentBoundary()
    {
        var succeeded = BoundaryAttachmentPlacement.TryProjectToBoundary(
            new RectD(0d, 0d, 10d, 10d),
            new PointD(0d, 0d),
            new SizeD(36d, 36d),
            out var placement);

        Assert.False(succeeded);
        Assert.Null(placement);
    }

    [Fact]
    public void PlacementUsesStructuralEquality()
    {
        var first = new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Left, 0.25d);
        var same = new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Left, 0.25d);
        var different = new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Left, 0.75d);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, different);
    }
}
