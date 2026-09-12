using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnExclusiveGatewayCommandAndProjectionTests
{
    private static readonly DocumentId DocumentId = new("bpmn:m32-command-document");
    private static readonly SemanticElementId GatewayId = new("bpmn:m32:gateway");
    private static readonly VisualStateId GatewayVisualId = new("bpmn:m32:gateway-visual");

    [Fact]
    public async Task RegisteredCreationIsAtomicAndRoundTripsExactlyThroughHistory()
    {
        var document = EmptyDocument();
        var before = document.CaptureSnapshot();
        var processor = Processor();
        var history = new HistoryManager(document);
        var command = Gateway(document);

        var created = await history.ExecuteAsync(processor, command);

        Assert.True(created.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(1, history.CaptureStatus().EntryCount);
        Assert.True(document.SemanticModel.TryGetElement(GatewayId, out var gateway));
        Assert.Equal(BpmnSemanticTypes.ExclusiveGateway, gateway!.TypeId);
        Assert.Equal("APPROVED", gateway.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal("Approved?", gateway.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            "Route according to the approval decision.",
            gateway.Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.False(gateway.Properties.ContainsKey(BpmnSemanticProperties.ElementNumber));
        Assert.True(document.VisualModel.TryGetVisualState(GatewayVisualId, out var visual));
        Assert.Equal(GatewayId, visual!.SemanticElementId);
        Assert.Equal(new PointD(240d, 80d), visual.Position);
        Assert.Equal(new SizeD(48d, 48d), visual.Size);

        var committedElements = document.CaptureSnapshot().SemanticModel.Elements;
        var committedVisuals = document.CaptureSnapshot().VisualModel.VisualStates;

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Equal(
            before.SemanticModel.Elements.AsEnumerable(),
            document.CaptureSnapshot().SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            before.VisualModel.VisualStates.AsEnumerable(),
            document.CaptureSnapshot().VisualModel.VisualStates.AsEnumerable());

        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.Equal(
            committedElements.AsEnumerable(),
            document.CaptureSnapshot().SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            committedVisuals.AsEnumerable(),
            document.CaptureSnapshot().VisualModel.VisualStates.AsEnumerable());
    }

    [Fact]
    public async Task CreationAndCodeValidationRejectMalformedGatewayDataAtomically()
    {
        var document = EmptyDocument();
        var processor = Processor();
        var history = new HistoryManager(document);

        var malformed = await history.ExecuteAsync(
            processor,
            new CreateBpmnExclusiveGatewayCommand(
                DocumentId,
                document.Revision,
                GatewayId,
                GatewayVisualId,
                new PointD(0d, 0d),
                new SizeD(48d, 48d),
                " ",
                "Approved?"));

        Assert.False(malformed.IsCommitted);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Equal(0, history.CaptureStatus().EntryCount);
        Assert.Empty(document.SemanticModel.Elements);
        Assert.Empty(document.VisualModel.VisualStates);

        Assert.True((await history.ExecuteAsync(processor, Gateway(document))).IsCommitted);
        var beforeInvalidUpdate = document.CaptureSnapshot();
        var historyBeforeInvalidUpdate = history.CaptureStatus();
        var invalidUpdate = await history.ExecuteAsync(
            processor,
            new UpdateSemanticElementPropertyCommand(
                DocumentId,
                document.Revision,
                GatewayId,
                BpmnSemanticProperties.Code,
                PropertyValue.FromText(" ")));

        Assert.False(invalidUpdate.IsCommitted);
        Assert.Equal(beforeInvalidUpdate, document.CaptureSnapshot());
        Assert.Equal(historyBeforeInvalidUpdate, history.CaptureStatus());

        var validUpdate = await history.ExecuteAsync(
            processor,
            new UpdateSemanticElementPropertyCommand(
                DocumentId,
                document.Revision,
                GatewayId,
                BpmnSemanticProperties.Code,
                PropertyValue.FromText("CHECK_APPROVAL")));
        Assert.True(validUpdate.IsCommitted);
        Assert.Equal(
            "CHECK_APPROVAL",
            GatewayElement(document).Properties[BpmnSemanticProperties.Code].TextValue);

        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.Equal(
            "APPROVED",
            GatewayElement(document).Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.Equal(
            "CHECK_APPROVAL",
            GatewayElement(document).Properties[BpmnSemanticProperties.Code].TextValue);
    }

    [Fact]
    public async Task SequenceFlowAcceptsGatewayIncomingOutgoingBranchAndMergeDegrees()
    {
        var document = EmptyDocument();
        var processor = Processor();
        var history = new HistoryManager(document);
        var taskA = new SemanticElementId("bpmn:m32:task-a");
        var taskB = new SemanticElementId("bpmn:m32:task-b");
        var taskC = new SemanticElementId("bpmn:m32:task-c");
        var taskD = new SemanticElementId("bpmn:m32:task-d");
        var start = new SemanticElementId("bpmn:m32:start");
        var end = new SemanticElementId("bpmn:m32:end");

        Assert.True((await history.ExecuteAsync(processor, Gateway(document))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, Task(document, taskA, 21))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, Task(document, taskB, 22))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, Task(document, taskC, 23))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, Task(document, taskD, 24))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, Start(document, start))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, End(document, end))).IsCommitted);

        var taskASource = new ConnectorAnchorId("bpmn:m32:anchor:task-a-source");
        var taskDSource = new ConnectorAnchorId("bpmn:m32:anchor:task-d-source");
        var gatewayTargetA = new ConnectorAnchorId("bpmn:m32:anchor:gateway-target-a");
        var gatewayTargetD = new ConnectorAnchorId("bpmn:m32:anchor:gateway-target-d");
        var gatewaySourceB = new ConnectorAnchorId("bpmn:m32:anchor:gateway-source-b");
        var gatewaySourceC = new ConnectorAnchorId("bpmn:m32:anchor:gateway-source-c");
        var taskBTarget = new ConnectorAnchorId("bpmn:m32:anchor:task-b-target");
        var taskCTarget = new ConnectorAnchorId("bpmn:m32:anchor:task-c-target");
        await AddAnchorAsync(
            history, processor, document, VisualId(taskA), taskASource,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        await AddAnchorAsync(
            history, processor, document, VisualId(taskD), taskDSource,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        await AddAnchorAsync(
            history, processor, document, GatewayVisualId, gatewayTargetA,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0);
        await AddAnchorAsync(
            history, processor, document, GatewayVisualId, gatewayTargetD,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 1);
        await AddAnchorAsync(
            history, processor, document, GatewayVisualId, gatewaySourceB,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        await AddAnchorAsync(
            history, processor, document, GatewayVisualId, gatewaySourceC,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 1);
        await AddAnchorAsync(
            history, processor, document, VisualId(taskB), taskBTarget,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0);
        await AddAnchorAsync(
            history, processor, document, VisualId(taskC), taskCTarget,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0);

        Assert.True((await history.ExecuteAsync(
            processor,
            Flow(
                document,
                "task-a-to-gateway",
                taskA,
                GatewayId,
                taskASource,
                gatewayTargetA))).IsCommitted);
        Assert.True((await history.ExecuteAsync(
            processor,
            Flow(
                document,
                "task-d-to-gateway",
                taskD,
                GatewayId,
                taskDSource,
                gatewayTargetD))).IsCommitted);
        Assert.True((await history.ExecuteAsync(
            processor,
            Flow(
                document,
                "gateway-to-task-b",
                GatewayId,
                taskB,
                gatewaySourceB,
                taskBTarget))).IsCommitted);
        Assert.True((await history.ExecuteAsync(
            processor,
            Flow(
                document,
                "gateway-to-task-c",
                GatewayId,
                taskC,
                gatewaySourceC,
                taskCTarget))).IsCommitted);

        Assert.Equal(4, document.SemanticModel.RelationshipCount);
        Assert.Equal(
            2,
            document.SemanticModel.Relationships.Count(flow => flow.SourceId == GatewayId));
        Assert.Equal(
            2,
            document.SemanticModel.Relationships.Count(flow => flow.TargetId == GatewayId));

        var startTarget = new ConnectorAnchorId("bpmn:m32:anchor:start-target-invalid");
        var endSource = new ConnectorAnchorId("bpmn:m32:anchor:end-source-invalid");
        var taskATarget = new ConnectorAnchorId("bpmn:m32:anchor:task-a-target");
        await AddAnchorAsync(
            history, processor, document, VisualId(start), startTarget,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0);
        await AddAnchorAsync(
            history, processor, document, VisualId(end), endSource,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        await AddAnchorAsync(
            history, processor, document, VisualId(taskA), taskATarget,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0);

        var beforeInvalidDirections = document.CaptureSnapshot();
        Assert.False((await history.ExecuteAsync(
            processor,
            Flow(
                document,
                "task-to-start",
                taskA,
                start,
                taskASource,
                startTarget))).IsCommitted);
        Assert.False((await history.ExecuteAsync(
            processor,
            Flow(
                document,
                "end-to-task",
                end,
                taskA,
                endSource,
                taskATarget))).IsCommitted);
        Assert.Equal(beforeInvalidDirections, document.CaptureSnapshot());
    }

    [Fact]
    public async Task ProjectionProducesOneTraceableGatewayNodeWithoutCenteredNameLabel()
    {
        var document = EmptyDocument();
        var processor = Processor();
        Assert.True((await processor.ExecuteAsync(document, Gateway(document))).IsCommitted);

        var execution = new ProjectionEngine(BpmnPluginRegistration.M32.ProjectionRules)
            .Project(document.CaptureSnapshot());

        Assert.True(execution.IsSuccessful);
        var graph = Assert.IsType<ProjectedGraph>(execution.Graph);
        var node = Assert.Single(graph.Nodes);
        Assert.Empty(graph.Labels);
        Assert.Equal(GatewayId, node.Source.SemanticElementId);
        Assert.Equal(BpmnSemanticTypes.ExclusiveGateway, node.Source.SemanticTypeId);
        Assert.Equal(GatewayVisualId, node.Source.VisualStateId);
        Assert.Equal(
            "BPMN.ExclusiveGateway",
            node.ProjectedProperties["BPMN.ProjectedSemanticType"].TextValue);
        Assert.Equal(
            "Approved?",
            node.SemanticProperties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            "APPROVED",
            node.SemanticProperties[BpmnSemanticProperties.Code].TextValue);
        Assert.False(node.SemanticProperties.ContainsKey(BpmnSemanticProperties.ElementNumber));
    }

    [Fact]
    public void M322ComposesM321AndReplacesOnlyTheExclusiveGatewayProjectionRule()
    {
        var m321 = BpmnPluginRegistration.M321;
        var m322 = BpmnPluginRegistration.M322;

        Assert.Equal(m321.CommandHandlers.AsEnumerable(), m322.CommandHandlers.AsEnumerable());
        Assert.Equal(
            m321.CommandValidators.AsEnumerable(),
            m322.CommandValidators.AsEnumerable());
        Assert.Equal(m321.HistoryPolicies.AsEnumerable(), m322.HistoryPolicies.AsEnumerable());
        Assert.Equal(m321.LayoutAlgorithms.AsEnumerable(), m322.LayoutAlgorithms.AsEnumerable());
        Assert.Equal(m321.RoutingAlgorithms.AsEnumerable(), m322.RoutingAlgorithms.AsEnumerable());
        Assert.Equal(m321.SceneContributors.AsEnumerable(), m322.SceneContributors.AsEnumerable());
        Assert.Equal(
            m321.ToolboxContributions.AsEnumerable(),
            m322.ToolboxContributions.AsEnumerable());
        Assert.Equal(m321.PropertiesSchemas.AsEnumerable(), m322.PropertiesSchemas.AsEnumerable());
        Assert.Equal(
            m321.ConnectorAnchorPolicies.AsEnumerable(),
            m322.ConnectorAnchorPolicies.AsEnumerable());
        Assert.Equal(m321.ProjectionRules.Length, m322.ProjectionRules.Length);

        var previousGateway = Assert.Single(m321.ProjectionRules, registration =>
            registration.SemanticTypeId == BpmnSemanticTypes.ExclusiveGateway);
        var currentGateway = Assert.Single(m322.ProjectionRules, registration =>
            registration.SemanticTypeId == BpmnSemanticTypes.ExclusiveGateway);
        Assert.Equal(previousGateway.RuleId, currentGateway.RuleId);
        Assert.Equal(previousGateway.SourceKind, currentGateway.SourceKind);
        Assert.NotSame(previousGateway.Rule, currentGateway.Rule);
        Assert.Equal(
            m321.ProjectionRules.Where(registration =>
                registration.SemanticTypeId != BpmnSemanticTypes.ExclusiveGateway),
            m322.ProjectionRules.Where(registration =>
                registration.SemanticTypeId != BpmnSemanticTypes.ExclusiveGateway));
    }

    [Fact]
    public async Task M322ProjectsOneOutsideBelowGatewayNameWithStableOwnerTrace()
    {
        var document = EmptyDocument();
        var processor = Processor();
        Assert.True((await processor.ExecuteAsync(document, Gateway(document))).IsCommitted);

        var execution = new ProjectionEngine(BpmnPluginRegistration.M322.ProjectionRules)
            .Project(document.CaptureSnapshot());

        Assert.True(execution.IsSuccessful);
        var graph = Assert.IsType<ProjectedGraph>(execution.Graph);
        var node = Assert.Single(graph.Nodes);
        var label = Assert.Single(graph.Labels);
        var placement = Assert.IsType<NodeLabelPlacement>(label.NodePlacement);
        Assert.Equal(node.Id, label.OwnerId);
        Assert.Equal("Approved?", label.Text);
        Assert.Equal(GatewayId, label.Source.SemanticElementId);
        Assert.Equal(GatewayVisualId, label.Source.VisualStateId);
        Assert.Equal(NodeLabelPlacementKind.OutsideBelow, placement.Kind);
        Assert.Equal(8d, placement.Gap);
        Assert.Equal(160d, placement.MaximumWidth);
        Assert.Equal(NodeLabelInteractionPolicy.Fixed, label.NodeInteractionPolicy);
    }

    [Fact]
    public async Task M323AddsGatewayLabelEditingIntentAndOptionalNameLifecycle()
    {
        var m322 = BpmnPluginRegistration.M322;
        var m323 = BpmnPluginRegistration.M323;
        Assert.Equal(m322.CommandHandlers.AsEnumerable(), m323.CommandHandlers.AsEnumerable());
        Assert.Equal(m322.CommandValidators.AsEnumerable(), m323.CommandValidators.AsEnumerable());
        Assert.Equal(m322.HistoryPolicies.AsEnumerable(), m323.HistoryPolicies.AsEnumerable());
        Assert.Equal(m322.LayoutAlgorithms.AsEnumerable(), m323.LayoutAlgorithms.AsEnumerable());
        Assert.Equal(m322.RoutingAlgorithms.AsEnumerable(), m323.RoutingAlgorithms.AsEnumerable());
        Assert.Equal(m322.SceneContributors.AsEnumerable(), m323.SceneContributors.AsEnumerable());
        Assert.Equal(m322.ToolboxContributions.AsEnumerable(), m323.ToolboxContributions.AsEnumerable());
        Assert.Equal(m322.ConnectorAnchorPolicies.AsEnumerable(), m323.ConnectorAnchorPolicies.AsEnumerable());
        Assert.Equal(m322.ProjectionRules.Length, m323.ProjectionRules.Length);
        Assert.Equal(m322.PropertiesSchemas.Length, m323.PropertiesSchemas.Length);

        var previousGatewaySchema = Assert.Single(m322.PropertiesSchemas, schema =>
            schema.SemanticTypeId == BpmnSemanticTypes.ExclusiveGateway);
        var currentGatewaySchema = Assert.Single(m323.PropertiesSchemas, schema =>
            schema.SemanticTypeId == BpmnSemanticTypes.ExclusiveGateway);
        Assert.Equal(
            previousGatewaySchema.Fields
                .Where(static field => field.FieldId.Value != "name"),
            currentGatewaySchema.Fields
                .Where(static field => field.FieldId.Value != "name"));
        Assert.Equal(
            SemanticPropertyMutationKind.Name,
            Assert.Single(previousGatewaySchema.Fields, static field =>
                field.FieldId.Value == "name").MutationKind);
        Assert.Equal(
            SemanticPropertyMutationKind.Property,
            Assert.Single(currentGatewaySchema.Fields, static field =>
                field.FieldId.Value == "name").MutationKind);

        var previousGateway = Assert.Single(m322.ProjectionRules, registration =>
            registration.SemanticTypeId == BpmnSemanticTypes.ExclusiveGateway);
        var currentGateway = Assert.Single(m323.ProjectionRules, registration =>
            registration.SemanticTypeId == BpmnSemanticTypes.ExclusiveGateway);
        Assert.Equal(previousGateway.RuleId, currentGateway.RuleId);

        var document = EmptyDocument();
        var processor = Processor();
        Assert.True((await processor.ExecuteAsync(document, Gateway(document))).IsCommitted);
        var execution = new ProjectionEngine(m323.ProjectionRules)
            .Project(document.CaptureSnapshot());
        var graph = Assert.IsType<ProjectedGraph>(execution.Graph);
        var label = Assert.Single(graph.Labels);
        var placement = Assert.IsType<NodeLabelPlacement>(label.NodePlacement);
        Assert.Equal(NodeLabelPlacementKind.OutsideBelow, placement.Kind);
        Assert.Equal(8d, placement.Gap);
        Assert.Equal(160d, placement.MaximumWidth);
        Assert.Equal(NodeLabelInteractionPolicy.MoveAndResize, label.NodeInteractionPolicy);
    }

    [Fact]
    public void M322SafelyOmitsAWhitespaceLegacyGatewayName()
    {
        var registration = Assert.Single(
            BpmnPluginRegistration.M322.ProjectionRules,
            candidate => candidate.SemanticTypeId == BpmnSemanticTypes.ExclusiveGateway);
        var legacyGateway = new SemanticElementSnapshot(
            GatewayId,
            BpmnSemanticTypes.ExclusiveGateway,
            [
                new(BpmnSemanticProperties.Code, PropertyValue.FromText("APPROVED")),
                new(BpmnSemanticProperties.Name, PropertyValue.FromText("   ")),
            ]);
        var visual = new VisualStateSnapshot(
            GatewayVisualId,
            GatewayId,
            new PointD(240d, 80d),
            new SizeD(48d, 48d),
            VisualPlacementMode.Pinned);

        var result = registration.Rule.Project(
            new ElementProjectionRuleInput(
                DocumentId,
                DocumentRevision.Zero,
                ProjectionContext.Empty,
                legacyGateway,
                [visual]),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var contribution = Assert.IsType<ProjectionRuleContribution>(result.Contribution);
        Assert.Single(contribution.Nodes);
        Assert.Empty(contribution.Labels);
    }

    private static Document EmptyDocument() =>
        Assert.IsType<Document>(DocumentFactory.CreateEmpty(DocumentId).Document);

    private static CommandProcessor Processor()
    {
        var registration = BpmnPluginRegistration.M32;
        return new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies);
    }

    private static CreateBpmnExclusiveGatewayCommand Gateway(Document document) =>
        new(
            DocumentId,
            document.Revision,
            GatewayId,
            GatewayVisualId,
            new PointD(240d, 80d),
            new SizeD(48d, 48d),
            "APPROVED",
            "Approved?",
            description: "Route according to the approval decision.");

    private static CreateBpmnTaskCommand Task(
        Document document,
        SemanticElementId id,
        long elementNumber) =>
        new(
            DocumentId,
            document.Revision,
            id,
            new VisualStateId($"{id.Value}:visual"),
            new PointD(0d, 0d),
            new SizeD(120d, 80d),
            $"CODE_{elementNumber}",
            $"Task {elementNumber}",
            elementNumber);

    private static CreateBpmnStartEventCommand Start(
        Document document,
        SemanticElementId id) =>
        new(
            DocumentId,
            document.Revision,
            id,
            new VisualStateId($"{id.Value}:visual"),
            new PointD(0d, 0d),
            new SizeD(36d, 36d));

    private static CreateBpmnEndEventCommand End(
        Document document,
        SemanticElementId id) =>
        new(
            DocumentId,
            document.Revision,
            id,
            new VisualStateId($"{id.Value}:visual"),
            new PointD(0d, 0d),
            new SizeD(36d, 36d));

    private static CreateBpmnSequenceFlowCommand Flow(
        Document document,
        string key,
        SemanticElementId source,
        SemanticElementId target,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId) =>
        new(
            DocumentId,
            document.Revision,
            new SemanticElementId($"bpmn:m32:flow:{key}"),
            new VisualStateId($"bpmn:m32:flow:{key}:visual"),
            source,
            target,
            sourceAnchorId,
            targetAnchorId);

    private static async ValueTask AddAnchorAsync(
        HistoryManager history,
        CommandProcessor processor,
        Document document,
        VisualStateId visualStateId,
        ConnectorAnchorId anchorId,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role,
        int insertionIndex)
    {
        var result = await history.ExecuteAsync(
            processor,
            new AddConnectorAnchorCommand(
                DocumentId,
                document.Revision,
                visualStateId,
                anchorId,
                side,
                role,
                insertionIndex));
        Assert.True(result.IsCommitted);
    }

    private static VisualStateId VisualId(SemanticElementId semanticElementId) =>
        new($"{semanticElementId.Value}:visual");

    private static Inceptus.DocumentEngine.Contracts.Semantics.SemanticElementSnapshot
        GatewayElement(Document document)
    {
        Assert.True(document.SemanticModel.TryGetElement(GatewayId, out var gateway));
        return Assert.IsType<
            Inceptus.DocumentEngine.Contracts.Semantics.SemanticElementSnapshot>(gateway);
    }
}
