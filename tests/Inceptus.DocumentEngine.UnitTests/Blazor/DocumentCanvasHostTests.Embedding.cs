using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Canvas2D.Rendering;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task EmbeddingHiddenStartupDefersReadyThenResizeAndDprPreserveDocument()
    {
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(640d, 420d, 1d))
        {
            InitialMeasurement = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, surface,
            compositionFactory: new BpmnModelerCompositionFactory());
        host.ModelerNotifications = notifications;

        var startup = host.InitializeAsync("active", "standby", "container").AsTask();
        Assert.False(startup.IsCompleted);
        Assert.Empty(log);
        Assert.Empty(execution.Calls);
        Assert.Null(host.CaptureState().Session);
        surface.InitialMeasurement.SetResult(new Canvas2DSurfaceSize(640d, 420d, 1d));
        await startup.WaitAsync(TimeSpan.FromSeconds(10));
        var ready = Assert.IsType<BpmnModelerReadyEventArgs>(Assert.Single(log));
        var session = Session(host);
        foreach (var size in new[]
        {
            new Canvas2DSurfaceSize(960d, 600d, 1d),
            new Canvas2DSurfaceSize(640d, 420d, 2d),
            new Canvas2DSurfaceSize(640d, 420d, 1.25d),
        })
        {
            await surface.RaiseAsync(size);
            Assert.Same(session, Session(host));
            Assert.Equal(ready.Snapshot, host.CaptureDocumentSnapshot().Snapshot);
            Assert.Single(log);
            Assert.True(host.CaptureState().LatestPresentationSucceeded);
        }
    }

    [Fact]
    public async Task EmbeddingDisposeDuringHiddenStartupSuppressesCallbacksAndLateReveal()
    {
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(640d, 420d, 1d))
        {
            InitialMeasurement = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, surface,
            compositionFactory: new BpmnModelerCompositionFactory());
        host.ModelerNotifications = notifications;
        var startup = host.InitializeAsync("active", "standby", "container").AsTask();
        await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        await startup.WaitAsync(TimeSpan.FromSeconds(10));
        surface.InitialMeasurement.SetResult(new Canvas2DSurfaceSize(640d, 420d, 1d));
        await surface.RaiseAsync(new Canvas2DSurfaceSize(960d, 600d, 2d));
        Assert.Empty(log);
        Assert.Null(host.CaptureState().Session);
        Assert.DoesNotContain("initialize:active", execution.Calls);
        Assert.Equal(1, surface.DisposeCount);
        Assert.Equal(1, execution.DisposeCount);
    }
}
