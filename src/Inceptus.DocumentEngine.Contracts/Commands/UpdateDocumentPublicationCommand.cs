using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Replaces the complete optional Publication state of one Document.
/// </summary>
public sealed record UpdateDocumentPublicationCommand :
    ICommand,
    ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/update-document-publication");

    public UpdateDocumentPublicationCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        string? code,
        string? title,
        string? description)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        Code = code;
        Title = title;
        Description = description;
    }

    public UpdateDocumentPublicationCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        DocumentPublicationSnapshot? publication)
        : this(
            targetDocumentId,
            expectedRevision,
            publication?.Code,
            publication?.Title,
            publication?.Description)
    {
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Publication;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.Publication;

    public string? Code { get; }

    public string? Title { get; }

    public string? Description { get; }

    public bool ClearsPublication =>
        Code is null && Title is null && Description is null;

    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        PipelineInvalidation.None;

    public bool TryCreatePublication(out DocumentPublicationSnapshot? publication)
    {
        if (ClearsPublication)
        {
            publication = null;
            return true;
        }

        return DocumentPublicationSnapshot.TryCreate(
            Code,
            Title,
            Description,
            out publication);
    }
}
