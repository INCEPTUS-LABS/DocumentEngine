using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Visuals;

public sealed class ElementConnectorAnchorPolicyTests
{
    private static readonly SemanticTypeId ElementTypeId = new("test:m0:element");

    [Fact]
    public void PerEdgeModesAndRoleCapabilitiesAreImmutableAndIndependent()
    {
        var definition = Definition(
            "left-both",
            ConnectorAnchorSide.Left,
            ConnectorAnchorRoleCapability.SourceOrTarget,
            0);
        var policy = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.DynamicUnlimited(
                ConnectorAnchorRoleCapability.Source),
            EdgeConnectorAnchorPolicy.DynamicSingle(
                ConnectorAnchorRoleCapability.Target),
            EdgeConnectorAnchorPolicy.Predefined([definition]));

        Assert.Equal(ConnectorAnchorPolicyMode.Disabled, policy.Top.Mode);
        Assert.Equal(ConnectorAnchorPolicyMode.DynamicUnlimited, policy.Right.Mode);
        Assert.True(policy.Right.Allows(ConnectorAnchorRole.Source));
        Assert.False(policy.Right.Allows(ConnectorAnchorRole.Target));
        Assert.Equal(ConnectorAnchorPolicyMode.DynamicSingle, policy.Bottom.Mode);
        Assert.False(policy.Bottom.Allows(ConnectorAnchorRole.Source));
        Assert.True(policy.Bottom.Allows(ConnectorAnchorRole.Target));
        Assert.Equal(ConnectorAnchorPolicyMode.Predefined, policy.Left.Mode);
        Assert.Equal(definition, Assert.Single(policy.Left.PredefinedAnchors));
        Assert.Same(policy.Top, policy.ForSide(ConnectorAnchorSide.Top));
        Assert.Same(policy.Right, policy.ForSide(ConnectorAnchorSide.Right));
        Assert.Same(policy.Bottom, policy.ForSide(ConnectorAnchorSide.Bottom));
        Assert.Same(policy.Left, policy.ForSide(ConnectorAnchorSide.Left));
    }

    [Fact]
    public void PolicyConstructionRejectsInvalidRolesDefinitionsAndSidePlacement()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EdgeConnectorAnchorPolicy.DynamicUnlimited(
                ConnectorAnchorRoleCapability.None));
        Assert.Throws<ArgumentException>(() =>
            EdgeConnectorAnchorPolicy.Predefined([]));
        Assert.Throws<ArgumentException>(() =>
            EdgeConnectorAnchorPolicy.Predefined(
            [
                Definition("one", ConnectorAnchorSide.Right,
                    ConnectorAnchorRoleCapability.Source, 0),
                Definition("gap", ConnectorAnchorSide.Right,
                    ConnectorAnchorRoleCapability.Target, 2),
            ]));
        Assert.Throws<ArgumentException>(() => new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Predefined(
            [
                Definition("wrong-side", ConnectorAnchorSide.Right,
                    ConnectorAnchorRoleCapability.Source, 0),
            ]),
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled));
    }

    [Fact]
    public void RegistryUsesExplicitPolicyRejectsDuplicatesAndFallsBackToUnlimited()
    {
        var explicitPolicy = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.DynamicSingle(),
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.DynamicUnlimited());
        var registry = new ElementConnectorAnchorPolicyRegistry(
        [
            new ElementConnectorAnchorPolicyRegistration(ElementTypeId, explicitPolicy),
        ]);

        Assert.Same(explicitPolicy, registry.Resolve(ElementTypeId));
        Assert.Equal(
            ElementConnectorAnchorPolicy.DynamicUnlimitedAllEdges,
            registry.Resolve(new SemanticTypeId("test:unknown")));
        Assert.All(Enum.GetValues<ConnectorAnchorSide>(), side =>
            Assert.Equal(
                ConnectorAnchorPolicyMode.DynamicUnlimited,
                registry.Resolve(new SemanticTypeId("test:unknown")).ForSide(side).Mode));
        Assert.Throws<ArgumentException>(() => new ElementConnectorAnchorPolicyRegistry(
        [
            new ElementConnectorAnchorPolicyRegistration(ElementTypeId, explicitPolicy),
            new ElementConnectorAnchorPolicyRegistration(ElementTypeId, explicitPolicy),
        ]));
    }

    [Fact]
    public void ResolverUnifiesDynamicAndPredefinedAnchorsWithoutPersistingDefinitions()
    {
        var dynamic = new ConnectorAnchor(
            new ConnectorAnchorId("test:dynamic"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Target,
            0);
        var policy = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Predefined(
            [
                Definition("top-source", ConnectorAnchorSide.Top,
                    ConnectorAnchorRoleCapability.Source, 0),
                Definition("top-both", ConnectorAnchorSide.Top,
                    ConnectorAnchorRoleCapability.SourceOrTarget, 1),
                Definition("top-target", ConnectorAnchorSide.Top,
                    ConnectorAnchorRoleCapability.Target, 2),
            ]),
            EdgeConnectorAnchorPolicy.DynamicUnlimited(),
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled);
        var registry = Provider(policy);
        var visual = Visual("owner-a", [dynamic]);

        var resolved = ElementConnectorAnchorResolver.Resolve(
            visual,
            ElementTypeId,
            registry);

        Assert.Single(visual.ConnectorAnchors);
        var top = resolved.Where(anchor => anchor.Side == ConnectorAnchorSide.Top).ToArray();
        Assert.Equal(3, top.Length);
        Assert.All(top, anchor => Assert.Equal(
            ResolvedConnectorAnchorKind.Predefined,
            anchor.Kind));
        Assert.Equal([0, 1, 2], top.Select(anchor => anchor.Order));
        Assert.Equal(
            [0.25d, 0.5d, 0.75d],
            top.Select(anchor => ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(
                anchor.Order,
                top.Length)));
        var resolvedDynamic = Assert.Single(resolved.Where(anchor => anchor.IsDynamic));
        Assert.Equal(dynamic.Id, resolvedDynamic.Id);
        Assert.True(resolvedDynamic.Allows(ConnectorAnchorRole.Target));
        Assert.False(resolvedDynamic.Allows(ConnectorAnchorRole.Source));
    }

    [Fact]
    public void PredefinedReferenceIdentityIsStableDistinctAndOwnerSpecific()
    {
        var definitionId = new PredefinedConnectorAnchorDefinitionId("definition:right:middle");
        var ownerA = new VisualStateId("visual:a");
        var ownerB = new VisualStateId("visual:b");

        var first = ConnectorAnchorReferenceIdentity.ForPredefined(ownerA, definitionId);
        var equivalent = ConnectorAnchorReferenceIdentity.ForPredefined(ownerA, definitionId);
        var otherOwner = ConnectorAnchorReferenceIdentity.ForPredefined(ownerB, definitionId);

        Assert.Equal(first, equivalent);
        Assert.NotEqual(first, otherOwner);
        Assert.StartsWith("inceptus:predefined-connector-anchor:", first.Value,
            StringComparison.Ordinal);
        Assert.True(ConnectorAnchorReferenceIdentity.IsPredefinedReference(first));
        Assert.False(ConnectorAnchorReferenceIdentity.IsPredefinedReference(
            new ConnectorAnchorId("dynamic")));
    }

    [Fact]
    public void ResolvedAnchorConstructorRejectsIncoherentKindRoleAndIdentityCombinations()
    {
        var owner = new VisualStateId("visual:owner");
        var otherOwner = new VisualStateId("visual:other");
        var definitionId = new PredefinedConnectorAnchorDefinitionId("definition:right");
        var canonical = ConnectorAnchorReferenceIdentity.ForPredefined(owner, definitionId);
        var otherOwnerReference = ConnectorAnchorReferenceIdentity.ForPredefined(
            otherOwner,
            definitionId);

        Assert.Throws<ArgumentException>(() => new ResolvedConnectorAnchor(
            new ConnectorAnchorId("dynamic:both"),
            owner,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.SourceOrTarget,
            0,
            ResolvedConnectorAnchorKind.Dynamic));
        Assert.Throws<ArgumentException>(() => new ResolvedConnectorAnchor(
            canonical,
            owner,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source,
            0,
            ResolvedConnectorAnchorKind.Dynamic));
        Assert.Throws<ArgumentException>(() => new ResolvedConnectorAnchor(
            new ConnectorAnchorId("dynamic:with-definition"),
            owner,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source,
            0,
            ResolvedConnectorAnchorKind.Dynamic,
            definitionId));
        Assert.Throws<ArgumentException>(() => new ResolvedConnectorAnchor(
            canonical,
            owner,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.SourceOrTarget,
            0,
            ResolvedConnectorAnchorKind.Predefined));
        Assert.Throws<ArgumentException>(() => new ResolvedConnectorAnchor(
            new ConnectorAnchorId("predefined:not-canonical"),
            owner,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.SourceOrTarget,
            0,
            ResolvedConnectorAnchorKind.Predefined,
            definitionId));
        Assert.Throws<ArgumentException>(() => new ResolvedConnectorAnchor(
            otherOwnerReference,
            owner,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.SourceOrTarget,
            0,
            ResolvedConnectorAnchorKind.Predefined,
            definitionId));

        var dynamic = new ResolvedConnectorAnchor(
            new ConnectorAnchorId("dynamic:source"),
            owner,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source,
            0,
            ResolvedConnectorAnchorKind.Dynamic);
        var predefined = new ResolvedConnectorAnchor(
            canonical,
            owner,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.SourceOrTarget,
            0,
            ResolvedConnectorAnchorKind.Predefined,
            definitionId);

        Assert.True(dynamic.IsDynamic);
        Assert.Equal(definitionId, predefined.DefinitionId);
    }

    [Fact]
    public void EvaluatorEnforcesOneTotalDynamicAnchorAcrossBothRoles()
    {
        var single = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.DynamicSingle(),
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled);
        var empty = Visual("empty");
        var occupied = Visual(
            "occupied",
            [new ConnectorAnchor(
                new ConnectorAnchorId("source"),
                ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Source,
                0)]);

        Assert.True(ElementConnectorAnchorPolicyEvaluator.CanAdd(
            single, empty, ConnectorAnchorSide.Right, ConnectorAnchorRole.Source));
        Assert.True(ElementConnectorAnchorPolicyEvaluator.CanAdd(
            single, empty, ConnectorAnchorSide.Right, ConnectorAnchorRole.Target));
        Assert.False(ElementConnectorAnchorPolicyEvaluator.CanAdd(
            single, occupied, ConnectorAnchorSide.Right, ConnectorAnchorRole.Source));
        Assert.False(ElementConnectorAnchorPolicyEvaluator.CanAdd(
            single, occupied, ConnectorAnchorSide.Right, ConnectorAnchorRole.Target));
        Assert.True(ElementConnectorAnchorPolicyEvaluator.CanRemove(
            single, occupied, new ConnectorAnchorId("source")));
    }

    private static ElementConnectorAnchorPolicyRegistry Provider(
        ElementConnectorAnchorPolicy policy) =>
        new ElementConnectorAnchorPolicyRegistry(
        [
            new ElementConnectorAnchorPolicyRegistration(ElementTypeId, policy),
        ]);

    private static PredefinedConnectorAnchorDefinition Definition(
        string id,
        ConnectorAnchorSide side,
        ConnectorAnchorRoleCapability roles,
        int order) =>
        new(new PredefinedConnectorAnchorDefinitionId(id), side, roles, order);

    private static VisualStateSnapshot Visual(
        string id,
        IEnumerable<ConnectorAnchor>? anchors = null) =>
        new(
            new VisualStateId(id),
            new SemanticElementId($"semantic:{id}"),
            new PointD(10d, 20d),
            new SizeD(100d, 50d),
            VisualPlacementMode.Manual,
            connectorAnchors: anchors);
}
