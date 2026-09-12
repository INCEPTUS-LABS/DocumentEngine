using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class BpmnModelerNotificationsTests
{
    [Fact]
    public async Task BlockedInvocationKeepsLaterNotificationsInFifoOrder()
    {
        var firstEntered = NewCompletion();
        var releaseFirst = NewCompletion();
        var observed = new ConcurrentQueue<object>();
        var first = new object();
        var second = new object();
        var third = new object();
        using var notifications = new BpmnModelerNotifications(async notification =>
        {
            observed.Enqueue(notification);
            if (ReferenceEquals(notification, first))
            {
                firstEntered.SetResult();
                await releaseFirst.Task;
            }
        });

        var firstInvocation = notifications.Enqueue(first);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondInvocation = notifications.Enqueue(second);
        var thirdInvocation = notifications.Enqueue(third);
        try
        {
            Assert.Same(first, Assert.Single(observed));
            Assert.False(firstInvocation.IsCompleted);
            Assert.False(secondInvocation.IsCompleted);
            Assert.False(thirdInvocation.IsCompleted);
        }
        finally
        {
            releaseFirst.TrySetResult();
        }

        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Collection(observed,
            notification => Assert.Same(first, notification),
            notification => Assert.Same(second, notification),
            notification => Assert.Same(third, notification));
        Assert.True(firstInvocation.IsCompletedSuccessfully);
        Assert.True(secondInvocation.IsCompletedSuccessfully);
        Assert.True(thirdInvocation.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task PendingBarrierCoversTheInvocationsQueuedWhenCaptured()
    {
        var firstEntered = NewCompletion();
        var secondEntered = NewCompletion();
        var releaseFirst = NewCompletion();
        var releaseSecond = NewCompletion();
        var first = new object();
        using var notifications = new BpmnModelerNotifications(async notification =>
        {
            if (ReferenceEquals(notification, first))
            {
                firstEntered.SetResult();
                await releaseFirst.Task;
            }
            else
            {
                secondEntered.SetResult();
                await releaseSecond.Task;
            }
        });
        Assert.True(notifications.Pending.IsCompletedSuccessfully);
        var firstInvocation = notifications.Enqueue(first);
        var firstBarrier = notifications.Pending;
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondInvocation = notifications.Enqueue(new object());
        var bothBarrier = notifications.Pending;
        try
        {
            Assert.Same(firstInvocation, firstBarrier);
            Assert.Same(secondInvocation, bothBarrier);
            releaseFirst.SetResult();
            await firstBarrier.WaitAsync(TimeSpan.FromSeconds(5));
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(bothBarrier.IsCompleted);
        }
        finally
        {
            releaseFirst.TrySetResult();
            releaseSecond.TrySetResult();
        }

        await bothBarrier.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SeparateModelerStreamsDoNotBlockOrShareNotifications()
    {
        var firstEntered = NewCompletion();
        var releaseFirst = NewCompletion();
        var observedFirst = new ConcurrentQueue<object>();
        var observedSecond = new ConcurrentQueue<object>();
        using var firstStream = new BpmnModelerNotifications(async notification =>
        {
            observedFirst.Enqueue(notification);
            firstEntered.SetResult();
            await releaseFirst.Task;
        });
        using var secondStream = new BpmnModelerNotifications(notification =>
        {
            observedSecond.Enqueue(notification);
            return Task.CompletedTask;
        });
        var first = new object();
        var second = new object();
        var firstInvocation = firstStream.Enqueue(first);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await secondStream.Enqueue(second).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(firstInvocation.IsCompleted);
            Assert.Same(first, Assert.Single(observedFirst));
            Assert.Same(second, Assert.Single(observedSecond));
            Assert.True(secondStream.Pending.IsCompletedSuccessfully);
        }
        finally
        {
            releaseFirst.TrySetResult();
        }

        await firstInvocation.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DisposalSuppressesQueuedAndLaterInvocations()
    {
        var firstEntered = NewCompletion();
        var releaseFirst = NewCompletion();
        var observed = new ConcurrentQueue<object>();
        using var notifications = new BpmnModelerNotifications(async notification =>
        {
            observed.Enqueue(notification);
            firstEntered.SetResult();
            await releaseFirst.Task;
        });
        var first = new object();
        var firstInvocation = notifications.Enqueue(first);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queuedInvocation = notifications.Enqueue(new object());
        notifications.Dispose();
        notifications.Dispose();
        var lateInvocation = notifications.Enqueue(new object());
        try
        {
            Assert.True(lateInvocation.IsCompletedSuccessfully);
            Assert.Same(first, Assert.Single(observed));
        }
        finally
        {
            releaseFirst.TrySetResult();
        }

        await Task.WhenAll(firstInvocation, queuedInvocation, notifications.Pending)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(first, Assert.Single(observed));
    }

    [Fact]
    public async Task InvocationCanEnqueueFollowingNotificationWithoutSharingItsBarrier()
    {
        var observed = new ConcurrentQueue<object>();
        var first = new object();
        var second = new object();
        Task? followingInvocation = null;
        BpmnModelerNotifications? notifications = null;
        using var ownedNotifications = notifications = new BpmnModelerNotifications(notification =>
        {
            observed.Enqueue(notification);
            if (ReferenceEquals(notification, first))
            {
                followingInvocation = notifications!.Enqueue(second);
            }

            return Task.CompletedTask;
        });

        var firstInvocation = notifications.Enqueue(first);
        await firstInvocation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(followingInvocation);
        await followingInvocation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Collection(observed,
            notification => Assert.Same(first, notification),
            notification => Assert.Same(second, notification));
    }

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
