using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Semantics;

/// <summary>
/// Assigns one semantic element to one explicit nested scope. An element with no
/// persisted assignment belongs to the Document's canonical root scope.
/// </summary>
public sealed class SemanticElementScopeMembershipSnapshot :
    IEquatable<SemanticElementScopeMembershipSnapshot>
{
    public SemanticElementScopeMembershipSnapshot(
        SemanticElementId semanticElementId,
        DocumentScopeId scopeId)
    {
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentNullException.ThrowIfNull(scopeId);

        SemanticElementId = semanticElementId;
        ScopeId = scopeId;
    }

    public SemanticElementId SemanticElementId { get; }

    public DocumentScopeId ScopeId { get; }

    public bool Equals(SemanticElementScopeMembershipSnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        SemanticElementId == other.SemanticElementId &&
        ScopeId == other.ScopeId;

    public override bool Equals(object? obj) =>
        Equals(obj as SemanticElementScopeMembershipSnapshot);

    public override int GetHashCode() => HashCode.Combine(SemanticElementId, ScopeId);

    public static bool operator ==(
        SemanticElementScopeMembershipSnapshot? left,
        SemanticElementScopeMembershipSnapshot? right) =>
        EqualityComparer<SemanticElementScopeMembershipSnapshot>.Default.Equals(left, right);

    public static bool operator !=(
        SemanticElementScopeMembershipSnapshot? left,
        SemanticElementScopeMembershipSnapshot? right) =>
        !(left == right);
}
