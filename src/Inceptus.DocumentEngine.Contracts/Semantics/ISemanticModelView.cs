using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.Contracts.Semantics;

public interface ISemanticModelView
{
    DocumentId DocumentId { get; }

    /// <summary>
    /// Gets the revision of the complete Document represented by this component view.
    /// </summary>
    DocumentRevision Revision { get; }

    int ElementCount { get; }

    int RelationshipCount { get; }

    DocumentScopeId RootScopeId { get; }

    ImmutableArray<SemanticElementSnapshot> Elements { get; }

    ImmutableArray<SemanticRelationshipSnapshot> Relationships { get; }

    ImmutableArray<DocumentScopeSnapshot> NestedScopes { get; }

    ImmutableArray<SemanticElementScopeMembershipSnapshot> ScopeMemberships { get; }

    ModelProfileStateSnapshot ModelProfiles { get; }

    ImmutableArray<ModelProfileElementAssignmentSnapshot> ProfileAssignments { get; }

    bool TryGetElement(SemanticElementId id, out SemanticElementSnapshot? element);

    bool TryGetRelationship(
        SemanticElementId id,
        out SemanticRelationshipSnapshot? relationship);

    DocumentScopeSnapshot GetRootScope();

    bool TryGetScope(DocumentScopeId scopeId, out DocumentScopeSnapshot? scope);

    bool TryGetScope(SemanticElementId semanticElementId, out DocumentScopeSnapshot? scope);

    DocumentScopeSnapshot GetScope(SemanticElementId semanticElementId);

    ImmutableArray<DocumentScopeSnapshot> GetTopLevelScopes();

    bool IsTopLevelScope(DocumentScopeId scopeId);

    bool IsExplicitPeerRoot(DocumentScopeId scopeId);

    ImmutableArray<DocumentScopeSnapshot> EnumerateScopeForest();

    DocumentScopeSnapshot? GetParentScope(DocumentScopeId scopeId);

    ImmutableArray<DocumentScopeSnapshot> GetChildScopes(DocumentScopeId scopeId);

    ImmutableArray<DocumentScopeSnapshot> GetAncestors(DocumentScopeId scopeId);

    ImmutableArray<DocumentScopeSnapshot> GetDescendants(DocumentScopeId scopeId);

    bool IsAncestorOf(DocumentScopeId ancestorScopeId, DocumentScopeId descendantScopeId);
}
