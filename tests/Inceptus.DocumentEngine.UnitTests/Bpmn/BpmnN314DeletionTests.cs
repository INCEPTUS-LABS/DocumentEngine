using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Deletion;
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

public sealed class BpmnN314DeletionTests
{
    [Theory]
    [InlineData("demo:bpmn:start-event", "demo:bpmn:visual:start-event")]
    [InlineData("demo:bpmn:review-order", "demo:bpmn:visual:review-order")]
    [InlineData("demo:bpmn:approved-gateway", "demo:bpmn:visual:approved-gateway")]
    [InlineData("demo:bpmn:parallel-split", "demo:bpmn:visual:parallel-split")]
    [InlineData("demo:bpmn:inclusive-split", "demo:bpmn:visual:inclusive-split")]
    [InlineData("demo:bpmn:end-event", "demo:bpmn:visual:end-event")]
    public async Task EverySupportedFlowNodeDeletesItsExactIncidentCascade(
        string elementIdValue,
        string visualStateIdValue)
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var before = composition.Document.CaptureSnapshot();
        var elementId = new SemanticElementId(elementIdValue);
        var visualStateId = new VisualStateId(visualStateIdValue);
        var incident = before.SemanticModel.Relationships.Where(relationship =>
                relationship.SourceId == elementId || relationship.TargetId == elementId)
            .ToArray();
        var survivingAnchors = incident.SelectMany(relationship =>
            {
                var connector = before.VisualModel.VisualStates.Single(visual =>
                    visual.SemanticElementId == relationship.Id);
                var anchors = new List<ConnectorAnchorId>(2);
                if (relationship.SourceId != elementId)
                {
                    anchors.Add(Assert.IsType<ConnectorAnchorId>(
                        connector.SourceAnchorId));
                }

                if (relationship.TargetId != elementId)
                {
                    anchors.Add(Assert.IsType<ConnectorAnchorId>(
                        connector.TargetAnchorId));
                }

                return anchors;
            })
            .Distinct()
            .ToArray();
        var history = new HistoryManager(composition.Document);

        var result = await history.ExecuteAsync(
            Processor(composition),
            new DeleteBpmnFlowNodeCommand(
                before.DocumentId,
                before.Revision,
                elementId,
                visualStateId));

        Assert.True(result.IsCommitted);
        Assert.Equal(before.Revision.Increment(), result.CommittedRevision);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
        var after = composition.Document.CaptureSnapshot();
        Assert.False(after.SemanticModel.TryGetElement(elementId, out _));
        Assert.False(after.VisualModel.TryGetVisualState(visualStateId, out _));
        Assert.All(incident, relationship =>
        {
            Assert.False(after.SemanticModel.TryGetRelationship(relationship.Id, out _));
            Assert.DoesNotContain(after.VisualModel.VisualStates, visual =>
                visual.SemanticElementId == relationship.Id);
        });
        Assert.All(survivingAnchors, anchorId =>
        {
            Assert.Contains(after.VisualModel.VisualStates.SelectMany(
                static visual => visual.ConnectorAnchors), anchor => anchor.Id == anchorId);
            Assert.False(ConnectorAnchorOccupancy.IsOccupied(
                after.VisualModel,
                anchorId));
        });
        Assert.Empty(DocumentInvariantValidator.Validate(
            after,
            composition.Configuration.ConnectorAnchorPolicyProvider));
    }

    [Fact]
    public void RegistrationAddsGenericDeletionCapabilityAndBothAtomicCommands()
    {
        var registration = BpmnPluginRegistration.N314;

        Assert.Contains(registration.CommandHandlers, candidate =>
            candidate.TypeId == DeleteBpmnSequenceFlowCommand.KnownTypeId);
        Assert.Contains(registration.CommandHandlers, candidate =>
            candidate.TypeId == DeleteBpmnFlowNodeCommand.KnownTypeId);
        Assert.Contains(registration.HistoryPolicies, candidate =>
            candidate.TypeId == DeleteBpmnSequenceFlowCommand.KnownTypeId);
        Assert.Contains(registration.HistoryPolicies, candidate =>
            candidate.TypeId == DeleteBpmnFlowNodeCommand.KnownTypeId);
        Assert.Single(registration.DiagramDeletionRegistrations);
        Assert.Empty(BpmnPluginRegistration.N31.DiagramDeletionRegistrations);
    }

    [Fact]
    public async Task DeleteConnectionCommitsOnceAndPreservesBothEndpointAnchors()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var before = composition.Document.CaptureSnapshot();
        var command = ConnectionDeletion(before, BpmnDemoPipeline.ThirdSequenceFlowId,
            BpmnDemoPipeline.ThirdSequenceFlowVisualId);
        var sourceOwner = OwnerOf(before, command.ExpectedSourceAnchorId);
        var targetOwner = OwnerOf(before, command.ExpectedTargetAnchorId);
        var events = new RecordingSubscriber();
        var history = new HistoryManager(composition.Document);

        var result = await history.ExecuteAsync(Processor(composition, events), command);
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);

        Assert.True(result.IsCommitted);
        Assert.Equal(before.Revision.Increment(), result.CommittedRevision);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
        Assert.Single(events.Events);
        var after = composition.Document.CaptureSnapshot();
        Assert.False(after.SemanticModel.TryGetRelationship(command.RelationshipId, out _));
        Assert.False(after.VisualModel.TryGetVisualState(command.ConnectorVisualStateId, out _));
        Assert.Equal(sourceOwner, AssertVisual(after, sourceOwner.Id));
        Assert.Equal(targetOwner, AssertVisual(after, targetOwner.Id));
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            after.VisualModel,
            command.ExpectedSourceAnchorId));
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            after.VisualModel,
            command.ExpectedTargetAnchorId));
        Assert.Empty(DocumentInvariantValidator.Validate(
            after,
            composition.Configuration.ConnectorAnchorPolicyProvider));
        Assert.Equal(NodeGeometryPipelineImpact.PreserveAll,
            events.Events.Single().NodeGeometryImpact);
    }

    [Fact]
    public async Task DeleteFlowNodeCascadesIncomingOutgoingAndPreservesOppositeAnchors()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var before = composition.Document.CaptureSnapshot();
        var incident = before.SemanticModel.Relationships.Where(relationship =>
                relationship.SourceId == BpmnDemoPipeline.TaskId ||
                relationship.TargetId == BpmnDemoPipeline.TaskId)
            .ToArray();
        Assert.Equal(2, incident.Length);
        var survivingAnchorIds = incident.SelectMany(relationship =>
            {
                var connector = before.VisualModel.VisualStates.Single(visual =>
                    visual.SemanticElementId == relationship.Id);
                return relationship.SourceId == BpmnDemoPipeline.TaskId
                    ? new[] { connector.TargetAnchorId! }
                    : new[] { connector.SourceAnchorId! };
            })
            .ToArray();
        var survivingVisuals = before.VisualModel.VisualStates
            .Where(visual => visual.SemanticElementId != BpmnDemoPipeline.TaskId &&
                !incident.Select(static relationship => relationship.Id)
                    .Contains(visual.SemanticElementId))
            .ToArray();
        var history = new HistoryManager(composition.Document);
        var events = new RecordingSubscriber();

        var result = await history.ExecuteAsync(
            Processor(composition, events),
            new DeleteBpmnFlowNodeCommand(
                before.DocumentId,
                before.Revision,
                BpmnDemoPipeline.TaskId,
                BpmnDemoPipeline.TaskVisualId));
        await CommandProcessor.WaitForEventDispatchIdleAsync(composition.Document);

        Assert.True(result.IsCommitted);
        Assert.Single(events.Events);
        var after = composition.Document.CaptureSnapshot();
        Assert.False(after.SemanticModel.TryGetElement(BpmnDemoPipeline.TaskId, out _));
        Assert.False(after.VisualModel.TryGetVisualState(BpmnDemoPipeline.TaskVisualId, out _));
        foreach (var relationship in incident)
        {
            Assert.False(after.SemanticModel.TryGetRelationship(relationship.Id, out _));
            Assert.DoesNotContain(after.VisualModel.VisualStates, visual =>
                visual.SemanticElementId == relationship.Id);
        }

        Assert.True(survivingVisuals.SequenceEqual(after.VisualModel.VisualStates));
        foreach (var anchorId in survivingAnchorIds)
        {
            Assert.Contains(after.VisualModel.VisualStates.SelectMany(
                static visual => visual.ConnectorAnchors), anchor => anchor.Id == anchorId);
            Assert.False(ConnectorAnchorOccupancy.IsOccupied(after.VisualModel, anchorId));
        }

        var impact = Assert.IsType<NodeGeometryPipelineImpact>(
            events.Events.Single().NodeGeometryImpact);
        Assert.Equal(
            BpmnDemoPipeline.TaskVisualId,
            Assert.Single(impact.RemovedVisualStateIds));
        Assert.Empty(DocumentInvariantValidator.Validate(
            after,
            composition.Configuration.ConnectorAnchorPolicyProvider));
    }

    [Fact]
    public async Task DeleteTaskFreesSurvivingAnchorsForExactN2Reuse()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var before = composition.Document.CaptureSnapshot();
        var incoming = AssertVisual(before, BpmnDemoPipeline.FirstSequenceFlowVisualId);
        var outgoing = AssertVisual(before, BpmnDemoPipeline.SecondSequenceFlowVisualId);
        var sourceAnchorId = Assert.IsType<ConnectorAnchorId>(incoming.SourceAnchorId);
        var targetAnchorId = Assert.IsType<ConnectorAnchorId>(outgoing.TargetAnchorId);
        var processor = Processor(composition);

        var deleted = await processor.ExecuteAsync(
            composition.Document,
            new DeleteBpmnFlowNodeCommand(
                before.DocumentId,
                before.Revision,
                BpmnDemoPipeline.TaskId,
                BpmnDemoPipeline.TaskVisualId));
        Assert.True(deleted.IsCommitted);
        var afterDelete = composition.Document.CaptureSnapshot();
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            afterDelete.VisualModel,
            sourceAnchorId));
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            afterDelete.VisualModel,
            targetAnchorId));

        var replacementId = new SemanticElementId("test:n314:node-delete-reuse");
        var replacementVisualId = new VisualStateId(
            "test:n314:node-delete-reuse:visual");
        var reused = await processor.ExecuteAsync(
            composition.Document,
            new CreateBpmnSequenceFlowCommand(
                afterDelete.DocumentId,
                afterDelete.Revision,
                replacementId,
                replacementVisualId,
                BpmnDemoPipeline.StartEventId,
                BpmnDemoPipeline.ExclusiveGatewayId,
                sourceAnchorId,
                targetAnchorId));

        Assert.True(reused.IsCommitted);
        var afterReuse = composition.Document.CaptureSnapshot();
        Assert.True(afterReuse.SemanticModel.TryGetRelationship(replacementId, out _));
        Assert.Equal(1, ConnectorAnchorOccupancy.CountEndpointReferences(
            afterReuse.VisualModel,
            sourceAnchorId));
        Assert.Equal(1, ConnectorAnchorOccupancy.CountEndpointReferences(
            afterReuse.VisualModel,
            targetAnchorId));
        Assert.Empty(DocumentInvariantValidator.Validate(
            afterReuse,
            composition.Configuration.ConnectorAnchorPolicyProvider));
    }

    [Fact]
    public async Task GatewayCascadeRemovesThreeIncidentFlowsInOneHistoryUnit()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var before = composition.Document.CaptureSnapshot();
        var incidentIds = before.SemanticModel.Relationships
            .Where(relationship =>
                relationship.SourceId == BpmnDemoPipeline.ExclusiveGatewayId ||
                relationship.TargetId == BpmnDemoPipeline.ExclusiveGatewayId)
            .Select(static relationship => relationship.Id)
            .ToArray();
        Assert.Equal(3, incidentIds.Length);
        var history = new HistoryManager(composition.Document);

        var deleted = await history.ExecuteAsync(
            Processor(composition),
            new DeleteBpmnFlowNodeCommand(
                before.DocumentId,
                before.Revision,
                BpmnDemoPipeline.ExclusiveGatewayId,
                BpmnDemoPipeline.ExclusiveGatewayVisualId));

        Assert.True(deleted.IsCommitted);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
        var after = composition.Document.CaptureSnapshot();
        Assert.All(incidentIds, id =>
            Assert.False(after.SemanticModel.TryGetRelationship(id, out _)));
        Assert.DoesNotContain(after.VisualModel.VisualStates, visual =>
            incidentIds.Contains(visual.SemanticElementId));
    }

    [Fact]
    public async Task SelfLoopIsRemovedExactlyOnceWithItsOwningNode()
    {
        var (document, policyProvider, nodeId, nodeVisualId, relationshipId) =
            CreateSelfLoopDocument();
        var before = document.CaptureSnapshot();
        var history = new HistoryManager(document);

        var deleted = await history.ExecuteAsync(
            Processor(policyProvider),
            new DeleteBpmnFlowNodeCommand(
                before.DocumentId,
                before.Revision,
                nodeId,
                nodeVisualId));

        Assert.True(deleted.IsCommitted);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
        var after = document.CaptureSnapshot();
        Assert.Empty(after.SemanticModel.Elements);
        Assert.Empty(after.SemanticModel.Relationships);
        Assert.Empty(after.VisualModel.VisualStates);
        Assert.False(after.SemanticModel.TryGetRelationship(relationshipId, out _));
        Assert.Empty(DocumentInvariantValidator.Validate(after, policyProvider));
    }

    [Fact]
    public async Task ConnectionAndNodeUndoRedoRestoreExactPersistentSnapshots()
    {
        var connectionComposition = await BpmnModelerTestComposition.CreateDemoAsync();
        var connectionBefore = connectionComposition.Document.CaptureSnapshot();
        var connectionHistory = new HistoryManager(connectionComposition.Document);
        var connectionProcessor = Processor(connectionComposition);
        var connectionDelete = await connectionHistory.ExecuteAsync(
            connectionProcessor,
            ConnectionDeletion(
                connectionBefore,
                BpmnDemoPipeline.ThirdSequenceFlowId,
                BpmnDemoPipeline.ThirdSequenceFlowVisualId));
        Assert.True(connectionDelete.IsCommitted);
        var connectionDeleted = connectionComposition.Document.CaptureSnapshot();
        Assert.True((await connectionHistory.UndoAsync(connectionProcessor)).IsCommitted);
        AssertEquivalentIgnoringRevision(
            connectionBefore,
            connectionComposition.Document.CaptureSnapshot());
        Assert.True((await connectionHistory.RedoAsync(connectionProcessor)).IsCommitted);
        AssertEquivalentIgnoringRevision(
            connectionDeleted,
            connectionComposition.Document.CaptureSnapshot());

        var nodeComposition = await BpmnModelerTestComposition.CreateDemoAsync();
        var nodeBefore = nodeComposition.Document.CaptureSnapshot();
        var nodeHistory = new HistoryManager(nodeComposition.Document);
        var nodeProcessor = Processor(nodeComposition);
        var nodeDelete = await nodeHistory.ExecuteAsync(
            nodeProcessor,
            new DeleteBpmnFlowNodeCommand(
                nodeBefore.DocumentId,
                nodeBefore.Revision,
                BpmnDemoPipeline.TaskId,
                BpmnDemoPipeline.TaskVisualId));
        Assert.True(nodeDelete.IsCommitted);
        var nodeDeleted = nodeComposition.Document.CaptureSnapshot();
        Assert.True((await nodeHistory.UndoAsync(nodeProcessor)).IsCommitted);
        AssertEquivalentIgnoringRevision(
            nodeBefore,
            nodeComposition.Document.CaptureSnapshot());
        Assert.True((await nodeHistory.RedoAsync(nodeProcessor)).IsCommitted);
        AssertEquivalentIgnoringRevision(
            nodeDeleted,
            nodeComposition.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task GenericFactoryRejectsMismatchedConnectionVisual()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var document = composition.Document.CaptureSnapshot();
        var catalog = composition.DeletionCatalog;
        var request = new DiagramDeletionRequest(
            document,
            document.Revision,
            DiagramDeletionTargetKind.Connection,
            BpmnDemoPipeline.ThirdSequenceFlowId,
            BpmnDemoPipeline.TaskVisualId);

        Assert.Empty(catalog.GetMatchingRegistrations(request));
    }

    [Fact]
    public async Task MissingOrStaleDeletionTargetsAreRejectedWithoutMutation()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var before = composition.Document.CaptureSnapshot();
        var processor = Processor(composition);
        var valid = ConnectionDeletion(
            before,
            BpmnDemoPipeline.ThirdSequenceFlowId,
            BpmnDemoPipeline.ThirdSequenceFlowVisualId);

        var missingRelationship = await processor.ExecuteAsync(
            composition.Document,
            new DeleteBpmnSequenceFlowCommand(
                before.DocumentId,
                before.Revision,
                new SemanticElementId("test:n314:missing-flow"),
                valid.ConnectorVisualStateId,
                valid.ExpectedSourceId,
                valid.ExpectedTargetId,
                valid.ExpectedSourceAnchorId,
                valid.ExpectedTargetAnchorId));
        var staleBinding = await processor.ExecuteAsync(
            composition.Document,
            new DeleteBpmnSequenceFlowCommand(
                before.DocumentId,
                before.Revision,
                valid.RelationshipId,
                valid.ConnectorVisualStateId,
                valid.ExpectedSourceId,
                valid.ExpectedTargetId,
                new ConnectorAnchorId("test:n314:stale-source-anchor"),
                valid.ExpectedTargetAnchorId));
        var missingNode = await processor.ExecuteAsync(
            composition.Document,
            new DeleteBpmnFlowNodeCommand(
                before.DocumentId,
                before.Revision,
                new SemanticElementId("test:n314:missing-node"),
                BpmnDemoPipeline.TaskVisualId));

        Assert.False(missingRelationship.IsCommitted);
        Assert.False(staleBinding.IsCommitted);
        Assert.False(missingNode.IsCommitted);
        Assert.All(
            new[] { missingRelationship, staleBinding },
            result => Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code ==
                    BpmnCommandDiagnosticCodes.SequenceFlowDeletionInvalid));
        Assert.Contains(missingNode.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.FlowNodeDeletionInvalid);
        Assert.Same(before, composition.Document.CaptureSnapshot());
    }

    private static DeleteBpmnSequenceFlowCommand ConnectionDeletion(
        DocumentSnapshot document,
        SemanticElementId relationshipId,
        VisualStateId visualStateId)
    {
        var relationship = Assert.IsType<SemanticRelationshipSnapshot>(
            document.SemanticModel.Relationships.Single(candidate =>
                candidate.Id == relationshipId));
        var visual = AssertVisual(document, visualStateId);
        return new DeleteBpmnSequenceFlowCommand(
            document.DocumentId,
            document.Revision,
            relationship.Id,
            visual.Id,
            relationship.SourceId,
            relationship.TargetId,
            Assert.IsType<ConnectorAnchorId>(visual.SourceAnchorId),
            Assert.IsType<ConnectorAnchorId>(visual.TargetAnchorId));
    }

    private static VisualStateSnapshot OwnerOf(
        DocumentSnapshot document,
        ConnectorAnchorId anchorId) => document.VisualModel.VisualStates.Single(visual =>
            visual.ConnectorAnchors.Any(anchor => anchor.Id == anchorId));

    private static VisualStateSnapshot AssertVisual(
        DocumentSnapshot document,
        VisualStateId visualStateId) => Assert.IsType<VisualStateSnapshot>(
        document.VisualModel.VisualStates.Single(visual => visual.Id == visualStateId));

    private static void AssertEquivalentIgnoringRevision(
        DocumentSnapshot expected,
        DocumentSnapshot actual)
    {
        Assert.Equal(expected.DocumentId, actual.DocumentId);
        Assert.True(expected.SemanticModel.Elements.SequenceEqual(
            actual.SemanticModel.Elements));
        Assert.True(expected.SemanticModel.Relationships.SequenceEqual(
            actual.SemanticModel.Relationships));
        Assert.True(expected.VisualModel.VisualStates.SequenceEqual(
            actual.VisualModel.VisualStates));
        Assert.Equal(expected.Metadata.SystemManagedProperties,
            actual.Metadata.SystemManagedProperties);
        Assert.Equal(expected.Metadata.ExtensionProperties,
            actual.Metadata.ExtensionProperties);
    }

    private static CommandProcessor Processor(
        DocumentCanvasComposition composition,
        params IDocumentChangedSubscriber[] subscribers) =>
        Processor(composition.Configuration.ConnectorAnchorPolicyProvider, subscribers);

    private static CommandProcessor Processor(
        IElementConnectorAnchorPolicyProvider connectorAnchorPolicyProvider,
        params IDocumentChangedSubscriber[] subscribers)
    {
        var registration = BpmnPluginRegistration.N314;
        return new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            subscribers,
            registration.HistoryPolicies,
            connectorAnchorPolicyProvider);
    }

    private static (
        Document Document,
        IElementConnectorAnchorPolicyProvider PolicyProvider,
        SemanticElementId NodeId,
        VisualStateId NodeVisualId,
        SemanticElementId RelationshipId) CreateSelfLoopDocument()
    {
        var documentId = new DocumentId("test:n314:self-loop");
        var nodeId = new SemanticElementId("test:n314:self-loop:node");
        var nodeVisualId = new VisualStateId("test:n314:self-loop:node:visual");
        var relationshipId = new SemanticElementId("test:n314:self-loop:flow");
        var relationshipVisualId = new VisualStateId("test:n314:self-loop:flow:visual");
        var sourceAnchorId = new ConnectorAnchorId("test:n314:self-loop:source");
        var targetAnchorId = new ConnectorAnchorId("test:n314:self-loop:target");
        var revision = DocumentRevision.Zero;
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                documentId,
                revision,
                [BpmnSemanticFactory.CreateTask(nodeId, "SELF", "Self", 1)],
                [BpmnSemanticFactory.CreateSequenceFlow(
                    relationshipId,
                    nodeId,
                    nodeId,
                    "Loop")]),
            new VisualModelSnapshot(
                documentId,
                revision,
                [
                    new VisualStateSnapshot(
                        nodeVisualId,
                        nodeId,
                        new PointD(100d, 100d),
                        new SizeD(160d, 84d),
                        VisualPlacementMode.Manual,
                        connectorAnchors:
                        [
                            new ConnectorAnchor(
                                sourceAnchorId,
                                ConnectorAnchorSide.Right,
                                ConnectorAnchorRole.Source,
                                0),
                            new ConnectorAnchor(
                                targetAnchorId,
                                ConnectorAnchorSide.Left,
                                ConnectorAnchorRole.Target,
                                0),
                        ]),
                    new VisualStateSnapshot(
                        relationshipVisualId,
                        relationshipId,
                        new PointD(0d, 0d),
                        new SizeD(0d, 0d),
                        VisualPlacementMode.Manual,
                        sourceAnchorId: sourceAnchorId,
                        targetAnchorId: targetAnchorId),
                ]),
            new DocumentMetadataSnapshot(documentId, revision));
        var provider = new ElementConnectorAnchorPolicyRegistry(
            BpmnPluginRegistration.N314.ConnectorAnchorPolicies);
        var construction = DocumentFactory.Create(snapshot, provider);
        return (
            Assert.IsType<Document>(construction.Document),
            provider,
            nodeId,
            nodeVisualId,
            relationshipId);
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = [];

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }
}
