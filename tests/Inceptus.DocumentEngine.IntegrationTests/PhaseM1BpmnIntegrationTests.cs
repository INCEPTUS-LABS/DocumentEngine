using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseM1BpmnIntegrationTests
{
    private static readonly DocumentId DocumentId = new("bpmn:m1-document");
    private static readonly SemanticElementId StartId = new("bpmn:start-1");
    private static readonly SemanticElementId TaskId = new("bpmn:task-1");
    private static readonly SemanticElementId EndId = new("bpmn:end-1");
    private static readonly SemanticElementId Flow1Id = new("bpmn:flow-1");
    private static readonly SemanticElementId Flow2Id = new("bpmn:flow-2");
    private static readonly VisualStateId StartVisualId = new("bpmn:start-visual");
    private static readonly VisualStateId TaskVisualId = new("bpmn:task-visual");
    private static readonly VisualStateId EndVisualId = new("bpmn:end-visual");
    private static readonly ConnectorAnchorId StartSourceAnchorId =
        new("bpmn:anchor:start-source");
    private static readonly ConnectorAnchorId TaskTargetAnchorId =
        new("bpmn:anchor:task-target");
    private static readonly ConnectorAnchorId TaskSourceAnchorId =
        new("bpmn:anchor:task-source");
    private static readonly ConnectorAnchorId EndTargetAnchorId =
        new("bpmn:anchor:end-target");

    [Fact]
    public async Task EditingSessionComposesM1RegistrationsWithoutBpmnLayoutRoutingOrCanvasCode()
    {
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(DocumentId).Document);
        var bpmn = BpmnPluginRegistration.M1;
        var neutral = NeutralDemoPipeline.CreateComposition().Configuration;
        var configuration = new EditingSessionConfiguration(
            new ProjectionEngine(bpmn.ProjectionRules),
            neutral.LayoutEngine,
            neutral.LayoutAlgorithmId,
            neutral.RoutingEngine,
            neutral.RoutingAlgorithmId,
            neutral.SceneBuilder,
            commandHandlers: bpmn.CommandHandlers,
            commandValidators: bpmn.CommandValidators,
            historyPolicies: bpmn.HistoryPolicies);
        var renderer = new Canvas2DRenderer(
            new RecordingRenderExecution(),
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "demo:font:dejavu",
                        "2.37",
                        "DejaVu Sans",
                        "/fonts/DejaVuSans-2.37.ttf",
                        400,
                        TextFontStyle.Normal),
                ],
                defaultFontFamily: "DejaVu Sans"));
        Assert.True((await renderer.InitializeAsync(
            "phase-m1-canvas",
            new Canvas2DSurfaceSize(800d, 500d, 1d))).Succeeded);
        var attachment = await EditingSession.AttachAsync(document, renderer, configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);

        var creation = await session.ExecuteAsync(Task(document));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(creation.IsCommitted);
        var state = session.CaptureState();
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        var taskNode = Assert.Single(state.ProjectedGraph!.Nodes);
        Assert.Equal(TaskId, taskNode.Source.SemanticElementId);
        Assert.Equal("Review order", Assert.Single(state.ProjectedGraph.Labels).Text);
        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(document.SemanticModel.Elements);
        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(document.SemanticModel.TryGetElement(TaskId, out _));
    }

    [Fact]
    public async Task CompleteInitialModelIsCreatedProjectedUndoneAndRedoneDeterministically()
    {
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(DocumentId).Document);
        var registration = BpmnPluginRegistration.M1;
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies);
        var history = new HistoryManager(document);

        Assert.True((await history.ExecuteAsync(processor, Start(document))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, Task(document))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, End(document))).IsCommitted);
        await AddAnchorAsync(
            history, processor, document, StartVisualId, StartSourceAnchorId,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        await AddAnchorAsync(
            history, processor, document, TaskVisualId, TaskTargetAnchorId,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0);
        await AddAnchorAsync(
            history, processor, document, TaskVisualId, TaskSourceAnchorId,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        await AddAnchorAsync(
            history, processor, document, EndVisualId, EndTargetAnchorId,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0);
        Assert.True((await history.ExecuteAsync(processor,
            Flow(
                document,
                Flow1Id,
                "bpmn:flow-visual-1",
                StartId,
                TaskId,
                StartSourceAnchorId,
                TaskTargetAnchorId))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor,
            Flow(
                document,
                Flow2Id,
                "bpmn:flow-visual-2",
                TaskId,
                EndId,
                TaskSourceAnchorId,
                EndTargetAnchorId))).IsCommitted);

        var created = document.CaptureSnapshot();
        Assert.Equal(3, created.SemanticModel.ElementCount);
        Assert.Equal(2, created.SemanticModel.RelationshipCount);
        Assert.Equal(5, created.VisualModel.Count);
        Assert.True(created.SemanticModel.TryGetElement(TaskId, out var task));
        Assert.Equal("REVIEW_ORDER", task!.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal("Review order", task.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            20L,
            task.Properties[BpmnSemanticProperties.ElementNumber].IntegerValue);
        Assert.NotEqual(TaskId.Value, task.Properties[BpmnSemanticProperties.Code].TextValue);

        var projectionEngine = new ProjectionEngine(registration.ProjectionRules);
        var firstProjection = projectionEngine.Project(created);
        Assert.True(firstProjection.IsSuccessful);
        var graph = Assert.IsType<ProjectedGraph>(firstProjection.Graph);
        Assert.Equal(3, graph.Nodes.Length);
        Assert.Equal(2, graph.Edges.Length);
        var label = Assert.Single(graph.Labels);
        Assert.Equal("Review order", label.Text);
        Assert.Equal(TaskId, label.Source.SemanticElementId);
        Assert.Equal(
            new[] { Flow1Id, Flow2Id },
            graph.Edges.Select(edge => edge.Source.SemanticElementId).OrderBy(id => id.Value));

        for (var index = 0; index < 9; index++)
        {
            Assert.True((await history.UndoAsync(processor)).IsCommitted);
        }

        Assert.Empty(document.SemanticModel.Elements);
        Assert.Empty(document.SemanticModel.Relationships);
        Assert.Empty(document.VisualModel.VisualStates);

        for (var index = 0; index < 9; index++)
        {
            Assert.True((await history.RedoAsync(processor)).IsCommitted);
        }

        var redone = document.CaptureSnapshot();
        Assert.Equal(created.SemanticModel.Elements.AsEnumerable(),
            redone.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(created.SemanticModel.Relationships.AsEnumerable(),
            redone.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(created.VisualModel.VisualStates.AsEnumerable(),
            redone.VisualModel.VisualStates.AsEnumerable());
        var secondProjection = projectionEngine.Project(redone);
        Assert.True(secondProjection.IsSuccessful);
        var redoneGraph = Assert.IsType<ProjectedGraph>(secondProjection.Graph);
        Assert.Equal(graph.Nodes.AsEnumerable(), redoneGraph.Nodes.AsEnumerable());
        Assert.Equal(graph.Edges.AsEnumerable(), redoneGraph.Edges.AsEnumerable());
        Assert.Equal(graph.Labels.AsEnumerable(), redoneGraph.Labels.AsEnumerable());
    }

    [Fact]
    public async Task InvalidSequenceFlowDirectionsAreAtomicFailures()
    {
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(DocumentId).Document);
        var registration = BpmnPluginRegistration.M1;
        var subscriber = new RecordingSubscriber();
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            subscribers: [subscriber],
            historyPolicies: registration.HistoryPolicies);
        var history = new HistoryManager(document);
        Assert.True((await history.ExecuteAsync(processor, Start(document))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, Task(document))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, End(document))).IsCommitted);
        var startTargetAnchorId = new ConnectorAnchorId("bpmn:anchor:start-target-invalid");
        var endSourceAnchorId = new ConnectorAnchorId("bpmn:anchor:end-source-invalid");
        await AddAnchorAsync(
            history, processor, document, TaskVisualId, TaskSourceAnchorId,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        await AddAnchorAsync(
            history, processor, document, TaskVisualId, TaskTargetAnchorId,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0);
        await AddAnchorAsync(
            history, processor, document, StartVisualId, startTargetAnchorId,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0);
        await AddAnchorAsync(
            history, processor, document, EndVisualId, endSourceAnchorId,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);
        var before = document.CaptureSnapshot();
        var historyBefore = history.CaptureStatus();
        var eventCountBefore = subscriber.Events.Count;

        var incomingStart = await history.ExecuteAsync(processor,
            Flow(
                document,
                Flow1Id,
                "bpmn:invalid-visual-1",
                TaskId,
                StartId,
                TaskSourceAnchorId,
                startTargetAnchorId));
        var outgoingEnd = await history.ExecuteAsync(processor,
            Flow(
                document,
                Flow2Id,
                "bpmn:invalid-visual-2",
                EndId,
                TaskId,
                endSourceAnchorId,
                TaskTargetAnchorId));
        var missingEndpoint = await history.ExecuteAsync(processor,
            Flow(
                document,
                new SemanticElementId("bpmn:missing-flow"),
                "bpmn:missing-flow-visual",
                TaskId,
                new SemanticElementId("bpmn:missing-target"),
                TaskSourceAnchorId,
                new ConnectorAnchorId("bpmn:anchor:missing-target")));
        var duplicateTask = await history.ExecuteAsync(processor, Task(document));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);

        Assert.False(incomingStart.IsCommitted);
        Assert.False(outgoingEnd.IsCommitted);
        Assert.False(missingEndpoint.IsCommitted);
        Assert.False(duplicateTask.IsCommitted);
        Assert.Contains(incomingStart.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.IncomingStartEvent);
        Assert.Contains(outgoingEnd.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.OutgoingEndEvent);
        Assert.Contains(missingEndpoint.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.EndpointMissing);
        Assert.Contains(duplicateTask.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.DuplicateSemanticId);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(historyBefore, history.CaptureStatus());
        Assert.Equal(eventCountBefore, subscriber.Events.Count);
    }

    [Fact]
    public async Task GenericPropertyCommandsEditTaskPropertiesWithNormalHistory()
    {
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(DocumentId).Document);
        var registration = BpmnPluginRegistration.M1;
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies);
        var history = new HistoryManager(document);
        Assert.True((await history.ExecuteAsync(processor, Task(document))).IsCommitted);

        Assert.True((await history.ExecuteAsync(processor, new UpdateSemanticElementNameCommand(
            document.DocumentId,
            document.Revision,
            TaskId,
            BpmnSemanticProperties.Name,
            "Review customer order"))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, new UpdateSemanticElementPropertyCommand(
            document.DocumentId,
            document.Revision,
            TaskId,
            BpmnSemanticProperties.Code,
            PropertyValue.FromText("REVIEW_CUSTOMER_ORDER")))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, new UpdateSemanticElementPropertyCommand(
            document.DocumentId,
            document.Revision,
            TaskId,
            BpmnSemanticProperties.ElementNumber,
            PropertyValue.FromInteger(21L)))).IsCommitted);

        Assert.True(document.SemanticModel.TryGetElement(TaskId, out var task));
        Assert.Equal("Review customer order",
            task!.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal("REVIEW_CUSTOMER_ORDER",
            task.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal(
            21L,
            task.Properties[BpmnSemanticProperties.ElementNumber].IntegerValue);
        Assert.Equal(4, history.CaptureStatus().EntryCount);
        var beforeRejectedEdit = document.CaptureSnapshot();
        var rejectedCode = await history.ExecuteAsync(processor,
            new UpdateSemanticElementPropertyCommand(
                document.DocumentId,
                document.Revision,
                TaskId,
                BpmnSemanticProperties.Code,
                PropertyValue.FromText(string.Empty)));
        Assert.False(rejectedCode.IsCommitted);
        Assert.Contains(rejectedCode.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.InvalidTask);
        var rejectedElementNumber = await history.ExecuteAsync(processor,
            new UpdateSemanticElementPropertyCommand(
                document.DocumentId,
                document.Revision,
                TaskId,
                BpmnSemanticProperties.ElementNumber,
                PropertyValue.FromText("21")));
        Assert.False(rejectedElementNumber.IsCommitted);
        Assert.Contains(rejectedElementNumber.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.InvalidTask);
        Assert.Equal(beforeRejectedEdit, document.CaptureSnapshot());
        Assert.Equal(4, history.CaptureStatus().EntryCount);
        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.True((await history.UndoAsync(processor)).IsCommitted);
        Assert.True(document.SemanticModel.TryGetElement(TaskId, out task));
        Assert.Equal("Review order", task!.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal("REVIEW_ORDER", task.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal(
            20L,
            task.Properties[BpmnSemanticProperties.ElementNumber].IntegerValue);
        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.True((await history.RedoAsync(processor)).IsCommitted);
        Assert.True(document.SemanticModel.TryGetElement(TaskId, out task));
        Assert.Equal(
            "Review customer order",
            task!.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            "REVIEW_CUSTOMER_ORDER",
            task.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal(
            21L,
            task.Properties[BpmnSemanticProperties.ElementNumber].IntegerValue);
    }

    private static CreateBpmnStartEventCommand Start(Document document) =>
        new(
            document.DocumentId,
            document.Revision,
            StartId,
            StartVisualId,
            new PointD(0d, 40d),
            new SizeD(36d, 36d));

    private static CreateBpmnTaskCommand Task(Document document) =>
        new(
            document.DocumentId,
            document.Revision,
            TaskId,
            TaskVisualId,
            new PointD(100d, 20d),
            new SizeD(120d, 80d),
            "REVIEW_ORDER",
            "Review order",
            20L);

    private static CreateBpmnEndEventCommand End(Document document) =>
        new(
            document.DocumentId,
            document.Revision,
            EndId,
            EndVisualId,
            new PointD(280d, 40d),
            new SizeD(36d, 36d));

    private static CreateBpmnSequenceFlowCommand Flow(
        Document document,
        SemanticElementId relationshipId,
        string visualId,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId) =>
        new(
            document.DocumentId,
            document.Revision,
            relationshipId,
            new VisualStateId(visualId),
            sourceId,
            targetId,
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
                document.DocumentId,
                document.Revision,
                visualStateId,
                anchorId,
                side,
                role,
                insertionIndex));
        Assert.True(result.IsCommitted);
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

    private sealed class RecordingRenderExecution : ICanvas2DRenderExecution
    {
        public ValueTask<Canvas2DInteropOperationResult> InitializeAsync(
            string canvasElementId,
            Canvas2DSurfaceSize surfaceSize,
            ImmutableSortedDictionary<string, string> imageResources,
            ImmutableArray<Canvas2DFontResource> fontResources,
            string? defaultFontFamily) =>
            ValueTask.FromResult(Success());

        public ValueTask<Canvas2DInteropOperationResult> ResizeAsync(
            Canvas2DSurfaceSize surfaceSize) =>
            ValueTask.FromResult(Success());

        public ValueTask<Canvas2DInteropOperationResult> RenderAsync(
            Canvas2DRenderFrame frame) =>
            ValueTask.FromResult(Success());

        public ValueTask<Canvas2DTextMeasurementInteropResult> MeasureTextAsync(
            Canvas2DTextMeasurementRequestData request) =>
            ValueTask.FromResult(new Canvas2DTextMeasurementInteropResult
            {
                Succeeded = true,
                Width = 80d,
                Ascent = 10d,
                Descent = 3d,
                LineHeight = 18d,
                BoundingX = 0d,
                BoundingY = -10d,
                BoundingWidth = 80d,
                BoundingHeight = 13d,
                ResolvedFontIdentity = "demo:font:dejavu@2.37",
            });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static Canvas2DInteropOperationResult Success() => new() { Succeeded = true };
    }
}
