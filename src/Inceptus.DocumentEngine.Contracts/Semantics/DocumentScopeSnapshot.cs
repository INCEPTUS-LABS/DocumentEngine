using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Semantics;

/// <summary>
/// Describes one notation-neutral semantic scope. The canonical root scope is
/// implicit; persisted entries describe either ownerless peer roots or nested scopes.
/// </summary>
public sealed class DocumentScopeSnapshot : IEquatable<DocumentScopeSnapshot>
{
    public DocumentScopeSnapshot(
        DocumentScopeId id,
        DocumentScopeId? parentScopeId = null,
        SemanticElementId? ownerSemanticElementId = null)
    {
        ArgumentNullException.ThrowIfNull(id);

        Id = id;
        ParentScopeId = parentScopeId;
        OwnerSemanticElementId = ownerSemanticElementId;
    }

    public DocumentScopeId Id { get; }

    public DocumentScopeId? ParentScopeId { get; }

    public SemanticElementId? OwnerSemanticElementId { get; }

    public bool Equals(DocumentScopeSnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id &&
        ParentScopeId == other.ParentScopeId &&
        OwnerSemanticElementId == other.OwnerSemanticElementId;

    public override bool Equals(object? obj) => Equals(obj as DocumentScopeSnapshot);

    public override int GetHashCode() =>
        HashCode.Combine(Id, ParentScopeId, OwnerSemanticElementId);

    public static bool operator ==(DocumentScopeSnapshot? left, DocumentScopeSnapshot? right) =>
        EqualityComparer<DocumentScopeSnapshot>.Default.Equals(left, right);

    public static bool operator !=(DocumentScopeSnapshot? left, DocumentScopeSnapshot? right) =>
        !(left == right);
}
