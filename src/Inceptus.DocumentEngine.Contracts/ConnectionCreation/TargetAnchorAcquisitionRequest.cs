using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.ConnectionCreation;

/// <summary>
/// Immutable authoritative inputs for generic node-body target-anchor acquisition.
/// </summary>
public sealed class TargetAnchorAcquisitionRequest
{
    public TargetAnchorAcquisitionRequest(
        DocumentSnapshot document,
        DocumentRevision expectedRevision,
        SemanticElementId targetSemanticElementId,
        VisualStateId targetVisualStateId,
        RectD targetBounds,
        PointD documentPoint,
        ElementConnectorAnchorPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(targetSemanticElementId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        ArgumentNullException.ThrowIfNull(policy);
        if (expectedRevision != document.Revision)
        {
            throw new ArgumentException(
                "The expected target-acquisition revision must match the supplied Document snapshot.",
                nameof(expectedRevision));
        }

        if (!double.IsFinite(documentPoint.X) || !double.IsFinite(documentPoint.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(documentPoint));
        }

        if (!double.IsFinite(targetBounds.X) || !double.IsFinite(targetBounds.Y) ||
            !double.IsFinite(targetBounds.Width) || !double.IsFinite(targetBounds.Height) ||
            !double.IsFinite(targetBounds.Right) || !double.IsFinite(targetBounds.Bottom))
        {
            throw new ArgumentOutOfRangeException(nameof(targetBounds));
        }

        Document = document;
        ExpectedRevision = expectedRevision;
        TargetSemanticElementId = targetSemanticElementId;
        TargetVisualStateId = targetVisualStateId;
        TargetBounds = targetBounds;
        DocumentPoint = documentPoint;
        Policy = policy;
    }

    public DocumentSnapshot Document { get; }

    public DocumentRevision ExpectedRevision { get; }

    public SemanticElementId TargetSemanticElementId { get; }

    public VisualStateId TargetVisualStateId { get; }

    public RectD TargetBounds { get; }

    public PointD DocumentPoint { get; }

    public ElementConnectorAnchorPolicy Policy { get; }
}
