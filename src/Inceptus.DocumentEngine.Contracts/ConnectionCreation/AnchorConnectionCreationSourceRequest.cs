using Inceptus.DocumentEngine.Contracts.Documents;
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
    }

    public DocumentSnapshot Document { get; }

    public DocumentRevision ExpectedRevision { get; }

    public SemanticElementId SourceSemanticElementId { get; }

    public VisualStateId SourceVisualStateId { get; }

    public ConnectorAnchorId SourceAnchorId { get; }
}
