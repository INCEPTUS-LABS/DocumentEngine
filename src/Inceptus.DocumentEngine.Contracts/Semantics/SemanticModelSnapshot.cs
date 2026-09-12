using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.Contracts.Semantics;

public sealed class SemanticModelSnapshot :
    ISemanticModelView,
    IEquatable<SemanticModelSnapshot>
{
    private readonly ImmutableDictionary<DocumentScopeId, ImmutableArray<DocumentScopeSnapshot>>
        _childrenByParentScopeId;
    private readonly ImmutableDictionary<DocumentScopeId, DocumentScopeSnapshot> _nestedScopesById;
    private readonly ImmutableDictionary<SemanticElementId, DocumentScopeId>
        _scopeMembershipsByElementId;

    public SemanticModelSnapshot(
        DocumentId documentId,
        DocumentRevision revision,
        IEnumerable<SemanticElementSnapshot>? elements = null,
        IEnumerable<SemanticRelationshipSnapshot>? relationships = null,
        IEnumerable<DocumentScopeSnapshot>? nestedScopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? scopeMemberships = null,
        ModelProfileStateSnapshot? modelProfiles = null,
        IEnumerable<ModelProfileElementAssignmentSnapshot>? profileAssignments = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);

        DocumentId = documentId;
        Revision = revision;
        RootScopeId = new DocumentScopeId(documentId.Value);
        Elements = CopyAndOrderElements(elements);
        Relationships = CopyAndOrderRelationships(relationships);
        NestedScopes = CopyAndOrderScopes(nestedScopes);
        ScopeMemberships = CopyAndOrderMemberships(scopeMemberships);
        ModelProfiles = modelProfiles ?? ModelProfileStateSnapshot.Empty;
        ProfileAssignments = CopyAndOrderProfileAssignments(profileAssignments);
        RejectDuplicateSemanticIdentities(Elements, Relationships);

        _nestedScopesById = NestedScopes.ToImmutableDictionary(static scope => scope.Id);
        _scopeMembershipsByElementId = ScopeMemberships.ToImmutableDictionary(
            static membership => membership.SemanticElementId,
            static membership => membership.ScopeId);
        _childrenByParentScopeId = NestedScopes
            .Where(static scope => scope.ParentScopeId is not null)
            .GroupBy(static scope => scope.ParentScopeId!)
            .ToImmutableDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray());
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision Revision { get; }

    public int ElementCount => Elements.Length;

    public int RelationshipCount => Relationships.Length;

    /// <summary>
    /// Gets the canonical implicit root scope identity derived from the Document identity.
    /// </summary>
    public DocumentScopeId RootScopeId { get; }

    public ImmutableArray<SemanticElementSnapshot> Elements { get; }

    public ImmutableArray<SemanticRelationshipSnapshot> Relationships { get; }

    /// <summary>
    /// Gets persisted explicit scopes: ownerless peer top-level roots and nested scopes.
    /// The property name is retained for persistence compatibility; the canonical root
    /// scope is implicit and is not duplicated in this collection.
    /// </summary>
    public ImmutableArray<DocumentScopeSnapshot> NestedScopes { get; }

    /// <summary>
    /// Gets explicit non-root memberships for scope-contained elements. A scope-contained
    /// element absent from this collection belongs to <see cref="RootScopeId"/>;
    /// document-contained elements never belong to a semantic scope.
    /// </summary>
    public ImmutableArray<SemanticElementScopeMembershipSnapshot> ScopeMemberships { get; }

    /// <summary>
    /// Gets sparse persistent optional-profile availability inside the canonical
    /// Semantic Model. Legacy snapshots default to an empty state.
    /// </summary>
    public ModelProfileStateSnapshot ModelProfiles { get; }

    /// <summary>
    /// Gets sparse profile-model assignments, unique by profile and source element.
    /// Absence means unassigned; scope remains owned by normal semantic containment.
    /// </summary>
    public ImmutableArray<ModelProfileElementAssignmentSnapshot> ProfileAssignments { get; }

    public bool TryGetElement(SemanticElementId id, out SemanticElementSnapshot? element)
    {
        ArgumentNullException.ThrowIfNull(id);

        var index = BinarySearchById(Elements, id.Value, static entry => entry.Id.Value);
        element = index >= 0 ? Elements[index] : null;
        return index >= 0;
    }

    public bool TryGetRelationship(
        SemanticElementId id,
        out SemanticRelationshipSnapshot? relationship)
    {
        ArgumentNullException.ThrowIfNull(id);

        var index = BinarySearchById(Relationships, id.Value, static entry => entry.Id.Value);
        relationship = index >= 0 ? Relationships[index] : null;
        return index >= 0;
    }

    public DocumentScopeSnapshot GetRootScope() => new(RootScopeId);

    /// <summary>
    /// Resolves a scope identity, including the implicit canonical root.
    /// </summary>
    public bool TryGetScope(DocumentScopeId scopeId, out DocumentScopeSnapshot? scope)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        if (scopeId == RootScopeId)
        {
            scope = GetRootScope();
            return true;
        }

        return _nestedScopesById.TryGetValue(scopeId, out scope);
    }

    /// <summary>
    /// Resolves scope containment. Document-contained elements return false and never
    /// fall back to the implicit root scope.
    /// </summary>
    public bool TryGetScope(
        SemanticElementId semanticElementId,
        out DocumentScopeSnapshot? scope)
    {
        ArgumentNullException.ThrowIfNull(semanticElementId);

        if (TryGetElement(semanticElementId, out var element) && element is not null)
        {
            if (element.ContainmentKind == SemanticElementContainmentKind.Document)
            {
                scope = null;
                return false;
            }

            var scopeId = _scopeMembershipsByElementId.TryGetValue(element.Id, out var assigned)
                ? assigned
                : RootScopeId;
            scope = ResolveScope(scopeId);
            return true;
        }

        if (TryGetRelationship(semanticElementId, out var relationship) && relationship is not null)
        {
            return TryGetScope(relationship.SourceId, out scope);
        }

        scope = null;
        return false;
    }

    /// <summary>
    /// Resolves an element's membership or a relationship's source-derived scope.
    /// </summary>
    public DocumentScopeSnapshot GetScope(SemanticElementId semanticElementId)
    {
        ArgumentNullException.ThrowIfNull(semanticElementId);

        if (TryGetScope(semanticElementId, out var scope) && scope is not null)
        {
            return scope;
        }

        if (TryGetElement(semanticElementId, out var element) &&
            element?.ContainmentKind == SemanticElementContainmentKind.Document)
        {
            throw new InvalidOperationException(
                $"Document-contained semantic element '{semanticElementId}' has no semantic scope.");
        }

        throw new KeyNotFoundException(
            $"Semantic identity '{semanticElementId}' does not exist in the Semantic Model.");
    }

    /// <summary>
    /// Gets the implicit canonical root followed by ownerless explicit peer roots
    /// in ordinal identity order.
    /// </summary>
    public ImmutableArray<DocumentScopeSnapshot> GetTopLevelScopes()
    {
        var roots = ImmutableArray.CreateBuilder<DocumentScopeSnapshot>();
        roots.Add(GetRootScope());
        roots.AddRange(NestedScopes.Where(static scope =>
            scope.ParentScopeId is null && scope.OwnerSemanticElementId is null));
        return roots.ToImmutable();
    }

    public bool IsTopLevelScope(DocumentScopeId scopeId)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        return scopeId == RootScopeId ||
            (_nestedScopesById.TryGetValue(scopeId, out var scope) &&
                scope.ParentScopeId is null &&
                scope.OwnerSemanticElementId is null);
    }

    public bool IsExplicitPeerRoot(DocumentScopeId scopeId)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        return scopeId != RootScopeId &&
            _nestedScopesById.TryGetValue(scopeId, out var scope) &&
            scope.ParentScopeId is null &&
            scope.OwnerSemanticElementId is null;
    }

    /// <summary>
    /// Enumerates the complete scope forest deterministically in pre-order: the implicit
    /// root and its descendants, then each explicit peer root and its descendants.
    /// </summary>
    public ImmutableArray<DocumentScopeSnapshot> EnumerateScopeForest()
    {
        var forest = ImmutableArray.CreateBuilder<DocumentScopeSnapshot>();
        foreach (var root in GetTopLevelScopes())
        {
            forest.Add(root);
            forest.AddRange(GetDescendants(root.Id));
        }

        return forest.ToImmutable();
    }

    public DocumentScopeSnapshot? GetParentScope(DocumentScopeId scopeId)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        if (scopeId == RootScopeId)
        {
            return null;
        }

        var scope = ResolveScope(scopeId);
        return scope.ParentScopeId is null ? null : ResolveScope(scope.ParentScopeId);
    }

    public ImmutableArray<DocumentScopeSnapshot> GetChildScopes(DocumentScopeId scopeId)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        _ = ResolveScope(scopeId);
        return _childrenByParentScopeId.TryGetValue(scopeId, out var children)
            ? children
            : [];
    }

    public ImmutableArray<DocumentScopeSnapshot> GetAncestors(DocumentScopeId scopeId)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        var current = ResolveScope(scopeId);
        var visited = new HashSet<DocumentScopeId> { current.Id };
        var ancestors = ImmutableArray.CreateBuilder<DocumentScopeSnapshot>();

        while (current.ParentScopeId is { } parentScopeId)
        {
            if (!visited.Add(parentScopeId))
            {
                throw new InvalidOperationException("The semantic scope hierarchy contains a cycle.");
            }

            current = ResolveScope(parentScopeId);
            ancestors.Add(current);
        }

        return ancestors.ToImmutable();
    }

    public ImmutableArray<DocumentScopeSnapshot> GetDescendants(DocumentScopeId scopeId)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        _ = ResolveScope(scopeId);

        var descendants = ImmutableArray.CreateBuilder<DocumentScopeSnapshot>();
        var visited = new HashSet<DocumentScopeId> { scopeId };
        var stack = new Stack<DocumentScopeSnapshot>();
        PushChildrenInReverseOrder(scopeId, stack);

        while (stack.TryPop(out var descendant))
        {
            if (!visited.Add(descendant.Id))
            {
                throw new InvalidOperationException("The semantic scope hierarchy contains a cycle.");
            }

            descendants.Add(descendant);
            PushChildrenInReverseOrder(descendant.Id, stack);
        }

        return descendants.ToImmutable();
    }

    public bool IsAncestorOf(
        DocumentScopeId ancestorScopeId,
        DocumentScopeId descendantScopeId)
    {
        ArgumentNullException.ThrowIfNull(ancestorScopeId);
        ArgumentNullException.ThrowIfNull(descendantScopeId);
        _ = ResolveScope(ancestorScopeId);
        var current = ResolveScope(descendantScopeId);
        var visited = new HashSet<DocumentScopeId> { current.Id };

        while (current.ParentScopeId is { } parentScopeId)
        {
            if (parentScopeId == ancestorScopeId)
            {
                return true;
            }

            if (!visited.Add(parentScopeId))
            {
                throw new InvalidOperationException("The semantic scope hierarchy contains a cycle.");
            }

            current = ResolveScope(parentScopeId);
        }

        return false;
    }

    public bool Equals(SemanticModelSnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        DocumentId == other.DocumentId &&
        Revision == other.Revision &&
        Elements.AsSpan().SequenceEqual(other.Elements.AsSpan()) &&
        Relationships.AsSpan().SequenceEqual(other.Relationships.AsSpan()) &&
        NestedScopes.AsSpan().SequenceEqual(other.NestedScopes.AsSpan()) &&
        ScopeMemberships.AsSpan().SequenceEqual(other.ScopeMemberships.AsSpan()) &&
        ModelProfiles.Equals(other.ModelProfiles) &&
        ProfileAssignments.AsSpan().SequenceEqual(other.ProfileAssignments.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as SemanticModelSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId);
        hash.Add(Revision);

        foreach (var element in Elements)
        {
            hash.Add(element);
        }

        foreach (var relationship in Relationships)
        {
            hash.Add(relationship);
        }

        foreach (var scope in NestedScopes)
        {
            hash.Add(scope);
        }

        foreach (var membership in ScopeMemberships)
        {
            hash.Add(membership);
        }

        hash.Add(ModelProfiles);
        foreach (var assignment in ProfileAssignments)
        {
            hash.Add(assignment);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(SemanticModelSnapshot? left, SemanticModelSnapshot? right) =>
        EqualityComparer<SemanticModelSnapshot>.Default.Equals(left, right);

    public static bool operator !=(SemanticModelSnapshot? left, SemanticModelSnapshot? right) =>
        !(left == right);

    private static ImmutableArray<ModelProfileElementAssignmentSnapshot>
        CopyAndOrderProfileAssignments(
            IEnumerable<ModelProfileElementAssignmentSnapshot>? profileAssignments)
    {
        var copy = profileAssignments?.ToArray() ?? [];
        RejectNullEntries(copy, nameof(profileAssignments));
        Array.Sort(copy, static (left, right) =>
        {
            var comparison = StringComparer.Ordinal.Compare(
                left.ProfileId.Value,
                right.ProfileId.Value);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(
                left.SemanticElementId.Value,
                right.SemanticElementId.Value);
        });
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].ProfileId == copy[index].ProfileId &&
                copy[index - 1].SemanticElementId == copy[index].SemanticElementId)
            {
                throw new ArgumentException(
                    "A semantic element cannot have multiple assignments in one profile.",
                    nameof(profileAssignments));
            }
        }

        return [.. copy];
    }

    private static ImmutableArray<SemanticElementSnapshot> CopyAndOrderElements(
        IEnumerable<SemanticElementSnapshot>? elements)
    {
        if (elements is null)
        {
            return [];
        }

        var copy = elements.ToArray();
        RejectNullEntries(copy, nameof(elements));
        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.Id.Value, right.Id.Value));
        RejectAdjacentDuplicateIds(copy, nameof(elements), static entry => entry.Id.Value);
        return [.. copy];
    }

    private static ImmutableArray<DocumentScopeSnapshot> CopyAndOrderScopes(
        IEnumerable<DocumentScopeSnapshot>? nestedScopes)
    {
        if (nestedScopes is null)
        {
            return [];
        }

        var copy = nestedScopes.ToArray();
        RejectNullEntries(copy, nameof(nestedScopes));
        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.Id.Value, right.Id.Value));
        RejectAdjacentDuplicateIds(copy, nameof(nestedScopes), static entry => entry.Id.Value);
        return [.. copy];
    }

    private static ImmutableArray<SemanticElementScopeMembershipSnapshot>
        CopyAndOrderMemberships(
            IEnumerable<SemanticElementScopeMembershipSnapshot>? scopeMemberships)
    {
        if (scopeMemberships is null)
        {
            return [];
        }

        var copy = scopeMemberships.ToArray();
        RejectNullEntries(copy, nameof(scopeMemberships));
        Array.Sort(copy, static (left, right) => StringComparer.Ordinal.Compare(
            left.SemanticElementId.Value,
            right.SemanticElementId.Value));
        RejectAdjacentDuplicateIds(
            copy,
            nameof(scopeMemberships),
            static entry => entry.SemanticElementId.Value);
        return [.. copy];
    }

    private static ImmutableArray<SemanticRelationshipSnapshot> CopyAndOrderRelationships(
        IEnumerable<SemanticRelationshipSnapshot>? relationships)
    {
        if (relationships is null)
        {
            return [];
        }

        var copy = relationships.ToArray();
        RejectNullEntries(copy, nameof(relationships));
        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.Id.Value, right.Id.Value));
        RejectAdjacentDuplicateIds(
            copy,
            nameof(relationships),
            static entry => entry.Id.Value);
        return [.. copy];
    }

    private static void RejectDuplicateSemanticIdentities(
        ImmutableArray<SemanticElementSnapshot> elements,
        ImmutableArray<SemanticRelationshipSnapshot> relationships)
    {
        var elementIndex = 0;
        var relationshipIndex = 0;

        while (elementIndex < elements.Length && relationshipIndex < relationships.Length)
        {
            var comparison = StringComparer.Ordinal.Compare(
                elements[elementIndex].Id.Value,
                relationships[relationshipIndex].Id.Value);

            if (comparison == 0)
            {
                throw new ArgumentException(
                    $"Semantic identity '{elements[elementIndex].Id}' is used by both an element and a relationship.",
                    nameof(relationships));
            }

            if (comparison < 0)
            {
                elementIndex++;
            }
            else
            {
                relationshipIndex++;
            }
        }
    }

    private static void RejectNullEntries<T>(T[] entries, string parameterName)
        where T : class
    {
        if (Array.Exists(entries, static entry => entry is null))
        {
            throw new ArgumentException("The collection cannot contain null entries.", parameterName);
        }
    }

    private static void RejectAdjacentDuplicateIds<T>(
        T[] entries,
        string parameterName,
        Func<T, string> getId)
    {
        for (var index = 1; index < entries.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(getId(entries[index - 1]), getId(entries[index])))
            {
                throw new ArgumentException(
                    $"Duplicate semantic identity '{getId(entries[index])}'.",
                    parameterName);
            }
        }
    }

    private static int BinarySearchById<T>(
        ImmutableArray<T> entries,
        string id,
        Func<T, string> getId)
    {
        var lower = 0;
        var upper = entries.Length - 1;

        while (lower <= upper)
        {
            var middle = lower + ((upper - lower) / 2);
            var comparison = StringComparer.Ordinal.Compare(getId(entries[middle]), id);

            if (comparison == 0)
            {
                return middle;
            }

            if (comparison < 0)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle - 1;
            }
        }

        return -1;
    }

    private DocumentScopeSnapshot ResolveScope(DocumentScopeId scopeId)
    {
        return TryGetScope(scopeId, out var scope) && scope is not null
            ? scope
            : throw new KeyNotFoundException(
                $"Document scope '{scopeId}' does not exist in the Semantic Model.");
    }

    private void PushChildrenInReverseOrder(
        DocumentScopeId parentScopeId,
        Stack<DocumentScopeSnapshot> stack)
    {
        if (!_childrenByParentScopeId.TryGetValue(parentScopeId, out var children))
        {
            return;
        }

        for (var index = children.Length - 1; index >= 0; index--)
        {
            stack.Push(children[index]);
        }
    }
}
