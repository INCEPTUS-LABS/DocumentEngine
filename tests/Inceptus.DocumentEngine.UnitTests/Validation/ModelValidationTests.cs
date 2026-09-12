using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Validation;

namespace Inceptus.DocumentEngine.UnitTests.Validation;

public sealed class ModelValidationTests
{
    private static readonly DocumentId DocumentId = new("validation:document");
    private static readonly DocumentRevision Revision = new(7);
    private static readonly SemanticElementId SemanticId = new("validation:semantic");
    private static readonly VisualStateId VisualId = new("validation:visual");

    [Fact]
    public void ContractsProvideThreeSeveritiesAndDocumentSemanticAndVisualTargets()
    {
        Assert.Equal(0, (int)ModelValidationSeverity.Error);
        Assert.Equal(1, (int)ModelValidationSeverity.Warning);
        Assert.Equal(2, (int)ModelValidationSeverity.Info);

        Assert.True(ModelValidationTarget.Document.IsDocument);
        Assert.Null(ModelValidationTarget.Document.SemanticElementId);
        var semantic = ModelValidationTarget.ForSemanticElement(SemanticId);
        Assert.Equal(SemanticId, semantic.SemanticElementId);
        Assert.Null(semantic.VisualStateId);
        var visual = ModelValidationTarget.ForVisualState(SemanticId, VisualId);
        Assert.Equal(SemanticId, visual.SemanticElementId);
        Assert.Equal(VisualId, visual.VisualStateId);
    }

    [Fact]
    public void IssueIdentityIsStableAndIncludesRuleCodeTargetAndDiscriminator()
    {
        var ruleId = new ModelValidationRuleId("test:rule");
        var first = new ModelValidationIssue(
            ruleId,
            ModelValidationSeverity.Warning,
            "TEST_CODE",
            "First message",
            ModelValidationTarget.ForVisualState(SemanticId, VisualId),
            "related-a");
        var sameIdentity = new ModelValidationIssue(
            ruleId,
            ModelValidationSeverity.Warning,
            "TEST_CODE",
            "Changed display text",
            ModelValidationTarget.ForVisualState(SemanticId, VisualId),
            "related-a");
        var different = new ModelValidationIssue(
            ruleId,
            ModelValidationSeverity.Warning,
            "TEST_CODE",
            "First message",
            ModelValidationTarget.ForVisualState(SemanticId, VisualId),
            "related-b");

        Assert.Equal(first.Id, sameIdentity.Id);
        Assert.NotEqual(first.Id, different.Id);
        Assert.Equal("TEST_CODE", first.Code);
        Assert.Equal("First message", first.Message);
        Assert.Equal(ModelValidationSeverity.Warning, first.Severity);
    }

    [Fact]
    public void CatalogIsEmptyOrCanonicallyOrderedAndRejectsNullAndDuplicateRules()
    {
        Assert.Empty(ModelValidationCatalog.Empty.Rules);
        var second = new StubRule("test:rule/b");
        var first = new StubRule("test:rule/a");
        var catalog = new ModelValidationCatalog([second, first]);
        Assert.Equal([first, second], catalog.Rules.ToArray());

        Assert.Throws<ArgumentException>(() => new ModelValidationCatalog([null!]));
        Assert.Throws<ArgumentException>(() => new ModelValidationCatalog(
            [first, new StubRule(first.RuleId.Value)]));
    }

    [Fact]
    public void EngineIsReadOnlyOrdersFindingsAndRejectsDuplicateIssueIdentity()
    {
        var snapshot = Snapshot();
        var beforeHash = snapshot.GetHashCode();
        var warningRule = new StubRule(
            "test:warning",
            new ModelValidationIssue(
                new ModelValidationRuleId("test:warning"),
                ModelValidationSeverity.Warning,
                "B_CODE",
                "Warning",
                ModelValidationTarget.ForSemanticElement(SemanticId)));
        var errorRule = new StubRule(
            "test:error",
            new ModelValidationIssue(
                new ModelValidationRuleId("test:error"),
                ModelValidationSeverity.Error,
                "Z_CODE",
                "Error"));
        var engine = new ModelValidationEngine(new ModelValidationCatalog(
            [warningRule, errorRule]));

        var result = engine.Validate(new ModelValidationContext(snapshot), sourceGeneration: 11);

        Assert.Equal(DocumentId, result.DocumentId);
        Assert.Equal(snapshot.SemanticModel.RootScopeId, result.ScopeId);
        Assert.Equal(Revision, result.SourceRevision);
        Assert.Equal(11, result.SourceGeneration);
        Assert.Equal(["Z_CODE", "B_CODE"], result.Issues.Select(static issue => issue.Code));
        Assert.Equal(beforeHash, snapshot.GetHashCode());
        Assert.Equal(snapshot, Snapshot());

        var duplicate = warningRule.Findings[0];
        Assert.Throws<ArgumentException>(() => engine.Validate(
            new ModelValidationContext(snapshot),
            [duplicate]));
    }

    [Fact]
    public void ValidationContextAndSnapshotCarryExactScopeWithRootCompatibility()
    {
        var source = Snapshot();
        var nestedScopeId = new DocumentScopeId("validation:scope:nested");
        var nested = new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                Revision,
                source.SemanticModel.Elements,
                nestedScopes:
                [
                    new DocumentScopeSnapshot(
                        nestedScopeId,
                        source.SemanticModel.RootScopeId,
                        SemanticId),
                ]),
            source.VisualModel,
            source.Metadata);

        var rootContext = new ModelValidationContext(nested);
        var nestedContext = new ModelValidationContext(nested, nestedScopeId);
        var result = new ModelValidationEngine().Validate(nestedContext);
        var compatible = new ValidationSnapshot(DocumentId, Revision);

        Assert.Equal(nested.SemanticModel.RootScopeId, rootContext.ActiveScopeId);
        Assert.Equal(nestedScopeId, nestedContext.ActiveScopeId);
        Assert.Equal(nestedScopeId, result.ScopeId);
        Assert.Equal(nested.SemanticModel.RootScopeId, compatible.ScopeId);
        Assert.Throws<ArgumentException>(() =>
            new ModelValidationContext(nested, new DocumentScopeId("missing")));
    }

    [Fact]
    public void EmptyEngineReturnsNoFindingsAndRuleExceptionsPropagate()
    {
        var context = new ModelValidationContext(Snapshot());
        Assert.Empty(new ModelValidationEngine().Validate(context).Issues);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ModelValidationEngine(new ModelValidationCatalog([new ThrowingRule()]))
                .Validate(context));
        Assert.Equal("validation failure", exception.Message);
    }

    [Fact]
    public void CurrentNoRouteMapsProjectedTraceToSemanticAndVisualTarget()
    {
        var ruleId = new ProjectionRuleId("test:projection");
        var nodeSource = new ProjectionSourceTrace(
            DocumentId,
            ruleId,
            ProjectionSourceKind.SemanticElement,
            new SemanticElementId("source"),
            new SemanticTypeId("test:node"),
            "node");
        var nodeTarget = new ProjectionSourceTrace(
            DocumentId,
            ruleId,
            ProjectionSourceKind.SemanticElement,
            new SemanticElementId("target"),
            new SemanticTypeId("test:node"),
            "node");
        var edgeSource = new ProjectionSourceTrace(
            DocumentId,
            ruleId,
            ProjectionSourceKind.SemanticRelationship,
            SemanticId,
            new SemanticTypeId("test:edge"),
            "edge",
            VisualId);
        var sourceNode = new ProjectedNode(nodeSource);
        var targetNode = new ProjectedNode(nodeTarget);
        var edge = new ProjectedEdge(edgeSource, sourceNode.Id, targetNode.Id);
        var graph = new ProjectedGraph(
            DocumentId,
            Revision,
            [sourceNode, targetNode],
            [edge]);
        var routing = new RoutingResult(
            DocumentId,
            Revision,
            new AlgorithmId("test:layout"),
            new AlgorithmId("test:routing"),
            new RoutingComputation(noRouteEdgeIds: [edge.Id]));

        var issue = Assert.Single(
            RoutingModelValidationIssueProvider.CreateIssues(graph, routing));
        Assert.Equal(RoutingModelValidationIssueProvider.NoLegalRouteCode, issue.Code);
        Assert.Equal(ModelValidationSeverity.Warning, issue.Severity);
        Assert.Equal(SemanticId, issue.Target.SemanticElementId);
        Assert.Equal(VisualId, issue.Target.VisualStateId);

        var stale = new RoutingResult(
            DocumentId,
            Revision.Increment(),
            new AlgorithmId("test:layout"),
            new AlgorithmId("test:routing"),
            RoutingComputation.Empty);
        Assert.Throws<ArgumentException>(() =>
            RoutingModelValidationIssueProvider.CreateIssues(graph, stale));
    }

    private static DocumentSnapshot Snapshot() => new(
        new SemanticModelSnapshot(
            DocumentId,
            Revision,
            [new SemanticElementSnapshot(SemanticId, new SemanticTypeId("test:node"))]),
        new VisualModelSnapshot(
            DocumentId,
            Revision,
            [
                new VisualStateSnapshot(
                    VisualId,
                    SemanticId,
                    new PointD(10d, 20d),
                    new SizeD(100d, 60d),
                    VisualPlacementMode.Manual),
            ]),
        new DocumentMetadataSnapshot(DocumentId, Revision));

    private sealed class StubRule : IModelValidationRule
    {
        internal StubRule(string id, params ModelValidationIssue[] findings)
        {
            RuleId = new ModelValidationRuleId(id);
            Findings = [.. findings];
        }

        public ModelValidationRuleId RuleId { get; }

        internal ImmutableArray<ModelValidationIssue> Findings { get; }

        public ImmutableArray<ModelValidationIssue> Validate(ModelValidationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return Findings;
        }
    }

    private sealed class ThrowingRule : IModelValidationRule
    {
        public ModelValidationRuleId RuleId { get; } = new("test:throwing");

        public ImmutableArray<ModelValidationIssue> Validate(ModelValidationContext context) =>
            throw new InvalidOperationException("validation failure");
    }
}
