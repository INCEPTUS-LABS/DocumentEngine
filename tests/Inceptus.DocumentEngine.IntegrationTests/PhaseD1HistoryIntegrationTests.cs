using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.IntegrationTests.Fixtures;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseD1HistoryIntegrationTests
{
    private static readonly VisualStateId AlphaVisualId = new("test:visual-alpha");

    [Fact]
    public async Task TwoMovesTwoUndosAndTwoRedosAdvanceRevisionsZeroThroughSix()
    {
        var document = CreateDocument();
        var events = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [events]);
        var history = new HistoryManager(document);
        var initialPosition = Position(document);
        var firstPosition = new PointD(220d, 140d);
        var secondPosition = new PointD(340d, 220d);

        var first = await history.ExecuteAsync(
            processor,
            Move(document, firstPosition));
        var second = await history.ExecuteAsync(
            processor,
            Move(document, secondPosition));
        var undoSecond = await history.UndoAsync(processor);
        var undoFirst = await history.UndoAsync(processor);
        var redoFirst = await history.RedoAsync(processor);
        var redoSecond = await history.RedoAsync(processor);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            Enumerable.Range(1, 6).Select(value => new DocumentRevision((ulong)value)),
            new[] { first, second, undoSecond, undoFirst, redoFirst, redoSecond }
                .Select(result => result.CommittedRevision!.Value));
        Assert.All(
            new[] { first, second, undoSecond, undoFirst, redoFirst, redoSecond },
            result => Assert.True(result.IsCommitted));
        Assert.Equal(new DocumentRevision(6), document.Revision);
        Assert.Equal(secondPosition, Position(document));
        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: false), history.CaptureStatus());
        Assert.Equal(1, first.HistoryStatus.EntryCount);
        Assert.Equal(2, redoSecond.HistoryStatus.EntryCount);
        Assert.Equal(
            Enumerable.Range(1, 6).Select(value => new DocumentRevision((ulong)value)),
            events.Events.Select(change => change.CommittedRevision));
        Assert.Equal(6, events.Events.Count);
        Assert.Equal(initialPosition,
            events.Events.Single(change => change.CommittedRevision == new DocumentRevision(4))
                .CommittedSnapshot.VisualModel.VisualStates
                .Single(state => state.Id == AlphaVisualId).Position);
    }

    [Fact]
    public async Task UndoThenNewMoveTruncatesRedoAndRetainsOneEventPerCommit()
    {
        var document = CreateDocument();
        var events = new RecordingSubscriber();
        var processor = new CommandProcessor(subscribers: [events]);
        var history = new HistoryManager(document);

        Assert.True((await history.ExecuteAsync(
            processor,
            Move(document, new PointD(210d, 130d)))).IsCommitted);
        Assert.True((await history.ExecuteAsync(
            processor,
            Move(document, new PointD(310d, 190d)))).IsCommitted);
        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: true), history.CaptureStatus());

        Assert.True((await history.ExecuteAsync(
            processor,
            Move(document, new PointD(410d, 250d)))).IsCommitted);
        var redo = await history.RedoAsync(processor);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HistoryOperationStatus.NothingToRedo, redo.Status);
        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: false), history.CaptureStatus());
        Assert.Equal(new DocumentRevision(4), document.Revision);
        Assert.Equal(new PointD(410d, 250d), Position(document));
        Assert.Equal(4, events.Events.Count);
        Assert.Equal(
            Enumerable.Range(1, 4).Select(value => new DocumentRevision((ulong)value)),
            events.Events.Select(change => change.CommittedRevision));
    }

    [Fact]
    public async Task SubscriberObservesRecordedHistoryBeforeReentrantCommit()
    {
        var document = CreateDocument();
        HistoryManager? history = null;
        CommandProcessor? processor = null;
        HistoryStatus? observedBeforeReentry = null;
        HistoryOperationResult? reentrantResult = null;
        var events = new RecordingSubscriber(async change =>
        {
            if (change.CommittedRevision == new DocumentRevision(1))
            {
                observedBeforeReentry = history!.CaptureStatus();
                reentrantResult = await history.ExecuteAsync(
                    processor!,
                    new MoveVisualStateCommand(
                        document.DocumentId,
                        change.CommittedRevision,
                        AlphaVisualId,
                        new PointD(360d, 240d)));
            }
        });
        processor = new CommandProcessor(subscribers: [events]);
        history = new HistoryManager(document);

        var outer = await history.ExecuteAsync(
            processor,
            Move(document, new PointD(220d, 140d)));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(outer.IsCommitted);
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false), observedBeforeReentry);
        Assert.True(Assert.IsType<HistoryOperationResult>(reentrantResult).IsCommitted);
        Assert.Equal(new HistoryStatus(2, canUndo: true, canRedo: false), history.CaptureStatus());
        Assert.Equal(new DocumentRevision(2), document.Revision);
        Assert.Equal(
            [new DocumentRevision(1), new DocumentRevision(2)],
            events.Events.Select(change => change.CommittedRevision));
        Assert.Equal(new PointD(360d, 240d), Position(document));
    }

    private static Document CreateDocument()
    {
        var fixture = new PluginNeutralDocumentFixture();
        return Assert.IsType<Document>(
            DocumentFactory.Create(fixture.CreateSnapshot(DocumentRevision.Zero)).Document);
    }

    private static MoveVisualStateCommand Move(Document document, PointD position) =>
        new(document.DocumentId, document.Revision, AlphaVisualId, position);

    private static PointD Position(Document document) =>
        document.VisualModel.VisualStates.Single(state => state.Id == AlphaVisualId).Position;

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
