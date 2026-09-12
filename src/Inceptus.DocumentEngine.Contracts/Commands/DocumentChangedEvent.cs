using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

public sealed class DocumentChangedEvent : IEquatable<DocumentChangedEvent>
{
    public DocumentChangedEvent(
        DocumentId documentId,
        DocumentRevision previousRevision,
        DocumentRevision committedRevision,
        AuthoritativeDocumentComponent affectedComponents,
        CommandTypeId commandTypeId,
        DocumentSnapshot committedSnapshot,
        PipelineInvalidation pipelineInvalidation = CommandPipelineInvalidation.Full,
        NodeGeometryPipelineImpact? nodeGeometryImpact = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(commandTypeId);
        ArgumentNullException.ThrowIfNull(committedSnapshot);

        if (committedRevision != previousRevision.Increment())
        {
            throw new ArgumentException(
                "The committed revision must be exactly one revision after the previous revision.",
                nameof(committedRevision));
        }

        if (!IsValidAffectedComponents(affectedComponents))
        {
            throw new ArgumentOutOfRangeException(
                nameof(affectedComponents),
                affectedComponents,
                "A committed event must identify at least one authoritative Document component.");
        }

        if (committedSnapshot.DocumentId != documentId)
        {
            throw new ArgumentException(
                "The committed snapshot must belong to the changed Document.",
                nameof(committedSnapshot));
        }

        if (committedSnapshot.Revision != committedRevision)
        {
            throw new ArgumentException(
                "The committed snapshot must describe the committed revision.",
                nameof(committedSnapshot));
        }

        CommandPipelineInvalidation.Validate(
            pipelineInvalidation,
            nameof(pipelineInvalidation));
        var resolvedNodeGeometryImpact =
            CommandPipelineInvalidation.ResolveNodeGeometryImpact(
                pipelineInvalidation,
                nodeGeometryImpact,
                nameof(nodeGeometryImpact));

        DocumentId = documentId;
        PreviousRevision = previousRevision;
        CommittedRevision = committedRevision;
        AffectedComponents = affectedComponents;
        CommandTypeId = commandTypeId;
        CommittedSnapshot = committedSnapshot;
        PipelineInvalidation = pipelineInvalidation;
        NodeGeometryImpact = resolvedNodeGeometryImpact;
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision PreviousRevision { get; }

    public DocumentRevision CommittedRevision { get; }

    public AuthoritativeDocumentComponent AffectedComponents { get; }

    public CommandTypeId CommandTypeId { get; }

    public DocumentSnapshot CommittedSnapshot { get; }

    public PipelineInvalidation PipelineInvalidation { get; }

    public NodeGeometryPipelineImpact? NodeGeometryImpact { get; }

    public bool Equals(DocumentChangedEvent? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        DocumentId == other.DocumentId &&
        PreviousRevision == other.PreviousRevision &&
        CommittedRevision == other.CommittedRevision &&
        AffectedComponents == other.AffectedComponents &&
        CommandTypeId == other.CommandTypeId &&
        CommittedSnapshot.Equals(other.CommittedSnapshot) &&
        PipelineInvalidation == other.PipelineInvalidation &&
        NodeGeometryImpact == other.NodeGeometryImpact;

    public override bool Equals(object? obj) => Equals(obj as DocumentChangedEvent);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId);
        hash.Add(PreviousRevision);
        hash.Add(CommittedRevision);
        hash.Add(AffectedComponents);
        hash.Add(CommandTypeId);
        hash.Add(CommittedSnapshot);
        hash.Add(PipelineInvalidation);
        hash.Add(NodeGeometryImpact);
        return hash.ToHashCode();
    }

    public static bool operator ==(DocumentChangedEvent? left, DocumentChangedEvent? right) =>
        EqualityComparer<DocumentChangedEvent>.Default.Equals(left, right);

    public static bool operator !=(DocumentChangedEvent? left, DocumentChangedEvent? right) =>
        !(left == right);

    private static bool IsValidAffectedComponents(AuthoritativeDocumentComponent value)
    {
        const AuthoritativeDocumentComponent all =
            AuthoritativeDocumentComponent.SemanticModel |
            AuthoritativeDocumentComponent.VisualModel |
            AuthoritativeDocumentComponent.Metadata |
            AuthoritativeDocumentComponent.Publication;

        return value != AuthoritativeDocumentComponent.None && (value & ~all) == 0;
    }
}
