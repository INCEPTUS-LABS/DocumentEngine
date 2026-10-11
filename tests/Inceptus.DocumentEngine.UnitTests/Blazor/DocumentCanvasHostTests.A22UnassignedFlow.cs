using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A22UnassignedSequenceFlowSupportsBothDirectionsAndCreationGestures(bool reverse, bool explicitTool)
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection, unassignedLocalTask: true);
        await host.InitializeAsync("a22-unassigned-flow", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var sourceId = reverse ? A22ToolLocalTask : A22ViewTaskId;
        var targetId = reverse ? A22ViewTaskId : A22ToolLocalTask;
        var sourceVisual = reverse ? A22ToolLocalVisual : A22ViewTaskVisualId;
        if (reverse)
        {
            Assert.True((await session.ExecuteAsync(new AddConnectorAnchorCommand(document.DocumentId, document.Revision,
                sourceVisual, A22SourceAnchor(sourceVisual), ConnectorAnchorSide.Top, ConnectorAnchorRole.Source, 0))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [sourceVisual]));
        await session.WaitForIdleAsync();
        if (explicitTool)
        {
            var tool = A22FlowTool("Sequence Flow");
            selection.Select(tool.ItemId, tool.CreationId);
            await host.RefreshToolboxPlacementAsync();
        }
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus.EntryCount;
        var scene = session.CaptureState().CurrentScene!;
        var source = explicitTool ? Center(A22ViewBody(scene, sourceId).Bounds) : A22SourcePoint(scene, sourceVisual);
        var target = Center(A22ViewBody(scene, targetId).Bounds);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(scene, source, pointerId: 601);
        if (explicitTool)
        {
            await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, source, pointerId: 601);
            Assert.NotNull(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
        }
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 601,
            buttons: explicitTool ? 0 : 1);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus.EntryCount);
        if (explicitTool) await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 601);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 601);
        await session.WaitForIdleAsync();

        var after = document.CaptureSnapshot();
        var flow = Assert.Single(after.SemanticModel.Relationships);
        Assert.Equal(BpmnSemanticTypes.SequenceFlow, flow.TypeId);
        Assert.Equal(sourceId, flow.SourceId);
        Assert.Equal(targetId, flow.TargetId);
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(history + 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Null(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
        Assert.Null(selection.SelectedItemId);
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements);
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        Assert.Null(OrganizationalSemantics.GetOwningPoolId(after.SemanticModel, A22ToolLocalTask));
        foreach (var original in before.VisualModel.VisualStates)
        {
            var current = after.VisualModel.VisualStates.Single(visual => visual.Id == original.Id);
            Assert.Equal(original.Position, current.Position);
            Assert.Equal(original.Size, current.Size);
            Assert.Equal(original.Properties, current.Properties);
        }
        var connector = Assert.Single(after.VisualModel.VisualStates, visual => visual.SemanticElementId == flow.Id);
        Assert.NotNull(connector.SourceAnchorId);
        Assert.NotNull(connector.TargetAnchorId);
        var route = Assert.Single(after.VisualModel.EnumerateConnectorRoutingRecords());
        Assert.Equal(ConnectorRoutingOutcome.Path, route.Outcome);
        Assert.Equal(connector.Id, route.VisualStateId);
        Assert.All(route.Path.Zip(route.Path.Skip(1)), pair =>
            Assert.True(pair.First.X == pair.Second.X || pair.First.Y == pair.Second.Y));
        Assert.Contains(session.CaptureState().CurrentScene!.Items, item => item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == connector.Id && item.Geometry.Kind == Canvas2DSceneGeometryKind.Path && item.Style.DashPattern.IsEmpty);

        await host.UndoAsync();
        Assert.Empty(document.CaptureSnapshot().SemanticModel.Relationships);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.CaptureSnapshot().VisualModel.VisualStates);
        await host.RedoAsync();
        var restored = document.CaptureSnapshot();
        Assert.Equal(flow, Assert.Single(restored.SemanticModel.Relationships));
        Assert.Equal(after.VisualModel.VisualStates.AsEnumerable(), restored.VisualModel.VisualStates);
        Assert.Equal(route, Assert.Single(restored.VisualModel.EnumerateConnectorRoutingRecords()));
        var opened = NativeDocumentSerializer.Import(NativeDocumentSerializer.Export(restored).AsMemory());
        Assert.True(opened.Succeeded);
        await using var reopened = CreatePropertiesUxHost(opened.Document!.CaptureSnapshot());
        await reopened.InitializeAsync("a22-unassigned-reopened", "container");
        Assert.Equal(restored, AttachedDocument(Session(reopened)).CaptureSnapshot());
    }

    [Fact]
    public async Task A22UnassignedMessageFlowIsRejectedWithoutSequenceFallback()
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection, unassignedLocalTask: true);
        await host.InitializeAsync("a22-unassigned-message", "container");
        var session = Session(host);
        var tool = A22FlowTool("Message Flow");
        selection.Select(tool.ItemId, tool.CreationId);
        await host.RefreshToolboxPlacementAsync();
        var before = AttachedDocument(session).CaptureSnapshot();
        foreach (var id in new[] { A22ViewTaskId, A22ToolLocalTask })
        {
            var scene = session.CaptureState().CurrentScene!;
            var point = Center(A22ViewBody(scene, id).Bounds);
            await PointerObserver(host).DownDocumentPointAsync(scene, point, pointerId: 602);
            await PointerObserver(host).UpDocumentPointAsync(session.CaptureState().CurrentScene!, point, pointerId: 602);
        }
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(tool.CreationId, ConnectionToolState.GetSelection(session.CaptureState().EditorState));
        Assert.NotNull(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
    }

    [Theory]
    [InlineData(ConnectorEndpointKind.Source)]
    [InlineData(ConnectorEndpointKind.Target)]
    public async Task A22UnassignedSequenceFlowReconnectionPreservesIdentityAndHistory(ConnectorEndpointKind endpoint)
    {
        await using var host = A22ConnectionHost(new ToolboxSelectionState());
        await host.InitializeAsync("a22-unassigned-reconnect", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var newAnchor = new ConnectorAnchorId(A22ToolPeerVisual.Value + (endpoint == ConnectorEndpointKind.Source ? ":source" : ":target"));
        // Keep the fixture within the Unassigned workspace's existing height.
        Assert.True((await session.ExecuteAsync(new MoveVisualStateCommand(document.DocumentId, document.Revision,
            A22ToolPeerVisual, new(50d, 60d), VisualPlacementMode.Pinned))).IsCommitted);
        if (endpoint == ConnectorEndpointKind.Source)
            Assert.True((await session.ExecuteAsync(new AddConnectorAnchorCommand(document.DocumentId, document.Revision,
                A22ToolPeerVisual, newAnchor, ConnectorAnchorSide.Top, ConnectorAnchorRole.Source, 0))).IsCommitted);
        Assert.True((await session.ExecuteAsync(new CreateBpmnSequenceFlowCommand(document.DocumentId, document.Revision,
            new("unassigned:flow"), new("unassigned:connector"), A22ViewTaskId, A22ToolLocalTask,
            A22SourceAnchor(A22ViewTaskVisualId), new(A22ToolLocalVisual.Value + ":target"), name: "Preserved"))).IsCommitted);
        await session.WaitForIdleAsync();
        var original = document.CaptureSnapshot();
        var flow = Assert.Single(original.SemanticModel.Relationships);
        var connector = Assert.Single(original.VisualModel.VisualStates, visual => visual.SemanticElementId == flow.Id);
        ReconnectBpmnSequenceFlowEndpointCommand Reconnect() => new(document.DocumentId, document.Revision, flow.Id, connector.Id,
            endpoint, endpoint == ConnectorEndpointKind.Source ? flow.SourceId : flow.TargetId,
            (endpoint == ConnectorEndpointKind.Source ? connector.SourceAnchorId : connector.TargetAnchorId)!, A22ToolPeerTask, newAnchor);
        var rejected = await session.ExecuteAsync(Reconnect());
        Assert.False(rejected.IsCommitted);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == BpmnEffectiveParticipant.SequenceCrossesParticipant);
        Assert.Same(original, document.CaptureSnapshot());

        var unassigned = await session.ExecuteAsync(new UnassignOrganizationalElementCommand(document.DocumentId, document.Revision,
            A22ToolPeerTask));
        Assert.True(unassigned.IsCommitted, string.Join("; ", unassigned.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        await session.WaitForIdleAsync();
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus.EntryCount;
        Assert.True((await session.ExecuteAsync(Reconnect())).IsCommitted);
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        var changed = Assert.Single(after.SemanticModel.Relationships);
        Assert.Equal(flow.Id, changed.Id);
        Assert.Equal(flow.Properties, changed.Properties);
        Assert.Equal(endpoint == ConnectorEndpointKind.Source ? A22ToolPeerTask : flow.SourceId, changed.SourceId);
        Assert.Equal(endpoint == ConnectorEndpointKind.Target ? A22ToolPeerTask : flow.TargetId, changed.TargetId);
        Assert.Equal(connector.Id, Assert.Single(after.VisualModel.VisualStates, visual => visual.SemanticElementId == flow.Id).Id);
        Assert.Equal(before.VisualModel.VisualStates.Where(visual => visual.Id != connector.Id),
            after.VisualModel.VisualStates.Where(visual => visual.Id != connector.Id));
        Assert.Equal(before.SemanticModel.ProfileAssignments.AsEnumerable(), after.SemanticModel.ProfileAssignments);
        Assert.Equal(history + 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(ConnectorRoutingOutcome.Path, Assert.Single(after.VisualModel.EnumerateConnectorRoutingRecords()).Outcome);
        await host.UndoAsync();
        Assert.Equal(flow, Assert.Single(document.CaptureSnapshot().SemanticModel.Relationships));
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.CaptureSnapshot().VisualModel.VisualStates);
        await host.RedoAsync();
        Assert.Equal(changed, Assert.Single(document.CaptureSnapshot().SemanticModel.Relationships));
        Assert.Equal(after.VisualModel.VisualStates.AsEnumerable(), document.CaptureSnapshot().VisualModel.VisualStates);
    }
}
