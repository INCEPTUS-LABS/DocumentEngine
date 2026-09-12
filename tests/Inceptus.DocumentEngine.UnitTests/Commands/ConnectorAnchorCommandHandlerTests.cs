using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class ConnectorAnchorCommandHandlerTests
{
    private static readonly DocumentId DocumentId = new("test:anchors");
    private static readonly SemanticElementId SourceId = new("test:source");
    private static readonly SemanticElementId TargetId = new("test:target");
    private static readonly SemanticElementId RelationshipId = new("test:relationship");
    private static readonly VisualStateId SourceVisualId = new("test:source-visual");
    private static readonly VisualStateId TargetVisualId = new("test:target-visual");
    private static readonly VisualStateId ConnectorVisualId = new("test:connector-visual");

    [Fact]
    public async Task AddInsertsAtRequestedSharedSideOrderAndPreservesOtherState()
    {
        var before = Snapshot(
        [
            Anchor("a1", ConnectorAnchorRole.Source, 0),
            Anchor("a2", ConnectorAnchorRole.Target, 1),
            Anchor("a3", ConnectorAnchorRole.Source, 2),
        ]);
        var sourceBefore = Visual(before, SourceVisualId);

        var result = await new AddConnectorAnchorCommandHandler().HandleAsync(
            new AddConnectorAnchorCommand(
                DocumentId,
                DocumentRevision.Zero,
                SourceVisualId,
                new ConnectorAnchorId("new"),
                ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Target,
                1),
            before,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        var source = Visual(proposed, SourceVisualId);
        Assert.Equal(["a1", "new", "a2", "a3"],
            source.ConnectorAnchors.Select(anchor => anchor.Id.Value));
        Assert.Equal([0, 1, 2, 3],
            source.ConnectorAnchors.Select(anchor => anchor.Order));
        Assert.Equal(sourceBefore.Position, source.Position);
        Assert.Equal(sourceBefore.Size, source.Size);
        Assert.Equal(sourceBefore.PlacementMode, source.PlacementMode);
        Assert.Equal(sourceBefore.Route, source.Route);
        Assert.Equal(sourceBefore.Properties, source.Properties);
        Assert.Same(before.SemanticModel, proposed.SemanticModel);
        Assert.Same(before.Metadata, proposed.Metadata);
        Assert.Same(Visual(before, ConnectorVisualId), Visual(proposed, ConnectorVisualId));
    }

    [Fact]
    public async Task RemoveDeletesExactlyOneAnchorAndRedistributesPersistentOrder()
    {
        var before = Snapshot(
        [
            Anchor("a1", ConnectorAnchorRole.Source, 0),
            Anchor("a2", ConnectorAnchorRole.Target, 1),
            Anchor("a3", ConnectorAnchorRole.Source, 2),
            Anchor("bottom", ConnectorAnchorRole.Target, 0, ConnectorAnchorSide.Bottom),
        ]);

        var result = await new RemoveConnectorAnchorCommandHandler().HandleAsync(
            new RemoveConnectorAnchorCommand(
                DocumentId,
                DocumentRevision.Zero,
                SourceVisualId,
                new ConnectorAnchorId("a2")),
            before,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var source = Visual(result.ProposedDocument!, SourceVisualId);
        Assert.Equal(["a1", "a3", "bottom"],
            source.ConnectorAnchors.Select(anchor => anchor.Id.Value));
        Assert.Equal([0, 1], source.ConnectorAnchors
            .Where(anchor => anchor.Side == ConnectorAnchorSide.Right)
            .Select(anchor => anchor.Order));
        Assert.Equal(0, source.ConnectorAnchors.Single(anchor =>
            anchor.Side == ConnectorAnchorSide.Bottom).Order);
    }

    [Fact]
    public async Task AddRejectsRelationshipOwnersDuplicateDocumentIdentityAndInvalidIndex()
    {
        var before = Snapshot([Anchor("existing", ConnectorAnchorRole.Source, 0)]);
        var handler = new AddConnectorAnchorCommandHandler();

        var relationshipOwner = await handler.HandleAsync(
            Add(ConnectorVisualId, "new", 0), before, CancellationToken.None);
        var duplicate = await handler.HandleAsync(
            Add(TargetVisualId, "existing", 0), before, CancellationToken.None);
        var invalidIndex = await handler.HandleAsync(
            Add(SourceVisualId, "new", 2), before, CancellationToken.None);

        AssertFailure(relationshipOwner,
            CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportConnectorAnchors);
        AssertFailure(duplicate,
            CommandExecutionDiagnosticCodes.ConnectorAnchorIdentityDuplicate);
        AssertFailure(invalidIndex,
            CommandExecutionDiagnosticCodes.ConnectorAnchorInsertionIndexInvalid);
    }

    [Fact]
    public async Task RemoveRejectsMissingAndEitherEndpointReferenceWithoutProposal()
    {
        var sourceReferencedId = new ConnectorAnchorId("used-source");
        var targetReferencedId = new ConnectorAnchorId("used-target");
        var before = Snapshot(
            [
                new ConnectorAnchor(
                    sourceReferencedId,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source,
                    0),
                new ConnectorAnchor(
                    targetReferencedId,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Target,
                    1),
            ],
            connectorSourceAnchorId: sourceReferencedId,
            connectorTargetAnchorId: targetReferencedId,
            relationshipTargetId: SourceId);
        var handler = new RemoveConnectorAnchorCommandHandler();

        var missing = await handler.HandleAsync(
            Remove("missing"), before, CancellationToken.None);
        var usedSource = await handler.HandleAsync(
            Remove("used-source"), before, CancellationToken.None);
        var usedTarget = await handler.HandleAsync(
            Remove("used-target"), before, CancellationToken.None);

        AssertFailure(missing, CommandExecutionDiagnosticCodes.ConnectorAnchorNotFound);
        AssertFailure(usedSource, CommandExecutionDiagnosticCodes.ConnectorAnchorInUse);
        AssertFailure(usedTarget, CommandExecutionDiagnosticCodes.ConnectorAnchorInUse);
        Assert.Equal(sourceReferencedId,
            Visual(before, ConnectorVisualId).SourceAnchorId);
        Assert.Equal(targetReferencedId,
            Visual(before, ConnectorVisualId).TargetAnchorId);
        Assert.Equal(2, Visual(before, SourceVisualId).ConnectorAnchors.Length);
    }

    [Fact]
    public async Task CancellationBeforeEitherHandlerProducesNoProposal()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var before = Snapshot();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new AddConnectorAnchorCommandHandler().HandleAsync(
                Add(SourceVisualId, "new", 0), before, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new RemoveConnectorAnchorCommandHandler().HandleAsync(
                Remove("missing"), before, cancellation.Token));
    }

    private static AddConnectorAnchorCommand Add(
        VisualStateId visualStateId,
        string anchorId,
        int insertionIndex) =>
        new(
            DocumentId,
            DocumentRevision.Zero,
            visualStateId,
            new ConnectorAnchorId(anchorId),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            insertionIndex);

    private static RemoveConnectorAnchorCommand Remove(string anchorId) =>
        new(
            DocumentId,
            DocumentRevision.Zero,
            SourceVisualId,
            new ConnectorAnchorId(anchorId));

    private static ConnectorAnchor Anchor(
        string id,
        ConnectorAnchorRole role,
        int order,
        ConnectorAnchorSide side = ConnectorAnchorSide.Right) =>
        new(new ConnectorAnchorId(id), side, role, order);

    private static DocumentSnapshot Snapshot(
        IEnumerable<ConnectorAnchor>? sourceAnchors = null,
        ConnectorAnchorId? connectorSourceAnchorId = null,
        ConnectorAnchorId? connectorTargetAnchorId = null,
        SemanticElementId? relationshipTargetId = null)
    {
        var revision = DocumentRevision.Zero;
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                [Element(SourceId), Element(TargetId)],
                [new SemanticRelationshipSnapshot(
                    RelationshipId,
                    new SemanticTypeId("test:relationship-type"),
                    SourceId,
                    relationshipTargetId ?? TargetId)]),
            new VisualModelSnapshot(
                DocumentId,
                revision,
                [
                    ElementVisual(SourceVisualId, SourceId, sourceAnchors),
                    ElementVisual(TargetVisualId, TargetId),
                    new VisualStateSnapshot(
                        ConnectorVisualId,
                        RelationshipId,
                        default,
                        default,
                        VisualPlacementMode.Automatic,
                        properties: [new("test:connector", PropertyValue.FromText("preserved"))],
                        sourceAnchorId: connectorSourceAnchorId,
                        targetAnchorId: connectorTargetAnchorId),
                ]),
            new DocumentMetadataSnapshot(DocumentId, revision));
    }

    private static SemanticElementSnapshot Element(SemanticElementId id) =>
        new(id, new SemanticTypeId("test:element-type"));

    private static VisualStateSnapshot ElementVisual(
        VisualStateId visualId,
        SemanticElementId semanticId,
        IEnumerable<ConnectorAnchor>? anchors = null) =>
        new(
            visualId,
            semanticId,
            new PointD(10d, 20d),
            new SizeD(100d, 60d),
            VisualPlacementMode.Pinned,
            properties: [new("test:fill", PropertyValue.FromText("preserved"))],
            connectorAnchors: anchors);

    private static VisualStateSnapshot Visual(
        DocumentSnapshot snapshot,
        VisualStateId visualStateId)
    {
        Assert.True(snapshot.VisualModel.TryGetVisualState(visualStateId, out var visual));
        return Assert.IsType<VisualStateSnapshot>(visual);
    }

    private static void AssertFailure(CommandHandlerResult result, string code)
    {
        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
    }
}
