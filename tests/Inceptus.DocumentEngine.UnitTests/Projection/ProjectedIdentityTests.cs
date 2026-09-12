using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Projection;

public sealed class ProjectedIdentityTests
{
    private static readonly DocumentId DocumentId = new("test:document");
    private static readonly ProjectionRuleId RuleId = new("test:rule");
    private static readonly SemanticElementId SemanticId = new("test:semantic");
    private static readonly SemanticTypeId SemanticTypeId = new("test:type");

    [Fact]
    public void EquivalentIdentityTuplesProduceTheSameDeterministicIdentity()
    {
        var first = ProjectedObjectIdentity.Create(
            DocumentId,
            RuleId,
            ProjectionSourceKind.SemanticElement,
            SemanticId,
            ProjectedObjectKind.Node,
            "main");
        var second = ProjectedObjectIdentity.Create(
            new DocumentId("test:document"),
            new ProjectionRuleId("test:rule"),
            ProjectionSourceKind.SemanticElement,
            new SemanticElementId("test:semantic"),
            ProjectedObjectKind.Node,
            "main");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(first.Value, first.ToString());
        Assert.StartsWith("projected:", first.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryIdentityTupleComponentParticipatesInIdentity()
    {
        var baseline = CreateIdentity(
            DocumentId,
            RuleId,
            ProjectionSourceKind.SemanticElement,
            SemanticId,
            ProjectedObjectKind.Node,
            "main");
        ProjectedObjectId[] alternatives =
        [
            CreateIdentity(
                new DocumentId("test:other-document"),
                RuleId,
                ProjectionSourceKind.SemanticElement,
                SemanticId,
                ProjectedObjectKind.Node,
                "main"),
            CreateIdentity(
                DocumentId,
                new ProjectionRuleId("test:other-rule"),
                ProjectionSourceKind.SemanticElement,
                SemanticId,
                ProjectedObjectKind.Node,
                "main"),
            CreateIdentity(
                DocumentId,
                RuleId,
                ProjectionSourceKind.SemanticRelationship,
                SemanticId,
                ProjectedObjectKind.Node,
                "main"),
            CreateIdentity(
                DocumentId,
                RuleId,
                ProjectionSourceKind.SemanticElement,
                new SemanticElementId("test:other-semantic"),
                ProjectedObjectKind.Node,
                "main"),
            CreateIdentity(
                DocumentId,
                RuleId,
                ProjectionSourceKind.SemanticElement,
                SemanticId,
                ProjectedObjectKind.Label,
                "main"),
            CreateIdentity(
                DocumentId,
                RuleId,
                ProjectionSourceKind.SemanticElement,
                SemanticId,
                ProjectedObjectKind.Node,
                "other"),
        ];

        Assert.All(alternatives, alternative => Assert.NotEqual(baseline, alternative));
        Assert.Equal(alternatives.Length, alternatives.Distinct().Count());
    }

    [Fact]
    public void LengthPrefixedSegmentsPreventDelimiterCollisions()
    {
        var first = CreateIdentity(
            new DocumentId("test:a:b"),
            new ProjectionRuleId("test:c"),
            ProjectionSourceKind.SemanticElement,
            SemanticId,
            ProjectedObjectKind.Node,
            "key");
        var second = CreateIdentity(
            new DocumentId("test:a"),
            new ProjectionRuleId("b:test:c"),
            ProjectionSourceKind.SemanticElement,
            SemanticId,
            ProjectedObjectKind.Node,
            "key");
        var localKeyCaseVariant = CreateIdentity(
            new DocumentId("test:a:b"),
            new ProjectionRuleId("test:c"),
            ProjectionSourceKind.SemanticElement,
            SemanticId,
            ProjectedObjectKind.Node,
            "KEY");

        Assert.NotEqual(first, second);
        Assert.NotEqual(first, localKeyCaseVariant);
    }

    [Fact]
    public void NonIdentityTraceabilityDoesNotChangeProjectedIdentity()
    {
        var withoutVisual = Source();
        var withVisual = new ProjectionSourceTrace(
            DocumentId,
            RuleId,
            ProjectionSourceKind.SemanticElement,
            SemanticId,
            SemanticTypeId,
            "main",
            new VisualStateId("test:visual"));
        var withOtherSemanticType = new ProjectionSourceTrace(
            DocumentId,
            RuleId,
            ProjectionSourceKind.SemanticElement,
            SemanticId,
            new SemanticTypeId("test:other-type"),
            "main");

        Assert.NotEqual(withoutVisual, withVisual);
        Assert.NotEqual(withoutVisual, withOtherSemanticType);
        Assert.Equal(
            ProjectedObjectIdentity.Create(withoutVisual, ProjectedObjectKind.Node),
            ProjectedObjectIdentity.Create(withVisual, ProjectedObjectKind.Node));
        Assert.Equal(
            ProjectedObjectIdentity.Create(withoutVisual, ProjectedObjectKind.Node),
            ProjectedObjectIdentity.Create(withOtherSemanticType, ProjectedObjectKind.Node));
    }

    [Fact]
    public void SourceTraceIsImmutableAndUsesStructuralEquality()
    {
        var first = new ProjectionSourceTrace(
            DocumentId,
            RuleId,
            ProjectionSourceKind.SemanticElement,
            SemanticId,
            SemanticTypeId,
            "main",
            new VisualStateId("test:visual"));
        var same = new ProjectionSourceTrace(
            new DocumentId("test:document"),
            new ProjectionRuleId("test:rule"),
            ProjectionSourceKind.SemanticElement,
            new SemanticElementId("test:semantic"),
            new SemanticTypeId("test:type"),
            "main",
            new VisualStateId("test:visual"));

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.Equal(DocumentId, first.DocumentId);
        Assert.Equal(RuleId, first.RuleId);
        Assert.Equal(SemanticId, first.SemanticElementId);
        Assert.Equal(SemanticTypeId, first.SemanticTypeId);
        Assert.Equal(new VisualStateId("test:visual"), first.VisualStateId);
        Assert.Equal("main", first.LocalKey);
    }

    [Fact]
    public void IdentityCreationRejectsInvalidKindsAndLocalKeys()
    {
        Assert.Throws<ArgumentException>(() => new ProjectionSourceTrace(
            DocumentId,
            RuleId,
            ProjectionSourceKind.SemanticElement,
            SemanticId,
            SemanticTypeId,
            string.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectionSourceTrace(
            DocumentId,
            RuleId,
            (ProjectionSourceKind)99,
            SemanticId,
            SemanticTypeId,
            "main"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectedObjectIdentity.Create(
            DocumentId,
            RuleId,
            ProjectionSourceKind.SemanticElement,
            SemanticId,
            (ProjectedObjectKind)99,
            "main"));
        Assert.Throws<ArgumentException>(() => ProjectedObjectIdentity.Create(
            DocumentId,
            RuleId,
            ProjectionSourceKind.SemanticElement,
            SemanticId,
            ProjectedObjectKind.Node,
            "   "));
    }

    private static ProjectionSourceTrace Source() =>
        new(
            DocumentId,
            RuleId,
            ProjectionSourceKind.SemanticElement,
            SemanticId,
            SemanticTypeId,
            "main");

    private static ProjectedObjectId CreateIdentity(
        DocumentId documentId,
        ProjectionRuleId ruleId,
        ProjectionSourceKind sourceKind,
        SemanticElementId semanticElementId,
        ProjectedObjectKind objectKind,
        string localKey) =>
        ProjectedObjectIdentity.Create(
            documentId,
            ruleId,
            sourceKind,
            semanticElementId,
            objectKind,
            localKey);
}
