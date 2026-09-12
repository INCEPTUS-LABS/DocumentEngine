using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class CommandDispatchDiagnosticsTests
{
    private static readonly SemanticElementId ElementId = new("test:dispatch-element");
    private static readonly VisualStateId VisualId = new("test:dispatch-visual");

    [Fact]
    public async Task DiagnosticsArePublicImmutableBoundedAndRetainTheNewestEntries()
    {
        var blocker = new BlockingSubscriber();
        var subscribers = new List<IDocumentChangedSubscriber>
        {
            new ThrowingSubscriber("discarded-000"),
            blocker,
        };
        subscribers.AddRange(Enumerable.Range(0, 256).Select(static index =>
            (IDocumentChangedSubscriber)new ThrowingSubscriber($"retained-{index:D3}")));

        var document = CreateDocument(new DocumentId("test:bounded-dispatch-diagnostics"));
        var processor = new CommandProcessor(subscribers: subscribers);

        var result = await processor.ExecuteAsync(document, Move(document, 30d));
        await blocker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var beforeOverflow = processor.CaptureDispatchDiagnostics();
        var oldest = Assert.Single(beforeOverflow);
        blocker.Release.TrySetResult();
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));

        var retained = processor.CaptureDispatchDiagnostics();

        Assert.True(result.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Single(beforeOverflow);
        Assert.Equal(256, retained.Length);
        Assert.DoesNotContain(retained, diagnostic => ReferenceEquals(diagnostic, oldest));
        Assert.All(retained, diagnostic => Assert.Equal(
            CommandExecutionDiagnosticCodes.SubscriberFailure,
            diagnostic.Code));
        Assert.Equal(
            Enumerable.Range(0, 256).Select(static index => $"retained-{index:D3}"),
            retained.Select(diagnostic => diagnostic.Context["ExceptionMessage"]));
    }

    [Fact]
    public async Task ConcurrentSubscriberFailuresDoNotChangeCommittedResults()
    {
        const int documentCount = 16;
        var processor = new CommandProcessor(subscribers: [new ThrowingSubscriber()]);
        var documents = Enumerable.Range(0, documentCount)
            .Select(index => CreateDocument(new DocumentId($"test:concurrent-dispatch-{index:D2}")))
            .ToArray();

        var results = await Task.WhenAll(documents.Select((document, index) =>
            processor.ExecuteAsync(document, Move(document, 100d + index)).AsTask()));
        await Task.WhenAll(documents.Select(document =>
                CommandProcessor.WaitForEventDispatchIdleAsync(document)))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(results, result => Assert.True(result.IsCommitted));
        Assert.All(documents, document => Assert.Equal(new DocumentRevision(1), document.Revision));
        Assert.Equal(
            documentCount,
            processor.CaptureDispatchDiagnostics().Count(diagnostic =>
                diagnostic.Code == CommandExecutionDiagnosticCodes.SubscriberFailure));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SchedulingRejectionOrThrowIsObservableAndLaterEnqueueRetriesFifo(
        bool throwOnFirstAttempt)
    {
        var scheduler = new RejectOrThrowThenCaptureScheduler(throwOnFirstAttempt);
        var document = new Document(
            Snapshot(new DocumentId($"test:scheduling-retry-{throwOnFirstAttempt}")),
            scheduler);
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [subscriber]);

        var first = await processor.ExecuteAsync(document, Move(document, 40d));
        var originalIdle = CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.True(first.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Empty(subscriber.Events);
        Assert.False(originalIdle.IsCompleted);
        var schedulingDiagnostic = Assert.Single(processor.CaptureDispatchDiagnostics());
        Assert.Equal(
            CommandExecutionDiagnosticCodes.EventDispatchSchedulingFailure,
            schedulingDiagnostic.Code);

        var second = await processor.ExecuteAsync(document, Move(document, 50d));

        Assert.True(second.IsCommitted);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        Assert.Same(originalIdle, CommandProcessor.WaitForEventDispatchIdleAsync(document));
        Assert.Empty(subscriber.Events);
        Assert.Equal(2, scheduler.Attempts);

        await scheduler.RunCapturedAsync();
        await originalIdle.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            [new DocumentRevision(1), new DocumentRevision(2)],
            subscriber.Events.Select(change => change.CommittedRevision));
    }

    private static MoveVisualStateCommand Move(Document document, double x) =>
        new(
            document.DocumentId,
            document.Revision,
            VisualId,
            new PointD(x, 25d));

    private static Document CreateDocument(DocumentId documentId) =>
        new(Snapshot(documentId));

    private static DocumentSnapshot Snapshot(DocumentId documentId)
    {
        var semantic = new SemanticModelSnapshot(
            documentId,
            DocumentRevision.Zero,
            [new SemanticElementSnapshot(ElementId, new SemanticTypeId("test:dispatch-type"))]);
        var visual = new VisualModelSnapshot(
            documentId,
            DocumentRevision.Zero,
            [
                new VisualStateSnapshot(
                    VisualId,
                    ElementId,
                    new PointD(10d, 20d),
                    new SizeD(30d, 40d),
                    VisualPlacementMode.Manual),
            ]);
        var metadata = new DocumentMetadataSnapshot(documentId, DocumentRevision.Zero);
        return new DocumentSnapshot(semantic, visual, metadata);
    }

    private sealed class ThrowingSubscriber(string message = "Expected subscriber failure.") :
        IDocumentChangedSubscriber
    {
        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change) =>
            throw new InvalidOperationException(message);
    }

    private sealed class BlockingSubscriber : IDocumentChangedSubscriber
    {
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Entered.TrySetResult();
            await Release.Task.ConfigureAwait(false);
        }
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

    private sealed class RejectOrThrowThenCaptureScheduler(bool throwOnFirstAttempt) :
        IDocumentDispatchScheduler
    {
        private Action? _captured;
        private int _attempts;

        internal int Attempts => Volatile.Read(ref _attempts);

        public bool TrySchedule(Action callback)
        {
            Assert.NotNull(callback);
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                if (throwOnFirstAttempt)
                {
                    throw new InvalidOperationException("Expected scheduling failure.");
                }

                return false;
            }

            _captured = callback;
            return true;
        }

        internal Task RunCapturedAsync()
        {
            var callback = Interlocked.Exchange(ref _captured, null);
            Assert.NotNull(callback);
            return Task.Run(callback);
        }
    }
}
