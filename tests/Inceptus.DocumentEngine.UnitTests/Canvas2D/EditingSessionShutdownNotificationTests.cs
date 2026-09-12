using System.Collections.Concurrent;
using System.Reflection;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class EditingSessionShutdownNotificationTests
{
    [Fact]
    public async Task DocumentObservationCannotOwnSubscriberGateWhileShutdownOwnsNotificationGate()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var sceneEntered = Signal();
        var observationPaused = Signal();
        var releaseScene = Signal();
        var releaseDocumentRun = Signal();
        using var releaseObservation = new ManualResetEventSlim();
        object? subscriberGate = null;
        var observationOwnsSubscriberGate = false;
        pipeline.EnqueueScene(async (artifacts, visualModel, editorState, cancellationToken) =>
        {
            using var registration = cancellationToken.Register(() =>
            {
                observationOwnsSubscriberGate = Monitor.IsEntered(subscriberGate!);
                observationPaused.TrySetResult();
                releaseObservation.Wait();
            });
            sceneEntered.TrySetResult();
            await releaseScene.Task;
            return ControlledEditingSessionPipeline.Success(artifacts, visualModel, editorState);
        });
        pipeline.EnqueueFull(async (snapshot, editorState, _) =>
        {
            await releaseDocumentRun.Task;
            return ControlledEditingSessionPipeline.Success(
                EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision),
                snapshot.VisualModel,
                editorState);
        });
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var subscriber = PrivateField(session, "_documentChangedSubscriber");
        subscriberGate = PrivateField(subscriber, "_sync");
        var notificationGate = PrivateField(session, "_notificationGate");
        var update = session.UpdateEditorStateAsync(
            new EditorStateSnapshot(activeToolId: "tool:shutdown-observation")).AsTask();
        var unsafeOpposingOwnership = false;

        try
        {
            await sceneEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var visual = document.VisualModel.VisualStates.First();
            var command = await session.ExecuteAsync(new MoveVisualStateCommand(
                document.DocumentId,
                document.Revision,
                visual.Id,
                new PointD(300d, 200d)));
            Assert.True(command.IsCommitted);
            await observationPaused.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Cancelling the preceding run pauses real document observation before its
            // notification. Probe the first two CloseAsync acquisitions without waiting:
            // opposing ownership proves the old cycle without deadlocking a test worker.
            var notificationEntered = Monitor.TryEnter(notificationGate);
            var subscriberEntered = false;
            try
            {
                if (notificationEntered)
                {
                    subscriberEntered = Monitor.TryEnter(subscriberGate);
                    unsafeOpposingOwnership = !subscriberEntered;
                }
            }
            finally
            {
                if (subscriberEntered)
                {
                    Monitor.Exit(subscriberGate);
                }
                if (notificationEntered)
                {
                    Monitor.Exit(notificationGate);
                }
            }
        }
        finally
        {
            releaseObservation.Set();
            releaseScene.TrySetResult();
            releaseDocumentRun.TrySetResult();
        }

        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(10));
        await update.WaitAsync(TimeSpan.FromSeconds(10));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        var committedSnapshot = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var close = await session.CloseAsync();

        Assert.True(observationOwnsSubscriberGate);
        Assert.False(unsafeOpposingOwnership,
            "Shutdown can own the notification gate while document observation owns the subscriber gate and is about to request notification.");
        Assert.Equal(EditingSessionOperationStatus.Closed, close.Status);
        Assert.Same(committedSnapshot, document.CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.Equal(1, execution.DisposeCount);
    }

    [Fact]
    public async Task CloseOverlappingDocumentObservationCompletesAndDeliversCommittedEventOnce()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var scheduler = new DeferredDispatchScheduler();
        var document = new Document(
            EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot(),
            scheduler);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var releaseRun = Signal();
        pipeline.EnqueueFull(async (snapshot, editorState, _) =>
        {
            await releaseRun.Task;
            return ControlledEditingSessionPipeline.Success(
                EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision),
                snapshot.VisualModel,
                editorState);
        });
        var subscriber = new RecordingSubscriber();
        var (session, execution) = await AttachAsync(document, pipeline, subscriber);
        await using var ownedSession = session;
        var retiredSubscriber = (IDocumentChangedSubscriber)PrivateField(
            session, "_documentChangedSubscriber");
        var observationEntered = Signal();
        var closeInvoked = Signal();
        using var releaseObservation = new ManualResetEventSlim();
        var notificationCount = 0;
        session.StateChanged += (_, _) =>
        {
            if (Interlocked.Increment(ref notificationCount) == 1)
            {
                observationEntered.TrySetResult();
                releaseObservation.Wait();
            }
        };
        var command = await MoveAsync(session, document);
        Assert.True(command.IsCommitted);
        var committedSnapshot = document.CaptureSnapshot();
        var committedHistory = session.CaptureState().HistoryStatus;
        var dispatch = scheduler.RunAsync();
        Task<EditingSessionOperationResult>? close = null;

        try
        {
            await observationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            close = Task.Run(async () =>
            {
                closeInvoked.TrySetResult();
                return await session.CloseAsync();
            });
            await closeInvoked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, execution.DisposeCount);
            Assert.Same(committedSnapshot, document.CaptureSnapshot());
        }
        finally
        {
            releaseObservation.Set();
            releaseRun.TrySetResult();
        }

        await dispatch.WaitAsync(TimeSpan.FromSeconds(10));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(10));
        var closed = await close!.WaitAsync(TimeSpan.FromSeconds(10));
        var delivered = Assert.Single(subscriber.Events);
        Assert.Same(committedSnapshot, delivered.CommittedSnapshot);
        Assert.Equal(command.DocumentId, delivered.DocumentId);
        Assert.Equal(command.CommittedRevision, delivered.CommittedRevision);
        Assert.Equal(EditingSessionOperationStatus.Closed, closed.Status);
        Assert.Same(committedSnapshot, document.CaptureSnapshot());
        Assert.Equal(committedHistory, session.CaptureState().HistoryStatus);
        Assert.Equal(1, committedHistory.EntryCount);
        Assert.Equal(1, execution.DisposeCount);

        var notificationsAtRetirement = Volatile.Read(ref notificationCount);
        await retiredSubscriber.OnDocumentChangedAsync(delivered);
        Assert.Equal(notificationsAtRetirement, Volatile.Read(ref notificationCount));
        Assert.Single(subscriber.Events);
        Assert.True(session.CaptureState().IsClosed);
        Assert.Null(session.CaptureState().CurrentScene);
    }

    [Fact]
    public async Task CloseBeforeQueuedDispatchRetiresSessionWithoutLosingCommittedSubscribers()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var scheduler = new DeferredDispatchScheduler();
        var document = new Document(
            EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot(),
            scheduler);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var subscriber = new RecordingSubscriber();
        var (session, execution) = await AttachAsync(document, pipeline, subscriber);
        await using var ownedSession = session;
        var notificationCount = 0;
        session.StateChanged += (_, _) => Interlocked.Increment(ref notificationCount);
        var command = await MoveAsync(session, document);
        Assert.True(command.IsCommitted);
        var committedSnapshot = document.CaptureSnapshot();
        var committedHistory = session.CaptureState().HistoryStatus;

        var close = await session.CloseAsync();
        Assert.Empty(subscriber.Events);
        await scheduler.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(EditingSessionOperationStatus.Closed, close.Status);
        var delivered = Assert.Single(subscriber.Events);
        Assert.Same(committedSnapshot, delivered.CommittedSnapshot);
        Assert.Equal(command.DocumentId, delivered.DocumentId);
        Assert.Equal(command.CommittedRevision, delivered.CommittedRevision);
        Assert.Equal(0, Volatile.Read(ref notificationCount));
        Assert.Equal(1, pipeline.DocumentRunCount);
        Assert.Equal(1, execution.DisposeCount);
        Assert.Same(committedSnapshot, document.CaptureSnapshot());
        Assert.Equal(committedHistory, session.CaptureState().HistoryStatus);
        Assert.Equal(1, committedHistory.EntryCount);
        Assert.False(session.TryCaptureDocumentSnapshot(out _));
    }

    [Fact]
    public async Task DocumentStateObserverCanInitiateCloseReentrantly()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var scheduler = new DeferredDispatchScheduler();
        var document = new Document(
            EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot(),
            scheduler);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var releaseRun = Signal();
        pipeline.EnqueueFull(async (snapshot, editorState, _) =>
        {
            await releaseRun.Task;
            return ControlledEditingSessionPipeline.Success(
                EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision),
                snapshot.VisualModel,
                editorState);
        });
        var subscriber = new RecordingSubscriber();
        var (session, execution) = await AttachAsync(document, pipeline, subscriber);
        await using var ownedSession = session;
        var closeRequested = new TaskCompletionSource<Task<EditingSessionOperationResult>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observerCount = 0;
        EditingSessionState? callbackState = null;
        session.StateChanged += (_, _) =>
        {
            Interlocked.Increment(ref observerCount);
            callbackState = session.CaptureState();
            closeRequested.TrySetResult(session.CloseAsync().AsTask());
        };
        var command = await MoveAsync(session, document);
        Assert.True(command.IsCommitted);
        var committedSnapshot = document.CaptureSnapshot();
        var committedHistory = session.CaptureState().HistoryStatus;
        var dispatch = scheduler.RunAsync();
        Task<EditingSessionOperationResult> close;

        try
        {
            close = await closeRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            releaseRun.TrySetResult();
        }

        await dispatch.WaitAsync(TimeSpan.FromSeconds(10));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(10));
        var closed = await close.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(EditingSessionOperationStatus.Closed, closed.Status);
        Assert.Equal(1, Volatile.Read(ref observerCount));
        Assert.NotNull(callbackState);
        Assert.Equal(committedSnapshot.Revision, callbackState.DocumentRevision);
        var delivered = Assert.Single(subscriber.Events);
        Assert.Same(committedSnapshot, delivered.CommittedSnapshot);
        Assert.Equal(command.DocumentId, delivered.DocumentId);
        Assert.Equal(command.CommittedRevision, delivered.CommittedRevision);
        Assert.Same(committedSnapshot, document.CaptureSnapshot());
        Assert.Equal(committedHistory, session.CaptureState().HistoryStatus);
        Assert.Equal(1, execution.DisposeCount);
    }

    [Fact]
    public async Task ConcurrentCloseCallsShareCompletionAndPreserveDocumentAndHistory()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var entered = Signal();
        var cancelled = Signal();
        var releaseRun = Signal();
        pipeline.EnqueueScene(async (artifacts, visualModel, editorState, cancellationToken) =>
        {
            using var registration = cancellationToken.Register(() => cancelled.TrySetResult());
            entered.TrySetResult();
            await releaseRun.Task;
            return ControlledEditingSessionPipeline.Success(artifacts, visualModel, editorState);
        });
        var (session, execution) = await AttachAsync(document, pipeline);
        await using var ownedSession = session;
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var notificationCount = 0;
        session.StateChanged += (_, _) => Interlocked.Increment(ref notificationCount);
        var update = session.UpdateEditorStateAsync(
            new EditorStateSnapshot(activeToolId: "tool:concurrent-close")).AsTask();
        Task<EditingSessionOperationResult> first;
        Task<EditingSessionOperationResult> second;

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            first = session.CloseAsync().AsTask();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = session.CloseAsync().AsTask();
            Assert.Same(first, second);
            Assert.False(first.IsCompleted);
            Assert.Equal(0, execution.DisposeCount);
        }
        finally
        {
            releaseRun.TrySetResult();
        }

        var closed = await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(closed, await second.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Same(closed, await session.CloseAsync());
        Assert.Equal(EditingSessionOperationStatus.Closed,
            (await update.WaitAsync(TimeSpan.FromSeconds(10))).Status);
        Assert.Equal(1, Volatile.Read(ref notificationCount));
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.True(session.CaptureState().IsClosed);
        Assert.Equal(1, execution.DisposeCount);
    }

    private static async Task<(EditingSession Session, Canvas2DRendererTestExecution Execution)>
        AttachAsync(
            Document document,
            ControlledEditingSessionPipeline pipeline,
            params IDocumentChangedSubscriber[] subscribers)
    {
        var configuration = EditingSessionTestHarness.Configuration();
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            new EditingSessionConfiguration(
                configuration.ProjectionEngine,
                configuration.LayoutEngine,
                configuration.LayoutAlgorithmId,
                configuration.RoutingEngine,
                configuration.RoutingAlgorithmId,
                configuration.SceneBuilder,
                documentChangedSubscribers: subscribers),
            pipeline);
        return (Assert.IsType<EditingSession>(attachment.Session), execution);
    }

    private static ValueTask<Inceptus.DocumentEngine.Contracts.History.HistoryOperationResult>
        MoveAsync(EditingSession session, Document document) =>
        session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            document.VisualModel.VisualStates.First().Id,
            new PointD(300d, 200d)));

    private sealed class DeferredDispatchScheduler : IDocumentDispatchScheduler
    {
        private Action? _callback;

        public bool TrySchedule(Action callback)
        {
            Assert.Null(Interlocked.CompareExchange(ref _callback, callback, null));
            return true;
        }

        internal Task RunAsync()
        {
            var callback = Interlocked.Exchange(ref _callback, null);
            Assert.NotNull(callback);
            return Task.Run(callback);
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

    private static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static object PrivateField(object owner, string name) =>
        Assert.IsAssignableFrom<object>(owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(owner));
}
