using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Presentation;
using Inceptus.DocumentEngine.Bpmn.Profiles;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    private static readonly DocumentId A22ViewDocumentId = new("a22:host:document");
    private static readonly SemanticElementId A22ViewCollaborationId = new("a22:host:collaboration");
    private static readonly SemanticElementId A22ViewParticipantId = new("a22:host:participant");
    private static readonly SemanticElementId A22ViewTaskId = new("a22:host:task");
    private static readonly VisualStateId A22ViewParticipantVisualId = new("a22:host:participant-visual");
    private static readonly VisualStateId A22ViewTaskVisualId = new("a22:host:task-visual");
    private static readonly ProcessScopePresentationContext A22ViewContext = new(new SemanticModelSnapshot(A22ViewDocumentId, DocumentRevision.Zero).RootScopeId);
    private static readonly SemanticElementId A22ViewPoolId = new("a22:host:pool");

    [Fact]
    public async Task A22HostNormalProcessIncludesAssignedParticipantsWithoutIndependentBounds()
    {
        await using var host = CreatePropertiesUxHost(A22ViewSnapshot());
        await host.InitializeAsync("a22-normal", "container");
        var session = Session(host);
        var state = session.CaptureState();
        Assert.NotNull(state.CurrentScene);
        Assert.Equal(A22ViewContext, state.PresentationContext);
        Assert.Equal(6, state.ProjectedGraph!.Nodes.Length);
        Assert.True(state.PresentationPlan!.Contains(A22ViewParticipantId));
        Assert.All(state.ProjectedGraph.Nodes.Where(node => node.Source.SemanticTypeId == BpmnSemanticTypes.Participant),
            node => Assert.NotNull(node.RegionEnvelope));
        var before = AttachedDocument(session).CaptureSnapshot();
        var history = state.HistoryStatus;
        Assert.DoesNotContain(host.CapturePresentationViewChoices(), choice => choice.Context is DocumentPresentationContext);
        var rejected = await host.ChangePresentationContextAsync(new DocumentPresentationContext(A22ViewCollaborationId));
        Assert.NotNull(rejected);
        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "PRESENTATION_CONTEXT_INVALID");
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.Equal(A22ViewContext, session.CaptureState().PresentationContext);
    }

    [Fact]
    public async Task A22HostPoolTaskPropertiesUseLocalBoundsAndTypeHistoryKeepsProcessView()
    {
        await using var host = CreatePropertiesUxHost(A22ViewSnapshot());
        await host.InitializeAsync("a22-view-canvas", "a22-view-container");
        var session = Session(host);
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [A22ViewTaskVisualId]));
        await session.WaitForIdleAsync();
        var original = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(A22ViewTaskVisualId));
        Assert.Equal(new RectD(50d, 60d, 120d, 80d), original.Bounds);
        Assert.Equal(A22ViewContext, original.PresentationContext);
        var before = AttachedDocument(session).CaptureSnapshot();
        var draft = new DocumentCanvasPropertiesDraft(original) { TypeIdValue = BpmnSemanticTypes.UserTask.Value };
        Assert.True(host.UpdatePropertiesFormState(true, A22ViewTaskVisualId, draft.IsDirty,
            targetPresentationId: original.SpatialRegion?.Id));

        var result = await host.ApplyPropertiesAsync(draft);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, result.Status);
        Assert.Equal(BpmnSemanticTypes.UserTask, result.Authoritative!.TypeId);
        Assert.Equal(original.Bounds, result.Authoritative.Bounds);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), AttachedDocument(session).CaptureSnapshot().VisualModel.VisualStates);
        Assert.Equal(A22ViewContext, session.CaptureState().PresentationContext);

        await host.UndoAsync();
        Assert.True(host.TryCaptureCurrentPropertiesForm(out var undone));
        Assert.Equal(BpmnSemanticTypes.Task, undone!.TypeId);
        Assert.Equal(A22ViewContext, session.CaptureState().PresentationContext);
        await host.RedoAsync();
        Assert.True(host.TryCaptureCurrentPropertiesForm(out var redone));
        Assert.Equal(BpmnSemanticTypes.UserTask, redone!.TypeId);
        Assert.Equal(A22ViewContext, session.CaptureState().PresentationContext);
    }

    [Fact]
    public async Task A22SharedParticipantLabelAndInternalTaskHaveIndependentSelection()
    {
        await using var host = CreatePropertiesUxHost(A22ViewSnapshot());
        await host.InitializeAsync("a22-selection", "container");
        var session = Session(host);
        var scene = session.CaptureState().CurrentScene!;
        var label = A22ViewBody(scene, A22ViewParticipantId);
        Assert.Equal(Canvas2DHitTestMode.FillOrStroke, label.HitTestPolicy.Mode);
        await PointerObserver(host).ClickDocumentPointAsync(scene, Center(label.Bounds));
        await session.WaitForIdleAsync();
        Assert.Equal(A22ViewParticipantVisualId, Assert.Single(session.CaptureState().EditorState.Selection));
        scene = session.CaptureState().CurrentScene!;
        await PointerObserver(host).ClickDocumentPointAsync(scene, Center(A22ViewBody(scene, A22ViewTaskId).Bounds));
        await session.WaitForIdleAsync();
        Assert.Equal(A22ViewTaskVisualId, Assert.Single(session.CaptureState().EditorState.Selection));
    }

    [Fact]
    public async Task A22PropertiesDraftCannotCrossProcessContextAtSameRevision()
    {
        await using var host = CreatePropertiesUxHost(A22ViewSnapshot());
        await host.InitializeAsync("a22-stale-properties", "container");
        var session = Session(host);
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [A22ViewTaskVisualId]));
        await session.WaitForIdleAsync();
        var original = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(A22ViewTaskVisualId));
        var draft = new DocumentCanvasPropertiesDraft(original) { TypeIdValue = BpmnSemanticTypes.UserTask.Value };
        Assert.True(host.UpdatePropertiesFormState(false, null, false));
        Assert.True((await session.NavigateToScopeAsync(new("a22:host:peer"))).Succeeded);
        var before = AttachedDocument(session).CaptureSnapshot();
        Assert.Equal(original.Revision, before.Revision);
        var result = await host.ApplyPropertiesAsync(draft);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Unavailable, result.Status);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A22DerivedParticipantAnchorOwnerCannotBeMovedOrResized(bool resize)
    {
        await using var host = CreatePropertiesUxHost(A22ViewSnapshot());
        await host.InitializeAsync("a22-derived-geometry", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        ICommand command = resize
            ? new ResizeVisualStateCommand(before.DocumentId, before.Revision, A22ViewParticipantVisualId, new RectD(0d, 0d, 600d, 300d))
            : new MoveVisualStateCommand(before.DocumentId, before.Revision, A22ViewParticipantVisualId, new PointD(100d, 100d), VisualPlacementMode.Pinned);
        Assert.False((await session.ExecuteAsync(command)).IsCommitted);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task A22ParticipantWithoutAnchorOwnerStillProjectsAndNativeRoundTrips()
    {
        var source = A22ViewSnapshot();
        var snapshot = new DocumentSnapshot(source.SemanticModel, new VisualModelSnapshot(source.DocumentId, source.Revision,
            source.VisualModel.VisualStates.Where(visual => visual.PlacementMode != VisualPlacementMode.Derived)), source.Metadata);
        await using var host = CreatePropertiesUxHost(snapshot);
        await host.InitializeAsync("a22-semantic-participant", "container");
        var scene = Session(host).CaptureState().CurrentScene!;
        Assert.Null(A22ViewBody(scene, A22ViewParticipantId).Origin.VisualStateId);
        var saved = await host.ExportNativeDocumentAsync();
        Assert.True(saved.Succeeded);
        var imported = NativeDocumentSerializer.Import(saved.Payload.AsMemory(),
            new ElementConnectorAnchorPolicyRegistry(BpmnPluginRegistration.N100.ConnectorAnchorPolicies));
        Assert.True(imported.Succeeded);
        Assert.Equal(saved.Payload.ToArray(), NativeDocumentSerializer.Export(imported.Document!).ToArray());
        Assert.DoesNotContain(imported.Document!.VisualModel.VisualStates, visual => visual.PlacementMode == VisualPlacementMode.Derived);
    }

    private static Canvas2DSceneItem A22ViewBody(Canvas2DScene scene, SemanticElementId id) =>
        Assert.Single(scene.Items, item => item.Origin.SemanticElementId == id && Canvas2DNodeBodyMetadata.IsNodeBody(item));

    private static DocumentSnapshot A22ViewSnapshot()
    {
        var peerParticipant = new SemanticElementId("a22:host:peer-participant");
        var peerTask = new SemanticElementId("a22:host:peer-task");
        var peerPool = new SemanticElementId("a22:host:peer-pool");
        SemanticElementSnapshot Participant(SemanticElementId id, string name) => new(id, BpmnSemanticTypes.Participant,
            [new(BpmnSemanticProperties.Name, PropertyValue.FromText(name))], containmentKind: SemanticElementContainmentKind.Document);
        SemanticElementSnapshot Pool(SemanticElementId id, SemanticElementId participant, string name) => new(id, OrganizationalSemanticTypes.Pool,
            [new(OrganizationalSemanticProperties.Name, PropertyValue.FromText(name)),
                new(OrganizationalSemanticProperties.ParticipantId, PropertyValue.FromText(participant.Value))]);
        var semantic = new SemanticModelSnapshot(A22ViewDocumentId, DocumentRevision.Zero,
            [Participant(A22ViewParticipantId, "Sender"), Participant(peerParticipant, "Receiver"),
                Pool(A22ViewPoolId, A22ViewParticipantId, "Sales"), Pool(peerPool, peerParticipant, "Operations"),
                BpmnSemanticFactory.CreateTask(A22ViewTaskId, "A", "Task A", 1),
                BpmnSemanticFactory.CreateTask(peerTask, "B", "Task B", 2)],
            nestedScopes: [new DocumentScopeSnapshot(new("a22:host:peer"))],
            modelProfiles: new ModelProfileStateSnapshot([BpmnModelProfiles.OrganizationalId]),
            profileAssignments: [new(OrganizationalModelProfile.Id, A22ViewTaskId, A22ViewPoolId),
                new(OrganizationalModelProfile.Id, peerTask, peerPool)]);
        var visual = new VisualModelSnapshot(A22ViewDocumentId, DocumentRevision.Zero,
            [new(A22ViewParticipantVisualId, A22ViewParticipantId, default, default, VisualPlacementMode.Derived),
                new(new VisualStateId("a22:host:peer-participant-visual"), peerParticipant, default, default, VisualPlacementMode.Derived),
                new(A22ViewTaskVisualId, A22ViewTaskId, new PointD(50d, 60d), new SizeD(120d, 80d), VisualPlacementMode.Pinned),
                new(new VisualStateId("a22:host:peer-task-visual"), peerTask, new PointD(50d, 600d), new SizeD(120d, 80d), VisualPlacementMode.Pinned)]);
        return new DocumentSnapshot(semantic, visual, new DocumentMetadataSnapshot(A22ViewDocumentId, DocumentRevision.Zero));
    }
}
