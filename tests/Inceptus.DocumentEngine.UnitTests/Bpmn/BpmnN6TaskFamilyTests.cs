using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Scene;
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

public sealed class BpmnN6TaskFamilyTests
{
    private const string ProjectedSemanticTypeProperty = "BPMN.ProjectedSemanticType";
    private static readonly DocumentId DocumentId = new("bpmn:n6:document");
    private static readonly DocumentRevision Revision = new(12);

    [Fact]
    public void SemanticTypesAndTaskFamilyClassificationAreExactAndNotationLocal()
    {
        Assert.Equal("BPMN.UserTask", BpmnSemanticTypes.UserTask.Value);
        Assert.Equal("BPMN.ManualTask", BpmnSemanticTypes.ManualTask.Value);
        Assert.Equal("BPMN.ServiceTask", BpmnSemanticTypes.ServiceTask.Value);
        Assert.Equal("BPMN.SendTask", BpmnSemanticTypes.SendTask.Value);
        Assert.Equal("BPMN.ReceiveTask", BpmnSemanticTypes.ReceiveTask.Value);
        Assert.Equal(
            [
                BpmnSemanticTypes.Task,
                BpmnSemanticTypes.UserTask,
                BpmnSemanticTypes.ManualTask,
                BpmnSemanticTypes.ServiceTask,
                BpmnSemanticTypes.SendTask,
                BpmnSemanticTypes.ReceiveTask,
            ],
            BpmnTaskSemanticTypes.All.AsEnumerable());
        Assert.All(BpmnTaskSemanticTypes.All, typeId =>
        {
            Assert.True(BpmnTaskSemanticTypes.IsTask(typeId));
        });
        Assert.False(BpmnTaskSemanticTypes.IsTask(BpmnSemanticTypes.EventBasedGateway));
        Assert.False(BpmnTaskSemanticTypes.IsTask(BpmnSemanticTypes.MessageCatchEvent));
        Assert.False(BpmnTaskSemanticTypes.IsTask(BpmnSemanticTypes.TimerCatchEvent));
    }

    [Fact]
    public void FactoryUsesOneSemanticTypeAuthorityAndExactlyTheSharedTaskProperties()
    {
        foreach (var typeId in BpmnTaskSemanticTypes.All)
        {
            var element = BpmnSemanticFactory.CreateTask(
                new SemanticElementId($"task:{typeId.Value}"),
                typeId,
                "TASK_CODE",
                "Task name",
                17,
                "Task description");

            Assert.Equal(typeId, element.TypeId);
            Assert.Equal(
                [
                    BpmnSemanticProperties.Code,
                    BpmnSemanticProperties.Description,
                    BpmnSemanticProperties.ElementNumber,
                    BpmnSemanticProperties.Name,
                ],
                element.Properties.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(PropertyValueKind.Integer,
                element.Properties[BpmnSemanticProperties.ElementNumber].Kind);
            Assert.Equal(17, element.Properties[
                BpmnSemanticProperties.ElementNumber].IntegerValue);
            Assert.DoesNotContain(element.Properties.Keys, key =>
                key.Contains("Assignee", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Role", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Endpoint", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Operation", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Message", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Correlation", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Queue", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Subscription", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Instantiate", StringComparison.OrdinalIgnoreCase));
        }

        var generic = BpmnSemanticFactory.CreateTask(
            new SemanticElementId("generic"), "GENERIC", "Generic", -4);
        Assert.Equal(BpmnSemanticTypes.Task, generic.TypeId);
        Assert.Equal(-4, generic.Properties[
            BpmnSemanticProperties.ElementNumber].IntegerValue);
    }

    [Fact]
    public void N6RegistersSharedProjectionPropertiesAnchorsAndOrderedToolboxData()
    {
        var n5 = BpmnPluginRegistration.N5;
        var n6 = BpmnPluginRegistration.N6;
        foreach (var typeId in BpmnTaskSemanticTypes.All)
        {
            Assert.Contains(n6.ProjectionRules, rule => rule.SemanticTypeId == typeId);
            var schema = Assert.Single(n6.PropertiesSchemas, item =>
                item.SemanticTypeId == typeId);
            Assert.Equal(
                ["Code", "Name", "Element number", "Description"],
                schema.Fields.Select(static field => field.DisplayName));
            Assert.Equal(
                [
                    ElementPropertyEditorKind.SingleLineText,
                    ElementPropertyEditorKind.SingleLineText,
                    ElementPropertyEditorKind.Integer,
                    ElementPropertyEditorKind.MultilineText,
                ],
                schema.Fields.Select(static field => field.EditorKind));
            var anchorPolicy = Assert.Single(n6.ConnectorAnchorPolicies, item =>
                item.ElementTypeId == typeId).Policy;
            Assert.All(
                new[] { anchorPolicy.Top, anchorPolicy.Right, anchorPolicy.Bottom, anchorPolicy.Left },
                edge =>
                {
                    Assert.Equal(ConnectorAnchorPolicyMode.DynamicUnlimited, edge.Mode);
                    Assert.Equal(
                        ConnectorAnchorRoleCapability.SourceOrTarget,
                        edge.AllowedRoles);
                });
        }

        Assert.All(BpmnTaskSemanticTypes.All.Skip(1), typeId =>
        {
            Assert.DoesNotContain(n5.ProjectionRules, rule => rule.SemanticTypeId == typeId);
            Assert.DoesNotContain(n5.PropertiesSchemas, schema =>
                schema.SemanticTypeId == typeId);
            Assert.DoesNotContain(n5.ConnectorAnchorPolicies, policy =>
                policy.ElementTypeId == typeId);
        });

        var taskItems = new ToolboxCatalog(n6.ToolboxContributions).Items
            .Where(item => BpmnTaskSemanticTypes.IsTask(item.ElementTypeId))
            .OrderBy(static item => item.Order)
            .ToArray();
        Assert.Equal(
            ["Task", "User Task", "Manual Task", "Service Task", "Send Task", "Receive Task"],
            taskItems.Select(static item => item.DisplayName));
        Assert.Equal(6, taskItems.Select(static item => item.ItemId).Distinct().Count());
        Assert.NotEqual(taskItems[4].Icon, taskItems[5].Icon);
    }

    [Fact]
    public void PlacementUsesMixedFamilyNumberingCanonicalSizeAndTypeSpecificDefaults()
    {
        var existing = new[]
        {
            BpmnSemanticFactory.CreateTask(new SemanticElementId("task"),
                BpmnSemanticTypes.Task, "TASK_8", "Task 8", 8),
            BpmnSemanticFactory.CreateTask(new SemanticElementId("user"),
                BpmnSemanticTypes.UserTask, "USER_TASK_13", "User Task 13", 13),
            BpmnSemanticFactory.CreateTask(new SemanticElementId("service"),
                BpmnSemanticTypes.ServiceTask, "SERVICE_TASK_21", "Service Task 21", 21),
        };
        var snapshot = Snapshot(existing);
        var registration = BpmnPluginRegistration.N6;
        var toolbox = new ToolboxCatalog(registration.ToolboxContributions);
        var manual = Assert.Single(toolbox.Items, item =>
            item.ElementTypeId == BpmnSemanticTypes.ManualTask);
        var placement = new ToolboxPlacementCatalog(
            registration.ToolboxPlacementRegistrations,
            toolbox);
        Assert.True(placement.TryGetRegistration(manual.ItemId, out var placementRegistration));
        var result = placementRegistration.CommandFactory.CreatePlan(
            new ToolboxPlacementRequest(
                manual.ItemId,
                snapshot,
                snapshot.Revision,
                new PointD(260d, 180d),
                new FixedIdentityProvider(
                    new SemanticElementId("manual"),
                    new VisualStateId("manual:visual"))));

        var command = Assert.IsType<CreateBpmnTaskCommand>(
            Assert.IsType<ToolboxPlacementPlan>(result.Plan).Command);
        Assert.Equal(BpmnSemanticTypes.ManualTask, command.TaskTypeId);
        Assert.Equal(22, command.ElementNumber);
        Assert.Equal("MANUAL_TASK_22", command.Code);
        Assert.Equal("Manual Task 22", command.Name);
        Assert.Equal("Manual Task 22 created from the Toolbox.", command.Description);
        Assert.Equal(new SizeD(120d, 80d), command.Size);
        Assert.Equal(new PointD(200d, 140d), command.Position);
        Assert.Equal(VisualPlacementMode.Pinned, command.PlacementMode);
    }

    [Fact]
    public void SpecializedMarkersAreFiniteInsideSharedTaskBodiesAndNeverHittable()
    {
        var nodes = BpmnTaskSemanticTypes.All
            .Select((typeId, index) => Node($"task-{index}", typeId))
            .ToArray();
        var geometries = nodes
            .Select((node, index) => new LayoutNodeGeometry(
                node.Id,
                new RectD(index * 150d, 0d, 120d, 80d),
                Matrix2D.CreateTranslation(index * 150d, 0d)))
            .ToArray();
        var contribution = Contribute(
            new ProjectedGraph(DocumentId, Revision, nodes: nodes),
            new LayoutResult(
                DocumentId,
                Revision,
                BpmnAlgorithmIds.DefaultLayout,
                new LayoutComputation(geometries)));

        Assert.Equal(6, contribution.CanonicalItemVisualOverrides.Length);
        Assert.All(contribution.CanonicalItemVisualOverrides, body =>
        {
            Assert.Equal(Canvas2DSceneGeometryKind.Path, body.Geometry.Kind);
            Assert.Equal(new RectD(0d, 0d, 120d, 80d), body.Geometry.Bounds);
        });
        var expectedCounts = new Dictionary<SemanticTypeId, int>
        {
            [BpmnSemanticTypes.Task] = 0,
            [BpmnSemanticTypes.UserTask] = 2,
            [BpmnSemanticTypes.ManualTask] = 1,
            [BpmnSemanticTypes.ServiceTask] = 2,
            [BpmnSemanticTypes.SendTask] = 2,
            [BpmnSemanticTypes.ReceiveTask] = 2,
        };
        foreach (var node in nodes)
        {
            var markers = contribution.Items.Where(item =>
                item.Origin.SemanticElementId == node.Source.SemanticElementId).ToArray();
            Assert.Equal(expectedCounts[node.Source.SemanticTypeId], markers.Length);
            Assert.All(markers, marker =>
            {
                Assert.Equal(Canvas2DSceneLayer.Decoration, marker.Layer);
                Assert.Equal(Canvas2DHitTestMode.None, marker.HitTestPolicy.Mode);
                Assert.True(marker.Origin.Categories.HasFlag(
                    Canvas2DSceneOriginCategory.RegisteredExtension));
                Assert.True(double.IsFinite(marker.Geometry.Bounds.X));
                Assert.True(double.IsFinite(marker.Geometry.Bounds.Y));
                Assert.True(double.IsFinite(marker.Geometry.Bounds.Width));
                Assert.True(double.IsFinite(marker.Geometry.Bounds.Height));
                Assert.True(marker.Geometry.Bounds.Left >= 0d);
                Assert.True(marker.Geometry.Bounds.Top >= 0d);
                Assert.True(marker.Geometry.Bounds.Right <= 120d);
                Assert.True(marker.Geometry.Bounds.Bottom <= 80d);
            });
        }

        var send = contribution.Items.Where(item =>
            item.Origin.SemanticElementId == nodes[4].Source.SemanticElementId).ToArray();
        var receive = contribution.Items.Where(item =>
            item.Origin.SemanticElementId == nodes[5].Source.SemanticElementId).ToArray();
        Assert.Contains(send, marker => marker.Style.Fill == "#000000");
        Assert.DoesNotContain(receive, marker => marker.Style.Fill == "#000000");
    }

    [Fact]
    public async Task GeneralizedTaskCommandCreatesEverySpecializationInOneHistoryUnit()
    {
        var registration = BpmnPluginRegistration.N6;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var creation = DocumentFactory.CreateEmpty(
            DocumentId,
            connectorAnchorPolicyProvider: provider);
        var document = Assert.IsType<Document>(creation.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: provider);
        var history = new HistoryManager(document);
        DocumentSnapshot? lastCommitted = null;
        foreach (var (typeId, index) in BpmnTaskSemanticTypes.All.Skip(1)
                     .Select((typeId, index) => (typeId, index)))
        {
            var result = await history.ExecuteAsync(
                processor,
                new CreateBpmnTaskCommand(
                    DocumentId,
                    document.Revision,
                    new SemanticElementId($"created:{index}"),
                    new VisualStateId($"created:{index}:visual"),
                    new PointD(20d + (index * 140d), 20d),
                    new SizeD(120d, 80d),
                    $"CODE_{index}",
                    $"Name {index}",
                    index + 1,
                    VisualPlacementMode.Pinned,
                    description: $"Description {index}",
                    taskTypeId: typeId));
            Assert.True(result.IsCommitted);
            lastCommitted = document.CaptureSnapshot();
            Assert.Equal(typeId, lastCommitted.SemanticModel.Elements.Single(element =>
                element.Id == new SemanticElementId($"created:{index}")).TypeId);
        }

        Assert.Equal(5, document.SemanticModel.ElementCount);
        Assert.Equal(5, document.VisualModel.Count);
        Assert.All(document.VisualModel.VisualStates, visual =>
            Assert.Empty(visual.ConnectorAnchors));
        Assert.Equal(5, history.CaptureStatus().EntryCount);
        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Equal(4, document.SemanticModel.ElementCount);
        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        var redone = document.CaptureSnapshot();
        Assert.All(lastCommitted!.SemanticModel.Elements, expected =>
            Assert.Equal(expected, redone.SemanticModel.Elements.Single(actual =>
                actual.Id == expected.Id)));
        Assert.All(lastCommitted.VisualModel.VisualStates, expected =>
            Assert.Equal(expected, redone.VisualModel.VisualStates.Single(actual =>
                actual.Id == expected.Id)));
    }

    [Fact]
    public async Task EventBasedConfigurationSupportsReceiveModeAndRejectsMixingUnsupportedTasksAndExtraIncoming()
    {
        var gateway = Element("gateway", BpmnSemanticTypes.EventBasedGateway);
        var receiveA = Element("receive-a", BpmnSemanticTypes.ReceiveTask);
        var receiveB = Element("receive-b", BpmnSemanticTypes.ReceiveTask);
        var timer = Element("timer", BpmnSemanticTypes.TimerCatchEvent);
        var message = Element("message", BpmnSemanticTypes.MessageCatchEvent);
        var user = Element("user", BpmnSemanticTypes.UserTask);
        var task = Element("task", BpmnSemanticTypes.Task);

        var receiveMode = ExecutableFlowHarness(
            [gateway, receiveA, receiveB, timer],
            [Flow("gateway-receive-a", gateway, receiveA), Flow("gateway-timer", gateway, timer)],
            gateway.Id,
            receiveB.Id);
        Assert.True((await receiveMode.Processor.ExecuteAsync(
            receiveMode.Document,
            receiveMode.Command)).IsCommitted);

        var messageMode = Snapshot(
            [gateway, message, timer, receiveA],
            [Flow("gateway-message", gateway, message), Flow("gateway-timer", gateway, timer)]);
        var mixedReceive = ExecutableFlowHarness(
            messageMode.SemanticModel.Elements,
            messageMode.SemanticModel.Relationships,
            gateway.Id,
            receiveA.Id);
        var mixedReceiveBefore = mixedReceive.Document.CaptureSnapshot();
        var mixedReceiveResult = await mixedReceive.Processor.ExecuteAsync(
            mixedReceive.Document,
            mixedReceive.Command);
        Assert.False(mixedReceiveResult.IsCommitted);
        Assert.Contains(mixedReceiveResult.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes
                .EventBasedGatewayMixedMessageReceptionModes);
        Assert.Equal(mixedReceiveBefore, mixedReceive.Document.CaptureSnapshot());

        var receiveOnly = Snapshot(
            [gateway, receiveA, message],
            [Flow("gateway-receive", gateway, receiveA)]);
        var mixedMessage = ExecutableFlowHarness(
            receiveOnly.SemanticModel.Elements,
            receiveOnly.SemanticModel.Relationships,
            gateway.Id,
            message.Id);
        var mixedMessageResult = await mixedMessage.Processor.ExecuteAsync(
            mixedMessage.Document,
            mixedMessage.Command);
        Assert.False(mixedMessageResult.IsCommitted);
        Assert.Contains(mixedMessageResult.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes
                .EventBasedGatewayMixedMessageReceptionModes);

        foreach (var unsupported in new[]
                 {
                     task,
                     user,
                     Element("manual", BpmnSemanticTypes.ManualTask),
                     Element("service", BpmnSemanticTypes.ServiceTask),
                     Element("send", BpmnSemanticTypes.SendTask),
                 })
        {
            var unsupportedHarness = ExecutableFlowHarness(
                [gateway, unsupported],
                [],
                gateway.Id,
                unsupported.Id);
            var result = await unsupportedHarness.Processor.ExecuteAsync(
                unsupportedHarness.Document,
                unsupportedHarness.Command);
            Assert.False(result.IsCommitted);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == BpmnCommandDiagnosticCodes
                    .EventBasedGatewayTargetInvalid);
        }

        var ordinaryFirst = Snapshot(
            [gateway, task, receiveA],
            [Flow("task-receive", task, receiveA)]);
        var ordinaryFirstHarness = ExecutableFlowHarness(
            ordinaryFirst.SemanticModel.Elements,
            ordinaryFirst.SemanticModel.Relationships,
            gateway.Id,
            receiveA.Id);
        var ordinaryFirstResult = await ordinaryFirstHarness.Processor.ExecuteAsync(
            ordinaryFirstHarness.Document,
            ordinaryFirstHarness.Command);
        Assert.False(ordinaryFirstResult.IsCommitted);
        Assert.Contains(ordinaryFirstResult.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes
                .EventBasedTargetAdditionalIncoming);

        var gatewayFirst = Snapshot(
            [gateway, task, receiveA],
            [Flow("gateway-receive", gateway, receiveA)]);
        var gatewayFirstHarness = ExecutableFlowHarness(
            gatewayFirst.SemanticModel.Elements,
            gatewayFirst.SemanticModel.Relationships,
            task.Id,
            receiveA.Id);
        var gatewayFirstResult = await gatewayFirstHarness.Processor.ExecuteAsync(
            gatewayFirstHarness.Document,
            gatewayFirstHarness.Command);
        Assert.False(gatewayFirstResult.IsCommitted);
        Assert.Contains(gatewayFirstResult.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes
                .EventBasedTargetAdditionalIncoming);

        var messageModeCreation = ExecutableFlowHarness(
            [gateway, message, timer],
            [Flow("gateway-message", gateway, message)],
            gateway.Id,
            timer.Id);
        Assert.True((await messageModeCreation.Processor.ExecuteAsync(
            messageModeCreation.Document,
            messageModeCreation.Command)).IsCommitted);

        var catchOrdinaryFirst = ExecutableFlowHarness(
            [gateway, task, timer],
            [Flow("task-timer", task, timer)],
            gateway.Id,
            timer.Id);
        var catchOrdinaryFirstResult = await catchOrdinaryFirst.Processor.ExecuteAsync(
            catchOrdinaryFirst.Document,
            catchOrdinaryFirst.Command);
        Assert.False(catchOrdinaryFirstResult.IsCommitted);
        Assert.Contains(catchOrdinaryFirstResult.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes
                .EventBasedTargetAdditionalIncoming);

        var catchGatewayFirst = ExecutableFlowHarness(
            [gateway, task, message],
            [Flow("gateway-message", gateway, message)],
            task.Id,
            message.Id);
        var catchGatewayFirstResult = await catchGatewayFirst.Processor.ExecuteAsync(
            catchGatewayFirst.Document,
            catchGatewayFirst.Command);
        Assert.False(catchGatewayFirstResult.IsCommitted);
        Assert.Contains(catchGatewayFirstResult.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes
                .EventBasedTargetAdditionalIncoming);
    }

    [Fact]
    public async Task OrdinaryTaskFamilySequenceFlowsUseTheGeneralEndpointRules()
    {
        var pairs = new[]
        {
            (BpmnSemanticTypes.Task, BpmnSemanticTypes.ReceiveTask),
            (BpmnSemanticTypes.ReceiveTask, BpmnSemanticTypes.Task),
            (BpmnSemanticTypes.UserTask, BpmnSemanticTypes.ServiceTask),
        };
        foreach (var (sourceType, targetType) in pairs)
        {
            var source = Element($"source:{sourceType.Value}", sourceType);
            var target = Element($"target:{targetType.Value}", targetType);
            var harness = ExecutableFlowHarness([source, target], [], source.Id, target.Id);

            var result = await harness.Processor.ExecuteAsync(
                harness.Document,
                harness.Command);

            Assert.True(result.IsCommitted);
        }
    }

    [Fact]
    public async Task SmartTargetAcquisitionAcceptsReceiveModeAndRejectsInvalidTargetsAtomically()
    {
        var gateway = Element("gateway", BpmnSemanticTypes.EventBasedGateway);
        var timer = Element("timer", BpmnSemanticTypes.TimerCatchEvent);
        var receive = Element("receive", BpmnSemanticTypes.ReceiveTask);
        var user = Element("user", BpmnSemanticTypes.UserTask);
        var message = Element("message", BpmnSemanticTypes.MessageCatchEvent);
        var eligibility = Assert.IsAssignableFrom<IAnchorConnectionTargetEligibility>(
            Assert.Single(BpmnPluginRegistration.N6.AnchorConnectionCreationRegistrations)
                .TargetEligibility);

        var valid = ExecutableFlowHarness(
            [gateway, timer, receive],
            [Flow("gateway-timer", gateway, timer)],
            gateway.Id,
            receive.Id,
            acquireTargetAnchor: true);
        var validBefore = valid.Document.CaptureSnapshot();
        Assert.True(CanAcquireSmartTarget(eligibility, validBefore, gateway.Id, receive.Id));
        var accepted = await valid.Processor.ExecuteAsync(valid.Document, valid.Command);
        Assert.True(accepted.IsCommitted);
        Assert.Contains(
            valid.Document.VisualModel.VisualStates.SelectMany(static visual =>
                visual.ConnectorAnchors),
            anchor => anchor.Id == valid.CandidateTargetAnchorId);

        var invalidUser = ExecutableFlowHarness(
            [gateway, user],
            [],
            gateway.Id,
            user.Id,
            acquireTargetAnchor: true);
        var invalidUserBefore = invalidUser.Document.CaptureSnapshot();
        Assert.False(CanAcquireSmartTarget(
            eligibility,
            invalidUserBefore,
            gateway.Id,
            user.Id));
        var invalidUserResult = await invalidUser.Processor.ExecuteAsync(
            invalidUser.Document,
            invalidUser.Command);
        Assert.False(invalidUserResult.IsCommitted);
        Assert.Contains(invalidUserResult.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.EventBasedGatewayTargetInvalid);
        AssertSmartRejectionIsAtomic(invalidUser, invalidUserBefore);

        var mixed = ExecutableFlowHarness(
            [gateway, message, receive],
            [Flow("gateway-message", gateway, message)],
            gateway.Id,
            receive.Id,
            acquireTargetAnchor: true);
        var mixedBefore = mixed.Document.CaptureSnapshot();
        Assert.False(CanAcquireSmartTarget(eligibility, mixedBefore, gateway.Id, receive.Id));
        var mixedResult = await mixed.Processor.ExecuteAsync(mixed.Document, mixed.Command);
        Assert.False(mixedResult.IsCommitted);
        Assert.Contains(mixedResult.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes
                .EventBasedGatewayMixedMessageReceptionModes);
        AssertSmartRejectionIsAtomic(mixed, mixedBefore);
    }

    [Fact]
    public async Task ReconnectionUsesTheSameConfigurationAuthorityAndPreservesRejectedConnectors()
    {
        var gateway = Element("gateway", BpmnSemanticTypes.EventBasedGateway);
        var receiveA = Element("receive-a", BpmnSemanticTypes.ReceiveTask);
        var receiveB = Element("receive-b", BpmnSemanticTypes.ReceiveTask);
        var user = Element("user", BpmnSemanticTypes.UserTask);
        var timer = Element("timer", BpmnSemanticTypes.TimerCatchEvent);
        var message = Element("message", BpmnSemanticTypes.MessageCatchEvent);
        var task = Element("task", BpmnSemanticTypes.Task);

        var valid = ExecutableFlowHarness(
            [gateway, receiveA, receiveB],
            [Flow("main", gateway, receiveA)],
            gateway.Id,
            receiveB.Id);
        var validResult = await valid.Processor.ExecuteAsync(
            valid.Document,
            ReconnectTarget(valid.Document, "main", receiveA.Id, receiveB.Id));
        Assert.True(validResult.IsCommitted);
        Assert.True(valid.Document.SemanticModel.TryGetRelationship(
            new SemanticElementId("main"),
            out var reconnected));
        Assert.Equal(receiveB.Id, reconnected!.TargetId);
        Assert.True(valid.Document.VisualModel.TryGetVisualState(
            new VisualStateId("main:visual"),
            out var reconnectedVisual));
        Assert.Equal(valid.CandidateTargetAnchorId, reconnectedVisual!.TargetAnchorId);
        Assert.Equal(new SemanticElementId("main"), reconnectedVisual.SemanticElementId);

        var invalidUser = ExecutableFlowHarness(
            [gateway, receiveA, user],
            [Flow("main", gateway, receiveA)],
            gateway.Id,
            user.Id);
        await AssertReconnectRejected(
            invalidUser,
            ReconnectTarget(invalidUser.Document, "main", receiveA.Id, user.Id),
            BpmnCommandDiagnosticCodes.EventBasedGatewayTargetInvalid);

        var mixed = ExecutableFlowHarness(
            [gateway, timer, message, receiveB],
            [Flow("main", gateway, timer), Flow("message-branch", gateway, message)],
            gateway.Id,
            receiveB.Id);
        await AssertReconnectRejected(
            mixed,
            ReconnectTarget(mixed.Document, "main", timer.Id, receiveB.Id),
            BpmnCommandDiagnosticCodes.EventBasedGatewayMixedMessageReceptionModes);

        var additionalIncoming = ExecutableFlowHarness(
            [gateway, receiveA, receiveB, task],
            [Flow("main", gateway, receiveA), Flow("ordinary", task, receiveB)],
            gateway.Id,
            receiveB.Id);
        await AssertReconnectRejected(
            additionalIncoming,
            ReconnectTarget(
                additionalIncoming.Document,
                "main",
                receiveA.Id,
                receiveB.Id),
            BpmnCommandDiagnosticCodes.EventBasedTargetAdditionalIncoming);
    }

    [Fact]
    public void N5EventBasedConfigurationValidationAgreesWithCommandRules()
    {
        var gateway = Element("gateway", BpmnSemanticTypes.EventBasedGateway);
        var receiveA = Element("receive-a", BpmnSemanticTypes.ReceiveTask);
        var receiveB = Element("receive-b", BpmnSemanticTypes.ReceiveTask);
        var timer = Element("timer", BpmnSemanticTypes.TimerCatchEvent);
        var message = Element("message", BpmnSemanticTypes.MessageCatchEvent);
        var task = Element("task", BpmnSemanticTypes.Task);
        var validReceiveMode = Snapshot(
            [gateway, receiveA, receiveB, timer],
            [
                Flow("gateway-receive-a", gateway, receiveA),
                Flow("gateway-receive-b", gateway, receiveB),
                Flow("gateway-timer", gateway, timer),
            ]);
        var validIssues = new BpmnStructuralValidationRule().Validate(
            new ModelValidationContext(validReceiveMode));
        Assert.DoesNotContain(validIssues, issue =>
            issue.Code == BpmnModelValidationCodes.EventBasedGatewayInvalidTarget ||
            issue.Code == BpmnModelValidationCodes
                .EventBasedGatewayMixedMessageReceptionModes ||
            issue.Code == BpmnModelValidationCodes.EventBasedTargetAdditionalIncoming);

        var mixed = Snapshot(
            [gateway, receiveA, message],
            [
                Flow("gateway-receive", gateway, receiveA),
                Flow("gateway-message", gateway, message),
            ]);
        var mixedIssues = new BpmnStructuralValidationRule().Validate(
            new ModelValidationContext(mixed));
        Assert.Single(mixedIssues, issue =>
            issue.Code == BpmnModelValidationCodes
                .EventBasedGatewayMixedMessageReceptionModes);

        var additionalIncoming = Snapshot(
            [gateway, receiveA, task],
            [
                Flow("gateway-receive", gateway, receiveA),
                Flow("task-receive", task, receiveA),
            ]);
        var incomingIssues = new BpmnStructuralValidationRule().Validate(
            new ModelValidationContext(additionalIncoming));
        Assert.Single(incomingIssues, issue =>
            issue.Code == BpmnModelValidationCodes.EventBasedTargetAdditionalIncoming);
    }

    [Fact]
    public void N5TreatsSpecializationsAsOrdinaryTasksAndFormatsTheirExactReadableTypes()
    {
        foreach (var typeId in BpmnTaskSemanticTypes.All.Skip(1))
        {
            var task = BpmnSemanticFactory.CreateTask(
                new SemanticElementId($"isolated:{typeId.Value}"),
                typeId,
                "CODE",
                "Readable name",
                25,
                "Description");
            var start = Element("start", BpmnSemanticTypes.StartEvent);
            var end = Element("end", BpmnSemanticTypes.EndEvent);
            var snapshot = Snapshot(
                [start, task, end],
                [Flow("start-end", start, end)]);
            var issues = new BpmnStructuralValidationRule().Validate(
                new ModelValidationContext(snapshot));
            var isolated = Assert.Single(issues, issue =>
                issue.Code == BpmnModelValidationCodes.FlowNodeIsolated &&
                issue.Target.SemanticElementId == task.Id);
            var displayName = typeId == BpmnSemanticTypes.UserTask ? "User Task" :
                typeId == BpmnSemanticTypes.ManualTask ? "Manual Task" :
                typeId == BpmnSemanticTypes.ServiceTask ? "Service Task" :
                typeId == BpmnSemanticTypes.SendTask ? "Send Task" : "Receive Task";
            Assert.StartsWith(
                $"{displayName} 25 \"Readable name\" " +
                $"[Code: CODE, ID: {task.Id.Value}]",
                isolated.Message,
                StringComparison.Ordinal);

            var unreachableSnapshot = Snapshot(
                [start, task, end],
                [Flow("start-end", start, end), Flow("task-end", task, end)]);
            Assert.Single(
                new BpmnStructuralValidationRule().Validate(
                    new ModelValidationContext(unreachableSnapshot)),
                issue => issue.Code == BpmnModelValidationCodes
                    .NodeUnreachableFromStart &&
                    issue.Target.SemanticElementId == task.Id);

            var cannotReachEndSnapshot = Snapshot(
                [start, task, end],
                [Flow("start-task", start, task), Flow("start-end", start, end)]);
            Assert.Single(
                new BpmnStructuralValidationRule().Validate(
                    new ModelValidationContext(cannotReachEndSnapshot)),
                issue => issue.Code == BpmnModelValidationCodes.NodeCannotReachEnd &&
                    issue.Target.SemanticElementId == task.Id);
        }
    }

    private static Canvas2DSceneContribution Contribute(
        ProjectedGraph graph,
        LayoutResult layout)
    {
        var registration = Assert.Single(BpmnPluginRegistration.N6.SceneContributors);
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
            new ProjectionRuleId($"bpmn:n6-test/{key}"),
            ProjectionSourceKind.SemanticElement,
            new SemanticElementId($"bpmn:n6-test/{key}"),
            semanticTypeId,
            "node",
            new VisualStateId($"bpmn:n6-test/{key}:visual"));
        return new ProjectedNode(
            trace,
            projectedProperties:
            [
                new(
                    ProjectedSemanticTypeProperty,
                    PropertyValue.FromText(semanticTypeId.Value)),
            ]);
    }

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
            new SemanticModelSnapshot(
                DocumentId,
                Revision,
                elements,
                flows ?? []),
            new VisualModelSnapshot(DocumentId, Revision),
            new DocumentMetadataSnapshot(DocumentId, Revision));

    private static FlowHarness ExecutableFlowHarness(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot> existingFlows,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        bool acquireTargetAnchor = false)
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
        var candidateTargetAnchorIndex = anchors[targetId].Count(anchor =>
            anchor.Side == ConnectorAnchorSide.Left);
        if (!acquireTargetAnchor)
        {
            anchors[targetId].Add(new ConnectorAnchor(
                candidateTargetAnchorId,
                ConnectorAnchorSide.Left,
                ConnectorAnchorRole.Target,
                candidateTargetAnchorIndex));
        }
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
        var registration = BpmnPluginRegistration.N6;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var creation = DocumentFactory.Create(snapshot, provider);
        Assert.True(creation.Succeeded, string.Join(" | ", creation.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        var document = Assert.IsType<Document>(creation.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: provider);
        ICommand command = acquireTargetAnchor
            ? new CreateBpmnSequenceFlowWithTargetAnchorCommand(
                DocumentId,
                document.Revision,
                candidateId,
                candidateVisualId,
                sourceId,
                targetId,
                candidateSourceAnchorId,
                new VisualStateId($"{targetId.Value}:visual"),
                candidateTargetAnchorId,
                ConnectorAnchorSide.Left,
                candidateTargetAnchorIndex)
            : new CreateBpmnSequenceFlowCommand(
                DocumentId,
                document.Revision,
                candidateId,
                candidateVisualId,
                sourceId,
                targetId,
                candidateSourceAnchorId,
                candidateTargetAnchorId);
        return new FlowHarness(document, processor, command, candidateTargetAnchorId);
    }

    private static bool CanAcquireSmartTarget(
        IAnchorConnectionTargetEligibility eligibility,
        DocumentSnapshot snapshot,
        SemanticElementId sourceId,
        SemanticElementId targetId) =>
        eligibility.CanAcquire(new AnchorConnectionTargetEligibilityRequest(
            snapshot,
            snapshot.Revision,
            sourceId,
            new VisualStateId($"{sourceId.Value}:visual"),
            new ConnectorAnchorId("candidate-flow:source"),
            targetId,
            new VisualStateId($"{targetId.Value}:visual")));

    private static void AssertSmartRejectionIsAtomic(
        FlowHarness harness,
        DocumentSnapshot before)
    {
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.DoesNotContain(
            harness.Document.VisualModel.VisualStates.SelectMany(static visual =>
                visual.ConnectorAnchors),
            anchor => anchor.Id == harness.CandidateTargetAnchorId);
        Assert.False(harness.Document.SemanticModel.TryGetRelationship(
            new SemanticElementId("candidate-flow"),
            out _));
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

    private static async Task AssertReconnectRejected(
        FlowHarness harness,
        ReconnectBpmnSequenceFlowEndpointCommand command,
        string diagnosticCode)
    {
        var before = harness.Document.CaptureSnapshot();
        var result = await harness.Processor.ExecuteAsync(harness.Document, command);

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == diagnosticCode);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
    }

    private sealed class FixedIdentityProvider(
        SemanticElementId semanticElementId,
        VisualStateId visualStateId) : IDocumentCreationIdentityProvider
    {
        public DocumentCreationIdentity CreateIdentity() =>
            new(semanticElementId, visualStateId);
    }

    private sealed record FlowHarness(
        Document Document,
        CommandProcessor Processor,
        ICommand Command,
        ConnectorAnchorId CandidateTargetAnchorId);
}
