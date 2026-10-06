using System.Collections.Concurrent;
using System.Text.Json;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Xunit.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class ConnectorLabelHostWorkTests(ITestOutputHelper output)
{
    [Fact]
    public async Task HundredConnectorLabelInputsMeasureActualComponentAndNotificationWork() =>
        output.WriteLine(await DocumentCanvasHostTests.MeasureConnectorLabelHostAsync());
}

public sealed partial class DocumentCanvasHostTests
{
    internal static async Task<string> MeasureConnectorLabelHostAsync()
    {
        await using var test = await PanComponentFixture.CreateAsync();
        var session = Session(test.Host);
        var label = session.CaptureState().CurrentScene!.Items.First(item => item.Layer == Canvas2DSceneLayer.Label &&
            item.IsVisible && item.Metadata.ContainsKey(Canvas2DLabelGestureMetadata.LabelMoveCapable));
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [label.Origin.VisualStateId!]));
        var scene = session.CaptureState().CurrentScene!;
        label = scene.Items.Single(item => item.Id == label.Id);
        var start = Center(label.Bounds);
        var pointer = PointerObserver(test.Host);
        await test.Renderer.Dispatcher.InvokeAsync(() => pointer.MoveDocumentPointAsync(scene, start));
        await test.Renderer.Dispatcher.InvokeAsync(() => pointer.DownDocumentPointAsync(scene, start));
        await test.DrainAsync();
        Assert.Equal(Canvas2DLabelGestureMetadata.Kind, session.CaptureState().EditorState.ActiveGesture?.Kind);
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
                session.CaptureState().CurrentScene!, start + new VectorD(index, index * 0.4), buttons: 1));
            await test.DrainAsync();
        }
        await session.WaitForIdleAsync();
        await test.DrainAsync();
        Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.NotNull(session.CaptureState().EditorState.ActiveGesture);
        Assert.Equal(100, states.Count);
        Assert.Equal(100, hostNotifications);
        Assert.Equal(100, test.Activator.Canvas.BuildCount);
        Assert.Equal(100, test.Activator.Input.BuildCount);
        Assert.Equal(100, test.Execution.Calls.Count(call => call == "render") - frames);
        Assert.Equal(test.FullUploadsBefore, test.Execution.FullUploadCount);
        return JsonSerializer.Serialize(new
        {
            inputs = 100,
            sessionNotifications = states.Count,
            hostNotifications,
            componentBuilds = test.Activator.Canvas.BuildCount,
            toolboxBuilds = test.Activator.Toolbox.BuildCount,
            inputBuilds = test.Activator.Input.BuildCount,
            frames = test.Execution.Calls.Count(call => call == "render") - frames,
            fullUploads = test.Execution.FullUploadCount - test.FullUploadsBefore,
        });
    }
}
