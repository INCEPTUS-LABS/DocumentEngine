using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnM321ExplicitAnchorBindingTests
{
    private static readonly DocumentId DocumentId = new("bpmn:m321:binding-document");
    private static readonly SemanticElementId TaskAId = new("bpmn:m321:task-a");
    private static readonly SemanticElementId TaskBId = new("bpmn:m321:task-b");
    private static readonly SemanticElementId TaskCId = new("bpmn:m321:task-c");
    private static readonly VisualStateId TaskAVisualId = new("bpmn:m321:task-a:visual");
    private static readonly VisualStateId TaskBVisualId = new("bpmn:m321:task-b:visual");
    private static readonly VisualStateId TaskCVisualId = new("bpmn:m321:task-c:visual");

    [Fact]
    public void M321ComposesM32AndContributesExactRoleSensitivePolicies()
    {
        var m32 = BpmnPluginRegistration.M32;
        var m321 = BpmnPluginRegistration.M321;

        Assert.Equal(m32.CommandHandlers.AsEnumerable(), m321.CommandHandlers.AsEnumerable());
        Assert.Equal(m32.CommandValidators.AsEnumerable(), m321.CommandValidators.AsEnumerable());
        Assert.Equal(m32.HistoryPolicies.AsEnumerable(), m321.HistoryPolicies.AsEnumerable());
        Assert.Equal(m32.ProjectionRules.AsEnumerable(), m321.ProjectionRules.AsEnumerable());
        Assert.Equal(m32.LayoutAlgorithms.AsEnumerable(), m321.LayoutAlgorithms.AsEnumerable());
        Assert.Equal(m32.RoutingAlgorithms.AsEnumerable(), m321.RoutingAlgorithms.AsEnumerable());
        Assert.Equal(m32.SceneContributors.AsEnumerable(), m321.SceneContributors.AsEnumerable());
        Assert.Equal(m32.ToolboxContributions.AsEnumerable(), m321.ToolboxContributions.AsEnumerable());
        Assert.Equal(m32.PropertiesSchemas.AsEnumerable(), m321.PropertiesSchemas.AsEnumerable());
        Assert.Empty(m32.ConnectorAnchorPolicies);
        Assert.Equal(4, m321.ConnectorAnchorPolicies.Length);

        var provider = new ElementConnectorAnchorPolicyRegistry(m321.ConnectorAnchorPolicies);
        AssertPolicy(
            provider.Resolve(BpmnSemanticTypes.StartEvent),
            ConnectorAnchorRoleCapability.Source);
        AssertPolicy(
            provider.Resolve(BpmnSemanticTypes.EndEvent),
            ConnectorAnchorRoleCapability.Target);
        AssertPolicy(
            provider.Resolve(BpmnSemanticTypes.Task),
            ConnectorAnchorRoleCapability.SourceOrTarget);
        AssertPolicy(
            provider.Resolve(BpmnSemanticTypes.ExclusiveGateway),
            ConnectorAnchorRoleCapability.SourceOrTarget);
    }

    [Fact]
    public void SequenceFlowCommandRequiresAndRetainsBothTypedAnchorIdentities()
    {
        var sourceAnchorId = new ConnectorAnchorId("bpmn:m321:source-anchor");
        var targetAnchorId = new ConnectorAnchorId("bpmn:m321:target-anchor");
        var command = Flow(DocumentRevision.Zero, sourceAnchorId, targetAnchorId);

        Assert.Equal(sourceAnchorId, command.SourceAnchorId);
        Assert.Equal(targetAnchorId, command.TargetAnchorId);
        Assert.Throws<ArgumentNullException>(() => Flow(
            DocumentRevision.Zero,
            null!,
            targetAnchorId));
        Assert.Throws<ArgumentNullException>(() => Flow(
            DocumentRevision.Zero,
            sourceAnchorId,
            null!));
    }

    [Fact]
    public async Task ValidBindingCommitsAndHistoryRestoresExactReferencesWhileAnchorsRemain()
    {
        var harness = await CreateHarnessAsync();
        var anchorsBefore = AnchorState(harness.Document);
        var command = Flow(
            harness.Document.Revision,
            harness.TaskASource,
            harness.TaskBTarget);

        var created = await harness.History.ExecuteAsync(harness.Processor, command);

        Assert.True(created.IsCommitted);
        var visual = FlowVisual(harness.Document);
        Assert.Equal(harness.TaskASource, visual.SourceAnchorId);
        Assert.Equal(harness.TaskBTarget, visual.TargetAnchorId);
        Assert.Equal(anchorsBefore, AnchorState(harness.Document));

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.False(harness.Document.VisualModel.TryGetVisualState(FlowVisualId, out _));
        Assert.Equal(anchorsBefore, AnchorState(harness.Document));

        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        visual = FlowVisual(harness.Document);
        Assert.Equal(harness.TaskASource, visual.SourceAnchorId);
        Assert.Equal(harness.TaskBTarget, visual.TargetAnchorId);
        Assert.Equal(anchorsBefore, AnchorState(harness.Document));
    }

    [Theory]
    [InlineData("wrong-source-role")]
    [InlineData("wrong-target-role")]
    [InlineData("wrong-source-owner")]
    [InlineData("wrong-target-owner")]
    [InlineData("missing-source")]
    [InlineData("missing-target")]
    public async Task InvalidBindingsRejectWithoutDocumentRevisionOrHistoryWork(string scenario)
    {
        var harness = await CreateHarnessAsync();
        var sourceAnchorId = scenario switch
        {
            "wrong-source-role" => harness.TaskATarget,
            "wrong-source-owner" => harness.TaskCSource,
            "missing-source" => new ConnectorAnchorId("bpmn:m321:missing-source"),
            _ => harness.TaskASource,
        };
        var targetAnchorId = scenario switch
        {
            "wrong-target-role" => harness.TaskBSource,
            "wrong-target-owner" => harness.TaskCTarget,
            "missing-target" => new ConnectorAnchorId("bpmn:m321:missing-target"),
            _ => harness.TaskBTarget,
        };
        var before = harness.Document.CaptureSnapshot();
        var historyBefore = harness.History.CaptureStatus();
        await CommandProcessor.WaitForEventDispatchIdleAsync(harness.Document);
        var eventCountBefore = harness.Subscriber.Events.Count;

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            Flow(harness.Document.Revision, sourceAnchorId, targetAnchorId));
        await CommandProcessor.WaitForEventDispatchIdleAsync(harness.Document);

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.SequenceFlowAnchorBindingInvalid);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.Equal(historyBefore, harness.History.CaptureStatus());
        Assert.Equal(eventCountBefore, harness.Subscriber.Events.Count);
    }

    [Fact]
    public async Task HandlerIndependentlyRejectsAnInvalidBindingWithoutAProposal()
    {
        var harness = await CreateHarnessAsync();
        var snapshot = harness.Document.CaptureSnapshot();
        var command = Flow(
            snapshot.Revision,
            new ConnectorAnchorId("bpmn:m321:missing-source"),
            harness.TaskBTarget);

        var handler = BpmnPluginRegistration.M321.CommandHandlers.Single(registration =>
            registration.TypeId == CreateBpmnSequenceFlowCommand.KnownTypeId).Handler;
        var result = await handler.HandleAsync(
            command,
            snapshot,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ProposedDocument);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.SequenceFlowAnchorBindingInvalid);
        Assert.Equal(snapshot, harness.Document.CaptureSnapshot());
    }

    private static async Task<Harness> CreateHarnessAsync()
    {
        var registration = BpmnPluginRegistration.M321;
        var policyProvider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(
            DocumentId,
            connectorAnchorPolicyProvider: policyProvider).Document);
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            subscribers: [subscriber],
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: policyProvider);
        var history = new HistoryManager(document);

        await ExecuteRequiredAsync(history, processor, Task(document, TaskAId, TaskAVisualId, 1));
        await ExecuteRequiredAsync(history, processor, Task(document, TaskBId, TaskBVisualId, 2));
        await ExecuteRequiredAsync(history, processor, Task(document, TaskCId, TaskCVisualId, 3));

        var taskASource = new ConnectorAnchorId("bpmn:m321:task-a:source");
        var taskATarget = new ConnectorAnchorId("bpmn:m321:task-a:target");
        var taskBSource = new ConnectorAnchorId("bpmn:m321:task-b:source");
        var taskBTarget = new ConnectorAnchorId("bpmn:m321:task-b:target");
        var taskCSource = new ConnectorAnchorId("bpmn:m321:task-c:source");
        var taskCTarget = new ConnectorAnchorId("bpmn:m321:task-c:target");
        await ExecuteRequiredAsync(history, processor, Add(
            document, TaskAVisualId, taskASource, ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source));
        await ExecuteRequiredAsync(history, processor, Add(
            document, TaskAVisualId, taskATarget, ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target));
        await ExecuteRequiredAsync(history, processor, Add(
            document, TaskBVisualId, taskBSource, ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source));
        await ExecuteRequiredAsync(history, processor, Add(
            document, TaskBVisualId, taskBTarget, ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target));
        await ExecuteRequiredAsync(history, processor, Add(
            document, TaskCVisualId, taskCSource, ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source));
        await ExecuteRequiredAsync(history, processor, Add(
            document, TaskCVisualId, taskCTarget, ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target));

        return new Harness(
            document,
            processor,
            history,
            taskASource,
            taskATarget,
            taskBSource,
            taskBTarget,
            taskCSource,
            taskCTarget,
            subscriber);
    }

    private static async Task ExecuteRequiredAsync(
        HistoryManager history,
        CommandProcessor processor,
        ICommand command) =>
        Assert.True((await history.ExecuteAsync(processor, command)).IsCommitted);

    private static CreateBpmnTaskCommand Task(
        Document document,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        long number) =>
        new(
            DocumentId,
            document.Revision,
            elementId,
            visualStateId,
            new PointD(number * 100d, 0d),
            new SizeD(120d, 80d),
            $"TASK_{number}",
            $"Task {number}",
            number);

    private static AddConnectorAnchorCommand Add(
        Document document,
        VisualStateId visualStateId,
        ConnectorAnchorId anchorId,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role) =>
        new(
            DocumentId,
            document.Revision,
            visualStateId,
            anchorId,
            side,
            role,
            insertionIndex: 0);

    private static readonly SemanticElementId FlowId = new("bpmn:m321:flow");
    private static readonly VisualStateId FlowVisualId = new("bpmn:m321:flow:visual");

    private static CreateBpmnSequenceFlowCommand Flow(
        DocumentRevision revision,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId) =>
        new(
            DocumentId,
            revision,
            FlowId,
            FlowVisualId,
            TaskAId,
            TaskBId,
            sourceAnchorId,
            targetAnchorId);

    private static VisualStateSnapshot FlowVisual(Document document)
    {
        Assert.True(document.VisualModel.TryGetVisualState(FlowVisualId, out var visual));
        return Assert.IsType<VisualStateSnapshot>(visual);
    }

    private static string AnchorState(Document document) => string.Join(
        "|",
        document.VisualModel.VisualStates
            .Where(visual => visual.ConnectorAnchors.Length != 0)
            .OrderBy(visual => visual.Id.Value, StringComparer.Ordinal)
            .SelectMany(visual => visual.ConnectorAnchors.Select(anchor =>
                $"{visual.Id.Value}:{anchor.Id.Value}:{anchor.Side}:{anchor.Role}:{anchor.Order}")));

    private static void AssertPolicy(
        ElementConnectorAnchorPolicy policy,
        ConnectorAnchorRoleCapability expectedRoles)
    {
        foreach (var side in Enum.GetValues<ConnectorAnchorSide>())
        {
            var edge = policy.ForSide(side);
            Assert.Equal(ConnectorAnchorPolicyMode.DynamicUnlimited, edge.Mode);
            Assert.Equal(expectedRoles, edge.AllowedRoles);
            Assert.Empty(edge.PredefinedAnchors);
        }
    }

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History,
        ConnectorAnchorId TaskASource,
        ConnectorAnchorId TaskATarget,
        ConnectorAnchorId TaskBSource,
        ConnectorAnchorId TaskBTarget,
        ConnectorAnchorId TaskCSource,
        ConnectorAnchorId TaskCTarget,
        RecordingSubscriber Subscriber);

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
