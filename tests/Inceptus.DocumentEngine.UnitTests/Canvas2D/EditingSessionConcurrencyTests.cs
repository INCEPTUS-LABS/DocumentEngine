using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class EditingSessionConcurrencyTests
{
    [Fact]
    public async Task RapidCommittedEventsAreLatestWinsAndStaleRunCannotInstall()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.EnqueueFull(async (snapshot, editorState, _) =>
        {
            firstEntered.TrySetResult();
            await releaseFirst.Task;
            var artifacts = EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision);
            return ControlledEditingSessionPipeline.Success(
                artifacts,
                snapshot.VisualModel,
                editorState);
        });
        pipeline.EnqueueFull((snapshot, editorState, _) =>
        {
            var artifacts = EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision);
            return ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                snapshot.VisualModel,
                editorState));
        });
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var visual = document.VisualModel.VisualStates.First();

        var first = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            new PointD(300d, 200d)));
        Assert.True(first.IsCommitted);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            new PointD(500d, 350d)));
        Assert.True(second.IsCommitted);
        await WaitUntilAsync(
            () => session.CaptureState().Status == EditingSessionStatus.Ready &&
                session.CaptureState().DocumentRevision == document.Revision,
            TimeSpan.FromSeconds(5));
        var latest = session.CaptureState();

        releaseFirst.TrySetResult();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var final = session.CaptureState();

        Assert.Equal(new DocumentRevision(5), document.Revision);
        Assert.Equal(new DocumentRevision(5), latest.CurrentScene!.SourceRevision);
        Assert.Same(latest.CurrentScene, final.CurrentScene);
        Assert.Equal(EditingSessionStatus.Ready, final.Status);
        Assert.Equal(3, pipeline.DocumentRunCount);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task EveryCommittedEventStartsOneRunWhenLiveDocumentIsAlreadyNewer()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var staleEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStale = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.EnqueueFull(async (snapshot, editorState, _) =>
        {
            staleEntered.TrySetResult();
            await releaseStale.Task;
            return ControlledEditingSessionPipeline.Success(
                EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision),
                snapshot.VisualModel,
                editorState);
        });
        pipeline.EnqueueFull((snapshot, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision),
                snapshot.VisualModel,
                editorState)));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var visual = document.VisualModel.VisualStates.First();
        var processor = new CommandProcessor();

        var revisionFour = await processor.ExecuteAsync(document, new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            new PointD(300d, 200d)));
        var revisionFive = await processor.ExecuteAsync(document, new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            new PointD(500d, 350d)));
        Assert.True(revisionFour.IsCommitted);
        Assert.True(revisionFive.IsCommitted);
        Assert.Equal(new DocumentRevision(5), document.Revision);

        session.ObserveDocumentChanged(revisionFour.CommittedEvent!);
        await staleEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.ObserveDocumentChanged(revisionFive.CommittedEvent!);
        await WaitUntilAsync(
            () => session.CaptureState().Status == EditingSessionStatus.Ready &&
                session.CaptureState().CurrentScene?.SourceRevision == new DocumentRevision(5),
            TimeSpan.FromSeconds(5));
        var latestScene = session.CaptureState().CurrentScene;

        Assert.Equal(3, pipeline.DocumentRunCount);
        Assert.Equal(
            [new DocumentRevision(3), new DocumentRevision(4), new DocumentRevision(5)],
            pipeline.DocumentRuns.Select(static snapshot => snapshot.Revision));
        releaseStale.TrySetResult();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(latestScene, session.CaptureState().CurrentScene);
        Assert.Equal(new DocumentRevision(5), session.CaptureState().DocumentRevision);
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task DelayedFifoDispatchStartsOneRunForEachExactCommittedSnapshot()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var original = EditingSessionTestHarness.CreateDocument(inputs).CaptureSnapshot();
        var scheduler = new DeferredDispatchScheduler();
        var document = new Document(original, scheduler);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        pipeline.EnqueueFull((snapshot, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision),
                snapshot.VisualModel,
                editorState)));
        pipeline.EnqueueFull((snapshot, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision),
                snapshot.VisualModel,
                editorState)));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var events = new RecordingSubscriber();
        var baseConfiguration = EditingSessionTestHarness.Configuration();
        var configuration = new EditingSessionConfiguration(
            baseConfiguration.ProjectionEngine,
            baseConfiguration.LayoutEngine,
            baseConfiguration.LayoutAlgorithmId,
            baseConfiguration.RoutingEngine,
            baseConfiguration.RoutingAlgorithmId,
            baseConfiguration.SceneBuilder,
            documentChangedSubscribers: [events]);
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            configuration,
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var visual = document.VisualModel.VisualStates.First();

        var revisionFour = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            new PointD(300d, 200d)));
        var revisionFive = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            new PointD(500d, 350d)));

        Assert.True(revisionFour.IsCommitted);
        Assert.True(revisionFive.IsCommitted);
        Assert.Equal(new DocumentRevision(5), document.Revision);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, scheduler.PendingCount);
        scheduler.RunNext();
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, pipeline.DocumentRunCount);
        Assert.Equal(
            [new DocumentRevision(3), new DocumentRevision(4), new DocumentRevision(5)],
            pipeline.DocumentRuns.Select(static snapshot => snapshot.Revision));
        Assert.Equal(2, events.Events.Count);
        Assert.Same(events.Events.ElementAt(0).CommittedSnapshot, pipeline.DocumentRuns[1]);
        Assert.Same(events.Events.ElementAt(1).CommittedSnapshot, pipeline.DocumentRuns[2]);
        Assert.Equal(new DocumentRevision(5), session.CaptureState().CurrentScene?.SourceRevision);
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task EditorStateChangeDuringFullRebuildCannotLeaveSessionPermanentlyRebuilding()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.EnqueueFull(async (snapshot, editorState, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            var artifacts = EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision);
            return ControlledEditingSessionPipeline.Success(
                artifacts,
                snapshot.VisualModel,
                editorState);
        });
        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
        {
            return ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState));
        });
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var visual = document.VisualModel.VisualStates.First();

        var command = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            new PointD(400d, 250d)));
        Assert.True(command.IsCommitted);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var latestEditorState = new EditorStateSnapshot(activeToolId: "tool:during-full-run");

        var editorUpdate = await session.UpdateEditorStateAsync(latestEditorState);
        release.TrySetResult();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var state = session.CaptureState();

        Assert.True(editorUpdate.Succeeded);
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        Assert.Same(latestEditorState, state.EditorState);
        Assert.NotNull(state.CurrentScene);
        Assert.Equal(document.Revision, state.CurrentScene.SourceRevision);
        Assert.Equal(2, pipeline.DocumentRunCount);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.SceneRebuildCount);
        Assert.Same(latestEditorState, pipeline.SceneEditorStates[^1]);
    }

    [Fact]
    public async Task CloseCancelsAndAwaitsInFlightSceneRebuildBeforeRendererDisposal()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.EnqueueScene(async (_, _, _, cancellationToken) =>
        {
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellationObserved.TrySetResult();
                throw;
            }

            throw new InvalidOperationException("Unreachable test path.");
        });
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        var session = Assert.IsType<EditingSession>(attachment.Session);
        var update = session.UpdateEditorStateAsync(
            new EditorStateSnapshot(activeToolId: "tool:close-race")).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var close = session.CloseAsync().AsTask();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var closeResult = await close.WaitAsync(TimeSpan.FromSeconds(5));
        var updateResult = await update.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(EditingSessionOperationStatus.Closed, closeResult.Status);
        Assert.Equal(EditingSessionOperationStatus.Closed, updateResult.Status);
        Assert.True(session.CaptureState().IsClosed);
        Assert.Equal(1, execution.DisposeCount);
        Assert.Equal("dispose", execution.Calls[^1]);
    }

    [Fact]
    public async Task LatePipelineCompletionAfterCloseCannotInstallOrRender()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.EnqueueScene(async (artifacts, visualModel, editorState, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState);
        });
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        var session = Assert.IsType<EditingSession>(attachment.Session);
        var update = session.UpdateEditorStateAsync(
            new EditorStateSnapshot(activeToolId: "tool:late-result")).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var close = session.CloseAsync().AsTask();
        release.TrySetResult();
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        await update.WaitAsync(TimeSpan.FromSeconds(5));

        var state = session.CaptureState();
        Assert.True(state.IsClosed);
        Assert.Null(state.CurrentScene);
        Assert.Null(state.LastKnownGoodScene);
        Assert.Equal(1, execution.Calls.Count(call => call == "render"));
        Assert.Equal("dispose", execution.Calls[^1]);
    }

    [Fact]
    public async Task SupersededCancellationIgnoringRunCompletingAfterCloseIsSilent()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var staleEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStale = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleResultProduced = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.EnqueueFull(async (snapshot, editorState, _) =>
        {
            staleEntered.TrySetResult();
            await releaseStale.Task;
            var result = ControlledEditingSessionPipeline.Success(
                EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision),
                snapshot.VisualModel,
                editorState);
            staleResultProduced.TrySetResult();
            return result;
        });
        pipeline.EnqueueFull((snapshot, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision),
                snapshot.VisualModel,
                editorState)));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        var session = Assert.IsType<EditingSession>(attachment.Session);
        var notificationCount = 0;
        session.StateChanged += (_, _) => Interlocked.Increment(ref notificationCount);
        var visual = document.VisualModel.VisualStates.First();

        Assert.True((await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            new PointD(300d, 200d)))).IsCommitted);
        await staleEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            new PointD(500d, 350d)))).IsCommitted);
        await WaitUntilAsync(
            () => session.CaptureState().Status == EditingSessionStatus.Ready &&
                session.CaptureState().DocumentRevision == document.Revision,
            TimeSpan.FromSeconds(5));
        var idle = session.WaitForIdleAsync().AsTask();
        await Task.Yield();
        Assert.False(idle.IsCompleted);

        var rendersBeforeRelease = execution.Calls.Count(call => call == "render");
        var close = session.CloseAsync().AsTask();
        await Task.Yield();
        Assert.False(close.IsCompleted);
        releaseStale.TrySetResult();
        await staleResultProduced.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await close.WaitAsync(TimeSpan.FromSeconds(5));
        await idle.WaitAsync(TimeSpan.FromSeconds(5));
        var notificationsAtClose = Volatile.Read(ref notificationCount);
        var rendersAtClose = execution.Calls.Count(call => call == "render");
        await Task.Delay(100);

        Assert.True(session.CaptureState().IsClosed);
        Assert.Null(session.CaptureState().CurrentScene);
        Assert.Equal(rendersBeforeRelease, rendersAtClose);
        Assert.Equal(rendersAtClose, execution.Calls.Count(call => call == "render"));
        Assert.Equal(notificationsAtClose, Volatile.Read(ref notificationCount));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected Editing Session state was not observed.");
            }

            await Task.Yield();
        }
    }

    private sealed class DeferredDispatchScheduler : IDocumentDispatchScheduler
    {
        private readonly Queue<Action> _callbacks = [];

        internal int PendingCount
        {
            get
            {
                lock (_callbacks)
                {
                    return _callbacks.Count;
                }
            }
        }

        public bool TrySchedule(Action callback)
        {
            lock (_callbacks)
            {
                _callbacks.Enqueue(callback);
            }

            return true;
        }

        internal void RunNext()
        {
            Action callback;
            lock (_callbacks)
            {
                callback = _callbacks.Dequeue();
            }

            callback();
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
}
