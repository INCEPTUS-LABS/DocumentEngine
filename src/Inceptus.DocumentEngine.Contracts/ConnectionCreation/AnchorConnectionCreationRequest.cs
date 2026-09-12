using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.ConnectionCreation;

/// <summary>
/// Immutable endpoint inputs supplied to a notation-owned connection-creation factory.
/// </summary>
public sealed class AnchorConnectionCreationRequest
{
    public AnchorConnectionCreationRequest(
        DocumentSnapshot document,
        DocumentRevision expectedRevision,
        SemanticElementId sourceSemanticElementId,
        VisualStateId sourceVisualStateId,
        ConnectorAnchorId sourceAnchorId,
        SemanticElementId targetSemanticElementId,
        VisualStateId targetVisualStateId,
        ConnectorAnchorId targetAnchorId,
        IDocumentCreationIdentityProvider identityProvider,
        TargetAnchorAcquisitionResult? targetAcquisition = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sourceSemanticElementId);
        ArgumentNullException.ThrowIfNull(sourceVisualStateId);
        ArgumentNullException.ThrowIfNull(sourceAnchorId);
        ArgumentNullException.ThrowIfNull(targetSemanticElementId);
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        ArgumentNullException.ThrowIfNull(targetAnchorId);
        ArgumentNullException.ThrowIfNull(identityProvider);

        if (targetAcquisition is not null &&
            (!targetAcquisition.IsAccepted ||
             targetAcquisition.TargetSemanticElementId != targetSemanticElementId ||
             targetAcquisition.TargetVisualStateId != targetVisualStateId ||
             targetAcquisition.AnchorId != targetAnchorId))
        {
            throw new ArgumentException(
                "The target acquisition must be accepted and match the supplied target identities.",
                nameof(targetAcquisition));
        }

        if (expectedRevision != document.Revision)
        {
            throw new ArgumentException(
                "The expected connection-creation revision must match the supplied Document snapshot.",
                nameof(expectedRevision));
        }

        Document = document;
        ExpectedRevision = expectedRevision;
        SourceSemanticElementId = sourceSemanticElementId;
        SourceVisualStateId = sourceVisualStateId;
        SourceAnchorId = sourceAnchorId;
        TargetSemanticElementId = targetSemanticElementId;
        TargetVisualStateId = targetVisualStateId;
        TargetAnchorId = targetAnchorId;
        IdentityProvider = identityProvider;
        TargetAcquisition = targetAcquisition;
    }

    public DocumentSnapshot Document { get; }

    public DocumentRevision ExpectedRevision { get; }

    public SemanticElementId SourceSemanticElementId { get; }

    public VisualStateId SourceVisualStateId { get; }

    public ConnectorAnchorId SourceAnchorId { get; }

    public SemanticElementId TargetSemanticElementId { get; }

    public VisualStateId TargetVisualStateId { get; }

    public ConnectorAnchorId TargetAnchorId { get; }

    public IDocumentCreationIdentityProvider IdentityProvider { get; }

    public TargetAnchorAcquisitionResult? TargetAcquisition { get; }
}
