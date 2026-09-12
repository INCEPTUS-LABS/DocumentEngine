using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.ConnectionCreation;

public enum TargetAnchorAcquisitionKind
{
    Rejected = 0,
    Existing = 1,
    Proposed = 2,
}

public enum TargetAnchorAcquisitionRejectionReason
{
    InvalidTarget = 0,
    PolicyRejected = 1,
    InvalidGeometry = 2,
}

/// <summary>
/// Describes one immutable smart target-anchor acquisition outcome.
/// Proposed outcomes are transient intent until their command commits.
/// </summary>
public sealed class TargetAnchorAcquisitionResult :
    IEquatable<TargetAnchorAcquisitionResult>
{
    private TargetAnchorAcquisitionResult(
        TargetAnchorAcquisitionKind kind,
        TargetAnchorAcquisitionRejectionReason? rejectionReason,
        SemanticElementId? targetSemanticElementId,
        VisualStateId? targetVisualStateId,
        ConnectorAnchorSide? side,
        ConnectorAnchorId? anchorId,
        PointD? documentPoint,
        int? insertionIndex)
    {
        Kind = kind;
        RejectionReason = rejectionReason;
        TargetSemanticElementId = targetSemanticElementId;
        TargetVisualStateId = targetVisualStateId;
        Side = side;
        AnchorId = anchorId;
        DocumentPoint = documentPoint;
        InsertionIndex = insertionIndex;
    }

    public TargetAnchorAcquisitionKind Kind { get; }

    public TargetAnchorAcquisitionRejectionReason? RejectionReason { get; }

    public SemanticElementId? TargetSemanticElementId { get; }

    public VisualStateId? TargetVisualStateId { get; }

    public ConnectorAnchorSide? Side { get; }

    public ConnectorAnchorId? AnchorId { get; }

    public PointD? DocumentPoint { get; }

    public int? InsertionIndex { get; }

    public bool IsAccepted => Kind != TargetAnchorAcquisitionKind.Rejected;

    public static TargetAnchorAcquisitionResult Existing(
        SemanticElementId targetSemanticElementId,
        VisualStateId targetVisualStateId,
        ConnectorAnchorSide side,
        ConnectorAnchorId anchorId,
        PointD documentPoint)
    {
        ValidateAccepted(
            targetSemanticElementId,
            targetVisualStateId,
            side,
            anchorId,
            documentPoint);
        return new(
            TargetAnchorAcquisitionKind.Existing,
            null,
            targetSemanticElementId,
            targetVisualStateId,
            side,
            anchorId,
            documentPoint,
            null);
    }

    public static TargetAnchorAcquisitionResult Proposed(
        SemanticElementId targetSemanticElementId,
        VisualStateId targetVisualStateId,
        ConnectorAnchorSide side,
        ConnectorAnchorId anchorId,
        PointD documentPoint,
        int insertionIndex)
    {
        ValidateAccepted(
            targetSemanticElementId,
            targetVisualStateId,
            side,
            anchorId,
            documentPoint);
        ArgumentOutOfRangeException.ThrowIfNegative(insertionIndex);
        return new(
            TargetAnchorAcquisitionKind.Proposed,
            null,
            targetSemanticElementId,
            targetVisualStateId,
            side,
            anchorId,
            documentPoint,
            insertionIndex);
    }

    public static TargetAnchorAcquisitionResult Rejected(
        TargetAnchorAcquisitionRejectionReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        return new(
            TargetAnchorAcquisitionKind.Rejected,
            reason,
            null,
            null,
            null,
            null,
            null,
            null);
    }

    public bool Equals(TargetAnchorAcquisitionResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Kind == other.Kind &&
        RejectionReason == other.RejectionReason &&
        TargetSemanticElementId == other.TargetSemanticElementId &&
        TargetVisualStateId == other.TargetVisualStateId &&
        Side == other.Side &&
        AnchorId == other.AnchorId &&
        DocumentPoint == other.DocumentPoint &&
        InsertionIndex == other.InsertionIndex;

    public override bool Equals(object? obj) =>
        Equals(obj as TargetAnchorAcquisitionResult);

    public override int GetHashCode() => HashCode.Combine(
        Kind,
        RejectionReason,
        TargetSemanticElementId,
        TargetVisualStateId,
        Side,
        AnchorId,
        DocumentPoint,
        InsertionIndex);

    private static void ValidateAccepted(
        SemanticElementId targetSemanticElementId,
        VisualStateId targetVisualStateId,
        ConnectorAnchorSide side,
        ConnectorAnchorId anchorId,
        PointD documentPoint)
    {
        ArgumentNullException.ThrowIfNull(targetSemanticElementId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        ArgumentNullException.ThrowIfNull(anchorId);
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        if (!double.IsFinite(documentPoint.X) || !double.IsFinite(documentPoint.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(documentPoint));
        }
    }
}
