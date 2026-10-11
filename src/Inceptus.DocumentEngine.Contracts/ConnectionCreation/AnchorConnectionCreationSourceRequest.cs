using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.ConnectionCreation;

/// <summary>
/// Immutable source-anchor inputs used to match a connection-creation capability.
/// </summary>
public sealed class AnchorConnectionCreationSourceRequest
{
    public AnchorConnectionCreationSourceRequest(
        DocumentSnapshot document,
        DocumentRevision expectedRevision,
        SemanticElementId sourceSemanticElementId,
        VisualStateId sourceVisualStateId,
        ConnectorAnchorId sourceAnchorId)
        : this(null, null, document, expectedRevision, sourceSemanticElementId, sourceVisualStateId, sourceAnchorId)
    {
    }

    public AnchorConnectionCreationSourceRequest(
        PresentationViewContext? presentationContext, PresentationViewPlan? presentationPlan,
        DocumentSnapshot document, DocumentRevision expectedRevision,
        SemanticElementId sourceSemanticElementId, VisualStateId sourceVisualStateId, ConnectorAnchorId sourceAnchorId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sourceSemanticElementId);
        ArgumentNullException.ThrowIfNull(sourceVisualStateId);
        ArgumentNullException.ThrowIfNull(sourceAnchorId);

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
        ConnectionPresentationRequest.Validate(document, presentationContext, presentationPlan);
        PresentationContext = presentationContext;
        PresentationPlan = presentationPlan;
    }

    public DocumentSnapshot Document { get; }

    public DocumentRevision ExpectedRevision { get; }

    public SemanticElementId SourceSemanticElementId { get; }

    public VisualStateId SourceVisualStateId { get; }

    public ConnectorAnchorId SourceAnchorId { get; }

    public PresentationViewContext? PresentationContext { get; }
    public PresentationViewPlan? PresentationPlan { get; }
}

internal static class ConnectionPresentationRequest
{
    internal static void Validate(DocumentSnapshot document, PresentationViewContext? context, PresentationViewPlan? plan)
    {
        if (plan is not null && (plan.Context != context || plan.DocumentId != document.DocumentId || plan.SourceRevision != document.Revision))
            throw new ArgumentException("The connection presentation plan must match the current Document and view.", nameof(plan));
    }
}
