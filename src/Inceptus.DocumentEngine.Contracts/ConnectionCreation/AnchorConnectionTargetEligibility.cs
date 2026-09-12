using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.ConnectionCreation;

/// <summary>
/// Immutable source and node-body target identities supplied to notation semantics before
/// generic target-anchor policy acquisition.
/// </summary>
public sealed class AnchorConnectionTargetEligibilityRequest
{
    public AnchorConnectionTargetEligibilityRequest(
        DocumentSnapshot document,
        DocumentRevision expectedRevision,
        SemanticElementId sourceSemanticElementId,
        VisualStateId sourceVisualStateId,
        ConnectorAnchorId sourceAnchorId,
        SemanticElementId targetSemanticElementId,
        VisualStateId targetVisualStateId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sourceSemanticElementId);
        ArgumentNullException.ThrowIfNull(sourceVisualStateId);
        ArgumentNullException.ThrowIfNull(sourceAnchorId);
        ArgumentNullException.ThrowIfNull(targetSemanticElementId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        if (expectedRevision != document.Revision)
        {
            throw new ArgumentException(
                "The expected target-eligibility revision must match the supplied Document snapshot.",
                nameof(expectedRevision));
        }

        Document = document;
        ExpectedRevision = expectedRevision;
        SourceSemanticElementId = sourceSemanticElementId;
        SourceVisualStateId = sourceVisualStateId;
        SourceAnchorId = sourceAnchorId;
        TargetSemanticElementId = targetSemanticElementId;
        TargetVisualStateId = targetVisualStateId;
    }

    public DocumentSnapshot Document { get; }
    public DocumentRevision ExpectedRevision { get; }
    public SemanticElementId SourceSemanticElementId { get; }
    public VisualStateId SourceVisualStateId { get; }
    public ConnectorAnchorId SourceAnchorId { get; }
    public SemanticElementId TargetSemanticElementId { get; }
    public VisualStateId TargetVisualStateId { get; }
}

/// <summary>
/// Supplies notation-owned semantic eligibility for optional node-body target acquisition.
/// Absence of this contribution preserves historical exact-anchor-only behavior.
/// </summary>
public interface IAnchorConnectionTargetEligibility
{
    bool CanAcquire(AnchorConnectionTargetEligibilityRequest request);
}
