using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN31SmartTargetAnchorCreationTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n31");
    private static readonly SemanticElementId SourceId = new("source");
    private static readonly SemanticElementId TargetId = new("target");
    private static readonly VisualStateId SourceVisualId = new("source-visual");
    private static readonly VisualStateId TargetVisualId = new("target-visual");
    private static readonly ConnectorAnchorId SourceAnchorId = new("source-anchor");
    private static readonly ConnectorAnchorId ProposedAnchorId = new("proposed-target");
    private static readonly SemanticElementId FlowId = new("flow");
    private static readonly VisualStateId FlowVisualId = new("flow-visual");

    [Fact]
    public void N31AddsAtomicCommandAndOptInWithoutChangingHistoricalRegistrations()
    {
        var n3Registration = Assert.Single(
            BpmnPluginRegistration.N3.AnchorConnectionCreationRegistrations);
        var n31Registration = Assert.Single(
            BpmnPluginRegistration.N31.AnchorConnectionCreationRegistrations);

        Assert.Null(n3Registration.TargetEligibility);
        Assert.NotNull(n31Registration.TargetEligibility);
        Assert.Equal(n3Registration.CreationId, n31Registration.CreationId);
        Assert.Contains(BpmnPluginRegistration.N31.CommandHandlers, registration =>
            registration.TypeId ==
                CreateBpmnSequenceFlowWithTargetAnchorCommand.KnownTypeId);
        Assert.DoesNotContain(BpmnPluginRegistration.N3.CommandHandlers, registration =>
            registration.TypeId ==
                CreateBpmnSequenceFlowWithTargetAnchorCommand.KnownTypeId);
        Assert.Equal(
            BpmnPluginRegistration.N3.ConnectorEndpointReconnectionRegistrations,
            BpmnPluginRegistration.N31.ConnectorEndpointReconnectionRegistrations);
    }

    [Fact]
    public void FactoryKeepsExistingTargetCommandAndUsesAtomicCommandForProposal()
    {
        var document = CreateSnapshot(includeExistingTarget: true);
        var registration = Assert.Single(
            BpmnPluginRegistration.N31.AnchorConnectionCreationRegistrations);
        var provider = new FixedIdentityProvider();
        var existingAnchor = new ConnectorAnchorId("existing-target");
        var existing = registration.CommandFactory.CreatePlan(new AnchorConnectionCreationRequest(
            document,
            document.Revision,
            SourceId,
            SourceVisualId,
            SourceAnchorId,
            TargetId,
            TargetVisualId,
            existingAnchor,
            provider,
            TargetAnchorAcquisitionResult.Existing(
                TargetId,
                TargetVisualId,
                ConnectorAnchorSide.Left,
                existingAnchor,
                new PointD(300d, 50d))));
        var proposed = registration.CommandFactory.CreatePlan(new AnchorConnectionCreationRequest(
            document,
            document.Revision,
            SourceId,
            SourceVisualId,
            SourceAnchorId,
            TargetId,
            TargetVisualId,
            ProposedAnchorId,
            provider,
            TargetAnchorAcquisitionResult.Proposed(
                TargetId,
                TargetVisualId,
                ConnectorAnchorSide.Left,
                ProposedAnchorId,
                new PointD(300d, 50d),
                0)));

        Assert.IsType<CreateBpmnSequenceFlowCommand>(
            Assert.IsType<AnchorConnectionCreationPlan>(existing.Plan).Command);
        Assert.IsType<CreateBpmnSequenceFlowWithTargetAnchorCommand>(
            Assert.IsType<AnchorConnectionCreationPlan>(proposed.Plan).Command);
    }

    [Fact]
    public async Task AtomicCommitUndoAndRedoPreserveOneIntentAndExactIds()
    {
        var harness = CreateHarness();
        var before = harness.Document.CaptureSnapshot();
        var beforeHistory = harness.History.CaptureStatus();

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            Command(harness.Document.Revision));

        Assert.True(result.IsCommitted);
        Assert.Equal(before.Revision.Increment(), harness.Document.Revision);
        Assert.Equal(beforeHistory.EntryCount + 1, harness.History.CaptureStatus().EntryCount);
        Assert.True(harness.Document.SemanticModel.TryGetRelationship(FlowId, out var flow));
        Assert.Equal(SourceId, flow!.SourceId);
        Assert.Equal(TargetId, flow.TargetId);
        Assert.True(harness.Document.VisualModel.TryGetVisualState(
            TargetVisualId,
            out var targetVisual));
        var anchor = Assert.Single(targetVisual!.ConnectorAnchors, item =>
            item.Id == ProposedAnchorId);
        Assert.Equal(ConnectorAnchorRole.Target, anchor.Role);
        Assert.True(harness.Document.VisualModel.TryGetVisualState(FlowVisualId, out var connector));
        Assert.Equal(SourceAnchorId, connector!.SourceAnchorId);
        Assert.Equal(ProposedAnchorId, connector.TargetAnchorId);

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.False(harness.Document.SemanticModel.TryGetRelationship(FlowId, out _));
        Assert.False(harness.Document.VisualModel.TryGetVisualState(FlowVisualId, out _));
        Assert.DoesNotContain(
            harness.Document.VisualModel.VisualStates.Single(item =>
                item.Id == TargetVisualId).ConnectorAnchors,
            item => item.Id == ProposedAnchorId);

        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        Assert.True(harness.Document.SemanticModel.TryGetRelationship(FlowId, out _));
        targetVisual = harness.Document.VisualModel.VisualStates.Single(item =>
            item.Id == TargetVisualId);
        Assert.Contains(targetVisual.ConnectorAnchors, item => item.Id == ProposedAnchorId);
        connector = harness.Document.VisualModel.VisualStates.Single(item =>
            item.Id == FlowVisualId);
        Assert.Equal(ProposedAnchorId, connector.TargetAnchorId);
    }

    [Fact]
    public async Task InvalidTargetPolicyRejectsWithoutOrphanAnchorRevisionOrHistory()
    {
        var harness = CreateHarness(targetType: BpmnSemanticTypes.StartEvent);
        var before = harness.Document.CaptureSnapshot();
        var history = harness.History.CaptureStatus();

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            Command(harness.Document.Revision));

        Assert.False(result.IsCommitted);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.Equal(history, harness.History.CaptureStatus());
        Assert.DoesNotContain(
            harness.Document.VisualModel.VisualStates.SelectMany(item =>
                item.ConnectorAnchors),
            item => item.Id == ProposedAnchorId);
    }

    [Fact]
    public async Task SeparateN314ConnectionDeletionPreservesSmartAnchorHistoryDistinction()
    {
        var registration = BpmnPluginRegistration.N314;
        var policyProvider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.Create(CreateSnapshot(), policyProvider);
        var document = Assert.IsType<Document>(construction.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: policyProvider);
        var history = new HistoryManager(document);

        Assert.True((await history.ExecuteAsync(
            processor,
            Command(document.Revision))).IsCommitted);
        var createdConnector = document.VisualModel.VisualStates.Single(item =>
            item.Id == FlowVisualId);
        Assert.True((await history.ExecuteAsync(
            processor,
            new DeleteBpmnSequenceFlowCommand(
                document.DocumentId,
                document.Revision,
                FlowId,
                FlowVisualId,
                SourceId,
                TargetId,
                SourceAnchorId,
                ProposedAnchorId))).IsCommitted);

        var afterDelete = document.CaptureSnapshot();
        Assert.False(afterDelete.SemanticModel.TryGetRelationship(FlowId, out _));
        Assert.Contains(
            afterDelete.VisualModel.VisualStates.Single(item => item.Id == TargetVisualId)
                .ConnectorAnchors,
            item => item.Id == ProposedAnchorId);
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            afterDelete.VisualModel,
            ProposedAnchorId));

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Equal(
            createdConnector,
            document.VisualModel.VisualStates.Single(item => item.Id == FlowVisualId));
        Assert.Contains(
            document.VisualModel.VisualStates.Single(item => item.Id == TargetVisualId)
                .ConnectorAnchors,
            item => item.Id == ProposedAnchorId);

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.False(document.SemanticModel.TryGetRelationship(FlowId, out _));
        Assert.DoesNotContain(
            document.VisualModel.VisualStates.Single(item => item.Id == TargetVisualId)
                .ConnectorAnchors,
            item => item.Id == ProposedAnchorId);
    }

    private static CreateBpmnSequenceFlowWithTargetAnchorCommand Command(
        DocumentRevision revision) => new(
            DocumentId,
            revision,
            FlowId,
            FlowVisualId,
            SourceId,
            TargetId,
            SourceAnchorId,
            TargetVisualId,
            ProposedAnchorId,
            ConnectorAnchorSide.Left,
            0);

    private static Harness CreateHarness(SemanticTypeId? targetType = null)
    {
        var registration = BpmnPluginRegistration.N31;
        var policyProvider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.Create(
            CreateSnapshot(targetType: targetType),
            policyProvider);
        var document = Assert.IsType<Document>(construction.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: policyProvider);
        return new(document, processor, new HistoryManager(document));
    }

    private static DocumentSnapshot CreateSnapshot(
        bool includeExistingTarget = false,
        SemanticTypeId? targetType = null)
    {
        var targetAnchors = includeExistingTarget
            ? new[]
            {
                new ConnectorAnchor(
                    new ConnectorAnchorId("existing-target"),
                    ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target,
                    0),
            }
            : [];
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new SemanticElementSnapshot(SourceId, BpmnSemanticTypes.Task),
                    new SemanticElementSnapshot(
                        TargetId,
                        targetType ?? BpmnSemanticTypes.Task),
                ]),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new VisualStateSnapshot(
                        SourceVisualId,
                        SourceId,
                        new PointD(0d, 0d),
                        new SizeD(120d, 80d),
                        VisualPlacementMode.Pinned,
                        connectorAnchors:
                        [
                            new ConnectorAnchor(
                                SourceAnchorId,
                                ConnectorAnchorSide.Right,
                                ConnectorAnchorRole.Source,
                                0),
                        ]),
                    new VisualStateSnapshot(
                        TargetVisualId,
                        TargetId,
                        new PointD(300d, 0d),
                        new SizeD(120d, 80d),
                        VisualPlacementMode.Pinned,
                        connectorAnchors: targetAnchors),
                ]),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
    }

    private sealed class FixedIdentityProvider : IDocumentCreationIdentityProvider
    {
        public DocumentCreationIdentity CreateIdentity() =>
            new(FlowId, FlowVisualId);
    }

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History);
}
