using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task A24SmartEditDefaultsOnAndTogglesOnlyOverlayWithoutReevaluationOrDocumentWork()
    {
        await using var host = A23Host(A24Snapshot());
        Assert.True(host.CaptureState().SmartEditEnabled);
        await host.InitializeAsync("smart-edit", "container");
        Assert.True(host.CaptureState().SmartEditEnabled);
        await A24Select(host, new VisualStateId("v-source"));
        var session = Session(host);
        var before = session.CaptureState();
        var snapshot = AttachedDocument(session).CaptureSnapshot();
        var bytes = NativeDocumentSerializer.Export(snapshot);
        var overlay = Assert.IsType<ElementToolboxState>(host.CaptureState().ElementToolbox);
        var evaluations = host.ElementCandidateEvaluationCount;
        for (var index = 0; index < 4; index++)
        {
            host.SetSmartEditEnabled(index % 2 == 1);
            var state = host.CaptureState();
            Assert.Equal(index % 2 == 1, state.SmartEditEnabled);
            if (state.SmartEditEnabled) Assert.Equal(overlay, state.ElementToolbox);
            else Assert.Null(state.ElementToolbox);
            Assert.Same(snapshot, AttachedDocument(session).CaptureSnapshot());
            Assert.Equal(bytes.ToArray(), NativeDocumentSerializer.Export(AttachedDocument(session).CaptureSnapshot()).ToArray());
            Assert.Equal(before.DocumentRevision, state.Session!.DocumentRevision);
            Assert.Equal(before.HistoryStatus, state.Session.HistoryStatus);
            Assert.Equal(before.EditorState, state.Session.EditorState);
            Assert.Same(before.CurrentScene, state.Session.CurrentScene);
            Assert.Equal(before.Generation, state.Session.Generation);
            Assert.Equal(evaluations, host.ElementCandidateEvaluationCount);
        }
        host.DismissElementToolbox();
        Assert.Null(host.CaptureState().ElementToolbox);
        host.SetSmartEditEnabled(false);
        Assert.Null(await host.SmartCreateAsync(new("bpmn:toolbox:task")));
        Assert.Null(await host.AutoPointsAsync());
        Assert.Null(await host.DeleteToolboxElementAsync());
        host.SetSmartEditEnabled(true);
        Assert.Equal(overlay, host.CaptureState().ElementToolbox);
        Assert.Same(snapshot, AttachedDocument(session).CaptureSnapshot());
    }

    [Theory]
    [InlineData("move")]
    [InlineData("resize")]
    [InlineData("reconnect")]
    [InlineData("placement")]
    [InlineData("Sequence Flow")]
    [InlineData("Message Flow")]
    public async Task A24SmartEditToggleDoesNotOverrideOrCancelAnActiveGesture(string kind)
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("smart-gesture", "container");
        await A24Select(host, new VisualStateId("v-source"));
        var session = Session(host);
        var idle = session.CaptureState().EditorState;
        var active = kind.EndsWith("Flow", StringComparison.Ordinal)
            ? ConnectionToolState.WithSelection(idle, A22FlowTool(kind).CreationId)
            : new EditorStateSnapshot(idle.Selection, null, null, null, idle.Viewport,
                new EditorGestureSnapshot("test", kind, default, default));
        Assert.True((await session.UpdateEditorStateAsync(active)).Succeeded);
        await session.WaitForIdleAsync();
        var snapshot = AttachedDocument(session).CaptureSnapshot();
        foreach (var enabled in new[] { true, false, true })
        {
            host.SetSmartEditEnabled(enabled);
            Assert.Null(host.CaptureState().ElementToolbox);
            Assert.Equal(active, session.CaptureState().EditorState);
            Assert.Same(snapshot, AttachedDocument(session).CaptureSnapshot());
        }
        if (kind.EndsWith("Flow", StringComparison.Ordinal)) await host.CancelActiveInteractionAsync();
        else await session.UpdateEditorStateAsync(idle);
        await session.WaitForIdleAsync();
        Assert.NotNull(host.CaptureState().ElementToolbox);
    }

    [Fact]
    public async Task A24SmartEditAndExpandedGroupSurviveCommandsViewportValidationAndImport()
    {
        await using var host = CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(1920, 1080, 1)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: A24Snapshot()),
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        await host.InitializeAsync("smart-lifecycle", "standby", "container");
        var groups = new ToolboxPresentationState();
        var catalog = new ToolboxCatalog(BpmnPluginRegistration.N100.ToolboxContributions);
        groups.Reconcile(catalog.Groups.Select(group => group.GroupId));
        Assert.Null(groups.ExpandedToolboxGroupId);
        var before = AttachedDocument(Session(host)).CaptureSnapshot();
        var history = Session(host).CaptureState().HistoryStatus;
        Assert.True(groups.ToggleGroup(catalog.Groups[0].GroupId));
        Assert.Same(before, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Equal(history, Session(host).CaptureState().HistoryStatus);
        foreach (var operation in new[] { "selection", "smart-create", "off", "undo", "redo", "pan", "zoom", "arrange", "validate", "import" })
        {
            var session = Session(host);
            switch (operation)
            {
                case "selection": await A24Select(host, new VisualStateId("v-source")); break;
                case "smart-create": Assert.True((await host.SmartCreateAsync(new("bpmn:toolbox:task")))!.IsCommitted); break;
                case "off": host.SetSmartEditEnabled(false); break;
                case "undo": await host.UndoAsync(); break;
                case "redo": await host.RedoAsync(); break;
                case "pan": await session.UpdateViewportAsync(new ViewportSnapshot(1, new(35, 22))); break;
                case "zoom": await session.UpdateViewportAsync(new ViewportSnapshot(.3, new(35, 22))); break;
                case "arrange":
                    Assert.True(host.CaptureState().CanAutoArrange);
                    Assert.True((await host.AutoArrangeAsync())!.IsCommitted);
                    break;
                case "validate": Assert.NotNull(await host.ValidateAsync()); break;
                case "import":
                    var bytes = NativeDocumentSerializer.Export(AttachedDocument(session).CaptureSnapshot());
                    Assert.True((await host.ImportNativeDocumentAsync(bytes.AsMemory())).Succeeded);
                    Assert.NotSame(session, Session(host));
                    break;
            }
            await Session(host).WaitForIdleAsync();
            groups.Reconcile(catalog.Groups.Select(group => group.GroupId));
            Assert.Equal(catalog.Groups[0].GroupId, groups.ExpandedToolboxGroupId);
            if (operation is not ("selection" or "smart-create")) Assert.False(host.CaptureState().SmartEditEnabled);
        }
        host.SetSmartEditEnabled(true);
        Assert.Equal(catalog.Groups[0].GroupId, groups.ExpandedToolboxGroupId);
        host.SetSmartEditEnabled(false);
        var exported = await host.ExportNativeDocumentAsync();
        Assert.True(exported.Succeeded);
        await using var fresh = A23Host(AttachedDocument(Session(host)).CaptureSnapshot());
        await fresh.InitializeAsync("fresh", "container");
        Assert.True(fresh.CaptureState().SmartEditEnabled);
        var freshGroups = new ToolboxPresentationState();
        freshGroups.Reconcile(catalog.Groups.Select(group => group.GroupId));
        Assert.Null(freshGroups.ExpandedToolboxGroupId);
    }

    [Theory]
    [InlineData("Sequence Flow")]
    [InlineData("Message Flow")]
    public async Task A24SmartEditOffAllowsExplicitFlowCreationAndExactUndoRedo(string name)
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22ConnectionHost(selection);
        await host.InitializeAsync("smart-off-flow", "container");
        host.SetSmartEditEnabled(false);
        var session = Session(host);
        var tool = A22FlowTool(name);
        Assert.True(selection.Select(tool.ItemId, tool.CreationId));
        await host.RefreshToolboxPlacementAsync();
        var before = AttachedDocument(session).CaptureSnapshot();
        var pointer = PointerObserver(host);
        foreach (var id in new[] { A22ViewTaskId, name == "Sequence Flow" ? A22ToolLocalTask : A22ToolPeerTask })
        {
            var scene = session.CaptureState().CurrentScene!;
            var point = Center(A22ViewBody(scene, id).Bounds);
            await pointer.DownDocumentPointAsync(scene, point, pointerId: 401);
            await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, point, pointerId: 401);
            await session.WaitForIdleAsync();
        }
        var after = AttachedDocument(session).CaptureSnapshot();
        Assert.Equal(name == "Sequence Flow" ? BpmnSemanticTypes.SequenceFlow : BpmnSemanticTypes.MessageFlow,
            Assert.Single(after.SemanticModel.Relationships).TypeId);
        await host.UndoAsync();
        A23PresentationEqual(before, AttachedDocument(session).CaptureSnapshot());
        await host.RedoAsync();
        A23PresentationEqual(after, AttachedDocument(session).CaptureSnapshot());
        Assert.False(host.CaptureState().SmartEditEnabled);
        Assert.Null(host.CaptureState().ElementToolbox);
    }
}
