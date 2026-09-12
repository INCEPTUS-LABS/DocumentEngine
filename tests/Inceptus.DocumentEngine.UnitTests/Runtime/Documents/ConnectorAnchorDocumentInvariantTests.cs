using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Runtime.Documents;

public sealed class ConnectorAnchorDocumentInvariantTests
{
    private static readonly DocumentId DocumentId = new("test:anchor-invariants");
    private static readonly SemanticElementId SourceId = new("test:source");
    private static readonly SemanticElementId TargetId = new("test:target");
    private static readonly SemanticElementId RelationshipId = new("test:relationship");
    private static readonly VisualStateId SourceVisualId = new("test:source-visual");
    private static readonly VisualStateId TargetVisualId = new("test:target-visual");
    private static readonly VisualStateId ConnectorVisualId = new("test:connector-visual");

    [Fact]
    public void ValidAnchorsReferencesAndOrderSurviveDocumentRoundTrip()
    {
        var sourceAnchorId = new ConnectorAnchorId("source-anchor");
        var targetAnchorId = new ConnectorAnchorId("target-anchor");
        var snapshot = Snapshot(
            sourceAnchors:
            [
                Anchor(sourceAnchorId, ConnectorAnchorRole.Source, 0),
                Anchor(new ConnectorAnchorId("source-edge-target"), ConnectorAnchorRole.Target, 1),
                Anchor(new ConnectorAnchorId("source-bottom"), ConnectorAnchorRole.Source, 0,
                    ConnectorAnchorSide.Bottom),
            ],
            targetAnchors: [Anchor(targetAnchorId, ConnectorAnchorRole.Target, 0)],
            sourceAnchorId: sourceAnchorId,
            targetAnchorId: targetAnchorId);

        var result = DocumentReconstructor.Reconstruct(snapshot);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        var roundTrip = Assert.IsType<Document>(result.Document).CaptureSnapshot();
        Assert.Equal(snapshot, roundTrip);
        Assert.Equal(
            ["source-anchor", "source-edge-target", "source-bottom"],
            Visual(roundTrip, SourceVisualId).ConnectorAnchors.Select(anchor => anchor.Id.Value));
        Assert.Equal(sourceAnchorId, Visual(roundTrip, ConnectorVisualId).SourceAnchorId);
        Assert.Equal(targetAnchorId, Visual(roundTrip, ConnectorVisualId).TargetAnchorId);
    }

    [Fact]
    public void DocumentsWithoutAnchorsOrReferencesRemainValid()
    {
        var result = DocumentReconstructor.Reconstruct(Snapshot());

        Assert.True(result.Succeeded);
        Assert.All(result.Document!.VisualModel.VisualStates, visual =>
        {
            Assert.Empty(visual.ConnectorAnchors);
            Assert.Null(visual.SourceAnchorId);
            Assert.Null(visual.TargetAnchorId);
        });
    }

    [Fact]
    public void AnchorIdentityMustBeUniqueAcrossTheWholeVisualModel()
    {
        var duplicate = new ConnectorAnchorId("duplicate");
        var result = DocumentReconstructor.Reconstruct(Snapshot(
            sourceAnchors: [Anchor(duplicate, ConnectorAnchorRole.Source, 0)],
            targetAnchors: [Anchor(duplicate, ConnectorAnchorRole.Target, 0)]));

        AssertFailure(result,
            DocumentInvariantValidator.VisualConnectorAnchorIdentityDuplicateCode);
    }

    [Fact]
    public void AnchorsAreElementOwnedAndReferencesAreRelationshipOwned()
    {
        var relationshipOwnsAnchor = DocumentReconstructor.Reconstruct(Snapshot(
            connectorAnchors:
            [
                Anchor(
                    new ConnectorAnchorId("invalid-owner"),
                    ConnectorAnchorRole.Source,
                    0),
            ]));
        var elementOwnsReference = DocumentReconstructor.Reconstruct(Snapshot(
            sourceVisualSourceAnchorId: new ConnectorAnchorId("missing")));

        AssertFailure(relationshipOwnsAnchor,
            DocumentInvariantValidator.VisualConnectorAnchorOwnershipInvalidCode);
        AssertFailure(elementOwnsReference,
            DocumentInvariantValidator.VisualConnectorAnchorOwnershipInvalidCode);
    }

    [Fact]
    public void ConnectorReferencesRequireExistingCorrectOwnerAndRole()
    {
        var sourceRoleId = new ConnectorAnchorId("source-role");
        var targetRoleOnSourceId = new ConnectorAnchorId("target-role-on-source");
        var sourceRoleOnTargetId = new ConnectorAnchorId("source-role-on-target");
        var sourceAnchors = new[]
        {
            Anchor(sourceRoleId, ConnectorAnchorRole.Source, 0),
            Anchor(targetRoleOnSourceId, ConnectorAnchorRole.Target, 1),
        };
        var targetAnchors = new[]
        {
            Anchor(sourceRoleOnTargetId, ConnectorAnchorRole.Source, 0),
        };

        var missing = DocumentReconstructor.Reconstruct(Snapshot(
            sourceAnchors,
            targetAnchors,
            sourceAnchorId: new ConnectorAnchorId("missing")));
        var wrongRole = DocumentReconstructor.Reconstruct(Snapshot(
            sourceAnchors,
            targetAnchors,
            sourceAnchorId: targetRoleOnSourceId));
        var wrongOwner = DocumentReconstructor.Reconstruct(Snapshot(
            sourceAnchors,
            targetAnchors,
            sourceAnchorId: sourceRoleOnTargetId));

        AssertFailure(missing,
            DocumentInvariantValidator.VisualConnectorAnchorReferenceInvalidCode);
        AssertFailure(wrongRole,
            DocumentInvariantValidator.VisualConnectorAnchorReferenceInvalidCode);
        AssertFailure(wrongOwner,
            DocumentInvariantValidator.VisualConnectorAnchorReferenceInvalidCode);
    }

    [Theory]
    [InlineData(ConnectorAnchorRole.Source)]
    [InlineData(ConnectorAnchorRole.Target)]
    public void ConnectorAnchorAllowsAtMostOnePersistentEndpointReference(
        ConnectorAnchorRole duplicatedRole)
    {
        var result = DocumentReconstructor.Reconstruct(
            SnapshotWithDuplicateEndpointReferences(duplicatedRole));

        AssertFailure(
            result,
            DocumentInvariantValidator.VisualConnectorAnchorOccupancyInvalidCode);
        var diagnostic = Assert.Single(result.Diagnostics, item =>
            item.Code ==
            DocumentInvariantValidator.VisualConnectorAnchorOccupancyInvalidCode);
        Assert.Contains(diagnostic.Context, item =>
            item.Key == "EndpointReferenceCount" && item.Value == "2");
    }

    private static ConnectorAnchor Anchor(
        ConnectorAnchorId id,
        ConnectorAnchorRole role,
        int order,
        ConnectorAnchorSide side = ConnectorAnchorSide.Right) =>
        new(id, side, role, order);

    private static DocumentSnapshot Snapshot(
        IEnumerable<ConnectorAnchor>? sourceAnchors = null,
        IEnumerable<ConnectorAnchor>? targetAnchors = null,
        ConnectorAnchorId? sourceAnchorId = null,
        ConnectorAnchorId? targetAnchorId = null,
        IEnumerable<ConnectorAnchor>? connectorAnchors = null,
        ConnectorAnchorId? sourceVisualSourceAnchorId = null)
    {
        var revision = new DocumentRevision(7);
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                [Element(SourceId), Element(TargetId)],
                [new SemanticRelationshipSnapshot(
                    RelationshipId,
                    new SemanticTypeId("test:relationship-type"),
                    SourceId,
                    TargetId)]),
            new VisualModelSnapshot(
                DocumentId,
                revision,
                [
                    ElementVisual(
                        SourceVisualId,
                        SourceId,
                        sourceAnchors,
                        sourceVisualSourceAnchorId),
                    ElementVisual(TargetVisualId, TargetId, targetAnchors),
                    new VisualStateSnapshot(
                        ConnectorVisualId,
                        RelationshipId,
                        default,
                        default,
                        VisualPlacementMode.Automatic,
                        connectorAnchors: connectorAnchors,
                        sourceAnchorId: sourceAnchorId,
                        targetAnchorId: targetAnchorId),
                ]),
            new DocumentMetadataSnapshot(DocumentId, revision));
    }

    private static DocumentSnapshot SnapshotWithDuplicateEndpointReferences(
        ConnectorAnchorRole duplicatedRole)
    {
        var revision = new DocumentRevision(7);
        var secondRelationshipId = new SemanticElementId("test:relationship:second");
        var sharedAnchorId = new ConnectorAnchorId("test:anchor:shared");
        var firstSourceAnchorId = duplicatedRole == ConnectorAnchorRole.Source
            ? sharedAnchorId
            : new ConnectorAnchorId("test:anchor:source:first");
        var secondSourceAnchorId = duplicatedRole == ConnectorAnchorRole.Source
            ? sharedAnchorId
            : new ConnectorAnchorId("test:anchor:source:second");
        var firstTargetAnchorId = duplicatedRole == ConnectorAnchorRole.Target
            ? sharedAnchorId
            : new ConnectorAnchorId("test:anchor:target:first");
        var secondTargetAnchorId = duplicatedRole == ConnectorAnchorRole.Target
            ? sharedAnchorId
            : new ConnectorAnchorId("test:anchor:target:second");
        ConnectorAnchor[] sourceAnchors = duplicatedRole == ConnectorAnchorRole.Source
            ? new[]
            {
                Anchor(sharedAnchorId, ConnectorAnchorRole.Source, 0),
            }
            :
            [
                Anchor(firstSourceAnchorId, ConnectorAnchorRole.Source, 0),
                Anchor(secondSourceAnchorId, ConnectorAnchorRole.Source, 1),
            ];
        ConnectorAnchor[] targetAnchors = duplicatedRole == ConnectorAnchorRole.Target
            ? new[]
            {
                Anchor(
                    sharedAnchorId,
                    ConnectorAnchorRole.Target,
                    0,
                    ConnectorAnchorSide.Left),
            }
            :
            [
                Anchor(
                    firstTargetAnchorId,
                    ConnectorAnchorRole.Target,
                    0,
                    ConnectorAnchorSide.Left),
                Anchor(
                    secondTargetAnchorId,
                    ConnectorAnchorRole.Target,
                    1,
                    ConnectorAnchorSide.Left),
            ];

        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                [Element(SourceId), Element(TargetId)],
                [
                    new SemanticRelationshipSnapshot(
                        RelationshipId,
                        new SemanticTypeId("test:relationship-type"),
                        SourceId,
                        TargetId),
                    new SemanticRelationshipSnapshot(
                        secondRelationshipId,
                        new SemanticTypeId("test:relationship-type"),
                        SourceId,
                        TargetId),
                ]),
            new VisualModelSnapshot(
                DocumentId,
                revision,
                [
                    ElementVisual(SourceVisualId, SourceId, sourceAnchors),
                    ElementVisual(TargetVisualId, TargetId, targetAnchors),
                    new VisualStateSnapshot(
                        ConnectorVisualId,
                        RelationshipId,
                        default,
                        default,
                        VisualPlacementMode.Automatic,
                        sourceAnchorId: firstSourceAnchorId,
                        targetAnchorId: firstTargetAnchorId),
                    new VisualStateSnapshot(
                        new VisualStateId("test:connector-visual:second"),
                        secondRelationshipId,
                        default,
                        default,
                        VisualPlacementMode.Automatic,
                        sourceAnchorId: secondSourceAnchorId,
                        targetAnchorId: secondTargetAnchorId),
                ]),
            new DocumentMetadataSnapshot(DocumentId, revision));
    }

    private static SemanticElementSnapshot Element(SemanticElementId id) =>
        new(id, new SemanticTypeId("test:element-type"));

    private static VisualStateSnapshot ElementVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        IEnumerable<ConnectorAnchor>? anchors,
        ConnectorAnchorId? sourceAnchorId = null) =>
        new(
            visualStateId,
            semanticElementId,
            new PointD(10d, 20d),
            new SizeD(100d, 60d),
            VisualPlacementMode.Manual,
            connectorAnchors: anchors,
            sourceAnchorId: sourceAnchorId);

    private static VisualStateSnapshot Visual(
        DocumentSnapshot snapshot,
        VisualStateId visualStateId)
    {
        Assert.True(snapshot.VisualModel.TryGetVisualState(visualStateId, out var visual));
        return Assert.IsType<VisualStateSnapshot>(visual);
    }

    private static void AssertFailure(
        DocumentConstructionResult result,
        string expectedCode)
    {
        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
    }
}
