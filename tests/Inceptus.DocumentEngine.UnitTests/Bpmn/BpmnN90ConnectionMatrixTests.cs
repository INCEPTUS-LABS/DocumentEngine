using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN90ConnectionMatrixTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n9:connections");
    private static readonly SemanticElementId OwnerId = new("bpmn:n9:connections:owner");
    private static readonly SemanticElementId BoundaryId = new("bpmn:n9:connections:boundary");
    private static readonly SemanticElementId SourceId = new("bpmn:n9:connections:source");
    private static readonly SemanticElementId TargetId = new("bpmn:n9:connections:target");
    private static readonly SemanticElementId SmartTargetId =
        new("bpmn:n9:connections:smart-target");
    private static readonly SemanticElementId ExistingFlowId =
        new("bpmn:n9:connections:existing-flow");
    private static readonly VisualStateId OwnerVisualId =
        new("bpmn:n9:connections:owner:visual");
    private static readonly VisualStateId BoundaryVisualId =
        new("bpmn:n9:connections:boundary:visual");
    private static readonly VisualStateId SourceVisualId =
        new("bpmn:n9:connections:source:visual");
    private static readonly VisualStateId TargetVisualId =
        new("bpmn:n9:connections:target:visual");
    private static readonly VisualStateId SmartTargetVisualId =
        new("bpmn:n9:connections:smart-target:visual");
    private static readonly VisualStateId ExistingFlowVisualId =
        new("bpmn:n9:connections:existing-flow:visual");
    private static readonly ConnectorAnchorId BoundarySourceAnchorId =
        new("bpmn:n9:connections:boundary:source");
    private static readonly ConnectorAnchorId SourceAnchorOneId =
        new("bpmn:n9:connections:source:one");
    private static readonly ConnectorAnchorId SourceAnchorTwoId =
        new("bpmn:n9:connections:source:two");
    private static readonly ConnectorAnchorId TargetAnchorId =
        new("bpmn:n9:connections:target:anchor");

    [Fact]
    public void TimerBoundaryAnchorPolicyIsDynamicUnlimitedSourceOnlyOnEverySide()
    {
        var registry = new ElementConnectorAnchorPolicyRegistry(
            BpmnPluginRegistration.N90.ConnectorAnchorPolicies);
        var policy = registry.Resolve(BpmnSemanticTypes.TimerBoundaryEvent);

        foreach (var side in Enum.GetValues<ConnectorAnchorSide>())
        {
            var edge = policy.ForSide(side);
            Assert.Equal(ConnectorAnchorPolicyMode.DynamicUnlimited, edge.Mode);
            Assert.Equal(ConnectorAnchorRoleCapability.Source, edge.AllowedRoles);
            Assert.True(edge.Allows(ConnectorAnchorRole.Source));
            Assert.False(edge.Allows(ConnectorAnchorRole.Target));
        }
    }

    [Fact]
    public async Task OutgoingBoundarySmartTargetCommitsAnchorAndFlowInOneHistoryUnit()
    {
        var harness = CreateHarness();
        var before = harness.Document.CaptureSnapshot();
        var registration = Assert.Single(
            BpmnPluginRegistration.N90.AnchorConnectionCreationRegistrations);
        var proposedAnchorId = new ConnectorAnchorId(
            "bpmn:n9:connections:smart-target:proposed");
        var flowId = new SemanticElementId("bpmn:n9:connections:boundary-smart-flow");
        var flowVisualId = new VisualStateId(
            "bpmn:n9:connections:boundary-smart-flow:visual");

        Assert.True(registration.TargetEligibility!.CanAcquire(
            new AnchorConnectionTargetEligibilityRequest(
                before,
                before.Revision,
                BoundaryId,
                BoundaryVisualId,
                BoundarySourceAnchorId,
                SmartTargetId,
                SmartTargetVisualId)));
        var planResult = registration.CommandFactory.CreatePlan(
            new AnchorConnectionCreationRequest(
                before,
                before.Revision,
                BoundaryId,
                BoundaryVisualId,
                BoundarySourceAnchorId,
                SmartTargetId,
                SmartTargetVisualId,
                proposedAnchorId,
                new FixedIdentityProvider(flowId, flowVisualId),
                TargetAnchorAcquisitionResult.Proposed(
                    SmartTargetId,
                    SmartTargetVisualId,
                    ConnectorAnchorSide.Left,
                    proposedAnchorId,
                    new PointD(560d, 140d),
                    0)));

        Assert.True(planResult.Succeeded, Diagnostics(planResult.Diagnostics));
        var plan = Assert.IsType<AnchorConnectionCreationPlan>(planResult.Plan);
        Assert.IsType<CreateBpmnSequenceFlowWithTargetAnchorCommand>(plan.Command);
        var committed = await harness.History.ExecuteAsync(harness.Processor, plan.Command);

        Assert.True(committed.IsCommitted, Diagnostics(committed.Diagnostics));
        Assert.Equal(before.Revision.Increment(), harness.Document.Revision);
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);
        var after = harness.Document.CaptureSnapshot();
        var flow = Assert.Single(after.SemanticModel.Relationships,
            relationship => relationship.Id == flowId);
        Assert.Equal(BoundaryId, flow.SourceId);
        Assert.Equal(SmartTargetId, flow.TargetId);
        var targetVisual = Assert.Single(after.VisualModel.VisualStates,
            visual => visual.Id == SmartTargetVisualId);
        var targetAnchor = Assert.Single(targetVisual.ConnectorAnchors,
            anchor => anchor.Id == proposedAnchorId);
        Assert.Equal(ConnectorAnchorRole.Target, targetAnchor.Role);
        var connector = Assert.Single(after.VisualModel.VisualStates,
            visual => visual.Id == flowVisualId);
        Assert.Equal(BoundarySourceAnchorId, connector.SourceAnchorId);
        Assert.Equal(proposedAnchorId, connector.TargetAnchorId);

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        AssertAuthoritativeContentEqual(before, harness.Document.CaptureSnapshot());
        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        AssertAuthoritativeContentEqual(after, harness.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task IncomingBoundarySmartTargetRejectsWithoutOrphanAnchorRevisionOrHistory()
    {
        var harness = CreateHarness();
        var before = harness.Document.CaptureSnapshot();
        var historyBefore = harness.History.CaptureStatus();
        var registration = Assert.Single(
            BpmnPluginRegistration.N90.AnchorConnectionCreationRegistrations);
        var proposedAnchorId = new ConnectorAnchorId(
            "bpmn:n9:connections:boundary:forbidden-target");
        var flowId = new SemanticElementId("bpmn:n9:connections:incoming-boundary");
        var flowVisualId = new VisualStateId(
            "bpmn:n9:connections:incoming-boundary:visual");

        Assert.False(registration.TargetEligibility!.CanAcquire(
            new AnchorConnectionTargetEligibilityRequest(
                before,
                before.Revision,
                SourceId,
                SourceVisualId,
                SourceAnchorTwoId,
                BoundaryId,
                BoundaryVisualId)));
        var command = new CreateBpmnSequenceFlowWithTargetAnchorCommand(
            DocumentId,
            before.Revision,
            flowId,
            flowVisualId,
            SourceId,
            BoundaryId,
            SourceAnchorTwoId,
            BoundaryVisualId,
            proposedAnchorId,
            ConnectorAnchorSide.Left,
            0);
        var rejected = await harness.History.ExecuteAsync(harness.Processor, command);

        Assert.False(rejected.IsCommitted);
        Assert.Contains(rejected.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.IncomingBoundaryEvent);
        Assert.Same(before, harness.Document.CaptureSnapshot());
        Assert.Equal(historyBefore, harness.History.CaptureStatus());
        Assert.DoesNotContain(
            harness.Document.VisualModel.VisualStates.SelectMany(static visual =>
                visual.ConnectorAnchors),
            anchor => anchor.Id == proposedAnchorId);
        Assert.False(harness.Document.SemanticModel.TryGetRelationship(flowId, out _));
        Assert.False(harness.Document.VisualModel.TryGetVisualState(flowVisualId, out _));
    }

    [Fact]
    public async Task ReconnectRejectsBoundaryTargetButAcceptsBoundarySourceWithStableIdentity()
    {
        var targetHarness = CreateHarness();
        var targetBefore = targetHarness.Document.CaptureSnapshot();
        var factory = Assert.Single(
            BpmnPluginRegistration.N90.ConnectorEndpointReconnectionRegistrations)
            .CommandFactory;
        var rejectedPlan = factory.CreatePlan(new ConnectorEndpointReconnectionRequest(
            targetBefore,
            targetBefore.Revision,
            ExistingFlowId,
            ExistingFlowVisualId,
            ConnectorEndpointKind.Target,
            TargetId,
            TargetAnchorId,
            BoundaryId,
            BoundaryVisualId,
            BoundarySourceAnchorId));

        Assert.False(rejectedPlan.Succeeded);
        Assert.Contains(rejectedPlan.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.IncomingBoundaryEvent);
        Assert.Same(targetBefore, targetHarness.Document.CaptureSnapshot());
        Assert.Equal(0, targetHarness.History.CaptureStatus().EntryCount);

        var sourceHarness = CreateHarness();
        var sourceBefore = sourceHarness.Document.CaptureSnapshot();
        var acceptedPlan = factory.CreatePlan(new ConnectorEndpointReconnectionRequest(
            sourceBefore,
            sourceBefore.Revision,
            ExistingFlowId,
            ExistingFlowVisualId,
            ConnectorEndpointKind.Source,
            SourceId,
            SourceAnchorOneId,
            BoundaryId,
            BoundaryVisualId,
            BoundarySourceAnchorId));

        Assert.True(acceptedPlan.Succeeded, Diagnostics(acceptedPlan.Diagnostics));
        var plan = Assert.IsType<ConnectorEndpointReconnectionPlan>(acceptedPlan.Plan);
        var committed = await sourceHarness.History.ExecuteAsync(
            sourceHarness.Processor,
            plan.Command);
        Assert.True(committed.IsCommitted, Diagnostics(committed.Diagnostics));
        Assert.Equal(1, sourceHarness.History.CaptureStatus().EntryCount);
        var after = sourceHarness.Document.CaptureSnapshot();
        var relationship = Assert.Single(after.SemanticModel.Relationships,
            candidate => candidate.Id == ExistingFlowId);
        var connector = Assert.Single(after.VisualModel.VisualStates,
            visual => visual.Id == ExistingFlowVisualId);
        Assert.Equal(ExistingFlowId, relationship.Id);
        Assert.Equal(BoundaryId, relationship.SourceId);
        Assert.Equal(TargetId, relationship.TargetId);
        Assert.Equal(ExistingFlowVisualId, connector.Id);
        Assert.Equal(BoundarySourceAnchorId, connector.SourceAnchorId);
        Assert.Equal(TargetAnchorId, connector.TargetAnchorId);
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            after.VisualModel,
            SourceAnchorOneId));

        Assert.True((await sourceHarness.History.UndoAsync(sourceHarness.Processor)).IsCommitted);
        AssertAuthoritativeContentEqual(sourceBefore, sourceHarness.Document.CaptureSnapshot());
        Assert.True((await sourceHarness.History.RedoAsync(sourceHarness.Processor)).IsCommitted);
        AssertAuthoritativeContentEqual(after, sourceHarness.Document.CaptureSnapshot());
    }

    private static Harness CreateHarness()
    {
        var registration = BpmnPluginRegistration.N90;
        var anchorPolicies = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var creation = DocumentFactory.Create(CreateSnapshot(), anchorPolicies);
        Assert.True(creation.Succeeded, Diagnostics(creation.Diagnostics));
        var document = Assert.IsType<Document>(creation.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: anchorPolicies);
        return new Harness(document, processor, new HistoryManager(document));
    }

    private static DocumentSnapshot CreateSnapshot()
    {
        var revision = DocumentRevision.Zero;
        var ownerBounds = new RectD(100d, 100d, 140d, 90d);
        var attachment = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Bottom,
            0.5d);
        var boundaryBounds = attachment.ResolveBounds(ownerBounds, new SizeD(36d, 36d));
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                [
                    BpmnSemanticFactory.CreateTask(OwnerId, "OWNER", "Owner", 1),
                    BpmnSemanticFactory.CreateTimerBoundaryEvent(
                        BoundaryId,
                        OwnerId,
                        "Timeout",
                        "PT5M"),
                    BpmnSemanticFactory.CreateTask(SourceId, "SOURCE", "Source", 2),
                    BpmnSemanticFactory.CreateTask(TargetId, "TARGET", "Target", 3),
                    BpmnSemanticFactory.CreateTask(
                        SmartTargetId,
                        "SMART_TARGET",
                        "Smart target",
                        4),
                ],
                [
                    BpmnSemanticFactory.CreateSequenceFlow(
                        ExistingFlowId,
                        SourceId,
                        TargetId,
                        "Existing flow"),
                ]),
            new VisualModelSnapshot(
                DocumentId,
                revision,
                [
                    Node(OwnerVisualId, OwnerId, ownerBounds),
                    Node(
                        BoundaryVisualId,
                        BoundaryId,
                        boundaryBounds,
                        [new ConnectorAnchor(
                            BoundarySourceAnchorId,
                            ConnectorAnchorSide.Right,
                            ConnectorAnchorRole.Source,
                            0)],
                        attachment),
                    Node(
                        SourceVisualId,
                        SourceId,
                        new RectD(300d, 100d, 120d, 80d),
                        [
                            new ConnectorAnchor(
                                SourceAnchorOneId,
                                ConnectorAnchorSide.Right,
                                ConnectorAnchorRole.Source,
                                0),
                            new ConnectorAnchor(
                                SourceAnchorTwoId,
                                ConnectorAnchorSide.Right,
                                ConnectorAnchorRole.Source,
                                1),
                        ]),
                    Node(
                        TargetVisualId,
                        TargetId,
                        new RectD(500d, 100d, 120d, 80d),
                        [new ConnectorAnchor(
                            TargetAnchorId,
                            ConnectorAnchorSide.Left,
                            ConnectorAnchorRole.Target,
                            0)]),
                    Node(
                        SmartTargetVisualId,
                        SmartTargetId,
                        new RectD(500d, 300d, 120d, 80d)),
                    new VisualStateSnapshot(
                        ExistingFlowVisualId,
                        ExistingFlowId,
                        new PointD(0d, 0d),
                        new SizeD(0d, 0d),
                        VisualPlacementMode.Manual,
                        sourceAnchorId: SourceAnchorOneId,
                        targetAnchorId: TargetAnchorId),
                ]),
            new DocumentMetadataSnapshot(DocumentId, revision));
    }

    private static VisualStateSnapshot Node(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        RectD bounds,
        IEnumerable<ConnectorAnchor>? anchors = null,
        BoundaryAttachmentPlacement? attachment = null) =>
        new(
            visualStateId,
            semanticElementId,
            bounds.TopLeft,
            bounds.Size,
            VisualPlacementMode.Pinned,
            connectorAnchors: anchors,
            boundaryAttachment: attachment);

    private static void AssertAuthoritativeContentEqual(
        DocumentSnapshot expected,
        DocumentSnapshot actual)
    {
        Assert.Equal(expected.SemanticModel.Elements.AsEnumerable(),
            actual.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(expected.SemanticModel.Relationships.AsEnumerable(),
            actual.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(expected.SemanticModel.NestedScopes.AsEnumerable(),
            actual.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(expected.SemanticModel.ScopeMemberships.AsEnumerable(),
            actual.SemanticModel.ScopeMemberships.AsEnumerable());
        Assert.Equal(expected.VisualModel.VisualStates.AsEnumerable(),
            actual.VisualModel.VisualStates.AsEnumerable());
    }

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class FixedIdentityProvider(
        SemanticElementId semanticElementId,
        VisualStateId visualStateId) : IDocumentCreationIdentityProvider
    {
        public DocumentCreationIdentity CreateIdentity() =>
            new(semanticElementId, visualStateId);
    }

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History);
}
