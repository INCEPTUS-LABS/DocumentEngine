using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Placement;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
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

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN1ToolboxPlacementTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n1:document");
    private static readonly PointD ClickPoint = new(500d, 300d);

    [Fact]
    public void N1AddsOnlySixNodePlacementRegistrationsToTheM34Surface()
    {
        var previous = BpmnPluginRegistration.M34;
        var current = BpmnPluginRegistration.N1;

        Assert.Equal(previous.CommandHandlers, current.CommandHandlers);
        Assert.Equal(previous.CommandValidators, current.CommandValidators);
        Assert.Equal(previous.HistoryPolicies, current.HistoryPolicies);
        Assert.Equal(previous.ProjectionRules, current.ProjectionRules);
        Assert.Equal(previous.LayoutAlgorithms, current.LayoutAlgorithms);
        Assert.Equal(previous.RoutingAlgorithms, current.RoutingAlgorithms);
        Assert.Equal(previous.SceneContributors, current.SceneContributors);
        Assert.Equal(previous.ToolboxContributions, current.ToolboxContributions);
        Assert.Equal(previous.PropertiesSchemas, current.PropertiesSchemas);
        Assert.Equal(previous.ConnectorAnchorPolicies, current.ConnectorAnchorPolicies);
        Assert.Empty(previous.ToolboxPlacementRegistrations);
        Assert.All(
            new[]
            {
                BpmnPluginRegistration.M1,
                BpmnPluginRegistration.M2,
                BpmnPluginRegistration.M3,
                BpmnPluginRegistration.M31,
                BpmnPluginRegistration.M32,
                BpmnPluginRegistration.M321,
                BpmnPluginRegistration.M322,
                BpmnPluginRegistration.M323,
                BpmnPluginRegistration.M33,
            },
            static registration => Assert.Empty(
                registration.ToolboxPlacementRegistrations));

        Assert.Equal(
            [
                "bpmn:toolbox:start-event",
                "bpmn:toolbox:task",
                "bpmn:toolbox:exclusive-gateway",
                "bpmn:toolbox:parallel-gateway",
                "bpmn:toolbox:inclusive-gateway",
                "bpmn:toolbox:end-event",
            ],
            current.ToolboxPlacementRegistrations
                .Select(static registration => registration.ToolboxItemId.Value));
        Assert.DoesNotContain(
            current.ToolboxPlacementRegistrations,
            static registration => registration.ToolboxItemId.Value.Contains(
                "sequence-flow",
                StringComparison.Ordinal));

        var toolboxCatalog = new ToolboxCatalog(current.ToolboxContributions);
        var placementCatalog = new ToolboxPlacementCatalog(
            current.ToolboxPlacementRegistrations,
            toolboxCatalog);
        Assert.Equal(
            toolboxCatalog.Items
                .Select(static item => item.ItemId)
                .OrderBy(static itemId => itemId.Value, StringComparer.Ordinal),
            placementCatalog.Registrations.Select(static item => item.ToolboxItemId));
    }

    [Fact]
    public async Task SixFactoriesCreateExistingCommandsWithCanonicalCenteredPinnedDefaults()
    {
        PlacementExpectation[] expectations =
        [
            new(
                "bpmn:toolbox:start-event",
                typeof(CreateBpmnStartEventCommand),
                new SizeD(36d, 36d)),
            new(
                "bpmn:toolbox:task",
                typeof(CreateBpmnTaskCommand),
                new SizeD(120d, 80d)),
            new(
                "bpmn:toolbox:exclusive-gateway",
                typeof(CreateBpmnExclusiveGatewayCommand),
                new SizeD(48d, 48d)),
            new(
                "bpmn:toolbox:parallel-gateway",
                typeof(CreateBpmnParallelGatewayCommand),
                new SizeD(48d, 48d)),
            new(
                "bpmn:toolbox:inclusive-gateway",
                typeof(CreateBpmnInclusiveGatewayCommand),
                new SizeD(48d, 48d)),
            new(
                "bpmn:toolbox:end-event",
                typeof(CreateBpmnEndEventCommand),
                new SizeD(36d, 36d)),
        ];

        foreach (var expectation in expectations)
        {
            var harness = CreateHarness();
            var snapshot = harness.Document.CaptureSnapshot();
            var semanticId = new SemanticElementId(
                $"bpmn:n1:created:{expectation.ItemId}");
            var visualId = new VisualStateId(
                $"bpmn:n1:visual:{expectation.ItemId}");
            var identities = new RecordingIdentityProvider(
                new DocumentCreationIdentity(semanticId, visualId));
            var registration = Registration(expectation.ItemId);

            var result = registration.CommandFactory.CreatePlan(new ToolboxPlacementRequest(
                registration.ToolboxItemId,
                snapshot,
                snapshot.Revision,
                ClickPoint,
                identities));

            Assert.True(result.Succeeded);
            Assert.Empty(result.Diagnostics);
            var plan = Assert.IsType<ToolboxPlacementPlan>(result.Plan);
            Assert.Equal(expectation.CommandType, plan.Command.GetType());
            Assert.Equal(semanticId, plan.CreatedSemanticElementId);
            Assert.Equal(visualId, plan.CreatedVisualStateId);
            Assert.Equal(1, identities.CallCount);

            var command = Assert.IsAssignableFrom<BpmnElementCreationCommand>(plan.Command);
            Assert.Equal(DocumentId, command.TargetDocumentId);
            Assert.Equal(snapshot.Revision, command.ExpectedRevision);
            Assert.Equal(semanticId, command.ElementId);
            Assert.Equal(visualId, command.VisualStateId);
            Assert.Equal(expectation.Size, command.Size);
            Assert.Equal(
                new PointD(
                    ClickPoint.X - (expectation.Size.Width / 2d),
                    ClickPoint.Y - (expectation.Size.Height / 2d)),
                command.Position);
            Assert.Equal(VisualPlacementMode.Pinned, command.PlacementMode);
            AssertSemanticDefaults(command);

            var execution = await harness.Processor.ExecuteAsync(
                harness.Document,
                command);
            Assert.True(execution.IsCommitted);
            var committed = harness.Document.CaptureSnapshot();
            Assert.Single(committed.SemanticModel.Elements);
            Assert.Empty(committed.SemanticModel.Relationships);
            var visual = Assert.Single(committed.VisualModel.VisualStates);
            Assert.Equal(visualId, visual.Id);
            Assert.Equal(semanticId, visual.SemanticElementId);
            Assert.Equal(command.Position, visual.Position);
            Assert.Equal(command.Size, visual.Size);
            Assert.Equal(VisualPlacementMode.Pinned, visual.PlacementMode);
            Assert.Empty(visual.ConnectorAnchors);
            Assert.Empty(visual.Route);
            Assert.Null(visual.SourceAnchorId);
            Assert.Null(visual.TargetAnchorId);
        }
    }

    [Fact]
    public async Task TaskFactoryUsesTypedMaximumPlusOneAndExecutionAddsOnlyOneNode()
    {
        var harness = CreateHarness();
        await SeedTaskAsync(harness, "existing-low", 7L);
        await SeedTaskAsync(harness, "existing-high", 41L);
        var snapshot = harness.Document.CaptureSnapshot();
        var identity = new DocumentCreationIdentity(
            new SemanticElementId("bpmn:n1:created:task"),
            new VisualStateId("bpmn:n1:visual:task"));
        var identities = new RecordingIdentityProvider(identity);
        var registration = Registration("bpmn:toolbox:task");

        var result = registration.CommandFactory.CreatePlan(new ToolboxPlacementRequest(
            registration.ToolboxItemId,
            snapshot,
            snapshot.Revision,
            ClickPoint,
            identities));

        var plan = Assert.IsType<ToolboxPlacementPlan>(result.Plan);
        var command = Assert.IsType<CreateBpmnTaskCommand>(plan.Command);
        Assert.Equal(42L, command.ElementNumber);
        Assert.Equal("TASK_42", command.Code);
        Assert.Equal("Task 42", command.Name);
        Assert.Equal("Task 42 created from the Toolbox.", command.Description);
        Assert.Equal(new PointD(440d, 260d), command.Position);
        Assert.Equal(new SizeD(120d, 80d), command.Size);

        var beforeRevision = harness.Document.Revision;
        var execution = await harness.Processor.ExecuteAsync(harness.Document, command);
        Assert.True(execution.IsCommitted);
        Assert.Equal(beforeRevision.Increment(), harness.Document.Revision);
        var committed = harness.Document.CaptureSnapshot();
        Assert.Equal(3, committed.SemanticModel.ElementCount);
        Assert.Empty(committed.SemanticModel.Relationships);
        Assert.Equal(3, committed.VisualModel.Count);
        Assert.True(committed.SemanticModel.TryGetElement(identity.SemanticElementId, out var task));
        Assert.NotNull(task);
        Assert.Equal(BpmnSemanticTypes.Task, task.TypeId);
        Assert.Equal(PropertyValueKind.Text,
            task.Properties[BpmnSemanticProperties.Code].Kind);
        Assert.Equal("TASK_42", task.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal(PropertyValueKind.Integer,
            task.Properties[BpmnSemanticProperties.ElementNumber].Kind);
        Assert.Equal(42L,
            task.Properties[BpmnSemanticProperties.ElementNumber].IntegerValue);
        Assert.Equal(
            "Task 42 created from the Toolbox.",
            task.Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.True(committed.VisualModel.TryGetVisualState(identity.VisualStateId, out var visual));
        Assert.NotNull(visual);
        Assert.Equal(new PointD(440d, 260d), visual.Position);
        Assert.Equal(new SizeD(120d, 80d), visual.Size);
        Assert.Equal(VisualPlacementMode.Pinned, visual.PlacementMode);
        Assert.Empty(visual.ConnectorAnchors);
        Assert.Empty(visual.Route);
        Assert.Null(visual.SourceAnchorId);
        Assert.Null(visual.TargetAnchorId);
    }

    [Fact]
    public async Task TaskElementNumberOverflowFailsBeforeAllocatingTechnicalIdentity()
    {
        var harness = CreateHarness();
        await SeedTaskAsync(harness, "maximum", long.MaxValue);
        var snapshot = harness.Document.CaptureSnapshot();
        var identities = new RecordingIdentityProvider(
            new DocumentCreationIdentity(
                new SemanticElementId("bpmn:n1:unused:task"),
                new VisualStateId("bpmn:n1:unused:visual")));
        var registration = Registration("bpmn:toolbox:task");

        var result = registration.CommandFactory.CreatePlan(new ToolboxPlacementRequest(
            registration.ToolboxItemId,
            snapshot,
            snapshot.Revision,
            ClickPoint,
            identities));

        Assert.False(result.Succeeded);
        Assert.Null(result.Plan);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            BpmnToolboxPlacementDiagnosticCodes.TaskElementNumberExhausted,
            diagnostic.Code);
        Assert.Equal(0, identities.CallCount);
        Assert.Equal(snapshot, harness.Document.CaptureSnapshot());
    }

    [Fact]
    public void TaskNumberingIgnoresElementNumberDataOwnedByANonTaskElement()
    {
        var unrelatedId = new SemanticElementId("bpmn:n1:unrelated:start");
        var revision = DocumentRevision.Zero;
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                elements:
                [
                    new SemanticElementSnapshot(
                        unrelatedId,
                        BpmnSemanticTypes.StartEvent,
                        [
                            new KeyValuePair<string, PropertyValue>(
                                BpmnSemanticProperties.ElementNumber,
                                PropertyValue.FromInteger(long.MaxValue)),
                        ]),
                ]),
            new VisualModelSnapshot(
                DocumentId,
                revision,
                [
                    new VisualStateSnapshot(
                        new VisualStateId("bpmn:n1:unrelated:start-visual"),
                        unrelatedId,
                        new PointD(0d, 0d),
                        new SizeD(36d, 36d),
                        VisualPlacementMode.Pinned),
                ]),
            new DocumentMetadataSnapshot(DocumentId, revision));
        var identities = new RecordingIdentityProvider(
            new DocumentCreationIdentity(
                new SemanticElementId("bpmn:n1:created:task-after-unrelated"),
                new VisualStateId("bpmn:n1:visual:task-after-unrelated")));
        var registration = Registration("bpmn:toolbox:task");

        var result = registration.CommandFactory.CreatePlan(new ToolboxPlacementRequest(
            registration.ToolboxItemId,
            snapshot,
            revision,
            ClickPoint,
            identities));

        var command = Assert.IsType<CreateBpmnTaskCommand>(
            Assert.IsType<ToolboxPlacementPlan>(result.Plan).Command);
        Assert.Equal(1L, command.ElementNumber);
        Assert.Equal("TASK_1", command.Code);
        Assert.Equal("Task 1", command.Name);
        Assert.Equal(1, identities.CallCount);
    }

    [Fact]
    public async Task TwoExplicitTaskPlacementsUseFreshInjectedIdsAndSequentialDefaults()
    {
        var harness = CreateHarness();
        var registration = Registration("bpmn:toolbox:task");
        var firstIdentity = new DocumentCreationIdentity(
            new SemanticElementId("bpmn:n1:created:first-task"),
            new VisualStateId("bpmn:n1:visual:first-task"));
        var first = CreatePlan(
            registration,
            harness.Document.CaptureSnapshot(),
            new PointD(100d, 100d),
            new RecordingIdentityProvider(firstIdentity));
        var firstCommand = Assert.IsType<CreateBpmnTaskCommand>(first.Command);
        Assert.Equal(1L, firstCommand.ElementNumber);
        Assert.True((await harness.Processor.ExecuteAsync(
            harness.Document,
            firstCommand)).IsCommitted);

        var secondIdentity = new DocumentCreationIdentity(
            new SemanticElementId("bpmn:n1:created:second-task"),
            new VisualStateId("bpmn:n1:visual:second-task"));
        var second = CreatePlan(
            registration,
            harness.Document.CaptureSnapshot(),
            new PointD(400d, 250d),
            new RecordingIdentityProvider(secondIdentity));
        var secondCommand = Assert.IsType<CreateBpmnTaskCommand>(second.Command);
        Assert.Equal(2L, secondCommand.ElementNumber);
        Assert.True((await harness.Processor.ExecuteAsync(
            harness.Document,
            secondCommand)).IsCommitted);

        var committed = harness.Document.CaptureSnapshot();
        Assert.Equal(2, committed.SemanticModel.ElementCount);
        Assert.Equal(2, committed.VisualModel.Count);
        Assert.Empty(committed.SemanticModel.Relationships);
        Assert.NotEqual(first.CreatedSemanticElementId, second.CreatedSemanticElementId);
        Assert.NotEqual(first.CreatedVisualStateId, second.CreatedVisualStateId);
        Assert.Equal(new PointD(40d, 60d), firstCommand.Position);
        Assert.Equal(new PointD(340d, 210d), secondCommand.Position);
        Assert.All(
            committed.VisualModel.VisualStates,
            static visual =>
            {
                Assert.Equal(VisualPlacementMode.Pinned, visual.PlacementMode);
                Assert.Empty(visual.ConnectorAnchors);
            });
    }

    [Fact]
    public void FactoryRejectsARequestForAnotherToolWithoutAllocatingIdentity()
    {
        var harness = CreateHarness();
        var snapshot = harness.Document.CaptureSnapshot();
        var identities = new RecordingIdentityProvider(
            new DocumentCreationIdentity(
                new SemanticElementId("bpmn:n1:unused:mismatch"),
                new VisualStateId("bpmn:n1:unused:mismatch-visual")));
        var task = Registration("bpmn:toolbox:task");

        var result = task.CommandFactory.CreatePlan(new ToolboxPlacementRequest(
            new ToolboxItemId("bpmn:toolbox:start-event"),
            snapshot,
            snapshot.Revision,
            ClickPoint,
            identities));

        Assert.False(result.Succeeded);
        Assert.Equal(
            BpmnToolboxPlacementDiagnosticCodes.ToolboxItemMismatch,
            Assert.Single(result.Diagnostics).Code);
        Assert.Equal(0, identities.CallCount);
    }

    [Theory]
    [InlineData(18d, 18d, true)]
    [InlineData(17d, 18d, false)]
    [InlineData(18d, 17d, false)]
    [InlineData(-1d, 18d, false)]
    public void PlacementRequiresTheCenteredNodeBoundsToStayInsideTheDocument(
        double centerX,
        double centerY,
        bool expectedSuccess)
    {
        var registration = Registration("bpmn:toolbox:start-event");
        var snapshot = CreateHarness().Document.CaptureSnapshot();
        var identities = new RecordingIdentityProvider(
            new DocumentCreationIdentity(
                new SemanticElementId("bpmn:n1:boundary:event"),
                new VisualStateId("bpmn:n1:boundary:event-visual")));

        var result = registration.CommandFactory.CreatePlan(new ToolboxPlacementRequest(
            registration.ToolboxItemId,
            snapshot,
            snapshot.Revision,
            new PointD(centerX, centerY),
            identities));

        Assert.Equal(expectedSuccess, result.Succeeded);
        Assert.Equal(expectedSuccess ? 1 : 0, identities.CallCount);
        if (expectedSuccess)
        {
            var command = Assert.IsType<CreateBpmnStartEventCommand>(
                Assert.IsType<ToolboxPlacementPlan>(result.Plan).Command);
            Assert.Equal(new PointD(0d, 0d), command.Position);
            return;
        }

        Assert.Null(result.Plan);
        Assert.Equal(
            CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
            Assert.Single(result.Diagnostics).Code);
    }

    private static void AssertSemanticDefaults(BpmnElementCreationCommand command)
    {
        switch (command)
        {
            case CreateBpmnStartEventCommand start:
                Assert.Null(start.Name);
                Assert.Null(start.Description);
                break;
            case CreateBpmnTaskCommand task:
                Assert.Equal("TASK_1", task.Code);
                Assert.Equal("Task 1", task.Name);
                Assert.Equal(1L, task.ElementNumber);
                Assert.Equal("Task 1 created from the Toolbox.", task.Description);
                break;
            case CreateBpmnExclusiveGatewayCommand gateway:
                Assert.Equal("EXCLUSIVE_GATEWAY", gateway.Code);
                Assert.Equal("Exclusive Gateway", gateway.Name);
                Assert.Equal(
                    "Exclusive Gateway created from the Toolbox.",
                    gateway.Description);
                break;
            case CreateBpmnParallelGatewayCommand gateway:
                Assert.Equal("PARALLEL_GATEWAY", gateway.Code);
                Assert.Equal("Parallel Gateway", gateway.Name);
                Assert.Equal(
                    "Parallel Gateway created from the Toolbox.",
                    gateway.Description);
                break;
            case CreateBpmnInclusiveGatewayCommand gateway:
                Assert.Equal("INCLUSIVE_GATEWAY", gateway.Code);
                Assert.Equal("Inclusive Gateway", gateway.Name);
                Assert.Equal(
                    "Inclusive Gateway created from the Toolbox.",
                    gateway.Description);
                break;
            case CreateBpmnEndEventCommand end:
                Assert.Null(end.Name);
                Assert.Null(end.Description);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unexpected BPMN placement command '{command.GetType()}'.");
        }
    }

    private static ToolboxPlacementRegistration Registration(string itemId) =>
        Assert.Single(BpmnPluginRegistration.N1.ToolboxPlacementRegistrations, registration =>
            registration.ToolboxItemId == new ToolboxItemId(itemId));

    private static ToolboxPlacementPlan CreatePlan(
        ToolboxPlacementRegistration registration,
        DocumentSnapshot snapshot,
        PointD documentPoint,
        IDocumentCreationIdentityProvider identityProvider) =>
        Assert.IsType<ToolboxPlacementPlan>(
            registration.CommandFactory.CreatePlan(new ToolboxPlacementRequest(
                registration.ToolboxItemId,
                snapshot,
                snapshot.Revision,
                documentPoint,
                identityProvider)).Plan);

    private static Harness CreateHarness()
    {
        var registration = BpmnPluginRegistration.N1;
        var connectorAnchorPolicyProvider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(
            DocumentId,
            connectorAnchorPolicyProvider: connectorAnchorPolicyProvider).Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: connectorAnchorPolicyProvider);
        return new Harness(document, processor);
    }

    private static async Task SeedTaskAsync(
        Harness harness,
        string key,
        long elementNumber)
    {
        var result = await harness.Processor.ExecuteAsync(
            harness.Document,
            new CreateBpmnTaskCommand(
                DocumentId,
                harness.Document.Revision,
                new SemanticElementId($"bpmn:n1:seed:{key}"),
                new VisualStateId($"bpmn:n1:seed-visual:{key}"),
                new PointD(0d, 0d),
                new SizeD(120d, 80d),
                $"SEED_{key}",
                $"Seed {key}",
                elementNumber,
                VisualPlacementMode.Pinned));
        Assert.True(result.IsCommitted);
    }

    private sealed class RecordingIdentityProvider : IDocumentCreationIdentityProvider
    {
        private readonly Queue<DocumentCreationIdentity> _identities;

        internal RecordingIdentityProvider(params DocumentCreationIdentity[] identities)
        {
            _identities = new Queue<DocumentCreationIdentity>(identities);
        }

        internal int CallCount { get; private set; }

        public DocumentCreationIdentity CreateIdentity()
        {
            CallCount++;
            return _identities.Dequeue();
        }
    }

    private sealed record Harness(Document Document, CommandProcessor Processor);

    private sealed record PlacementExpectation(
        string ItemId,
        Type CommandType,
        SizeD Size);
}
