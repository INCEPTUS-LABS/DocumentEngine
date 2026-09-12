using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class MoveVisualStateCommandHandlerTests
{
    private static readonly DocumentId TestDocumentId = new("test:move-handler");
    private static readonly SemanticElementId ElementId = new("test:element");
    private static readonly VisualStateId VisualId = new("test:visual");

    [Fact]
    public async Task MoveProposalChangesOnlyPositionAndPreservesPersistentVisualData()
    {
        var snapshot = Snapshot();
        var original = Assert.Single(snapshot.VisualModel.VisualStates);
        var command = new MoveVisualStateCommand(
            TestDocumentId,
            DocumentRevision.Zero,
            VisualId,
            new PointD(80d, 90d));

        var result = await new MoveVisualStateCommandHandler()
            .HandleAsync(command, snapshot, CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        var moved = Assert.Single(proposed.VisualModel.VisualStates);
        Assert.Equal(new PointD(80d, 90d), moved.Position);
        Assert.Equal(original.PlacementMode, moved.PlacementMode);
        Assert.Equal(original.Size, moved.Size);
        Assert.Equal(original.Route, moved.Route);
        Assert.Equal(original.Properties, moved.Properties);
        Assert.Same(snapshot.SemanticModel, proposed.SemanticModel);
        Assert.Same(snapshot.Metadata, proposed.Metadata);
        Assert.Equal(snapshot.Revision, proposed.Revision);
        Assert.Equal(
            CommandPipelineInvalidation.WithoutNodeLayout,
            result.PipelineInvalidation);
        Assert.Equal(
            [VisualId],
            Assert.IsType<NodeGeometryPipelineImpact>(result.NodeGeometryImpact)
                .ChangedVisualStateIds
                .ToArray());
    }

    [Fact]
    public async Task ExplicitPlacementIntentChangesTheProposedPlacementMode()
    {
        var snapshot = Snapshot();
        var command = new MoveVisualStateCommand(
            TestDocumentId,
            DocumentRevision.Zero,
            VisualId,
            new PointD(80d, 90d),
            VisualPlacementMode.Pinned);

        var result = await new MoveVisualStateCommandHandler()
            .HandleAsync(command, snapshot, CancellationToken.None);

        var moved = Assert.Single(Assert.IsType<DocumentSnapshot>(result.ProposedDocument)
            .VisualModel.VisualStates);
        Assert.Equal(VisualPlacementMode.Pinned, moved.PlacementMode);
    }

    [Fact]
    public async Task MissingVisualStateFailsWithoutAProposal()
    {
        var result = await new MoveVisualStateCommandHandler().HandleAsync(
            new MoveVisualStateCommand(
                TestDocumentId,
                DocumentRevision.Zero,
                new VisualStateId("test:missing"),
                new PointD(80d, 90d)),
            Snapshot(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.VisualStateNotFound,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task RelationshipVisualStateCannotBeMovedAsAPositionedElement()
    {
        var relationshipId = new SemanticElementId("test:relationship");
        var relationshipVisualId = new VisualStateId("test:relationship-visual");
        var semantic = new SemanticModelSnapshot(
            TestDocumentId,
            DocumentRevision.Zero,
            [
                new SemanticElementSnapshot(ElementId, new SemanticTypeId("test:type")),
                new SemanticElementSnapshot(
                    new SemanticElementId("test:target"),
                    new SemanticTypeId("test:type")),
            ],
            [
                new SemanticRelationshipSnapshot(
                    relationshipId,
                    new SemanticTypeId("test:relationship-type"),
                    ElementId,
                    new SemanticElementId("test:target")),
            ]);
        var relationshipVisual = new VisualStateSnapshot(
            relationshipVisualId,
            relationshipId,
            new PointD(10d, 20d),
            new SizeD(30d, 40d),
            VisualPlacementMode.Manual,
            [new PointD(10d, 20d), new PointD(40d, 60d)]);
        var snapshot = new DocumentSnapshot(
            semantic,
            new VisualModelSnapshot(
                TestDocumentId,
                DocumentRevision.Zero,
                [relationshipVisual]),
            new DocumentMetadataSnapshot(TestDocumentId, DocumentRevision.Zero));

        var result = await new MoveVisualStateCommandHandler().HandleAsync(
            new MoveVisualStateCommand(
                TestDocumentId,
                DocumentRevision.Zero,
                relationshipVisualId,
                new PointD(80d, 90d)),
            snapshot,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.VisualStateDoesNotSupportPosition,
            Assert.Single(result.Diagnostics).Code);
        Assert.Same(relationshipVisual, Assert.Single(snapshot.VisualModel.VisualStates));
    }

    [Fact]
    public async Task NonRenderableTargetBoundsDoNotCommitRecordHistoryOrPublish()
    {
        var before = Snapshot();
        var document = Assert.IsType<Document>(DocumentFactory.Create(before).Document);
        var history = new HistoryManager(document);
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);

        var result = await history.ExecuteAsync(
            processor,
            new MoveVisualStateCommand(
                TestDocumentId,
                DocumentRevision.Zero,
                VisualId,
                new PointD(double.MaxValue, 90d),
                VisualPlacementMode.Pinned));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(0, history.CaptureStatus().EntryCount);
        Assert.Empty(subscriber.Events);
    }

    [Theory]
    [InlineData(-1d, 20d)]
    [InlineData(10d, -1d)]
    public async Task NegativeTargetPositionIsRejectedAuthoritatively(
        double x,
        double y)
    {
        var result = await new MoveVisualStateCommandHandler().HandleAsync(
            new MoveVisualStateCommand(
                TestDocumentId,
                DocumentRevision.Zero,
                VisualId,
                new PointD(x, y),
                VisualPlacementMode.Pinned),
            Snapshot(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task CancellationBeforeHandlerWorkProducesNoProposal()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new MoveVisualStateCommandHandler().HandleAsync(
                new MoveVisualStateCommand(
                    TestDocumentId,
                    DocumentRevision.Zero,
                    VisualId,
                    new PointD(80d, 90d)),
                Snapshot(),
                cancellation.Token));
    }

    private static DocumentSnapshot Snapshot()
    {
        var semantic = new SemanticModelSnapshot(
            TestDocumentId,
            DocumentRevision.Zero,
            [new SemanticElementSnapshot(ElementId, new SemanticTypeId("test:type"))]);
        var visual = new VisualModelSnapshot(
            TestDocumentId,
            DocumentRevision.Zero,
            [
                new VisualStateSnapshot(
                    VisualId,
                    ElementId,
                    new PointD(10d, 20d),
                    new SizeD(30d, 40d),
                    VisualPlacementMode.Manual,
                    [new PointD(1d, 2d), new PointD(3d, 4d)],
                    [new("test:style", PropertyValue.FromText("preserved"))]),
            ]);
        var metadata = new DocumentMetadataSnapshot(
            TestDocumentId,
            DocumentRevision.Zero,
            [new("test:schema", PropertyValue.FromInteger(1))]);

        return new DocumentSnapshot(semantic, visual, metadata);
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }
}
