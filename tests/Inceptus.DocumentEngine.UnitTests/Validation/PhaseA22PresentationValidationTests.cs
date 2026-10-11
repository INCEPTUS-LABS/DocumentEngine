using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Validation;

namespace Inceptus.DocumentEngine.UnitTests.Validation;

public sealed class PhaseA22PresentationValidationTests
{
    [Fact]
    public void DocumentValidationUsesExactPresentationMembershipWithoutInventingProcessScope()
    {
        var (document, plan) = Sample();
        var context = new ModelValidationContext(document, plan);
        var rule = new MembershipRule();
        var result = new ModelValidationEngine(new ModelValidationCatalog([rule]))
            .Validate(context, sourceGeneration: 12);

        Assert.Same(document, context.Document);
        Assert.Same(plan, context.PresentationPlan);
        Assert.Equal(plan.Context, context.PresentationContext);
        Assert.Equal(plan.Context, result.PresentationContext);
        Assert.Equal(document.DocumentId, result.DocumentId);
        Assert.Equal(document.Revision, result.SourceRevision);
        Assert.Equal(12, result.SourceGeneration);
        Assert.True(context.IncludesScope(document.SemanticModel.RootScopeId));
        Assert.False(context.IncludesScope(new DocumentScopeId("other-process")));
        Assert.True(context.Contains(new SemanticElementId("participant")));
        Assert.True(context.Contains(new SemanticElementId("visible-node")));
        Assert.False(context.Contains(new SemanticElementId("unrepresented-node")));
        Assert.Throws<InvalidOperationException>(() => context.ActiveScopeId);
        Assert.Throws<InvalidOperationException>(() => result.ScopeId);
        Assert.Equal(new SemanticElementId("participant"), Assert.Single(result.Issues).Target.SemanticElementId);
        Assert.Equal(document, Sample().Document);
    }

    [Fact]
    public void ExistingProcessValidationRetainsScopeMembershipAndSnapshotCompatibility()
    {
        var (document, _) = Sample();
        var context = new ModelValidationContext(document);
        var result = new ModelValidationEngine().Validate(context);

        Assert.Null(context.PresentationPlan);
        Assert.Equal(document.SemanticModel.RootScopeId, context.ActiveScopeId);
        Assert.Equal(context.ActiveScopeId, result.ScopeId);
        Assert.Equal(new ProcessScopePresentationContext(context.ActiveScopeId), result.PresentationContext);
        Assert.True(context.Contains(new SemanticElementId("visible-node")));
        Assert.True(context.Contains(new SemanticElementId("unrepresented-node")));
        Assert.False(context.Contains(new SemanticElementId("participant")));
        Assert.Equal(result.PresentationContext,
            new ValidationSnapshot(document.DocumentId, document.Revision).PresentationContext);
    }

    [Theory]
    [InlineData("document")]
    [InlineData("revision")]
    [InlineData("owner")]
    [InlineData("scoped-owner")]
    [InlineData("scope")]
    [InlineData("element")]
    [InlineData("relationship")]
    public void ValidationRejectsStaleOrUnresolvedPresentationMembership(string invalid)
    {
        var (document, original) = Sample();
        var plan = new PresentationViewPlan(
            invalid == "document" ? new DocumentId("different") : document.DocumentId,
            invalid == "revision" ? document.Revision.Increment() : document.Revision,
            new DocumentPresentationContext(new SemanticElementId(invalid switch
            {
                "owner" => "missing-owner",
                "scoped-owner" => "visible-node",
                _ => "collaboration",
            })),
            invalid == "element" ? [new SemanticElementId("missing-element")] : original.ElementIds,
            invalid == "relationship" ? [new SemanticElementId("missing-relationship")] : [],
            invalid == "scope"
                ? [new PresentationScopePlacement(new DocumentScopeId("missing-scope"), new VectorD(0, 0))]
                : original.ScopePlacements);

        Assert.Throws<ArgumentException>(() => new ModelValidationContext(document, plan));
    }

    private static (DocumentSnapshot Document, PresentationViewPlan Plan) Sample()
    {
        var id = new DocumentId("a22-validation");
        var revision = new DocumentRevision(4);
        var nodeType = new SemanticTypeId("test:node");
        var semantic = new SemanticModelSnapshot(id, revision,
        [
            new(new SemanticElementId("collaboration"), nodeType, containmentKind: SemanticElementContainmentKind.Document),
            new(new SemanticElementId("participant"), nodeType, containmentKind: SemanticElementContainmentKind.Document),
            new(new SemanticElementId("visible-node"), nodeType),
            new(new SemanticElementId("unrepresented-node"), nodeType),
        ]);
        var document = new DocumentSnapshot(semantic, new VisualModelSnapshot(id, revision),
            new DocumentMetadataSnapshot(id, revision));
        return (document, new PresentationViewPlan(id, revision,
            new DocumentPresentationContext(new SemanticElementId("collaboration")),
            [new SemanticElementId("participant"), new SemanticElementId("visible-node")], [],
            [new PresentationScopePlacement(semantic.RootScopeId, new VectorD(40, 100))]));
    }

    private sealed class MembershipRule : IModelValidationRule
    {
        public ModelValidationRuleId RuleId { get; } = new("test:membership");

        public ImmutableArray<ModelValidationIssue> Validate(ModelValidationContext context)
        {
            Assert.IsType<DocumentPresentationContext>(context.PresentationContext);
            return context.Document.SemanticModel.Elements
                .Where(element => element.ContainmentKind == SemanticElementContainmentKind.Document && context.Contains(element.Id))
                .Select(element => new ModelValidationIssue(RuleId, ModelValidationSeverity.Info, "TEST_MEMBER", "Visible document member",
                    ModelValidationTarget.ForSemanticElement(element.Id)))
                .ToImmutableArray();
        }
    }
}
