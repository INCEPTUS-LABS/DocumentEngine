using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.ConnectionCreation;

public sealed class ConnectorTargetAnchorAcquisitionTests
{
    private static readonly DocumentId DocumentId = new("target-acquisition");
    private static readonly DocumentRevision Revision = new(7);
    private static readonly SemanticElementId TargetId = new("target");
    private static readonly VisualStateId TargetVisualId = new("target-visual");
    private static readonly SemanticTypeId NodeType = new("test:node");

    [Theory]
    [InlineData(150d, 201d, ConnectorAnchorSide.Top)]
    [InlineData(299d, 225d, ConnectorAnchorSide.Right)]
    [InlineData(150d, 299d, ConnectorAnchorSide.Bottom)]
    [InlineData(101d, 225d, ConnectorAnchorSide.Left)]
    [InlineData(200d, 250d, ConnectorAnchorSide.Top)]
    public void EdgeResolutionUsesNearestDistanceAndCanonicalTieOrder(
        double x,
        double y,
        ConnectorAnchorSide expected)
    {
        Assert.Equal(expected, ConnectorTargetEdgeResolver.Resolve(
            new RectD(100d, 200d, 200d, 100d),
            new PointD(x, y)));
    }

    [Fact]
    public void ExistingFreeTargetOnSelectedEdgeHasPriorityAndNearestCandidateWins()
    {
        var upper = Anchor("upper", ConnectorAnchorSide.Right, ConnectorAnchorRole.Target, 0);
        var lower = Anchor("lower", ConnectorAnchorSide.Right, ConnectorAnchorRole.Target, 1);
        var document = CreateDocument([upper, lower]);
        var allocationCount = 0;

        var result = Acquire(
            document,
            ElementConnectorAnchorPolicy.DynamicUnlimitedAllEdges,
            new PointD(299d, 270d),
            () =>
            {
                allocationCount++;
                return new ConnectorAnchorId("proposed");
            });

        Assert.Equal(TargetAnchorAcquisitionKind.Existing, result.Kind);
        Assert.Equal(lower.Id, result.AnchorId);
        Assert.Equal(ConnectorAnchorSide.Right, result.Side);
        Assert.Equal(300d, result.DocumentPoint!.Value.X);
        Assert.Equal(200d + (200d / 3d), result.DocumentPoint.Value.Y, precision: 10);
        Assert.Equal(0, allocationCount);
    }

    [Fact]
    public void SelectedEdgeNeverFallsBackToCompatibleAnchorOnAnotherEdge()
    {
        var leftTarget = Anchor(
            "left-target",
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        var policy = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.DynamicUnlimited(),
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.DynamicUnlimited());

        var result = Acquire(
            CreateDocument([leftTarget]),
            policy,
            new PointD(150d, 201d));

        Assert.Equal(TargetAnchorAcquisitionKind.Rejected, result.Kind);
        Assert.Equal(
            TargetAnchorAcquisitionRejectionReason.PolicyRejected,
            result.RejectionReason);
    }

    [Fact]
    public void OccupiedAnchorIsSkippedAndDynamicUnlimitedProposesOnSameEdge()
    {
        var occupied = Anchor(
            "occupied",
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Target,
            0);
        var document = CreateDocument([occupied], occupiedTarget: occupied.Id);

        var result = Acquire(
            document,
            ElementConnectorAnchorPolicy.DynamicUnlimitedAllEdges,
            new PointD(299d, 225d));

        Assert.Equal(TargetAnchorAcquisitionKind.Proposed, result.Kind);
        Assert.Equal(ConnectorAnchorSide.Right, result.Side);
        Assert.Equal(0, result.InsertionIndex);
        Assert.Equal(300d, result.DocumentPoint!.Value.X);
        Assert.Equal(200d + (100d / 3d), result.DocumentPoint.Value.Y, precision: 10);
    }

    [Fact]
    public void InsertionUsesMixedRoleOrderAndPredictsFinalRedistribution()
    {
        ConnectorAnchor[] anchors =
        [
            Anchor("source", ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
            Anchor("target", ConnectorAnchorSide.Right, ConnectorAnchorRole.Target, 1),
        ];
        var document = CreateDocument(anchors, occupiedTarget: anchors[1].Id);

        var result = Acquire(
            document,
            ElementConnectorAnchorPolicy.DynamicUnlimitedAllEdges,
            new PointD(299d, 250d));

        Assert.Equal(TargetAnchorAcquisitionKind.Proposed, result.Kind);
        Assert.Equal(1, result.InsertionIndex);
        Assert.Equal(new PointD(300d, 250d), result.DocumentPoint);
        var targetVisual = document.VisualModel.VisualStates.Single(item =>
            item.Id == TargetVisualId);
        Assert.Equal(0, targetVisual.ConnectorAnchors[0].Order);
        Assert.Equal(1, targetVisual.ConnectorAnchors[1].Order);
    }

    [Fact]
    public void DynamicSingleRejectsASecondAnchorOnTheSelectedEdge()
    {
        var source = Anchor(
            "source",
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        var singleRight = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.DynamicSingle(),
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled);

        var result = Acquire(
            CreateDocument([source]),
            singleRight,
            new PointD(299d, 250d));

        Assert.Equal(TargetAnchorAcquisitionKind.Rejected, result.Kind);
    }

    [Fact]
    public void PredefinedFreeTargetIsReusableButPredefinedEdgeCannotPropose()
    {
        var definition = new PredefinedConnectorAnchorDefinition(
            new PredefinedConnectorAnchorDefinitionId("middle"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRoleCapability.Target,
            0);
        var policy = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Predefined([definition]),
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled);
        var free = Acquire(CreateDocument([]), policy, new PointD(299d, 250d));

        Assert.Equal(TargetAnchorAcquisitionKind.Existing, free.Kind);
        Assert.Equal(
            ConnectorAnchorReferenceIdentity.ForPredefined(TargetVisualId, definition.Id),
            free.AnchorId);

        var occupied = CreateDocument([], occupiedTarget: free.AnchorId);
        var rejected = Acquire(occupied, policy, new PointD(299d, 250d));
        Assert.Equal(TargetAnchorAcquisitionKind.Rejected, rejected.Kind);
    }

    [Fact]
    public void DisabledOrSourceOnlySelectedEdgeRejectsTargetAcquisition()
    {
        var disabled = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled);
        var sourceOnly = new ElementConnectorAnchorPolicy(
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.DynamicUnlimited(
                ConnectorAnchorRoleCapability.Source),
            EdgeConnectorAnchorPolicy.Disabled,
            EdgeConnectorAnchorPolicy.Disabled);
        var document = CreateDocument([]);

        Assert.Equal(TargetAnchorAcquisitionKind.Rejected, Acquire(
            document, disabled, new PointD(299d, 250d)).Kind);
        Assert.Equal(TargetAnchorAcquisitionKind.Rejected, Acquire(
            document, sourceOnly, new PointD(299d, 250d)).Kind);
    }

    private static TargetAnchorAcquisitionResult Acquire(
        DocumentSnapshot document,
        ElementConnectorAnchorPolicy policy,
        PointD point,
        Func<ConnectorAnchorId>? factory = null) =>
        ConnectorTargetAnchorAcquisition.Acquire(
            new TargetAnchorAcquisitionRequest(
                document,
                Revision,
                TargetId,
                TargetVisualId,
                new RectD(100d, 200d, 200d, 100d),
                point,
                policy),
            factory ?? (() => new ConnectorAnchorId("proposed")));

    private static DocumentSnapshot CreateDocument(
        IEnumerable<ConnectorAnchor> anchors,
        ConnectorAnchorId? occupiedTarget = null)
    {
        var target = new SemanticElementSnapshot(TargetId, NodeType);
        var elements = new List<SemanticElementSnapshot> { target };
        var relationships = new List<SemanticRelationshipSnapshot>();
        var visuals = new List<VisualStateSnapshot>
        {
            new(
                TargetVisualId,
                TargetId,
                new PointD(100d, 200d),
                new SizeD(200d, 100d),
                VisualPlacementMode.Pinned,
                connectorAnchors: anchors),
        };
        if (occupiedTarget is not null)
        {
            var sourceId = new SemanticElementId("source");
            var relationshipId = new SemanticElementId("relationship");
            elements.Add(new SemanticElementSnapshot(sourceId, NodeType));
            relationships.Add(new SemanticRelationshipSnapshot(
                relationshipId,
                new SemanticTypeId("test:relationship"),
                sourceId,
                TargetId));
            visuals.Add(new VisualStateSnapshot(
                new VisualStateId("relationship-visual"),
                relationshipId,
                new PointD(0d, 0d),
                new SizeD(0d, 0d),
                VisualPlacementMode.Manual,
                targetAnchorId: occupiedTarget));
        }

        return new DocumentSnapshot(
            new SemanticModelSnapshot(DocumentId, Revision, elements, relationships),
            new VisualModelSnapshot(DocumentId, Revision, visuals),
            new DocumentMetadataSnapshot(DocumentId, Revision));
    }

    private static ConnectorAnchor Anchor(
        string id,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role,
        int order) => new(new ConnectorAnchorId(id), side, role, order);
}
