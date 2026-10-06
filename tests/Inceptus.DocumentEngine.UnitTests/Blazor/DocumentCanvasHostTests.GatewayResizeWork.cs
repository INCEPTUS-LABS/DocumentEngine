using System.Collections.Concurrent;
using System.Text.Json;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Xunit.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class GatewayResizeHostWorkTests(ITestOutputHelper output)
{
    [Fact]
    public async Task HundredResizeInputsMeasureActualComponentAndNotificationWork() =>
        output.WriteLine(await DocumentCanvasHostTests.MeasureGatewayResizeHostAsync());
}

public sealed partial class DocumentCanvasHostTests
{
    internal static async Task<string> MeasureGatewayResizeHostAsync()
    {
        await using var test = await PanComponentFixture.CreateAsync();
        var session = Session(test.Host);
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [BpmnDemoPipeline.ExclusiveGatewayVisualId]));
        var scene = session.CaptureState().CurrentScene!;
        var zone = scene.Items.Single(item => item.Origin.VisualStateId == BpmnDemoPipeline.ExclusiveGatewayVisualId &&
            item.Origin.StableSourceKey?.StartsWith("node-label-resize-zone:east:", StringComparison.Ordinal) == true);
        var start = Center(zone.Bounds);
        var pointer = PointerObserver(test.Host);
        await test.Renderer.Dispatcher.InvokeAsync(() => pointer.MoveDocumentPointAsync(scene, start));
        await test.Renderer.Dispatcher.InvokeAsync(() => pointer.DownDocumentPointAsync(scene, start));
        await test.DrainAsync();
        var document = AttachedDocument(session).CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var states = new ConcurrentQueue<EditingSessionState>();
        var hostNotifications = 0;
        session.StateChanged += (_, args) => states.Enqueue(args.State);
        test.Host.StateChanged += _ => { Interlocked.Increment(ref hostNotifications); return Task.CompletedTask; };
        var frames = test.Execution.Calls.Count(call => call == "render");
        test.ResetCounts();
        for (var index = 1; index <= 100; index++)
        {
            await test.Renderer.Dispatcher.InvokeAsync(() => pointer.MoveDocumentPointAsync(
                session.CaptureState().CurrentScene!, start + new VectorD(index, 0), buttons: 1));
            await test.DrainAsync();
        }
        await session.WaitForIdleAsync();
        await test.DrainAsync();
        Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.NotNull(session.CaptureState().EditorState.ActiveGesture);
        return JsonSerializer.Serialize(new
        {
            inputs = 100,
            sessionNotifications = states.Count,
            hostNotifications,
            componentBuilds = test.Activator.Canvas.BuildCount,
            toolboxBuilds = test.Activator.Toolbox.BuildCount,
            inputBuilds = test.Activator.Input.BuildCount,
            frames = test.Execution.Calls.Count(call => call == "render") - frames,
            fullUploads = test.Execution.FullUploadCount - test.FullUploadsBefore
        });
    }
}
