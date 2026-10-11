using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(ConnectorEndpointKind.Source)]
    [InlineData(ConnectorEndpointKind.Target)]
    public async Task A22MessageFlowPointerReconnectionUsesCurrentViewAndPreservesOpenPreviewAndHistory(ConnectorEndpointKind endpoint)
    {
        await using var host = A22ConnectionHost(new ToolboxSelectionState());
        await host.InitializeAsync("a22-reconnect", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var participantTarget = new SemanticElementId("a22:host:peer-participant");
        var participantTargetVisual = new VisualStateId("a22:host:peer-participant-visual");
        var participantTargetAnchor = new ConnectorAnchorId("a22:reconnect:participant-target");
        Assert.True((await session.ExecuteAsync(new AddConnectorAnchorCommand(document.DocumentId, document.Revision,
            participantTargetVisual, participantTargetAnchor, ConnectorAnchorSide.Top, ConnectorAnchorRole.Target, 0))).IsCommitted);
        var relationship = new SemanticElementId("a22:reconnect:flow");
        var visual = new VisualStateId("a22:reconnect:flow-visual");
        var created = await session.ExecuteAsync(new CreateBpmnMessageFlowCommand(document.DocumentId, document.Revision,
            relationship, visual, A22ViewTaskId, A22ToolPeerTask, A22SourceAnchor(A22ViewTaskVisualId),
            new ConnectorAnchorId(A22ToolPeerVisual.Value + ":target")));
        Assert.True(created.IsCommitted, string.Join("; ", created.Diagnostics.Select(item => item.Message)));
        await session.WaitForIdleAsync();
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [visual]));
        await session.WaitForIdleAsync();
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus.EntryCount;
        var scene = session.CaptureState().CurrentScene!;
        var endpointRole = endpoint == ConnectorEndpointKind.Source
            ? Canvas2DConnectorEndpointMetadata.StartEndpointRole : Canvas2DConnectorEndpointMetadata.EndEndpointRole;
        var endpointHandle = Assert.Single(scene.Items, item =>
            item.Metadata.TryGetValue(Canvas2DConnectorEndpointMetadata.HandleRole, out var role) && role.TextValue == endpointRole);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(scene, Center(endpointHandle.Bounds), pointerId: 340);
        var gesture = Assert.IsType<EditorGestureSnapshot>(session.CaptureState().EditorState.ActiveGesture);
        Assert.Equal(Canvas2DConnectorEndpointReconnectionGestureMetadata.Kind, gesture.Kind);
        var candidateAnchor = endpoint == ConnectorEndpointKind.Source ? A22SourceAnchor(A22ViewParticipantVisualId) : participantTargetAnchor;
        var candidate = Assert.Single(session.CaptureState().CurrentScene!.Items, item =>
            item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorId, out var id) && id.TextValue == candidateAnchor.Value);
        var destination = Center(candidate.Bounds);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, destination, pointerId: 340, buttons: 1);
        Assert.Same(before, document.CaptureSnapshot());
        var previews = session.CaptureState().CurrentScene!.Items.Where(item => item.Origin.StableSourceKey?.StartsWith(
            "connector-endpoint-reconnection-preview:", StringComparison.Ordinal) == true).ToArray();
        Assert.Contains(previews, item => item.Style.DashPattern.SequenceEqual([6d, 4d]));
        var decoration = Assert.Single(previews, item => item.Origin.StableSourceKey!.EndsWith(
            endpoint == ConnectorEndpointKind.Source ? ":source-decoration" : ":target-decoration", StringComparison.Ordinal));
        if (endpoint == ConnectorEndpointKind.Source)
            Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, decoration.Geometry.Kind);
        else
        {
            Assert.True(decoration.Geometry.IsClosed);
            Assert.Equal("#ffffff", decoration.Style.Fill);
        }
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, destination, pointerId: 340);
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        var flow = Assert.Single(after.SemanticModel.Relationships);
        Assert.Equal(endpoint == ConnectorEndpointKind.Source ? A22ViewParticipantId : A22ViewTaskId, flow.SourceId);
        Assert.Equal(endpoint == ConnectorEndpointKind.Target ? participantTarget : A22ToolPeerTask, flow.TargetId);
        Assert.Equal(history + 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        Assert.Equal(before.VisualModel.VisualStates.Where(item => item.Id != visual), after.VisualModel.VisualStates.Where(item => item.Id != visual));
        await host.UndoAsync();
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), document.CaptureSnapshot().SemanticModel.Relationships);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.CaptureSnapshot().VisualModel.VisualStates);
        await host.RedoAsync();
        Assert.Equal(after.SemanticModel.Relationships.AsEnumerable(), document.CaptureSnapshot().SemanticModel.Relationships);
        Assert.Equal(after.VisualModel.VisualStates.AsEnumerable(), document.CaptureSnapshot().VisualModel.VisualStates);
    }
}
