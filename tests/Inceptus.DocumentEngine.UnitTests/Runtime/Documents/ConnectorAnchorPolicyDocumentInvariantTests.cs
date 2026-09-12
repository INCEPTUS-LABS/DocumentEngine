using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Runtime.Documents;

public sealed class ConnectorAnchorPolicyDocumentInvariantTests
{
    private static readonly DocumentId DocumentId = new("test:m0:invariants");
    private static readonly SemanticTypeId ElementTypeId = new("test:m0:type");
    private static readonly SemanticElementId SourceId = new("test:m0:source");
    private static readonly SemanticElementId TargetId = new("test:m0:target");
    private static readonly SemanticElementId RelationshipId = new("test:m0:relationship");
    private static readonly VisualStateId SourceVisualId = new("test:m0:source-visual");
    private static readonly VisualStateId TargetVisualId = new("test:m0:target-visual");

    [Fact]
    public void PredefinedReferencesResolveByOwnerRoleAndStableDefinitionWithoutInstanceCopies()
    {
        var sourceDefinition = Definition(
            "right-source",
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Source,
            0);
        var targetDefinition = Definition(
            "left-target",
            ConnectorAnchorSide.Left,
            ConnectorAnchorRoleCapability.Target,
            0);
        var provider = Provider(new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Predefined([sourceDefinition]),
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Predefined([targetDefinition])));
        var sourceReference = ConnectorAnchorReferenceIdentity.ForPredefined(
            SourceVisualId,
            sourceDefinition.Id);
        var targetReference = ConnectorAnchorReferenceIdentity.ForPredefined(
            TargetVisualId,
            targetDefinition.Id);
        var snapshot = Snapshot(sourceReference, targetReference);

        var result = DocumentReconstructor.Reconstruct(snapshot, provider);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        var roundTrip = result.Document!.CaptureSnapshot();
        Assert.Empty(Visual(roundTrip, SourceVisualId).ConnectorAnchors);
        Assert.Empty(Visual(roundTrip, TargetVisualId).ConnectorAnchors);
        Assert.Equal(sourceReference, ConnectorVisual(roundTrip).SourceAnchorId);
        Assert.Equal(targetReference, ConnectorVisual(roundTrip).TargetAnchorId);
    }

    [Fact]
    public void UnknownOrWrongRolePredefinedReferencesAreRejectedWithoutRedirecting()
    {
        var sourceDefinition = Definition(
            "right-target-only",
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Target,
            0);
        var provider = Provider(new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Predefined([sourceDefinition]),
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled));
        var wrongRole = ConnectorAnchorReferenceIdentity.ForPredefined(
            SourceVisualId,
            sourceDefinition.Id);
        var unknown = ConnectorAnchorReferenceIdentity.ForPredefined(
            TargetVisualId,
            new PredefinedConnectorAnchorDefinitionId("missing"));

        AssertFailure(
            DocumentReconstructor.Reconstruct(Snapshot(wrongRole), provider),
            DocumentInvariantValidator.VisualConnectorAnchorReferenceInvalidCode);
        AssertFailure(
            DocumentReconstructor.Reconstruct(Snapshot(targetAnchorId: unknown), provider),
            DocumentInvariantValidator.VisualConnectorAnchorReferenceInvalidCode);
    }

    [Theory]
    [InlineData(ConnectorAnchorPolicyMode.Disabled)]
    [InlineData(ConnectorAnchorPolicyMode.Predefined)]
    public void PersistentDynamicAnchorsAreRejectedOnNondynamicEdges(
        ConnectorAnchorPolicyMode mode)
    {
        var right = mode == ConnectorAnchorPolicyMode.Disabled
            ? EdgeConnectorAnchorPolicy.Disabled
            : EdgeConnectorAnchorPolicy.Predefined(
            [
                Definition("fixed", ConnectorAnchorSide.Right,
                    ConnectorAnchorRoleCapability.SourceOrTarget, 0),
            ]);
        var provider = Provider(new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            right,
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled));
        var snapshot = Snapshot(
            sourceAnchors:
            [
                new ConnectorAnchor(
                    new ConnectorAnchorId("dynamic"),
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source,
                    0),
            ]);

        AssertFailure(
            DocumentReconstructor.Reconstruct(snapshot, provider),
            DocumentInvariantValidator.VisualConnectorAnchorPolicyInvalidCode);
    }

    [Fact]
    public void DynamicSingleRejectsTwoTotalAnchorsEvenWhenRolesDiffer()
    {
        var provider = Provider(new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.DynamicSingle(),
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled));
        var snapshot = Snapshot(
            sourceAnchors:
            [
                new ConnectorAnchor(new ConnectorAnchorId("source"),
                    ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
                new ConnectorAnchor(new ConnectorAnchorId("target"),
                    ConnectorAnchorSide.Right, ConnectorAnchorRole.Target, 1),
            ]);

        AssertFailure(
            DocumentReconstructor.Reconstruct(snapshot, provider),
            DocumentInvariantValidator.VisualConnectorAnchorPolicyInvalidCode);
    }

    [Fact]
    public void MissingRegistrationUsesUnlimitedFallbackForLegacyDynamicDocuments()
    {
        var anchor = new ConnectorAnchor(
            new ConnectorAnchorId("legacy"),
            ConnectorAnchorSide.Top,
            ConnectorAnchorRole.Target,
            0);

        var result = DocumentReconstructor.Reconstruct(Snapshot(sourceAnchors: [anchor]));

        Assert.True(result.Succeeded);
        Assert.Equal(anchor, Assert.Single(
            Visual(result.Document!.CaptureSnapshot(), SourceVisualId).ConnectorAnchors));
    }

    [Fact]
    public void DynamicAnchorCannotMasqueradeAsAnUnknownPredefinedReference()
    {
        var reserved = ConnectorAnchorReferenceIdentity.ForPredefined(
            SourceVisualId,
            new PredefinedConnectorAnchorDefinitionId("missing"));
        var snapshot = Snapshot(
            sourceAnchorId: reserved,
            sourceAnchors:
            [
                new ConnectorAnchor(
                    reserved,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source,
                    0),
            ]);

        AssertFailure(
            DocumentReconstructor.Reconstruct(snapshot),
            DocumentInvariantValidator.VisualConnectorAnchorPolicyInvalidCode);
    }

    private static DocumentSnapshot Snapshot(
        ConnectorAnchorId? sourceAnchorId = null,
        ConnectorAnchorId? targetAnchorId = null,
        IEnumerable<ConnectorAnchor>? sourceAnchors = null)
    {
        var revision = new DocumentRevision(5);
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                [
                    new SemanticElementSnapshot(SourceId, ElementTypeId),
                    new SemanticElementSnapshot(TargetId, ElementTypeId),
                ],
                [new SemanticRelationshipSnapshot(
                    RelationshipId,
                    new SemanticTypeId("test:m0:relationship-type"),
                    SourceId,
                    TargetId)]),
            new VisualModelSnapshot(
                DocumentId,
                revision,
                [
                    ElementVisual(SourceVisualId, SourceId, sourceAnchors),
                    ElementVisual(TargetVisualId, TargetId),
                    new VisualStateSnapshot(
                        new VisualStateId("test:m0:connector-visual"),
                        RelationshipId,
                        default,
                        default,
                        VisualPlacementMode.Automatic,
                        sourceAnchorId: sourceAnchorId,
                        targetAnchorId: targetAnchorId),
                ]),
            new DocumentMetadataSnapshot(DocumentId, revision));
    }

    private static VisualStateSnapshot ElementVisual(
        VisualStateId id,
        SemanticElementId semanticId,
        IEnumerable<ConnectorAnchor>? anchors = null) =>
        new(
            id,
            semanticId,
            new PointD(10d, 20d),
            new SizeD(100d, 50d),
            VisualPlacementMode.Manual,
            connectorAnchors: anchors);

    private static PredefinedConnectorAnchorDefinition Definition(
        string id,
        ConnectorAnchorSide side,
        ConnectorAnchorRoleCapability roles,
        int order) =>
        new(new PredefinedConnectorAnchorDefinitionId(id), side, roles, order);

    private static ElementConnectorAnchorPolicyRegistry Provider(
        ElementConnectorAnchorPolicy policy) =>
        new ElementConnectorAnchorPolicyRegistry(
        [
            new ElementConnectorAnchorPolicyRegistration(ElementTypeId, policy),
        ]);

    private static VisualStateSnapshot Visual(
        DocumentSnapshot snapshot,
        VisualStateId id)
    {
        Assert.True(snapshot.VisualModel.TryGetVisualState(id, out var visual));
        return Assert.IsType<VisualStateSnapshot>(visual);
    }

    private static VisualStateSnapshot ConnectorVisual(DocumentSnapshot snapshot)
    {
        Assert.True(snapshot.VisualModel.TryGetVisualState(
            new VisualStateId("test:m0:connector-visual"),
            out var visual));
        return Assert.IsType<VisualStateSnapshot>(visual);
    }

    private static void AssertFailure(
        DocumentConstructionResult result,
        string code)
    {
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }
}
