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

public sealed class ResizeVisualStateCommandHandlerTests
{
    private static readonly DocumentId TestDocumentId = new("test:resize-handler");
    private static readonly SemanticElementId ElementId = new("test:element");
    private static readonly VisualStateId VisualId = new("test:visual");

    [Fact]
    public async Task ResizeProposalChangesOnlyBoundsAndPreservesPersistentVisualData()
    {
        var snapshot = Snapshot();
        var original = Assert.Single(snapshot.VisualModel.VisualStates);
        var command = new ResizeVisualStateCommand(
            TestDocumentId,
            DocumentRevision.Zero,
            VisualId,
            new RectD(80d, 90d, 120d, 70d));

        var result = await new ResizeVisualStateCommandHandler()
            .HandleAsync(command, snapshot, CancellationToken.None);

        Assert.True(result.Succeeded);
        var proposed = Assert.IsType<DocumentSnapshot>(result.ProposedDocument);
        var resized = Assert.Single(proposed.VisualModel.VisualStates);
        Assert.Equal(new PointD(80d, 90d), resized.Position);
        Assert.Equal(new SizeD(120d, 70d), resized.Size);
        Assert.Equal(original.PlacementMode, resized.PlacementMode);
        Assert.Equal(original.SemanticElementId, resized.SemanticElementId);
        Assert.Equal(original.Route, resized.Route);
        Assert.Equal(original.Properties, resized.Properties);
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
        var result = await new ResizeVisualStateCommandHandler().HandleAsync(
            new ResizeVisualStateCommand(
                TestDocumentId,
                DocumentRevision.Zero,
                VisualId,
                new RectD(80d, 90d, 120d, 70d),
                VisualPlacementMode.Pinned),
            Snapshot(),
            CancellationToken.None);

        var resized = Assert.Single(Assert.IsType<DocumentSnapshot>(result.ProposedDocument)
            .VisualModel.VisualStates);
        Assert.Equal(VisualPlacementMode.Pinned, resized.PlacementMode);
    }

    [Fact]
    public async Task ZeroExtentIsRejectedForNodeGeometry()
    {
        var result = await new ResizeVisualStateCommandHandler().HandleAsync(
            new ResizeVisualStateCommand(
                TestDocumentId,
                DocumentRevision.Zero,
                VisualId,
                new RectD(80d, 90d, 0d, 0d)),
            Snapshot(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task MissingVisualStateFailsWithoutAProposal()
    {
        var result = await new ResizeVisualStateCommandHandler().HandleAsync(
            new ResizeVisualStateCommand(
                TestDocumentId,
                DocumentRevision.Zero,
                new VisualStateId("test:missing"),
                new RectD(80d, 90d, 120d, 70d)),
            Snapshot(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.VisualStateNotFound,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task RelationshipVisualStateCannotBeResizedAsAPositionedElement()
    {
        var relationshipId = new SemanticElementId("test:relationship");
        var relationshipVisualId = new VisualStateId("test:relationship-visual");
        var sourceId = new SemanticElementId("test:source");
        var targetId = new SemanticElementId("test:target");
        var semantic = new SemanticModelSnapshot(
            TestDocumentId,
            DocumentRevision.Zero,
            [
                new SemanticElementSnapshot(sourceId, new SemanticTypeId("test:type")),
                new SemanticElementSnapshot(targetId, new SemanticTypeId("test:type")),
            ],
            [
                new SemanticRelationshipSnapshot(
                    relationshipId,
                    new SemanticTypeId("test:relationship-type"),
                    sourceId,
                    targetId),
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

        var result = await new ResizeVisualStateCommandHandler().HandleAsync(
            new ResizeVisualStateCommand(
                TestDocumentId,
                DocumentRevision.Zero,
                relationshipVisualId,
                new RectD(80d, 90d, 120d, 70d)),
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
        var excessive = Math.Sqrt(double.MaxValue) / 8d;

        var result = await history.ExecuteAsync(
            processor,
            new ResizeVisualStateCommand(
                TestDocumentId,
                DocumentRevision.Zero,
                VisualId,
                new RectD(80d, 90d, excessive, 70d),
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
    public async Task NegativeTargetBoundsAreRejectedAuthoritatively(
        double x,
        double y)
    {
        var result = await new ResizeVisualStateCommandHandler().HandleAsync(
            new ResizeVisualStateCommand(
                TestDocumentId,
                DocumentRevision.Zero,
                VisualId,
                new RectD(x, y, 30d, 40d),
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
            await new ResizeVisualStateCommandHandler().HandleAsync(
                new ResizeVisualStateCommand(
                    TestDocumentId,
                    DocumentRevision.Zero,
                    VisualId,
                    new RectD(80d, 90d, 120d, 70d)),
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
