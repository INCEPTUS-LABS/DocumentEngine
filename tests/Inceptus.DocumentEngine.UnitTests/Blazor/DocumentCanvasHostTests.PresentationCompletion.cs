using System.Collections.Concurrent;
using System.Reflection;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("zoom-in", true)]
    [InlineData("zoom-in", false)]
    [InlineData("zoom-out", true)]
    [InlineData("zoom-out", false)]
    [InlineData("resize", true)]
    [InlineData("resize", false)]
    [InlineData("context-menu", true)]
    [InlineData("context-menu", false)]
    public async Task PresentationCompletionIsNotInferredFromRepeatedReadyNotifications(
        string action,
        bool renderSucceeds)
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var before = session.CaptureState();
        var document = host.CaptureDocumentSnapshot().Snapshot;
        Assert.True(before.IsCurrentScenePresented);
        execution.BlockRender = true;
        execution.RenderResult = new Canvas2DInteropOperationResult
        {
            Succeeded = renderSucceeds,
            Code = renderSucceeds ? null : "TEST_RENDER_FAILED",
        };
        var operation = Task.Run(async () =>
        {
            switch (action)
            {
                case "zoom-in":
                    await host.ZoomInAsync();
                    break;
                case "zoom-out":
                    await host.ZoomOutAsync();
                    break;
                case "resize":
                    await observer.RaiseAsync(new Canvas2DSurfaceSize(960d, 540d, 2d));
                    break;
                case "context-menu":
                    var body = before.CurrentScene!.Items.First(Canvas2DNodeBodyMetadata.IsNodeBody);
                    await PointerObserver(host).ContextMenuDocumentPointAsync(
                        before.CurrentScene, Center(body.Bounds));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }
        });
        try
        {
            await execution.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // State notifications are observations, not render-completion receipts.
            // Re-observe the actual state without changing the pipeline or its results.
            NotifyPresentationState(session);
            NotifyPresentationState(session);
            Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
            Assert.False(session.CaptureState().IsCurrentScenePresented);
            Assert.False(host.CaptureState().LatestPresentationSucceeded);
            Assert.Equal(1, host.CaptureState().SuccessfulRenderCount);
            Assert.False(operation.IsCompleted);
        }
        finally
        {
            execution.ReleaseRender();
            await operation.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await session.WaitForIdleAsync();
        NotifyPresentationState(session);
        NotifyPresentationState(session);
        Assert.Equal(renderSucceeds, session.CaptureState().IsCurrentScenePresented);
        Assert.Equal(renderSucceeds, host.CaptureState().LatestPresentationSucceeded);
        Assert.Equal(renderSucceeds ? 2 : 1, host.CaptureState().SuccessfulRenderCount);
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
        Assert.Same(document, host.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(before.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(1, execution.MaximumConcurrency);
    }

    [Fact]
    public async Task IndependentHostsDoNotSharePresentationCompletionOrCounters()
    {
        var executionA = new RecordingRenderExecution();
        var executionB = new RecordingRenderExecution();
        await using var hostA = CreateHost(executionA,
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d)));
        await using var hostB = CreateHost(executionB,
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d)));
        await hostA.InitializeAsync("canvas-a", "container-a");
        await hostB.InitializeAsync("canvas-b", "container-b");
        executionA.BlockRender = true;
        var operationA = hostA.ZoomInAsync().AsTask();
        try
        {
            await executionA.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await hostB.ZoomOutAsync();
            Assert.Equal(1, hostA.CaptureState().SuccessfulRenderCount);
            Assert.False(Session(hostA).CaptureState().IsCurrentScenePresented);
            Assert.Equal(2, hostB.CaptureState().SuccessfulRenderCount);
            Assert.True(Session(hostB).CaptureState().IsCurrentScenePresented);
        }
        finally
        {
            executionA.ReleaseRender();
            await operationA.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(2, hostA.CaptureState().SuccessfulRenderCount);
        Assert.Equal(2, hostB.CaptureState().SuccessfulRenderCount);
        Assert.NotSame(Counters(hostA), Counters(hostB));
    }

    private static void NotifyPresentationState(EditingSession session) =>
        typeof(EditingSession).GetMethod("NotifyStateChanged",
            BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(session, null);

    [Theory]
    [InlineData("zoom-in")]
    [InlineData("zoom-out")]
    [InlineData("context-menu")]
    public async Task DelayedSessionNotificationsDoNotLoseCompletedPresentation(string action)
    {
        var execution = new RecordingRenderExecution();
        var observer = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(720d, 480d, 1d));
        await using var host = CreateHost(execution, observer);
        await host.InitializeAsync("canvas", "container");
        var session = Session(host);
        var before = session.CaptureState();
        var document = host.CaptureDocumentSnapshot().Snapshot;
        var beforeCount = host.CaptureState().SuccessfulRenderCount;
        var notifications = new ConcurrentQueue<EditingSessionState>();
        session.StateChanged += (_, args) => notifications.Enqueue(args.State);
        var notificationGate = PresentationField(session, "_notificationGate");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var blockedObserver = Task.Factory.StartNew(() =>
        {
            // Model another observer occupying the existing notification gate. State
            // installation is independent of callback delivery; no scheduler delay is used.
            lock (notificationGate)
            {
                entered.TrySetResult();
                release.Wait();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task? operation = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            operation = Task.Run(async () =>
            {
                switch (action)
                {
                    case "zoom-in":
                        await host.ZoomInAsync();
                        break;
                    case "zoom-out":
                        await host.ZoomOutAsync();
                        break;
                    case "context-menu":
                        var body = before.CurrentScene!.Items.First(Canvas2DNodeBodyMetadata.IsNodeBody);
                        await PointerObserver(host).ContextMenuDocumentPointAsync(
                            before.CurrentScene, Center(body.Bounds));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(action));
                }
            });
            await WaitForPresentationSceneInstallationAsync(session, before.CurrentScene!);
            Assert.Empty(notifications);
            Assert.Equal(beforeCount, host.CaptureState().SuccessfulRenderCount);
        }
        finally
        {
            release.Set();
            await blockedObserver.WaitAsync(TimeSpan.FromSeconds(10));
            if (operation is not null)
            {
                await operation.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }

        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotEmpty(notifications);
        Assert.All(notifications, state => Assert.Equal(EditingSessionStatus.Ready, state.Status));
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));
        Assert.Same(document, host.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(before.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.True(host.CaptureState().LatestPresentationSucceeded);
        Assert.Equal(beforeCount + 1, host.CaptureState().SuccessfulRenderCount);
    }

    private static async Task WaitForPresentationSceneInstallationAsync(
        EditingSession session,
        Canvas2DScene precedingScene)
    {
        var sync = PresentationField(session, "_sync");
        while (true)
        {
            Task pulse;
            lock (sync)
            {
                var state = session.CaptureState();
                if (state.Status == EditingSessionStatus.Ready &&
                    !ReferenceEquals(precedingScene, state.CurrentScene))
                {
                    return;
                }

                pulse = ((TaskCompletionSource)PresentationField(session, "_statePulse")).Task;
            }

            // This is the session's real state-transition signal, not timer polling.
            await pulse.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static object PresentationField(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(owner)!;
}
