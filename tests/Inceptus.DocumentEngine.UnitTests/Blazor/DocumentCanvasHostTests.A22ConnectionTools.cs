using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    private static readonly SemanticElementId A22ToolLocalTask = new("a22:tools:local-task");
    private static readonly VisualStateId A22ToolLocalVisual = new("a22:tools:local-visual");
    private static readonly SemanticElementId A22ToolPeerTask = new("a22:host:peer-task");
    private static readonly VisualStateId A22ToolPeerVisual = new("a22:host:peer-task-visual");

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task A22ExplicitMessageToolControlsPointerPreviewAcquisitionCommitAndOneShotCleanup(
        bool participantSource, bool existingTargetAnchor, bool existingSourceAnchor)
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection, existingTargetAnchor, existingSourceAnchor: existingSourceAnchor);
        await host.InitializeAsync("a22-tools", "container");
        var session = Session(host);
        var sourceId = participantSource ? A22ViewParticipantId : A22ViewTaskId;
        var sourceVisual = participantSource ? A22ViewParticipantVisualId : A22ViewTaskVisualId;
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [sourceVisual]))).Succeeded);
        await session.WaitForIdleAsync();
        var tool = A22FlowTool("Message Flow");
        Assert.True(selection.Select(tool.ItemId, tool.CreationId));
        await host.RefreshToolboxPlacementAsync();
        Assert.Equal(tool.CreationId, ConnectionToolState.GetSelection(session.CaptureState().EditorState));
        var before = AttachedDocument(session).CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus.EntryCount;
        var scene = session.CaptureState().CurrentScene!;
        var start = Center(A22ViewBody(scene, sourceId).Bounds);
        var pointer = PointerObserver(host);
        await pointer.DownDocumentPointAsync(scene, start, pointerId: 301);
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        Assert.Null(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, start, pointerId: 301);
        var pending = Assert.IsType<ElementConnectionPreview>(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
        Assert.Equal(sourceId, pending.Source.SemanticId);
        var descriptor = pending.Descriptor;
        Assert.Equal<double>([6d, 4d], descriptor.DashPattern);
        Assert.Equal(ConnectorEndpointDecoration.OpenCircle, descriptor.SourceDecoration);
        Assert.Equal(ConnectorEndpointDecoration.OpenArrow, descriptor.TargetDecoration);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        var target = Center(A22ViewBody(session.CaptureState().CurrentScene!, A22ToolPeerTask).Bounds);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 301, buttons: 0);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus.EntryCount);
        var previewScene = session.CaptureState().CurrentScene!;
        var sourceCircle = Assert.Single(previewScene.Items, item =>
            item.Origin.StableSourceKey == ElementConnectionPreview.FeedbackId + ":source-decoration");
        Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, sourceCircle.Geometry.Kind);
        Assert.Equal("#ffffff", sourceCircle.Style.Fill);
        Assert.Equal(start, Center(sourceCircle.Bounds));
        Assert.DoesNotContain(previewScene.Items, item => item.Metadata.ContainsKey(Canvas2DConnectorAnchorMetadata.AnchorId));
        await pointer.DownDocumentPointAsync(previewScene, target, pointerId: 302);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 302);
        await session.WaitForIdleAsync();

        var after = AttachedDocument(session).CaptureSnapshot();
        var flow = Assert.Single(after.SemanticModel.Relationships);
        Assert.Equal(BpmnSemanticTypes.MessageFlow, flow.TypeId);
        Assert.Equal(sourceId, flow.SourceId);
        Assert.Equal(A22ToolPeerTask, flow.TargetId);
        Assert.Equal(history + 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        Assert.Null(ConnectionToolState.GetSelection(session.CaptureState().EditorState));
        Assert.Null(selection.SelectedItemId);
        var visual = Assert.Single(after.VisualModel.VisualStates, value => value.SemanticElementId == flow.Id);
        if (existingSourceAnchor) Assert.Equal(A22SourceAnchor(sourceVisual), visual.SourceAnchorId);
        else Assert.NotNull(visual.SourceAnchorId);
        Assert.NotNull(visual.TargetAnchorId);
        Assert.Empty(after.VisualModel.DocumentRoutingStates);
        Assert.Single(after.VisualModel.EnumerateConnectorRoutingRecords());
        Assert.Equal(before.VisualModel.VisualStates.Where(value => value.Id != A22ToolPeerVisual && value.Id != sourceVisual),
            after.VisualModel.VisualStates.Where(value => value.Id != A22ToolPeerVisual && value.Id != sourceVisual && value.Id != visual.Id));
        if (existingSourceAnchor) Assert.Equal(before.VisualModel.VisualStates.Single(value => value.Id == sourceVisual),
            after.VisualModel.VisualStates.Single(value => value.Id == sourceVisual));
        await host.UndoAsync();
        Assert.Empty(AttachedDocument(session).CaptureSnapshot().SemanticModel.Relationships);
        await host.RedoAsync();
        Assert.Equal(flow, Assert.Single(AttachedDocument(session).CaptureSnapshot().SemanticModel.Relationships));
        Assert.Equal(visual, AttachedDocument(session).CaptureSnapshot().VisualModel.VisualStates.Single(value => value.Id == visual.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A22SequenceFlowToolAndExistingImplicitSourceShortcutKeepExactSequenceFlowCreation(bool explicitTool)
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection);
        await host.InitializeAsync("a22-sequence-tool", "container");
        var session = Session(host);
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [A22ViewTaskVisualId]));
        await session.WaitForIdleAsync();
        if (explicitTool)
        {
            var tool = A22FlowTool("Sequence Flow");
            selection.Select(tool.ItemId, tool.CreationId);
            await host.RefreshToolboxPlacementAsync();
        }
        var scene = session.CaptureState().CurrentScene!;
        var pointer = PointerObserver(host);
        var target = Center(A22ViewBody(scene, A22ToolLocalTask).Bounds);
        if (explicitTool)
        {
            var source = Center(A22ViewBody(scene, A22ViewTaskId).Bounds);
            await pointer.DownDocumentPointAsync(scene, source, pointerId: 302);
            await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, source, pointerId: 302);
            Assert.NotNull(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
            Assert.Null(session.CaptureState().EditorState.ActiveGesture);
            await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 302, buttons: 0);
            await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 303);
            await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 303);
        }
        else
        {
            await pointer.DownDocumentPointAsync(scene, A22SourcePoint(scene, A22ViewTaskVisualId), pointerId: 302);
            var gesture = Assert.IsType<EditorGestureSnapshot>(session.CaptureState().EditorState.ActiveGesture);
            Assert.Equal(Canvas2DAnchorConnectionGestureMetadata.Kind, gesture.Kind);
            Assert.Equal(ConnectorVisualDescriptor.Default, ConnectorVisualMetadata.Resolve(gesture.Properties));
            await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 302, buttons: 1);
            await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 302);
        }
        await session.WaitForIdleAsync();
        var after = AttachedDocument(session).CaptureSnapshot();
        Assert.Equal(BpmnSemanticTypes.SequenceFlow, Assert.Single(after.SemanticModel.Relationships).TypeId);
        Assert.Empty(after.VisualModel.DocumentRoutingStates);
        Assert.Null(selection.SelectedItemId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A22MessageFlowInvalidTargetOrCancellationDoesNotCreateOrFallBack(bool cancel)
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection);
        await host.InitializeAsync("a22-invalid-tool", "container");
        var session = Session(host);
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [A22ViewTaskVisualId]));
        await session.WaitForIdleAsync();
        var tool = A22FlowTool("Message Flow");
        selection.Select(tool.ItemId, tool.CreationId);
        await host.RefreshToolboxPlacementAsync();
        var before = AttachedDocument(session).CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus.EntryCount;
        var scene = session.CaptureState().CurrentScene!;
        var pointer = PointerObserver(host);
        var sourcePoint = Center(A22ViewBody(scene, A22ViewTaskId).Bounds);
        await pointer.DownDocumentPointAsync(scene, sourcePoint, pointerId: 303);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, sourcePoint, pointerId: 303);
        Assert.NotNull(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
        if (cancel) await host.CancelActiveInteractionAsync();
        else
        {
            var target = Center(A22ViewBody(session.CaptureState().CurrentScene!, A22ToolLocalTask).Bounds);
            await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 303, buttons: 0);
            await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 304);
            await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, target, pointerId: 303);
        }
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        if (cancel)
        {
            Assert.Null(ConnectionToolState.GetSelection(session.CaptureState().EditorState));
            Assert.Null(selection.SelectedItemId);
        }
        else
        {
            Assert.Equal(tool.CreationId, ConnectionToolState.GetSelection(session.CaptureState().EditorState));
            Assert.NotNull(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
        }
    }

    [Fact]
    public async Task A22ConnectionSelectionCancelAndChangingToAnElementToolAreTransient()
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection);
        await host.InitializeAsync("a22-select-tool", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var tool = A22FlowTool("Message Flow");
        selection.Select(tool.ItemId, tool.CreationId);
        await host.RefreshToolboxPlacementAsync();
        Assert.Equal(tool.CreationId, ConnectionToolState.GetSelection(session.CaptureState().EditorState));
        await host.CancelActiveInteractionAsync();
        Assert.Null(ConnectionToolState.GetSelection(session.CaptureState().EditorState));
        Assert.Null(selection.SelectedItemId);
        selection.Select(tool.ItemId, tool.CreationId);
        await host.RefreshToolboxPlacementAsync();
        var task = new ToolboxCatalog(BpmnPluginRegistration.N100.ToolboxContributions).Items.Single(item => item.DisplayName == "Task");
        selection.Select(task.ItemId);
        await host.RefreshToolboxPlacementAsync();
        Assert.Null(ConnectionToolState.GetSelection(session.CaptureState().EditorState));
        Assert.Equal(task.ItemId, selection.SelectedItemId);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task A22MessageToolInNormalPoolDiagramAcceptsSourceWithoutMovingTheNode()
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection);
        await host.InitializeAsync("a22-ineligible-source", "container");
        var session = Session(host);
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [A22ViewTaskVisualId]));
        await session.WaitForIdleAsync();
        var tool = A22FlowTool("Message Flow");
        var source = Center(A22ViewBody(session.CaptureState().CurrentScene!, A22ViewTaskId).Bounds);
        selection.Select(tool.ItemId, tool.CreationId);
        await host.RefreshToolboxPlacementAsync();
        var before = AttachedDocument(session).CaptureSnapshot();
        var pointer = PointerObserver(host);
        var scene = session.CaptureState().CurrentScene!;
        await pointer.MoveDocumentPointAsync(scene, source);
        Assert.NotEqual("not-allowed", pointer.CursorValues[^1]);
        await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, source, pointerId: 304);
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, source, pointerId: 304);
        scene = session.CaptureState().CurrentScene!;
        var body = Center(A22ViewBody(scene, A22ViewTaskId).Bounds);
        var moved = body + new VectorD(80d, 50d);
        await pointer.DownDocumentPointAsync(scene, body, pointerId: 305);
        await pointer.MoveDocumentPointAsync(session.CaptureState().CurrentScene!, moved, pointerId: 305, buttons: 1);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, moved, pointerId: 305);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        Assert.Equal(tool.CreationId, ConnectionToolState.GetSelection(session.CaptureState().EditorState));
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData("BPMN.ExclusiveGateway")]
    [InlineData("BPMN.ReceiveTask")]
    [InlineData("BPMN.MessageCatchEvent")]
    public async Task A22ExplicitToolsOfferEligibleBodiesWithoutAnchorHandles(string type)
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection, sourceType: new(type));
        await host.InitializeAsync("a22-source-visibility", "container");
        var session = Session(host);
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [A22ViewTaskVisualId]));
        await session.WaitForIdleAsync();
        _ = A22SourcePoint(session.CaptureState().CurrentScene!, A22ViewTaskVisualId);
        var before = AttachedDocument(session).CaptureSnapshot();
        var message = A22FlowTool("Message Flow");
        selection.Select(message.ItemId, message.CreationId);
        await host.RefreshToolboxPlacementAsync();
        Assert.False(ConnectionCandidateMetadata.AllowsSource(session.CaptureState().EditorState.ToolState, A22ViewTaskVisualId));
        Assert.DoesNotContain(session.CaptureState().CurrentScene!.Items, item =>
            item.Metadata.ContainsKey(Canvas2DConnectorAnchorMetadata.AnchorId));
        var sequence = A22FlowTool("Sequence Flow");
        selection.Select(sequence.ItemId, sequence.CreationId);
        await host.RefreshToolboxPlacementAsync();
        Assert.Equal(sequence.CreationId, ConnectionToolState.GetSelection(session.CaptureState().EditorState));
        Assert.True(ConnectionCandidateMetadata.AllowsSource(session.CaptureState().EditorState.ToolState, A22ViewTaskVisualId));
        Assert.DoesNotContain(session.CaptureState().CurrentScene!.Items, item => item.Metadata.ContainsKey(Canvas2DConnectorAnchorMetadata.AnchorId));
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
    }

    [Fact]
    public async Task A22RevisionWithoutActiveGestureClearsSourceEligibilityAndSelectedTool()
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection);
        await host.InitializeAsync("a22-source-revision", "container");
        var session = Session(host);
        var tool = A22FlowTool("Message Flow");
        selection.Select(tool.ItemId, tool.CreationId);
        await host.RefreshToolboxPlacementAsync();
        Assert.True(ConnectionCandidateMetadata.AllowsSource(session.CaptureState().EditorState.ToolState, A22ViewTaskVisualId));
        var document = AttachedDocument(session);
        Assert.True((await session.ExecuteAsync(new MoveVisualStateCommand(document.DocumentId, document.Revision,
            A22ToolLocalVisual, new(400d, 60d), VisualPlacementMode.Pinned))).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Null(ConnectionToolState.GetSelection(session.CaptureState().EditorState));
        Assert.Null(selection.SelectedItemId);
        Assert.Empty(session.CaptureState().EditorState.ToolState);
    }

    [Fact]
    public async Task A22ExternalRevisionClearsCapturedMessageGestureAndCannotCommitItsStaleTarget()
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection);
        await host.InitializeAsync("a22-stale-tool", "container");
        var session = Session(host);
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [A22ViewTaskVisualId]));
        await session.WaitForIdleAsync();
        var tool = A22FlowTool("Message Flow");
        selection.Select(tool.ItemId, tool.CreationId);
        await host.RefreshToolboxPlacementAsync();
        var scene = session.CaptureState().CurrentScene!;
        var pointer = PointerObserver(host);
        var sourcePoint = Center(A22ViewBody(scene, A22ViewTaskId).Bounds);
        await pointer.DownDocumentPointAsync(scene, sourcePoint, pointerId: 306);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, sourcePoint, pointerId: 306);
        Assert.NotNull(ConnectionToolState.GetPendingSource(session.CaptureState().EditorState));
        var document = AttachedDocument(session);
        Assert.True((await session.ExecuteAsync(new MoveVisualStateCommand(document.DocumentId, document.Revision,
            A22ToolLocalVisual, new(400d, 60d), VisualPlacementMode.Pinned))).IsCommitted);
        await session.WaitForIdleAsync();
        var changed = document.CaptureSnapshot();
        Assert.Null(session.CaptureState().EditorState.ActiveGesture);
        Assert.Null(ConnectionToolState.GetSelection(session.CaptureState().EditorState));
        Assert.Null(selection.SelectedItemId);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!,
            Center(A22ViewBody(session.CaptureState().CurrentScene!, A22ToolPeerTask).Bounds), pointerId: 306);
        Assert.Same(changed, document.CaptureSnapshot());
        Assert.Empty(changed.SemanticModel.Relationships);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
    }

    private static ToolboxConnectionItemDefinition A22FlowTool(string displayName) =>
        new ToolboxCatalog(BpmnPluginRegistration.N100.ToolboxContributions).ConnectionItems.Single(item => item.DisplayName == displayName);

    private static ConnectorAnchorId A22SourceAnchor(VisualStateId visual) => new(visual.Value + ":source");

    private static PointD A22SourcePoint(Canvas2DScene scene, VisualStateId visual) => Center(Assert.Single(scene.Items,
        item => item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorId, out var id) &&
            id.TextValue == A22SourceAnchor(visual).Value).Bounds);

    private static DocumentCanvasHost A22ConnectionHost(ToolboxSelectionState selection, bool existingTargetAnchor = true,
        SemanticTypeId? sourceType = null, bool existingSourceAnchor = true, bool unassignedLocalTask = false)
    {
        var source = A22ViewSnapshot();
        var semantic = new SemanticModelSnapshot(source.DocumentId, source.Revision,
            source.SemanticModel.Elements.Select(element => element.Id == A22ViewTaskId && sourceType is not null
                ? new SemanticElementSnapshot(element.Id, sourceType, element.Properties,
                    element.AttachedToElementId, element.ContainmentKind) : element)
                .Append(BpmnSemanticFactory.CreateTask(A22ToolLocalTask, "LOCAL", "Local", 3)),
            source.SemanticModel.Relationships, source.SemanticModel.NestedScopes, source.SemanticModel.ScopeMemberships,
            source.SemanticModel.ModelProfiles, unassignedLocalTask ? source.SemanticModel.ProfileAssignments :
                source.SemanticModel.ProfileAssignments.Append(
                    new(Inceptus.DocumentEngine.Organizational.Profiles.OrganizationalModelProfile.Id, A22ToolLocalTask, A22ViewPoolId)));
        var visuals = source.VisualModel.VisualStates.Append(new VisualStateSnapshot(A22ToolLocalVisual,
            A22ToolLocalTask, new(350d, 60d), new(120d, 80d), VisualPlacementMode.Pinned)).Select(visual =>
            new VisualStateSnapshot(visual.Id, visual.SemanticElementId, visual.Position, visual.Size, visual.PlacementMode,
                visual.Route, visual.Properties,
                visual.Id == A22ViewTaskVisualId || visual.Id == A22ViewParticipantVisualId
                    ? existingSourceAnchor ? [new ConnectorAnchor(A22SourceAnchor(visual.Id), ConnectorAnchorSide.Bottom, ConnectorAnchorRole.Source, 0)] : []
                    : visual.Id == A22ToolLocalVisual || (visual.Id == A22ToolPeerVisual && existingTargetAnchor)
                        ? [new ConnectorAnchor(new(visual.Id.Value + ":target"), ConnectorAnchorSide.Top, ConnectorAnchorRole.Target, 0)]
                        : [], visual.SourceAnchorId, visual.TargetAnchorId, visual.BoundaryAttachment));
        return CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(1400d, 900d, 1d)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: new DocumentSnapshot(semantic,
                new VisualModelSnapshot(source.DocumentId, source.Revision, visuals), source.Metadata)), toolboxSelection: selection);
    }
}
