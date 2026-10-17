using System.Net;
using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task A24OpenSubProcessUsesExistingNavigationHistoryAndBreadcrumbWithoutDocumentEdits()
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("smart-open", "container");
        await A24Select(host, new VisualStateId("v-source"));
        Assert.Null(host.CaptureState().ElementToolbox!.OpenSubProcessScopeId);
        Assert.True((await host.SmartCreateAsync(new("bpmn:toolbox:sub-process")))!.IsCommitted);
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var request = host.CaptureState().ElementToolbox!;
        Assert.NotNull(request.OpenSubProcessScopeId);
        Assert.Equal(162, request.ActionsWidth);
        var clicked = false;
        using var culture = new ModelerCultureScope("en");
        using var services = PublishComponentServices(new PublishComponentActivator(host), new PublishDownloadRuntime());
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var callback = EventCallback.Factory.Create<ElementToolboxState>(new object(), async state =>
        {
            clicked = true;
            Assert.True((await host.OpenToolboxSubProcessAsync(state))?.Succeeded);
        });
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ElementToolbox>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(ElementToolbox.State)] = request, [nameof(ElementToolbox.OpenSubProcess)] = callback })));
        var markup = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        var actionGroup = markup[..markup.IndexOf("element-creation", StringComparison.Ordinal)];
        Assert.Equal(["Properties", "Open SubProcess", "Auto Points", "Delete"],
            Regex.Matches(actionGroup, "<button[^>]*aria-label=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        await callback.InvokeAsync(request);
        Assert.True(clicked);
        var nested = host.CaptureState();
        Assert.Equal(request.OpenSubProcessScopeId, nested.Session!.ActiveScopeId);
        Assert.Equal(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(history.EntryCount + 1, nested.Session.HistoryStatus.EntryCount);
        Assert.Null(host.Operations().ValidationSnapshot);
        Assert.Null(nested.ElementToolbox);
        Assert.Equal(before.SemanticModel.RootScopeId, nested.ScopeBreadcrumb[0].ScopeId);
        Assert.True((await host.NavigateToScopeAsync(nested.ScopeBreadcrumb[0].ScopeId))?.Succeeded);
        Assert.Equal(before.SemanticModel.RootScopeId, session.CaptureState().ActiveScopeId);
        Assert.Equal(history.EntryCount + 2, session.CaptureState().HistoryStatus.EntryCount);
        await host.UndoAsync();
        Assert.Equal(request.OpenSubProcessScopeId, session.CaptureState().ActiveScopeId);
        await host.RedoAsync();
        Assert.Equal(before.SemanticModel.RootScopeId, session.CaptureState().ActiveScopeId);
        Assert.Equal(before, AttachedDocument(session).CaptureSnapshot());
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("disabled")]
    [InlineData("dismissed")]
    [InlineData("revision")]
    [InlineData("replacement")]
    [InlineData("scope")]
    [InlineData("forged-target")]
    public async Task A24OpenSubProcessRejectsStaleOrUnavailableRequests(string change)
    {
        await using var host = CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(1920, 1080, 1)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: A24Snapshot()),
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        await host.InitializeAsync("smart-stale", "container");
        await A24Select(host, new VisualStateId("v-source"));
        Assert.True((await host.SmartCreateAsync(new("bpmn:toolbox:sub-process")))!.IsCommitted);
        var request = host.CaptureState().ElementToolbox!;
        if (change == "selection")
        {
            Assert.True((await host.SmartCreateAsync(new("bpmn:toolbox:sub-process")))!.IsCommitted);
            Assert.NotEqual(request.OpenSubProcessScopeId, host.CaptureState().ElementToolbox!.OpenSubProcessScopeId);
        }
        if (change == "disabled") host.SetSmartEditEnabled(false);
        if (change == "dismissed") host.DismissElementToolbox();
        if (change == "replacement") Assert.True((await host.NewDiagramAsync()).Succeeded);
        if (change == "scope") Assert.True((await host.NavigateToScopeAsync(request.OpenSubProcessScopeId!))?.Succeeded);
        if (change == "revision")
        {
            var session = Session(host);
            var document = AttachedDocument(session);
            Assert.True((await session.ExecuteAsync(new UpdateSemanticElementNameCommand(document.DocumentId,
                document.Revision, request.NavigationRequest!.Action.OwnerSemanticElementId, BpmnSemanticProperties.Name, "Changed"))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        if (change == "forged-target") request = request with
        {
            NavigationRequest = request.NavigationRequest! with
            { Action = request.NavigationRequest.Action with { TargetScopeId = new DocumentScopeId("unrelated") } }
        };
        var before = Session(host).CaptureState();
        var snapshot = AttachedDocument(Session(host)).CaptureSnapshot();
        Assert.Null(await host.OpenToolboxSubProcessAsync(request));
        Assert.Equal(before.ActiveScopeId, Session(host).CaptureState().ActiveScopeId);
        Assert.Equal(before.HistoryStatus, Session(host).CaptureState().HistoryStatus);
        Assert.Same(snapshot, AttachedDocument(Session(host)).CaptureSnapshot());
    }

    [Theory]
    [InlineData("BPMN.Task")]
    [InlineData("BPMN.StartEvent")]
    [InlineData("BPMN.ExclusiveGateway")]
    [InlineData("BPMN.SubProcess")]
    public async Task A24OpenSubProcessAbsentWithoutNavigableNestedScope(string type)
    {
        await using var host = A23Host(A24Snapshot(new SemanticTypeId(type)));
        await host.InitializeAsync("smart-ineligible", "container");
        await A24Select(host, new VisualStateId("v-source"));
        Assert.Null(host.CaptureState().ElementToolbox!.OpenSubProcessScopeId);
    }

    [Theory]
    [InlineData(320, 560, .3)]
    [InlineData(390, 640, 1)]
    [InlineData(1840, 830, 2)]
    public async Task A24FourButtonPanelClampsAtEveryViewportCornerAndStaysStable(double width, double height, double zoom)
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("smart-bounds", "container");
        await A24Select(host, new VisualStateId("v-source"));
        Assert.True((await host.SmartCreateAsync(new("bpmn:toolbox:sub-process")))!.IsCommitted);
        var session = Session(host);
        var request = host.CaptureState().ElementToolbox!;
        await session.ResizeAsync(new Canvas2DSurfaceSize(width, height, 1));
        // Host positioning consumes its surface observer, as normal browser resize does.
        typeof(DocumentCanvasHost).GetField("_surfaceSize", PropertiesUxFlags)!.SetValue(host, new Canvas2DSurfaceSize(width, height, 1));
        var visual = AttachedDocument(session).VisualModel.VisualStates.Single(item => item.Id == request.VisualId);
        foreach (var corner in new[] { new PointD(6, 6), new PointD(width - 30, 6), new PointD(6, height - 30), new PointD(width - 30, height - 30) })
        {
            await session.UpdateViewportAsync(new ViewportSnapshot(zoom,
                new(corner.X - visual.Position.X * zoom, corner.Y - visual.Position.Y * zoom)));
            await session.WaitForIdleAsync();
            var panel = host.CaptureState().ElementToolbox!;
            Assert.InRange(panel.ActionsPosition.X, 8, width - panel.ActionsWidth - 8);
            Assert.InRange(panel.ActionsPosition.Y, 8, height - 56);
            Assert.False(new RectD(panel.ActionsPosition.X, panel.ActionsPosition.Y, panel.ActionsWidth, 48).Intersects(panel.Anchor));
            Assert.Same(panel, host.CaptureState().ElementToolbox);
        }
        var stable = host.CaptureState().ElementToolbox!;
        await A24DuringPausedSceneAsync(host,
            () => InteractionController(host).PointerMovedAsync(stable.Anchor.TopLeft).AsTask(),
            async () =>
            {
                Assert.Same(stable, host.CaptureState().ElementToolbox);
                Assert.Null(await host.OpenToolboxSubProcessAsync(stable));
            });
        Assert.Same(stable, host.CaptureState().ElementToolbox);
    }
}
