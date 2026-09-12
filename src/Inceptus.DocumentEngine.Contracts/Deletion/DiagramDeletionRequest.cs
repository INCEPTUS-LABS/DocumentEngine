using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Deletion;

/// <summary>
/// Supplies one current persistent target to a notation-owned deletion factory.
/// </summary>
public sealed class DiagramDeletionRequest
{
    public DiagramDeletionRequest(
        DocumentSnapshot document,
        DocumentRevision expectedRevision,
        DiagramDeletionTargetKind targetKind,
        SemanticElementId semanticId,
        VisualStateId? visualStateId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(semanticId);
        if (document.Revision != expectedRevision)
        {
            throw new ArgumentException(
                "The expected deletion revision must match the supplied Document snapshot.",
                nameof(expectedRevision));
        }

        if (!Enum.IsDefined(targetKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetKind),
                targetKind,
                "The deletion target kind must be defined.");
        }

        Document = document;
        ExpectedRevision = expectedRevision;
        TargetKind = targetKind;
        SemanticId = semanticId;
        VisualStateId = visualStateId;
    }

    public DocumentSnapshot Document { get; }

    public DocumentRevision ExpectedRevision { get; }

    public DiagramDeletionTargetKind TargetKind { get; }

    public SemanticElementId SemanticId { get; }

    public VisualStateId? VisualStateId { get; }
}
