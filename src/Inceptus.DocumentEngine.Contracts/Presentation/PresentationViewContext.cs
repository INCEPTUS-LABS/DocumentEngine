using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Presentation;

/// <summary>Identifies a presentation without changing semantic containment.</summary>
public abstract record PresentationViewContext;

public sealed record ProcessScopePresentationContext : PresentationViewContext
{
    public ProcessScopePresentationContext(DocumentScopeId scopeId)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        ScopeId = scopeId;
    }

    public DocumentScopeId ScopeId { get; }
}

public sealed record DocumentPresentationContext : PresentationViewContext
{
    public DocumentPresentationContext(SemanticElementId ownerId)
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        OwnerId = ownerId;
    }

    public SemanticElementId OwnerId { get; }
}
