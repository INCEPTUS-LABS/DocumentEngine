using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Creates one ownerless explicit peer top-level semantic scope. The canonical
/// implicit root remains derived from the Document identity and is never persisted.
/// </summary>
public sealed record CreateTopLevelDocumentScopeCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } =
        new("inceptus:command/create-top-level-document-scope");

    public CreateTopLevelDocumentScopeCommand(
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        DocumentScopeId scopeId)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(scopeId);
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        ScopeId = scopeId;
    }

    public CommandTypeId TypeId => KnownTypeId;

    public DocumentId TargetDocumentId { get; }

    public DocumentRevision ExpectedRevision { get; }

    public CommandCategory Category => CommandCategory.Semantic;

    public AuthoritativeDocumentComponent AffectedComponents =>
        AuthoritativeDocumentComponent.SemanticModel;

    public DocumentScopeId ScopeId { get; }

    PipelineInvalidation ICommandPipelineInvalidation.PipelineInvalidation =>
        PipelineInvalidation.Scene;
}
