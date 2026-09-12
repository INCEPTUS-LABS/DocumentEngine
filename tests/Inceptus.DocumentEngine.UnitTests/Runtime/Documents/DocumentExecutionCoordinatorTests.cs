using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Runtime.Documents;

public sealed class DocumentExecutionCoordinatorTests
{
    [Fact]
    public async Task IdleQueueUsesTheCompletionPreparedBeforeEnqueue()
    {
        var coordinator = new DocumentExecutionCoordinator();
        var workItem = new DocumentDispatchWorkItem(
            static () => ValueTask.CompletedTask);
        var preparedCompletion = workItem.DispatchIdleCompletion.Task;

        await coordinator.WaitForExecutionAsync(CancellationToken.None);
        var shouldStart = coordinator.Enqueue(workItem);
        var observedCompletion = coordinator.WaitForDispatchIdleAsync();
        coordinator.ReleaseExecution();

        Assert.True(shouldStart);
        Assert.Same(preparedCompletion, observedCompletion);

        workItem.ReleaseForDispatch();
        Assert.True(coordinator.TryStartDispatch().IsScheduled);
        await observedCompletion;
    }

    [Fact]
    public async Task PreparedWorkCannotInvokeSubscriberCodeBeforeGateReleaseSignal()
    {
        var callbackInvoked = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var workItem = new DocumentDispatchWorkItem(() =>
        {
            callbackInvoked.TrySetResult();
            return ValueTask.CompletedTask;
        });

        var dispatchTask = workItem.DispatchAsync().AsTask();

        Assert.False(callbackInvoked.Task.IsCompleted);
        Assert.False(dispatchTask.IsCompleted);

        workItem.ReleaseForDispatch();
        await dispatchTask;

        Assert.True(callbackInvoked.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task SchedulingRejectionKeepsTheOriginalFifoAndIdleGenerationRetryable()
    {
        var scheduler = new RejectThenCaptureScheduler();
        var coordinator = new DocumentExecutionCoordinator(scheduler);
        var delivered = new List<int>();
        var first = WorkItem(1, delivered);
        var second = WorkItem(2, delivered);

        await coordinator.WaitForExecutionAsync(CancellationToken.None);
        Assert.True(coordinator.Enqueue(first));
        var originalIdle = coordinator.WaitForDispatchIdleAsync();
        coordinator.ReleaseExecution();
        first.ReleaseForDispatch();

        Assert.False(coordinator.TryStartDispatch().IsScheduled);
        Assert.False(originalIdle.IsCompleted);

        await coordinator.WaitForExecutionAsync(CancellationToken.None);
        Assert.True(coordinator.Enqueue(second));
        Assert.Same(originalIdle, coordinator.WaitForDispatchIdleAsync());
        coordinator.ReleaseExecution();
        second.ReleaseForDispatch();

        Assert.True(coordinator.TryStartDispatch().IsScheduled);
        Assert.Empty(delivered);

        await scheduler.RunCapturedAsync();
        await originalIdle.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([1, 2], delivered);
    }

    private static DocumentDispatchWorkItem WorkItem(int value, List<int> delivered) =>
        new(() =>
        {
            delivered.Add(value);
            return ValueTask.CompletedTask;
        });

    private sealed class RejectThenCaptureScheduler : IDocumentDispatchScheduler
    {
        private Action? _captured;
        private int _calls;

        public bool TrySchedule(Action callback)
        {
            Assert.NotNull(callback);
            if (Interlocked.Increment(ref _calls) == 1)
            {
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
