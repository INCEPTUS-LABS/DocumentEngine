using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.IntegrationTests.Fixtures;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseC2CommandExecutionTests
{
    private static readonly VisualStateId AlphaVisualId = new("test:visual-alpha");

    [Fact]
    public async Task NormalCommitInstallsRevisionOneAndEnqueuesOneExactVisualChange()
    {
        var document = CreateDocument();
        var before = document.CaptureSnapshot();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);

        var result = await processor.ExecuteAsync(
            document,
            Move(document, DocumentRevision.Zero, new PointD(240d, 160d)));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        var committed = document.CaptureSnapshot();
        var change = Assert.Single(subscriber.Events);
        Assert.True(result.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(document.Revision, result.CommittedRevision);
        Assert.Equal(before.Revision, change.PreviousRevision);
        Assert.Equal(document.Revision, change.CommittedRevision);
        Assert.Equal(AuthoritativeDocumentComponent.VisualModel, change.AffectedComponents);
        Assert.Equal(committed, change.CommittedSnapshot);
        Assert.Same(committed, change.CommittedSnapshot);
        Assert.Equal(committed, result.CommittedSnapshot);
        Assert.Equal(
            before.SemanticModel.Elements.AsEnumerable(),
            committed.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            before.SemanticModel.Relationships.AsEnumerable(),
            committed.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(
            before.Metadata.SystemManagedProperties,
            committed.Metadata.SystemManagedProperties);
        Assert.Equal(
            before.Metadata.ExtensionProperties,
            committed.Metadata.ExtensionProperties);
        Assert.Equal(
            new PointD(240d, 160d),
            committed.VisualModel.VisualStates.Single(state => state.Id == AlphaVisualId).Position);
        Assert.Equal(
            before.VisualModel.VisualStates
                .Where(state => state.Id != AlphaVisualId)
                .AsEnumerable(),
            committed.VisualModel.VisualStates
                .Where(state => state.Id != AlphaVisualId)
                .AsEnumerable());
    }

    [Fact]
    public async Task TwoRevisionZeroCommandsYieldOneCommitAndOneStaleEnvelopeFailure()
    {
        var document = CreateDocument();
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);
        var first = Move(document, DocumentRevision.Zero, new PointD(210d, 130d));
        var stale = Move(document, DocumentRevision.Zero, new PointD(300d, 190d));

        var firstResult = await processor.ExecuteAsync(document, first);
        var staleResult = await processor.ExecuteAsync(document, stale);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(firstResult.IsCommitted);
        Assert.False(staleResult.IsCommitted);
        Assert.Equal(CommandExecutionStatus.EnvelopeValidationFailed, staleResult.Status);
        Assert.Contains(staleResult.Diagnostics, diagnostic =>
            diagnostic.Code == CommandValidationDiagnosticCodes.StaleRevision);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Single(subscriber.Events);
        Assert.Equal(
            new PointD(210d, 130d),
            document.VisualModel.VisualStates.Single(state => state.Id == AlphaVisualId).Position);
    }

    [Fact]
    public async Task RapidCommitsPreserveFifoOrderAndRevisionSpecificEventSnapshots()
    {
        var firstDeliveryEntered = NewSignal();
        var permitFirstDeliveryToComplete = NewSignal();
        var subscriber = new RecordingSubscriber(async change =>
        {
            if (change.CommittedRevision == new DocumentRevision(1))
            {
                firstDeliveryEntered.TrySetResult();
                await permitFirstDeliveryToComplete.Task;
            }
        });
        var document = CreateDocument();
        var processor = new CommandProcessor(subscribers: [subscriber]);

        var first = await processor.ExecuteAsync(
            document,
            Move(document, DocumentRevision.Zero, new PointD(210d, 130d)));
        await firstDeliveryEntered.Task;
        var second = await processor.ExecuteAsync(
            document,
            Move(document, new DocumentRevision(1), new PointD(330d, 210d)));

        Assert.True(first.IsCommitted);
        Assert.True(second.IsCommitted);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        permitFirstDeliveryToComplete.TrySetResult();
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        var events = subscriber.Events.ToArray();
        Assert.Equal(2, events.Length);
        Assert.Equal(
            [new DocumentRevision(1), new DocumentRevision(2)],
            events.Select(change => change.CommittedRevision));
        Assert.Equal(new DocumentRevision(1), events[0].CommittedSnapshot.Revision);
        Assert.Equal(new DocumentRevision(2), events[1].CommittedSnapshot.Revision);
        Assert.Equal(
            new PointD(210d, 130d),
            events[0].CommittedSnapshot.VisualModel.VisualStates
                .Single(state => state.Id == AlphaVisualId).Position);
        Assert.Equal(
            new PointD(330d, 210d),
            events[1].CommittedSnapshot.VisualModel.VisualStates
                .Single(state => state.Id == AlphaVisualId).Position);
    }

    [Fact]
    public async Task SubscriberCanAwaitAReentrantSameDocumentCommandWithoutDeadlock()
    {
        var document = CreateDocument();
        CommandProcessor? processor = null;
        CommandExecutionResult? reentrantResult = null;
        var subscriber = new RecordingSubscriber(async change =>
        {
            if (change.CommittedRevision == new DocumentRevision(1))
            {
                reentrantResult = await processor!.ExecuteAsync(
                    document,
                    Move(
                        document,
                        change.CommittedRevision,
                        new PointD(360d, 240d)));
            }
        });
        processor = new CommandProcessor(subscribers: [subscriber]);

        var outerResult = await processor.ExecuteAsync(
            document,
            Move(document, DocumentRevision.Zero, new PointD(210d, 130d)));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(outerResult.IsCommitted);
        Assert.True(Assert.IsType<CommandExecutionResult>(reentrantResult).IsCommitted);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        Assert.Equal(
            [new DocumentRevision(1), new DocumentRevision(2)],
            subscriber.Events.Select(change => change.CommittedRevision));
        Assert.Equal(
            new PointD(360d, 240d),
            document.VisualModel.VisualStates.Single(state => state.Id == AlphaVisualId).Position);
    }

    private static Document CreateDocument()
    {
        var fixture = new PluginNeutralDocumentFixture();
        return Assert.IsType<Document>(
            DocumentFactory.Create(fixture.CreateSnapshot(DocumentRevision.Zero)).Document);
    }

    private static MoveVisualStateCommand Move(
        Document document,
        DocumentRevision expectedRevision,
        PointD targetPosition) =>
        new(
            document.DocumentId,
            expectedRevision,
            AlphaVisualId,
            targetPosition);

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        private readonly Func<DocumentChangedEvent, ValueTask>? _callback;

        internal RecordingSubscriber(Func<DocumentChangedEvent, ValueTask>? callback = null) =>
            _callback = callback;

        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public async ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            if (_callback is not null)
            {
                await _callback(change);
            }
        }
    }
}
