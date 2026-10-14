using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    private static readonly string[] SiteToolbarOperations = ["ExportNativeDocumentAsync", "ExportStandardDocumentAsync", "ValidateAsync"];
    private static readonly string[] SiteToolbarEligibility = ["CanUseNativeDocumentTransfer", "CanPublish", "CanValidate"];
    private static readonly string[] SiteToolbarImports = ["ImportNativeDocumentAsync", "ImportStandardDocumentAsync"];

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SiteToolbarRetainsButtonAndFileInputAttributesDuringPausedHover(bool smartEdit, bool executeRetainedActions)
    {
        await using var test = await PanComponentFixture.CreateAsync(document: A24Snapshot());
        test.Host.StateChanged += _ => RefreshSiteToolbarAsync(test);
        await test.Renderer.Dispatcher.InvokeAsync(() => test.Host.SetSmartEditEnabled(smartEdit));
        if (!executeRetainedActions)
            await test.Renderer.Dispatcher.InvokeAsync(() => typeof(BpmnDemoHost).GetMethod("ToggleDocumentMenu", PropertiesUxFlags)!
                .Invoke(test.Activator.Demo, ["export"]));
        await RefreshSiteToolbarAsync(test);
        var before = SiteToolbarMarkup(await test.MarkupAsync());
        var session = Session(test.Host);
        var scene = session.CaptureState().CurrentScene!;
        var presentation = SiteToolbarSnapshot(test.Activator.Demo);
        await A24DuringPausedSceneAsync(test.Host,
            () => test.Renderer.Dispatcher.InvokeAsync(() => InteractionController(test.Host)
                .PointerMovedAsync(scene.ViewportTransform.TransformPoint(new PointD(180, 290))).AsTask()),
            async () =>
            {
                await test.DrainAsync();
                Assert.Equal(EditingSessionStatus.Rebuilding, session.CaptureState().Status);
                Assert.Null(session.CaptureState().CurrentScene);
                Assert.Equal(before, SiteToolbarMarkup(await test.MarkupAsync()));
                Assert.Same(presentation, SiteToolbarSnapshot(test.Activator.Demo));
                Assert.False(test.Host.CaptureEditorContext()!.HasOpenProperties);
                var component = test.Activator.Demo;
                var state = (DemoHostEditorState)typeof(BpmnDemoHost).GetField("_state", PropertiesUxFlags)!.GetValue(component)!;
                Assert.False(state.PropertiesFormOpen);
                if (!executeRetainedActions)
                {
                    Assert.Equal("export", typeof(BpmnDemoHost).GetField("_documentMenu", PropertiesUxFlags)!.GetValue(component));
                    return;
                }
                var document = AttachedDocument(session).CaptureSnapshot();
                await test.Renderer.Dispatcher.InvokeAsync(async () =>
                {
                    foreach (var property in SiteToolbarEligibility)
                        Assert.False((bool)typeof(BpmnDemoHost).GetProperty(property, PropertiesUxFlags)!.GetValue(component)!);
                    InvokePublishComponent(component, "OpenPublicationDialog");
                    Assert.False((bool)typeof(BpmnDemoHost).GetField("_publicationDialogOpen", PropertiesUxFlags)!.GetValue(component)!);
                    foreach (var method in SiteToolbarOperations) await InvokePublishComponentAsync(component, method);
                    foreach (var method in SiteToolbarImports)
                        await (Task)typeof(BpmnDemoHost).GetMethod(method, PropertiesUxFlags)!
                            .Invoke(component, [new InputFileChangeEventArgs([])])!;
                });
                Assert.Null(test.Host.Operations().ValidationSnapshot);
                Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
            });
        await test.DrainAsync();
        Assert.Equal(before, SiteToolbarMarkup(await test.MarkupAsync()));
        Assert.Same(presentation, SiteToolbarSnapshot(test.Activator.Demo));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SiteToolbarStableAcrossElementPoolLaneResizeHoverAndCanvasLeave(bool smartEdit)
    {
        await using var test = await PanComponentFixture.CreateAsync(document: A22PoolSnapshot());
        test.Host.StateChanged += _ => RefreshSiteToolbarAsync(test);
        await test.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            test.Host.SetSmartEditEnabled(smartEdit);
            await A24Select(test.Host, new VisualStateId("sales-task-visual"));
        });
        await RefreshSiteToolbarAsync(test);
        var session = Session(test.Host);
        var scene = session.CaptureState().CurrentScene!;
        var region = scene.GetVisualRegion(new("sales-task-visual"))!;
        var body = A24PanelBody(scene, new("sales-task-visual"));
        var before = SiteToolbarMarkup(await test.MarkupAsync());
        var presentation = SiteToolbarSnapshot(test.Activator.Demo);
        var points = new[] { Center(body.Bounds), new PointD(body.Bounds.Right, body.Bounds.Top + 20),
            new PointD(body.Bounds.Right + 6, body.Bounds.Top + 20),
            new PointD(region.Bounds.Right, region.Bounds.Top + 20),
            new PointD(region.Bounds.Right + 6, region.Bounds.Top + 20),
            new PointD(region.Bounds.Left + 20, region.Bounds.Bottom - 6),
            new PointD(region.Bounds.Left + 20, region.Bounds.Bottom),
            new PointD(region.Bounds.Left + 20, region.Bounds.Bottom + 6),
            Center(A24PanelBody(scene, new("warehouse-task-visual")).Bounds) };
        foreach (var point in points.Concat(points.Reverse()))
        {
            await test.Renderer.Dispatcher.InvokeAsync(() => PointerObserver(test.Host)
                .MoveDocumentPointAsync(session.CaptureState().CurrentScene!, point));
            await test.DrainAsync();
            Assert.Equal(before, SiteToolbarMarkup(await test.MarkupAsync()));
            Assert.Same(presentation, SiteToolbarSnapshot(test.Activator.Demo));
        }
        await test.Renderer.Dispatcher.InvokeAsync(() => PointerObserver(test.Host).LeaveAsync());
        await test.DrainAsync();
        Assert.Equal(before, SiteToolbarMarkup(await test.MarkupAsync()));
    }

    [Theory]
    [InlineData("form")]
    [InlineData("validation")]
    [InlineData("standard")]
    [InlineData("import")]
    [InlineData("export")]
    [InlineData("publish")]
    [InlineData("publication-dialog")]
    [InlineData("session")]
    [InlineData("document")]
    [InlineData("revision")]
    [InlineData("scope")]
    [InlineData("gesture")]
    [InlineData("fault")]
    [InlineData("closed")]
    public async Task SiteToolbarInvalidatesChangedInputsDuringRebuilding(string change)
    {
        await using var test = await PanComponentFixture.CreateAsync(document: A24Snapshot());
        await RefreshSiteToolbarAsync(test);
        var component = test.Activator.Demo;
        var original = (DemoHostEditorState)typeof(BpmnDemoHost).GetField("_state", PropertiesUxFlags)!.GetValue(component)!;
        Assert.True(SiteToolbarEnabled(component, "CanTransfer"));
        var session = original.Session!;
        var state = original with
        {
            Session = ActionBarSession(session,
            change == "fault" ? EditingSessionStatus.RuntimeFaulted : EditingSessionStatus.Rebuilding,
            documentId: change == "document" ? new("replacement") : null,
            revision: change == "revision" ? session.DocumentRevision.Increment() : null,
            context: change == "scope" ? new Inceptus.DocumentEngine.Contracts.Presentation.ProcessScopePresentationContext(new("other")) : null,
            editor: change == "gesture" ? new EditorStateSnapshot(activeGesture: new("move", "drag", new(0, 0), new(10, 10))) : null,
            closed: change == "closed")
        };
        state = change switch
        {
            "form" => state with { PropertiesFormOpen = true },
            "validation" => state with { ValidationInFlight = true },
            "session" => state with { DocumentSessionVersion = state.DocumentSessionVersion + 1 },
            _ => state
        };
        await test.Renderer.Dispatcher.InvokeAsync(() =>
        {
            var field = change switch
            {
                "standard" => "_standardInProgress",
                "import" => "_nativeDocumentImportInProgress",
                "export" => "_nativeDocumentExportInProgress",
                "publish" => "_publishInProgress",
                "publication-dialog" => "_publicationDialogOpen",
                _ => null
            };
            if (field is not null) typeof(BpmnDemoHost).GetField(field, PropertiesUxFlags)!.SetValue(component, true);
            typeof(BpmnDemoHost).GetField("_state", PropertiesUxFlags)!.SetValue(component, state);
            typeof(ComponentBase).GetMethod("StateHasChanged", PropertiesUxFlags)!.Invoke(component, null);
        });
        Assert.False(SiteToolbarEnabled(component, "CanTransfer"));
        Assert.False(SiteToolbarEnabled(component, "CanPublish"));
        Assert.False(SiteToolbarEnabled(component, "CanValidate"));
        await test.Renderer.Dispatcher.InvokeAsync(() =>
        {
            typeof(BpmnDemoHost).GetField("_state", PropertiesUxFlags)!.SetValue(component,
                original with { Session = ActionBarSession(session, EditingSessionStatus.Rebuilding) });
            typeof(ComponentBase).GetMethod("StateHasChanged", PropertiesUxFlags)!.Invoke(component, null);
        });
        Assert.False(SiteToolbarEnabled(component, "CanTransfer"));
    }

    [Fact]
    public async Task SiteToolbarInvalidatesOnEditorReattachmentAndDisposal()
    {
        await using var test = await PanComponentFixture.CreateAsync(document: A24Snapshot());
        await RefreshSiteToolbarAsync(test);
        var component = test.Activator.Demo;
        Assert.True(SiteToolbarEnabled(component, "CanTransfer"));
        component.AttachEditor(test.Host);
        Assert.False(SiteToolbarEnabled(component, "CanTransfer"));
        await RefreshSiteToolbarAsync(test);
        Assert.True(SiteToolbarEnabled(component, "CanTransfer"));
        await test.Renderer.DisposeAsync();
        Assert.False(SiteToolbarEnabled(component, "CanTransfer"));
        Assert.False(SiteToolbarEnabled(component, "CanValidate"));
    }

    private static object SiteToolbarSnapshot(BpmnDemoHost component) =>
        typeof(BpmnDemoHost).GetField("_toolbarPresentation", PropertiesUxFlags)!.GetValue(component)!;

    private static bool SiteToolbarEnabled(BpmnDemoHost component, string property) =>
        (bool)SiteToolbarSnapshot(component).GetType().GetProperty(property)!.GetValue(SiteToolbarSnapshot(component))!;

    private static Task RefreshSiteToolbarAsync(PanComponentFixture test) => test.Renderer.Dispatcher.InvokeAsync(() =>
    {
        // Invoke the same callback/render combination as the modeler's ViewChanged EventCallback.
        typeof(BpmnDemoHost).GetMethod("OnViewChanged", PropertiesUxFlags)!.Invoke(test.Activator.Demo, null);
        typeof(ComponentBase).GetMethod("StateHasChanged", PropertiesUxFlags)!.Invoke(test.Activator.Demo, null);
    });

    private static string SiteToolbarMarkup(string markup)
    {
        var match = Regex.Match(markup, "<div class=\"editor-toolbar host-toolbar\"[\\s\\S]*?(?=<canvas)");
        Assert.True(match.Success);
        return match.Value;
    }
}
