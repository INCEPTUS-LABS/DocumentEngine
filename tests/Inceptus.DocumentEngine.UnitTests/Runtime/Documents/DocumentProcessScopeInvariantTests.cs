using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Runtime.Documents;

public sealed class DocumentProcessScopeInvariantTests
{
    private static readonly DocumentId TestDocumentId = new("n8:document");
    private static readonly DocumentRevision TestRevision = new(8);
    private static readonly DocumentScopeId RootScopeId = new(TestDocumentId.Value);
    private static readonly SemanticTypeId TestElementTypeId = new("test:element");
    private static readonly SemanticTypeId TestRelationshipTypeId = new("test:relationship");

    [Fact]
    public void DuplicateScopeAndMembershipIdentitiesAreRejectedAtTheContractBoundary()
    {
        var child = Scope("scope:child", RootScopeId);

        Assert.Throws<ArgumentException>(() => Snapshot(
            nestedScopes: [child, Scope("scope:child", RootScopeId)]));
        Assert.Throws<ArgumentException>(() => Snapshot(
            elements: [Element("test:node")],
            nestedScopes: [child],
            memberships:
            [
                Membership("test:node", child.Id),
                Membership("test:node", RootScopeId),
            ]));
    }

    [Fact]
    public void ExplicitNestedScopeCannotCollideWithTheImplicitRootIdentity()
    {
        var snapshot = Snapshot(
            nestedScopes: [new DocumentScopeSnapshot(RootScopeId, RootScopeId)]);

        AssertRejected(snapshot, DocumentInvariantValidator.ScopeRootCollisionCode);
    }

    [Fact]
    public void NestedScopeMustDeclareAnExistingParent()
    {
        var withoutParent = Snapshot(
            elements: [Element("test:owner")],
            nestedScopes:
            [new DocumentScopeSnapshot(
                new DocumentScopeId("scope:orphan"),
                ownerSemanticElementId: new SemanticElementId("test:owner"))]);
        var unknownParent = Snapshot(
            nestedScopes:
            [Scope("scope:orphan", new DocumentScopeId("scope:missing"))]);

        AssertRejected(withoutParent, DocumentInvariantValidator.ScopeParentMissingCode);
        AssertRejected(unknownParent, DocumentInvariantValidator.ScopeParentMissingCode);
    }

    [Fact]
    public void OwnerlessExplicitPeerRootIsValidAndHasNoFakeMainParent()
    {
        var peer = new DocumentScopeSnapshot(new DocumentScopeId("scope:peer"));
        var snapshot = Snapshot(nestedScopes: [peer]);

        var result = DocumentReconstructor.Reconstruct(snapshot);

        Assert.True(result.Succeeded);
        var semanticModel = Assert.IsType<Document>(result.Document).SemanticModel;
        Assert.True(semanticModel.IsExplicitPeerRoot(peer.Id));
        Assert.Null(semanticModel.GetParentScope(peer.Id));
        Assert.Null(peer.OwnerSemanticElementId);
    }

    [Fact]
    public void DocumentContainedElementCannotCarryScopeMembership()
    {
        var peer = new DocumentScopeSnapshot(new DocumentScopeId("scope:peer"));
        var documentElement = new SemanticElementSnapshot(
            new SemanticElementId("test:document"),
            TestElementTypeId,
            containmentKind: SemanticElementContainmentKind.Document);
        var snapshot = Snapshot(
            elements: [documentElement],
            nestedScopes: [peer],
            memberships: [new(documentElement.Id, peer.Id)]);

        AssertRejected(
            snapshot,
            DocumentInvariantValidator.DocumentElementScopeMembershipCode);
    }

    [Fact]
    public void DocumentContainedElementCannotOwnAProcessScope()
    {
        var owner = new SemanticElementSnapshot(
            new SemanticElementId("test:document-owner"),
            TestElementTypeId,
            containmentKind: SemanticElementContainmentKind.Document);
        var snapshot = Snapshot(
            elements: [owner],
            nestedScopes:
            [new DocumentScopeSnapshot(
                new DocumentScopeId("scope:child"),
                RootScopeId,
                owner.Id)]);

        AssertRejected(snapshot, DocumentInvariantValidator.ScopeOwnerContainmentInvalidCode);
    }

    [Fact]
    public void ScopeCannotBeItsOwnParent()
    {
        var self = new DocumentScopeId("scope:self");
        var snapshot = Snapshot(nestedScopes: [Scope(self.Value, self)]);

        AssertRejected(
            snapshot,
            DocumentInvariantValidator.ScopeSelfParentCode,
            DocumentInvariantValidator.ScopeCycleCode);
    }

    [Fact]
    public void TwoScopeCycleIsRejectedWithOrdinalDiagnostics()
    {
        var scopeA = new DocumentScopeId("scope:A");
        var scopeB = new DocumentScopeId("scope:B");
        var snapshot = Snapshot(
            nestedScopes:
            [
                Scope(scopeB.Value, scopeA),
                Scope(scopeA.Value, scopeB),
            ]);

        var diagnostics = AssertRejected(
            snapshot,
            DocumentInvariantValidator.ScopeCycleCode,
            DocumentInvariantValidator.ScopeCycleCode);

        Assert.Equal(
            [scopeA.Value, scopeB.Value],
            diagnostics.Select(static diagnostic => diagnostic.SourceIdentity));
    }

    [Fact]
    public void LongerScopeCycleIsRejectedWithOrdinalDiagnostics()
    {
        var scopeA = new DocumentScopeId("scope:A");
        var scopeB = new DocumentScopeId("scope:B");
        var scopeC = new DocumentScopeId("scope:C");
        var snapshot = Snapshot(
            nestedScopes:
            [
                Scope(scopeC.Value, scopeA),
                Scope(scopeA.Value, scopeB),
                Scope(scopeB.Value, scopeC),
            ]);

        var diagnostics = AssertRejected(
            snapshot,
            DocumentInvariantValidator.ScopeCycleCode,
            DocumentInvariantValidator.ScopeCycleCode,
            DocumentInvariantValidator.ScopeCycleCode);

        Assert.Equal(
            [scopeA.Value, scopeB.Value, scopeC.Value],
            diagnostics.Select(static diagnostic => diagnostic.SourceIdentity));
    }

    [Fact]
    public void ScopeMembershipMustReferenceAnExistingSemanticElement()
    {
        var child = Scope("scope:child", RootScopeId);
        var snapshot = Snapshot(
            nestedScopes: [child],
            memberships: [Membership("test:missing", child.Id)]);

        AssertRejected(
            snapshot,
            DocumentInvariantValidator.ScopeMembershipElementMissingCode);
    }

    [Fact]
    public void ScopeMembershipMustReferenceAnExistingNestedScope()
    {
        var snapshot = Snapshot(
            elements: [Element("test:node")],
            memberships:
            [Membership("test:node", new DocumentScopeId("scope:missing"))]);

        AssertRejected(
            snapshot,
            DocumentInvariantValidator.ScopeMembershipScopeMissingCode);
    }

    [Fact]
    public void RootMembershipMustRemainImplicit()
    {
        var snapshot = Snapshot(
            elements: [Element("test:node")],
            memberships: [Membership("test:node", RootScopeId)]);

        AssertRejected(
            snapshot,
            DocumentInvariantValidator.ScopeMembershipRootExplicitCode);
    }

    [Fact]
    public void NestedScopeOwnerMustReferenceAnExistingSemanticElement()
    {
        var snapshot = Snapshot(
            nestedScopes:
            [new DocumentScopeSnapshot(
                new DocumentScopeId("scope:child"),
                RootScopeId,
                new SemanticElementId("test:missing-owner"))]);

        AssertRejected(snapshot, DocumentInvariantValidator.ScopeOwnerMissingCode);
    }

    [Fact]
    public void NestedScopeOwnerMustBelongToItsChildScopesParent()
    {
        var parent = Scope("scope:parent", RootScopeId);
        var child = new DocumentScopeSnapshot(
            new DocumentScopeId("scope:child"),
            parent.Id,
            new SemanticElementId("test:owner"));
        var snapshot = Snapshot(
            elements: [Element("test:owner")],
            nestedScopes: [child, parent]);

        AssertRejected(
            snapshot,
            DocumentInvariantValidator.ScopeOwnerParentMismatchCode);
    }

    [Fact]
    public void OneSemanticOwnerCannotOwnMultipleChildScopes()
    {
        var ownerId = new SemanticElementId("test:owner");
        var snapshot = Snapshot(
            elements: [new SemanticElementSnapshot(ownerId, TestElementTypeId)],
            nestedScopes:
            [
                new DocumentScopeSnapshot(
                    new DocumentScopeId("scope:B"),
                    RootScopeId,
                    ownerId),
                new DocumentScopeSnapshot(
                    new DocumentScopeId("scope:A"),
                    RootScopeId,
                    ownerId),
            ]);

        var diagnostic = Assert.Single(AssertRejected(
            snapshot,
            DocumentInvariantValidator.ScopeOwnerDuplicateCode));
        Assert.Equal(ownerId.Value, diagnostic.SourceIdentity);
        Assert.Equal("scope:B", diagnostic.Context["ScopeId"]);
    }

    [Fact]
    public void ValidNestedScopeStateReconstructsAndCapturesExactly()
    {
        var ownerA = Element("test:owner-a");
        var ownerB = Element("test:owner-b");
        var nestedSource = Element("test:nested-source");
        var nestedTarget = Element("test:nested-target");
        var scopeA = new DocumentScopeSnapshot(
            new DocumentScopeId("scope:A"),
            RootScopeId,
            ownerA.Id);
        var scopeB = new DocumentScopeSnapshot(
            new DocumentScopeId("scope:B"),
            scopeA.Id,
            ownerB.Id);
        var snapshot = Snapshot(
            elements: [ownerA, ownerB, nestedSource, nestedTarget],
            relationships:
            [Relationship("test:relationship", nestedSource.Id, nestedTarget.Id)],
            nestedScopes: [scopeB, scopeA],
            memberships:
            [
                new SemanticElementScopeMembershipSnapshot(ownerB.Id, scopeA.Id),
                new SemanticElementScopeMembershipSnapshot(nestedSource.Id, scopeB.Id),
                new SemanticElementScopeMembershipSnapshot(nestedTarget.Id, scopeB.Id),
            ]);

        var result = DocumentReconstructor.Reconstruct(snapshot);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        var document = Assert.IsType<Document>(result.Document);
        Assert.Equal(snapshot, document.CaptureSnapshot());
        Assert.Equal(scopeB.Id, document.SemanticModel.GetScope(nestedSource.Id).Id);
        Assert.Equal(
            [scopeA.Id, scopeB.Id],
            document.SemanticModel.GetDescendants(RootScopeId)
                .Select(static scope => scope.Id));
    }

    [Fact]
    public void GenericReconstructionDoesNotInventANotationSpecificCrossScopeRule()
    {
        var source = Element("test:nested-source");
        var target = Element("test:root-target");
        var child = Scope("scope:child", RootScopeId);
        var relationship = Relationship("test:relationship", source.Id, target.Id);
        var snapshot = Snapshot(
            elements: [source, target],
            relationships: [relationship],
            nestedScopes: [child],
            memberships:
            [new SemanticElementScopeMembershipSnapshot(source.Id, child.Id)]);

        var result = DocumentReconstructor.Reconstruct(snapshot);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            child.Id,
            Assert.IsType<Document>(result.Document)
                .SemanticModel.GetScope(relationship.Id).Id);
    }

    private static System.Collections.Immutable.ImmutableArray<
        Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic>
        AssertRejected(DocumentSnapshot snapshot, params string[] expectedCodes)
    {
        var result = DocumentReconstructor.Reconstruct(snapshot);

        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.Equal(expectedCodes, result.Diagnostics.Select(static diagnostic => diagnostic.Code));
        return result.Diagnostics;
    }

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot>? elements = null,
        IEnumerable<SemanticRelationshipSnapshot>? relationships = null,
        IEnumerable<DocumentScopeSnapshot>? nestedScopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null) =>
        new(
            new SemanticModelSnapshot(
                TestDocumentId,
                TestRevision,
                elements,
                relationships,
                nestedScopes,
                memberships),
            new VisualModelSnapshot(TestDocumentId, TestRevision),
            new DocumentMetadataSnapshot(TestDocumentId, TestRevision));

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
