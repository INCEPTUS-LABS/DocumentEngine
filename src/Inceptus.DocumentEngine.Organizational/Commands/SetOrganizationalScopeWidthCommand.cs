using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Organizational.Commands;

/// <summary>Sets the shared outer width of every Pool and Unassigned in one scope.</summary>
public sealed record SetOrganizationalScopeWidthCommand : ICommand, ICommandPipelineInvalidation
{
    public static CommandTypeId KnownTypeId { get; } = new("inceptus:organizational/command/set-scope-width");

    public SetOrganizationalScopeWidthCommand(DocumentId targetDocumentId,
        DocumentRevision expectedRevision, DocumentScopeId scopeId, double outerWidth)
    {
        ArgumentNullException.ThrowIfNull(targetDocumentId);
        ArgumentNullException.ThrowIfNull(scopeId);
        if (!double.IsFinite(outerWidth) || outerWidth <= 0d)
            throw new ArgumentOutOfRangeException(nameof(outerWidth));
        TargetDocumentId = targetDocumentId;
        ExpectedRevision = expectedRevision;
        ScopeId = scopeId;
        OuterWidth = outerWidth;
    }

    public CommandTypeId TypeId => KnownTypeId;
    public DocumentId TargetDocumentId { get; }
    public DocumentRevision ExpectedRevision { get; }
    public DocumentScopeId ScopeId { get; }
    public double OuterWidth { get; }
    public CommandCategory Category => CommandCategory.Visual;
    public AuthoritativeDocumentComponent AffectedComponents => AuthoritativeDocumentComponent.VisualModel;
    public PipelineInvalidation PipelineInvalidation => CommandPipelineInvalidation.WithoutNodeLayout;
}
