using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

/// <summary>
/// Identifies the side of an owning node boundary used by an attached visual.
/// </summary>
public enum BoundaryAttachmentSide
{
    Top = 0,
    Right = 1,
    Bottom = 2,
    Left = 3,
}

/// <summary>
/// Describes one persistent, normalized placement on an owning node boundary.
/// Absolute attached-node bounds are derived from this placement and the owner bounds.
/// </summary>
public sealed class BoundaryAttachmentPlacement : IEquatable<BoundaryAttachmentPlacement>
{
    public BoundaryAttachmentPlacement(
        BoundaryAttachmentSide side,
        double positionOnSide)
    {
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(
                nameof(side),
                side,
                "The boundary-attachment side must be defined.");
        }

        if (!double.IsFinite(positionOnSide) || positionOnSide is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(positionOnSide),
                positionOnSide,
                "The boundary position must be finite and between zero and one.");
        }

        Side = side;
        PositionOnSide = positionOnSide;
    }

    public BoundaryAttachmentSide Side { get; }

    public double PositionOnSide { get; }

    /// <summary>
    /// Projects a Document-space point to the nearest point on an owner perimeter.
    /// Equal-distance ties resolve deterministically in Top, Right, Bottom, Left order.
    /// </summary>
    public static BoundaryAttachmentPlacement ProjectToBoundary(
        RectD ownerBounds,
        PointD documentPoint)
    {
        if (ownerBounds.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ownerBounds),
                ownerBounds,
                "Boundary projection requires positive owner width and height.");
        }

        var topX = Math.Clamp(documentPoint.X, ownerBounds.Left, ownerBounds.Right);
        var topDistance = DistanceSquared(
            documentPoint,
            new PointD(topX, ownerBounds.Top));
        var bestSide = BoundaryAttachmentSide.Top;
        var bestPosition = (topX - ownerBounds.Left) / ownerBounds.Width;
        var bestDistance = topDistance;

        var rightY = Math.Clamp(documentPoint.Y, ownerBounds.Top, ownerBounds.Bottom);
        Consider(
            BoundaryAttachmentSide.Right,
            (rightY - ownerBounds.Top) / ownerBounds.Height,
            DistanceSquared(documentPoint, new PointD(ownerBounds.Right, rightY)),
            ref bestSide,
            ref bestPosition,
            ref bestDistance);

        var bottomX = Math.Clamp(documentPoint.X, ownerBounds.Left, ownerBounds.Right);
        Consider(
            BoundaryAttachmentSide.Bottom,
            (bottomX - ownerBounds.Left) / ownerBounds.Width,
            DistanceSquared(documentPoint, new PointD(bottomX, ownerBounds.Bottom)),
            ref bestSide,
            ref bestPosition,
            ref bestDistance);

        var leftY = Math.Clamp(documentPoint.Y, ownerBounds.Top, ownerBounds.Bottom);
        Consider(
            BoundaryAttachmentSide.Left,
            (leftY - ownerBounds.Top) / ownerBounds.Height,
            DistanceSquared(documentPoint, new PointD(ownerBounds.Left, leftY)),
            ref bestSide,
            ref bestPosition,
            ref bestDistance);

        return new BoundaryAttachmentPlacement(bestSide, bestPosition);
    }

    /// <summary>
    /// Projects a Document-space point to the nearest legal owner-perimeter point
    /// whose complete attached bounds remain inside the minimum Document boundary.
    /// Equal-distance ties resolve in Top, Right, Bottom, Left order.
    /// </summary>
    public static bool TryProjectToBoundary(
        RectD ownerBounds,
        PointD documentPoint,
        SizeD attachedSize,
        out BoundaryAttachmentPlacement? placement)
    {
        if (ownerBounds.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ownerBounds),
                ownerBounds,
                "Boundary projection requires positive owner width and height.");
        }

        if (attachedSize.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attachedSize),
                attachedSize,
                "Boundary projection requires positive attached width and height.");
        }

        var minimumCenterX = DocumentGeometryBoundary.MinimumX +
            (attachedSize.Width / 2d);
        var minimumCenterY = DocumentGeometryBoundary.MinimumY +
            (attachedSize.Height / 2d);
        var hasCandidate = false;
        var bestSide = default(BoundaryAttachmentSide);
        var bestPosition = 0d;
        var bestDistance = double.PositiveInfinity;

        if (ownerBounds.Top >= minimumCenterY && ownerBounds.Right >= minimumCenterX)
        {
            ConsiderLegal(
                BoundaryAttachmentSide.Top,
                MinimumPosition(ownerBounds.Left, ownerBounds.Width, minimumCenterX),
                ownerBounds,
                documentPoint,
                ref hasCandidate,
                ref bestSide,
                ref bestPosition,
                ref bestDistance);
        }

        if (ownerBounds.Right >= minimumCenterX && ownerBounds.Bottom >= minimumCenterY)
        {
            ConsiderLegal(
                BoundaryAttachmentSide.Right,
                MinimumPosition(ownerBounds.Top, ownerBounds.Height, minimumCenterY),
                ownerBounds,
                documentPoint,
                ref hasCandidate,
                ref bestSide,
                ref bestPosition,
                ref bestDistance);
        }

        if (ownerBounds.Bottom >= minimumCenterY && ownerBounds.Right >= minimumCenterX)
        {
            ConsiderLegal(
                BoundaryAttachmentSide.Bottom,
                MinimumPosition(ownerBounds.Left, ownerBounds.Width, minimumCenterX),
                ownerBounds,
                documentPoint,
                ref hasCandidate,
                ref bestSide,
                ref bestPosition,
                ref bestDistance);
        }

        if (ownerBounds.Left >= minimumCenterX && ownerBounds.Bottom >= minimumCenterY)
        {
            ConsiderLegal(
                BoundaryAttachmentSide.Left,
                MinimumPosition(ownerBounds.Top, ownerBounds.Height, minimumCenterY),
                ownerBounds,
                documentPoint,
                ref hasCandidate,
                ref bestSide,
                ref bestPosition,
                ref bestDistance);
        }

        if (!hasCandidate)
        {
            placement = null;
            return false;
        }

        var resolved = new BoundaryAttachmentPlacement(bestSide, bestPosition);
        if (!DocumentGeometryBoundary.Contains(
                resolved.ResolveBounds(ownerBounds, attachedSize)))
        {
            placement = null;
            return false;
        }

        placement = resolved;
        return true;
    }

    /// <summary>
    /// Resolves the attachment center in logical Document coordinates.
    /// </summary>
    public PointD ResolveCenter(RectD ownerBounds) => Side switch
    {
        BoundaryAttachmentSide.Top => new PointD(
            ownerBounds.Left + (ownerBounds.Width * PositionOnSide),
            ownerBounds.Top),
        BoundaryAttachmentSide.Right => new PointD(
            ownerBounds.Right,
            ownerBounds.Top + (ownerBounds.Height * PositionOnSide)),
        BoundaryAttachmentSide.Bottom => new PointD(
            ownerBounds.Left + (ownerBounds.Width * PositionOnSide),
            ownerBounds.Bottom),
        BoundaryAttachmentSide.Left => new PointD(
            ownerBounds.Left,
            ownerBounds.Top + (ownerBounds.Height * PositionOnSide)),
        _ => throw new InvalidOperationException("The boundary-attachment side is undefined."),
    };

    /// <summary>
    /// Resolves complete attached-node bounds whose center lies on the owner boundary.
    /// </summary>
    public RectD ResolveBounds(RectD ownerBounds, SizeD attachedSize)
    {
        if (attachedSize.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attachedSize),
                attachedSize,
                "An attached visual must have positive width and height.");
        }

        var center = ResolveCenter(ownerBounds);
        return new RectD(
            center.X - (attachedSize.Width / 2d),
            center.Y - (attachedSize.Height / 2d),
            attachedSize.Width,
            attachedSize.Height);
    }

    public bool Equals(BoundaryAttachmentPlacement? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Side == other.Side &&
        PositionOnSide.Equals(other.PositionOnSide);

    public override bool Equals(object? obj) => Equals(obj as BoundaryAttachmentPlacement);

    public override int GetHashCode() => HashCode.Combine(Side, PositionOnSide);

    public static bool operator ==(
        BoundaryAttachmentPlacement? left,
        BoundaryAttachmentPlacement? right) =>
        EqualityComparer<BoundaryAttachmentPlacement>.Default.Equals(left, right);

    public static bool operator !=(
        BoundaryAttachmentPlacement? left,
        BoundaryAttachmentPlacement? right) =>
        !(left == right);

    private static void Consider(
        BoundaryAttachmentSide side,
        double position,
        double distance,
        ref BoundaryAttachmentSide bestSide,
        ref double bestPosition,
        ref double bestDistance)
    {
        if (distance >= bestDistance)
        {
            return;
        }

        bestSide = side;
        bestPosition = position;
        bestDistance = distance;
    }

    private static void ConsiderLegal(
        BoundaryAttachmentSide side,
        double minimumPosition,
        RectD ownerBounds,
        PointD documentPoint,
        ref bool hasCandidate,
        ref BoundaryAttachmentSide bestSide,
        ref double bestPosition,
        ref double bestDistance)
    {
        var projectedPosition = side is BoundaryAttachmentSide.Top or
            BoundaryAttachmentSide.Bottom
            ? (documentPoint.X - ownerBounds.Left) / ownerBounds.Width
            : (documentPoint.Y - ownerBounds.Top) / ownerBounds.Height;
        var position = Math.Clamp(projectedPosition, minimumPosition, 1d);
        var candidate = new BoundaryAttachmentPlacement(side, position);
        var distance = DistanceSquared(documentPoint, candidate.ResolveCenter(ownerBounds));
        if (hasCandidate && distance >= bestDistance)
        {
            return;
        }

        hasCandidate = true;
        bestSide = side;
        bestPosition = position;
        bestDistance = distance;
    }

    private static double MinimumPosition(
        double sideStart,
        double sideLength,
        double minimumCenter) =>
        Math.Clamp((minimumCenter - sideStart) / sideLength, 0d, 1d);

    private static double DistanceSquared(PointD left, PointD right)
    {
        var deltaX = left.X - right.X;
        var deltaY = left.Y - right.Y;
        return (deltaX * deltaX) + (deltaY * deltaY);
    }
}
