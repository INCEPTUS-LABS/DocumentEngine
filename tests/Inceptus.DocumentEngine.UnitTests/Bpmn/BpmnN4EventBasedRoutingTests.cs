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
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN4EventBasedRoutingTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n4:document");
    private static readonly SemanticElementId SourceId = new("bpmn:n4:source");
    private static readonly SemanticElementId TargetId = new("bpmn:n4:target");
    private static readonly VisualStateId SourceVisualId = new("bpmn:n4:source:visual");
    private static readonly VisualStateId TargetVisualId = new("bpmn:n4:target:visual");
    private static readonly ConnectorAnchorId SourceAnchorId = new("bpmn:n4:source:anchor");
    private static readonly ConnectorAnchorId TargetAnchorId = new("bpmn:n4:target:anchor");
    private static readonly SemanticElementId FlowId = new("bpmn:n4:flow");
    private static readonly VisualStateId FlowVisualId = new("bpmn:n4:flow:visual");

    [Fact]
    public void N4AddsExactTypesAndPluginOwnedVerticalSliceWithoutChangingN314()
    {
        Assert.Equal("BPMN.MessageCatchEvent", BpmnSemanticTypes.MessageCatchEvent.Value);
        Assert.Equal("BPMN.TimerCatchEvent", BpmnSemanticTypes.TimerCatchEvent.Value);
        Assert.Equal("BPMN.EventBasedGateway", BpmnSemanticTypes.EventBasedGateway.Value);

        Type[] commands =
        [
            typeof(CreateBpmnMessageCatchEventCommand),
            typeof(CreateBpmnTimerCatchEventCommand),
            typeof(CreateBpmnEventBasedGatewayCommand),
        ];
        Assert.All(commands, type => Assert.True(type.IsPublic));
        Assert.All(commands, type => Assert.True(typeof(ICommand).IsAssignableFrom(type)));

        var n314 = BpmnPluginRegistration.N314;
        var n4 = BpmnPluginRegistration.N4;
        Assert.Equal(n314.CommandHandlers, n4.CommandHandlers.Take(n314.CommandHandlers.Length));
        Assert.Equal(n314.ProjectionRules, n4.ProjectionRules.Take(n314.ProjectionRules.Length));
        Assert.Equal(n314.DiagramDeletionRegistrations, n4.DiagramDeletionRegistrations);
        Assert.Equal(
            n314.AnchorConnectionCreationRegistrations,
            n4.AnchorConnectionCreationRegistrations);
        Assert.Equal(
            n314.ConnectorEndpointReconnectionRegistrations,
            n4.ConnectorEndpointReconnectionRegistrations);
        Assert.All(commands, type =>
        {
            var knownTypeId = Assert.IsType<CommandTypeId>(
                type.GetProperty("KnownTypeId")!.GetValue(null));
            Assert.DoesNotContain(n314.CommandHandlers, item => item.TypeId == knownTypeId);
            Assert.Contains(n4.CommandHandlers, item => item.TypeId == knownTypeId);
            Assert.Contains(n4.HistoryPolicies, item => item.TypeId == knownTypeId);
        });

        SemanticTypeId[] n4Types =
        [
            BpmnSemanticTypes.MessageCatchEvent,
            BpmnSemanticTypes.TimerCatchEvent,
            BpmnSemanticTypes.EventBasedGateway,
        ];
        Assert.All(n4Types, type =>
        {
            Assert.DoesNotContain(n314.ProjectionRules, item => item.SemanticTypeId == type);
            Assert.Contains(n4.ProjectionRules, item => item.SemanticTypeId == type);
            Assert.DoesNotContain(n314.PropertiesSchemas, item => item.SemanticTypeId == type);
            Assert.Contains(n4.PropertiesSchemas, item => item.SemanticTypeId == type);
            Assert.DoesNotContain(n314.ConnectorAnchorPolicies, item =>
                item.ElementTypeId == type);
            var anchorPolicy = Assert.Single(n4.ConnectorAnchorPolicies, item =>
                item.ElementTypeId == type).Policy;
            Assert.All(
                new[] { anchorPolicy.Top, anchorPolicy.Right, anchorPolicy.Bottom, anchorPolicy.Left },
                edge =>
                {
                    Assert.Equal(ConnectorAnchorPolicyMode.DynamicUnlimited, edge.Mode);
                    Assert.Equal(
                        ConnectorAnchorRoleCapability.SourceOrTarget,
                        edge.AllowedRoles);
                });
        });
    }

    [Fact]
    public async Task CreationCommandsCommitExactSemanticsVisualsAndHistory()
    {
        var harness = CreateHarness(EmptySnapshot());
        var commands = new BpmnElementCreationCommand[]
        {
            new CreateBpmnMessageCatchEventCommand(
                DocumentId,
                harness.Document.Revision,
                new SemanticElementId("message"),
                new VisualStateId("message:visual"),
                new PointD(10d, 20d),
                new SizeD(36d, 36d),
                "Message event",
                description: "Message description"),
            new CreateBpmnTimerCatchEventCommand(
                DocumentId,
                harness.Document.Revision.Increment(),
                new SemanticElementId("timer"),
                new VisualStateId("timer:visual"),
                new PointD(80d, 20d),
                new SizeD(36d, 36d),
                "Timer event",
                timerDefinition: "PT15M",
                description: "Timer description"),
            new CreateBpmnEventBasedGatewayCommand(
                DocumentId,
                harness.Document.Revision.Increment().Increment(),
                new SemanticElementId("event-gateway"),
                new VisualStateId("event-gateway:visual"),
                new PointD(150d, 14d),
                new SizeD(48d, 48d),
                "EVENT_BASED_GATEWAY",
                "Await event",
                description: "Gateway description"),
        };

        foreach (var command in commands)
        {
            var result = await harness.History.ExecuteAsync(harness.Processor, command);
            Assert.True(result.IsCommitted);
        }

        var committed = harness.Document.CaptureSnapshot();
        Assert.Equal(3, committed.SemanticModel.ElementCount);
        Assert.Equal(3, committed.VisualModel.Count);
        Assert.Empty(committed.SemanticModel.Relationships);
        Assert.All(committed.VisualModel.VisualStates, visual =>
        {
            Assert.Empty(visual.ConnectorAnchors);
            Assert.Null(visual.SourceAnchorId);
            Assert.Null(visual.TargetAnchorId);
        });
        var message = Element(committed, "message");
        Assert.Equal(BpmnSemanticTypes.MessageCatchEvent, message.TypeId);
        Assert.Equal("Message event", Text(message, BpmnSemanticProperties.Name));
        Assert.Equal(
            [BpmnSemanticProperties.Description, BpmnSemanticProperties.Name],
            message.Properties.Keys.Order());
        var timer = Element(committed, "timer");
        Assert.Equal("PT15M", Text(timer, BpmnSemanticProperties.TimerDefinition));
        var gateway = Element(committed, "event-gateway");
        Assert.Equal("EVENT_BASED_GATEWAY", Text(gateway, BpmnSemanticProperties.Code));
        Assert.False(gateway.Properties.ContainsKey(BpmnSemanticProperties.ElementNumber));
        Assert.Equal(3, harness.History.CaptureStatus().EntryCount);

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.False(harness.Document.SemanticModel.TryGetElement(
            new SemanticElementId("event-gateway"),
            out _));
        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(gateway, Element(harness.Document.CaptureSnapshot(), "event-gateway"));
    }

    [Fact]
    public void PropertiesSchemasUseCurrentEditorsAndNoElementNumber()
    {
        var schemas = BpmnPluginRegistration.N4.PropertiesSchemas.ToDictionary(
            schema => schema.SemanticTypeId);
        AssertFields(
            schemas[BpmnSemanticTypes.MessageCatchEvent],
            ("Name", BpmnSemanticProperties.Name, ElementPropertyEditorKind.SingleLineText),
            ("Description", BpmnSemanticProperties.Description,
                ElementPropertyEditorKind.MultilineText));
        AssertFields(
            schemas[BpmnSemanticTypes.TimerCatchEvent],
            ("Name", BpmnSemanticProperties.Name, ElementPropertyEditorKind.SingleLineText),
            ("Timer definition", BpmnSemanticProperties.TimerDefinition,
                ElementPropertyEditorKind.MultilineText),
            ("Description", BpmnSemanticProperties.Description,
                ElementPropertyEditorKind.MultilineText));
        AssertFields(
            schemas[BpmnSemanticTypes.EventBasedGateway],
            ("Code", BpmnSemanticProperties.Code, ElementPropertyEditorKind.SingleLineText),
            ("Name", BpmnSemanticProperties.Name, ElementPropertyEditorKind.SingleLineText),
            ("Description", BpmnSemanticProperties.Description,
                ElementPropertyEditorKind.MultilineText));
        Assert.All(
            schemas.Where(pair => pair.Key == BpmnSemanticTypes.MessageCatchEvent ||
                pair.Key == BpmnSemanticTypes.TimerCatchEvent ||
                pair.Key == BpmnSemanticTypes.EventBasedGateway),
            pair => Assert.DoesNotContain(pair.Value.Fields, field =>
                field.SemanticPropertyKey == BpmnSemanticProperties.ElementNumber));
    }

    [Fact]
    public void ToolboxUsesDistinctOrderedDataAndPlacementDefaults()
    {
        var n4 = BpmnPluginRegistration.N4;
        var catalog = new ToolboxCatalog(n4.ToolboxContributions);
        Assert.DoesNotContain(catalog.Items, item =>
            item.ElementTypeId == BpmnSemanticTypes.SequenceFlow);
        var message = Assert.Single(catalog.Items, item =>
            item.ElementTypeId == BpmnSemanticTypes.MessageCatchEvent);
        var timer = Assert.Single(catalog.Items, item =>
            item.ElementTypeId == BpmnSemanticTypes.TimerCatchEvent);
        var gateway = Assert.Single(catalog.Items, item =>
            item.ElementTypeId == BpmnSemanticTypes.EventBasedGateway);
        Assert.Equal("Message Catch Event", message.DisplayName);
        Assert.Equal("Timer Catch Event", timer.DisplayName);
        Assert.Equal("Event-Based Gateway", gateway.DisplayName);
        Assert.NotEqual(message.Icon, timer.Icon);
        Assert.NotEqual(timer.Icon, gateway.Icon);
        Assert.True(message.Order < timer.Order);
        Assert.True(timer.Order < gateway.Order);

        var placement = new ToolboxPlacementCatalog(
            n4.ToolboxPlacementRegistrations,
            catalog);
        var empty = EmptySnapshot();
        var click = new PointD(200d, 160d);
        var expectations = new[]
        {
            (message, typeof(CreateBpmnMessageCatchEventCommand), new SizeD(36d, 36d)),
            (timer, typeof(CreateBpmnTimerCatchEventCommand), new SizeD(36d, 36d)),
            (gateway, typeof(CreateBpmnEventBasedGatewayCommand), new SizeD(48d, 48d)),
        };
        foreach (var (item, commandType, size) in expectations)
        {
            Assert.True(placement.TryGetRegistration(item.ItemId, out var registration));
            var result = registration.CommandFactory.CreatePlan(new ToolboxPlacementRequest(
                item.ItemId,
                empty,
                empty.Revision,
                click,
                new FixedIdentityProvider(
                    new SemanticElementId($"placed:{item.ItemId.Value}"),
                    new VisualStateId($"placed:{item.ItemId.Value}:visual"))));
            var command = Assert.IsAssignableFrom<BpmnElementCreationCommand>(
                Assert.IsType<ToolboxPlacementPlan>(result.Plan).Command);
            Assert.Equal(commandType, command.GetType());
            Assert.Equal(size, command.Size);
            Assert.Equal(
                new PointD(click.X - (size.Width / 2d), click.Y - (size.Height / 2d)),
                command.Position);
            Assert.Equal(VisualPlacementMode.Pinned, command.PlacementMode);
        }
    }

    [Fact]
    public async Task EventBasedGatewayAllowsOnlySupportedCatchTargets()
    {
        SemanticTypeId[] validTargets =
        [
            BpmnSemanticTypes.MessageCatchEvent,
            BpmnSemanticTypes.TimerCatchEvent,
        ];
        foreach (var targetType in validTargets)
        {
            var harness = CreateHarness(FlowSnapshot(
                BpmnSemanticTypes.EventBasedGateway,
                targetType));
            var result = await harness.History.ExecuteAsync(
                harness.Processor,
                FlowCommand(harness.Document.Revision));
            Assert.True(result.IsCommitted);
        }

        SemanticTypeId[] invalidTargets =
        [
            BpmnSemanticTypes.Task,
            BpmnSemanticTypes.StartEvent,
            BpmnSemanticTypes.EndEvent,
            BpmnSemanticTypes.ExclusiveGateway,
            BpmnSemanticTypes.ParallelGateway,
            BpmnSemanticTypes.InclusiveGateway,
            BpmnSemanticTypes.EventBasedGateway,
        ];
        foreach (var targetType in invalidTargets)
        {
            var harness = CreateHarness(FlowSnapshot(
                BpmnSemanticTypes.EventBasedGateway,
                targetType,
                includeTargetAnchor: targetType != BpmnSemanticTypes.StartEvent));
            var before = harness.Document.CaptureSnapshot();
            var history = harness.History.CaptureStatus();
            var result = await harness.History.ExecuteAsync(
                harness.Processor,
                FlowCommand(harness.Document.Revision));
            Assert.False(result.IsCommitted);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == BpmnCommandDiagnosticCodes.EventBasedGatewayTargetInvalid);
            Assert.Equal(before, harness.Document.CaptureSnapshot());
            Assert.Equal(history, harness.History.CaptureStatus());
        }

        SemanticTypeId[] ordinaryPairs =
        [
            BpmnSemanticTypes.Task,
            BpmnSemanticTypes.MessageCatchEvent,
            BpmnSemanticTypes.MessageCatchEvent,
            BpmnSemanticTypes.Task,
            BpmnSemanticTypes.TimerCatchEvent,
            BpmnSemanticTypes.ExclusiveGateway,
        ];
        for (var index = 0; index < ordinaryPairs.Length; index += 2)
        {
            var harness = CreateHarness(FlowSnapshot(
                ordinaryPairs[index],
                ordinaryPairs[index + 1]));
            Assert.True((await harness.Processor.ExecuteAsync(
                harness.Document,
                FlowCommand(harness.Document.Revision))).IsCommitted);
        }
    }

    [Fact]
    public async Task SmartInvalidTaskTargetLeavesZeroAnchorFlowRevisionAndHistory()
    {
        var snapshot = FlowSnapshot(
            BpmnSemanticTypes.EventBasedGateway,
            BpmnSemanticTypes.Task,
            includeTargetAnchor: false);
        var harness = CreateHarness(snapshot);
        var history = harness.History.CaptureStatus();
        var atomic = new CreateBpmnSequenceFlowWithTargetAnchorCommand(
            DocumentId,
            harness.Document.Revision,
            FlowId,
            FlowVisualId,
            SourceId,
            TargetId,
            SourceAnchorId,
            TargetVisualId,
            TargetAnchorId,
            ConnectorAnchorSide.Left,
            0);

        var result = await harness.History.ExecuteAsync(harness.Processor, atomic);

        Assert.False(result.IsCommitted);
        Assert.Equal(snapshot, harness.Document.CaptureSnapshot());
        Assert.Equal(history, harness.History.CaptureStatus());
        Assert.DoesNotContain(
            harness.Document.VisualModel.VisualStates.SelectMany(visual =>
                visual.ConnectorAnchors),
            anchor => anchor.Id == TargetAnchorId);
        Assert.False(harness.Document.SemanticModel.TryGetRelationship(FlowId, out _));

        var creation = Assert.Single(
            BpmnPluginRegistration.N4.AnchorConnectionCreationRegistrations);
        var eligibility = Assert.IsAssignableFrom<IAnchorConnectionTargetEligibility>(
            creation.TargetEligibility);
        Assert.False(eligibility.CanAcquire(new AnchorConnectionTargetEligibilityRequest(
            snapshot,
            snapshot.Revision,
            SourceId,
            SourceVisualId,
            SourceAnchorId,
            TargetId,
            TargetVisualId)));
        var timerSnapshot = FlowSnapshot(
            BpmnSemanticTypes.EventBasedGateway,
            BpmnSemanticTypes.TimerCatchEvent,
            includeTargetAnchor: false);
        Assert.True(eligibility.CanAcquire(new AnchorConnectionTargetEligibilityRequest(
            timerSnapshot,
            timerSnapshot.Revision,
            SourceId,
            SourceVisualId,
            SourceAnchorId,
            TargetId,
            TargetVisualId)));

        var timerHarness = CreateHarness(timerSnapshot);
        var accepted = await timerHarness.History.ExecuteAsync(
            timerHarness.Processor,
            new CreateBpmnSequenceFlowWithTargetAnchorCommand(
                DocumentId,
                timerHarness.Document.Revision,
                FlowId,
                FlowVisualId,
                SourceId,
                TargetId,
                SourceAnchorId,
                TargetVisualId,
                TargetAnchorId,
                ConnectorAnchorSide.Left,
                0));
        Assert.True(accepted.IsCommitted);
        Assert.True(timerHarness.Document.SemanticModel.TryGetRelationship(FlowId, out var flow));
        Assert.Equal(TargetId, flow!.TargetId);
        Assert.True(timerHarness.Document.VisualModel.TryGetVisualState(
            TargetVisualId,
            out var targetVisual));
        Assert.Contains(targetVisual!.ConnectorAnchors, anchor =>
            anchor.Id == TargetAnchorId && anchor.Role == ConnectorAnchorRole.Target);
        Assert.True(timerHarness.Document.VisualModel.TryGetVisualState(
            FlowVisualId,
            out var flowVisual));
        Assert.Equal(TargetAnchorId, flowVisual!.TargetAnchorId);
        Assert.Equal(1, timerHarness.History.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task ReconnectionRevalidatesTheResultingPairAndPreservesIdentity()
    {
        var snapshot = ReconnectionSnapshot();
        var harness = CreateHarness(snapshot);
        var invalid = new ReconnectBpmnSequenceFlowEndpointCommand(
            DocumentId,
            harness.Document.Revision,
            FlowId,
            FlowVisualId,
            ConnectorEndpointKind.Target,
            new SemanticElementId("message"),
            new ConnectorAnchorId("message:target"),
            new SemanticElementId("task"),
            new ConnectorAnchorId("task:target"));
        var before = harness.Document.CaptureSnapshot();
        var history = harness.History.CaptureStatus();

        var rejected = await harness.History.ExecuteAsync(harness.Processor, invalid);

        Assert.False(rejected.IsCommitted);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.Equal(history, harness.History.CaptureStatus());

        var valid = new ReconnectBpmnSequenceFlowEndpointCommand(
            DocumentId,
            harness.Document.Revision,
            FlowId,
            FlowVisualId,
            ConnectorEndpointKind.Target,
            new SemanticElementId("message"),
            new ConnectorAnchorId("message:target"),
            new SemanticElementId("timer"),
            new ConnectorAnchorId("timer:target"));
        var accepted = await harness.History.ExecuteAsync(harness.Processor, valid);
        Assert.True(accepted.IsCommitted);
        Assert.True(harness.Document.SemanticModel.TryGetRelationship(FlowId, out var flow));
        Assert.Equal(new SemanticElementId("timer"), flow!.TargetId);
        Assert.True(harness.Document.VisualModel.TryGetVisualState(FlowVisualId, out var visual));
        Assert.Equal(new ConnectorAnchorId("timer:target"), visual!.TargetAnchorId);
        Assert.Equal(FlowId, visual.SemanticElementId);
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.True(harness.Document.SemanticModel.TryGetRelationship(FlowId, out flow));
        Assert.Equal(new SemanticElementId("message"), flow!.TargetId);
        Assert.True(harness.Document.VisualModel.TryGetVisualState(FlowVisualId, out visual));
        Assert.Equal(new ConnectorAnchorId("message:target"), visual!.TargetAnchorId);
    }

    [Fact]
    public async Task SourceReconnectionValidatesTheUnchangedTargetWithTheCandidateSource()
    {
        var invalidSnapshot = SourceReconnectionSnapshot(BpmnSemanticTypes.Task);
        var invalidHarness = CreateHarness(invalidSnapshot);
        var invalidHistory = invalidHarness.History.CaptureStatus();

        var rejected = await invalidHarness.History.ExecuteAsync(
            invalidHarness.Processor,
            SourceReconnectCommand(invalidHarness.Document.Revision));

        Assert.False(rejected.IsCommitted);
        Assert.Contains(rejected.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.EventBasedGatewayTargetInvalid);
        Assert.Equal(invalidSnapshot, invalidHarness.Document.CaptureSnapshot());
        Assert.Equal(invalidHistory, invalidHarness.History.CaptureStatus());

        var validHarness = CreateHarness(
            SourceReconnectionSnapshot(BpmnSemanticTypes.TimerCatchEvent));
        var accepted = await validHarness.History.ExecuteAsync(
            validHarness.Processor,
            SourceReconnectCommand(validHarness.Document.Revision));

        Assert.True(accepted.IsCommitted);
        Assert.True(validHarness.Document.SemanticModel.TryGetRelationship(FlowId, out var flow));
        Assert.Equal(new SemanticElementId("gateway"), flow!.SourceId);
        Assert.Equal(TargetId, flow.TargetId);
        Assert.True(validHarness.Document.VisualModel.TryGetVisualState(
            FlowVisualId,
            out var visual));
        Assert.Equal(new ConnectorAnchorId("gateway:source"), visual!.SourceAnchorId);
        Assert.Equal(FlowId, visual.SemanticElementId);
        Assert.True((await validHarness.History.UndoAsync(
            validHarness.Processor)).IsCommitted);
        Assert.Equal(
            SourceId,
            validHarness.Document.SemanticModel.Relationships.Single().SourceId);
        Assert.Equal(
            SourceAnchorId,
            validHarness.Document.VisualModel.VisualStates.Single(item =>
                item.Id == FlowVisualId).SourceAnchorId);
    }

    private static Harness CreateHarness(DocumentSnapshot snapshot)
    {
        var registration = BpmnPluginRegistration.N4;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.Create(snapshot, provider);
        Assert.True(
            construction.Succeeded,
            string.Join(" | ", construction.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var document = Assert.IsType<Document>(construction.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: provider);
        return new Harness(document, processor, new HistoryManager(document));
    }

    private static DocumentSnapshot EmptySnapshot() =>
        new(
            new SemanticModelSnapshot(DocumentId, DocumentRevision.Zero),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));

    private static DocumentSnapshot FlowSnapshot(
        SemanticTypeId sourceType,
        SemanticTypeId targetType,
        bool includeTargetAnchor = true) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new SemanticElementSnapshot(SourceId, sourceType),
                    new SemanticElementSnapshot(TargetId, targetType),
                ]),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    NodeVisual(
                        SourceVisualId,
                        SourceId,
                        new ConnectorAnchor(
                            SourceAnchorId,
                            ConnectorAnchorSide.Right,
                            ConnectorAnchorRole.Source,
                            0)),
                    NodeVisual(
                        TargetVisualId,
                        TargetId,
                        includeTargetAnchor
                            ? new ConnectorAnchor(
                                TargetAnchorId,
                                ConnectorAnchorSide.Left,
                                ConnectorAnchorRole.Target,
                                0)
                            : null),
                ]),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));

    private static DocumentSnapshot ReconnectionSnapshot()
    {
        var gatewayId = new SemanticElementId("gateway");
        var messageId = new SemanticElementId("message");
        var timerId = new SemanticElementId("timer");
        var taskId = new SemanticElementId("task");
        var gatewayAnchor = new ConnectorAnchorId("gateway:source");
        var messageAnchor = new ConnectorAnchorId("message:target");
        var timerAnchor = new ConnectorAnchorId("timer:target");
        var taskAnchor = new ConnectorAnchorId("task:target");
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new SemanticElementSnapshot(gatewayId,
                        BpmnSemanticTypes.EventBasedGateway),
                    new SemanticElementSnapshot(messageId,
                        BpmnSemanticTypes.MessageCatchEvent),
                    new SemanticElementSnapshot(timerId,
                        BpmnSemanticTypes.TimerCatchEvent),
                    new SemanticElementSnapshot(taskId, BpmnSemanticTypes.Task),
                ],
                [
                    new SemanticRelationshipSnapshot(
                        FlowId,
                        BpmnSemanticTypes.SequenceFlow,
                        gatewayId,
                        messageId),
                ]),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    NodeVisual(new VisualStateId("gateway:visual"), gatewayId,
                        new ConnectorAnchor(gatewayAnchor, ConnectorAnchorSide.Right,
                            ConnectorAnchorRole.Source, 0)),
                    NodeVisual(new VisualStateId("message:visual"), messageId,
                        new ConnectorAnchor(messageAnchor, ConnectorAnchorSide.Left,
                            ConnectorAnchorRole.Target, 0)),
                    NodeVisual(new VisualStateId("timer:visual"), timerId,
                        new ConnectorAnchor(timerAnchor, ConnectorAnchorSide.Left,
                            ConnectorAnchorRole.Target, 0)),
                    NodeVisual(new VisualStateId("task:visual"), taskId,
                        new ConnectorAnchor(taskAnchor, ConnectorAnchorSide.Left,
                            ConnectorAnchorRole.Target, 0)),
                    new VisualStateSnapshot(
                        FlowVisualId,
                        FlowId,
                        new PointD(0d, 0d),
                        new SizeD(0d, 0d),
                        VisualPlacementMode.Manual,
                        sourceAnchorId: gatewayAnchor,
                        targetAnchorId: messageAnchor),
                ]),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
    }

    private static DocumentSnapshot SourceReconnectionSnapshot(SemanticTypeId targetType)
    {
        var gatewayId = new SemanticElementId("gateway");
        var gatewayAnchorId = new ConnectorAnchorId("gateway:source");
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    new SemanticElementSnapshot(SourceId, BpmnSemanticTypes.Task),
                    new SemanticElementSnapshot(TargetId, targetType),
                    new SemanticElementSnapshot(gatewayId,
                        BpmnSemanticTypes.EventBasedGateway),
                ],
                [
                    new SemanticRelationshipSnapshot(
                        FlowId,
                        BpmnSemanticTypes.SequenceFlow,
                        SourceId,
                        TargetId),
                ]),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    NodeVisual(
                        SourceVisualId,
                        SourceId,
                        new ConnectorAnchor(
                            SourceAnchorId,
                            ConnectorAnchorSide.Right,
                            ConnectorAnchorRole.Source,
                            0)),
                    NodeVisual(
                        TargetVisualId,
                        TargetId,
                        new ConnectorAnchor(
                            TargetAnchorId,
                            ConnectorAnchorSide.Left,
                            ConnectorAnchorRole.Target,
                            0)),
                    NodeVisual(
                        new VisualStateId("gateway:visual"),
                        gatewayId,
                        new ConnectorAnchor(
                            gatewayAnchorId,
                            ConnectorAnchorSide.Right,
                            ConnectorAnchorRole.Source,
                            0)),
                    new VisualStateSnapshot(
                        FlowVisualId,
                        FlowId,
                        new PointD(0d, 0d),
                        new SizeD(0d, 0d),
                        VisualPlacementMode.Manual,
                        sourceAnchorId: SourceAnchorId,
                        targetAnchorId: TargetAnchorId),
                ]),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
    }

    private static ReconnectBpmnSequenceFlowEndpointCommand SourceReconnectCommand(
        DocumentRevision revision) =>
        new(
            DocumentId,
            revision,
            FlowId,
            FlowVisualId,
            ConnectorEndpointKind.Source,
            SourceId,
            SourceAnchorId,
            new SemanticElementId("gateway"),
            new ConnectorAnchorId("gateway:source"));

    private static VisualStateSnapshot NodeVisual(
        VisualStateId visualId,
        SemanticElementId semanticId,
        ConnectorAnchor? anchor) =>
        new(
            visualId,
            semanticId,
            new PointD(0d, 0d),
            new SizeD(120d, 80d),
            VisualPlacementMode.Pinned,
            connectorAnchors: anchor is null ? [] : [anchor]);

    private static CreateBpmnSequenceFlowCommand FlowCommand(DocumentRevision revision) =>
        new(
            DocumentId,
            revision,
            FlowId,
            FlowVisualId,
            SourceId,
            TargetId,
            SourceAnchorId,
            TargetAnchorId);

    private static SemanticElementSnapshot Element(DocumentSnapshot snapshot, string id) =>
        Assert.Single(snapshot.SemanticModel.Elements, element =>
            element.Id == new SemanticElementId(id));

    private static string Text(SemanticElementSnapshot element, string key) =>
        element.Properties[key].TextValue!;

    private static void AssertFields(
        ElementPropertiesSchema schema,
        params (string DisplayName, string PropertyKey, ElementPropertyEditorKind Editor)[]
            expected)
    {
        Assert.Equal(expected.Length, schema.Fields.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].DisplayName, schema.Fields[index].DisplayName);
            Assert.Equal(expected[index].PropertyKey,
                schema.Fields[index].SemanticPropertyKey);
            Assert.Equal(expected[index].Editor, schema.Fields[index].EditorKind);
        }
    }

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
