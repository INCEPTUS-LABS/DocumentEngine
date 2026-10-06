using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("en-GB")]
    [InlineData("pl-PL")]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("es-ES")]
    public async Task ContextMenuLifetimePointerMoveKeepsRenderedPropertiesAndDelete(string culture)
    {
        using var cultureScope = new ModelerCultureScope(culture);
        var events = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(events);
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000, 700, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("menu-lifetime", "container");
        var session = Session(host);
        var document = host.CaptureDocumentSnapshot().Snapshot;
        var history = session.CaptureState().HistoryStatus;
        var scene = session.CaptureState().CurrentScene!;
        var node = scene.Items.Last(item => item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId);
        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, Center(node.Bounds));
        var menu = Assert.IsType<DocumentCanvasContextMenuState>(host.CaptureState().ContextMenu);
        var activator = new PublishComponentActivator(host);
        using var services = PublishComponentServices(activator, new PublishDownloadRuntime());
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(
            () => renderer.RenderComponentAsync<DocumentCanvas>());
        var before = ContextMenuButtons(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        Assert.Equal(2, before.Length);
        Assert.Contains("-properties-action\"", before[0], StringComparison.Ordinal);
        Assert.Contains("-delete-element-action\"", before[1], StringComparison.Ordinal);

        var observations = new ConcurrentQueue<(EditingSessionStatus Status, string[] Buttons)>();
        void Observe(object? sender, EditingSessionStateChangedEventArgs args)
        {
            // Render at the real notification boundary, including transient Scene rebuilds.
            // No artificial delay or model mutation is needed to expose the flicker.
            var buttons = renderer.Dispatcher.InvokeAsync(async () =>
            {
                typeof(DocumentCanvas).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(activator.Component, host.CaptureState());
                await activator.Component.RefreshAsync();
                return ContextMenuButtons(rendered.ToHtmlString());
            }).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            observations.Enqueue((args.State.Status, buttons));
        }
        session.StateChanged += Observe;
        try
        {
            var other = scene.Items.Last(item => item.Layer == Canvas2DSceneLayer.Content &&
                item.Origin.VisualStateId == BpmnDemoPipeline.ApprovedTaskVisualId);
            var connector = scene.Items.First(item => item.Layer == Canvas2DSceneLayer.Connector &&
                item.Origin.VisualStateId == BpmnDemoPipeline.ThirdSequenceFlowVisualId &&
                item.Geometry.Kind == Canvas2DSceneGeometryKind.Path && !IsTargetArrow(item));
            // Menu coordinates also exercise rerenders from input that could arrive while
            // crossing the popup. Real DOM event isolation is covered in browser acceptance.
            PointD[] positions = [menu.CssPosition + new VectorD(30, 15),
                menu.CssPosition + new VectorD(30, 55), menu.CssPosition + new VectorD(30, 36),
                menu.CssPosition + new VectorD(260, 100),
                scene.ViewportTransform.TransformPoint(Center(node.Bounds)),
                scene.ViewportTransform.TransformPoint(Center(other.Bounds)),
                scene.ViewportTransform.TransformPoint(Center(connector.Bounds)), new(850, 650)];
            foreach (var point in positions)
            {
                await Task.Run(() => PointerObserver(host).MoveDocumentPointAsync(scene,
                    Canvas2DRenderer.ConvertCssToDocument(scene, point)))
                    .WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Same(menu, host.CaptureState().ContextMenu);
            }
            await session.WaitForIdleAsync();
        }
        finally
        {
            session.StateChanged -= Observe;
        }

        Assert.Same(menu, host.CaptureState().ContextMenu);
        Assert.NotEmpty(observations);
        Assert.All(observations, observation => Assert.True(before.SequenceEqual(observation.Buttons),
            $"{observation.Status}: expected {string.Join(" | ", before)}; actual {string.Join(" | ", observation.Buttons)}"));
        Assert.Contains(observations, observation => observation.Status == EditingSessionStatus.Rebuilding);
        Assert.All(before, button => Assert.DoesNotContain("disabled", button, StringComparison.Ordinal));
        Assert.NotEqual(session.CaptureState().EditorState.HoveredObjectId, menu.TargetSceneObjectId);
        host.CloseContextMenu();
        Assert.Null(host.CaptureState().ContextMenu);
        Assert.False(host.CanOpenContextProperties());
        Assert.Same(document, host.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(ModelerChanges(events));
    }

    [Fact]
    public async Task ContextMenuLifetimeCloseReopenAndResizeKeepCapturedApplicabilityAndTarget()
    {
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000, 700, 1));
        await using var host = CreateHost(new RecordingRenderExecution(), surface,
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("menu-reopen", "container");
        var scene = Session(host).CaptureState().CurrentScene!;
        var node = scene.Items.Last(item => item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId);
        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, Center(node.Bounds));
        var first = Assert.IsType<DocumentCanvasContextMenuState>(host.CaptureState().ContextMenu);
        Assert.True(first.PropertiesAvailable);
        await surface.RaiseAsync(new Canvas2DSurfaceSize(950, 650, 1));
        var resized = Assert.IsType<DocumentCanvasContextMenuState>(host.CaptureState().ContextMenu);
        Assert.Equal(first with { CssPosition = resized.CssPosition }, resized);
        host.CloseContextMenu();
        Assert.Null(host.CaptureState().ContextMenu);
        var other = scene.Items.Last(item => item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == BpmnDemoPipeline.ApprovedTaskVisualId);
        await PointerObserver(host).ContextMenuDocumentPointAsync(Session(host).CaptureState().CurrentScene!, Center(other.Bounds));
        var reopened = Assert.IsType<DocumentCanvasContextMenuState>(host.CaptureState().ContextMenu);
        Assert.NotSame(first, reopened);
        Assert.Equal(BpmnDemoPipeline.ApprovedTaskVisualId, reopened.TargetVisualStateId);
        Assert.NotEqual(first.DeletionAction, reopened.DeletionAction);
        var properties = await host.OpenPropertiesAsync(reopened.TargetVisualStateId!);
        Assert.Equal(reopened.TargetVisualStateId, properties?.VisualStateId);
        Assert.Null(host.CaptureState().ContextMenu);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextMenuLifetimeAuthoritativeChangeInvalidatesCapturedActions(bool replaceSession)
    {
        var events = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(events);
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000, 700, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory,
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("menu-stale", "standby", "container");
        var session = Session(host);
        var scene = session.CaptureState().CurrentScene!;
        var node = scene.Items.Last(item => item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId);
        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, Center(node.Bounds));
        var menu = Assert.IsType<DocumentCanvasContextMenuState>(host.CaptureState().ContextMenu);
        var snapshot = host.CaptureDocumentSnapshot().Snapshot!;
        if (replaceSession)
        {
            Assert.True((await host.LoadDocumentAsync(snapshot)).Succeeded);
            Assert.NotSame(session, Session(host));
            Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
        }
        else
        {
            var other = snapshot.VisualModel.VisualStates.Single(visual => visual.Id == BpmnDemoPipeline.ApprovedTaskVisualId);
            Assert.True((await session.ExecuteAsync(new MoveVisualStateCommand(snapshot.DocumentId, snapshot.Revision,
                other.Id, other.Position + new VectorD(10, 0), VisualPlacementMode.Pinned))).IsCommitted);
            await CommandProcessor.WaitForEventDispatchIdleAsync(AttachedDocument(session))
                .WaitAsync(TimeSpan.FromSeconds(10));
            await session.WaitForIdleAsync();
        }
        Assert.Null(host.CaptureState().ContextMenu);
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        var current = host.CaptureDocumentSnapshot().Snapshot;
        var history = Session(host).CaptureState().HistoryStatus;
        var changeCount = ModelerChanges(events).Length;
        Assert.Null(await host.ExecuteDeletionContextActionAsync());
        if (!replaceSession)
        {
            // Simulate an already queued stale click reaching the existing execution guard.
            typeof(DocumentCanvasHost).GetField("_contextMenu", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(host, menu);
            Assert.Null(await host.ExecuteDeletionContextActionAsync());
            Assert.Null(host.CaptureState().ContextMenu);
        }
        Assert.Same(current, host.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(history, Session(host).CaptureState().HistoryStatus);
        Assert.True(current!.VisualModel.TryGetVisualState(BpmnDemoPipeline.TaskVisualId, out _));
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(changeCount, ModelerChanges(events).Length);
        Assert.Empty(host.CaptureState().InteractionDiagnostics);
    }

    [Fact]
    public async Task ContextMenuLifetimeManualRouteActionsKeepExactSegmentAndPointAcrossHover()
    {
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000, 700, 1)),
            compositionFactory: BpmnModelerTestComposition.DemoFactory);
        await host.InitializeAsync("menu-route", "container");
        var session = Session(host);
        var snapshot = host.CaptureDocumentSnapshot().Snapshot!;
        var id = BpmnDemoPipeline.ThirdSequenceFlowVisualId;
        Assert.True((await session.ExecuteAsync(new SetConnectorRoutingTypeCommand(snapshot.DocumentId,
            snapshot.Revision, id, ConnectorRoutingType.Manual))).IsCommitted);
        await session.WaitForIdleAsync();
        var beforeAdd = SavedRoute();
        await OpenSequenceFlowMenu(host, id, labelOrigin: false);
        var addedPoint = await CheckAndExecute(Canvas2DConnectorRouteContextActionKind.AddPoint);
        var withPoint = SavedRoute();
        Assert.Contains(addedPoint, withPoint.ManualDefinition!.Value);
        await PointerObserver(host).ContextMenuDocumentPointAsync(session.CaptureState().CurrentScene!, addedPoint);
        await CheckAndExecute(Canvas2DConnectorRouteContextActionKind.DeletePoint);
        Assert.Equal(beforeAdd, SavedRoute());
        await host.UndoAsync();
        Assert.Equal(withPoint, SavedRoute());

        ConnectorRoutingRecord SavedRoute() => host.CaptureDocumentSnapshot().Snapshot!.VisualModel.RoutingScopes!.Value
            .SelectMany(scope => scope.Connectors).Single(saved => saved.VisualStateId == id);

        async Task<PointD> CheckAndExecute(Canvas2DConnectorRouteContextActionKind kind)
        {
            var menu = Assert.IsType<DocumentCanvasContextMenuState>(host.CaptureState().ContextMenu);
            var action = Assert.IsType<Canvas2DConnectorRouteContextAction>(menu.ConnectorRouteAction);
            Assert.Equal(kind, action.Kind);
            Assert.Equal(id, action.TargetVisualStateId);
            var before = host.CaptureDocumentSnapshot().Snapshot!;
            var history = session.CaptureState().HistoryStatus;
            await PointerObserver(host).MoveDocumentPointAsync(session.CaptureState().CurrentScene!, new PointD(800, 600));
            Assert.Same(menu, host.CaptureState().ContextMenu);
            Assert.Same(action, host.CaptureState().ContextMenu!.ConnectorRouteAction);
            Assert.Equal(history, session.CaptureState().HistoryStatus);
            Assert.Same(before, host.CaptureDocumentSnapshot().Snapshot);
            Assert.True((await host.ExecuteConnectorRouteContextActionAsync())?.IsCommitted);
            Assert.Equal(history.EntryCount + 1, session.CaptureState().HistoryStatus.EntryCount);
            Assert.Equal(before.Revision.Increment(), host.CaptureDocumentSnapshot().Snapshot!.Revision);
            Assert.Null(host.CaptureState().ContextMenu);
            return action.RoutePoint;
        }
    }

    private static string[] ContextMenuButtons(string markup) => Regex.Matches(markup,
        "<button[^>]*role=\"menuitem\"[^>]*>.*?</button>", RegexOptions.Singleline,
        TimeSpan.FromSeconds(1)).Select(match => WebUtility.HtmlDecode(match.Value)).ToArray();
}
