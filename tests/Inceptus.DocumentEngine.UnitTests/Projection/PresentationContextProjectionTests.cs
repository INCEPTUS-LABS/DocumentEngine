using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Projection;

public sealed class PresentationContextProjectionTests
{
    private static readonly DocumentId DocumentId = new("presentation-document");
    private static readonly SemanticElementId OwnerId = new("presentation-owner");
    private static readonly SemanticElementId FirstId = new("first-node");
    private static readonly SemanticElementId SecondId = new("second-node");
    private static readonly SemanticTypeId NodeType = new("test.node");
    private static readonly SemanticTypeId OwnerType = new("test.owner");
    private static readonly DocumentScopeId PeerScope = new("peer-process");
    private static readonly ProjectionRuleId RuleId = new("test.node-rule");

    [Fact]
    public void ProcessProjectionRemainsIsolatedWhenDocumentProviderIsRegistered()
    {
        var document = CreateDocument();
        var engine = CreateEngine(new Provider());
        var root = engine.Project(document);
        var peer = engine.Project(document, PeerScope);
        Assert.Equal(FirstId, Assert.Single(root.Graph!.Nodes).Source.SemanticElementId);
        Assert.Equal(SecondId, Assert.Single(peer.Graph!.Nodes).Source.SemanticElementId);
        Assert.DoesNotContain(root.Graph.Nodes.Concat(peer.Graph.Nodes), node => node.Source.SemanticElementId == OwnerId);
    }

    [Fact]
    public void ExplicitDocumentContextProjectsOneInstanceOfEachIncludedElement()
    {
        var document = CreateDocument();
        var engine = CreateEngine(new Provider());
        var context = new DocumentPresentationContext(OwnerId);
        var result = engine.Project(document, context);
        Assert.True(result.IsSuccessful);
        Assert.Equal(3, result.Graph!.NodeCount);
        Assert.Equal(3, result.Graph.Nodes.Select(static node => node.Id).Distinct().Count());
        Assert.Equal(context, Assert.Single(engine.GetDocumentContexts(document)));
        Assert.Equal(new DocumentScopeId(DocumentId.Value), document.SemanticModel.GetScope(FirstId).Id);
        Assert.Equal(PeerScope, document.SemanticModel.GetScope(SecondId).Id);
        Assert.False(document.SemanticModel.TryGetScope(OwnerId, out _));
    }

    [Fact]
    public void StaleOrIncompleteViewPlanFailsWithoutPartialProjection()
    {
        var document = CreateDocument();
        var context = new DocumentPresentationContext(OwnerId);
        var stale = new PresentationViewPlan(DocumentId, new DocumentRevision(1), context,
            [FirstId], [], [new(new DocumentScopeId(DocumentId.Value), new VectorD(0, 0))]);
        var engine = CreateEngine(new Provider());
        var result = engine.Project(document, stale);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.Graph);
        Assert.Contains(result.Diagnostics, item => item.Code == "PRESENTATION_CONTEXT_INVALID");
        var incomplete = new PresentationViewPlan(DocumentId, document.Revision, context, [SecondId], []);
        Assert.False(engine.Project(document, incomplete).IsSuccessful);
    }

    [Fact]
    public void MissingOwnerOrProviderIsRejectedRatherThanUsingRootScope()
    {
        var document = CreateDocument();
        Assert.False(new ProjectionEngine().Project(document, new DocumentPresentationContext(OwnerId)).IsSuccessful);
        Assert.False(CreateEngine(new Provider()).Project(document,
            new DocumentPresentationContext(new SemanticElementId("missing"))).IsSuccessful);
    }

    [Fact]
    public void ScopePlacementIsReversibleAndNeverChangesAuthoredGeometry()
    {
        var placement = new PresentationScopePlacement(PeerScope, new VectorD(250, 430));
        var local = new PointD(20, 30);
        var bounds = new RectD(20, 30, 120, 80);
        Assert.Equal(new PointD(270, 460), placement.MapLocalToView(local));
        Assert.Equal(local, placement.MapViewToLocal(placement.MapLocalToView(local)));
        Assert.Equal(bounds, placement.MapViewToLocal(placement.MapLocalToView(bounds)));
        var item = new Canvas2DSceneItem(placement, new SceneObjectId("mapped-item"), Canvas2DSceneLayer.Content,
            0, Canvas2DSceneGeometry.Rectangle(bounds), new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.RegisteredExtension, stableSourceKey: "mapping-test"));
        Assert.Equal(local, item.MapPresentationToLocal(new PointD(270, 460)));
        Assert.Equal(bounds, item.MapPresentationToLocal(placement.MapLocalToView(bounds)));
    }

    [Fact]
    public void DuplicateProcessPlacementIsRejectedRatherThanDuplicatingProcessInstances()
    {
        var placement = new PresentationScopePlacement(PeerScope, new VectorD(0, 0));
        Assert.Throws<ArgumentException>(() => new PresentationViewPlan(DocumentId, DocumentRevision.Zero,
            new DocumentPresentationContext(OwnerId), [SecondId], [], [placement, placement]));
    }

    private static DocumentSnapshot CreateDocument()
    {
        var semantic = new SemanticModelSnapshot(DocumentId, DocumentRevision.Zero,
            [new(OwnerId, OwnerType, containmentKind: SemanticElementContainmentKind.Document),
             new(FirstId, NodeType), new(SecondId, NodeType)],
            nestedScopes: [new(PeerScope)], scopeMemberships: [new(SecondId, PeerScope)]);
        return new DocumentSnapshot(semantic, new VisualModelSnapshot(DocumentId, DocumentRevision.Zero),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
    }

    private static ProjectionEngine CreateEngine(Provider provider) => new(
        [new(RuleId, ProjectionSourceKind.SemanticElement, NodeType, new NodeRule(RuleId)),
         new(new ProjectionRuleId("test.owner-rule"), ProjectionSourceKind.SemanticElement, OwnerType, new NodeRule(new("test.owner-rule")))],
        [new(OwnerType, provider)]);

    private sealed class Provider : IPresentationViewProvider
    {
        public ImmutableArray<DocumentPresentationContext> GetContexts(DocumentSnapshot document) => [new(OwnerId)];

        public PresentationViewResolution Resolve(DocumentSnapshot document, DocumentPresentationContext context) =>
            PresentationViewResolution.Success(new PresentationViewPlan(document.DocumentId, document.Revision, context,
                [OwnerId, FirstId, SecondId], [],
                [new(document.SemanticModel.RootScopeId, new VectorD(0, 100)), new(PeerScope, new VectorD(0, 400))]));
    }

    private sealed class NodeRule(ProjectionRuleId ruleId) : IProjectionRule
    {
        public ProjectionRuleResult Project(ProjectionRuleInput input, CancellationToken cancellationToken) =>
            ProjectionRuleResult.Success(new ProjectionRuleContribution(nodes:
                [new ProjectedNode(new ProjectionSourceTrace(input.DocumentId, ruleId, input.SourceKind,
                    input.SemanticId, input.SemanticTypeId, "node"))]));
    }
}
