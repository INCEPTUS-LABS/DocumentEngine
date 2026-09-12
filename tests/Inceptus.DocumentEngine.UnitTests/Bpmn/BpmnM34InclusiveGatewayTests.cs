using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnM34InclusiveGatewayTests
{
    private static readonly DocumentId DocumentId = new("bpmn:m34:document");
    private static readonly SemanticElementId GatewayId =
        new("bpmn:m34:inclusive-gateway");
    private static readonly VisualStateId GatewayVisualId =
        new("bpmn:m34:inclusive-gateway:visual");

    [Fact]
    public void SemanticTypeAndFactoryAreCanonicalImmutableAndHaveNoElementNumber()
    {
        var gateway = BpmnSemanticFactory.CreateInclusiveGateway(
            GatewayId,
            "OPTIONAL_SERVICES",
            "Optional services",
            "Select one or more optional process branches.");

        Assert.Equal("BPMN.InclusiveGateway", BpmnSemanticTypes.InclusiveGateway.Value);
        Assert.Equal(GatewayId, gateway.Id);
        Assert.Equal(BpmnSemanticTypes.InclusiveGateway, gateway.TypeId);
        Assert.Equal(
            "OPTIONAL_SERVICES",
            gateway.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal(
            "Optional services",
            gateway.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            "Select one or more optional process branches.",
            gateway.Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.NotEqual(
            gateway.Id.Value,
            gateway.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.False(gateway.Properties.ContainsKey(BpmnSemanticProperties.ElementNumber));
        Assert.Equal(3, gateway.Properties.Count);

        var withoutDescription = BpmnSemanticFactory.CreateInclusiveGateway(
            new SemanticElementId("bpmn:m34:inclusive-gateway:no-description"),
            "OPTIONAL_JOIN",
            "Continue after selected services");
        Assert.False(withoutDescription.Properties.ContainsKey(
            BpmnSemanticProperties.Description));
        Assert.Throws<ArgumentException>(() =>
            BpmnSemanticFactory.CreateInclusiveGateway(GatewayId, " ", "Gateway"));
        Assert.Throws<ArgumentException>(() =>
            BpmnSemanticFactory.CreateInclusiveGateway(GatewayId, "CODE", " "));
        Assert.Throws<ArgumentException>(() =>
            BpmnSemanticFactory.CreateInclusiveGateway(GatewayId, "CODE", "Gateway", " "));
    }

    [Fact]
    public async Task RegisteredCreationCommitsOnceAndRoundTripsExactlyThroughHistory()
    {
        var subscriber = new RecordingSubscriber();
        var harness = CreateHarness(subscriber);
        var before = harness.Document.CaptureSnapshot();

        var created = await harness.History.ExecuteAsync(
            harness.Processor,
            Create(harness.Document));
        await CommandProcessor.WaitForEventDispatchIdleAsync(harness.Document);

        Assert.True(created.IsCommitted);
        Assert.Equal(CreateBpmnInclusiveGatewayCommand.KnownTypeId, created.CommandTypeId);
        Assert.Equal(new DocumentRevision(1), harness.Document.Revision);
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);
        Assert.Single(subscriber.Events);
        Assert.True(harness.Document.SemanticModel.TryGetElement(GatewayId, out var gateway));
        Assert.Equal(BpmnSemanticTypes.InclusiveGateway, gateway!.TypeId);
        Assert.False(gateway.Properties.ContainsKey(BpmnSemanticProperties.ElementNumber));
        Assert.True(harness.Document.VisualModel.TryGetVisualState(
            GatewayVisualId,
            out var visual));
        Assert.Equal(GatewayId, visual!.SemanticElementId);
        Assert.Equal(new PointD(240d, 80d), visual.Position);
        Assert.Equal(new SizeD(48d, 48d), visual.Size);
        var committed = harness.Document.CaptureSnapshot();

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(
            before.SemanticModel.Elements.AsEnumerable(),
            harness.Document.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            before.VisualModel.VisualStates.AsEnumerable(),
            harness.Document.VisualModel.VisualStates.AsEnumerable());

        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(
            committed.SemanticModel.Elements.AsEnumerable(),
            harness.Document.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            committed.VisualModel.VisualStates.AsEnumerable(),
            harness.Document.VisualModel.VisualStates.AsEnumerable());
    }

    [Fact]
    public async Task RegisteredEnvelopeRejectsTheWrongRequestShapeWithoutMutation()
    {
        var harness = CreateHarness();
        var before = harness.Document.CaptureSnapshot();

        var result = await harness.Processor.ExecuteAsync(
            harness.Document,
            new WrongShapeCommand(DocumentId, harness.Document.Revision));

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.InvalidCommand);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task CreationAndCodeValidationRejectMalformedOrDuplicateStateAtomically()
    {
        var harness = CreateHarness();
        var malformed = await harness.History.ExecuteAsync(
            harness.Processor,
            new CreateBpmnInclusiveGatewayCommand(
                DocumentId,
                harness.Document.Revision,
                GatewayId,
                GatewayVisualId,
                new PointD(0d, 0d),
                new SizeD(48d, 48d),
                " ",
                "Gateway"));

        Assert.False(malformed.IsCommitted);
        Assert.Contains(malformed.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.InvalidInclusiveGateway);
        Assert.Equal(DocumentRevision.Zero, harness.Document.Revision);
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);

        var malformedName = await harness.History.ExecuteAsync(
            harness.Processor,
            new CreateBpmnInclusiveGatewayCommand(
                DocumentId,
                harness.Document.Revision,
                GatewayId,
                GatewayVisualId,
                new PointD(0d, 0d),
                new SizeD(48d, 48d),
                "CODE",
                " "));
        Assert.False(malformedName.IsCommitted);
        Assert.Contains(malformedName.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.InvalidInclusiveGateway);

        var malformedDescription = await harness.History.ExecuteAsync(
            harness.Processor,
            new CreateBpmnInclusiveGatewayCommand(
                DocumentId,
                harness.Document.Revision,
                GatewayId,
                GatewayVisualId,
                new PointD(0d, 0d),
                new SizeD(48d, 48d),
                "CODE",
                "Gateway",
                description: " "));
        Assert.False(malformedDescription.IsCommitted);
        Assert.Contains(malformedDescription.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.InvalidCommand);
        Assert.Equal(DocumentRevision.Zero, harness.Document.Revision);

        Assert.True((await harness.History.ExecuteAsync(
            harness.Processor,
            Create(harness.Document))).IsCommitted);
        var beforeDuplicate = harness.Document.CaptureSnapshot();
        var duplicateSemantic = await harness.History.ExecuteAsync(
            harness.Processor,
            new CreateBpmnInclusiveGatewayCommand(
                DocumentId,
                harness.Document.Revision,
                GatewayId,
                new VisualStateId("bpmn:m34:duplicate-visual"),
                new PointD(0d, 0d),
                new SizeD(48d, 48d),
                "OTHER",
                "Other"));
        Assert.False(duplicateSemantic.IsCommitted);
        Assert.Contains(duplicateSemantic.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.DuplicateSemanticId);
        Assert.Equal(beforeDuplicate, harness.Document.CaptureSnapshot());

        var duplicateVisual = await harness.History.ExecuteAsync(
            harness.Processor,
            new CreateBpmnInclusiveGatewayCommand(
                DocumentId,
                harness.Document.Revision,
                new SemanticElementId("bpmn:m34:duplicate-semantic"),
                GatewayVisualId,
                new PointD(0d, 0d),
                new SizeD(48d, 48d),
                "OTHER",
                "Other"));
        Assert.False(duplicateVisual.IsCommitted);
        Assert.Contains(duplicateVisual.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.DuplicateVisualId);
        Assert.Equal(beforeDuplicate, harness.Document.CaptureSnapshot());

        var invalidCode = await harness.History.ExecuteAsync(
            harness.Processor,
            new UpdateSemanticElementPropertyCommand(
                DocumentId,
                harness.Document.Revision,
                GatewayId,
                BpmnSemanticProperties.Code,
                PropertyValue.FromText(" ")));
        Assert.False(invalidCode.IsCommitted);
        Assert.Contains(invalidCode.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.InvalidInclusiveGateway);
        Assert.Equal(beforeDuplicate, harness.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task GenericSemanticCommandsEditCodeNameAndDescription()
    {
        var harness = CreateHarness();
        Assert.True((await harness.History.ExecuteAsync(
            harness.Processor,
            Create(harness.Document))).IsCommitted);

        Assert.True((await harness.History.ExecuteAsync(
            harness.Processor,
            new UpdateSemanticElementPropertyCommand(
                DocumentId,
                harness.Document.Revision,
                GatewayId,
                BpmnSemanticProperties.Code,
                PropertyValue.FromText("OPTIONAL_JOIN")))).IsCommitted);
        Assert.True((await harness.History.ExecuteAsync(
            harness.Processor,
            new UpdateSemanticElementNameCommand(
                DocumentId,
                harness.Document.Revision,
                GatewayId,
                BpmnSemanticProperties.Name,
                "Continue after selected services"))).IsCommitted);
        Assert.True((await harness.History.ExecuteAsync(
            harness.Processor,
            new UpdateSemanticElementPropertyCommand(
                DocumentId,
                harness.Document.Revision,
                GatewayId,
                BpmnSemanticProperties.Description,
                PropertyValue.FromText("Merge the selected optional branches.")))).IsCommitted);

        Assert.True(harness.Document.SemanticModel.TryGetElement(GatewayId, out var gateway));
        Assert.Equal(
            "OPTIONAL_JOIN",
            gateway!.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal(
            "Continue after selected services",
            gateway.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            "Merge the selected optional branches.",
            gateway.Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.False(gateway.Properties.ContainsKey(BpmnSemanticProperties.ElementNumber));
    }

    [Fact]
    public async Task ProjectionProducesOneTraceableNodeAndOneEditableOutsideBelowName()
    {
        var harness = CreateHarness();
        Assert.True((await harness.Processor.ExecuteAsync(
            harness.Document,
            Create(harness.Document))).IsCommitted);

        var execution = new ProjectionEngine(BpmnPluginRegistration.M34.ProjectionRules)
            .Project(harness.Document.CaptureSnapshot());

        Assert.True(execution.IsSuccessful);
        var graph = Assert.IsType<ProjectedGraph>(execution.Graph);
        var node = Assert.Single(graph.Nodes);
        var label = Assert.Single(graph.Labels);
        Assert.Equal(GatewayId, node.Source.SemanticElementId);
        Assert.Equal(BpmnSemanticTypes.InclusiveGateway, node.Source.SemanticTypeId);
        Assert.Equal(GatewayVisualId, node.Source.VisualStateId);
        Assert.Equal(
            BpmnSemanticTypes.InclusiveGateway.Value,
            node.ProjectedProperties["BPMN.ProjectedSemanticType"].TextValue);
        Assert.Equal(node.Id, label.OwnerId);
        Assert.Equal("Optional services", label.Text);
        Assert.Equal(GatewayId, label.Source.SemanticElementId);
        Assert.Equal(GatewayVisualId, label.Source.VisualStateId);
        var placement = Assert.IsType<NodeLabelPlacement>(label.NodePlacement);
        Assert.Equal(NodeLabelPlacementKind.OutsideBelow, placement.Kind);
        Assert.Equal(8d, placement.Gap);
        Assert.Equal(160d, placement.MaximumWidth);
        Assert.Equal(
            NodeLabelInteractionPolicy.MoveAndResize,
            label.NodeInteractionPolicy);
    }

    [Fact]
    public void M34AppendsOnlyInclusiveCapabilitiesAndPreservesM33Surface()
    {
        var previous = BpmnPluginRegistration.M33;
        var current = BpmnPluginRegistration.M34;

        Assert.Equal(
            previous.CommandHandlers.AsEnumerable(),
            current.CommandHandlers.Take(previous.CommandHandlers.Length));
        Assert.Equal(
            previous.CommandValidators.AsEnumerable(),
            current.CommandValidators.Take(previous.CommandValidators.Length));
        Assert.Equal(
            previous.HistoryPolicies.AsEnumerable(),
            current.HistoryPolicies.Take(previous.HistoryPolicies.Length));
        Assert.Equal(
            previous.ProjectionRules.AsEnumerable(),
            current.ProjectionRules.Take(previous.ProjectionRules.Length));
        Assert.Equal(previous.LayoutAlgorithms.AsEnumerable(), current.LayoutAlgorithms);
        Assert.Equal(previous.RoutingAlgorithms.AsEnumerable(), current.RoutingAlgorithms);
        Assert.Equal(previous.SceneContributors.AsEnumerable(), current.SceneContributors);
        Assert.Equal(
            previous.PropertiesSchemas.AsEnumerable(),
            current.PropertiesSchemas.Take(previous.PropertiesSchemas.Length));
        Assert.Equal(
            previous.ConnectorAnchorPolicies.AsEnumerable(),
            current.ConnectorAnchorPolicies.Take(previous.ConnectorAnchorPolicies.Length));
        Assert.Equal(previous.CommandHandlers.Length + 1, current.CommandHandlers.Length);
        Assert.Equal(previous.CommandValidators.Length + 2, current.CommandValidators.Length);
        Assert.Equal(previous.HistoryPolicies.Length + 1, current.HistoryPolicies.Length);
        Assert.Equal(previous.ProjectionRules.Length + 1, current.ProjectionRules.Length);
        Assert.Equal(previous.PropertiesSchemas.Length + 1, current.PropertiesSchemas.Length);
        Assert.Equal(
            previous.ConnectorAnchorPolicies.Length + 1,
            current.ConnectorAnchorPolicies.Length);

        Assert.Contains(current.CommandHandlers, registration =>
            registration.TypeId == CreateBpmnInclusiveGatewayCommand.KnownTypeId);
        Assert.Contains(current.CommandValidators, registration =>
            registration.TypeId == CreateBpmnInclusiveGatewayCommand.KnownTypeId &&
            registration.ValidatorId.Value == "bpmn:validator/create-inclusive-gateway");
        Assert.Contains(current.CommandValidators, registration =>
            registration.TypeId == UpdateSemanticElementPropertyCommand.KnownTypeId &&
            registration.ValidatorId.Value ==
                "bpmn:validator/inclusive-gateway-code-update");
        Assert.Contains(current.HistoryPolicies, registration =>
            registration.TypeId == CreateBpmnInclusiveGatewayCommand.KnownTypeId);
        Assert.Contains(current.ProjectionRules, registration =>
            registration.SourceKind == ProjectionSourceKind.SemanticElement &&
            registration.SemanticTypeId == BpmnSemanticTypes.InclusiveGateway &&
            registration.RuleId.Value == "bpmn:projection/inclusive-gateway");
        Assert.Contains(current.ConnectorAnchorPolicies, registration =>
            registration.ElementTypeId == BpmnSemanticTypes.InclusiveGateway &&
            AllEdgesAreDynamicSourceOrTarget(registration.Policy));

        Assert.DoesNotContain(previous.CommandHandlers, registration =>
            registration.TypeId == CreateBpmnInclusiveGatewayCommand.KnownTypeId);
        Assert.DoesNotContain(previous.ProjectionRules, registration =>
            registration.SemanticTypeId == BpmnSemanticTypes.InclusiveGateway);
        Assert.DoesNotContain(previous.PropertiesSchemas, schema =>
            schema.SemanticTypeId == BpmnSemanticTypes.InclusiveGateway);
        Assert.DoesNotContain(previous.ConnectorAnchorPolicies, registration =>
            registration.ElementTypeId == BpmnSemanticTypes.InclusiveGateway);
        Assert.Equal(
            ["Start Event", "Task", "Exclusive Gateway", "Parallel Gateway", "End Event"],
            Assert.Single(previous.ToolboxContributions).Items
                .Select(static item => item.DisplayName));
    }

    [Fact]
    public void M34PropertiesAndToolboxAreExactAndDataOnly()
    {
        var registration = BpmnPluginRegistration.M34;
        var schema = Assert.Single(registration.PropertiesSchemas, candidate =>
            candidate.SemanticTypeId == BpmnSemanticTypes.InclusiveGateway);

        Assert.Equal(
            ["code", "name", "description"],
            schema.Fields.Select(static field => field.FieldId.Value));
        Assert.Equal(
            ["Code", "Name", "Description"],
            schema.Fields.Select(static field => field.DisplayName));
        Assert.Equal(
            [
                BpmnSemanticProperties.Code,
                BpmnSemanticProperties.Name,
                BpmnSemanticProperties.Description,
            ],
            schema.Fields.Select(static field => field.SemanticPropertyKey));
        Assert.Equal(
            [
                ElementPropertyEditorKind.SingleLineText,
                ElementPropertyEditorKind.SingleLineText,
                ElementPropertyEditorKind.MultilineText,
            ],
            schema.Fields.Select(static field => field.EditorKind));
        Assert.Equal(
            [
                SemanticPropertyMutationKind.Property,
                SemanticPropertyMutationKind.Name,
                SemanticPropertyMutationKind.Property,
            ],
            schema.Fields.Select(static field => field.MutationKind));
        Assert.DoesNotContain(schema.Fields, field =>
            StringComparer.Ordinal.Equals(
                field.SemanticPropertyKey,
                BpmnSemanticProperties.ElementNumber));
        Assert.All(schema.Fields, static field => Assert.True(field.IsEditable));

        var toolbox = Assert.Single(registration.ToolboxContributions);
        Assert.Equal(
            [
                "Start Event",
                "Task",
                "Exclusive Gateway",
                "Parallel Gateway",
                "Inclusive Gateway",
                "End Event",
            ],
            toolbox.Items.Select(static item => item.DisplayName));
        Assert.Equal(
            [
                "bpmn:toolbox:start-event",
                "bpmn:toolbox:task",
                "bpmn:toolbox:exclusive-gateway",
                "bpmn:toolbox:parallel-gateway",
                "bpmn:toolbox:inclusive-gateway",
                "bpmn:toolbox:end-event",
            ],
            toolbox.Items.Select(static item => item.ItemId.Value));
        Assert.Equal(BpmnSemanticTypes.InclusiveGateway, toolbox.Items[4].ElementTypeId);
        Assert.Equal("bpmn:inclusive-gateway", toolbox.Items[4].Icon.IconKey);
        Assert.Equal("◇O", toolbox.Items[4].Icon.FallbackGlyph);
        Assert.DoesNotContain(toolbox.Items, static item =>
            item.ElementTypeId == BpmnSemanticTypes.SequenceFlow);
    }

    private static Harness CreateHarness(RecordingSubscriber? subscriber = null)
    {
        var registration = BpmnPluginRegistration.M34;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(
            DocumentId,
            connectorAnchorPolicyProvider: provider).Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            subscribers: subscriber is null ? null : [subscriber],
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: provider);
        return new Harness(document, processor, new HistoryManager(document));
    }

    private static CreateBpmnInclusiveGatewayCommand Create(Document document) =>
        new(
            DocumentId,
            document.Revision,
            GatewayId,
            GatewayVisualId,
            new PointD(240d, 80d),
            new SizeD(48d, 48d),
            "OPTIONAL_SERVICES",
            "Optional services",
            description: "Select one or more optional process branches.");

    private static bool AllEdgesAreDynamicSourceOrTarget(
        ElementConnectorAnchorPolicy policy) =>
        Enum.GetValues<ConnectorAnchorSide>().All(side =>
            policy.ForSide(side).Mode == ConnectorAnchorPolicyMode.DynamicUnlimited &&
            policy.ForSide(side).AllowedRoles ==
                ConnectorAnchorRoleCapability.SourceOrTarget);

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History);

    private sealed record WrongShapeCommand(
        DocumentId TargetDocumentId,
        DocumentRevision ExpectedRevision) : ICommand
    {
        public CommandTypeId TypeId => CreateBpmnInclusiveGatewayCommand.KnownTypeId;

        public CommandCategory Category => CommandCategory.Document;

        public AuthoritativeDocumentComponent AffectedComponents =>
            AuthoritativeDocumentComponent.SemanticModel |
            AuthoritativeDocumentComponent.VisualModel;
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }
}
