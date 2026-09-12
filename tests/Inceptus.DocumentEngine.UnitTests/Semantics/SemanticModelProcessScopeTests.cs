using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Semantics;

public sealed class SemanticModelProcessScopeTests
{
    private static readonly DocumentId TestDocumentId = new("n8:document");
    private static readonly DocumentRevision TestRevision = new(8);
    private static readonly SemanticTypeId TestElementTypeId = new("test:element");
    private static readonly SemanticTypeId TestRelationshipTypeId = new("test:relationship");

    [Fact]
    public void RootScopeIsCanonicalImplicitAndBackwardsCompatible()
    {
        var source = Element("test:source");
        var target = Element("test:target");
        var relationship = Relationship("test:relationship", source.Id, target.Id);

        var snapshot = new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [source, target],
            [relationship]);

        var root = snapshot.GetRootScope();
        Assert.Equal(new DocumentScopeId(TestDocumentId.Value), snapshot.RootScopeId);
        Assert.Equal(snapshot.RootScopeId, root.Id);
        Assert.Null(root.ParentScopeId);
        Assert.Null(root.OwnerSemanticElementId);
        Assert.Empty(snapshot.NestedScopes);
        Assert.Empty(snapshot.ScopeMemberships);
        Assert.Equal(root, snapshot.GetScope(source.Id));
        Assert.Equal(root, snapshot.GetScope(target.Id));
        Assert.Equal(root, snapshot.GetScope(relationship.Id));
        Assert.Null(snapshot.GetParentScope(root.Id));
        Assert.Empty(snapshot.GetChildScopes(root.Id));
        Assert.Empty(snapshot.GetAncestors(root.Id));
        Assert.Empty(snapshot.GetDescendants(root.Id));
        Assert.False(snapshot.IsAncestorOf(root.Id, root.Id));
    }

    [Fact]
    public void NestedHierarchySupportsDeterministicParentAncestorAndDescendantQueries()
    {
        var root = new DocumentScopeId(TestDocumentId.Value);
        var scopeA = new DocumentScopeSnapshot(
            new DocumentScopeId("scope:A"),
            root,
            new SemanticElementId("test:owner-a"));
        var scopeB = new DocumentScopeSnapshot(
            new DocumentScopeId("scope:B"),
            scopeA.Id,
            new SemanticElementId("test:owner-b"));
        var relationship = Relationship(
            "test:nested-relationship",
            new SemanticElementId("test:nested-source"),
            new SemanticElementId("test:nested-target"));
        var snapshot = new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [
                Element("test:owner-a"),
                Element("test:owner-b"),
                Element("test:nested-source"),
                Element("test:nested-target"),
            ],
            [relationship],
            [scopeB, scopeA],
            [
                Membership("test:nested-target", scopeB.Id),
                Membership("test:owner-b", scopeA.Id),
                Membership("test:nested-source", scopeB.Id),
            ]);

        Assert.Equal(root, snapshot.GetParentScope(scopeA.Id)?.Id);
        Assert.Equal(scopeA.Id, snapshot.GetParentScope(scopeB.Id)?.Id);
        Assert.Equal(
            [scopeA.Id, scopeB.Id],
            snapshot.GetDescendants(root).Select(static scope => scope.Id));
        Assert.Equal(
            [scopeA.Id, root],
            snapshot.GetAncestors(scopeB.Id).Select(static scope => scope.Id));
        Assert.Equal([scopeA.Id],
            snapshot.GetChildScopes(root).Select(static scope => scope.Id));
        Assert.Equal([scopeB.Id],
            snapshot.GetChildScopes(scopeA.Id).Select(static scope => scope.Id));
        Assert.Equal(scopeB.Id, snapshot.GetScope(relationship.Id).Id);
        Assert.True(snapshot.IsAncestorOf(root, scopeA.Id));
        Assert.True(snapshot.IsAncestorOf(root, scopeB.Id));
        Assert.True(snapshot.IsAncestorOf(scopeA.Id, scopeB.Id));
        Assert.False(snapshot.IsAncestorOf(scopeB.Id, scopeA.Id));
        Assert.False(snapshot.IsAncestorOf(scopeA.Id, scopeA.Id));
    }

    [Fact]
    public void ChildrenAndDescendantsUseOrdinalDepthFirstPreorder()
    {
        var root = new DocumentScopeId(TestDocumentId.Value);
        DocumentScopeSnapshot[] scopes =
        [
            Scope("scope:z", root),
            Scope("scope:A/child-z", new DocumentScopeId("scope:A")),
            Scope("scope:a", root),
            Scope("scope:A/child-a/grandchild", new DocumentScopeId("scope:A/child-a")),
            Scope("scope:A", root),
            Scope("scope:A/child-a", new DocumentScopeId("scope:A")),
        ];

        var snapshot = new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            nestedScopes: scopes);

        Assert.Equal(
            ["scope:A", "scope:a", "scope:z"],
            snapshot.GetChildScopes(root).Select(static scope => scope.Id.Value));
        Assert.Equal(
            [
                "scope:A",
                "scope:A/child-a",
                "scope:A/child-a/grandchild",
                "scope:A/child-z",
                "scope:a",
                "scope:z",
            ],
            snapshot.GetDescendants(root).Select(static scope => scope.Id.Value));
        Assert.Equal(
            snapshot.GetDescendants(root).Select(static scope => scope.Id.Value),
            snapshot.GetDescendants(root).Select(static scope => scope.Id.Value));
    }

    [Fact]
    public void ScopeCollectionsAreDefensivelyCopiedAndOrdinallyOrdered()
    {
        var root = new DocumentScopeId(TestDocumentId.Value);
        var scopes = new List<DocumentScopeSnapshot>
        {
            Scope("scope:z", root),
            Scope("scope:A", root),
            Scope("scope:a", root),
        };
        var memberships = new List<SemanticElementScopeMembershipSnapshot>
        {
            Membership("test:z", new DocumentScopeId("scope:z")),
            Membership("test:A", new DocumentScopeId("scope:A")),
            Membership("test:a", new DocumentScopeId("scope:a")),
        };

        var snapshot = new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [Element("test:z"), Element("test:A"), Element("test:a")],
            nestedScopes: scopes,
            scopeMemberships: memberships);
        scopes.Clear();
        memberships.Clear();

        Assert.Equal(
            ["scope:A", "scope:a", "scope:z"],
            snapshot.NestedScopes.Select(static scope => scope.Id.Value));
        Assert.Equal(
            ["test:A", "test:a", "test:z"],
            snapshot.ScopeMemberships.Select(static membership =>
                membership.SemanticElementId.Value));
    }

    [Fact]
    public void IndependentScopeSnapshotsUseDeepStructuralEquality()
    {
        var root = new DocumentScopeId(TestDocumentId.Value);
        var first = new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [Element("test:owner"), Element("test:node")],
            nestedScopes:
            [new DocumentScopeSnapshot(
                new DocumentScopeId("scope:child"),
                root,
                new SemanticElementId("test:owner"))],
            scopeMemberships:
            [Membership("test:node", new DocumentScopeId("scope:child"))]);
        var same = new SemanticModelSnapshot(
            new DocumentId(TestDocumentId.Value),
            new DocumentRevision(TestRevision.Value),
            [Element("test:node"), Element("test:owner")],
            nestedScopes:
            [new DocumentScopeSnapshot(
                new DocumentScopeId("scope:child"),
                new DocumentScopeId(TestDocumentId.Value),
                new SemanticElementId("test:owner"))],
            scopeMemberships:
            [Membership("test:node", new DocumentScopeId("scope:child"))]);
        var different = new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [Element("test:owner"), Element("test:node")],
            nestedScopes:
            [new DocumentScopeSnapshot(
                new DocumentScopeId("scope:child"),
                root,
                new SemanticElementId("test:owner"))]);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void DuplicateScopeAndMembershipIdentitiesAreRejected()
    {
        var root = new DocumentScopeId(TestDocumentId.Value);
        var scope = Scope("scope:child", root);

        Assert.Throws<ArgumentException>(() => new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            nestedScopes: [scope, Scope("scope:child", root)]));
        Assert.Throws<ArgumentException>(() => new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [Element("test:node")],
            nestedScopes: [scope],
            scopeMemberships:
            [
                Membership("test:node", scope.Id),
                Membership("test:node", root),
            ]));
    }

    [Fact]
    public void RelationshipScopeIsDerivedFromItsSourceMembership()
    {
        var root = new DocumentScopeId(TestDocumentId.Value);
        var child = Scope("scope:child", root);
        var relationship = Relationship(
            "test:relationship",
            new SemanticElementId("test:nested-source"),
            new SemanticElementId("test:root-target"));
        var snapshot = new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [Element("test:nested-source"), Element("test:root-target")],
            [relationship],
            [child],
            [Membership("test:nested-source", child.Id)]);

        Assert.Equal(child.Id, snapshot.GetScope(relationship.Id).Id);
        Assert.Equal(root, snapshot.GetScope(relationship.TargetId).Id);
    }

    [Fact]
    public void ScopeForestEnumeratesMainPeersAndNestedScopesDeterministically()
    {
        var root = new DocumentScopeId(TestDocumentId.Value);
        var peerB = new DocumentScopeSnapshot(new DocumentScopeId("scope:B"));
        var peerC = new DocumentScopeSnapshot(new DocumentScopeId("scope:C"));
        var nestedB1 = new DocumentScopeSnapshot(
            new DocumentScopeId("scope:B1"),
            peerB.Id,
            new SemanticElementId("test:subprocess-b1"));
        var mainChild = new DocumentScopeSnapshot(
            new DocumentScopeId("scope:main-child"),
            root,
            new SemanticElementId("test:main-subprocess"));
        var snapshot = new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [Element("test:main-subprocess"), Element("test:subprocess-b1")],
            nestedScopes: [peerC, nestedB1, mainChild, peerB],
            scopeMemberships: [Membership("test:subprocess-b1", peerB.Id)]);

        Assert.Equal(
            [root, peerB.Id, peerC.Id],
            snapshot.GetTopLevelScopes().Select(static scope => scope.Id));
        Assert.Equal(
            [root, mainChild.Id, peerB.Id, nestedB1.Id, peerC.Id],
            snapshot.EnumerateScopeForest().Select(static scope => scope.Id));
        Assert.True(snapshot.IsTopLevelScope(root));
        Assert.True(snapshot.IsExplicitPeerRoot(peerB.Id));
        Assert.False(snapshot.IsExplicitPeerRoot(root));
        Assert.Equal(peerB.Id, snapshot.GetParentScope(nestedB1.Id)?.Id);
        Assert.Empty(snapshot.GetAncestors(peerB.Id));
    }

    [Fact]
    public void ContainmentDistinguishesLegacyMainPeerAndDocumentElements()
    {
        var peer = new DocumentScopeSnapshot(new DocumentScopeId("scope:peer"));
        var legacyMain = Element("test:legacy-main");
        var peerElement = Element("test:peer");
        var documentElement = new SemanticElementSnapshot(
            new SemanticElementId("test:document"),
            TestElementTypeId,
            containmentKind: SemanticElementContainmentKind.Document);
        var snapshot = new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [documentElement, peerElement, legacyMain],
            nestedScopes: [peer],
            scopeMemberships: [new(peerElement.Id, peer.Id)]);

        Assert.Equal(snapshot.RootScopeId, snapshot.GetScope(legacyMain.Id).Id);
        Assert.Equal(peer.Id, snapshot.GetScope(peerElement.Id).Id);
        Assert.False(snapshot.TryGetScope(documentElement.Id, out var documentScope));
        Assert.Null(documentScope);
        Assert.Throws<InvalidOperationException>(() => snapshot.GetScope(documentElement.Id));
        Assert.Equal(
            snapshot.RootScopeId,
            snapshot.GetScope(new SemanticElementId("test:legacy-main")).Id);
    }

    [Fact]
    public void UnknownSemanticAndScopeQueriesFailDeterministically()
    {
        var snapshot = new SemanticModelSnapshot(TestDocumentId, TestRevision);

        Assert.Throws<KeyNotFoundException>(() =>
            snapshot.GetScope(new SemanticElementId("test:missing")));
        Assert.Throws<KeyNotFoundException>(() =>
            snapshot.GetParentScope(new DocumentScopeId("scope:missing")));
        Assert.Throws<KeyNotFoundException>(() =>
            snapshot.GetChildScopes(new DocumentScopeId("scope:missing")));
        Assert.Throws<KeyNotFoundException>(() =>
            snapshot.GetAncestors(new DocumentScopeId("scope:missing")));
        Assert.Throws<KeyNotFoundException>(() =>
            snapshot.GetDescendants(new DocumentScopeId("scope:missing")));
    }

    private static SemanticElementSnapshot Element(string id) =>
        new(new SemanticElementId(id), TestElementTypeId);

    private static SemanticRelationshipSnapshot Relationship(
        string id,
        SemanticElementId sourceId,
        SemanticElementId targetId) =>
        new(new SemanticElementId(id), TestRelationshipTypeId, sourceId, targetId);

    private static DocumentScopeSnapshot Scope(string id, DocumentScopeId parentScopeId) =>
        new(new DocumentScopeId(id), parentScopeId);

    private static SemanticElementScopeMembershipSnapshot Membership(
        string elementId,
        DocumentScopeId scopeId) =>
        new(new SemanticElementId(elementId), scopeId);
}
