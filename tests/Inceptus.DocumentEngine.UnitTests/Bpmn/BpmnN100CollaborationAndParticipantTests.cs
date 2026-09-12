using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.ContextMenus;
using Inceptus.DocumentEngine.Bpmn.Profiles;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ContextMenus;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN100CollaborationAndParticipantTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n10:document");
    private static readonly SemanticElementId CollaborationId =
        new("bpmn:n10:collaboration");
    private static readonly SemanticElementId ParticipantId =
        new("bpmn:n10:participant");
    private static readonly DocumentScopeId PeerScopeId =
        new("bpmn:n10:process:peer");

    [Fact]
    public void N100ContributesStableProfilesTypesAndAddProcessAction()
    {
        Assert.Equal("bpmn:profile:organizational", BpmnModelProfiles.OrganizationalId.Value);
        Assert.Equal("bpmn:profile:stage", BpmnModelProfiles.StageId.Value);
        Assert.Equal("Organizational profile", BpmnModelProfiles.Organizational.DisplayName);
        Assert.Equal("Stage profile", BpmnModelProfiles.Stage.DisplayName);
        Assert.Equal("BPMN.Collaboration", BpmnSemanticTypes.Collaboration.Value);
        Assert.Equal("BPMN.Participant", BpmnSemanticTypes.Participant.Value);
        Assert.False(BpmnSemanticTypes.IsFlowNode(BpmnSemanticTypes.Collaboration));
        Assert.False(BpmnSemanticTypes.IsFlowNode(BpmnSemanticTypes.Participant));

        var registration = BpmnPluginRegistration.N100;
        Assert.Equal(BpmnModelProfiles.Definitions, registration.ModelProfileDefinitions);
        var action = Assert.Single(registration.BackgroundActions);
        Assert.Equal(BpmnCanvasBackgroundActions.AddProcessId, action.Id);
        Assert.Equal("Add", action.GroupLabel);
        Assert.Equal("Process", action.DisplayName);

        var snapshot = Snapshot(organizationalAvailable: false);
        var allocatedScopeId = new DocumentScopeId("bpmn:n10:allocated-process");
        var identityProvider = new FixedScopeIdentityProvider(allocatedScopeId);
        var plan = action.CreatePlan(new CanvasBackgroundActionRequest(
            snapshot,
            snapshot.SemanticModel.RootScopeId,
            identityProvider));
        var command = Assert.IsType<CreateTopLevelDocumentScopeCommand>(plan.Command);
        Assert.Equal(snapshot.DocumentId, command.TargetDocumentId);
        Assert.Equal(snapshot.Revision, command.ExpectedRevision);
        Assert.Equal(allocatedScopeId, command.ScopeId);
        Assert.Equal(allocatedScopeId, plan.NavigateToScopeId);
        Assert.Equal(1, identityProvider.ScopeIdCallCount);
    }

    [Fact]
    public void FactoriesCreateDocumentContainedCollaborationAndBlackOrWhiteBoxParticipants()
    {
        var collaboration = BpmnSemanticFactory.CreateCollaboration(
            CollaborationId,
            "Logistics",
            "Cross-company fulfillment.");
        var blackBox = BpmnSemanticFactory.CreateParticipant(
            ParticipantId,
            CollaborationId,
            name: "Customer");
        var whiteBox = BpmnSemanticFactory.CreateParticipant(
            new SemanticElementId("bpmn:n10:participant:white"),
            CollaborationId,
            PeerScopeId,
            "Carrier");

        Assert.Equal(SemanticElementContainmentKind.Document, collaboration.ContainmentKind);
        Assert.Equal(SemanticElementContainmentKind.Document, blackBox.ContainmentKind);
        Assert.True(BpmnCollaborationSemantics.TryGetCollaborationId(
            blackBox,
            out var blackBoxCollaborationId));
        Assert.Equal(CollaborationId, blackBoxCollaborationId);
        Assert.True(BpmnCollaborationSemantics.TryGetProcessScopeId(
            blackBox,
            out var blackBoxProcessScopeId));
        Assert.Null(blackBoxProcessScopeId);
        Assert.True(BpmnCollaborationSemantics.TryGetProcessScopeId(
            whiteBox,
            out var whiteBoxProcessScopeId));
        Assert.Equal(PeerScopeId, whiteBoxProcessScopeId);
        Assert.DoesNotContain(BpmnSemanticProperties.Code, collaboration.Properties.Keys);
        Assert.DoesNotContain(BpmnSemanticProperties.ElementNumber, blackBox.Properties.Keys);
    }

    [Fact]
    public async Task OrganizationalCommandsRejectWhileProfileUnavailableWithoutResidue()
    {
        var harness = CreateHarness(Snapshot(organizationalAvailable: false));
        var before = harness.Document.CaptureSnapshot();
        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            new CreateBpmnCollaborationCommand(
                before.DocumentId,
                before.Revision,
                CollaborationId,
                "Unavailable"));

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.OrganizationalProfileUnavailable);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task GenericEditsCannotBypassAvailabilityOrTypedStructuralReferences()
    {
        var collaboration = BpmnSemanticFactory.CreateCollaboration(
            CollaborationId,
            "Current");
        var participant = BpmnSemanticFactory.CreateParticipant(
            ParticipantId,
            CollaborationId,
            PeerScopeId,
            "Carrier");
        var unavailableHarness = CreateHarness(Snapshot(
            organizationalAvailable: false,
            elements: [collaboration, participant],
            nestedScopes: [new DocumentScopeSnapshot(PeerScopeId)]));
        var unavailable = unavailableHarness.Document.CaptureSnapshot();
        var nameResult = await unavailableHarness.History.ExecuteAsync(
            unavailableHarness.Processor,
            new UpdateSemanticElementNameCommand(
                unavailable.DocumentId,
                unavailable.Revision,
                CollaborationId,
                BpmnSemanticProperties.Name,
                "Changed"));
        Assert.False(nameResult.IsCommitted);
        Assert.Contains(nameResult.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.OrganizationalProfileUnavailable);
        Assert.Equal(unavailable, unavailableHarness.Document.CaptureSnapshot());

        var availableHarness = CreateHarness(Snapshot(
            organizationalAvailable: true,
            elements: [collaboration, participant],
            nestedScopes: [new DocumentScopeSnapshot(PeerScopeId)]));
        var available = availableHarness.Document.CaptureSnapshot();
        var structuralResult = await availableHarness.History.ExecuteAsync(
            availableHarness.Processor,
            new UpdateSemanticElementPropertyCommand(
                available.DocumentId,
                available.Revision,
                ParticipantId,
                BpmnSemanticProperties.ProcessScopeId,
                PropertyValue.FromText(available.SemanticModel.RootScopeId.Value)));
        Assert.False(structuralResult.IsCommitted);
        Assert.Contains(structuralResult.Diagnostics, diagnostic =>
            diagnostic.Code ==
                BpmnCommandDiagnosticCodes.OrganizationalStructuralReferenceInvalid);
        Assert.Equal(available, availableHarness.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task ParticipantCreationAcceptsMainPeerAndBlackBoxButRejectsNestedScope()
    {
        var subProcessId = new SemanticElementId("bpmn:n10:subprocess");
        var nestedScopeId = new DocumentScopeId("bpmn:n10:subprocess:scope");
        var collaboration = BpmnSemanticFactory.CreateCollaboration(CollaborationId);
        var subProcess = BpmnSemanticFactory.CreateSubProcess(
            subProcessId,
            "SUB",
            "SubProcess");
        var harness = CreateHarness(Snapshot(
            organizationalAvailable: true,
            elements: [collaboration, subProcess],
            nestedScopes:
            [
                new DocumentScopeSnapshot(PeerScopeId),
                new DocumentScopeSnapshot(
                    nestedScopeId,
                    new DocumentScopeId(DocumentId.Value),
                    subProcessId),
            ]));

        var before = harness.Document.CaptureSnapshot();
        var rejected = await harness.History.ExecuteAsync(
            harness.Processor,
            new CreateBpmnParticipantCommand(
                before.DocumentId,
                before.Revision,
                ParticipantId,
                CollaborationId,
                nestedScopeId,
                "Nested"));
        Assert.False(rejected.IsCommitted);
        Assert.Contains(rejected.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.ParticipantProcessScopeInvalid);
        Assert.False(harness.Document.CaptureSnapshot().SemanticModel.TryGetElement(
            ParticipantId,
            out _));

        var current = harness.Document.CaptureSnapshot();
        var accepted = await harness.History.ExecuteAsync(
            harness.Processor,
            new CreateBpmnParticipantCommand(
                current.DocumentId,
                current.Revision,
                ParticipantId,
                CollaborationId,
                PeerScopeId,
                "Carrier"));
        Assert.True(accepted.IsCommitted, Diagnostics(accepted.Diagnostics));
        var participant = Assert.Single(
            harness.Document.CaptureSnapshot().SemanticModel.Elements,
            element => element.Id == ParticipantId);
        Assert.Equal(SemanticElementContainmentKind.Document, participant.ContainmentKind);
        Assert.True(BpmnCollaborationSemantics.TryGetProcessScopeId(
            participant,
            out var processScopeId));
        Assert.Equal(PeerScopeId, processScopeId);
    }

    [Fact]
    public async Task ParticipantUpdateAndDeletionAreExactAndNeverDeleteReferencedProcess()
    {
        var participant = BpmnSemanticFactory.CreateParticipant(
            ParticipantId,
            CollaborationId,
            PeerScopeId,
            "Carrier");
        var harness = CreateHarness(Snapshot(
            organizationalAvailable: true,
            elements:
            [
                BpmnSemanticFactory.CreateCollaboration(CollaborationId),
                participant,
            ],
            nestedScopes: [new DocumentScopeSnapshot(PeerScopeId)]));

        var beforeUpdate = harness.Document.CaptureSnapshot();
        var updated = await harness.History.ExecuteAsync(
            harness.Processor,
            new UpdateBpmnParticipantCommand(
                beforeUpdate.DocumentId,
                beforeUpdate.Revision,
                ParticipantId,
                CollaborationId,
                processScopeId: null,
                name: "Black-box carrier"));
        Assert.True(updated.IsCommitted, Diagnostics(updated.Diagnostics));
        var updatedSnapshot = harness.Document.CaptureSnapshot();
        var updatedParticipant = Assert.Single(
            updatedSnapshot.SemanticModel.Elements,
            element => element.Id == ParticipantId);
        Assert.Equal(SemanticElementContainmentKind.Document,
            updatedParticipant.ContainmentKind);
        Assert.True(BpmnCollaborationSemantics.TryGetProcessScopeId(
            updatedParticipant,
            out var blackBoxScopeId));
        Assert.Null(blackBoxScopeId);
        Assert.True(updatedSnapshot.SemanticModel.IsExplicitPeerRoot(PeerScopeId));

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(
            beforeUpdate.SemanticModel.Elements.AsEnumerable(),
            harness.Document.CaptureSnapshot().SemanticModel.Elements.AsEnumerable());
        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(
            updatedSnapshot.SemanticModel.Elements.AsEnumerable(),
            harness.Document.CaptureSnapshot().SemanticModel.Elements.AsEnumerable());

        var beforeDelete = harness.Document.CaptureSnapshot();
        var deleted = await harness.History.ExecuteAsync(
            harness.Processor,
            new DeleteBpmnParticipantCommand(
                beforeDelete.DocumentId,
                beforeDelete.Revision,
                ParticipantId));
        Assert.True(deleted.IsCommitted, Diagnostics(deleted.Diagnostics));
        var deletedSnapshot = harness.Document.CaptureSnapshot();
        Assert.False(deletedSnapshot.SemanticModel.TryGetElement(ParticipantId, out _));
        Assert.True(deletedSnapshot.SemanticModel.IsExplicitPeerRoot(PeerScopeId));
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(
            beforeDelete.SemanticModel.Elements.AsEnumerable(),
            harness.Document.CaptureSnapshot().SemanticModel.Elements.AsEnumerable());
    }

    [Fact]
    public async Task CollaborationDeletionCascadesOnlyItsParticipantsAndPreservesProcesses()
    {
        var otherCollaborationId = new SemanticElementId("bpmn:n10:collaboration:other");
        var participantTwoId = new SemanticElementId("bpmn:n10:participant:two");
        var otherParticipantId = new SemanticElementId("bpmn:n10:participant:other");
        var processElementId = new SemanticElementId("bpmn:n10:process-task");
        var harness = CreateHarness(Snapshot(
            organizationalAvailable: true,
            elements:
            [
                BpmnSemanticFactory.CreateTask(processElementId, "TASK", "Task", 1L),
                BpmnSemanticFactory.CreateCollaboration(CollaborationId),
                BpmnSemanticFactory.CreateParticipant(
                    ParticipantId,
                    CollaborationId,
                    new DocumentScopeId(DocumentId.Value)),
                BpmnSemanticFactory.CreateParticipant(
                    participantTwoId,
                    CollaborationId,
                    PeerScopeId),
                BpmnSemanticFactory.CreateCollaboration(otherCollaborationId),
                BpmnSemanticFactory.CreateParticipant(
                    otherParticipantId,
                    otherCollaborationId,
                    PeerScopeId),
            ],
            nestedScopes: [new DocumentScopeSnapshot(PeerScopeId)]));
        var before = harness.Document.CaptureSnapshot();

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            new DeleteBpmnCollaborationCommand(
                before.DocumentId,
                before.Revision,
                CollaborationId));
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var committed = harness.Document.CaptureSnapshot();
        Assert.False(committed.SemanticModel.TryGetElement(CollaborationId, out _));
        Assert.False(committed.SemanticModel.TryGetElement(ParticipantId, out _));
        Assert.False(committed.SemanticModel.TryGetElement(participantTwoId, out _));
        Assert.True(committed.SemanticModel.TryGetElement(otherCollaborationId, out _));
        Assert.True(committed.SemanticModel.TryGetElement(otherParticipantId, out _));
        Assert.True(committed.SemanticModel.TryGetElement(processElementId, out _));
        Assert.True(committed.SemanticModel.IsExplicitPeerRoot(PeerScopeId));

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(
            before.SemanticModel.Elements.AsEnumerable(),
            harness.Document.CaptureSnapshot().SemanticModel.Elements.AsEnumerable());
        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(
            committed.SemanticModel.Elements.AsEnumerable(),
            harness.Document.CaptureSnapshot().SemanticModel.Elements.AsEnumerable());
    }

    [Fact]
    public void PureValidatorAuditsEveryCollaborationAndSkipsRetainedDataWhenUnavailable()
    {
        var validBlackBox = Snapshot(
            organizationalAvailable: true,
            elements:
            [
                BpmnSemanticFactory.CreateCollaboration(CollaborationId),
                BpmnSemanticFactory.CreateParticipant(ParticipantId, CollaborationId),
            ]);
        Assert.Empty(BpmnCollaborationStructuralValidator.Validate(validBlackBox));

        var invalidCollaboration = new SemanticElementSnapshot(
            CollaborationId,
            BpmnSemanticTypes.Collaboration,
            attachedToElementId: CollaborationId);
        var peerScope = new DocumentScopeSnapshot(PeerScopeId);
        var membership = new SemanticElementScopeMembershipSnapshot(
            CollaborationId,
            PeerScopeId);
        var available = Snapshot(
            organizationalAvailable: true,
            elements: [invalidCollaboration],
            nestedScopes: [peerScope],
            memberships: [membership]);

        var issues = BpmnCollaborationStructuralValidator.Validate(available);
        Assert.Contains(issues, issue =>
            issue.Code == BpmnModelValidationCodes.CollaborationContainmentInvalid);
        Assert.Contains(issues, issue =>
            issue.Code == BpmnModelValidationCodes.CollaborationOwnershipContradictory);
        Assert.All(issues, issue => Assert.Equal(
            CollaborationId,
            issue.Target.SemanticElementId));

        var unavailableValid = Snapshot(
            organizationalAvailable: false,
            elements:
            [
                BpmnSemanticFactory.CreateCollaboration(CollaborationId),
                BpmnSemanticFactory.CreateParticipant(ParticipantId, CollaborationId),
            ]);
        Assert.Empty(BpmnCollaborationStructuralValidator.Validate(unavailableValid));

        var unavailableMalformed = Snapshot(
            organizationalAvailable: false,
            elements: [invalidCollaboration],
            nestedScopes: [peerScope],
            memberships: [membership]);
        var unavailableIssues = BpmnCollaborationStructuralValidator.Validate(
            unavailableMalformed);
        Assert.Contains(unavailableIssues, issue =>
            issue.Code == BpmnModelValidationCodes.CollaborationContainmentInvalid);
        Assert.Contains(unavailableIssues, issue =>
            issue.Code == BpmnModelValidationCodes.CollaborationOwnershipContradictory);
        Assert.DoesNotContain(
            BpmnPluginRegistration.N100.ModelValidationRules,
            rule => rule.RuleId == BpmnCollaborationStructuralValidator.KnownRuleId);
    }

    [Fact]
    public void PureValidatorRejectsDocumentContainedFlowNodeAndScopeValidationStaysTotal()
    {
        var invalidTaskId = new SemanticElementId("bpmn:n10:document-task");
        var validTask = BpmnSemanticFactory.CreateTask(
            new SemanticElementId("bpmn:n10:scope-task"),
            "VALID",
            "Valid task",
            1L);
        var invalidTask = new SemanticElementSnapshot(
            invalidTaskId,
            BpmnSemanticTypes.Task,
            containmentKind: SemanticElementContainmentKind.Document);
        var flow = BpmnSemanticFactory.CreateSequenceFlow(
            new SemanticElementId("bpmn:n10:invalid-containment-flow"),
            invalidTaskId,
            validTask.Id);
        var semanticModel = new SemanticModelSnapshot(
            DocumentId,
            DocumentRevision.Zero,
            [invalidTask, validTask],
            [flow]);
        var snapshot = new DocumentSnapshot(
            semanticModel,
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));

        var containmentIssue = Assert.Single(
            BpmnCollaborationStructuralValidator.Validate(snapshot),
            issue => issue.Code ==
                BpmnModelValidationCodes.ProcessElementContainmentInvalid);
        Assert.Equal(invalidTaskId, containmentIssue.Target.SemanticElementId);

        var processIssues = new BpmnStructuralValidationRule().Validate(
            new ModelValidationContext(snapshot));
        Assert.DoesNotContain(processIssues, issue =>
            issue.Target.SemanticElementId == invalidTaskId);
    }

    [Fact]
    public void PureValidatorRejectsScopeContainedParticipant()
    {
        var correctParticipant = BpmnSemanticFactory.CreateParticipant(
            ParticipantId,
            CollaborationId);
        var malformedParticipant = new SemanticElementSnapshot(
            correctParticipant.Id,
            correctParticipant.TypeId,
            correctParticipant.Properties);
        var snapshot = Snapshot(
            organizationalAvailable: true,
            elements:
            [
                BpmnSemanticFactory.CreateCollaboration(CollaborationId),
                malformedParticipant,
            ]);

        var issue = Assert.Single(
            BpmnCollaborationStructuralValidator.Validate(snapshot),
            candidate => candidate.Code ==
                BpmnModelValidationCodes.ParticipantContainmentInvalid);
        Assert.Equal(ParticipantId, issue.Target.SemanticElementId);
    }

    [Fact]
    public void ProcessProjectionIsolatesPeerRootsAndSkipsDocumentLevelSemantics()
    {
        var rootTaskId = new SemanticElementId("bpmn:n10:root-task");
        var peerTaskId = new SemanticElementId("bpmn:n10:peer-task");
        var rootVisualId = new VisualStateId("bpmn:n10:root-task:visual");
        var peerVisualId = new VisualStateId("bpmn:n10:peer-task:visual");
        var semanticModel = new SemanticModelSnapshot(
            DocumentId,
            DocumentRevision.Zero,
            [
                BpmnSemanticFactory.CreateTask(rootTaskId, "ROOT", "Root", 1L),
                BpmnSemanticFactory.CreateTask(peerTaskId, "PEER", "Peer", 2L),
                BpmnSemanticFactory.CreateCollaboration(CollaborationId),
                BpmnSemanticFactory.CreateParticipant(
                    ParticipantId,
                    CollaborationId,
                    PeerScopeId),
            ],
            relationships: null,
            nestedScopes: [new DocumentScopeSnapshot(PeerScopeId)],
            scopeMemberships:
            [
                new SemanticElementScopeMembershipSnapshot(peerTaskId, PeerScopeId),
            ],
            modelProfiles: new ModelProfileStateSnapshot(
                [BpmnModelProfiles.OrganizationalId]));
        var snapshot = new DocumentSnapshot(
            semanticModel,
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    NodeVisual(rootVisualId, rootTaskId, new PointD(10d, 20d)),
                    NodeVisual(peerVisualId, peerTaskId, new PointD(30d, 40d)),
                ]),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
        var engine = new ProjectionEngine(BpmnPluginRegistration.N100.ProjectionRules);

        var root = engine.Project(snapshot);
        Assert.True(root.IsSuccessful, Diagnostics(root.Diagnostics));
        var rootGraph = Assert.IsType<ProjectedGraph>(root.Graph);
        Assert.Equal(rootTaskId, Assert.Single(rootGraph.Nodes).Source.SemanticElementId);

        var peer = engine.Project(snapshot, PeerScopeId);
        Assert.True(peer.IsSuccessful, Diagnostics(peer.Diagnostics));
        var peerGraph = Assert.IsType<ProjectedGraph>(peer.Graph);
        Assert.Equal(peerTaskId, Assert.Single(peerGraph.Nodes).Source.SemanticElementId);
        Assert.DoesNotContain(rootGraph.Nodes.Concat(peerGraph.Nodes), node =>
            node.Source.SemanticElementId == CollaborationId ||
            node.Source.SemanticElementId == ParticipantId);
    }

    [Fact]
    public void ProcessScopeOwnershipValidationDoesNotLeakAcrossPeerRoots()
    {
        var invalidPeerOwner = BpmnSemanticFactory.CreateTask(
            new SemanticElementId("bpmn:n10:peer:invalid-owner"),
            "OWNER",
            "Not a SubProcess",
            1L);
        var childScopeId = new DocumentScopeId("bpmn:n10:peer:child");
        var snapshot = Snapshot(
            organizationalAvailable: false,
            elements: [invalidPeerOwner],
            nestedScopes:
            [
                new DocumentScopeSnapshot(PeerScopeId),
                new DocumentScopeSnapshot(childScopeId, PeerScopeId, invalidPeerOwner.Id),
            ],
            memberships: [new(invalidPeerOwner.Id, PeerScopeId)]);
        var rule = new BpmnStructuralValidationRule();

        var mainIssues = rule.Validate(new ModelValidationContext(
            snapshot,
            snapshot.SemanticModel.RootScopeId));
        var peerIssues = rule.Validate(new ModelValidationContext(snapshot, PeerScopeId));

        Assert.DoesNotContain(mainIssues, issue =>
            issue.Code == BpmnModelValidationCodes.ProcessScopeOwnerInvalid);
        Assert.Contains(peerIssues, issue =>
            issue.Code == BpmnModelValidationCodes.ProcessScopeOwnerInvalid &&
            issue.Discriminator == childScopeId.Value);
    }

    [Fact]
    public void CrossScopeSequenceFlowFindingAppearsOnlyInItsTwoPeerContexts()
    {
        var peerCId = new DocumentScopeId("bpmn:n10:process:peer-c");
        var peerBTask = BpmnSemanticFactory.CreateTask(
            new SemanticElementId("bpmn:n10:peer-b:task"),
            "B",
            "Peer B",
            1L);
        var peerCTask = BpmnSemanticFactory.CreateTask(
            new SemanticElementId("bpmn:n10:peer-c:task"),
            "C",
            "Peer C",
            2L);
        var crossFlow = BpmnSemanticFactory.CreateSequenceFlow(
            new SemanticElementId("bpmn:n10:peer-cross-flow"),
            peerBTask.Id,
            peerCTask.Id);
        var semanticModel = new SemanticModelSnapshot(
            DocumentId,
            DocumentRevision.Zero,
            [peerBTask, peerCTask],
            [crossFlow],
            [new DocumentScopeSnapshot(PeerScopeId), new DocumentScopeSnapshot(peerCId)],
            [new(peerBTask.Id, PeerScopeId), new(peerCTask.Id, peerCId)]);
        var snapshot = new DocumentSnapshot(
            semanticModel,
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
        var rule = new BpmnStructuralValidationRule();

        var mainIssues = rule.Validate(new ModelValidationContext(
            snapshot,
            semanticModel.RootScopeId));
        var peerBIssues = rule.Validate(new ModelValidationContext(snapshot, PeerScopeId));
        var peerCIssues = rule.Validate(new ModelValidationContext(snapshot, peerCId));

        Assert.DoesNotContain(mainIssues, issue =>
            issue.Code == BpmnModelValidationCodes.SequenceFlowCrossesScope);
        Assert.Single(peerBIssues, issue =>
            issue.Code == BpmnModelValidationCodes.SequenceFlowCrossesScope);
        Assert.Single(peerCIssues, issue =>
            issue.Code == BpmnModelValidationCodes.SequenceFlowCrossesScope);
    }

    private static Harness CreateHarness(DocumentSnapshot snapshot)
    {
        var construction = DocumentFactory.Create(snapshot);
        Assert.True(construction.Succeeded, Diagnostics(construction.Diagnostics));
        var document = Assert.IsType<Document>(construction.Document);
        var registration = BpmnPluginRegistration.N100;
        return new Harness(
            document,
            new CommandProcessor(
                registration.CommandHandlers,
                registration.CommandValidators,
                historyPolicies: registration.HistoryPolicies),
            new HistoryManager(document));
    }

    private static DocumentSnapshot Snapshot(
        bool organizationalAvailable,
        IEnumerable<SemanticElementSnapshot>? elements = null,
        IEnumerable<DocumentScopeSnapshot>? nestedScopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null)
    {
        var profiles = organizationalAvailable
            ? new ModelProfileStateSnapshot([BpmnModelProfiles.OrganizationalId])
            : ModelProfileStateSnapshot.Empty;
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                elements,
                relationships: null,
                nestedScopes,
                memberships,
                profiles),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
    }

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private static VisualStateSnapshot NodeVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        PointD position) =>
        new(
            visualStateId,
            semanticElementId,
            position,
            new SizeD(120d, 80d),
            VisualPlacementMode.Manual);

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History);

    private sealed class FixedScopeIdentityProvider(DocumentScopeId scopeId)
        : IDocumentCreationIdentityProvider
    {
        internal int ScopeIdCallCount { get; private set; }

        public DocumentCreationIdentity CreateIdentity() =>
            throw new InvalidOperationException(
                "The Add Process action must request a Document scope identity directly.");

        public DocumentScopeId CreateDocumentScopeId()
        {
            ScopeIdCallCount++;
            return scopeId;
        }
    }
}
