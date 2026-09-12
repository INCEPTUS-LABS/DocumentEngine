using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Projection;

public sealed class ProjectionRuleContractTests
{
    private static readonly DocumentId DocumentId = new("test:document");
    private static readonly DocumentRevision Revision = new(7);

    [Fact]
    public void ContextDefensivelyCopiesConfigurationAndUsesStructuralEquality()
    {
        var configuration = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:mode", PropertyValue.FromText("stable")),
        };
        var first = new ProjectionContext(configuration);
        configuration.Clear();
        var same = new ProjectionContext(
        [
            new("test:mode", PropertyValue.FromText("stable")),
        ]);

        Assert.Equal("stable", first.Configuration["test:mode"].TextValue);
        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.Empty(ProjectionContext.Empty.Configuration);
    }

    [Fact]
    public void ElementRuleInputPreservesCoherentSourceAndOrdersVisuals()
    {
        var element = Element("test:element", "test:node");
        var visualZ = Visual("test:z", element.Id);
        var visualA = Visual("test:a", element.Id);
        var visuals = new List<VisualStateSnapshot> { visualZ, visualA };

        var input = new ElementProjectionRuleInput(
            DocumentId,
            Revision,
            ProjectionContext.Empty,
            element,
            visuals);
        visuals.Clear();

        Assert.Equal(DocumentId, input.DocumentId);
        Assert.Equal(Revision, input.SourceRevision);
        Assert.Same(element, input.Element);
        Assert.Equal(ProjectionSourceKind.SemanticElement, input.SourceKind);
        Assert.Equal(element.Id, input.SemanticId);
        Assert.Equal(element.TypeId, input.SemanticTypeId);
        Assert.Equal(["test:a", "test:z"],
            input.VisualStates.Select(visual => visual.Id.Value));
        AssertReadOnly(input.VisualStates);
    }

    [Fact]
    public void RelationshipRuleInputPreservesResolvedEndpoints()
    {
        var source = Element("test:source", "test:node");
        var target = Element("test:target", "test:node");
        var relationship = new SemanticRelationshipSnapshot(
            new SemanticElementId("test:relationship"),
            new SemanticTypeId("test:edge"),
            source.Id,
            target.Id);

        var input = new RelationshipProjectionRuleInput(
            DocumentId,
            Revision,
            ProjectionContext.Empty,
            relationship,
            source,
            target,
            [Visual("test:edge-visual", relationship.Id)]);

        Assert.Same(relationship, input.Relationship);
        Assert.Same(source, input.SourceElement);
        Assert.Same(target, input.TargetElement);
        Assert.Equal(ProjectionSourceKind.SemanticRelationship, input.SourceKind);
        Assert.Equal(relationship.Id, input.SemanticId);
        Assert.Equal(relationship.TypeId, input.SemanticTypeId);
    }

    [Fact]
    public void RuleInputsRejectMismatchedVisualAndRelationshipReferences()
    {
        var source = Element("test:source", "test:node");
        var target = Element("test:target", "test:node");
        var other = Element("test:other", "test:node");
        var relationship = new SemanticRelationshipSnapshot(
            new SemanticElementId("test:relationship"),
            new SemanticTypeId("test:edge"),
            source.Id,
            target.Id);

        Assert.Throws<ArgumentException>(() => new ElementProjectionRuleInput(
            DocumentId,
            Revision,
            ProjectionContext.Empty,
            source,
            [Visual("test:wrong", target.Id)]));
        Assert.Throws<ArgumentException>(() => new RelationshipProjectionRuleInput(
            DocumentId,
            Revision,
            ProjectionContext.Empty,
            relationship,
            other,
            target));
    }

    [Fact]
    public void RegistrationPreservesStableApplicabilityWithoutClrTypeSelection()
    {
        var rule = new EmptyRule();
        var registration = new ProjectionRuleRegistration(
            new ProjectionRuleId("test:rule"),
            ProjectionSourceKind.SemanticElement,
            new SemanticTypeId("test:node"),
            rule);

        Assert.Equal(new ProjectionRuleId("test:rule"), registration.RuleId);
        Assert.Equal(ProjectionSourceKind.SemanticElement, registration.SourceKind);
        Assert.Equal(new SemanticTypeId("test:node"), registration.SemanticTypeId);
        Assert.Same(rule, registration.Rule);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectionRuleRegistration(
            new ProjectionRuleId("test:invalid"),
            (ProjectionSourceKind)99,
            new SemanticTypeId("test:node"),
            rule));
    }

    [Fact]
    public void RuleContractReceivesOnlyImmutableInputAndCancellation()
    {
        var rule = new EmptyRule();
        var input = new ElementProjectionRuleInput(
            DocumentId,
            Revision,
            ProjectionContext.Empty,
            Element("test:element", "test:node"));
        using var cancellation = new CancellationTokenSource();

        var result = rule.Project(input, cancellation.Token);

        Assert.True(result.Succeeded);
        Assert.Same(input, rule.ObservedInput);
        Assert.Equal(cancellation.Token, rule.ObservedCancellationToken);
    }

    private static SemanticElementSnapshot Element(string id, string typeId) =>
        new(new SemanticElementId(id), new SemanticTypeId(typeId));

    private static VisualStateSnapshot Visual(string id, SemanticElementId semanticId) =>
        new(
            new VisualStateId(id),
            semanticId,
            new PointD(10d, 20d),
            new SizeD(100d, 60d),
            VisualPlacementMode.Manual);

    private static void AssertReadOnly<T>(ImmutableArray<T> values)
    {
        var mutableView = (IList<T>)values;
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
    }

    private sealed class EmptyRule : IProjectionRule
    {
        internal ProjectionRuleInput? ObservedInput { get; private set; }

        internal CancellationToken ObservedCancellationToken { get; private set; }

        public ProjectionRuleResult Project(
            ProjectionRuleInput input,
            CancellationToken cancellationToken)
        {
            ObservedInput = input;
            ObservedCancellationToken = cancellationToken;
            return ProjectionRuleResult.Success(ProjectionRuleContribution.Empty);
        }
    }
}
