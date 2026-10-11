using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task A24SmartEditRespectsPropertiesAndContextMenuAfterReenabling()
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("smart-modal", "container");
        await A24Select(host, new VisualStateId("v-source"));
        Assert.NotNull(await host.OpenPropertiesAsync(new VisualStateId("v-source")));
        host.SetSmartEditEnabled(false);
        host.SetSmartEditEnabled(true);
        Assert.Null(host.CaptureState().ElementToolbox);
        host.UpdatePropertiesFormState(false, null, false);
        Assert.NotNull(host.CaptureState().ElementToolbox);
        await PointerObserver(host).ContextMenuDocumentPointAsync(Session(host).CaptureState().CurrentScene!, new(180, 290));
        Assert.NotNull(host.CaptureState().ContextMenu);
        host.SetSmartEditEnabled(false);
        host.SetSmartEditEnabled(true);
        Assert.Null(host.CaptureState().ElementToolbox);
        Assert.Equal(new VisualStateId("v-source"), Assert.Single(Session(host).CaptureState().EditorState.Selection));
    }

    [Fact]
    public async Task A24SmartEditOffAllowsEndpointReconnectionAndExactHistory()
    {
        await using var host = A22ConnectionHost(new ToolboxSelectionState());
        await host.InitializeAsync("smart-off-reconnect", "container");
        host.SetSmartEditEnabled(false);
        var session = Session(host);
        var document = AttachedDocument(session);
        var visual = new VisualStateId("smart:flow-visual");
        Assert.True((await session.ExecuteAsync(new CreateBpmnMessageFlowCommand(document.DocumentId, document.Revision,
            new("smart:flow"), visual, A22ViewTaskId, A22ToolPeerTask, A22SourceAnchor(A22ViewTaskVisualId),
            new ConnectorAnchorId(A22ToolPeerVisual.Value + ":target")))).IsCommitted);
        await session.WaitForIdleAsync();
        await A24Select(host, visual);
        var before = document.CaptureSnapshot();
        var scene = session.CaptureState().CurrentScene!;
        var handle = Assert.Single(scene.Items, item =>
            item.Metadata.TryGetValue(Canvas2DConnectorEndpointMetadata.HandleRole, out var role) &&
            role.TextValue == Canvas2DConnectorEndpointMetadata.StartEndpointRole);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(scene, Center(handle.Bounds), pointerId: 414);
        Assert.Equal(Canvas2DConnectorEndpointReconnectionGestureMetadata.Kind, session.CaptureState().EditorState.ActiveGesture?.Kind);
        var candidate = Assert.Single(session.CaptureState().CurrentScene!.Items, item =>
            item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorId, out var id) &&
            id.TextValue == A22SourceAnchor(A22ViewParticipantVisualId).Value);
        var destination = Center(candidate.Bounds);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, destination, pointerId: 414, buttons: 1);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, destination, pointerId: 414);
        await session.WaitForIdleAsync();
        var after = document.CaptureSnapshot();
        Assert.Equal(A22ViewParticipantId, Assert.Single(after.SemanticModel.Relationships).SourceId);
        await host.UndoAsync();
        A23PresentationEqual(before, document.CaptureSnapshot());
        await host.RedoAsync();
        A23PresentationEqual(after, document.CaptureSnapshot());
        Assert.False(host.CaptureState().SmartEditEnabled);
    }

    [Fact]
    public async Task A24SmartEditOffAllowsGlobalPlacementMoveResizePropertiesDeleteAndHistory()
    {
        var selection = new ToolboxSelectionState();
        await using var host = CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(1920, 1080, 1)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: A24Snapshot()), toolboxSelection: selection);
        await host.InitializeAsync("smart-off-edit", "container");
        host.SetSmartEditEnabled(false);
        var session = Session(host);
        var document = AttachedDocument(session);
        var pointer = PointerObserver(host);
        selection.Select(new("bpmn:toolbox:task"));
        await host.RefreshToolboxPlacementAsync();
        await pointer.ClickDocumentPointAsync(session.CaptureState().CurrentScene!, new(500, 300));
        await session.WaitForIdleAsync();
        Assert.Equal(2, document.SemanticModel.Elements.Length);
        var id = Assert.Single(session.CaptureState().EditorState.Selection);
        var placed = document.VisualModel.VisualStates.Single(visual => visual.Id == id);
        Assert.NotNull(await host.OpenPropertiesAsync(id));
        Assert.True(host.UpdatePropertiesFormState(false, null, false));
        var scene = session.CaptureState().CurrentScene!;
        var start = new PointD(500, 300);
        var end = start + new VectorD(40, 40);
        await pointer.DownDocumentPointAsync(scene, start, pointerId: 410);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, end, pointerId: 410, buttons: 1);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, end, pointerId: 410);
        await session.WaitForIdleAsync();
        Assert.Equal(placed.Position + new VectorD(40, 40), document.VisualModel.VisualStates.Single(visual => visual.Id == id).Position);
        scene = session.CaptureState().CurrentScene!;
        start = Center(ResizeZone(scene, "southeast").Bounds);
        end = start + new VectorD(30, 20);
        await pointer.DownDocumentPointAsync(scene, start, pointerId: 411);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, end, pointerId: 411, buttons: 1);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, end, pointerId: 411);
        await session.WaitForIdleAsync();
        var resized = document.VisualModel.VisualStates.Single(visual => visual.Id == id);
        Assert.True(resized.Size.Width > placed.Size.Width);
        Assert.True(resized.Size.Height > placed.Size.Height);
        var beforeDelete = document.CaptureSnapshot();
        await pointer.ContextMenuDocumentPointAsync(session.CaptureState().CurrentScene!, new(540, 340));
        Assert.NotNull(host.CaptureState().ContextMenu?.DeletionAction);
        Assert.True((await host.ExecuteDeletionContextActionAsync())!.IsCommitted);
        Assert.Single(document.SemanticModel.Elements);
        await host.UndoAsync();
        A23PresentationEqual(beforeDelete, document.CaptureSnapshot());
        await host.RedoAsync();
        Assert.Single(document.SemanticModel.Elements);
        Assert.False(host.CaptureState().SmartEditEnabled);
        Assert.Null(host.CaptureState().ElementToolbox);
    }

    [Fact]
    public async Task A24SmartEditOffAllowsConnectorBendEditingAndUndo()
    {
        await using var host = CreatePropertiesUxHost(A22ProcessConnectorSnapshot());
        await host.InitializeAsync("smart-off-route", "container");
        host.SetSmartEditEnabled(false);
        var session = Session(host);
        var document = AttachedDocument(session);
        Assert.True((await session.ExecuteAsync(new SetConnectorRoutingTypeCommand(document.DocumentId,
            document.Revision, A22ProcessFlowVisual, ConnectorRoutingType.Manual))).IsCommitted);
        await session.WaitForIdleAsync();
        var record = A22ProcessRoute(document.CaptureSnapshot());
        var bend = new PointD(240, 190);
        Assert.True((await session.ExecuteAsync(new UpdateConnectionRouteCommand(document.DocumentId,
            document.Revision, A22ProcessFlowVisual, [record.Path[0], bend, record.Path[^1]]))).IsCommitted);
        await session.WaitForIdleAsync();
        await A24Select(host, A22ProcessFlowVisual);
        var scene = session.CaptureState().CurrentScene!;
        var handle = Assert.Single(scene.Items, item => item.Origin.VisualStateId == A22ProcessFlowVisual &&
            item.Metadata.ContainsKey(Canvas2DRouteGestureMetadata.BendIndex));
        var start = Center(handle.Bounds);
        var end = start + new VectorD(20, 30);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(scene, start, pointerId: 412);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, end, pointerId: 412, buttons: 1);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, end, pointerId: 412);
        await session.WaitForIdleAsync();
        Assert.Equal(bend + new VectorD(20, 30), A22ProcessRoute(document.CaptureSnapshot()).Path[1]);
        await host.UndoAsync();
        Assert.Equal(bend, A22ProcessRoute(document.CaptureSnapshot()).Path[1]);
        Assert.False(host.CaptureState().SmartEditEnabled);
    }
}
