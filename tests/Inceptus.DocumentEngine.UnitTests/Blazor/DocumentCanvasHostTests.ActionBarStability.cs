using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    private static readonly string[] ActionBarRejectedMethods = ["UndoAsync", "RedoAsync", "ZoomInAsync", "AutoArrangeAsync"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActionBarRetainsButtonAttributesDuringPausedHoverRebuild(bool smartEdit)
    {
        await using var test = await PanComponentFixture.CreateAsync(document: A24Snapshot());
        await test.Renderer.Dispatcher.InvokeAsync(() => test.Host.SetSmartEditEnabled(smartEdit));
        var session = Session(test.Host);
        await test.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            var document = AttachedDocument(session);
            for (var i = 0; i < 2; i++)
            {
                Assert.True((await session.ExecuteAsync(new UpdateSemanticElementNameCommand(document.DocumentId,
                    document.Revision, new("source"), BpmnSemanticProperties.Name, "Name " + i))).IsCommitted);
                await session.WaitForIdleAsync();
            }
            await test.Host.UndoAsync();
        });
        await test.DrainAsync();
        var before = ActionBarMarkup(await test.MarkupAsync());
        var scene = session.CaptureState().CurrentScene;
        var presentation = ActionBarSnapshot(test.Activator.Canvas);
        Assert.True(ActionBarEnabled(presentation, "CanUndo"));
        Assert.True(ActionBarEnabled(presentation, "CanRedo"));
        await A24DuringPausedSceneAsync(test.Host,
            () => test.Renderer.Dispatcher.InvokeAsync(() =>
                InteractionController(test.Host).PointerMovedAsync(scene!.ViewportTransform.TransformPoint(new PointD(180, 290))).AsTask()),
            async () =>
            {
                await test.DrainAsync();
                Assert.Equal(EditingSessionStatus.Rebuilding, session.CaptureState().Status);
                Assert.Null(session.CaptureState().CurrentScene);
                Assert.Equal(before, ActionBarMarkup(await test.MarkupAsync()));
                Assert.Same(presentation, ActionBarSnapshot(test.Activator.Canvas));
                // This is the pre-correction binding: Ready-only predicates still
                // become false, but must no longer determine visual availability.
                Assert.False(ActionBarExecutionAllowed(test.Activator.Canvas, "CanCreateNewDiagram"));
                Assert.False(ActionBarExecutionAllowed(test.Activator.Canvas, "CanZoomIn"));
                Assert.False(ActionBarExecutionAllowed(test.Activator.Canvas, "CanAutoArrange"));
                var document = AttachedDocument(session).CaptureSnapshot();
                var history = session.CaptureState().HistoryStatus;
                await test.Renderer.Dispatcher.InvokeAsync(async () =>
                {
                    InvokePublishComponent(test.Activator.Canvas, "OpenNewDiagramConfirmation");
                    Assert.False((bool)typeof(DocumentCanvas).GetField("_newDiagramConfirmationOpen", PropertiesUxFlags)!
                        .GetValue(test.Activator.Canvas)!);
                    foreach (var method in ActionBarRejectedMethods)
                        await (Task)typeof(DocumentCanvas).GetMethod(method, PropertiesUxFlags)!.Invoke(test.Activator.Canvas, null)!;
                });
                Assert.Null(await test.Host.AutoArrangeAsync());
                Assert.False((await test.Host.NewDiagramAsync()).Succeeded);
                Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
                Assert.Equal(history, session.CaptureState().HistoryStatus);
            });
        await test.DrainAsync();
        Assert.NotSame(scene, session.CaptureState().CurrentScene);
        Assert.Equal(before, ActionBarMarkup(await test.MarkupAsync()));
        Assert.Same(presentation, ActionBarSnapshot(test.Activator.Canvas));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActionBarStaysStableAcrossElementPoolLaneResizeHoverAndLeave(bool smartEdit)
    {
        await using var test = await PanComponentFixture.CreateAsync(document: A22PoolSnapshot());
        await test.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            test.Host.SetSmartEditEnabled(smartEdit);
            await A24Select(test.Host, new VisualStateId("sales-task-visual"));
        });
        await test.DrainAsync();
        var session = Session(test.Host);
        var scene = session.CaptureState().CurrentScene!;
        var body = A24PanelBody(scene, new("sales-task-visual"));
        var region = scene.GetVisualRegion(new("sales-task-visual"))!;
        var before = ActionBarMarkup(await test.MarkupAsync());
        var presentation = ActionBarSnapshot(test.Activator.Canvas);
        var rebuilds = 0;
        Exception? failure = null;
        session.StateChanged += (_, args) =>
        {
            if (args.State.Status != EditingSessionStatus.Rebuilding) return;
            rebuilds++;
            failure ??= Record.Exception(() => Assert.Same(presentation, ActionBarSnapshot(test.Activator.Canvas)));
        };
        var points = new[] { Center(body.Bounds), new PointD(body.Bounds.Right + 6, body.Bounds.Top + 20),
            new PointD(region.Bounds.Right - 6, region.Bounds.Top + 20),
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
            Assert.Equal(before, ActionBarMarkup(await test.MarkupAsync()));
            Assert.Same(presentation, ActionBarSnapshot(test.Activator.Canvas));
        }
        await test.Renderer.Dispatcher.InvokeAsync(() => PointerObserver(test.Host).LeaveAsync());
        await test.DrainAsync();
        Assert.Equal(before, ActionBarMarkup(await test.MarkupAsync()));
        Assert.True(rebuilds > 0);
        Assert.Null(failure);
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("document")]
    [InlineData("revision")]
    [InlineData("history")]
    [InlineData("zoom")]
    [InlineData("scope")]
    [InlineData("profile")]
    [InlineData("profile-visibility")]
    [InlineData("profile-collapse")]
    [InlineData("surface")]
    [InlineData("form")]
    [InlineData("model-view-form")]
    [InlineData("applying")]
    [InlineData("dirty")]
    [InlineData("confirmation")]
    [InlineData("replacement")]
    [InlineData("gesture")]
    [InlineData("fault")]
    [InlineData("closed")]
    [InlineData("disposed")]
    public async Task ActionBarDoesNotRetainAvailabilityAcrossRelevantChangesDuringRebuild(string change)
    {
        await using var test = await PanComponentFixture.CreateAsync(document: A24Snapshot());
        var component = test.Activator.Canvas;
        var initial = test.Host.CaptureState();
        var session = initial.Session!;
        Assert.True(ActionBarEnabled(ActionBarSnapshot(component), "CanCreateNewDiagram"));
        var state = initial with
        {
            Session = ActionBarSession(session,
            change == "fault" ? EditingSessionStatus.RuntimeFaulted : EditingSessionStatus.Rebuilding,
            editor: change switch
            {
                "zoom" => new(viewport: new(4, session.EditorState.Viewport.Pan)),
                "gesture" => new(viewport: session.EditorState.Viewport,
                    activeGesture: new("drag", "move", new(0, 0), new(10, 10))),
                _ => null
            },
            history: change == "history" ? new(1, true, false) : null,
            documentId: change == "document" ? new("replacement") : null,
            revision: change == "revision" ? session.DocumentRevision.Increment() : null,
            context: change == "scope" ? new ProcessScopePresentationContext(new("other-scope")) : null,
            profiles: change == "profile" ? new([new ModelProfileId("changed-profile")]) : null,
            profileViews: change == "profile-visibility" ? new([new ModelProfileId("hidden-profile")]) : null,
            elementViews: change == "profile-collapse" ? new([new(new("profile"), new("pool"))]) : null,
            closed: change == "closed")
        };
        state = change switch
        {
            "session" => state with { DocumentSessionVersion = state.DocumentSessionVersion + 1 },
            "surface" => state with { SurfaceSize = null },
            "form" => state with { PropertiesFormOpen = true },
            "model-view-form" => state with { ModelViewPropertiesFormOpen = true },
            "applying" => state with { PropertiesApplyInFlight = true },
            "dirty" => state with { PropertiesFormDirty = true },
            "disposed" => state with { IsDisposed = true },
            _ => state
        };
        await test.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            if (change is "confirmation" or "replacement")
                typeof(DocumentCanvas).GetField(change == "confirmation" ? "_newDiagramConfirmationOpen" : "_newDiagramInProgress",
                    PropertiesUxFlags)!.SetValue(component, true);
            typeof(DocumentCanvas).GetField("_state", PropertiesUxFlags)!.SetValue(component, state);
            await component.RefreshAsync();
        });
        Assert.False(ActionBarEnabled(ActionBarSnapshot(component), "CanCreateNewDiagram"));
        Assert.False(ActionBarEnabled(ActionBarSnapshot(component), "CanAutoArrange"));
        // Returning to the old inputs while still rebuilding must not resurrect the cache.
        await test.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            typeof(DocumentCanvas).GetField("_state", PropertiesUxFlags)!
                .SetValue(component, initial with { Session = ActionBarSession(session, EditingSessionStatus.Rebuilding) });
            await component.RefreshAsync();
        });
        Assert.False(ActionBarEnabled(ActionBarSnapshot(component), "CanCreateNewDiagram"));
    }

    [Fact]
    public async Task ActionBarReflectsRealHistoryZoomCapabilityAndReplacementChanges()
    {
        await using var test = await PanComponentFixture.CreateAsync(document: A23Snapshot(A23Edges("linear")));
        var component = test.Activator.Canvas;
        Assert.False(ActionBarEnabled(ActionBarSnapshot(component), "CanUndo"));
        await test.Renderer.Dispatcher.InvokeAsync(() => test.Host.AutoArrangeAsync().AsTask());
        await test.DrainAsync();
        Assert.True(ActionBarEnabled(ActionBarSnapshot(component), "CanUndo"));
        Assert.False(ActionBarEnabled(ActionBarSnapshot(component), "CanRedo"));
        await test.Renderer.Dispatcher.InvokeAsync(() => test.Host.UndoAsync().AsTask());
        await test.DrainAsync();
        Assert.False(ActionBarEnabled(ActionBarSnapshot(component), "CanUndo"));
        Assert.True(ActionBarEnabled(ActionBarSnapshot(component), "CanRedo"));
        await test.Renderer.Dispatcher.InvokeAsync(() => test.Host.RedoAsync().AsTask());
        foreach (var zoom in new[] { .25, 1, 4 })
        {
            await test.Renderer.Dispatcher.InvokeAsync(() => Session(test.Host).UpdateViewportAsync(new(zoom, default)).AsTask());
            await test.DrainAsync();
            Assert.Equal(zoom > .25, ActionBarEnabled(ActionBarSnapshot(component), "CanZoomOut"));
            Assert.Equal(zoom < 4, ActionBarEnabled(ActionBarSnapshot(component), "CanZoomIn"));
            Assert.Equal(zoom != 1, ActionBarEnabled(ActionBarSnapshot(component), "CanSetZoomToActualSize"));
        }
        Assert.True(ActionBarEnabled(ActionBarSnapshot(component), "CanAutoArrange"));
        await test.Renderer.Dispatcher.InvokeAsync(async () => Assert.True((await test.Host.NewDiagramAsync()).Succeeded));
        await test.DrainAsync();
        Assert.False(ActionBarEnabled(ActionBarSnapshot(component), "CanAutoArrange"));
        Assert.False(ActionBarEnabled(ActionBarSnapshot(component), "CanUndo"));
        Assert.False(ActionBarEnabled(ActionBarSnapshot(component), "CanRedo"));
        await component.DisposeAsync();
        Assert.False(ActionBarEnabled(ActionBarSnapshot(component), "CanCreateNewDiagram"));
    }

    private static EditingSessionState ActionBarSession(EditingSessionState original, EditingSessionStatus status,
        EditorStateSnapshot? editor = null, HistoryStatus? history = null, DocumentId? documentId = null,
        DocumentRevision? revision = null, PresentationViewContext? context = null,
        ModelProfileStateSnapshot? profiles = null, ModelProfileViewStateSnapshot? profileViews = null,
        ModelProfileElementViewStateSnapshot? elementViews = null, bool closed = false) => new(
            documentId ?? original.DocumentId, revision ?? original.DocumentRevision,
            original.ActiveScopeId, status, original.Generation,
            status == EditingSessionStatus.Ready ? original.CurrentScene : null, null,
            status == EditingSessionStatus.Ready ? original.ProjectedGraph : null,
            status == EditingSessionStatus.Ready ? original.LayoutResult : null,
            status == EditingSessionStatus.Ready ? original.RoutingResult : null,
            editor ?? original.EditorState, history ?? original.HistoryStatus, [], [], closed,
            original.ModelProfileCatalog, profiles ?? original.ModelProfileState, profileViews ?? original.ModelProfileViewState,
            elementViews ?? original.ModelProfileElementViewState, presentationContext: context ?? original.PresentationContext);

    private static object ActionBarSnapshot(DocumentCanvas component) =>
        typeof(DocumentCanvas).GetField("_actionBarPresentation", PropertiesUxFlags)!.GetValue(component)!;

    private static bool ActionBarEnabled(object presentation, string property) =>
        (bool)presentation.GetType().GetProperty(property)!.GetValue(presentation)!;

    private static bool ActionBarExecutionAllowed(DocumentCanvas component, string property) =>
        (bool)typeof(DocumentCanvas).GetProperty(property, PropertiesUxFlags)!.GetValue(component)!;

    private static string ActionBarMarkup(string markup)
    {
        var match = Regex.Match(markup,
            "<div class=\"editor-toolbar\"[\\s\\S]*?(?=<div[^>]+class=\"document-canvas-surface\")");
        Assert.True(match.Success);
        return match.Value;
    }
}
