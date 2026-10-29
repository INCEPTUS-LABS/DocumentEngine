using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(false, "BPMN.Task")]
    [InlineData(true, "BPMN.Task")]
    [InlineData(false, "BPMN.StartEvent")]
    [InlineData(true, "BPMN.StartEvent")]
    public async Task PlacementPreviewCoalescesIntermediateUiWithoutChangingLiveReadiness(bool smartEdit, string type)
    {
        await using var test = await PanComponentFixture.CreateAsync(document: A22PoolSnapshot());
        test.Host.StateChanged += _ => RefreshSiteToolbarAsync(test);
        await test.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            test.Host.SetSmartEditEnabled(smartEdit);
            var tool = new ToolboxCatalog(BpmnPluginRegistration.N100.ToolboxContributions)
                .Items.Single(item => item.ElementTypeId?.Value == type);
            Assert.True(test.Selection.Select(tool.ItemId));
            await test.Host.RefreshToolboxPlacementAsync();
        });
        await test.DrainAsync();
        var session = Session(test.Host);
        var initial = session.CaptureState();
        var document = AttachedDocument(session).CaptureSnapshot();
        var actionBar = ActionBarMarkup(await test.MarkupAsync());
        var siteBar = SiteToolbarMarkup(await test.MarkupAsync());
        var notifications = new ConcurrentQueue<EditingSessionState>();
        test.Host.StateChanged += _ =>
        {
            notifications.Enqueue(session.CaptureState());
            return Task.CompletedTask;
        };
        await A24DuringPausedSceneAsync(test.Host,
            () => test.Renderer.Dispatcher.InvokeAsync(() => PointerObserver(test.Host)
                .MoveDocumentPointAsync(initial.CurrentScene!, new PointD(750, 450))),
            async () =>
            {
                await test.DrainAsync();
                Assert.Equal(EditingSessionStatus.Rebuilding, session.CaptureState().Status);
                Assert.Empty(notifications);
                Assert.Equal(actionBar, ActionBarMarkup(await test.MarkupAsync()));
                Assert.Equal(siteBar, SiteToolbarMarkup(await test.MarkupAsync()));
                // An unrelated parent render must also retain visual availability, while
                // operation handlers continue to reject work during the real rebuild.
                typeof(DocumentCanvas).GetField("_state", PropertiesUxFlags)!
                    .SetValue(test.Activator.Canvas, test.Host.CaptureState());
                Assert.True(ActionBarEnabled(ActionBarSnapshot(test.Activator.Canvas), "CanZoomIn"));
                Assert.False(ActionBarExecutionAllowed(test.Activator.Canvas, "CanZoomIn"));
                await RefreshSiteToolbarAsync(test);
                Assert.Equal(siteBar, SiteToolbarMarkup(await test.MarkupAsync()));
            });
        await test.DrainAsync();
        Assert.NotEmpty(notifications);
        Assert.All(notifications, state => Assert.True(state.IsCurrentScenePresented));
        notifications.Clear();
        var uploads = test.Execution.FullUploadCount;
        foreach (var point in new[] { new PointD(780, 470), new PointD(800, 490), new PointD(820, 510) })
            await test.Renderer.Dispatcher.InvokeAsync(() => PointerObserver(test.Host)
                .MoveDocumentPointAsync(session.CaptureState().CurrentScene!, point));
        await test.DrainAsync();
        Assert.All(notifications, state => Assert.True(state.IsCurrentScenePresented));
        Assert.Equal(uploads, test.Execution.FullUploadCount);
        Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(initial.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(initial.LayoutResult, session.CaptureState().LayoutResult);
        Assert.Equal(initial.RoutingResult, session.CaptureState().RoutingResult);
        Assert.Equal(actionBar, ActionBarMarkup(await test.MarkupAsync()));
        Assert.Equal(siteBar, SiteToolbarMarkup(await test.MarkupAsync()));
    }

    [Fact]
    public async Task PlacementPreviewPresentationFailureStillNotifiesUiAndRestoresCursor()
    {
        await using var test = await PanComponentFixture.CreateAsync();
        await ArmPlacementTask(test.Host, test.Selection);
        var session = Session(test.Host);
        var notifications = new ConcurrentQueue<EditingSessionState>();
        test.Host.StateChanged += _ =>
        {
            notifications.Enqueue(session.CaptureState());
            return Task.CompletedTask;
        };
        test.Execution.RenderResult = new Canvas2DInteropOperationResult
        {
            Succeeded = false,
            Code = "CANVAS2D_RENDERER_RENDERING_FAILED",
            SourceIdentity = "preview",
        };
        await test.Renderer.Dispatcher.InvokeAsync(() => PointerObserver(test.Host)
            .MoveDocumentPointAsync(session.CaptureState().CurrentScene!, new PointD(750, 450)));
        await test.DrainAsync();
        Assert.Contains(notifications, state => !state.PresentationDiagnostics.IsEmpty);
        Assert.False(session.CaptureState().IsCurrentScenePresented);
        Assert.Equal("crosshair", test.Host.CaptureState().CssCursor);
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }
}
