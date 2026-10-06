using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class ManualRoutePointHistoryIntegrationTests
{
    [Fact]
    public async Task ModeAddMoveRemoveAndReplayEachCommitExactlyOneRevisionAndEvent()
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var id = Connector(test);
        await Commit(new SetConnectorRoutingTypeCommand(test.Snapshot.DocumentId, test.Snapshot.Revision, id,
            ConnectorRoutingType.Manual));
        var manual = Route(test, id);
        await Commit(Edit(test, id, new PointD(470, 114)));
        var added = Route(test, id);
        await Commit(Edit(test, id, new PointD(480, 180)));
        var moved = Route(test, id);
        await Commit(new UpdateConnectionRouteCommand(test.Snapshot.DocumentId, test.Snapshot.Revision, id, []));
        var removed = Route(test, id);
        foreach (var expected in new[] { moved, added, manual })
        {
            await Replay(true);
            Assert.Equal(expected, Route(test, id));
        }
        await Replay(true);
        Assert.Equal(ConnectorRoutingType.Automatic, Route(test, id).RoutingType);
        foreach (var expected in new[] { manual, added, moved, removed })
        {
            await Replay(false);
            Assert.Equal(expected, Route(test, id));
        }

        async Task Commit(ICommand command)
        {
            var before = test.Snapshot;
            var events = test.Events.Count;
            var count = test.State.HistoryStatus.EntryCount;
            await test.ExecuteAsync(command);
            Assert.Equal(before.Revision.Increment(), test.Snapshot.Revision);
            Assert.Equal(events + 1, test.Events.Count);
            Assert.Equal(count + 1, test.State.HistoryStatus.EntryCount);
        }
        async Task Replay(bool undo)
        {
            var revision = test.Snapshot.Revision;
            var events = test.Events.Count;
            Assert.True((undo ? await test.Session.UndoAsync() : await test.Session.RedoAsync()).IsCommitted);
            await test.Session.WaitForIdleAsync();
            Assert.Equal(revision.Increment(), test.Snapshot.Revision);
            Assert.Equal(events + 1, test.Events.Count);
            Assert.Equal(4, test.State.HistoryStatus.EntryCount);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalOrSnappedBackReleaseAndRejectedCommandHaveNoPersistentEffects(bool snap)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var id = Connector(test);
        await test.ExecuteAsync(new SetConnectorRoutingTypeCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            id, ConnectorRoutingType.Manual));
        var start = new PointD(Route(test, id).Path[0].X, 180);
        await test.ExecuteAsync(Edit(test, id, start));
        await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [id]));
        var before = test.Snapshot;
        var history = test.State.HistoryStatus;
        var events = test.Events.Count;
        await using var controller = test.CreateInteractionController();
        await controller.PointerMovedAsync(Pointer(test, start));
        await controller.PointerPressedAsync(Pointer(test, start));
        Assert.NotNull(test.State.EditorState.ActiveGesture);
        await controller.PointerMovedAsync(Pointer(test, start + new VectorD(35, 30)));
        Assert.Same(before, test.Snapshot);
        Assert.Equal(history, test.State.HistoryStatus);
        Assert.Equal(events, test.Events.Count);
        await controller.PointerReleasedAsync(Pointer(test, start + new VectorD(snap ? 3 : 0, 0)));
        await test.Session.WaitForIdleAsync();
        Assert.Null(test.State.EditorState.ActiveGesture);
        Assert.Same(before, test.Snapshot);
        Assert.Equal(history, test.State.HistoryStatus);
        Assert.Equal(events, test.Events.Count);
        Assert.Equal(HistoryOperationStatus.NoChange, (await test.Session.ExecuteAsync(Edit(test, id, start))).Status);
        var rejected = await test.Session.ExecuteAsync(Edit(test, id, new PointD(-1, 180)));
        Assert.False(rejected.Succeeded);
        Assert.Same(before, test.Snapshot);
        Assert.Equal(history, test.State.HistoryStatus);
        Assert.Equal(events, test.Events.Count);
        await test.ExecuteAsync(Edit(test, id, new PointD(480, 200)));
        Assert.Equal(events + 1, test.Events.Count);
    }

    [Fact]
    public async Task NativeOpenAttachesEmptySessionHistoryAndNewPointOperationsAreUndoable()
    {
        await using var original = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var id = Connector(original);
        await original.ExecuteAsync(new SetConnectorRoutingTypeCommand(original.Snapshot.DocumentId,
            original.Snapshot.Revision, id, ConnectorRoutingType.Manual));
        await original.ExecuteAsync(Edit(original, id, new PointD(470, 180)));
        var saved = original.Snapshot;
        var opened = NativeDocumentSerializer.Import(NativeDocumentSerializer.Export(saved).AsMemory());
        Assert.True(opened.Succeeded);
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync(
            BpmnModelerComposition.Create(opened.Document!));
        Assert.Equal(saved, test.Snapshot);
        Assert.Equal(new HistoryStatus(0, false, false), test.State.HistoryStatus);
        var before = Route(test, id);
        await test.ExecuteAsync(Edit(test, id, new(470, 180), new(490, 200)));
        var added = Route(test, id);
        await test.ExecuteAsync(Edit(test, id, new(480, 190), new(490, 200)));
        var moved = Route(test, id);
        await test.ExecuteAsync(Edit(test, id, new PointD(480, 190)));
        var removed = Route(test, id);
        foreach (var expected in new[] { moved, added, before })
        {
            Assert.True((await test.Session.UndoAsync()).IsCommitted);
            await test.Session.WaitForIdleAsync();
            Assert.Equal(expected, Route(test, id));
        }
        foreach (var expected in new[] { added, moved, removed })
        {
            Assert.True((await test.Session.RedoAsync()).IsCommitted);
            await test.Session.WaitForIdleAsync();
            Assert.Equal(expected, Route(test, id));
        }
    }

    private static VisualStateId Connector(PhaseA122PanSceneReuseIntegrationTests.Fixture test) =>
        test.Snapshot.VisualModel.VisualStates.Single(visual => visual.SemanticElementId == BpmnDemoPipeline.ThirdSequenceFlowId).Id;

    private static ConnectorRoutingRecord Route(PhaseA122PanSceneReuseIntegrationTests.Fixture test, VisualStateId id) =>
        BpmnModelerTestComposition.SavedRoute(test.Snapshot, id);

    private static UpdateConnectionRouteCommand Edit(PhaseA122PanSceneReuseIntegrationTests.Fixture test,
        VisualStateId id, params PointD[] points) => new(test.Snapshot.DocumentId, test.Snapshot.Revision, id,
            [Route(test, id).Path[0], .. points, Route(test, id).Path[^1]]);

    private static Canvas2DPointerInput Pointer(PhaseA122PanSceneReuseIntegrationTests.Fixture test, PointD point) =>
        new(43, test.State.CurrentScene!.ViewportTransform.TransformPoint(point), button: 0, buttons: 1);
}
