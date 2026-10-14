using System.Collections.Concurrent;
using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.UnitTests.Canvas2D;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task A24PanelsRetainExactPresentationWhileHoverRebuildHasNoCurrentScene()
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("stable-hover", "container");
        await A24Select(host, new VisualStateId("v-source"));
        var session = Session(host);
        var before = session.CaptureState();
        var document = AttachedDocument(session).CaptureSnapshot();
        var overlay = Assert.IsType<ElementToolboxState>(host.CaptureState().ElementToolbox);
        var evaluations = host.ElementCandidateEvaluationCount;
        await A24DuringPausedSceneAsync(host,
            () => InteractionController(host).PointerMovedAsync(new PointD(180, 290)).AsTask(),
            async () =>
            {
                Assert.Equal(EditingSessionStatus.Rebuilding, session.CaptureState().Status);
                Assert.Null(session.CaptureState().CurrentScene);
                Assert.Same(overlay, host.CaptureState().ElementToolbox);
                Assert.Null(await host.SmartCreateAsync(new("bpmn:toolbox:task")));
                Assert.Null(await host.AutoPointsAsync());
                Assert.Null(await host.DeleteToolboxElementAsync());
                Assert.Same(overlay, host.CaptureState().ElementToolbox);
                Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
                Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
            });
        Assert.NotSame(before.CurrentScene, session.CaptureState().CurrentScene);
        Assert.Same(overlay, host.CaptureState().ElementToolbox);
        Assert.Equal(evaluations, host.ElementCandidateEvaluationCount);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("select")]
    [InlineData("disable")]
    [InlineData("dismiss")]
    public async Task A24PanelsDoNotRetainInvalidSelectionOrExplicitlyHiddenPresentationDuringRebuild(string operation)
    {
        await using var host = A23Host(A23Snapshot(A23Edges("linear")));
        await host.InitializeAsync("stable-invalidation", "container");
        await A24Select(host, new VisualStateId("v-a"));
        var session = Session(host);
        var prior = session.CaptureState().EditorState;
        Assert.NotNull(host.CaptureState().ElementToolbox);
        await A24DuringPausedSceneAsync(host,
            () => operation is "clear" or "select"
                ? session.UpdateEditorStateAsync(new EditorStateSnapshot(
                    operation == "select" ? [new VisualStateId("v-b")] : [], viewport: prior.Viewport)).AsTask()
                : InteractionController(host).PointerMovedAsync(Center(A24PanelBody(session.CaptureState().CurrentScene!, new("v-a")).Bounds)).AsTask(),
            () =>
            {
                if (operation == "disable") host.SetSmartEditEnabled(false);
                if (operation == "dismiss") host.DismissElementToolbox();
                Assert.Null(host.CaptureState().ElementToolbox);
                return Task.CompletedTask;
            });
        if (operation == "select") Assert.Equal(new VisualStateId("v-b"), host.CaptureState().ElementToolbox!.VisualId);
        else Assert.Null(host.CaptureState().ElementToolbox);
    }

    [Fact]
    public async Task A24PanelsStayIdenticalAcrossElementPoolLaneResizeHoverAndCanvasLeaveBoundaries()
    {
        await using var host = A23Host(A22PoolSnapshot());
        await host.InitializeAsync("stable-boundaries", "container");
        await A24Select(host, new VisualStateId("sales-task-visual"));
        var session = Session(host);
        var scene = session.CaptureState().CurrentScene!;
        var overlay = Assert.IsType<ElementToolboxState>(host.CaptureState().ElementToolbox);
        var body = A24PanelBody(scene, overlay.VisualId);
        var region = scene.GetVisualRegion(overlay.VisualId)!;
        var other = A24PanelBody(scene, new("warehouse-task-visual"));
        var samples = new[] { Center(body.Bounds), new PointD(body.Bounds.Right + 6, body.Bounds.Top + 20),
            new PointD(region.Bounds.Right - 6, region.Bounds.Top + 20),
            new PointD(region.Bounds.Right, region.Bounds.Top + 20),
            new PointD(region.Bounds.Right + 6, region.Bounds.Top + 20),
            new PointD(region.Bounds.Left + 20, region.Bounds.Bottom - 6),
            new PointD(region.Bounds.Left + 20, region.Bounds.Bottom),
            new PointD(region.Bounds.Left + 20, region.Bounds.Bottom + 6), Center(other.Bounds) };
        var observations = new ConcurrentQueue<ElementToolboxState?>();
        session.StateChanged += (_, _) => observations.Enqueue(host.CaptureState().ElementToolbox);
        var before = AttachedDocument(session).CaptureSnapshot();
        foreach (var point in samples.Concat(samples.Reverse()))
        {
            await PointerObserver(host).MoveDocumentPointAsync(session.CaptureState().CurrentScene!, point);
            Assert.Same(overlay, host.CaptureState().ElementToolbox);
        }
        // Entering either HTML panel leaves the canvas; returning reacquires hover.
        await PointerObserver(host).LeaveAsync();
        await PointerObserver(host).MoveDocumentPointAsync(session.CaptureState().CurrentScene!, Center(body.Bounds));
        Assert.NotEmpty(observations);
        Assert.All(observations, captured => Assert.Same(overlay, captured));
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A24PanelsFollowActualMoveAndResizePreviewsWithoutAllowingGestureActions(bool resize, bool pooled)
    {
        await using var host = A23Host(pooled ? A22PoolSnapshot() : A24Snapshot());
        await host.InitializeAsync("stable-gesture", "container");
        var id = new VisualStateId(pooled ? "sales-task-visual" : "v-source");
        await A24Select(host, id);
        var session = Session(host);
        var original = host.CaptureState().ElementToolbox!;
        var document = AttachedDocument(session).CaptureSnapshot();
        var scene = session.CaptureState().CurrentScene!;
        var start = resize ? Center(ResizeZone(scene, "southeast").Bounds) : Center(A24PanelBody(scene, id).Bounds);
        var end = start + new VectorD(40, 30);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(scene, start, pointerId: 941);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, end, pointerId: 941, buttons: 1);
        var preview = Assert.IsType<ElementToolboxState>(host.CaptureState().ElementToolbox);
        Assert.NotNull(session.CaptureState().EditorState.ActiveGesture);
        Assert.Equal(resize ? original.Anchor.X : original.Anchor.X + 40, preview.Anchor.X, 6);
        Assert.Equal(resize ? original.Anchor.Y : original.Anchor.Y + 30, preview.Anchor.Y, 6);
        Assert.Equal(resize ? original.Anchor.Width + 40 : original.Anchor.Width, preview.Anchor.Width, 6);
        Assert.Equal(resize ? original.Anchor.Height + 30 : original.Anchor.Height, preview.Anchor.Height, 6);
        Assert.Same(document, AttachedDocument(session).CaptureSnapshot());
        Assert.Null(await host.DeleteToolboxElementAsync());
        Assert.Null(await host.SmartCreateAsync(new("bpmn:toolbox:task")));
        Assert.NotNull(session.CaptureState().EditorState.ActiveGesture);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, end, pointerId: 941);
        await session.WaitForIdleAsync();
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(preview.Anchor, host.CaptureState().ElementToolbox!.Anchor);
        await host.UndoAsync();
        Assert.Equal(original.Anchor, host.CaptureState().ElementToolbox!.Anchor);
    }

    [Fact]
    public async Task A24PanelsRepositionForViewportAndSurfaceAndRetainEquivalentPresentation()
    {
        var surface = new RecordingSurfaceObserver(new(1920, 1080, 1));
        await using var host = CreateHost(new RecordingRenderExecution(), surface,
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: A24Snapshot()));
        await host.InitializeAsync("stable-viewport", "container");
        await A24Select(host, new VisualStateId("v-source"));
        var session = Session(host);
        var original = host.CaptureState().ElementToolbox!;
        await session.PanViewportAsync(new(-40, -30));
        var panned = host.CaptureState().ElementToolbox!;
        Assert.Equal(original.Anchor.Translate(new(-40, -30)), panned.Anchor);
        await session.UpdateViewportAsync(new ViewportSnapshot(2, new(-40, -30)));
        var zoomed = host.CaptureState().ElementToolbox!;
        Assert.Equal(original.Anchor.Width * 2, zoomed.Anchor.Width);
        Assert.Equal(original.CreationWidth, zoomed.CreationWidth);
        await surface.RaiseAsync(new(360, 900, 1));
        var fitted = host.CaptureState().ElementToolbox!;
        Assert.Equal(344, fitted.CreationWidth);
        Assert.InRange(fitted.CreationPosition.X, 8, 8);
        await PointerObserver(host).MoveDocumentPointAsync(session.CaptureState().CurrentScene!, new(180, 290));
        Assert.Same(fitted, host.CaptureState().ElementToolbox);
    }

    [Fact]
    public async Task A24PanelsRefreshCapabilitiesWithoutSelectionChangeAndInvalidateDeletion()
    {
        await using var host = A23Host(A23Snapshot(A23Edges("linear")));
        await host.InitializeAsync("stable-capabilities", "container");
        await A24Select(host, new VisualStateId("v-a"));
        var session = Session(host);
        var document = AttachedDocument(session);
        var original = host.CaptureState().ElementToolbox!;
        Assert.True(original.CanAutoPoints);
        foreach (var record in document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords().ToArray())
        {
            Assert.True((await session.ExecuteAsync(new SetConnectorRoutingTypeCommand(document.DocumentId,
                document.Revision, record.VisualStateId, ConnectorRoutingType.Manual))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        var changed = host.CaptureState().ElementToolbox!;
        Assert.False(changed.CanAutoPoints);
        Assert.Equal(original.VisualId, changed.VisualId);
        Assert.Equal(original.Anchor, changed.Anchor);
        Assert.Equal(original.Candidates, changed.Candidates);
        Assert.True((await session.ExecuteAsync(new UpdateSemanticElementNameCommand(document.DocumentId, document.Revision,
            new("a"), BpmnSemanticProperties.Name, "Renamed"))).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Same(changed, host.CaptureState().ElementToolbox);
        Assert.True((await host.DeleteToolboxElementAsync())!.IsCommitted);
        Assert.Null(host.CaptureState().ElementToolbox);
        Assert.Null(await host.SmartCreateAsync(new("bpmn:toolbox:task")));
    }

    [Fact]
    public async Task A24PanelsUpdateCreationCapabilitiesForSameElementAndSameBounds()
    {
        await using var host = A23Host(A24Snapshot(BpmnSemanticTypes.ExclusiveGateway));
        await host.InitializeAsync("stable-types", "container");
        await A24Select(host, new VisualStateId("v-source"));
        var session = Session(host);
        var document = AttachedDocument(session);
        var original = host.CaptureState().ElementToolbox!;
        Assert.True((await session.ExecuteAsync(new ChangeBpmnElementTypeCommand(document.DocumentId, document.Revision,
            new("source"), BpmnSemanticTypes.EventBasedGateway))).IsCommitted);
        await session.WaitForIdleAsync();
        var changed = host.CaptureState().ElementToolbox!;
        Assert.Equal(original.VisualId, changed.VisualId);
        Assert.Equal(original.Anchor, changed.Anchor);
        Assert.Equal(original.ActionsPosition, changed.ActionsPosition);
        Assert.Equal(4, changed.Candidates.Length);
        Assert.NotEqual(original.Candidates, changed.Candidates);
        Assert.Same(changed, host.CaptureState().ElementToolbox);
    }

    [Fact]
    public async Task A24PanelsInvalidateDeletedElementBeforeReplacementSceneIsReady()
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("stable-delete", "container");
        await A24Select(host, new VisualStateId("v-source"));
        Assert.NotNull(host.CaptureState().ElementToolbox);
        await A24DuringPausedSceneAsync(host, () => host.DeleteToolboxElementAsync().AsTask(), () =>
        {
            Assert.Equal(EditingSessionStatus.Rebuilding, Session(host).CaptureState().Status);
            Assert.Null(Session(host).CaptureState().CurrentScene);
            Assert.Empty(AttachedDocument(Session(host)).CaptureSnapshot().VisualModel.VisualStates);
            Assert.Null(host.CaptureState().ElementToolbox);
            return Task.CompletedTask;
        });
        Assert.Null(host.CaptureState().ElementToolbox);
    }

    private static Canvas2DSceneItem A24PanelBody(Canvas2DScene scene, VisualStateId id) => scene.Items.First(item =>
        item.Origin.VisualStateId == id && item.Layer == Canvas2DSceneLayer.Content &&
        (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 && item.HitTestPolicy.Mode != Canvas2DHitTestMode.None);

    private static async Task A24DuringPausedSceneAsync(DocumentCanvasHost host, Func<Task> trigger, Func<Task> inspect)
    {
        var session = Session(host);
        var state = session.CaptureState();
        var document = AttachedDocument(session).CaptureSnapshot();
        var field = typeof(EditingSession).GetField("_pipeline", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var original = (ISessionPipelineProcessing)field.GetValue(session)!;
        var controlled = new ControlledEditingSessionPipeline();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        controlled.EnqueueScene(async (artifacts, _, editor, cancellation) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellation);
            return await original.RebuildSceneForTransientPresentationAsync(state.CurrentScene!, document, artifacts,
                editor, state.ModelProfileViewState, state.ModelProfileElementViewState, cancellation);
        });
        controlled.EnqueueScopedFull(async (currentDocument, scope, editor, cancellation) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellation);
            return await original.RunFullAsync(currentDocument, scope, editor,
                state.ModelProfileViewState, state.ModelProfileElementViewState, cancellation);
        });
        field.SetValue(session, controlled);
        Task? operation = null;
        try
        {
            operation = trigger();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await inspect();
        }
        finally
        {
            release.TrySetResult();
            try
            {
                if (operation is not null) await operation.WaitAsync(TimeSpan.FromSeconds(10));
                await session.WaitForIdleAsync();
            }
            finally { field.SetValue(session, original); }
        }
    }
}
