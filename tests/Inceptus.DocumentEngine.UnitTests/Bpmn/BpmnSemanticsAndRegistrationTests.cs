using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnSemanticsAndRegistrationTests
{
    [Fact]
    public void SemanticTypeIdsAreCanonicalAndStable()
    {
        Assert.Equal("BPMN.StartEvent", BpmnSemanticTypes.StartEvent.Value);
        Assert.Equal("BPMN.Task", BpmnSemanticTypes.Task.Value);
        Assert.Equal("BPMN.EndEvent", BpmnSemanticTypes.EndEvent.Value);
        Assert.Equal("BPMN.SequenceFlow", BpmnSemanticTypes.SequenceFlow.Value);
    }

    [Fact]
    public void TaskKeepsTechnicalIdentityCodeNameAndElementNumberIndependent()
    {
        var task = BpmnSemanticFactory.CreateTask(
            new SemanticElementId("bpmn:task-technical-id"),
            "REVIEW_ORDER",
            "Review order",
            20L);

        Assert.Equal(BpmnSemanticTypes.Task, task.TypeId);
        Assert.NotEqual(task.Id.Value, task.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal("REVIEW_ORDER", task.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal("Review order", task.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            PropertyValueKind.Integer,
            task.Properties[BpmnSemanticProperties.ElementNumber].Kind);
        Assert.Equal(20L, task.Properties[BpmnSemanticProperties.ElementNumber].IntegerValue);
    }

    [Fact]
    public void FactoryCreatesTheFourM1SemanticShapesAndRejectsMalformedTaskData()
    {
        var startId = new SemanticElementId("bpmn:start");
        var taskId = new SemanticElementId("bpmn:task");
        var endId = new SemanticElementId("bpmn:end");

        Assert.Equal(BpmnSemanticTypes.StartEvent,
            BpmnSemanticFactory.CreateStartEvent(startId).TypeId);
        Assert.Equal(BpmnSemanticTypes.Task,
            BpmnSemanticFactory.CreateTask(taskId, "CODE", "Task", 20L).TypeId);
        Assert.Equal(BpmnSemanticTypes.EndEvent,
            BpmnSemanticFactory.CreateEndEvent(endId).TypeId);
        var flow = BpmnSemanticFactory.CreateSequenceFlow(
            new SemanticElementId("bpmn:flow"), startId, taskId);
        Assert.Equal(BpmnSemanticTypes.SequenceFlow, flow.TypeId);
        Assert.Equal(startId, flow.SourceId);
        Assert.Equal(taskId, flow.TargetId);
        Assert.Throws<ArgumentException>(() =>
            BpmnSemanticFactory.CreateTask(taskId, " ", "Task", 20L));
        Assert.Throws<ArgumentException>(() =>
            BpmnSemanticFactory.CreateTask(taskId, "CODE", " ", 20L));
    }

    [Fact]
    public void M1RegistrationExposesAllCommandValidationHistoryAndProjectionContributions()
    {
        var registration = BpmnPluginRegistration.M1;
        var publicCreationTypes = new[]
        {
            CreateBpmnStartEventCommand.KnownTypeId,
            CreateBpmnTaskCommand.KnownTypeId,
            CreateBpmnEndEventCommand.KnownTypeId,
            CreateBpmnSequenceFlowCommand.KnownTypeId,
        };

        Assert.All(publicCreationTypes, typeId =>
        {
            Assert.Contains(registration.CommandHandlers, item => item.TypeId == typeId);
            Assert.Contains(registration.CommandValidators, item => item.TypeId == typeId);
            Assert.Contains(registration.HistoryPolicies, item => item.TypeId == typeId);
        });
        Assert.Equal(5, registration.CommandHandlers.Length);
        Assert.Equal(6, registration.CommandValidators.Length);
        Assert.Contains(registration.CommandValidators, item =>
            item.ValidatorId.Value == "bpmn:validator/task-code-update");
        Assert.Contains(registration.CommandValidators, item =>
            item.ValidatorId.Value == "bpmn:validator/task-element-number-update");
        Assert.Equal(4, registration.HistoryPolicies.Length);
        Assert.Equal(4, registration.ProjectionRules.Length);
        Assert.Empty(registration.LayoutAlgorithms);
        Assert.Empty(registration.RoutingAlgorithms);
        Assert.Empty(registration.SceneContributors);
        Assert.Empty(registration.ToolboxContributions);
        Assert.Empty(registration.PropertiesSchemas);
        Assert.Contains(registration.ProjectionRules, rule =>
            rule.SourceKind == ProjectionSourceKind.SemanticRelationship &&
            rule.SemanticTypeId == BpmnSemanticTypes.SequenceFlow);
        Assert.All(registration.CommandHandlers, item =>
            Assert.IsAssignableFrom<ICommandEnvelopeValidator>(item.EnvelopeValidator));
    }

    [Fact]
    public void M2RegistrationComposesM1WithOneLayoutAndRoutingAlgorithm()
    {
        var m1 = BpmnPluginRegistration.M1;
        var m2 = BpmnPluginRegistration.M2;

        Assert.Equal(m1.CommandHandlers.AsEnumerable(), m2.CommandHandlers.AsEnumerable());
        Assert.Equal(m1.CommandValidators.AsEnumerable(), m2.CommandValidators.AsEnumerable());
        Assert.Equal(m1.HistoryPolicies.AsEnumerable(), m2.HistoryPolicies.AsEnumerable());
        Assert.Equal(m1.ProjectionRules.AsEnumerable(), m2.ProjectionRules.AsEnumerable());

        var layout = Assert.Single(m2.LayoutAlgorithms);
        Assert.Equal(BpmnAlgorithmIds.DefaultLayout, layout.AlgorithmId);
        Assert.Equal("bpmn:layout/default", layout.AlgorithmId.Value);
        Assert.IsType<BpmnLayoutAlgorithm>(layout.Algorithm);
        Assert.IsAssignableFrom<ILayoutAlgorithm>(layout.Algorithm);

        var routing = Assert.Single(m2.RoutingAlgorithms);
        Assert.Equal(BpmnAlgorithmIds.DefaultRouting, routing.AlgorithmId);
        Assert.Equal("bpmn:routing/default", routing.AlgorithmId.Value);
        Assert.IsType<BpmnRoutingAlgorithm>(routing.Algorithm);
        Assert.IsAssignableFrom<IRoutingAlgorithm>(routing.Algorithm);

        Assert.Empty(m1.LayoutAlgorithms);
        Assert.Empty(m1.RoutingAlgorithms);
        Assert.Empty(m1.SceneContributors);
        Assert.Empty(m1.ToolboxContributions);
        Assert.Empty(m1.PropertiesSchemas);
        Assert.Empty(m2.SceneContributors);
        Assert.Empty(m2.ToolboxContributions);
        Assert.Empty(m2.PropertiesSchemas);
    }
}
