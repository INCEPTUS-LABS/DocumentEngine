using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN3SequenceFlowEndpointReconnectionTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n3:document");
    private static readonly SemanticElementId TaskAId = new("bpmn:n3:task-a");
    private static readonly SemanticElementId TaskBId = new("bpmn:n3:task-b");
    private static readonly SemanticElementId TaskCId = new("bpmn:n3:task-c");
    private static readonly SemanticElementId StartId = new("bpmn:n3:start");
    private static readonly SemanticElementId EndId = new("bpmn:n3:end");
    private static readonly SemanticElementId FlowId = new("bpmn:n3:flow");
    private static readonly VisualStateId TaskAVisualId = new("bpmn:n3:task-a:visual");
    private static readonly VisualStateId TaskBVisualId = new("bpmn:n3:task-b:visual");
    private static readonly VisualStateId TaskCVisualId = new("bpmn:n3:task-c:visual");
    private static readonly VisualStateId StartVisualId = new("bpmn:n3:start:visual");
    private static readonly VisualStateId EndVisualId = new("bpmn:n3:end:visual");
    private static readonly VisualStateId FlowVisualId = new("bpmn:n3:flow:visual");
    private static readonly ConnectorAnchorId TaskASource1 = new("bpmn:n3:task-a:source-1");
    private static readonly ConnectorAnchorId TaskASource2 = new("bpmn:n3:task-a:source-2");
    private static readonly ConnectorAnchorId TaskATarget = new("bpmn:n3:task-a:target");
    private static readonly ConnectorAnchorId TaskBTarget = new("bpmn:n3:task-b:target");
    private static readonly ConnectorAnchorId TaskCSource = new("bpmn:n3:task-c:source");
    private static readonly ConnectorAnchorId TaskCTarget = new("bpmn:n3:task-c:target");
    private static readonly ConnectorAnchorId StartSource = new("bpmn:n3:start:source");
    private static readonly ConnectorAnchorId EndTarget = new("bpmn:n3:end:target");

    [Fact]
    public void N3ComposesN2AndAddsOnlyTheSequenceFlowReconnectionCapability()
    {
        var n2 = BpmnPluginRegistration.N2;
        var n3 = BpmnPluginRegistration.N3;

        Assert.Equal(
            n2.CommandHandlers.AsEnumerable(),
            n3.CommandHandlers.Take(n2.CommandHandlers.Length));
        Assert.Equal(
            n2.CommandValidators.AsEnumerable(),
            n3.CommandValidators.Take(n2.CommandValidators.Length));
        Assert.Equal(
            n2.HistoryPolicies.AsEnumerable(),
            n3.HistoryPolicies.Take(n2.HistoryPolicies.Length));
        Assert.Equal(n2.ProjectionRules.AsEnumerable(), n3.ProjectionRules.AsEnumerable());
        Assert.Equal(n2.LayoutAlgorithms.AsEnumerable(), n3.LayoutAlgorithms.AsEnumerable());
        Assert.Equal(n2.RoutingAlgorithms.AsEnumerable(), n3.RoutingAlgorithms.AsEnumerable());
        Assert.Equal(n2.SceneContributors.AsEnumerable(), n3.SceneContributors.AsEnumerable());
        Assert.Equal(
            n2.ToolboxContributions.AsEnumerable(),
            n3.ToolboxContributions.AsEnumerable());
        Assert.Equal(
            n2.ToolboxPlacementRegistrations.AsEnumerable(),
            n3.ToolboxPlacementRegistrations.AsEnumerable());
        Assert.Equal(n2.PropertiesSchemas.AsEnumerable(), n3.PropertiesSchemas.AsEnumerable());
        Assert.Equal(
            n2.ConnectorAnchorPolicies.AsEnumerable(),
            n3.ConnectorAnchorPolicies.AsEnumerable());
        Assert.Equal(
            n2.AnchorConnectionCreationRegistrations.AsEnumerable(),
            n3.AnchorConnectionCreationRegistrations.AsEnumerable());
        Assert.Empty(n2.ConnectorEndpointReconnectionRegistrations);

        Assert.Equal(n2.CommandHandlers.Length + 1, n3.CommandHandlers.Length);
        Assert.Equal(n2.CommandValidators.Length + 1, n3.CommandValidators.Length);
        Assert.Equal(n2.HistoryPolicies.Length + 1, n3.HistoryPolicies.Length);
        var registration = Assert.Single(n3.ConnectorEndpointReconnectionRegistrations);
        Assert.Equal("bpmn:endpoint-reconnection/sequence-flow", registration.ReconnectionId.Value);
        Assert.Equal(
            "BpmnSequenceFlowEndpointReconnectionCommandFactory",
            registration.CommandFactory.GetType().Name);
        Assert.Contains(n3.CommandHandlers, item =>
            item.TypeId == ReconnectBpmnSequenceFlowEndpointCommand.KnownTypeId);
        Assert.Contains(n3.CommandValidators, item =>
            item.TypeId == ReconnectBpmnSequenceFlowEndpointCommand.KnownTypeId &&
            item.ValidatorId.Value == "bpmn:validator/reconnect-sequence-flow-endpoint");
        Assert.Contains(n3.HistoryPolicies, item =>
            item.TypeId == ReconnectBpmnSequenceFlowEndpointCommand.KnownTypeId);
    }

    [Fact]
    public void FactoryMatchesExactExistingEndpointsAndBuildsOnePureAtomicCommand()
    {
        var document = CreateSnapshot();
        var factory = Factory();

        Assert.True(factory.CanStart(StartRequest(
            document,
            ConnectorEndpointKind.Source,
            TaskAId,
            TaskASource1)));
        Assert.True(factory.CanStart(StartRequest(
            document,
            ConnectorEndpointKind.Target,
            TaskBId,
            TaskBTarget)));
        Assert.False(factory.CanStart(StartRequest(
            document,
            ConnectorEndpointKind.Source,
            TaskBId,
            TaskASource1)));
        Assert.False(factory.CanStart(new ConnectorEndpointReconnectionStartRequest(
            document,
            document.Revision,
            FlowId,
            TaskAVisualId,
            ConnectorEndpointKind.Source,
            TaskAId,
            TaskASource1)));

        var result = factory.CreatePlan(Request(
            document,
            ConnectorEndpointKind.Source,
            TaskAId,
            TaskASource1,
            TaskAId,
            TaskAVisualId,
            TaskASource2));

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        var plan = Assert.IsType<ConnectorEndpointReconnectionPlan>(result.Plan);
        var command = Assert.IsType<ReconnectBpmnSequenceFlowEndpointCommand>(plan.Command);
        Assert.Equal(DocumentId, command.TargetDocumentId);
        Assert.Equal(document.Revision, command.ExpectedRevision);
        Assert.Equal(FlowId, command.RelationshipId);
        Assert.Equal(FlowVisualId, command.ConnectorVisualStateId);
        Assert.Equal(ConnectorEndpointKind.Source, command.EndpointKind);
        Assert.Equal(TaskAId, command.ExpectedCurrentSemanticElementId);
        Assert.Equal(TaskASource1, command.ExpectedCurrentAnchorId);
        Assert.Equal(TaskAId, command.NewSemanticElementId);
        Assert.Equal(TaskASource2, command.NewAnchorId);
        Assert.Equal(FlowId, plan.RelationshipId);
        Assert.Equal(FlowVisualId, plan.ConnectorVisualStateId);
        Assert.Equal(TaskAId, Relationship(document).SourceId);
        Assert.Equal(TaskASource1, FlowVisual(document).SourceAnchorId);
    }

    [Theory]
    [InlineData("wrong-role")]
    [InlineData("occupied")]
    [InlineData("non-persistent")]
    [InlineData("incoming-start")]
    [InlineData("outgoing-end")]
    public void FactoryPreservesBpmnRoleOccupancyAndFlowNodeRestrictions(string scenario)
    {
        var document = CreateSnapshot(includeOccupyingFlow: scenario == "occupied");
        var request = scenario switch
        {
            "wrong-role" => Request(
                document,
                ConnectorEndpointKind.Target,
                TaskBId,
                TaskBTarget,
                TaskCId,
                TaskCVisualId,
                TaskCSource),
            "occupied" => Request(
                document,
                ConnectorEndpointKind.Target,
                TaskBId,
                TaskBTarget,
                TaskCId,
                TaskCVisualId,
                TaskCTarget),
            "non-persistent" => Request(
                document,
                ConnectorEndpointKind.Target,
                TaskBId,
                TaskBTarget,
                TaskCId,
                TaskCVisualId,
                ConnectorAnchorReferenceIdentity.ForPredefined(
                    TaskCVisualId,
                    new PredefinedConnectorAnchorDefinitionId("target"))),
            "incoming-start" => Request(
                document,
                ConnectorEndpointKind.Target,
                TaskBId,
                TaskBTarget,
                StartId,
                StartVisualId,
                StartSource),
            "outgoing-end" => Request(
                document,
                ConnectorEndpointKind.Source,
                TaskAId,
                TaskASource1,
                EndId,
                EndVisualId,
                EndTarget),
            _ => throw new InvalidOperationException(),
        };

        var result = Factory().CreatePlan(request);

        Assert.False(result.Succeeded);
        Assert.Null(result.Plan);
        var expectedCode = scenario switch
        {
            "occupied" => CommandExecutionDiagnosticCodes.ConnectorAnchorInUse,
            "incoming-start" => BpmnCommandDiagnosticCodes.IncomingStartEvent,
            "outgoing-end" => BpmnCommandDiagnosticCodes.OutgoingEndEvent,
            _ => BpmnCommandDiagnosticCodes.SequenceFlowAnchorBindingInvalid,
        };
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
        Assert.Equal(TaskBId, Relationship(document).TargetId);
        Assert.Equal(TaskBTarget, FlowVisual(document).TargetAnchorId);
    }

    [Fact]
    public async Task TargetReconnectIsOneIdentityPreservingCommitWithExactUndoAndRedo()
    {
        var harness = CreateHarness();
        var before = harness.Document.CaptureSnapshot();
        var beforeRelationship = Relationship(before);
        var beforeVisual = FlowVisual(before);
        var command = Reconnect(
            harness.Document.Revision,
            ConnectorEndpointKind.Target,
            TaskBId,
            TaskBTarget,
            TaskCId,
            TaskCTarget);

        var committed = await harness.History.ExecuteAsync(harness.Processor, command);

        Assert.True(committed.IsCommitted);
        Assert.Equal(new DocumentRevision(1), harness.Document.Revision);
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);
        var after = harness.Document.CaptureSnapshot();
        var afterRelationship = Relationship(after);
        var afterVisual = FlowVisual(after);
        Assert.Equal(beforeRelationship.Id, afterRelationship.Id);
        Assert.Equal(beforeRelationship.TypeId, afterRelationship.TypeId);
        Assert.Equal(beforeRelationship.SourceId, afterRelationship.SourceId);
        Assert.Equal(TaskCId, afterRelationship.TargetId);
        Assert.Equal(beforeRelationship.Properties, afterRelationship.Properties);
        Assert.Equal(beforeVisual.Id, afterVisual.Id);
        Assert.Equal(beforeVisual.Route, afterVisual.Route);
        Assert.Equal(beforeVisual.Properties, afterVisual.Properties);
        Assert.Equal(beforeVisual.ConnectorAnchors, afterVisual.ConnectorAnchors);
        Assert.Equal(beforeVisual.SourceAnchorId, afterVisual.SourceAnchorId);
        Assert.Equal(TaskCTarget, afterVisual.TargetAnchorId);
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            harness.Document.VisualModel,
            TaskBTarget));
        Assert.True(ConnectorAnchorOccupancy.IsOccupied(
            harness.Document.VisualModel,
            TaskCTarget));

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(TaskBId, Relationship(harness.Document.CaptureSnapshot()).TargetId);
        Assert.Equal(TaskBTarget, FlowVisual(harness.Document.CaptureSnapshot()).TargetAnchorId);
        Assert.True(ConnectorAnchorOccupancy.IsOccupied(
            harness.Document.VisualModel,
            TaskBTarget));
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            harness.Document.VisualModel,
            TaskCTarget));

        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(TaskCId, Relationship(harness.Document.CaptureSnapshot()).TargetId);
        Assert.Equal(TaskCTarget, FlowVisual(harness.Document.CaptureSnapshot()).TargetAnchorId);
        Assert.Equal(FlowId, Relationship(harness.Document.CaptureSnapshot()).Id);
        Assert.Equal(FlowVisualId, FlowVisual(harness.Document.CaptureSnapshot()).Id);
    }

    [Fact]
    public async Task SameOwnerDifferentAnchorAndDistinctAnchorSelfLoopRemainValid()
    {
        var sourceHarness = CreateHarness();
        var sourceResult = await sourceHarness.History.ExecuteAsync(
            sourceHarness.Processor,
            Reconnect(
                sourceHarness.Document.Revision,
                ConnectorEndpointKind.Source,
                TaskAId,
                TaskASource1,
                TaskAId,
                TaskASource2));

        Assert.True(sourceResult.IsCommitted);
        Assert.Equal(TaskAId, Relationship(sourceHarness.Document.CaptureSnapshot()).SourceId);
        Assert.Equal(TaskASource2, FlowVisual(sourceHarness.Document.CaptureSnapshot()).SourceAnchorId);
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            sourceHarness.Document.VisualModel,
            TaskASource1));

        var selfLoopHarness = CreateHarness();
        var selfLoopResult = await selfLoopHarness.History.ExecuteAsync(
            selfLoopHarness.Processor,
            Reconnect(
                selfLoopHarness.Document.Revision,
                ConnectorEndpointKind.Target,
                TaskBId,
                TaskBTarget,
                TaskAId,
                TaskATarget));

        Assert.True(selfLoopResult.IsCommitted);
        var relationship = Relationship(selfLoopHarness.Document.CaptureSnapshot());
        var visual = FlowVisual(selfLoopHarness.Document.CaptureSnapshot());
        Assert.Equal(TaskAId, relationship.SourceId);
        Assert.Equal(TaskAId, relationship.TargetId);
        Assert.Equal(TaskASource1, visual.SourceAnchorId);
        Assert.Equal(TaskATarget, visual.TargetAnchorId);
        Assert.NotEqual(visual.SourceAnchorId, visual.TargetAnchorId);
    }

    [Fact]
    public async Task ExpectedOldAndExactNoChangeGuardsRejectWithoutRevisionOrHistory()
    {
        var harness = CreateHarness();
        var before = harness.Document.CaptureSnapshot();

        var stale = await harness.History.ExecuteAsync(
            harness.Processor,
            Reconnect(
                harness.Document.Revision,
                ConnectorEndpointKind.Target,
                TaskCId,
                TaskBTarget,
                TaskCId,
                TaskCTarget));
        var noChange = await harness.History.ExecuteAsync(
            harness.Processor,
            Reconnect(
                harness.Document.Revision,
                ConnectorEndpointKind.Target,
                TaskBId,
                TaskBTarget,
                TaskBId,
                TaskBTarget));

        Assert.False(stale.IsCommitted);
        Assert.Contains(stale.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.SequenceFlowEndpointStateMismatch);
        Assert.False(noChange.IsCommitted);
        Assert.Contains(noChange.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.SequenceFlowEndpointReconnectionNoChange);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
    }

    private static IConnectorEndpointReconnectionCommandFactory Factory() =>
        Assert.Single(BpmnPluginRegistration.N3.ConnectorEndpointReconnectionRegistrations)
            .CommandFactory;

    private static ConnectorEndpointReconnectionStartRequest StartRequest(
        DocumentSnapshot document,
        ConnectorEndpointKind endpointKind,
        SemanticElementId currentSemanticElementId,
        ConnectorAnchorId currentAnchorId) =>
        new(
            document,
            document.Revision,
            FlowId,
            FlowVisualId,
            endpointKind,
            currentSemanticElementId,
            currentAnchorId);

    private static ConnectorEndpointReconnectionRequest Request(
        DocumentSnapshot document,
        ConnectorEndpointKind endpointKind,
        SemanticElementId currentSemanticElementId,
        ConnectorAnchorId currentAnchorId,
        SemanticElementId candidateSemanticElementId,
        VisualStateId candidateVisualStateId,
        ConnectorAnchorId candidateAnchorId) =>
        new(
            document,
            document.Revision,
            FlowId,
            FlowVisualId,
            endpointKind,
            currentSemanticElementId,
            currentAnchorId,
            candidateSemanticElementId,
            candidateVisualStateId,
            candidateAnchorId);

    private static ReconnectBpmnSequenceFlowEndpointCommand Reconnect(
        DocumentRevision revision,
        ConnectorEndpointKind endpointKind,
        SemanticElementId currentSemanticElementId,
        ConnectorAnchorId currentAnchorId,
        SemanticElementId newSemanticElementId,
        ConnectorAnchorId newAnchorId) =>
        new(
            DocumentId,
            revision,
            FlowId,
            FlowVisualId,
            endpointKind,
            currentSemanticElementId,
            currentAnchorId,
            newSemanticElementId,
            newAnchorId);

    private static Harness CreateHarness()
    {
        var registration = BpmnPluginRegistration.N3;
        var policyProvider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.Create(CreateSnapshot(), policyProvider);
        var document = Assert.IsType<Document>(construction.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: policyProvider);
        return new Harness(document, processor, new HistoryManager(document));
    }

    private static DocumentSnapshot CreateSnapshot(bool includeOccupyingFlow = false)
    {
        var revision = DocumentRevision.Zero;
        var elements = new[]
        {
            new SemanticElementSnapshot(TaskAId, BpmnSemanticTypes.Task),
            new SemanticElementSnapshot(TaskBId, BpmnSemanticTypes.Task),
            new SemanticElementSnapshot(TaskCId, BpmnSemanticTypes.Task),
            new SemanticElementSnapshot(StartId, BpmnSemanticTypes.StartEvent),
            new SemanticElementSnapshot(EndId, BpmnSemanticTypes.EndEvent),
        };
        var relationships = new List<SemanticRelationshipSnapshot>
        {
            new(
                FlowId,
                BpmnSemanticTypes.SequenceFlow,
                TaskAId,
                TaskBId,
                [new("bpmn:test:semantic-property", PropertyValue.FromText("preserved"))]),
        };
        var visuals = new List<VisualStateSnapshot>
        {
            Node(
                TaskAVisualId,
                TaskAId,
                0d,
                [
                    new ConnectorAnchor(
                        TaskASource1,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        0),
                    new ConnectorAnchor(
                        TaskASource2,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        1),
                    new ConnectorAnchor(
                        TaskATarget,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        0),
                ]),
            Node(
                TaskBVisualId,
                TaskBId,
                220d,
                [
                    new ConnectorAnchor(
                        TaskBTarget,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        0),
                ]),
            Node(
                TaskCVisualId,
                TaskCId,
                440d,
                [
                    new ConnectorAnchor(
                        TaskCSource,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        0),
                    new ConnectorAnchor(
                        TaskCTarget,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        0),
                ]),
            Node(
                StartVisualId,
                StartId,
                660d,
                [
                    new ConnectorAnchor(
                        StartSource,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        0),
                ]),
            Node(
                EndVisualId,
                EndId,
                880d,
                [
                    new ConnectorAnchor(
                        EndTarget,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        0),
                ]),
            new(
                FlowVisualId,
                FlowId,
                new PointD(0d, 0d),
                new SizeD(0d, 0d),
                VisualPlacementMode.Manual,
                [new PointD(120d, 40d), new PointD(170d, 75d), new PointD(220d, 40d)],
                [new("bpmn:test:visual-property", PropertyValue.FromText("preserved"))],
                sourceAnchorId: TaskASource1,
                targetAnchorId: TaskBTarget),
        };
        if (includeOccupyingFlow)
        {
            var occupyingFlowId = new SemanticElementId("bpmn:n3:occupying-flow");
            relationships.Add(new SemanticRelationshipSnapshot(
                occupyingFlowId,
                BpmnSemanticTypes.SequenceFlow,
                TaskAId,
                TaskCId));
            visuals.Add(new VisualStateSnapshot(
                new VisualStateId("bpmn:n3:occupying-flow:visual"),
                occupyingFlowId,
                new PointD(0d, 0d),
                new SizeD(0d, 0d),
                VisualPlacementMode.Manual,
                sourceAnchorId: TaskASource2,
                targetAnchorId: TaskCTarget));
        }

        return new DocumentSnapshot(
            new SemanticModelSnapshot(DocumentId, revision, elements, relationships),
            new VisualModelSnapshot(DocumentId, revision, visuals),
            new DocumentMetadataSnapshot(
                DocumentId,
                revision,
                extensionProperties:
                [
                    new KeyValuePair<string, PropertyValue>(
                        "bpmn:test:metadata",
                        PropertyValue.FromText("preserved")),
                ]));
    }

    private static VisualStateSnapshot Node(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        double x,
        IEnumerable<ConnectorAnchor> anchors) =>
        new(
            visualStateId,
            semanticElementId,
            new PointD(x, 0d),
            new SizeD(120d, 80d),
            VisualPlacementMode.Pinned,
            connectorAnchors: anchors);

    private static SemanticRelationshipSnapshot Relationship(DocumentSnapshot document)
    {
        Assert.True(document.SemanticModel.TryGetRelationship(FlowId, out var relationship));
        return Assert.IsType<SemanticRelationshipSnapshot>(relationship);
    }

    private static VisualStateSnapshot FlowVisual(DocumentSnapshot document)
    {
        Assert.True(document.VisualModel.TryGetVisualState(FlowVisualId, out var visual));
        return Assert.IsType<VisualStateSnapshot>(visual);
    }

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History);
}
