using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Profiles;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN100OptionalProfilesCollaborationIntegrationTests
{
    private static readonly DocumentId DocumentId =
        new("bpmn:n10.0:integration");
    private static readonly DocumentScopeId MainScopeId =
        new(DocumentId.Value);
    private static readonly DocumentScopeId PeerScopeBId =
        new("bpmn:n10.0:process:b");
    private static readonly DocumentScopeId PeerScopeCId =
        new("bpmn:n10.0:process:c");
    private static readonly DocumentScopeId NestedScopeB1Id =
        new("bpmn:n10.0:process:b:nested:b1");
    private static readonly SemanticElementId SubProcessB1OwnerId =
        new("bpmn:n10.0:process:b:subprocess:b1");
    private static readonly SemanticElementId CollaborationId =
        new("bpmn:n10.0:collaboration:c1");
    private static readonly SemanticElementId MainParticipantId =
        new("bpmn:n10.0:participant:p1-main");
    private static readonly SemanticElementId PeerParticipantId =
        new("bpmn:n10.0:participant:p2-b");
    private static readonly SemanticElementId BlackBoxParticipantId =
        new("bpmn:n10.0:participant:p3-black-box");

    [Fact]
    public void ExactForestAndDocumentContainedCollaborationRoundTripWithoutFakeOwnership()
    {
        var rootTaskId = new SemanticElementId("bpmn:n10.0:process:main:task");
        var peerTaskId = new SemanticElementId("bpmn:n10.0:process:b:task");
        var nestedTaskId = new SemanticElementId("bpmn:n10.0:process:b:nested:task");
        var candidate = Snapshot(
            revision: new DocumentRevision(17),
            elements:
            [
                BpmnSemanticFactory.CreateTask(rootTaskId, "MAIN", "Main task", 1),
                BpmnSemanticFactory.CreateTask(peerTaskId, "PEER_B", "Peer B task", 2),
                BpmnSemanticFactory.CreateSubProcess(
                    SubProcessB1OwnerId,
                    "SUB_B1",
                    "SubProcess B1"),
                BpmnSemanticFactory.CreateTask(
                    nestedTaskId,
                    "NESTED_B1",
                    "Nested B1 task",
                    3),
                BpmnSemanticFactory.CreateCollaboration(
                    CollaborationId,
                    "C1",
                    "Document-level collaboration"),
                BpmnSemanticFactory.CreateParticipant(
                    MainParticipantId,
                    CollaborationId,
                    MainScopeId,
                    "Main participant"),
                BpmnSemanticFactory.CreateParticipant(
                    PeerParticipantId,
                    CollaborationId,
                    PeerScopeBId,
                    "Peer B participant"),
                BpmnSemanticFactory.CreateParticipant(
                    BlackBoxParticipantId,
                    CollaborationId,
                    name: "Black-box participant"),
            ],
            scopes:
            [
                new DocumentScopeSnapshot(PeerScopeCId),
                new DocumentScopeSnapshot(
                    NestedScopeB1Id,
                    PeerScopeBId,
                    SubProcessB1OwnerId),
                new DocumentScopeSnapshot(PeerScopeBId),
            ],
            memberships:
            [
                new SemanticElementScopeMembershipSnapshot(peerTaskId, PeerScopeBId),
                new SemanticElementScopeMembershipSnapshot(
                    SubProcessB1OwnerId,
                    PeerScopeBId),
                new SemanticElementScopeMembershipSnapshot(nestedTaskId, NestedScopeB1Id),
            ],
            profiles: new ModelProfileStateSnapshot(
                [BpmnModelProfiles.OrganizationalId]));
        var provider = Provider();

        var reconstruction = DocumentReconstructor.Reconstruct(candidate, provider);

        Assert.True(reconstruction.Succeeded, Diagnostics(reconstruction.Diagnostics));
        var document = Assert.IsType<Document>(reconstruction.Document);
        var captured = document.CaptureSnapshot();
        Assert.Equal(candidate, captured);

        var semantic = captured.SemanticModel;
        Assert.Equal(MainScopeId, semantic.RootScopeId);
        Assert.Equal(
            new[] { MainScopeId, PeerScopeBId, NestedScopeB1Id, PeerScopeCId },
            semantic.EnumerateScopeForest().Select(static scope => scope.Id));
        Assert.Null(semantic.GetRootScope().ParentScopeId);
        Assert.Null(semantic.GetRootScope().OwnerSemanticElementId);

        var peerB = Assert.Single(semantic.NestedScopes, scope => scope.Id == PeerScopeBId);
        Assert.Null(peerB.ParentScopeId);
        Assert.Null(peerB.OwnerSemanticElementId);
        Assert.True(semantic.IsExplicitPeerRoot(PeerScopeBId));
        var peerC = Assert.Single(semantic.NestedScopes, scope => scope.Id == PeerScopeCId);
        Assert.Null(peerC.ParentScopeId);
        Assert.Null(peerC.OwnerSemanticElementId);
        Assert.True(semantic.IsExplicitPeerRoot(PeerScopeCId));

        var nested = Assert.Single(
            semantic.NestedScopes,
            scope => scope.Id == NestedScopeB1Id);
        Assert.Equal(PeerScopeBId, nested.ParentScopeId);
        Assert.Equal(SubProcessB1OwnerId, nested.OwnerSemanticElementId);
        Assert.Equal(PeerScopeBId, semantic.GetScope(SubProcessB1OwnerId).Id);
        Assert.Equal(MainScopeId, semantic.GetScope(rootTaskId).Id);
        Assert.Equal(PeerScopeBId, semantic.GetScope(peerTaskId).Id);
        Assert.Equal(NestedScopeB1Id, semantic.GetScope(nestedTaskId).Id);

        AssertDocumentContainedWithoutMembership(semantic, CollaborationId);
        AssertDocumentContainedWithoutMembership(semantic, MainParticipantId);
        AssertDocumentContainedWithoutMembership(semantic, PeerParticipantId);
        AssertDocumentContainedWithoutMembership(semantic, BlackBoxParticipantId);
        AssertParticipantReference(semantic, MainParticipantId, MainScopeId);
        AssertParticipantReference(semantic, PeerParticipantId, PeerScopeBId);
        AssertParticipantReference(semantic, BlackBoxParticipantId, expectedScopeId: null);

        var second = DocumentReconstructor.Reconstruct(captured, provider);
        Assert.True(second.Succeeded, Diagnostics(second.Diagnostics));
        Assert.Equal(captured, Assert.IsType<Document>(second.Document).CaptureSnapshot());
    }

    [Fact]
    public async Task UnavailableProfilesLeaveMultiProcessEditingBoundaryEventsAndHistoryIndependent()
    {
        var provider = Provider();
        var document = CreateEmptyDocument(provider);
        var (processor, history) = Processing(document, provider);
        var rootTaskId = new SemanticElementId("bpmn:n10.0:edit:main-task");
        var rootTaskVisualId = new VisualStateId("bpmn:n10.0:edit:main-task:visual");
        var peerTaskId = new SemanticElementId("bpmn:n10.0:edit:peer-b-task");
        var peerTaskVisualId = new VisualStateId("bpmn:n10.0:edit:peer-b-task:visual");
        var peerCTaskId = new SemanticElementId("bpmn:n10.0:edit:peer-c-task");
        var peerCTaskVisualId = new VisualStateId("bpmn:n10.0:edit:peer-c-task:visual");
        var rootSourceAnchorId = new ConnectorAnchorId(
            "bpmn:n10.0:edit:main-task:right:source");
        var peerTargetAnchorId = new ConnectorAnchorId(
            "bpmn:n10.0:edit:peer-b-task:left:target");
        var subProcessId = new SemanticElementId("bpmn:n10.0:edit:peer-b-subprocess");
        var subProcessVisualId = new VisualStateId(
            "bpmn:n10.0:edit:peer-b-subprocess:visual");
        var nestedScopeId = new DocumentScopeId(
            "bpmn:n10.0:edit:peer-b-subprocess:scope");
        var timerId = new SemanticElementId("bpmn:n10.0:edit:peer-b-timer");
        var timerVisualId = new VisualStateId("bpmn:n10.0:edit:peer-b-timer:visual");

        Assert.Empty(document.SemanticModel.ModelProfiles.AvailableProfileIds);
        await ExecuteCommittedAsync(
            document,
            processor,
            history,
            revision => new CreateTopLevelDocumentScopeCommand(
                DocumentId,
                revision,
                PeerScopeBId));
        await ExecuteCommittedAsync(
            document,
            processor,
            history,
            revision => new CreateTopLevelDocumentScopeCommand(
                DocumentId,
                revision,
                PeerScopeCId));
        await ExecuteCommittedAsync(
            document,
            processor,
            history,
            revision => TaskCommand(
                revision,
                rootTaskId,
                rootTaskVisualId,
                "MAIN",
                10,
                targetScopeId: null));
        await ExecuteCommittedAsync(
            document,
            processor,
            history,
            revision => TaskCommand(
                revision,
                peerTaskId,
                peerTaskVisualId,
                "PEER_B",
                11,
                PeerScopeBId));
        await ExecuteCommittedAsync(
            document,
            processor,
            history,
            revision => TaskCommand(
                revision,
                peerCTaskId,
                peerCTaskVisualId,
                "PEER_C",
                12,
                PeerScopeCId));
        await ExecuteCommittedAsync(
            document,
            processor,
            history,
            revision => new AddConnectorAnchorCommand(
                DocumentId,
                revision,
                rootTaskVisualId,
                rootSourceAnchorId,
                ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Source,
                insertionIndex: 0));
        await ExecuteCommittedAsync(
            document,
            processor,
            history,
            revision => new AddConnectorAnchorCommand(
                DocumentId,
                revision,
                peerTaskVisualId,
                peerTargetAnchorId,
                ConnectorAnchorSide.Left,
                ConnectorAnchorRole.Target,
                insertionIndex: 0));
        await ExecuteCommittedAsync(
            document,
            processor,
            history,
            revision => new CreateBpmnSubProcessCommand(
                DocumentId,
                revision,
                subProcessId,
                subProcessVisualId,
                PeerScopeBId,
                nestedScopeId,
                new PointD(460d, 180d),
                new SizeD(120d, 80d),
                "SUB_B",
                "Peer B SubProcess",
                VisualPlacementMode.Pinned));
        await ExecuteCommittedAsync(
            document,
            processor,
            history,
            revision => new CreateBpmnTimerBoundaryEventCommand(
                DocumentId,
                revision,
                timerId,
                timerVisualId,
                peerTaskId,
                BoundaryAttachmentSide.Bottom,
                0.5d,
                new RectD(240d, 160d, 120d, 80d),
                "Peer timer",
                "PT5M",
                targetScopeId: PeerScopeBId));

        var committed = document.CaptureSnapshot();
        Assert.Equal(new DocumentRevision(9), committed.Revision);
        Assert.Equal(new HistoryStatus(9, canUndo: true, canRedo: false),
            history.CaptureStatus());
        Assert.Empty(committed.SemanticModel.ModelProfiles.AvailableProfileIds);
        Assert.Equal(MainScopeId, committed.SemanticModel.GetScope(rootTaskId).Id);
        Assert.Equal(PeerScopeBId, committed.SemanticModel.GetScope(peerTaskId).Id);
        Assert.Equal(PeerScopeCId, committed.SemanticModel.GetScope(peerCTaskId).Id);
        Assert.Equal(PeerScopeBId, committed.SemanticModel.GetScope(subProcessId).Id);
        Assert.Equal(PeerScopeBId, committed.SemanticModel.GetScope(timerId).Id);
        Assert.Equal(peerTaskId,
            committed.SemanticModel.Elements.Single(element => element.Id == timerId)
                .AttachedToElementId);
        var nested = Assert.Single(
            committed.SemanticModel.NestedScopes,
            scope => scope.Id == nestedScopeId);
        Assert.Equal(PeerScopeBId, nested.ParentScopeId);
        Assert.Equal(subProcessId, nested.OwnerSemanticElementId);

        var projection = new ProjectionEngine(BpmnPluginRegistration.N100.ProjectionRules);
        AssertProjectedElements(projection, committed, MainScopeId, [rootTaskId]);
        AssertProjectedElements(
            projection,
            committed,
            PeerScopeBId,
            [peerTaskId, subProcessId, timerId]);
        AssertProjectedElements(projection, committed, PeerScopeCId, [peerCTaskId]);

        var beforeCrossRoot = document.CaptureSnapshot();
        var historyBeforeCrossRoot = history.CaptureStatus();
        var rejected = await history.ExecuteAsync(
            processor,
            new CreateBpmnSequenceFlowCommand(
                DocumentId,
                document.Revision,
                new SemanticElementId("bpmn:n10.0:edit:cross-root-flow"),
                new VisualStateId("bpmn:n10.0:edit:cross-root-flow:visual"),
                rootTaskId,
                peerTaskId,
                rootSourceAnchorId,
                peerTargetAnchorId));
        Assert.False(rejected.IsCommitted);
        Assert.Contains(rejected.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.SequenceFlowCrossesScope);
        Assert.Same(beforeCrossRoot, document.CaptureSnapshot());
        Assert.Equal(historyBeforeCrossRoot, history.CaptureStatus());

        var undo = await history.UndoAsync(processor);
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        Assert.False(document.SemanticModel.TryGetElement(timerId, out _));
        Assert.True(document.SemanticModel.TryGetElement(subProcessId, out _));
        var redo = await history.RedoAsync(processor);
        Assert.True(redo.IsCommitted, Diagnostics(redo.Diagnostics));
        Assert.Equal(PeerScopeBId, document.SemanticModel.GetScope(timerId).Id);
    }

    [Fact]
    public async Task AvailabilityAndOrganizationalDeletionAreNonDestructiveToReferencedProcesses()
    {
        var provider = Provider();
        var candidate = Snapshot(
            revision: DocumentRevision.Zero,
            elements:
            [
                BpmnSemanticFactory.CreateCollaboration(CollaborationId, "C1"),
                BpmnSemanticFactory.CreateParticipant(
                    MainParticipantId,
                    CollaborationId,
                    MainScopeId,
                    "Main"),
                BpmnSemanticFactory.CreateParticipant(
                    PeerParticipantId,
                    CollaborationId,
                    PeerScopeBId,
                    "Peer B"),
                BpmnSemanticFactory.CreateParticipant(
                    BlackBoxParticipantId,
                    CollaborationId,
                    name: "Black box"),
            ],
            scopes: [new DocumentScopeSnapshot(PeerScopeBId)],
            profiles: new ModelProfileStateSnapshot(
                [BpmnModelProfiles.OrganizationalId]));
        var reconstruction = DocumentReconstructor.Reconstruct(candidate, provider);
        Assert.True(reconstruction.Succeeded, Diagnostics(reconstruction.Diagnostics));
        var document = Assert.IsType<Document>(reconstruction.Document);
        var (processor, history) = Processing(document, provider);

        await ExecuteCommittedAsync(
            document,
            processor,
            history,
            revision => new SetModelProfileAvailabilityCommand(
                DocumentId,
                revision,
                [new ModelProfileAvailabilityChange(
                    BpmnModelProfiles.OrganizationalId,
                    isAvailable: false)]));
        var disabled = document.CaptureSnapshot();
        Assert.False(disabled.SemanticModel.ModelProfiles.IsAvailable(
            BpmnModelProfiles.OrganizationalId));
        AssertOrganizationalIdentitiesAndReferencesExact(disabled.SemanticModel);
        Assert.True(disabled.SemanticModel.IsExplicitPeerRoot(PeerScopeBId));
        var disabledRoundTrip = DocumentReconstructor.Reconstruct(disabled, provider);
        Assert.True(
            disabledRoundTrip.Succeeded,
            Diagnostics(disabledRoundTrip.Diagnostics));
        var reconstructedDisabled = Assert.IsType<Document>(disabledRoundTrip.Document)
            .CaptureSnapshot();
        Assert.Equal(disabled, reconstructedDisabled);
        Assert.False(reconstructedDisabled.SemanticModel.ModelProfiles.IsAvailable(
            BpmnModelProfiles.OrganizationalId));
        AssertOrganizationalIdentitiesAndReferencesExact(
            reconstructedDisabled.SemanticModel);

        await ExecuteCommittedAsync(
            document,
            processor,
            history,
            revision => new SetModelProfileAvailabilityCommand(
                DocumentId,
                revision,
                [new ModelProfileAvailabilityChange(
                    BpmnModelProfiles.OrganizationalId,
                    isAvailable: true)]));
        var enabledAgain = document.CaptureSnapshot();
        Assert.True(enabledAgain.SemanticModel.ModelProfiles.IsAvailable(
            BpmnModelProfiles.OrganizationalId));
        AssertOrganizationalIdentitiesAndReferencesExact(enabledAgain.SemanticModel);

        var beforeParticipantDelete = document.CaptureSnapshot();
        await ExecuteCommittedAsync(
            document,
            processor,
            history,
            revision => new DeleteBpmnParticipantCommand(
                DocumentId,
                revision,
                PeerParticipantId));
        Assert.False(document.SemanticModel.TryGetElement(PeerParticipantId, out _));
        Assert.True(document.SemanticModel.IsExplicitPeerRoot(PeerScopeBId));
        var participantUndo = await history.UndoAsync(processor);
        Assert.True(participantUndo.IsCommitted, Diagnostics(participantUndo.Diagnostics));
        AssertAuthoritativeContentEqual(
            beforeParticipantDelete,
            document.CaptureSnapshot());

        var beforeCollaborationDelete = document.CaptureSnapshot();
        await ExecuteCommittedAsync(
            document,
            processor,
            history,
            revision => new DeleteBpmnCollaborationCommand(
                DocumentId,
                revision,
                CollaborationId));
        Assert.DoesNotContain(document.SemanticModel.Elements, element =>
            element.Id == CollaborationId ||
            element.Id == MainParticipantId ||
            element.Id == PeerParticipantId ||
            element.Id == BlackBoxParticipantId);
        Assert.True(document.SemanticModel.IsExplicitPeerRoot(PeerScopeBId));

        var collaborationUndo = await history.UndoAsync(processor);
        Assert.True(collaborationUndo.IsCommitted, Diagnostics(collaborationUndo.Diagnostics));
        AssertAuthoritativeContentEqual(
            beforeCollaborationDelete,
            document.CaptureSnapshot());
        AssertOrganizationalIdentitiesAndReferencesExact(
            document.CaptureSnapshot().SemanticModel);
    }

    [Fact]
    public async Task EditingSessionKeepsVisibilityTransientScopeKeyedAndRetainsProfileData()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var renderer = await CreateRendererAsync("phase-n100-profile-view-state");
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        Assert.Equal(BpmnModelProfiles.Definitions.Length,
            session.ModelProfileCatalog.Definitions.Length);

        var rootScopeId = composition.Document.SemanticModel.RootScopeId;
        var initial = session.CaptureState();
        Assert.Empty(initial.ModelProfileState.AvailableProfileIds);
        Assert.NotNull(initial.ProjectedGraph);
        Assert.NotEmpty(initial.ProjectedGraph!.Edges);
        Assert.NotNull(initial.LayoutResult);
        Assert.NotNull(initial.RoutingResult);
        var taskVisual = composition.Document.VisualModel.VisualStates.Single(visual =>
            visual.Id == BpmnDemoPipeline.TaskVisualId);
        await ExecuteSessionCommandAsync(
            session,
            new MoveVisualStateCommand(
                composition.Document.DocumentId,
                composition.Document.Revision,
                taskVisual.Id,
                new PointD(taskVisual.Position.X + 24d, taskVisual.Position.Y + 12d),
                VisualPlacementMode.Pinned));
        var movedWithoutProfiles = session.CaptureState();
        Assert.Equal(initial.DocumentRevision.Increment(),
            movedWithoutProfiles.DocumentRevision);
        Assert.Equal(initial.HistoryStatus.EntryCount + 1,
            movedWithoutProfiles.HistoryStatus.EntryCount);
        Assert.NotEmpty(movedWithoutProfiles.ProjectedGraph!.Edges);
        Assert.NotEmpty(movedWithoutProfiles.RoutingResult!.Routes);
        var validationIssues = new BpmnStructuralValidationRule().Validate(
            new ModelValidationContext(
                composition.Document.CaptureSnapshot(),
                rootScopeId));
        Assert.DoesNotContain(validationIssues, issue =>
            issue.Severity == ModelValidationSeverity.Error);

        var beforeRootVisibility = composition.Document.CaptureSnapshot();
        var beforeRootVisibilityState = session.CaptureState();
        var rootHidden = new ModelProfileViewStateSnapshot(
            [BpmnModelProfiles.OrganizationalId]);
        var visibility = await session.UpdateModelProfileViewStateAsync(rootHidden);
        Assert.True(visibility.Succeeded, Diagnostics(visibility.Diagnostics));
        var rootAfterVisibility = session.CaptureState();
        Assert.Same(beforeRootVisibility, composition.Document.CaptureSnapshot());
        Assert.Equal(
            beforeRootVisibilityState.HistoryStatus,
            rootAfterVisibility.HistoryStatus);
        Assert.Equal(
            beforeRootVisibilityState.Generation.Value + 1,
            rootAfterVisibility.Generation.Value);
        Assert.Same(beforeRootVisibilityState.ProjectedGraph,
            rootAfterVisibility.ProjectedGraph);
        Assert.Same(beforeRootVisibilityState.LayoutResult,
            rootAfterVisibility.LayoutResult);
        Assert.Same(beforeRootVisibilityState.RoutingResult,
            rootAfterVisibility.RoutingResult);
        Assert.False(rootAfterVisibility.IsModelProfileEffectivelyVisible(
            BpmnModelProfiles.OrganizationalId));

        await ExecuteSessionCommandAsync(
            session,
            new SetModelProfileAvailabilityCommand(
                composition.Document.DocumentId,
                composition.Document.Revision,
                [
                    new ModelProfileAvailabilityChange(
                        BpmnModelProfiles.OrganizationalId,
                        isAvailable: true),
                    new ModelProfileAvailabilityChange(
                        BpmnModelProfiles.StageId,
                        isAvailable: true),
                ]));
        var available = session.CaptureState();
        Assert.False(available.IsModelProfileEffectivelyVisible(
            BpmnModelProfiles.OrganizationalId));
        Assert.True(available.IsModelProfileEffectivelyVisible(BpmnModelProfiles.StageId));
        AssertPipelineGeometryEqual(rootAfterVisibility, available);

        var peerScopeId = new DocumentScopeId("bpmn:n10.0:view:peer-process");
        await ExecuteSessionCommandAsync(
            session,
            new CreateTopLevelDocumentScopeCommand(
                composition.Document.DocumentId,
                composition.Document.Revision,
                peerScopeId));
        Assert.True((await session.NavigateToScopeAsync(peerScopeId)).Succeeded);
        var peerInitial = session.CaptureState();
        Assert.True(peerInitial.IsModelProfileEffectivelyVisible(
            BpmnModelProfiles.OrganizationalId));
        Assert.True(peerInitial.IsModelProfileEffectivelyVisible(BpmnModelProfiles.StageId));

        var beforePeerVisibility = composition.Document.CaptureSnapshot();
        var historyBeforePeerVisibility = peerInitial.HistoryStatus;
        var generationBeforePeerVisibility = peerInitial.Generation;
        var peerHidden = new ModelProfileViewStateSnapshot([BpmnModelProfiles.StageId]);
        Assert.True((await session.UpdateModelProfileViewStateAsync(peerHidden)).Succeeded);
        var peerAfterVisibility = session.CaptureState();
        Assert.Same(beforePeerVisibility, composition.Document.CaptureSnapshot());
        Assert.Equal(historyBeforePeerVisibility, peerAfterVisibility.HistoryStatus);
        Assert.Equal(
            generationBeforePeerVisibility.Value + 1,
            peerAfterVisibility.Generation.Value);
        Assert.Same(peerInitial.ProjectedGraph, peerAfterVisibility.ProjectedGraph);
        Assert.Same(peerInitial.LayoutResult, peerAfterVisibility.LayoutResult);
        Assert.Same(peerInitial.RoutingResult, peerAfterVisibility.RoutingResult);
        Assert.True(peerAfterVisibility.IsModelProfileEffectivelyVisible(
            BpmnModelProfiles.OrganizationalId));
        Assert.False(peerAfterVisibility.IsModelProfileEffectivelyVisible(
            BpmnModelProfiles.StageId));

        await ExecuteSessionCommandAsync(
            session,
            new CreateBpmnCollaborationCommand(
                composition.Document.DocumentId,
                composition.Document.Revision,
                CollaborationId,
                "Retained C1"));
        await ExecuteSessionCommandAsync(
            session,
            new CreateBpmnParticipantCommand(
                composition.Document.DocumentId,
                composition.Document.Revision,
                PeerParticipantId,
                CollaborationId,
                peerScopeId,
                "Retained participant"));
        var organizationalBeforeDisable = composition.Document.CaptureSnapshot();
        var pipelineBeforeDisable = session.CaptureState();

        await ExecuteSessionCommandAsync(
            session,
            new SetModelProfileAvailabilityCommand(
                composition.Document.DocumentId,
                composition.Document.Revision,
                [new ModelProfileAvailabilityChange(
                    BpmnModelProfiles.OrganizationalId,
                    isAvailable: false)]));
        Assert.True(composition.Document.SemanticModel.TryGetElement(
            CollaborationId,
            out _));
        Assert.True(composition.Document.SemanticModel.TryGetElement(
            PeerParticipantId,
            out _));
        Assert.False(session.CaptureState().IsModelProfileEffectivelyVisible(
            BpmnModelProfiles.OrganizationalId));
        var disabledState = session.CaptureState();
        AssertPipelineGeometryEqual(pipelineBeforeDisable, disabledState);

        await ExecuteSessionCommandAsync(
            session,
            new SetModelProfileAvailabilityCommand(
                composition.Document.DocumentId,
                composition.Document.Revision,
                [new ModelProfileAvailabilityChange(
                    BpmnModelProfiles.OrganizationalId,
                    isAvailable: true)]));
        var peerReenabled = session.CaptureState();
        Assert.True(peerReenabled.IsModelProfileEffectivelyVisible(
            BpmnModelProfiles.OrganizationalId));
        Assert.False(peerReenabled.IsModelProfileEffectivelyVisible(
            BpmnModelProfiles.StageId));
        AssertPipelineGeometryEqual(disabledState, peerReenabled);
        Assert.Equal(
            organizationalBeforeDisable.SemanticModel.Elements.AsEnumerable(),
            composition.Document.SemanticModel.Elements.AsEnumerable());

        var beforeReturnToRoot = composition.Document.CaptureSnapshot();
        var historyBeforeReturnToRoot = session.CaptureState().HistoryStatus;
        Assert.True((await session.NavigateToScopeAsync(rootScopeId)).Succeeded);
        var rootRestored = session.CaptureState();
        Assert.Same(beforeReturnToRoot, composition.Document.CaptureSnapshot());
        Assert.Equal(historyBeforeReturnToRoot.EntryCount + 1,
            rootRestored.HistoryStatus.EntryCount);
        Assert.False(rootRestored.IsModelProfileEffectivelyVisible(
            BpmnModelProfiles.OrganizationalId));
        Assert.True(rootRestored.IsModelProfileEffectivelyVisible(BpmnModelProfiles.StageId));

        var undoNavigation = await session.UndoAsync();
        Assert.True(undoNavigation.IsApplied, Diagnostics(undoNavigation.Diagnostics));
        Assert.False(undoNavigation.IsCommitted);
        Assert.Equal(peerScopeId, session.CaptureState().ActiveScopeId);
        Assert.Same(beforeReturnToRoot, composition.Document.CaptureSnapshot());

        var undoAvailability = await session.UndoAsync();
        Assert.True(undoAvailability.IsCommitted, Diagnostics(undoAvailability.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(composition.Document.SemanticModel.ModelProfiles.IsAvailable(
            BpmnModelProfiles.OrganizationalId));
        Assert.True(composition.Document.SemanticModel.TryGetElement(CollaborationId, out _));
        Assert.True(composition.Document.SemanticModel.TryGetElement(PeerParticipantId, out _));

        var redoAvailability = await session.RedoAsync();
        Assert.True(redoAvailability.IsCommitted, Diagnostics(redoAvailability.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        var redoNavigation = await session.RedoAsync();
        Assert.True(redoNavigation.IsApplied, Diagnostics(redoNavigation.Diagnostics));
        Assert.False(redoNavigation.IsCommitted);
        Assert.Equal(rootScopeId, session.CaptureState().ActiveScopeId);
        Assert.True(composition.Document.SemanticModel.ModelProfiles.IsAvailable(
            BpmnModelProfiles.OrganizationalId));
    }

    private static DocumentSnapshot Snapshot(
        DocumentRevision revision,
        IEnumerable<SemanticElementSnapshot>? elements = null,
        IEnumerable<DocumentScopeSnapshot>? scopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null,
        ModelProfileStateSnapshot? profiles = null) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                elements,
                relationships: null,
                scopes,
                memberships,
                profiles),
            new VisualModelSnapshot(DocumentId, revision),
            new DocumentMetadataSnapshot(DocumentId, revision));

    private static ElementConnectorAnchorPolicyRegistry Provider()
    {
        var registration = BpmnPluginRegistration.N100;
        return new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
    }

    private static Document CreateEmptyDocument(
        IElementConnectorAnchorPolicyProvider provider)
    {
        var creation = DocumentFactory.CreateEmpty(
            DocumentId,
            connectorAnchorPolicyProvider: provider);
        Assert.True(creation.Succeeded, Diagnostics(creation.Diagnostics));
        return Assert.IsType<Document>(creation.Document);
    }

    private static (CommandProcessor Processor, HistoryManager History) Processing(
        Document document,
        IElementConnectorAnchorPolicyProvider provider)
    {
        var registration = BpmnPluginRegistration.N100;
        return (
            new CommandProcessor(
                registration.CommandHandlers,
                registration.CommandValidators,
                historyPolicies: registration.HistoryPolicies,
                connectorAnchorPolicyProvider: provider),
            new HistoryManager(document));
    }

    private static CreateBpmnTaskCommand TaskCommand(
        DocumentRevision revision,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        string code,
        long elementNumber,
        DocumentScopeId? targetScopeId) =>
        new(
            DocumentId,
            revision,
            elementId,
            visualStateId,
            new PointD(240d, 160d),
            new SizeD(120d, 80d),
            code,
            $"{code} task",
            elementNumber,
            VisualPlacementMode.Pinned,
            targetScopeId: targetScopeId);

    private static async ValueTask ExecuteCommittedAsync(
        Document document,
        CommandProcessor processor,
        HistoryManager history,
        Func<DocumentRevision, ICommand> commandFactory)
    {
        var result = await history.ExecuteAsync(
            processor,
            commandFactory(document.Revision));
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
    }

    private static async ValueTask ExecuteSessionCommandAsync(
        EditingSession session,
        ICommand command)
    {
        var result = await session.ExecuteAsync(command);
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        var state = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        Assert.Empty(state.RuntimeDiagnostics.Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error));
    }

    private static void AssertProjectedElements(
        ProjectionEngine projection,
        DocumentSnapshot snapshot,
        DocumentScopeId scopeId,
        IEnumerable<SemanticElementId> expectedElementIds)
    {
        var execution = projection.Project(snapshot, scopeId);
        Assert.True(execution.IsSuccessful, Diagnostics(execution.Diagnostics));
        var graph = Assert.IsType<ProjectedGraph>(execution.Graph);
        Assert.Equal(
            expectedElementIds.OrderBy(static id => id.Value),
            graph.Nodes.Select(static node => node.Source.SemanticElementId)
                .OrderBy(static id => id.Value));
        Assert.DoesNotContain(graph.Nodes, node =>
            node.Source.SemanticElementId == CollaborationId ||
            node.Source.SemanticElementId == MainParticipantId ||
            node.Source.SemanticElementId == PeerParticipantId ||
            node.Source.SemanticElementId == BlackBoxParticipantId);
    }

    private static void AssertDocumentContainedWithoutMembership(
        SemanticModelSnapshot semanticModel,
        SemanticElementId elementId)
    {
        var element = Assert.Single(
            semanticModel.Elements,
            candidate => candidate.Id == elementId);
        Assert.Equal(SemanticElementContainmentKind.Document, element.ContainmentKind);
        Assert.False(semanticModel.TryGetScope(elementId, out _));
        Assert.DoesNotContain(
            semanticModel.ScopeMemberships,
            membership => membership.SemanticElementId == elementId);
    }

    private static void AssertParticipantReference(
        SemanticModelSnapshot semanticModel,
        SemanticElementId participantId,
        DocumentScopeId? expectedScopeId)
    {
        var participant = Assert.Single(
            semanticModel.Elements,
            element => element.Id == participantId);
        Assert.True(BpmnCollaborationSemantics.TryGetCollaborationId(
            participant,
            out var collaborationId));
        Assert.Equal(CollaborationId, collaborationId);
        Assert.True(BpmnCollaborationSemantics.TryGetProcessScopeId(
            participant,
            out var processScopeId));
        Assert.Equal(expectedScopeId, processScopeId);
    }

    private static void AssertOrganizationalIdentitiesAndReferencesExact(
        SemanticModelSnapshot semanticModel)
    {
        AssertDocumentContainedWithoutMembership(semanticModel, CollaborationId);
        AssertDocumentContainedWithoutMembership(semanticModel, MainParticipantId);
        AssertDocumentContainedWithoutMembership(semanticModel, PeerParticipantId);
        AssertDocumentContainedWithoutMembership(semanticModel, BlackBoxParticipantId);
        AssertParticipantReference(semanticModel, MainParticipantId, MainScopeId);
        AssertParticipantReference(semanticModel, PeerParticipantId, PeerScopeBId);
        AssertParticipantReference(semanticModel, BlackBoxParticipantId, expectedScopeId: null);
    }

    private static void AssertAuthoritativeContentEqual(
        DocumentSnapshot expected,
        DocumentSnapshot actual)
    {
        Assert.Equal(
            expected.SemanticModel.Elements.AsEnumerable(),
            actual.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            expected.SemanticModel.Relationships.AsEnumerable(),
            actual.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(
            expected.SemanticModel.NestedScopes.AsEnumerable(),
            actual.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(
            expected.SemanticModel.ScopeMemberships.AsEnumerable(),
            actual.SemanticModel.ScopeMemberships.AsEnumerable());
        Assert.Equal(expected.SemanticModel.ModelProfiles, actual.SemanticModel.ModelProfiles);
        Assert.Equal(
            expected.VisualModel.VisualStates.AsEnumerable(),
            actual.VisualModel.VisualStates.AsEnumerable());
    }

    private static void AssertPipelineGeometryEqual(
        EditingSessionState expected,
        EditingSessionState actual)
    {
        var expectedGraph = Assert.IsType<ProjectedGraph>(expected.ProjectedGraph);
        var actualGraph = Assert.IsType<ProjectedGraph>(actual.ProjectedGraph);
        Assert.Equal(expectedGraph.Nodes.AsEnumerable(), actualGraph.Nodes.AsEnumerable());
        Assert.Equal(expectedGraph.Edges.AsEnumerable(), actualGraph.Edges.AsEnumerable());
        Assert.Equal(expectedGraph.Groups.AsEnumerable(), actualGraph.Groups.AsEnumerable());
        Assert.Equal(expectedGraph.Ports.AsEnumerable(), actualGraph.Ports.AsEnumerable());
        Assert.Equal(expectedGraph.Labels.AsEnumerable(), actualGraph.Labels.AsEnumerable());

        var expectedLayout = Assert.IsType<Contracts.Layout.LayoutResult>(
            expected.LayoutResult);
        var actualLayout = Assert.IsType<Contracts.Layout.LayoutResult>(actual.LayoutResult);
        Assert.Equal(expectedLayout.Nodes.AsEnumerable(), actualLayout.Nodes.AsEnumerable());
        Assert.Equal(expectedLayout.Groups.AsEnumerable(), actualLayout.Groups.AsEnumerable());

        var expectedRouting = Assert.IsType<Contracts.Routing.RoutingResult>(
            expected.RoutingResult);
        var actualRouting = Assert.IsType<Contracts.Routing.RoutingResult>(
            actual.RoutingResult);
        Assert.Equal(expectedRouting.Routes.AsEnumerable(), actualRouting.Routes.AsEnumerable());
        Assert.Equal(
            expectedRouting.NoRouteEdgeIds.AsEnumerable(),
            actualRouting.NoRouteEdgeIds.AsEnumerable());
    }

    private static async ValueTask<Canvas2DRenderer> CreateRendererAsync(string canvasId)
    {
        var renderer = new Canvas2DRenderer(
            new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "org.dejavu.DejaVuSans",
                        "2.37",
                        "DejaVu Sans",
                        "fonts/DejaVuSans-2.37.ttf"),
                ],
                defaultFontFamily: "DejaVu Sans"));
        var initialization = await renderer.InitializeAsync(
            canvasId,
            new Canvas2DSurfaceSize(1200d, 800d, 1d));
        Assert.True(initialization.Succeeded, Diagnostics(initialization.Diagnostics));
        return renderer;
    }

    private static string Diagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(
            Environment.NewLine,
            diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}"));
}
