using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Creation;

/// <summary>
/// Supplies the distinct technical identities allocated for one persistent creation attempt.
/// </summary>
public sealed class DocumentCreationIdentity : IEquatable<DocumentCreationIdentity>
{
    public DocumentCreationIdentity(
        SemanticElementId semanticElementId,
        VisualStateId visualStateId)
    {
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentNullException.ThrowIfNull(visualStateId);

        SemanticElementId = semanticElementId;
        VisualStateId = visualStateId;
    }

    public SemanticElementId SemanticElementId { get; }

    public VisualStateId VisualStateId { get; }

    public bool Equals(DocumentCreationIdentity? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        SemanticElementId == other.SemanticElementId &&
        VisualStateId == other.VisualStateId;

    public override bool Equals(object? obj) => Equals(obj as DocumentCreationIdentity);

    public override int GetHashCode() => HashCode.Combine(SemanticElementId, VisualStateId);
}

/// <summary>
/// Allocates fresh technical identities for one persistent creation attempt.
/// </summary>
public interface IDocumentCreationIdentityProvider
{
    DocumentCreationIdentity CreateIdentity();

    /// <summary>
    /// Allocates one fresh identity for a peer top-level Document scope. The default
    /// implementation derives it from the application-owned persistent creation source.
    /// </summary>
    DocumentScopeId CreateDocumentScopeId()
    {
        var identity = CreateIdentity();
        ArgumentNullException.ThrowIfNull(identity);
        return new DocumentScopeId($"scope:{identity.VisualStateId.Value}");
    }

    /// <summary>
    /// Allocates one fresh technical identity for a persistent connector anchor.
    /// The default implementation derives the identity from the same application-owned
    /// creation source used for semantic and visual identities.
    /// </summary>
    ConnectorAnchorId CreateConnectorAnchorId()
    {
        var identity = CreateIdentity();
        ArgumentNullException.ThrowIfNull(identity);
        return new ConnectorAnchorId($"connector-anchor:{identity.VisualStateId.Value}");
    }
}
