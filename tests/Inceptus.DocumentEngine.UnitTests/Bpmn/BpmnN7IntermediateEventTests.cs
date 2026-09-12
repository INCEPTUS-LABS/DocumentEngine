using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN7IntermediateEventTests
{
    private const string ProjectedSemanticTypeProperty = "BPMN.ProjectedSemanticType";
    private static readonly DocumentId DocumentId = new("bpmn:n7:document");
    private static readonly DocumentRevision Revision = new(7);

    [Fact]
    public void SemanticTypesClassificationsAndPropertiesAreExact()
    {
        Assert.Equal("BPMN.MessageThrowEvent", BpmnSemanticTypes.MessageThrowEvent.Value);
        Assert.Equal("BPMN.SignalCatchEvent", BpmnSemanticTypes.SignalCatchEvent.Value);
        Assert.Equal("BPMN.SignalThrowEvent", BpmnSemanticTypes.SignalThrowEvent.Value);
        Assert.Equal(
            [
                BpmnSemanticTypes.MessageCatchEvent,
                BpmnSemanticTypes.MessageThrowEvent,
                BpmnSemanticTypes.TimerCatchEvent,
                BpmnSemanticTypes.SignalCatchEvent,
                BpmnSemanticTypes.SignalThrowEvent,
            ],
            BpmnIntermediateEventSemanticTypes.All.AsEnumerable());
        Assert.Equal(
            [
                BpmnSemanticTypes.MessageCatchEvent,
                BpmnSemanticTypes.TimerCatchEvent,
                BpmnSemanticTypes.SignalCatchEvent,
            ],
            BpmnIntermediateEventSemanticTypes.Catch.AsEnumerable());
        Assert.Equal(
            [BpmnSemanticTypes.MessageThrowEvent, BpmnSemanticTypes.SignalThrowEvent],
            BpmnIntermediateEventSemanticTypes.Throw.AsEnumerable());

        foreach (var typeId in BpmnIntermediateEventSemanticTypes.All)
        {
            Assert.True(BpmnSemanticTypes.IsEvent(typeId));
            Assert.True(BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(typeId));
            Assert.False(BpmnTaskSemanticTypes.IsTask(typeId));
            SemanticTypeId[] gatewayTypes =
            [
                BpmnSemanticTypes.ExclusiveGateway,
                BpmnSemanticTypes.ParallelGateway,
                BpmnSemanticTypes.InclusiveGateway,
                BpmnSemanticTypes.EventBasedGateway,
            ];
            Assert.DoesNotContain(typeId, gatewayTypes);
        }

        Assert.True(BpmnIntermediateEventSemanticTypes.IsThrowEvent(
            BpmnSemanticTypes.MessageThrowEvent));
        Assert.True(BpmnIntermediateEventSemanticTypes.IsCatchEvent(
            BpmnSemanticTypes.SignalCatchEvent));
        Assert.True(BpmnIntermediateEventSemanticTypes.IsThrowEvent(
            BpmnSemanticTypes.SignalThrowEvent));
        Assert.False(BpmnIntermediateEventSemanticTypes.IsCatchEvent(
            BpmnSemanticTypes.MessageThrowEvent));
        Assert.False(BpmnIntermediateEventSemanticTypes.IsThrowEvent(
            BpmnSemanticTypes.SignalCatchEvent));

        var elements = new[]
        {
            BpmnSemanticFactory.CreateMessageThrowEvent(
                new SemanticElementId("message-throw"), "Send notification", "Description"),
            BpmnSemanticFactory.CreateSignalCatchEvent(
                new SemanticElementId("signal-catch"), "Cancellation signal", "Description"),
            BpmnSemanticFactory.CreateSignalThrowEvent(
                new SemanticElementId("signal-throw"), "Cancellation raised", "Description"),
        };
        Assert.Equal(
            [
                BpmnSemanticTypes.MessageThrowEvent,
                BpmnSemanticTypes.SignalCatchEvent,
                BpmnSemanticTypes.SignalThrowEvent,
            ],
            elements.Select(static element => element.TypeId));
        Assert.All(elements, element => Assert.Equal(
            [BpmnSemanticProperties.Description, BpmnSemanticProperties.Name],
            element.Properties.Keys.Order(StringComparer.Ordinal)));
        Assert.All(elements, element => Assert.DoesNotContain(element.Properties.Keys, key =>
            key.Contains("Code", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("ElementNumber", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Message", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Signal", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Ref", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void N7RegistersTheCompleteBpmnLocalVerticalSliceAndOrderedToolbox()
    {
        var n6 = BpmnPluginRegistration.N6;
        var n7 = BpmnPluginRegistration.N7;
        Assert.Equal(n6.CommandHandlers.Length + 3, n7.CommandHandlers.Length);
        Assert.Equal(n6.CommandValidators.Length + 3, n7.CommandValidators.Length);
        Assert.Equal(n6.HistoryPolicies.Length + 3, n7.HistoryPolicies.Length);
        Assert.Equal(n6.ProjectionRules.Length + 3, n7.ProjectionRules.Length);
        Assert.Equal(n6.PropertiesSchemas.Length + 3, n7.PropertiesSchemas.Length);
        Assert.Equal(n6.ConnectorAnchorPolicies.Length + 3,
            n7.ConnectorAnchorPolicies.Length);
        Assert.Equal(n6.LayoutAlgorithms, n7.LayoutAlgorithms);
        Assert.Equal(n6.RoutingAlgorithms, n7.RoutingAlgorithms);
        Assert.Equal(n6.SceneContributors, n7.SceneContributors);
        Assert.Equal(n6.ModelValidationRules, n7.ModelValidationRules);

        foreach (var typeId in N7Types())
        {
            Assert.Contains(n7.ProjectionRules, rule => rule.SemanticTypeId == typeId);
            var schema = Assert.Single(n7.PropertiesSchemas, candidate =>
                candidate.SemanticTypeId == typeId);
            Assert.Equal(["Name", "Description"],
                schema.Fields.Select(static field => field.DisplayName));
            Assert.Equal(
                [ElementPropertyEditorKind.SingleLineText,
                    ElementPropertyEditorKind.MultilineText],
                schema.Fields.Select(static field => field.EditorKind));
            var anchorPolicy = Assert.Single(n7.ConnectorAnchorPolicies, candidate =>
                candidate.ElementTypeId == typeId).Policy;
            Assert.All(
                new[] { anchorPolicy.Top, anchorPolicy.Right, anchorPolicy.Bottom,
                    anchorPolicy.Left },
                edge =>
                {
                    Assert.Equal(ConnectorAnchorPolicyMode.DynamicUnlimited, edge.Mode);
                    Assert.Equal(ConnectorAnchorRoleCapability.SourceOrTarget,
                        edge.AllowedRoles);
                });
        }

        var toolbox = new ToolboxCatalog(n7.ToolboxContributions);
        var section = Assert.Single(toolbox.Sections);
        Assert.Equal("BPMN", section.DisplayName);
        Assert.Equal(
            ["Events", "Tasks", "Gateways"],
            toolbox.GetGroups(section.SectionId)
                .Select(static group => group.DisplayName));
        Assert.All(toolbox.Groups, static group => Assert.NotNull(group.Icon));
        Assert.Equal(
            [
                "Start Event",
                "Message Catch Event",
                "Message Throw Event",
                "Timer Catch Event",
                "Signal Catch Event",
                "Signal Throw Event",
                "End Event",
            ],
            toolbox.GetItems(toolbox.Groups[0].GroupId)
                .Select(static item => item.DisplayName));
        Assert.Equal(
            [
                "Task",
                "User Task",
                "Manual Task",
                "Service Task",
                "Send Task",
                "Receive Task",
            ],
            toolbox.GetItems(toolbox.Groups[1].GroupId)
                .Select(static item => item.DisplayName));
        Assert.Equal(
            [
                "Exclusive Gateway",
                "Parallel Gateway",
                "Inclusive Gateway",
                "Event-Based Gateway",
            ],
            toolbox.GetItems(toolbox.Groups[2].GroupId)
                .Select(static item => item.DisplayName));
        var messageIcons = toolbox.Items.Where(item =>
            item.ElementTypeId == BpmnSemanticTypes.MessageCatchEvent ||
            item.ElementTypeId == BpmnSemanticTypes.MessageThrowEvent)
            .Select(static item => item.Icon)
            .ToArray();
        var signalIcons = toolbox.Items.Where(item =>
            item.ElementTypeId == BpmnSemanticTypes.SignalCatchEvent ||
            item.ElementTypeId == BpmnSemanticTypes.SignalThrowEvent)
            .Select(static item => item.Icon)
            .ToArray();
        Assert.Equal(2, messageIcons.Distinct().Count());
        Assert.Equal(2, signalIcons.Distinct().Count());
    }

    [Fact]
    public void PlacementUsesSharedEventSizePinnedDefaultsAndNoHiddenAnchors()
    {
        var registration = BpmnPluginRegistration.N7;
        var toolbox = new ToolboxCatalog(registration.ToolboxContributions);
        var placements = new ToolboxPlacementCatalog(
            registration.ToolboxPlacementRegistrations,
            toolbox);
        var snapshot = Snapshot([]);
        var expectedCommands = new Dictionary<SemanticTypeId, Type>
        {
            [BpmnSemanticTypes.MessageThrowEvent] =
                typeof(CreateBpmnMessageThrowEventCommand),
            [BpmnSemanticTypes.SignalCatchEvent] =
                typeof(CreateBpmnSignalCatchEventCommand),
            [BpmnSemanticTypes.SignalThrowEvent] =
                typeof(CreateBpmnSignalThrowEventCommand),
        };
        foreach (var (typeId, commandType) in expectedCommands)
        {
            var item = Assert.Single(toolbox.Items, candidate =>
                candidate.ElementTypeId == typeId);
            Assert.True(placements.TryGetRegistration(item.ItemId, out var placement));
            var planResult = placement.CommandFactory.CreatePlan(
                new ToolboxPlacementRequest(
                    item.ItemId,
                    snapshot,
                    snapshot.Revision,
                    new PointD(118d, 98d),
                    new FixedIdentityProvider(
                        new SemanticElementId($"placed:{typeId.Value}"),
                        new VisualStateId($"placed:{typeId.Value}:visual"))));
            var command = Assert.IsAssignableFrom<BpmnElementCreationCommand>(
                Assert.IsType<ToolboxPlacementPlan>(planResult.Plan).Command);
            Assert.Equal(commandType, command.GetType());
            Assert.Equal(new SizeD(36d, 36d), command.Size);
            Assert.Equal(new PointD(100d, 80d), command.Position);
            Assert.Equal(VisualPlacementMode.Pinned, command.PlacementMode);
        }

        var messageItem = Assert.Single(toolbox.Items, candidate =>
            candidate.ElementTypeId == BpmnSemanticTypes.MessageThrowEvent);
        Assert.True(placements.TryGetRegistration(messageItem.ItemId, out var boundary));
        var rejected = boundary.CommandFactory.CreatePlan(new ToolboxPlacementRequest(
            messageItem.ItemId,
            snapshot,
            snapshot.Revision,
            new PointD(17d, 18d),
            new FixedIdentityProvider(
                new SemanticElementId("rejected"),
                new VisualStateId("rejected:visual"))));
        Assert.Null(rejected.Plan);
    }

    [Fact]
    public void SceneUsesOneDoubleCircleBodyAndDistinctNonHittableMarkers()
    {
        var nodes = BpmnIntermediateEventSemanticTypes.All
            .Select((typeId, index) => Node($"event-{index}", typeId))
            .ToArray();
        var geometries = nodes.Select((node, index) => new LayoutNodeGeometry(
            node.Id,
            new RectD(index * 50d, 0d, 36d, 36d),
            Matrix2D.CreateTranslation(index * 50d, 0d))).ToArray();
        var scene = Contribute(
            new ProjectedGraph(DocumentId, Revision, nodes: nodes),
            new LayoutResult(
                DocumentId,
                Revision,
                BpmnAlgorithmIds.DefaultLayout,
                new LayoutComputation(geometries)));

        Assert.Equal(5, scene.CanonicalItemVisualOverrides.Length);
        Assert.All(scene.CanonicalItemVisualOverrides, body =>
        {
            Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, body.Geometry.Kind);
            Assert.Equal(new RectD(0d, 0d, 36d, 36d), body.Geometry.Bounds);
        });
        foreach (var node in nodes)
        {
            var markers = scene.Items.Where(item =>
                item.Origin.SemanticElementId == node.Source.SemanticElementId).ToArray();
            Assert.Contains(markers, marker =>
                marker.Origin.StableSourceKey?.Contains("inner-ring",
                    StringComparison.Ordinal) == true);
            Assert.All(markers, marker =>
            {
                Assert.Equal(Canvas2DSceneLayer.Decoration, marker.Layer);
                Assert.Equal(Canvas2DHitTestMode.None, marker.HitTestPolicy.Mode);
                Assert.True(marker.Origin.Categories.HasFlag(
                    Canvas2DSceneOriginCategory.RegisteredExtension));
                Assert.True(marker.Geometry.Bounds.Left >= 0d);
                Assert.True(marker.Geometry.Bounds.Top >= 0d);
                Assert.True(marker.Geometry.Bounds.Right <= 36d);
                Assert.True(marker.Geometry.Bounds.Bottom <= 36d);
                Assert.True(double.IsFinite(marker.Geometry.Bounds.Width));
                Assert.True(double.IsFinite(marker.Geometry.Bounds.Height));
            });
        }

        var messageCatch = Markers(scene, nodes[0]);
        var messageThrow = Markers(scene, nodes[1]);
        var signalCatch = Markers(scene, nodes[3]);
        var signalThrow = Markers(scene, nodes[4]);
        Assert.DoesNotContain(messageCatch, marker => marker.Style.Fill == "#000000");
        Assert.Contains(messageThrow, marker => marker.Style.Fill == "#000000");
        Assert.DoesNotContain(signalCatch, marker => marker.Style.Fill == "#000000");
        Assert.Contains(signalThrow, marker => marker.Style.Fill == "#000000");
        Assert.NotEqual(
            messageCatch.Select(static marker => marker.Style).ToArray(),
            messageThrow.Select(static marker => marker.Style).ToArray());
        Assert.NotEqual(
            signalCatch.Select(static marker => marker.Style).ToArray(),
            signalThrow.Select(static marker => marker.Style).ToArray());
    }

    [Fact]
    public async Task CreationPropertiesLabelsAndUndoRedoPreserveExactIdentity()
    {
        foreach (var (typeId, index) in N7Types().Select((typeId, index) =>
                     (typeId, index)))
        {
            var registration = BpmnPluginRegistration.N7;
            var provider = new ElementConnectorAnchorPolicyRegistry(
                registration.ConnectorAnchorPolicies);
            var creation = DocumentFactory.CreateEmpty(
                DocumentId,
                connectorAnchorPolicyProvider: provider);
            var document = Assert.IsType<Document>(creation.Document);
            var processor = Processor(registration, provider);
            var history = new HistoryManager(document);
            var elementId = new SemanticElementId($"created:{index}");
            var visualId = new VisualStateId($"created:{index}:visual");

            var created = await history.ExecuteAsync(
                processor,
                CreateEventCommand(typeId, document.Revision, elementId, visualId));
            Assert.True(created.IsCommitted);
            var createdSnapshot = document.CaptureSnapshot();
            var element = Assert.Single(createdSnapshot.SemanticModel.Elements);
            var visual = Assert.Single(createdSnapshot.VisualModel.VisualStates);
            Assert.Equal(typeId, element.TypeId);
            Assert.Equal(elementId, element.Id);
            Assert.Equal(visualId, visual.Id);
            Assert.Equal(VisualPlacementMode.Pinned, visual.PlacementMode);
            Assert.Empty(visual.ConnectorAnchors);

            Assert.True((await history.ExecuteAsync(
                processor,
                new UpdateSemanticElementPropertyCommand(
                    DocumentId,
                    document.Revision,
                    elementId,
                    BpmnSemanticProperties.Name,
                    PropertyValue.FromText("Updated name")))).IsCommitted);
            var labelOverride = new NodeLabelVisualOverride(4d, 31d, 72d, 42d);
            Assert.True((await history.ExecuteAsync(
                processor,
                new UpdateNodeLabelVisualOverrideCommand(
                    DocumentId,
                    document.Revision,
                    visualId,
                    labelOverride))).IsCommitted);
            Assert.True(NodeLabelVisualOverride.TryRead(
                document.VisualModel.VisualStates.Single().Properties,
                out var storedOverride));
            Assert.Equal(labelOverride, storedOverride);

            Assert.True((await history.UndoAsync(processor)).IsCommitted);
            Assert.False(NodeLabelVisualOverride.TryRead(
                document.VisualModel.VisualStates.Single().Properties,
                out _));
            Assert.True((await history.RedoAsync(processor)).IsCommitted);
            Assert.True(NodeLabelVisualOverride.TryRead(
                document.VisualModel.VisualStates.Single().Properties,
                out storedOverride));
            Assert.Equal(labelOverride, storedOverride);
        }
    }

    [Fact]
    public async Task EventBasedRulesAcceptSignalCatchAndRejectThrowEventsAtomically()
    {
        foreach (var targetType in new[]
                 {
                     BpmnSemanticTypes.MessageCatchEvent,
                     BpmnSemanticTypes.TimerCatchEvent,
                     BpmnSemanticTypes.SignalCatchEvent,
                     BpmnSemanticTypes.ReceiveTask,
                 })
        {
            var harness = FlowHarness(
                BpmnSemanticTypes.EventBasedGateway,
                targetType,
                includeTargetAnchor: true);
            Assert.True((await harness.History.ExecuteAsync(
                harness.Processor,
                FlowCommand(harness.Document.Revision))).IsCommitted);
        }

        foreach (var targetType in new[]
                 {
                     BpmnSemanticTypes.MessageThrowEvent,
                     BpmnSemanticTypes.SignalThrowEvent,
                 })
        {
            var harness = FlowHarness(
                BpmnSemanticTypes.EventBasedGateway,
                targetType,
                includeTargetAnchor: false,
                smartTarget: true);
            var before = harness.Document.CaptureSnapshot();
            var historyBefore = harness.History.CaptureStatus();
            var rejected = await harness.History.ExecuteAsync(
                harness.Processor,
                SmartFlowCommand(harness.Document.Revision));
            Assert.False(rejected.IsCommitted);
            Assert.Contains(rejected.Diagnostics, diagnostic =>
                diagnostic.Code == BpmnCommandDiagnosticCodes.EventBasedGatewayTargetInvalid);
            Assert.Equal(before, harness.Document.CaptureSnapshot());
            Assert.Equal(historyBefore, harness.History.CaptureStatus());
            Assert.DoesNotContain(
                harness.Document.VisualModel.VisualStates.SelectMany(static visual =>
                    visual.ConnectorAnchors),
                anchor => anchor.Id == TargetAnchorId);
        }

        foreach (var pair in new[]
                 {
                     (BpmnSemanticTypes.Task, BpmnSemanticTypes.MessageThrowEvent),
                     (BpmnSemanticTypes.Task, BpmnSemanticTypes.SignalCatchEvent),
                     (BpmnSemanticTypes.Task, BpmnSemanticTypes.SignalThrowEvent),
                     (BpmnSemanticTypes.MessageThrowEvent, BpmnSemanticTypes.Task),
                     (BpmnSemanticTypes.SignalCatchEvent, BpmnSemanticTypes.Task),
                     (BpmnSemanticTypes.SignalThrowEvent, BpmnSemanticTypes.Task),
                 })
        {
            var ordinary = FlowHarness(pair.Item1, pair.Item2, includeTargetAnchor: true);
            Assert.True((await ordinary.Processor.ExecuteAsync(
                ordinary.Document,
                FlowCommand(ordinary.Document.Revision))).IsCommitted);
        }

        var eligibility = Assert.IsAssignableFrom<IAnchorConnectionTargetEligibility>(
            Assert.Single(BpmnPluginRegistration.N7.AnchorConnectionCreationRegistrations)
                .TargetEligibility);
        var signal = FlowHarness(
            BpmnSemanticTypes.EventBasedGateway,
            BpmnSemanticTypes.SignalCatchEvent,
            includeTargetAnchor: false,
            smartTarget: true);
        Assert.True(eligibility.CanAcquire(new AnchorConnectionTargetEligibilityRequest(
            signal.Document.CaptureSnapshot(),
            signal.Document.Revision,
            SourceId,
            SourceVisualId,
            SourceAnchorId,
            TargetId,
            TargetVisualId)));
        Assert.True((await signal.History.ExecuteAsync(
            signal.Processor,
            SmartFlowCommand(signal.Document.Revision))).IsCommitted);
    }

    [Fact]
    public async Task SignalCatchConfigurationIsOrderIndependentAndReconnectsExactly()
    {
        var gateway = Element("gateway", BpmnSemanticTypes.EventBasedGateway);
        var message = Element("message", BpmnSemanticTypes.MessageCatchEvent);
        var timer = Element("timer", BpmnSemanticTypes.TimerCatchEvent);
        var signal = Element("signal", BpmnSemanticTypes.SignalCatchEvent);
        var receive = Element("receive", BpmnSemanticTypes.ReceiveTask);
        var task = Element("task", BpmnSemanticTypes.Task);
        var messageThrow = Element("message-throw", BpmnSemanticTypes.MessageThrowEvent);
        var signalThrow = Element("signal-throw", BpmnSemanticTypes.SignalThrowEvent);

        var messageMode = ExecutableFlowHarness(
            [gateway, message, timer, signal],
            [Flow("gateway-message", gateway, message),
                Flow("gateway-timer", gateway, timer)],
            gateway.Id,
            signal.Id);
        Assert.True((await messageMode.Processor.ExecuteAsync(
            messageMode.Document,
            messageMode.Command)).IsCommitted);

        var receiveMode = ExecutableFlowHarness(
            [gateway, receive, timer, signal],
            [Flow("gateway-receive", gateway, receive),
                Flow("gateway-timer", gateway, timer)],
            gateway.Id,
            signal.Id);
        Assert.True((await receiveMode.Processor.ExecuteAsync(
            receiveMode.Document,
            receiveMode.Command)).IsCommitted);

        var mixedMode = ExecutableFlowHarness(
            [gateway, message, receive, signal],
            [Flow("gateway-message", gateway, message),
                Flow("gateway-signal", gateway, signal)],
            gateway.Id,
            receive.Id);
        var mixedResult = await mixedMode.Processor.ExecuteAsync(
            mixedMode.Document,
            mixedMode.Command);
        Assert.False(mixedResult.IsCommitted);
        Assert.Contains(mixedResult.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes
                .EventBasedGatewayMixedMessageReceptionModes);

        var ordinaryFirst = ExecutableFlowHarness(
            [gateway, task, signal],
            [Flow("task-signal", task, signal)],
            gateway.Id,
            signal.Id);
        var ordinaryFirstResult = await ordinaryFirst.Processor.ExecuteAsync(
            ordinaryFirst.Document,
            ordinaryFirst.Command);
        Assert.False(ordinaryFirstResult.IsCommitted);
        Assert.Contains(ordinaryFirstResult.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes
                .EventBasedTargetAdditionalIncoming);

        var gatewayFirst = ExecutableFlowHarness(
            [gateway, task, signal],
            [Flow("gateway-signal", gateway, signal)],
            task.Id,
            signal.Id);
        var gatewayFirstResult = await gatewayFirst.Processor.ExecuteAsync(
            gatewayFirst.Document,
            gatewayFirst.Command);
        Assert.False(gatewayFirstResult.IsCommitted);
        Assert.Contains(gatewayFirstResult.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes
                .EventBasedTargetAdditionalIncoming);

        var validReconnect = ExecutableFlowHarness(
            [gateway, timer, signal],
            [Flow("main", gateway, timer)],
            gateway.Id,
            signal.Id);
        var validReconnectResult = await validReconnect.Processor.ExecuteAsync(
            validReconnect.Document,
            ReconnectTarget(validReconnect.Document, "main", timer.Id, signal.Id));
        Assert.True(validReconnectResult.IsCommitted);
        Assert.Equal(signal.Id, validReconnect.Document.SemanticModel.Relationships.Single()
            .TargetId);
        Assert.Equal(new SemanticElementId("main"),
            validReconnect.Document.VisualModel.VisualStates.Single(visual =>
                visual.Id == new VisualStateId("main:visual")).SemanticElementId);

        foreach (var throwTarget in new[] { messageThrow, signalThrow })
        {
            var invalidReconnect = ExecutableFlowHarness(
                [gateway, signal, throwTarget],
                [Flow("main", gateway, signal)],
                gateway.Id,
                throwTarget.Id);
            var before = invalidReconnect.Document.CaptureSnapshot();
            var rejected = await invalidReconnect.Processor.ExecuteAsync(
                invalidReconnect.Document,
                ReconnectTarget(
                    invalidReconnect.Document,
                    "main",
                    signal.Id,
                    throwTarget.Id));
            Assert.False(rejected.IsCommitted);
            Assert.Contains(rejected.Diagnostics, diagnostic =>
                diagnostic.Code == BpmnCommandDiagnosticCodes
                    .EventBasedGatewayTargetInvalid);
            Assert.Equal(before, invalidReconnect.Document.CaptureSnapshot());
        }

        var validModelIssues = new BpmnStructuralValidationRule().Validate(
            new ModelValidationContext(Snapshot(
                [gateway, message, signal, timer],
                [Flow("gateway-message", gateway, message),
                    Flow("gateway-signal", gateway, signal),
                    Flow("gateway-timer", gateway, timer)])));
        Assert.DoesNotContain(validModelIssues, issue =>
            issue.Code == BpmnModelValidationCodes.EventBasedGatewayInvalidTarget ||
            issue.Code == BpmnModelValidationCodes.EventBasedTargetAdditionalIncoming ||
            issue.Code == BpmnModelValidationCodes
                .EventBasedGatewayMixedMessageReceptionModes);
        var invalidModelIssues = new BpmnStructuralValidationRule().Validate(
            new ModelValidationContext(Snapshot(
                [gateway, signal, signalThrow, task],
                [Flow("gateway-signal", gateway, signal),
                    Flow("gateway-throw", gateway, signalThrow),
                    Flow("task-signal", task, signal)])));
        Assert.Single(invalidModelIssues, issue =>
            issue.Code == BpmnModelValidationCodes.EventBasedGatewayInvalidTarget);
        Assert.Single(invalidModelIssues, issue =>
            issue.Code == BpmnModelValidationCodes.EventBasedTargetAdditionalIncoming);
    }

    [Fact]
    public void StructuralValidationTreatsEveryN7TypeAsAnOrdinaryReadableFlowNode()
    {
        foreach (var typeId in N7Types())
        {
            var start = Element("start", BpmnSemanticTypes.StartEvent);
            var intermediate = new SemanticElementSnapshot(
                new SemanticElementId($"isolated:{typeId.Value}"),
                typeId,
                [new(BpmnSemanticProperties.Name, PropertyValue.FromText("Readable name"))]);
            var end = Element("end", BpmnSemanticTypes.EndEvent);
            var snapshot = Snapshot(
                [start, intermediate, end],
                [Flow("start-end", start, end)]);
            var issues = new BpmnStructuralValidationRule().Validate(
                new ModelValidationContext(snapshot));
            var isolated = Assert.Single(issues, issue =>
                issue.Code == BpmnModelValidationCodes.FlowNodeIsolated &&
                issue.Target.SemanticElementId == intermediate.Id);
            var displayName = typeId == BpmnSemanticTypes.MessageThrowEvent
                ? "Message Throw Event"
                : typeId == BpmnSemanticTypes.SignalCatchEvent
                    ? "Signal Catch Event"
                    : "Signal Throw Event";
            Assert.StartsWith(
                $"{displayName} \"Readable name\" [ID: {intermediate.Id.Value}]",
                isolated.Message,
                StringComparison.Ordinal);

            var unreachable = Snapshot(
                [start, intermediate, end],
                [Flow("start-end", start, end), Flow("event-end", intermediate, end)]);
            Assert.Single(
                new BpmnStructuralValidationRule().Validate(
                    new ModelValidationContext(unreachable)),
                issue => issue.Code == BpmnModelValidationCodes.NodeUnreachableFromStart &&
                    issue.Target.SemanticElementId == intermediate.Id);
            var cannotReachEnd = Snapshot(
                [start, intermediate, end],
                [Flow("start-event", start, intermediate), Flow("start-end", start, end)]);
            Assert.Single(
                new BpmnStructuralValidationRule().Validate(
                    new ModelValidationContext(cannotReachEnd)),
                issue => issue.Code == BpmnModelValidationCodes.NodeCannotReachEnd &&
                    issue.Target.SemanticElementId == intermediate.Id);
        }
    }

    private static SemanticTypeId[] N7Types() =>
    [
        BpmnSemanticTypes.MessageThrowEvent,
        BpmnSemanticTypes.SignalCatchEvent,
        BpmnSemanticTypes.SignalThrowEvent,
    ];

    private static Canvas2DSceneItem[] Markers(
        Canvas2DSceneContribution scene,
        ProjectedNode node) =>
        scene.Items.Where(item =>
            item.Origin.SemanticElementId == node.Source.SemanticElementId).ToArray();

    private static Canvas2DSceneContribution Contribute(
        ProjectedGraph graph,
        LayoutResult layout)
    {
        var registration = Assert.Single(BpmnPluginRegistration.N7.SceneContributors);
        var result = registration.Contributor.Contribute(
            new Canvas2DSceneContributionContext(
                graph,
                layout,
                new RoutingResult(
                    DocumentId,
                    Revision,
                    BpmnAlgorithmIds.DefaultLayout,
                    BpmnAlgorithmIds.DefaultRouting,
                    RoutingComputation.Empty),
                new VisualModelSnapshot(DocumentId, Revision),
                EditorStateSnapshot.Empty,
                Canvas2DSceneConfiguration.Default,
                registration.Descriptor));
        Assert.True(result.Succeeded);
        return Assert.IsType<Canvas2DSceneContribution>(result.Contribution);
    }

    private static ProjectedNode Node(string key, SemanticTypeId semanticTypeId)
    {
        var trace = new ProjectionSourceTrace(
            DocumentId,
            new ProjectionRuleId($"bpmn:n7-test/{key}"),
            ProjectionSourceKind.SemanticElement,
            new SemanticElementId($"bpmn:n7-test/{key}"),
            semanticTypeId,
            "node",
            new VisualStateId($"bpmn:n7-test/{key}:visual"));
        return new ProjectedNode(
            trace,
            projectedProperties:
            [
                new(ProjectedSemanticTypeProperty, PropertyValue.FromText(
                    semanticTypeId.Value)),
            ]);
    }

    private static ICommand CreateEventCommand(
        SemanticTypeId typeId,
        DocumentRevision revision,
        SemanticElementId elementId,
        VisualStateId visualId) => typeId == BpmnSemanticTypes.MessageThrowEvent
        ? new CreateBpmnMessageThrowEventCommand(
            DocumentId, revision, elementId, visualId, new PointD(20d, 20d),
            new SizeD(36d, 36d), "Message throw", VisualPlacementMode.Pinned,
            "Description")
        : typeId == BpmnSemanticTypes.SignalCatchEvent
            ? new CreateBpmnSignalCatchEventCommand(
                DocumentId, revision, elementId, visualId, new PointD(20d, 20d),
                new SizeD(36d, 36d), "Signal catch", VisualPlacementMode.Pinned,
                "Description")
            : new CreateBpmnSignalThrowEventCommand(
                DocumentId, revision, elementId, visualId, new PointD(20d, 20d),
                new SizeD(36d, 36d), "Signal throw", VisualPlacementMode.Pinned,
                "Description");

    private static CommandProcessor Processor(
        BpmnPluginRegistration registration,
        IElementConnectorAnchorPolicyProvider provider) =>
        new(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: provider);

    private static SemanticElementSnapshot Element(string id, SemanticTypeId typeId) =>
        new(new SemanticElementId(id), typeId);

    private static SemanticRelationshipSnapshot Flow(
        string id,
        SemanticElementSnapshot source,
        SemanticElementSnapshot target) =>
        new(new SemanticElementId(id), BpmnSemanticTypes.SequenceFlow, source.Id, target.Id);

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot>? flows = null) =>
        new(
            new SemanticModelSnapshot(DocumentId, Revision, elements, flows ?? []),
            new VisualModelSnapshot(DocumentId, Revision),
            new DocumentMetadataSnapshot(DocumentId, Revision));

    private static readonly SemanticElementId SourceId = new("source");
    private static readonly SemanticElementId TargetId = new("target");
    private static readonly SemanticElementId FlowId = new("flow");
    private static readonly VisualStateId SourceVisualId = new("source:visual");
    private static readonly VisualStateId TargetVisualId = new("target:visual");
    private static readonly VisualStateId FlowVisualId = new("flow:visual");
    private static readonly ConnectorAnchorId SourceAnchorId = new("source:anchor");
    private static readonly ConnectorAnchorId TargetAnchorId = new("target:anchor");

    private static Harness FlowHarness(
        SemanticTypeId sourceType,
        SemanticTypeId targetType,
        bool includeTargetAnchor,
        bool smartTarget = false)
    {
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [new SemanticElementSnapshot(SourceId, sourceType),
                    new SemanticElementSnapshot(TargetId, targetType)]),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                [
                    NodeVisual(SourceVisualId, SourceId, new ConnectorAnchor(
                        SourceAnchorId,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        0)),
                    NodeVisual(TargetVisualId, TargetId, includeTargetAnchor
                        ? new ConnectorAnchor(
                            TargetAnchorId,
                            ConnectorAnchorSide.Left,
                            ConnectorAnchorRole.Target,
                            0)
                        : null),
                ]),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
        var registration = BpmnPluginRegistration.N7;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.Create(snapshot, provider);
        Assert.True(construction.Succeeded, string.Join(" | ", construction.Diagnostics));
        var document = Assert.IsType<Document>(construction.Document);
        var processor = Processor(registration, provider);
        return new Harness(document, processor, new HistoryManager(document), smartTarget);
    }

    private static AdvancedFlowHarness ExecutableFlowHarness(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot> existingFlows,
        SemanticElementId sourceId,
        SemanticElementId targetId)
    {
        var elementArray = elements.ToArray();
        var flowArray = existingFlows.ToArray();
        var anchors = elementArray.ToDictionary(
            static element => element.Id,
            static _ => new List<ConnectorAnchor>());
        var connectorVisuals = new List<VisualStateSnapshot>();
        foreach (var flow in flowArray)
        {
            var sourceAnchorId = new ConnectorAnchorId($"{flow.Id.Value}:source");
            var targetAnchorId = new ConnectorAnchorId($"{flow.Id.Value}:target");
            anchors[flow.SourceId].Add(new ConnectorAnchor(
                sourceAnchorId,
                ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Source,
                anchors[flow.SourceId].Count(anchor =>
                    anchor.Side == ConnectorAnchorSide.Right)));
            anchors[flow.TargetId].Add(new ConnectorAnchor(
                targetAnchorId,
                ConnectorAnchorSide.Left,
                ConnectorAnchorRole.Target,
                anchors[flow.TargetId].Count(anchor =>
                    anchor.Side == ConnectorAnchorSide.Left)));
            connectorVisuals.Add(new VisualStateSnapshot(
                new VisualStateId($"{flow.Id.Value}:visual"),
                flow.Id,
                new PointD(0d, 0d),
                new SizeD(0d, 0d),
                VisualPlacementMode.Manual,
                sourceAnchorId: sourceAnchorId,
                targetAnchorId: targetAnchorId));
        }

        var candidateId = new SemanticElementId("candidate-flow");
        var candidateVisualId = new VisualStateId("candidate-flow:visual");
        var candidateSourceAnchorId = new ConnectorAnchorId("candidate-flow:source");
        var candidateTargetAnchorId = new ConnectorAnchorId("candidate-flow:target");
        anchors[sourceId].Add(new ConnectorAnchor(
            candidateSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            anchors[sourceId].Count(anchor => anchor.Side == ConnectorAnchorSide.Right)));
        anchors[targetId].Add(new ConnectorAnchor(
            candidateTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            anchors[targetId].Count(anchor => anchor.Side == ConnectorAnchorSide.Left)));
        var nodeVisuals = elementArray.Select((element, index) =>
            new VisualStateSnapshot(
                new VisualStateId($"{element.Id.Value}:visual"),
                element.Id,
                new PointD(20d + (index * 150d), 20d),
                new SizeD(120d, 80d),
                VisualPlacementMode.Pinned,
                connectorAnchors: anchors[element.Id]));
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                elementArray,
                flowArray),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                nodeVisuals.Concat(connectorVisuals)),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
        var registration = BpmnPluginRegistration.N7;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var creation = DocumentFactory.Create(snapshot, provider);
        Assert.True(creation.Succeeded, string.Join(" | ", creation.Diagnostics));
        var document = Assert.IsType<Document>(creation.Document);
        var processor = Processor(registration, provider);
        var command = new CreateBpmnSequenceFlowCommand(
            DocumentId,
            document.Revision,
            candidateId,
            candidateVisualId,
            sourceId,
            targetId,
            candidateSourceAnchorId,
            candidateTargetAnchorId);
        return new AdvancedFlowHarness(document, processor, command);
    }

    private static ReconnectBpmnSequenceFlowEndpointCommand ReconnectTarget(
        Document document,
        string relationshipId,
        SemanticElementId expectedTargetId,
        SemanticElementId newTargetId) =>
        new(
            DocumentId,
            document.Revision,
            new SemanticElementId(relationshipId),
            new VisualStateId($"{relationshipId}:visual"),
            ConnectorEndpointKind.Target,
            expectedTargetId,
            new ConnectorAnchorId($"{relationshipId}:target"),
            newTargetId,
            new ConnectorAnchorId("candidate-flow:target"));

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

    private static CreateBpmnSequenceFlowWithTargetAnchorCommand SmartFlowCommand(
        DocumentRevision revision) =>
        new(
            DocumentId,
            revision,
            FlowId,
            FlowVisualId,
            SourceId,
            TargetId,
            SourceAnchorId,
            TargetVisualId,
            TargetAnchorId,
            ConnectorAnchorSide.Left,
            0);

    private static VisualStateSnapshot NodeVisual(
        VisualStateId visualId,
        SemanticElementId elementId,
        ConnectorAnchor? anchor) =>
        new(
            visualId,
            elementId,
            new PointD(20d, 20d),
            new SizeD(36d, 36d),
            VisualPlacementMode.Pinned,
            connectorAnchors: anchor is null ? [] : [anchor]);

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
        HistoryManager History,
        bool SmartTarget);

    private sealed record AdvancedFlowHarness(
        Document Document,
        CommandProcessor Processor,
        ICommand Command);
}
