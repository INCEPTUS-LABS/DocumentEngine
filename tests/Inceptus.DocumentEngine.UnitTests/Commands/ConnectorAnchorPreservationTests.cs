using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class ConnectorAnchorPreservationTests
{
    private static readonly DocumentId DocumentId = new("test:anchor-preservation");
    private static readonly SemanticElementId SourceId = new("test:source");
    private static readonly SemanticElementId TargetId = new("test:target");
    private static readonly SemanticElementId RelationshipId = new("test:relationship");
    private static readonly VisualStateId SourceVisualId = new("test:source-visual");
    private static readonly VisualStateId TargetVisualId = new("test:target-visual");
    private static readonly VisualStateId ConnectorVisualId = new("test:connector-visual");
    private static readonly ConnectorAnchorId SourceAnchorId = new("test:source-anchor");
    private static readonly ConnectorAnchorId TargetAnchorId = new("test:target-anchor");

    [Fact]
    public async Task ExistingVisualCommandsPreserveAnchorsAndAttachmentReferences()
    {
        var snapshot = Snapshot();

        var moved = await new MoveVisualStateCommandHandler().HandleAsync(
            new MoveVisualStateCommand(
                DocumentId,
                snapshot.Revision,
                SourceVisualId,
                new PointD(30d, 40d)),
            snapshot,
            CancellationToken.None);
        AssertNodeAnchors(moved.ProposedDocument!);

        var multiMoved = await new MoveVisualStatesCommandHandler().HandleAsync(
            new MoveVisualStatesCommand(
                DocumentId,
                snapshot.Revision,
                [
                    new VisualStateMove(SourceVisualId, new PointD(30d, 40d)),
                    new VisualStateMove(TargetVisualId, new PointD(230d, 40d)),
                ]),
            snapshot,
            CancellationToken.None);
        AssertNodeAnchors(multiMoved.ProposedDocument!);

        var resized = await new ResizeVisualStateCommandHandler().HandleAsync(
            new ResizeVisualStateCommand(
                DocumentId,
                snapshot.Revision,
                SourceVisualId,
                new RectD(10d, 20d, 180d, 100d)),
            snapshot,
            CancellationToken.None);
        AssertNodeAnchors(resized.ProposedDocument!);

        var rerouted = await new UpdateConnectionRouteCommandHandler().HandleAsync(
            new UpdateConnectionRouteCommand(
                DocumentId,
                snapshot.Revision,
                ConnectorVisualId,
                [new PointD(110d, 50d), new PointD(160d, 80d), new PointD(210d, 50d)]),
            snapshot,
            CancellationToken.None);
        AssertConnectorReferences(rerouted.ProposedDocument!);

        var labelMoved = await new MoveLabelCommandHandler().HandleAsync(
            new MoveLabelCommand(
                DocumentId,
                snapshot.Revision,
                ConnectorVisualId,
                new ConnectorLabelPlacement(0.75d, new VectorD(4d, -10d))),
            snapshot,
            CancellationToken.None);
        AssertConnectorReferences(labelMoved.ProposedDocument!);
    }

    private static DocumentSnapshot Snapshot()
    {
        var revision = new DocumentRevision(4);
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
                        new ConnectorAnchor(
                            SourceAnchorId,
                            ConnectorAnchorSide.Right,
                            ConnectorAnchorRole.Source,
                            0)),
                    ElementVisual(
                        TargetVisualId,
                        TargetId,
                        new ConnectorAnchor(
                            TargetAnchorId,
                            ConnectorAnchorSide.Left,
                            ConnectorAnchorRole.Target,
                            0)),
                    new VisualStateSnapshot(
                        ConnectorVisualId,
                        RelationshipId,
                        default,
                        default,
                        VisualPlacementMode.Manual,
                        [new PointD(110d, 50d), new PointD(210d, 50d)],
                        sourceAnchorId: SourceAnchorId,
                        targetAnchorId: TargetAnchorId),
                ]),
            new DocumentMetadataSnapshot(DocumentId, revision));
    }

    private static SemanticElementSnapshot Element(SemanticElementId id) =>
        new(id, new SemanticTypeId("test:element-type"));

    private static VisualStateSnapshot ElementVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        ConnectorAnchor anchor) =>
        new(
            visualStateId,
            semanticElementId,
            new PointD(10d, 20d),
            new SizeD(100d, 60d),
            VisualPlacementMode.Manual,
            connectorAnchors: [anchor]);

    private static void AssertNodeAnchors(DocumentSnapshot snapshot)
    {
        Assert.Equal(SourceAnchorId, Visual(snapshot, SourceVisualId).ConnectorAnchors[0].Id);
        Assert.Equal(TargetAnchorId, Visual(snapshot, TargetVisualId).ConnectorAnchors[0].Id);
        AssertConnectorReferences(snapshot);
    }

    private static void AssertConnectorReferences(DocumentSnapshot snapshot)
    {
        var connector = Visual(snapshot, ConnectorVisualId);
        Assert.Equal(SourceAnchorId, connector.SourceAnchorId);
        Assert.Equal(TargetAnchorId, connector.TargetAnchorId);
    }

    private static VisualStateSnapshot Visual(
        DocumentSnapshot snapshot,
        VisualStateId visualStateId)
    {
        Assert.True(snapshot.VisualModel.TryGetVisualState(visualStateId, out var visual));
        return Assert.IsType<VisualStateSnapshot>(visual);
    }
}
