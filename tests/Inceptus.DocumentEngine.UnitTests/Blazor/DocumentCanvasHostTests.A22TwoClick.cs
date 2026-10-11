using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task A22TwoClickJointRoutingChoosesFacingFreePointsWithoutCreatingBetterPoints()
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection);
        await host.InitializeAsync("a22-joint-pair", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var sourceAnchor = new ConnectorAnchorId("a22:joint:source-right");
        var targetAnchor = new ConnectorAnchorId("a22:joint:target-left");
        Assert.True((await session.ExecuteAsync(new AddConnectorAnchorCommand(document.DocumentId, document.Revision,
            A22ViewTaskVisualId, sourceAnchor, ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0))).IsCommitted);
        Assert.True((await session.ExecuteAsync(new AddConnectorAnchorCommand(document.DocumentId, document.Revision,
            A22ToolLocalVisual, targetAnchor, ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0))).IsCommitted);
        await session.WaitForIdleAsync();
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus.EntryCount;
        var tool = A22FlowTool("Sequence Flow");
        selection.Select(tool.ItemId, tool.CreationId);
        await host.RefreshToolboxPlacementAsync();
        var source = A22ViewBody(session.CaptureState().CurrentScene!, A22ViewTaskId).Bounds;
        var target = A22ViewBody(session.CaptureState().CurrentScene!, A22ToolLocalTask).Bounds;
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, Center(source), pointerId: 420);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, Center(source), pointerId: 420);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, Center(target), pointerId: 421, buttons: 0);
        await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, Center(target), pointerId: 421);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, Center(target), pointerId: 421);
        var after = document.CaptureSnapshot();
        var flow = Assert.Single(after.SemanticModel.Relationships);
        var visual = Assert.Single(after.VisualModel.VisualStates, visual => visual.SemanticElementId == flow.Id);
        Assert.Equal(sourceAnchor, visual.SourceAnchorId);
        Assert.Equal(targetAnchor, visual.TargetAnchorId);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), after.VisualModel.VisualStates.Where(item => item.Id != visual.Id));
        var path = Assert.Single(after.VisualModel.EnumerateConnectorRoutingRecords()).Path;
        Assert.Equal<PointD>([new(source.Right, Center(source).Y), new(target.Left, Center(target).Y)], path);
        Assert.Equal(history + 1, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A22TwoClickCenterPreviewSurvivesPanZoomAndInvalidTargetThenCommitsValidTarget(bool message)
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection);
        await host.InitializeAsync("a22-two-click-viewport", "container");
        var session = Session(host);
        var tool = A22FlowTool(message ? "Message Flow" : "Sequence Flow");
        selection.Select(tool.ItemId, tool.CreationId);
        await host.RefreshToolboxPlacementAsync();
        var before = AttachedDocument(session).CaptureSnapshot();
        var source = Center(A22ViewBody(session.CaptureState().CurrentScene!, A22ViewTaskId).Bounds);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, source, pointerId: 401);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, source, pointerId: 401);
        var pending = Assert.IsType<ElementConnectionPreview>(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
        var preview = Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Origin.StableSourceKey == ElementConnectionPreview.FeedbackId);
        Assert.Equal(source, preview.Geometry.Points[0]);
        Assert.Equal(Canvas2DHitTestMode.None, preview.HitTestPolicy.Mode);
        await host.ZoomInAsync();
        await session.PanViewportAsync(new VectorD(40d, -20d));
        await session.WaitForIdleAsync();
        Assert.Equal(pending.Source, ConnectionToolState.GetPendingSource(session.CaptureState().EditorState)!.Source);
        var empty = new PointD(1100d, 850d);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, empty, pointerId: 402, buttons: 0);
        await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, empty, pointerId: 402);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, empty, pointerId: 402);
        Assert.NotNull(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        if (message)
        {
            var invalid = Center(A22ViewBody(session.CaptureState().CurrentScene!, A22ToolLocalTask).Bounds);
            await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, invalid, pointerId: 403);
            await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, invalid, pointerId: 403);
            Assert.NotNull(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
            Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        }
        var target = Center(A22ViewBody(session.CaptureState().CurrentScene!, message ? A22ToolPeerTask : A22ToolLocalTask).Bounds);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 404, buttons: 0);
        preview = Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Origin.StableSourceKey == ElementConnectionPreview.FeedbackId);
        Assert.Equal(source, preview.Geometry.Points[0]);
        Assert.Equal(target.X, preview.Geometry.Points[1].X, 8);
        Assert.Equal(target.Y, preview.Geometry.Points[1].Y, 8);
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
        await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 404);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 404);
        Assert.Equal(message ? BpmnSemanticTypes.MessageFlow : BpmnSemanticTypes.SequenceFlow,
            Assert.Single(AttachedDocument(session).CaptureSnapshot().SemanticModel.Relationships).TypeId);
        Assert.Null(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A22TwoClickSwitchOrCancelClearsSourcePreviewWithoutDocumentMutation(bool switchTool)
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection);
        await host.InitializeAsync("a22-two-click-cancel", "container");
        var session = Session(host);
        var tool = A22FlowTool("Sequence Flow");
        selection.Select(tool.ItemId, tool.CreationId);
        await host.RefreshToolboxPlacementAsync();
        var before = AttachedDocument(session).CaptureSnapshot();
        var point = Center(A22ViewBody(session.CaptureState().CurrentScene!, A22ViewTaskId).Bounds);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, point, pointerId: 410);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, point, pointerId: 410);
        Assert.NotNull(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
        if (switchTool)
        {
            var message = A22FlowTool("Message Flow");
            selection.Select(message.ItemId, message.CreationId);
            await host.RefreshToolboxPlacementAsync();
            Assert.Equal(message.CreationId, ConnectionToolState.GetSelection(session.CaptureState().EditorState));
        }
        else await host.CancelActiveInteractionAsync();
        Assert.Null(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
        Assert.DoesNotContain(session.CaptureState().CurrentScene!.Items, item => item.Origin.StableSourceKey == ElementConnectionPreview.FeedbackId);
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }
}
