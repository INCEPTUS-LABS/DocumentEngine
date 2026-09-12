using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnCommandAndProjectionTests
{
    [Fact]
    public async Task RegisteredCommandsAtomicallyCreateVisualizedSemanticsAndProjection()
    {
        var documentId = new DocumentId("bpmn:unit-document");
        var startId = new SemanticElementId("bpmn:unit-start");
        var taskId = new SemanticElementId("bpmn:unit-task");
        var endId = new SemanticElementId("bpmn:unit-end");
        var firstFlowId = new SemanticElementId("bpmn:unit-flow-1");
        var secondFlowId = new SemanticElementId("bpmn:unit-flow-2");
        var startSourceAnchorId = new ConnectorAnchorId("bpmn:unit-start-source");
        var taskTargetAnchorId = new ConnectorAnchorId("bpmn:unit-task-target");
        var taskSourceAnchorId = new ConnectorAnchorId("bpmn:unit-task-source");
        var endTargetAnchorId = new ConnectorAnchorId("bpmn:unit-end-target");
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(documentId).Document);
        var registration = BpmnPluginRegistration.M1;
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies);

        Assert.True((await processor.ExecuteAsync(document, new CreateBpmnStartEventCommand(
            documentId,
            document.Revision,
            startId,
            new VisualStateId("bpmn:unit-start-visual"),
            new PointD(0d, 0d),
            new SizeD(36d, 36d)))).IsCommitted);
        Assert.Equal((1, 0, 1), Counts(document));
        Assert.True((await processor.ExecuteAsync(document, new CreateBpmnTaskCommand(
            documentId,
            document.Revision,
            taskId,
            new VisualStateId("bpmn:unit-task-visual"),
            new PointD(80d, 0d),
            new SizeD(120d, 80d),
            "REVIEW_ORDER",
            "Review order",
            20L))).IsCommitted);
        Assert.Equal((2, 0, 2), Counts(document));
        Assert.True((await processor.ExecuteAsync(document, new CreateBpmnEndEventCommand(
            documentId,
            document.Revision,
            endId,
            new VisualStateId("bpmn:unit-end-visual"),
            new PointD(260d, 0d),
            new SizeD(36d, 36d)))).IsCommitted);
        Assert.Equal((3, 0, 3), Counts(document));
        Assert.True((await processor.ExecuteAsync(document, new AddConnectorAnchorCommand(
            documentId,
            document.Revision,
            new VisualStateId("bpmn:unit-start-visual"),
            startSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0))).IsCommitted);
        Assert.True((await processor.ExecuteAsync(document, new AddConnectorAnchorCommand(
            documentId,
            document.Revision,
            new VisualStateId("bpmn:unit-task-visual"),
            taskTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0))).IsCommitted);
        Assert.True((await processor.ExecuteAsync(document, new AddConnectorAnchorCommand(
            documentId,
            document.Revision,
            new VisualStateId("bpmn:unit-task-visual"),
            taskSourceAnchorId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0))).IsCommitted);
        Assert.True((await processor.ExecuteAsync(document, new AddConnectorAnchorCommand(
            documentId,
            document.Revision,
            new VisualStateId("bpmn:unit-end-visual"),
            endTargetAnchorId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0))).IsCommitted);
        Assert.True((await processor.ExecuteAsync(document, new CreateBpmnSequenceFlowCommand(
            documentId,
            document.Revision,
            firstFlowId,
            new VisualStateId("bpmn:unit-flow-visual-1"),
            startId,
            taskId,
            startSourceAnchorId,
            taskTargetAnchorId))).IsCommitted);
        Assert.Equal((3, 1, 4), Counts(document));
        Assert.True((await processor.ExecuteAsync(document, new CreateBpmnSequenceFlowCommand(
            documentId,
            document.Revision,
            secondFlowId,
            new VisualStateId("bpmn:unit-flow-visual-2"),
            taskId,
            endId,
            taskSourceAnchorId,
            endTargetAnchorId))).IsCommitted);
        Assert.Equal((3, 2, 5), Counts(document));

        var result = new ProjectionEngine(registration.ProjectionRules)
            .Project(document.CaptureSnapshot());
        Assert.True(result.IsSuccessful);
        var graph = Assert.IsType<ProjectedGraph>(result.Graph);
        Assert.Equal(3, graph.Nodes.Length);
        Assert.Equal(2, graph.Edges.Length);
        Assert.Equal(4, graph.Ports.Length);
        var taskNode = Assert.Single(graph.Nodes.Where(node =>
            node.Source.SemanticElementId == taskId));
        Assert.Equal(BpmnSemanticTypes.Task, taskNode.Source.SemanticTypeId);
        Assert.Equal(
            20L,
            taskNode.SemanticProperties[BpmnSemanticProperties.ElementNumber].IntegerValue);
        var taskLabel = Assert.Single(graph.Labels);
        Assert.Equal("Review order", taskLabel.Text);
        Assert.Equal(taskNode.Id, taskLabel.OwnerId);
        foreach (var edge in graph.Edges)
        {
            Assert.Contains(graph.Nodes, node => node.Id == edge.SourceNodeId);
            Assert.Contains(graph.Nodes, node => node.Id == edge.TargetNodeId);
            Assert.Contains(new[] { firstFlowId, secondFlowId }, id =>
                id == edge.Source.SemanticElementId);
            Assert.Equal(BpmnSemanticTypes.SequenceFlow, edge.Source.SemanticTypeId);
            Assert.NotNull(edge.SourcePortId);
            Assert.NotNull(edge.TargetPortId);
        }
    }

    private static (int Elements, int Relationships, int Visuals) Counts(Document document) =>
        (
            document.SemanticModel.ElementCount,
            document.SemanticModel.RelationshipCount,
            document.VisualModel.Count);
}
