using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Visuals;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(.5, true, false)]
    [InlineData(1, true, true)]
    [InlineData(2, true, false)]
    [InlineData(1, false, true)]
    public async Task GridMovePreviewCommitHistoryAndNativeOpenUseSameGeometry(double zoom, bool snap, bool show)
    {
        var grid = new TestGridSource(new() { SnapToGrid = snap, ShowGrid = show });
        await using var host = GridHost(grid);
        await host.InitializeAsync("grid", "standby", "container");
        var session = Session(host);
        await session.UpdateViewportAsync(new ViewportSnapshot(zoom, new(53, -17)));
        var before = AttachedDocument(session).CaptureSnapshot();
        var original = before.VisualModel.VisualStates.Single();
        var scene = session.CaptureState().CurrentScene!;
        var start = Center(A24PanelBody(scene, original.Id).Bounds);
        var destination = start + new VectorD(33, 27);
        var expectedCenter = snap ? new PointD(220, 320) : new PointD(213, 317);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(scene, start, pointerId: 771);
        for (var i = 0; i < 4; i++)
            await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, destination, pointerId: 771, buttons: 1);
        var gesture = session.CaptureState().EditorState.ActiveGesture!;
        Assert.NotNull(gesture);
        Assert.Equal(expectedCenter - start, gesture.Current - gesture.Origin);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, destination, pointerId: 771);
        await session.WaitForIdleAsync();
        var after = AttachedDocument(session).CaptureSnapshot();
        Assert.Equal(expectedCenter - new VectorD(60, 40), after.VisualModel.VisualStates.Single().Position);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        await host.UndoAsync();
        Assert.Equal(original.Position, AttachedDocument(session).VisualModel.VisualStates.Single().Position);
        await host.RedoAsync();
        Assert.Equal(after.VisualModel.VisualStates.Single().Position, AttachedDocument(session).VisualModel.VisualStates.Single().Position);
        var bytes = NativeDocumentSerializer.Export(AttachedDocument(session).CaptureSnapshot());
        Assert.True((await host.ImportNativeDocumentAsync(bytes.AsMemory())).Succeeded);
        Assert.Equal(after.VisualModel.VisualStates.Single().Position, AttachedDocument(Session(host)).VisualModel.VisualStates.Single().Position);
        Assert.Same(grid.Grid, Session(host).GridSettings);
    }

    [Fact]
    public async Task GridSettingsRedrawReusesSceneAndContentWithoutDocumentHistoryOrPanelWork()
    {
        await using var settings = new BpmnEditorSettingsStore(new GridStorageRuntime());
        var execution = new RecordingRenderExecution();
        await using var host = GridHost(settings, execution);
        await host.InitializeAsync("grid", "container");
        settings.Changed += host.RefreshGridAsync;
        await A24Select(host, new VisualStateId("v-source"));
        var session = Session(host);
        var state = session.CaptureState();
        var snapshot = AttachedDocument(session).CaptureSnapshot();
        var bytes = NativeDocumentSerializer.Export(snapshot);
        var panel = host.CaptureState().ElementToolbox;
        var evaluations = host.ElementCandidateEvaluationCount;
        var uploads = execution.FullUploadCount;
        foreach (var grid in new[] { new GridSettings { PointDistance = 13 }, new GridSettings { ShowGrid = false }, new GridSettings { SnapToGrid = false } })
        {
            Assert.True(await settings.ApplyAsync(grid));
            Assert.Same(state.CurrentScene, session.CaptureState().CurrentScene);
            Assert.Equal(state.Generation, session.CaptureState().Generation);
            Assert.Equal(state.HistoryStatus, session.CaptureState().HistoryStatus);
            Assert.Same(snapshot, AttachedDocument(session).CaptureSnapshot());
            Assert.Equal(bytes.ToArray(), NativeDocumentSerializer.Export(snapshot).ToArray());
            Assert.Equal(uploads, execution.FullUploadCount);
            Assert.Same(panel, host.CaptureState().ElementToolbox);
            Assert.Equal(evaluations, host.ElementCandidateEvaluationCount);
        }
        settings.Changed -= host.RefreshGridAsync;
    }

    [Fact]
    public async Task GridGroupTranslationPreservesOffsetsAndCancellationDoesNotCommit()
    {
        var grid = new TestGridSource(new());
        var selection = new ToolboxSelectionState();
        await using var host = GridHost(grid, selection: selection);
        await host.InitializeAsync("grid", "container");
        var session = Session(host);
        selection.Select(new("bpmn:toolbox:task"));
        await host.RefreshToolboxPlacementAsync();
        await PointerObserver(host).ClickDocumentPointAsync(session.CaptureState().CurrentScene!, new(507, 303));
        await session.WaitForIdleAsync();
        var before = AttachedDocument(session).CaptureSnapshot();
        var ids = before.VisualModel.VisualStates.Select(v => v.Id).ToArray();
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(ids, viewport: session.CaptureState().EditorState.Viewport));
        var history = session.CaptureState().HistoryStatus;
        var pointer = PointerObserver(host);
        var start = new PointD(180, 290);
        var destination = start + new VectorD(31, 23);
        foreach (var cancel in new[] { true, false })
        {
            await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, start, pointerId: 772);
            await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, destination, pointerId: 772, buttons: 1);
            if (cancel)
            {
                await host.CancelActiveInteractionAsync();
                Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
                Assert.Equal(history, session.CaptureState().HistoryStatus);
                continue;
            }
            await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, destination, pointerId: 772);
            await session.WaitForIdleAsync();
        }
        var after = AttachedDocument(session).CaptureSnapshot();
        var deltas = ids.Select(id => after.VisualModel.VisualStates.Single(v => v.Id == id).Position -
            before.VisualModel.VisualStates.Single(v => v.Id == id).Position).ToArray();
        Assert.All(deltas, delta => Assert.Equal(new VectorD(40, 30), delta));
        Assert.Equal(history.EntryCount + 1, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData("bpmn:toolbox:task", true)]
    [InlineData("bpmn:toolbox:start-event", true)]
    [InlineData("bpmn:toolbox:exclusive-gateway", true)]
    [InlineData("bpmn:toolbox:sub-process", true)]
    [InlineData("bpmn:toolbox:task", false)]
    [InlineData("bpmn:toolbox:start-event", false)]
    [InlineData("bpmn:toolbox:exclusive-gateway", false)]
    [InlineData("bpmn:toolbox:sub-process", false)]
    public async Task GridPlacementPreviewEqualsCommittedCenterAtZoom(string tool, bool snap)
    {
        var selection = new ToolboxSelectionState();
        await using var host = GridHost(new TestGridSource(new() { SnapToGrid = snap }), selection: selection);
        await host.InitializeAsync("grid", "container");
        var session = Session(host);
        await session.UpdateViewportAsync(new ViewportSnapshot(.75, new(-21, 33)));
        selection.Select(new(tool));
        await host.RefreshToolboxPlacementAsync();
        var pointer = PointerObserver(host);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, new(507, 303));
        var preview = Assert.Single(session.CaptureState().EditorState.TemporaryFeedback).PlacementPreview!;
        Assert.True(preview.IsAllowed);
        Assert.Equal(snap ? new PointD(500, 300) : new PointD(507, 303), Center(preview.Bounds));
        await pointer.ClickDocumentPointAsync(session.CaptureState().CurrentScene!, new(507, 303));
        await session.WaitForIdleAsync();
        var visual = AttachedDocument(session).VisualModel.VisualStates.Single(v => v.Id != new VisualStateId("v-source"));
        Assert.Equal(preview.Bounds.TopLeft, visual.Position);
        Assert.Equal(preview.Bounds.Size, visual.Size);
    }

    [Fact]
    public void GridEligibilityDefersBoundaryEventsAndOrganizationalTypes()
    {
        Assert.True(BpmnGridPolicy.Supports(BpmnSemanticTypes.Task));
        Assert.False(BpmnGridPolicy.Supports(BpmnSemanticTypes.TimerBoundaryEvent));
        Assert.False(BpmnGridPolicy.Supports(new("organizational:pool")));
    }

    private static DocumentCanvasHost GridHost(IGridSettingsSource grid, RecordingRenderExecution? execution = null,
        ToolboxSelectionState? selection = null) => CreateHost(execution ?? new(), new RecordingSurfaceObserver(new(1200, 800, 1)),
        compositionFactory: new BpmnModelerCompositionFactory(initialDocument: A24Snapshot()), toolboxSelection: selection,
        replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()), gridSettings: grid);

    [Fact]
    public async Task GridSpacingChangesKeepActionBarAndSmartEditPresentationStable()
    {
        var grid = new TestGridSource(new());
        await using var test = await PanComponentFixture.CreateAsync(document: A24Snapshot(), gridSettings: grid);
        await test.Renderer.Dispatcher.InvokeAsync(() => A24Select(test.Host, new VisualStateId("v-source")));
        await test.DrainAsync();
        var markup = ActionBarMarkup(await test.MarkupAsync());
        var actions = ActionBarSnapshot(test.Activator.Canvas);
        var panel = test.Host.CaptureState().ElementToolbox;
        var scene = Session(test.Host).CaptureState().CurrentScene;
        foreach (var spacing in new[] { 10d, 37d, 100d })
        {
            grid.Grid = grid.Grid with { PointDistance = spacing };
            await test.Renderer.Dispatcher.InvokeAsync(test.Host.RefreshGridAsync);
            await test.DrainAsync();
            Assert.Same(scene, Session(test.Host).CaptureState().CurrentScene);
            Assert.Same(actions, ActionBarSnapshot(test.Activator.Canvas));
            Assert.Same(panel, test.Host.CaptureState().ElementToolbox);
            Assert.Equal(markup, ActionBarMarkup(await test.MarkupAsync()));
        }
    }

    [Theory]
    [InlineData(ConnectorRoutingType.Automatic)]
    [InlineData(ConnectorRoutingType.Straight)]
    [InlineData(ConnectorRoutingType.Manual)]
    public async Task GridMoveRetainsRoutingModeManualBendsUnrelatedRoutesAndPriority(ConnectorRoutingType mode)
    {
        await using var host = GridHost(new TestGridSource(new() { PointDistance = 37 }));
        await host.InitializeAsync("grid-routing", "container");
        await A24Select(host, new VisualStateId("v-source"));
        for (var i = 0; i < 3; i++) Assert.True((await host.SmartCreateAsync(new("bpmn:toolbox:task")))!.IsCommitted);
        var session = Session(host);
        var document = AttachedDocument(session);
        var connector = document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords().First().VisualStateId;
        if (mode != ConnectorRoutingType.Automatic)
        {
            Assert.True((await session.ExecuteAsync(new SetConnectorRoutingTypeCommand(document.DocumentId,
                document.Revision, connector, mode))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        if (mode == ConnectorRoutingType.Manual)
        {
            var route = document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords().Single(r => r.VisualStateId == connector).Path;
            Assert.True((await session.ExecuteAsync(new UpdateConnectionRouteCommand(document.DocumentId,
                document.Revision, connector, [route[0], new(293, 397), new(317, 397), route[^1]]))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        var before = document.CaptureSnapshot();
        var source = before.VisualModel.VisualStates.Single(v => v.Id == new VisualStateId("v-source"));
        var routes = before.VisualModel.EnumerateConnectorRoutingRecords().ToArray();
        var unrelatedIds = before.SemanticModel.Relationships.Where(r => r.SourceId != source.SemanticElementId && r.TargetId != source.SemanticElementId)
            .Select(r => before.VisualModel.VisualStates.Single(v => v.SemanticElementId == r.Id).Id).ToHashSet();
        await A24Select(host, source.Id);
        var scene = session.CaptureState().CurrentScene!;
        var start = Center(A24PanelBody(scene, source.Id).Bounds);
        var destination = start + new VectorD(0, 87);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(scene, start, pointerId: 773);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, destination, pointerId: 773, buttons: 1);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, destination, pointerId: 773);
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        var current = after.VisualModel.EnumerateConnectorRoutingRecords().ToArray();
        // The existing router moves changed records behind survivors. Snapping must
        // produce exactly the same route contents and priority as a free drag to this point.
        await using var reference = CreateHost(new(), new RecordingSurfaceObserver(new(1200, 800, 1)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: before));
        await reference.InitializeAsync("reference", "container");
        var referenceSession = Session(reference);
        var referencePointer = PointerObserver(reference);
        var referenceEnd = GridGeometry.Snap(destination, new() { PointDistance = 37 });
        await referencePointer.DownDocumentPointAsync(referenceSession.CaptureState().CurrentScene!, start, pointerId: 774);
        await referencePointer.MoveDocumentPointAsync(referenceSession.CaptureState().CurrentScene!, referenceEnd, pointerId: 774, buttons: 1);
        await referencePointer.UpDocumentPointAsync(referenceSession.CaptureState().CurrentScene!, referenceEnd, pointerId: 774);
        await referenceSession.WaitForIdleAsync();
        Assert.Equal(AttachedDocument(referenceSession).CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords(), current);
        foreach (var record in routes.Where(r => unrelatedIds.Contains(r.VisualStateId)))
            Assert.Equal(record, current.Single(r => r.VisualStateId == record.VisualStateId));
        Assert.Equal(mode, current.Single(r => r.VisualStateId == connector).RoutingType);
        if (mode == ConnectorRoutingType.Manual)
            Assert.Equal(routes.Single(r => r.VisualStateId == connector).ManualDefinition!.Value.AsEnumerable(),
                current.Single(r => r.VisualStateId == connector).ManualDefinition!.Value);
        await host.UndoAsync();
        await reference.UndoAsync();
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.CaptureSnapshot().VisualModel.VisualStates);
        foreach (var record in routes)
            Assert.Equal(record, document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords()
                .Single(candidate => candidate.VisualStateId == record.VisualStateId));
        A23PresentationEqual(AttachedDocument(referenceSession).CaptureSnapshot(), document.CaptureSnapshot());
        await host.RedoAsync();
        await reference.RedoAsync();
        A23PresentationEqual(after, document.CaptureSnapshot());
        A23PresentationEqual(AttachedDocument(referenceSession).CaptureSnapshot(), document.CaptureSnapshot());
    }

    private sealed class TestGridSource(GridSettings grid) : IGridSettingsSource
    {
        public GridSettings Grid { get; set; } = grid;
    }
}
