using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN91BoundaryEventIntegrationTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n9.1:integration");

    private static readonly SemanticElementId StartId = new("bpmn:n9.1:start");
    private static readonly SemanticElementId ActivityId = new("bpmn:n9.1:activity");
    private static readonly SemanticElementId TimerId = new("bpmn:n9.1:timer");
    private static readonly SemanticElementId MessageId = new("bpmn:n9.1:message");
    private static readonly SemanticElementId SignalId = new("bpmn:n9.1:signal");
    private static readonly SemanticElementId MessageHandlerId =
        new("bpmn:n9.1:message-handler");
    private static readonly SemanticElementId SignalHandlerId =
        new("bpmn:n9.1:signal-handler");
    private static readonly SemanticElementId EventGatewayId =
        new("bpmn:n9.1:event-gateway");
    private static readonly SemanticElementId EndId = new("bpmn:n9.1:end");

    private static readonly VisualStateId StartVisualId =
        new("bpmn:n9.1:start:visual");
    private static readonly VisualStateId ActivityVisualId =
        new("bpmn:n9.1:activity:visual");
    private static readonly VisualStateId TimerVisualId =
        new("bpmn:n9.1:timer:visual");
    private static readonly VisualStateId MessageVisualId =
        new("bpmn:n9.1:message:visual");
    private static readonly VisualStateId SignalVisualId =
        new("bpmn:n9.1:signal:visual");
    private static readonly VisualStateId MessageHandlerVisualId =
        new("bpmn:n9.1:message-handler:visual");
    private static readonly VisualStateId SignalHandlerVisualId =
        new("bpmn:n9.1:signal-handler:visual");
    private static readonly VisualStateId EventGatewayVisualId =
        new("bpmn:n9.1:event-gateway:visual");
    private static readonly VisualStateId EndVisualId =
        new("bpmn:n9.1:end:visual");

    private static readonly SemanticElementId StartActivityFlowId =
        new("bpmn:n9.1:flow:start-activity");
    private static readonly SemanticElementId ActivityEndFlowId =
        new("bpmn:n9.1:flow:activity-end");
    private static readonly SemanticElementId MessageHandlerFlowId =
        new("bpmn:n9.1:flow:message-handler");
    private static readonly SemanticElementId SignalHandlerFlowId =
        new("bpmn:n9.1:flow:signal-handler");
    private static readonly SemanticElementId MessageHandlerEndFlowId =
        new("bpmn:n9.1:flow:message-handler-end");
    private static readonly SemanticElementId SignalHandlerEndFlowId =
        new("bpmn:n9.1:flow:signal-handler-end");

    private static readonly ConnectorAnchorId StartSourceAnchorId =
        new("bpmn:n9.1:start:source");
    private static readonly ConnectorAnchorId ActivityTargetAnchorId =
        new("bpmn:n9.1:activity:target");
    private static readonly ConnectorAnchorId ActivitySourceAnchorId =
        new("bpmn:n9.1:activity:source");
    private static readonly ConnectorAnchorId TimerSourceAnchorId =
        new("bpmn:n9.1:timer:source");
    private static readonly ConnectorAnchorId MessageSourceAnchorId =
        new("bpmn:n9.1:message:source");
    private static readonly ConnectorAnchorId SignalSourceAnchorId =
        new("bpmn:n9.1:signal:source");
    private static readonly ConnectorAnchorId MessageHandlerTargetAnchorId =
        new("bpmn:n9.1:message-handler:target");
    private static readonly ConnectorAnchorId MessageHandlerSourceAnchorId =
        new("bpmn:n9.1:message-handler:source");
    private static readonly ConnectorAnchorId SignalHandlerTargetAnchorId =
        new("bpmn:n9.1:signal-handler:target");
    private static readonly ConnectorAnchorId SignalHandlerSourceAnchorId =
        new("bpmn:n9.1:signal-handler:source");
    private static readonly ConnectorAnchorId EventGatewaySourceAnchorId =
        new("bpmn:n9.1:event-gateway:source");
    private static readonly ConnectorAnchorId EndActivityTargetAnchorId =
        new("bpmn:n9.1:end:activity-target");
    private static readonly ConnectorAnchorId EndMessageTargetAnchorId =
        new("bpmn:n9.1:end:message-target");
    private static readonly ConnectorAnchorId EndSignalTargetAnchorId =
        new("bpmn:n9.1:end:signal-target");

    private static readonly RectD InitialActivityBounds =
        new(180d, 140d, 160d, 100d);
    private static readonly SizeD BoundarySize = new(36d, 36d);
    private static readonly BoundaryAttachmentPlacement TimerPlacement =
        new(BoundaryAttachmentSide.Bottom, 0.25d);
    private static readonly BoundaryAttachmentPlacement OverlappingPlacement =
        new(BoundaryAttachmentSide.Right, 0.5d);

    [Fact]
    public async Task MixedAttachmentsRemainIndependentOverlappingAndExactlyUndoable()
    {
        var harness = CreateHarness();
        await CreateActivityAsync(harness);

        await CreateTimerAsync(harness, TimerPlacement);
        var timerAfterCreation = BoundaryVisual(harness, TimerVisualId);

        await CreateMessageAsync(harness, OverlappingPlacement);
        Assert.Equal(timerAfterCreation, BoundaryVisual(harness, TimerVisualId));
        var messageAfterCreation = BoundaryVisual(harness, MessageVisualId);

        await CreateSignalAsync(harness, OverlappingPlacement);
        Assert.Equal(timerAfterCreation, BoundaryVisual(harness, TimerVisualId));
        Assert.Equal(messageAfterCreation, BoundaryVisual(harness, MessageVisualId));

        AssertBoundary(harness, TimerVisualId, TimerPlacement, InitialActivityBounds);
        AssertBoundary(
            harness,
            MessageVisualId,
            OverlappingPlacement,
            InitialActivityBounds);
        AssertBoundary(
            harness,
            SignalVisualId,
            OverlappingPlacement,
            InitialActivityBounds);
        Assert.Equal(
            VisualBounds(BoundaryVisual(harness, MessageVisualId)),
            VisualBounds(BoundaryVisual(harness, SignalVisualId)));

        var overlapValidation = new BpmnStructuralValidationRule().Validate(
            new ModelValidationContext(harness.Document.CaptureSnapshot()));
        var attachmentValidationCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            BpmnModelValidationCodes.BoundaryEventAttachmentMissing,
            BpmnModelValidationCodes.BoundaryEventAttachmentTargetMissing,
            BpmnModelValidationCodes.BoundaryEventAttachmentOwnerInvalid,
            BpmnModelValidationCodes.BoundaryEventAttachmentScopeMismatch,
        };
        Assert.DoesNotContain(overlapValidation, issue =>
            (issue.Target.SemanticElementId == MessageId ||
                issue.Target.SemanticElementId == SignalId) &&
            (attachmentValidationCodes.Contains(issue.Code) ||
                issue.Code.Contains("overlap", StringComparison.OrdinalIgnoreCase) ||
                issue.Message.Contains("overlap", StringComparison.OrdinalIgnoreCase)));

        AssertAttachedType(harness, TimerId, BpmnSemanticTypes.TimerBoundaryEvent);
        AssertAttachedType(harness, MessageId, BpmnSemanticTypes.MessageBoundaryEvent);
        AssertAttachedType(harness, SignalId, BpmnSemanticTypes.SignalBoundaryEvent);

        var beforeMessageMove = harness.Document.CaptureSnapshot();
        var timerBeforeMessageMove = BoundaryVisual(harness, TimerVisualId);
        var signalBeforeMessageMove = BoundaryVisual(harness, SignalVisualId);
        var movedMessagePlacement = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Top,
            0.75d);
        await ExecuteCommittedAsync(
            harness,
            revision => new UpdateBoundaryAttachmentCommand(
                DocumentId,
                revision,
                MessageVisualId,
                movedMessagePlacement,
                InitialActivityBounds));
        var afterMessageMove = harness.Document.CaptureSnapshot();
        AssertBoundary(
            harness,
            MessageVisualId,
            movedMessagePlacement,
            InitialActivityBounds);
        Assert.Equal(timerBeforeMessageMove, BoundaryVisual(harness, TimerVisualId));
        Assert.Equal(signalBeforeMessageMove, BoundaryVisual(harness, SignalVisualId));

        await AssertUndoRedoExactAsync(harness, beforeMessageMove, afterMessageMove);

        var beforeSignalMove = harness.Document.CaptureSnapshot();
        var timerBeforeSignalMove = BoundaryVisual(harness, TimerVisualId);
        var messageBeforeSignalMove = BoundaryVisual(harness, MessageVisualId);
        var movedSignalPlacement = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Left,
            0.2d);
        await ExecuteCommittedAsync(
            harness,
            revision => new UpdateBoundaryAttachmentCommand(
                DocumentId,
                revision,
                SignalVisualId,
                movedSignalPlacement,
                InitialActivityBounds));
        var afterSignalMove = harness.Document.CaptureSnapshot();
        AssertBoundary(
            harness,
            SignalVisualId,
            movedSignalPlacement,
            InitialActivityBounds);
        Assert.Equal(timerBeforeSignalMove, BoundaryVisual(harness, TimerVisualId));
        Assert.Equal(messageBeforeSignalMove, BoundaryVisual(harness, MessageVisualId));
        await AssertUndoRedoExactAsync(harness, beforeSignalMove, afterSignalMove);

        var movedOwnerBounds = new RectD(235d, 185d, 160d, 100d);
        var beforeOwnerMove = harness.Document.CaptureSnapshot();
        await ExecuteCommittedAsync(
            harness,
            revision => new MoveVisualStateCommand(
                DocumentId,
                revision,
                ActivityVisualId,
                movedOwnerBounds.TopLeft,
                VisualPlacementMode.Pinned));
        var afterOwnerMove = harness.Document.CaptureSnapshot();
        AssertAllBoundaryGeometry(
            harness,
            movedOwnerBounds,
            movedMessagePlacement,
            movedSignalPlacement);
        await AssertUndoRedoExactAsync(harness, beforeOwnerMove, afterOwnerMove);
        AssertAllBoundaryGeometry(
            harness,
            movedOwnerBounds,
            movedMessagePlacement,
            movedSignalPlacement);

        var resizedOwnerBounds = new RectD(235d, 185d, 220d, 130d);
        var beforeOwnerResize = harness.Document.CaptureSnapshot();
        await ExecuteCommittedAsync(
            harness,
            revision => new ResizeVisualStateCommand(
                DocumentId,
                revision,
                ActivityVisualId,
                resizedOwnerBounds,
                VisualPlacementMode.Pinned));
        var afterOwnerResize = harness.Document.CaptureSnapshot();
        AssertAllBoundaryGeometry(
            harness,
            resizedOwnerBounds,
            movedMessagePlacement,
            movedSignalPlacement);
        await AssertUndoRedoExactAsync(harness, beforeOwnerResize, afterOwnerResize);
        AssertAllBoundaryGeometry(
            harness,
            resizedOwnerBounds,
            movedMessagePlacement,
            movedSignalPlacement);
    }

    [Fact]
    public async Task MessageAndSignalFlowsValidationAndRejectionsUseBoundarySemantics()
    {
        var harness = await CreateFlowModelAsync();
        var snapshot = harness.Document.CaptureSnapshot();

        Assert.Contains(snapshot.SemanticModel.Relationships, relationship =>
            relationship.Id == MessageHandlerFlowId &&
            relationship.SourceId == MessageId &&
            relationship.TargetId == MessageHandlerId);
        Assert.Contains(snapshot.SemanticModel.Relationships, relationship =>
            relationship.Id == SignalHandlerFlowId &&
            relationship.SourceId == SignalId &&
            relationship.TargetId == SignalHandlerId);

        AssertSourceOnlyBoundaryAnchor(
            BoundaryVisual(harness, MessageVisualId),
            MessageSourceAnchorId);
        AssertSourceOnlyBoundaryAnchor(
            BoundaryVisual(harness, SignalVisualId),
            SignalSourceAnchorId);

        var pipeline = RunPipeline(snapshot);
        AssertBoundaryFlowRoute(
            pipeline,
            snapshot,
            MessageHandlerFlowId,
            MessageVisualId,
            MessageHandlerVisualId);
        AssertBoundaryFlowRoute(
            pipeline,
            snapshot,
            SignalHandlerFlowId,
            SignalVisualId,
            SignalHandlerVisualId);

        var validation = new BpmnStructuralValidationRule().Validate(
            new ModelValidationContext(snapshot));
        var boundaryBranches = new HashSet<SemanticElementId>
        {
            MessageId,
            SignalId,
            MessageHandlerId,
            SignalHandlerId,
        };
        Assert.DoesNotContain(validation, issue =>
            issue.Target.SemanticElementId is { } elementId &&
            boundaryBranches.Contains(elementId) &&
            (issue.Code == BpmnModelValidationCodes.FlowNodeIsolated ||
                issue.Code == BpmnModelValidationCodes.NodeUnreachableFromStart));

        await AssertRejectedFlowIsAtomicAsync(
            harness,
            new SemanticElementId("bpmn:n9.1:flow:incoming-message"),
            MessageHandlerId,
            MessageId,
            MessageHandlerSourceAnchorId,
            MessageSourceAnchorId,
            BpmnCommandDiagnosticCodes.IncomingBoundaryEvent);
        await AssertRejectedFlowIsAtomicAsync(
            harness,
            new SemanticElementId("bpmn:n9.1:flow:incoming-signal"),
            SignalHandlerId,
            SignalId,
            SignalHandlerSourceAnchorId,
            SignalSourceAnchorId,
            BpmnCommandDiagnosticCodes.IncomingBoundaryEvent);

        var beforeTargetReconnect = harness.Document.CaptureSnapshot();
        var historyBeforeTargetReconnect = harness.History.CaptureStatus();
        var rejectedTargetReconnect = await harness.History.ExecuteAsync(
            harness.Processor,
            new ReconnectBpmnSequenceFlowEndpointCommand(
                DocumentId,
                harness.Document.Revision,
                ActivityEndFlowId,
                new VisualStateId($"{ActivityEndFlowId.Value}:visual"),
                ConnectorEndpointKind.Target,
                EndId,
                EndActivityTargetAnchorId,
                MessageId,
                MessageSourceAnchorId));
        Assert.False(rejectedTargetReconnect.IsCommitted);
        Assert.Contains(rejectedTargetReconnect.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.IncomingBoundaryEvent);
        Assert.Same(beforeTargetReconnect, harness.Document.CaptureSnapshot());
        Assert.Equal(
            historyBeforeTargetReconnect,
            harness.History.CaptureStatus());

        var alternateSignalSourceAnchorId = new ConnectorAnchorId(
            "bpmn:n9.1:signal:alternate-source");
        await AddAnchorAsync(
            harness,
            SignalVisualId,
            alternateSignalSourceAnchorId,
            ConnectorAnchorSide.Bottom,
            ConnectorAnchorRole.Source,
            0);
        Assert.All(
            BoundaryVisual(harness, SignalVisualId).ConnectorAnchors,
            anchor => Assert.Equal(ConnectorAnchorRole.Source, anchor.Role));

        var beforeSourceReconnect = harness.Document.CaptureSnapshot();
        var relationshipBeforeSourceReconnect = Assert.Single(
            beforeSourceReconnect.SemanticModel.Relationships,
            relationship => relationship.Id == MessageHandlerFlowId);
        var connectorBeforeSourceReconnect = Assert.Single(
            beforeSourceReconnect.VisualModel.VisualStates,
            visual => visual.SemanticElementId == MessageHandlerFlowId);
        await ExecuteCommittedAsync(
            harness,
            revision => new ReconnectBpmnSequenceFlowEndpointCommand(
                DocumentId,
                revision,
                MessageHandlerFlowId,
                connectorBeforeSourceReconnect.Id,
                ConnectorEndpointKind.Source,
                MessageId,
                MessageSourceAnchorId,
                SignalId,
                alternateSignalSourceAnchorId));
        var afterSourceReconnect = harness.Document.CaptureSnapshot();
        var reconnectedRelationship = Assert.Single(
            afterSourceReconnect.SemanticModel.Relationships,
            relationship => relationship.Id == MessageHandlerFlowId);
        var reconnectedConnector = Assert.Single(
            afterSourceReconnect.VisualModel.VisualStates,
            visual => visual.Id == connectorBeforeSourceReconnect.Id);
        Assert.Equal(relationshipBeforeSourceReconnect.Id, reconnectedRelationship.Id);
        Assert.Equal(SignalId, reconnectedRelationship.SourceId);
        Assert.Equal(MessageHandlerId, reconnectedRelationship.TargetId);
        Assert.Equal(connectorBeforeSourceReconnect.Id, reconnectedConnector.Id);
        Assert.Equal(MessageHandlerFlowId, reconnectedConnector.SemanticElementId);
        Assert.Equal(
            alternateSignalSourceAnchorId,
            reconnectedConnector.SourceAnchorId);
        Assert.Equal(
            MessageHandlerTargetAnchorId,
            reconnectedConnector.TargetAnchorId);
        await AssertUndoRedoExactAsync(
            harness,
            beforeSourceReconnect,
            afterSourceReconnect);
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        AssertAuthoritativeContentEqual(
            beforeSourceReconnect,
            harness.Document.CaptureSnapshot());

        foreach (var (targetId, targetAnchorId, suffix) in new[]
                 {
                     (MessageId, MessageSourceAnchorId, "message"),
                     (SignalId, SignalSourceAnchorId, "signal"),
                 })
        {
            var relationshipId = new SemanticElementId(
                $"bpmn:n9.1:flow:event-gateway-{suffix}");
            var before = harness.Document.CaptureSnapshot();
            var historyBefore = harness.History.CaptureStatus();
            var rejected = await harness.History.ExecuteAsync(
                harness.Processor,
                new CreateBpmnSequenceFlowCommand(
                    DocumentId,
                    harness.Document.Revision,
                    relationshipId,
                    new VisualStateId($"{relationshipId.Value}:visual"),
                    EventGatewayId,
                    targetId,
                    EventGatewaySourceAnchorId,
                    targetAnchorId));

            Assert.False(rejected.IsCommitted);
            Assert.Contains(rejected.Diagnostics, diagnostic =>
                diagnostic.Code ==
                    BpmnCommandDiagnosticCodes.EventBasedGatewayTargetInvalid);
            Assert.Contains(rejected.Diagnostics, diagnostic =>
                diagnostic.Code == BpmnCommandDiagnosticCodes.IncomingBoundaryEvent);
            Assert.Same(before, harness.Document.CaptureSnapshot());
            Assert.Equal(historyBefore, harness.History.CaptureStatus());
            Assert.False(harness.Document.SemanticModel.TryGetRelationship(
                relationshipId,
                out _));
        }
    }

    [Fact]
    public async Task IndividualAndOwnerDeletionCascadeMixedAttachmentsExactly()
    {
        var harness = await CreateFlowModelAsync();

        var beforeIndividualDelete = harness.Document.CaptureSnapshot();
        await ExecuteCommittedAsync(
            harness,
            revision => new DeleteBpmnFlowNodeCommand(
                DocumentId,
                revision,
                MessageId,
                MessageVisualId));
        var afterIndividualDelete = harness.Document.CaptureSnapshot();
        Assert.False(afterIndividualDelete.SemanticModel.TryGetElement(MessageId, out _));
        Assert.False(afterIndividualDelete.VisualModel.TryGetVisualState(
            MessageVisualId,
            out _));
        Assert.False(afterIndividualDelete.SemanticModel.TryGetRelationship(
            MessageHandlerFlowId,
            out _));
        Assert.True(afterIndividualDelete.SemanticModel.TryGetElement(ActivityId, out _));
        Assert.True(afterIndividualDelete.SemanticModel.TryGetElement(TimerId, out _));
        Assert.True(afterIndividualDelete.SemanticModel.TryGetElement(SignalId, out _));
        Assert.True(afterIndividualDelete.SemanticModel.TryGetElement(
            MessageHandlerId,
            out _));

        await AssertUndoRedoExactAsync(
            harness,
            beforeIndividualDelete,
            afterIndividualDelete);
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        AssertAuthoritativeContentEqual(
            beforeIndividualDelete,
            harness.Document.CaptureSnapshot());

        var beforeOwnerDelete = harness.Document.CaptureSnapshot();
        var cascadedElementIds = new HashSet<SemanticElementId>
        {
            ActivityId,
            TimerId,
            MessageId,
            SignalId,
        };
        var cascadedFlowIds = beforeOwnerDelete.SemanticModel.Relationships
            .Where(relationship =>
                cascadedElementIds.Contains(relationship.SourceId) ||
                cascadedElementIds.Contains(relationship.TargetId))
            .Select(static relationship => relationship.Id)
            .ToHashSet();
        Assert.Equal(4, cascadedFlowIds.Count);

        await ExecuteCommittedAsync(
            harness,
            revision => new DeleteBpmnFlowNodeCommand(
                DocumentId,
                revision,
                ActivityId,
                ActivityVisualId));
        var afterOwnerDelete = harness.Document.CaptureSnapshot();
        Assert.DoesNotContain(afterOwnerDelete.SemanticModel.Elements, element =>
            cascadedElementIds.Contains(element.Id));
        Assert.DoesNotContain(afterOwnerDelete.VisualModel.VisualStates, visual =>
            visual.Id == ActivityVisualId ||
            visual.Id == TimerVisualId ||
            visual.Id == MessageVisualId ||
            visual.Id == SignalVisualId);
        Assert.DoesNotContain(afterOwnerDelete.SemanticModel.Relationships, relationship =>
            cascadedFlowIds.Contains(relationship.Id));
        Assert.Contains(afterOwnerDelete.SemanticModel.Elements, element =>
            element.Id == MessageHandlerId);
        Assert.Contains(afterOwnerDelete.SemanticModel.Elements, element =>
            element.Id == SignalHandlerId);
        Assert.Contains(afterOwnerDelete.SemanticModel.Relationships, relationship =>
            relationship.Id == MessageHandlerEndFlowId);
        Assert.Contains(afterOwnerDelete.SemanticModel.Relationships, relationship =>
            relationship.Id == SignalHandlerEndFlowId);

        await AssertUndoRedoExactAsync(harness, beforeOwnerDelete, afterOwnerDelete);
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        AssertAuthoritativeContentEqual(
            beforeOwnerDelete,
            harness.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task MixedAttachmentSnapshotReconstructsExactly()
    {
        var harness = await CreateFlowModelAsync();
        var snapshot = harness.Document.CaptureSnapshot();
        var provider = new ElementConnectorAnchorPolicyRegistry(
            BpmnPluginRegistration.N91.ConnectorAnchorPolicies);

        var reconstruction = DocumentReconstructor.Reconstruct(snapshot, provider);

        Assert.True(
            reconstruction.Succeeded,
            Diagnostics(reconstruction.Diagnostics));
        var captured = Assert.IsType<Document>(reconstruction.Document).CaptureSnapshot();
        Assert.Equal(snapshot, captured);

        AssertRoundTrippedBoundary(
            captured,
            MessageId,
            MessageVisualId,
            BpmnSemanticTypes.MessageBoundaryEvent,
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.55d),
            "Message boundary",
            cancelActivity: true,
            "Message boundary description.",
            MessageSourceAnchorId);
        AssertRoundTrippedBoundary(
            captured,
            SignalId,
            SignalVisualId,
            BpmnSemanticTypes.SignalBoundaryEvent,
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.8d),
            "Signal boundary",
            cancelActivity: false,
            "Signal boundary description.",
            SignalSourceAnchorId);

        Assert.DoesNotContain(captured.SemanticModel.ScopeMemberships, membership =>
            membership.SemanticElementId == MessageId ||
            membership.SemanticElementId == SignalId);
        Assert.Contains(captured.SemanticModel.Relationships, relationship =>
            relationship.Id == MessageHandlerFlowId &&
            relationship.SourceId == MessageId &&
            relationship.TargetId == MessageHandlerId);
        Assert.Contains(captured.SemanticModel.Relationships, relationship =>
            relationship.Id == SignalHandlerFlowId &&
            relationship.SourceId == SignalId &&
            relationship.TargetId == SignalHandlerId);
    }

    [Fact]
    public async Task RecursiveSubProcessDeletionCascadesMixedParentAndChildAttachments()
    {
        var harness = CreateHarness();
        var rootScopeId = harness.Document.SemanticModel.RootScopeId;
        var subProcessId = new SemanticElementId("bpmn:n9.1:recursive:subprocess");
        var subProcessVisualId = new VisualStateId(
            "bpmn:n9.1:recursive:subprocess:visual");
        var childScopeId = new DocumentScopeId("bpmn:n9.1:recursive:scope");
        var subProcessBounds = new RectD(120d, 100d, 180d, 110d);
        var parentTimerId = new SemanticElementId("bpmn:n9.1:recursive:timer");
        var parentTimerVisualId = new VisualStateId(
            "bpmn:n9.1:recursive:timer:visual");
        var parentMessageId = new SemanticElementId("bpmn:n9.1:recursive:message");
        var parentMessageVisualId = new VisualStateId(
            "bpmn:n9.1:recursive:message:visual");
        var parentSignalId = new SemanticElementId("bpmn:n9.1:recursive:signal");
        var parentSignalVisualId = new VisualStateId(
            "bpmn:n9.1:recursive:signal:visual");
        var childActivityId = new SemanticElementId(
            "bpmn:n9.1:recursive:child-activity");
        var childActivityVisualId = new VisualStateId(
            "bpmn:n9.1:recursive:child-activity:visual");
        var childBounds = new RectD(90d, 80d, 150d, 90d);
        var childSignalId = new SemanticElementId(
            "bpmn:n9.1:recursive:child-signal");
        var childSignalVisualId = new VisualStateId(
            "bpmn:n9.1:recursive:child-signal:visual");

        await ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnSubProcessCommand(
                DocumentId,
                revision,
                subProcessId,
                subProcessVisualId,
                rootScopeId,
                childScopeId,
                subProcessBounds.TopLeft,
                subProcessBounds.Size,
                "N91_RECURSIVE",
                "Recursive N9.1",
                VisualPlacementMode.Pinned));
        await ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnTimerBoundaryEventCommand(
                DocumentId,
                revision,
                parentTimerId,
                parentTimerVisualId,
                subProcessId,
                BoundaryAttachmentSide.Bottom,
                0.25d,
                subProcessBounds,
                "Parent timer"));
        await ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnMessageBoundaryEventCommand(
                DocumentId,
                revision,
                parentMessageId,
                parentMessageVisualId,
                subProcessId,
                BoundaryAttachmentSide.Bottom,
                0.5d,
                subProcessBounds,
                "Parent message"));
        await ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnSignalBoundaryEventCommand(
                DocumentId,
                revision,
                parentSignalId,
                parentSignalVisualId,
                subProcessId,
                BoundaryAttachmentSide.Bottom,
                0.75d,
                subProcessBounds,
                "Parent signal"));
        await ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnTaskCommand(
                DocumentId,
                revision,
                childActivityId,
                childActivityVisualId,
                childBounds.TopLeft,
                childBounds.Size,
                "N91_CHILD",
                "Child activity",
                94,
                VisualPlacementMode.Pinned,
                targetScopeId: childScopeId));
        await ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnSignalBoundaryEventCommand(
                DocumentId,
                revision,
                childSignalId,
                childSignalVisualId,
                childActivityId,
                BoundaryAttachmentSide.Right,
                0.5d,
                childBounds,
                "Child signal",
                targetScopeId: childScopeId));

        var beforeDelete = harness.Document.CaptureSnapshot();
        Assert.Equal(rootScopeId, beforeDelete.SemanticModel.GetScope(parentTimerId).Id);
        Assert.Equal(rootScopeId, beforeDelete.SemanticModel.GetScope(parentMessageId).Id);
        Assert.Equal(rootScopeId, beforeDelete.SemanticModel.GetScope(parentSignalId).Id);
        Assert.Equal(childScopeId, beforeDelete.SemanticModel.GetScope(childSignalId).Id);

        await ExecuteCommittedAsync(
            harness,
            revision => new DeleteBpmnFlowNodeCommand(
                DocumentId,
                revision,
                subProcessId,
                subProcessVisualId));
        var afterDelete = harness.Document.CaptureSnapshot();
        var removedElements = new HashSet<SemanticElementId>
        {
            subProcessId,
            parentTimerId,
            parentMessageId,
            parentSignalId,
            childActivityId,
            childSignalId,
        };
        var removedVisuals = new HashSet<VisualStateId>
        {
            subProcessVisualId,
            parentTimerVisualId,
            parentMessageVisualId,
            parentSignalVisualId,
            childActivityVisualId,
            childSignalVisualId,
        };
        Assert.DoesNotContain(afterDelete.SemanticModel.Elements, element =>
            removedElements.Contains(element.Id));
        Assert.DoesNotContain(afterDelete.VisualModel.VisualStates, visual =>
            removedVisuals.Contains(visual.Id));
        Assert.DoesNotContain(afterDelete.SemanticModel.NestedScopes, scope =>
            scope.Id == childScopeId);
        Assert.DoesNotContain(afterDelete.SemanticModel.ScopeMemberships, membership =>
            membership.ScopeId == childScopeId ||
            removedElements.Contains(membership.SemanticElementId));

        await AssertUndoRedoExactAsync(harness, beforeDelete, afterDelete);
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        AssertAuthoritativeContentEqual(beforeDelete, harness.Document.CaptureSnapshot());
    }

    private static Harness CreateHarness()
    {
        var registration = BpmnPluginRegistration.N91;
        var anchorPolicies = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.CreateEmpty(
            DocumentId,
            connectorAnchorPolicyProvider: anchorPolicies);
        Assert.True(construction.Succeeded, Diagnostics(construction.Diagnostics));
        var document = Assert.IsType<Document>(construction.Document);
        return new Harness(
            document,
            new CommandProcessor(
                registration.CommandHandlers,
                registration.CommandValidators,
                historyPolicies: registration.HistoryPolicies,
                connectorAnchorPolicyProvider: anchorPolicies),
            new HistoryManager(document));
    }

    private static async Task CreateActivityAsync(Harness harness) =>
        await ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnTaskCommand(
                DocumentId,
                revision,
                ActivityId,
                ActivityVisualId,
                InitialActivityBounds.TopLeft,
                InitialActivityBounds.Size,
                "N91_ACTIVITY",
                "N9.1 activity",
                91,
                VisualPlacementMode.Pinned));

    private static Task CreateTimerAsync(
        Harness harness,
        BoundaryAttachmentPlacement placement) =>
        ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnTimerBoundaryEventCommand(
                DocumentId,
                revision,
                TimerId,
                TimerVisualId,
                ActivityId,
                placement.Side,
                placement.PositionOnSide,
                InitialActivityBounds,
                "Timer boundary",
                "PT5M"));

    private static Task CreateMessageAsync(
        Harness harness,
        BoundaryAttachmentPlacement placement) =>
        ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnMessageBoundaryEventCommand(
                DocumentId,
                revision,
                MessageId,
                MessageVisualId,
                ActivityId,
                placement.Side,
                placement.PositionOnSide,
                InitialActivityBounds,
                "Message boundary",
                cancelActivity: true,
                description: "Message boundary description."));

    private static Task CreateSignalAsync(
        Harness harness,
        BoundaryAttachmentPlacement placement) =>
        ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnSignalBoundaryEventCommand(
                DocumentId,
                revision,
                SignalId,
                SignalVisualId,
                ActivityId,
                placement.Side,
                placement.PositionOnSide,
                InitialActivityBounds,
                "Signal boundary",
                cancelActivity: false,
                description: "Signal boundary description."));

    private static async Task<Harness> CreateFlowModelAsync()
    {
        var harness = CreateHarness();
        await ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnStartEventCommand(
                DocumentId,
                revision,
                StartId,
                StartVisualId,
                new PointD(40d, 170d),
                BoundarySize,
                VisualPlacementMode.Pinned,
                "Start"));
        await CreateActivityAsync(harness);
        await CreateTimerAsync(harness, TimerPlacement);
        await CreateMessageAsync(
            harness,
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.55d));
        await CreateSignalAsync(
            harness,
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.8d));
        await ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnTaskCommand(
                DocumentId,
                revision,
                MessageHandlerId,
                MessageHandlerVisualId,
                new PointD(430d, 270d),
                new SizeD(130d, 80d),
                "MESSAGE_HANDLER",
                "Handle message",
                92,
                VisualPlacementMode.Pinned));
        await ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnTaskCommand(
                DocumentId,
                revision,
                SignalHandlerId,
                SignalHandlerVisualId,
                new PointD(430d, 390d),
                new SizeD(130d, 80d),
                "SIGNAL_HANDLER",
                "Handle signal",
                93,
                VisualPlacementMode.Pinned));
        await ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnEventBasedGatewayCommand(
                DocumentId,
                revision,
                EventGatewayId,
                EventGatewayVisualId,
                new PointD(430d, 50d),
                new SizeD(48d, 48d),
                "EVENT_WAIT",
                "Wait for event",
                VisualPlacementMode.Pinned));
        await ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnEndEventCommand(
                DocumentId,
                revision,
                EndId,
                EndVisualId,
                new PointD(700d, 170d),
                BoundarySize,
                VisualPlacementMode.Pinned,
                "End"));

        await AddAnchorAsync(
            harness,
            StartVisualId,
            StartSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        await AddAnchorAsync(
            harness,
            ActivityVisualId,
            ActivityTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        await AddAnchorAsync(
            harness,
            ActivityVisualId,
            ActivitySourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        await AddAnchorAsync(
            harness,
            TimerVisualId,
            TimerSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        await AddAnchorAsync(
            harness,
            MessageVisualId,
            MessageSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        await AddAnchorAsync(
            harness,
            SignalVisualId,
            SignalSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        await AddAnchorAsync(
            harness,
            MessageHandlerVisualId,
            MessageHandlerTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        await AddAnchorAsync(
            harness,
            MessageHandlerVisualId,
            MessageHandlerSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        await AddAnchorAsync(
            harness,
            SignalHandlerVisualId,
            SignalHandlerTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        await AddAnchorAsync(
            harness,
            SignalHandlerVisualId,
            SignalHandlerSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        await AddAnchorAsync(
            harness,
            EventGatewayVisualId,
            EventGatewaySourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0);
        await AddAnchorAsync(
            harness,
            EndVisualId,
            EndActivityTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0);
        await AddAnchorAsync(
            harness,
            EndVisualId,
            EndMessageTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            1);
        await AddAnchorAsync(
            harness,
            EndVisualId,
            EndSignalTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            2);

        await CreateFlowAsync(
            harness,
            StartActivityFlowId,
            StartId,
            ActivityId,
            StartSourceAnchorId,
            ActivityTargetAnchorId);
        await CreateFlowAsync(
            harness,
            ActivityEndFlowId,
            ActivityId,
            EndId,
            ActivitySourceAnchorId,
            EndActivityTargetAnchorId);
        await CreateFlowAsync(
            harness,
            MessageHandlerFlowId,
            MessageId,
            MessageHandlerId,
            MessageSourceAnchorId,
            MessageHandlerTargetAnchorId);
        await CreateFlowAsync(
            harness,
            SignalHandlerFlowId,
            SignalId,
            SignalHandlerId,
            SignalSourceAnchorId,
            SignalHandlerTargetAnchorId);
        await CreateFlowAsync(
            harness,
            MessageHandlerEndFlowId,
            MessageHandlerId,
            EndId,
            MessageHandlerSourceAnchorId,
            EndMessageTargetAnchorId);
        await CreateFlowAsync(
            harness,
            SignalHandlerEndFlowId,
            SignalHandlerId,
            EndId,
            SignalHandlerSourceAnchorId,
            EndSignalTargetAnchorId);
        return harness;
    }

    private static Task AddAnchorAsync(
        Harness harness,
        VisualStateId visualStateId,
        ConnectorAnchorId anchorId,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role,
        int insertionIndex) =>
        ExecuteCommittedAsync(
            harness,
            revision => new AddConnectorAnchorCommand(
                DocumentId,
                revision,
                visualStateId,
                anchorId,
                side,
                role,
                insertionIndex));

    private static Task CreateFlowAsync(
        Harness harness,
        SemanticElementId relationshipId,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId) =>
        ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnSequenceFlowCommand(
                DocumentId,
                revision,
                relationshipId,
                new VisualStateId($"{relationshipId.Value}:visual"),
                sourceId,
                targetId,
                sourceAnchorId,
                targetAnchorId));

    private static async Task AssertRejectedFlowIsAtomicAsync(
        Harness harness,
        SemanticElementId relationshipId,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId,
        string diagnosticCode)
    {
        var before = harness.Document.CaptureSnapshot();
        var historyBefore = harness.History.CaptureStatus();
        var rejected = await harness.History.ExecuteAsync(
            harness.Processor,
            new CreateBpmnSequenceFlowCommand(
                DocumentId,
                harness.Document.Revision,
                relationshipId,
                new VisualStateId($"{relationshipId.Value}:visual"),
                sourceId,
                targetId,
                sourceAnchorId,
                targetAnchorId));

        Assert.False(rejected.IsCommitted);
        Assert.Contains(rejected.Diagnostics, diagnostic =>
            diagnostic.Code == diagnosticCode);
        Assert.Same(before, harness.Document.CaptureSnapshot());
        Assert.Equal(historyBefore, harness.History.CaptureStatus());
        Assert.False(harness.Document.SemanticModel.TryGetRelationship(
            relationshipId,
            out _));
    }

    private static async Task AssertUndoRedoExactAsync(
        Harness harness,
        DocumentSnapshot before,
        DocumentSnapshot after)
    {
        var undo = await harness.History.UndoAsync(harness.Processor);
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        AssertAuthoritativeContentEqual(before, harness.Document.CaptureSnapshot());

        var redo = await harness.History.RedoAsync(harness.Processor);
        Assert.True(redo.IsCommitted, Diagnostics(redo.Diagnostics));
        AssertAuthoritativeContentEqual(after, harness.Document.CaptureSnapshot());
    }

    private static async Task ExecuteCommittedAsync(
        Harness harness,
        Func<DocumentRevision, ICommand> createCommand)
    {
        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            createCommand(harness.Document.Revision));
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
    }

    private static void AssertAttachedType(
        Harness harness,
        SemanticElementId elementId,
        SemanticTypeId expectedTypeId)
    {
        var element = Assert.Single(harness.Document.SemanticModel.Elements, candidate =>
            candidate.Id == elementId);
        Assert.Equal(expectedTypeId, element.TypeId);
        Assert.Equal(ActivityId, element.AttachedToElementId);
        Assert.Equal(
            harness.Document.SemanticModel.RootScopeId,
            harness.Document.SemanticModel.GetScope(elementId).Id);
    }

    private static void AssertAllBoundaryGeometry(
        Harness harness,
        RectD ownerBounds,
        BoundaryAttachmentPlacement messagePlacement,
        BoundaryAttachmentPlacement signalPlacement)
    {
        AssertBoundary(harness, TimerVisualId, TimerPlacement, ownerBounds);
        AssertBoundary(harness, MessageVisualId, messagePlacement, ownerBounds);
        AssertBoundary(harness, SignalVisualId, signalPlacement, ownerBounds);
    }

    private static void AssertSourceOnlyBoundaryAnchor(
        VisualStateSnapshot visual,
        ConnectorAnchorId expectedAnchorId)
    {
        var anchor = Assert.Single(visual.ConnectorAnchors);
        Assert.Equal(expectedAnchorId, anchor.Id);
        Assert.Equal(ConnectorAnchorRole.Source, anchor.Role);
    }

    private static void AssertRoundTrippedBoundary(
        DocumentSnapshot snapshot,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        SemanticTypeId expectedTypeId,
        BoundaryAttachmentPlacement expectedPlacement,
        string expectedName,
        bool cancelActivity,
        string expectedDescription,
        ConnectorAnchorId expectedSourceAnchorId)
    {
        var element = Assert.Single(snapshot.SemanticModel.Elements, candidate =>
            candidate.Id == elementId);
        Assert.Equal(expectedTypeId, element.TypeId);
        Assert.Equal(ActivityId, element.AttachedToElementId);
        Assert.Equal(
            expectedName,
            element.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            cancelActivity,
            element.Properties[BpmnSemanticProperties.CancelActivity].BooleanValue);
        Assert.Equal(
            expectedDescription,
            element.Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.Equal(
            snapshot.SemanticModel.RootScopeId,
            snapshot.SemanticModel.GetScope(elementId).Id);

        var visual = Assert.Single(snapshot.VisualModel.VisualStates, candidate =>
            candidate.Id == visualStateId);
        Assert.Equal(elementId, visual.SemanticElementId);
        Assert.Equal(expectedPlacement, visual.BoundaryAttachment);
        AssertSourceOnlyBoundaryAnchor(visual, expectedSourceAnchorId);
    }

    private static PipelineArtifacts RunPipeline(DocumentSnapshot snapshot)
    {
        var registration = BpmnPluginRegistration.N91;
        var projection = new ProjectionEngine(registration.ProjectionRules).Project(snapshot);
        Assert.True(projection.IsSuccessful, Diagnostics(projection.Diagnostics));
        var graph = Assert.IsType<ProjectedGraph>(projection.Graph);

        var layoutExecution = new LayoutEngine(registration.LayoutAlgorithms).Layout(
            graph,
            BpmnAlgorithmIds.DefaultLayout);
        Assert.True(
            layoutExecution.IsSuccessful,
            Diagnostics(layoutExecution.Diagnostics));
        var layout = Assert.IsType<LayoutResult>(layoutExecution.Result);

        var routingExecution = new RoutingEngine(registration.RoutingAlgorithms).Route(
            graph,
            layout,
            BpmnAlgorithmIds.DefaultRouting);
        Assert.True(
            routingExecution.IsSuccessful,
            Diagnostics(routingExecution.Diagnostics));
        return new PipelineArtifacts(
            graph,
            layout,
            Assert.IsType<RoutingResult>(routingExecution.Result));
    }

    private static void AssertBoundaryFlowRoute(
        PipelineArtifacts pipeline,
        DocumentSnapshot snapshot,
        SemanticElementId flowId,
        VisualStateId sourceVisualId,
        VisualStateId targetVisualId)
    {
        var edge = Assert.Single(pipeline.Graph.Edges, candidate =>
            candidate.Source.SemanticElementId == flowId);
        var route = Assert.Single(pipeline.Routing.Routes, candidate =>
            candidate.ProjectedEdgeId == edge.Id);
        var sourceVisual = Assert.Single(snapshot.VisualModel.VisualStates, visual =>
            visual.Id == sourceVisualId);
        var targetVisual = Assert.Single(snapshot.VisualModel.VisualStates, visual =>
            visual.Id == targetVisualId);
        Assert.Equal(
            ConnectorAnchorGeometryResolver.ResolvePoint(
                VisualBounds(sourceVisual),
                ConnectorAnchorSide.Right,
                0,
                1),
            route.SourceAnchor);
        Assert.Equal(
            ConnectorAnchorGeometryResolver.ResolvePoint(
                VisualBounds(targetVisual),
                ConnectorAnchorSide.Left,
                0,
                1),
            route.DestinationAnchor);
        Assert.Equal(route.SourceAnchor, route.Path[0]);
        Assert.Equal(route.DestinationAnchor, route.Path[^1]);
    }

    private static RectD VisualBounds(VisualStateSnapshot visual) =>
        new(visual.Position.X, visual.Position.Y, visual.Size.Width, visual.Size.Height);

    private static void AssertBoundary(
        Harness harness,
        VisualStateId visualStateId,
        BoundaryAttachmentPlacement expectedPlacement,
        RectD ownerBounds)
    {
        var visual = BoundaryVisual(harness, visualStateId);
        var expectedBounds = expectedPlacement.ResolveBounds(ownerBounds, BoundarySize);
        Assert.Equal(expectedPlacement, visual.BoundaryAttachment);
        Assert.Equal(expectedBounds, VisualBounds(visual));
        Assert.Equal(VisualPlacementMode.Manual, visual.PlacementMode);
    }

    private static VisualStateSnapshot BoundaryVisual(
        Harness harness,
        VisualStateId visualStateId) =>
        Assert.Single(harness.Document.VisualModel.VisualStates, visual =>
            visual.Id == visualStateId);

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
        Assert.Equal(
            expected.VisualModel.VisualStates.AsEnumerable(),
            actual.VisualModel.VisualStates.AsEnumerable());
    }

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History);

    private sealed record PipelineArtifacts(
        ProjectedGraph Graph,
        LayoutResult Layout,
        RoutingResult Routing);
}
