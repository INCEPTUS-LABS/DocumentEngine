using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Validation;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN5StructuralValidationTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n5:document");
    private static readonly DocumentRevision Revision = new(3);

    [Fact]
    public void N5ComposesN4AndAddsOnlyTheStructuralValidationContribution()
    {
        var n4 = BpmnPluginRegistration.N4;
        var n5 = BpmnPluginRegistration.N5;

        Assert.Equal(n4.CommandHandlers, n5.CommandHandlers);
        Assert.Equal(n4.CommandValidators, n5.CommandValidators);
        Assert.Equal(n4.HistoryPolicies, n5.HistoryPolicies);
        Assert.Equal(n4.ProjectionRules, n5.ProjectionRules);
        Assert.Equal(n4.LayoutAlgorithms, n5.LayoutAlgorithms);
        Assert.Equal(n4.RoutingAlgorithms, n5.RoutingAlgorithms);
        Assert.Equal(n4.SceneContributors, n5.SceneContributors);
        Assert.Equal(n4.ToolboxContributions, n5.ToolboxContributions);
        Assert.Equal(n4.ToolboxPlacementRegistrations, n5.ToolboxPlacementRegistrations);
        Assert.Equal(n4.PropertiesSchemas, n5.PropertiesSchemas);
        Assert.Equal(n4.ConnectorAnchorPolicies, n5.ConnectorAnchorPolicies);
        Assert.Equal(
            n4.AnchorConnectionCreationRegistrations,
            n5.AnchorConnectionCreationRegistrations);
        Assert.Equal(
            n4.ConnectorEndpointReconnectionRegistrations,
            n5.ConnectorEndpointReconnectionRegistrations);
        Assert.Equal(n4.DiagramDeletionRegistrations, n5.DiagramDeletionRegistrations);
        Assert.Empty(n4.ModelValidationRules);
        Assert.IsType<BpmnStructuralValidationRule>(Assert.Single(n5.ModelValidationRules));
    }

    [Fact]
    public void MissingStartProducesOneGlobalWarningWithoutUnreachableFlood()
    {
        var task = Element("task", BpmnSemanticTypes.Task);
        var end = Element("end", BpmnSemanticTypes.EndEvent);
        var issues = Validate(
            [task, end],
            [Flow("task-end", task, end)]);

        var issue = Assert.Single(issues);
        Assert.Equal(BpmnModelValidationCodes.NoStartEvent, issue.Code);
        Assert.True(issue.Target.IsDocument);
        Assert.DoesNotContain(issues, item =>
            item.Code == BpmnModelValidationCodes.NodeUnreachableFromStart);
    }

    [Fact]
    public void MissingEndProducesOneGlobalWarningWithoutCannotReachEndFlood()
    {
        var start = Element("start", BpmnSemanticTypes.StartEvent);
        var task = Element("task", BpmnSemanticTypes.Task);
        var issues = Validate(
            [start, task],
            [Flow("start-task", start, task)]);

        var issue = Assert.Single(issues);
        Assert.Equal(BpmnModelValidationCodes.NoEndEvent, issue.Code);
        Assert.True(issue.Target.IsDocument);
        Assert.DoesNotContain(issues, item =>
            item.Code == BpmnModelValidationCodes.NodeCannotReachEnd);
    }

    [Fact]
    public void StartAndEndCompletenessWarningsTargetTheExactEvents()
    {
        var start = Element("start", BpmnSemanticTypes.StartEvent);
        var end = Element("end", BpmnSemanticTypes.EndEvent);
        var issues = Validate([start, end]);

        Assert.Equal(
            start.Id,
            Assert.Single(issues, item =>
                item.Code == BpmnModelValidationCodes.StartEventNoOutgoing)
                .Target.SemanticElementId);
        Assert.Equal(
            end.Id,
            Assert.Single(issues, item =>
                item.Code == BpmnModelValidationCodes.EndEventNoIncoming)
                .Target.SemanticElementId);
    }

    [Theory]
    [MemberData(nameof(OrdinaryFlowNodeTypes))]
    public void IsolatedOrdinaryFlowNodeHasOneIssueAndSuppressesReachabilityDuplicates(
        SemanticTypeId typeId)
    {
        var start = Element("start", BpmnSemanticTypes.StartEvent);
        var end = Element("end", BpmnSemanticTypes.EndEvent);
        var isolated = Element("isolated", typeId);
        var issues = Validate(
            [start, isolated, end],
            [Flow("start-end", start, end)]);

        var targeted = issues.Where(issue =>
            issue.Target.SemanticElementId == isolated.Id).ToArray();
        Assert.Equal(
            BpmnModelValidationCodes.FlowNodeIsolated,
            Assert.Single(targeted).Code);
    }

    [Theory]
    [MemberData(nameof(GatewayTypes))]
    public void EveryGatewayTypeReportsMissingIncomingAndOutgoing(SemanticTypeId typeId)
    {
        var start = Element("start", BpmnSemanticTypes.StartEvent);
        var end = Element("end", BpmnSemanticTypes.EndEvent);
        var gateway = Element("gateway", typeId);
        var issues = Validate(
            [start, gateway, end],
            [Flow("start-end", start, end)]);

        Assert.Single(issues, issue =>
            issue.Code == BpmnModelValidationCodes.GatewayNoIncoming &&
            issue.Target.SemanticElementId == gateway.Id);
        Assert.Single(issues, issue =>
            issue.Code == BpmnModelValidationCodes.GatewayNoOutgoing &&
            issue.Target.SemanticElementId == gateway.Id);
    }

    [Fact]
    public void EventBasedGatewayRequiresTwoOutgoingFlowsAndRejectsNonCatchTarget()
    {
        var start = Element("start", BpmnSemanticTypes.StartEvent);
        var gateway = Element("gateway", BpmnSemanticTypes.EventBasedGateway);
        var message = Element("message", BpmnSemanticTypes.MessageCatchEvent);
        var timer = Element("timer", BpmnSemanticTypes.TimerCatchEvent);
        var task = Element("task", BpmnSemanticTypes.Task);
        var end = Element("end", BpmnSemanticTypes.EndEvent);

        var zero = Validate([start, gateway, end], [Flow("start-gateway", start, gateway)]);
        Assert.Single(zero, issue =>
            issue.Code == BpmnModelValidationCodes.EventBasedGatewayIncomplete);

        var one = Validate(
            [start, gateway, message, end],
            [
                Flow("start-gateway", start, gateway),
                Flow("gateway-message", gateway, message),
                Flow("message-end", message, end),
            ]);
        Assert.Single(one, issue =>
            issue.Code == BpmnModelValidationCodes.EventBasedGatewayIncomplete);

        var valid = Validate(
            [start, gateway, message, timer, end],
            [
                Flow("start-gateway", start, gateway),
                Flow("gateway-message", gateway, message),
                Flow("gateway-timer", gateway, timer),
                Flow("message-end", message, end),
                Flow("timer-end", timer, end),
            ]);
        Assert.DoesNotContain(valid, issue =>
            issue.Code == BpmnModelValidationCodes.EventBasedGatewayIncomplete ||
            issue.Code == BpmnModelValidationCodes.EventBasedGatewayInvalidTarget);

        var invalidFlow = Flow("gateway-task", gateway, task);
        var invalid = Validate(
            [start, gateway, message, task, end],
            [
                Flow("start-gateway", start, gateway),
                Flow("gateway-message", gateway, message),
                invalidFlow,
                Flow("message-end", message, end),
                Flow("task-end", task, end),
            ],
            visualSemanticId: invalidFlow.Id);
        var error = Assert.Single(invalid, issue =>
            issue.Code == BpmnModelValidationCodes.EventBasedGatewayInvalidTarget);
        Assert.Equal(ModelValidationSeverity.Error, error.Severity);
        Assert.Equal(invalidFlow.Id, error.Target.SemanticElementId);
        Assert.Equal(new VisualStateId("visual:gateway-task"), error.Target.VisualStateId);
    }

    [Fact]
    public void ForwardAndReverseTraversalsHandleReachabilityAndDeadEnds()
    {
        var start = Element("start", BpmnSemanticTypes.StartEvent);
        var a = Element("a", BpmnSemanticTypes.Task);
        var b = Element("b", BpmnSemanticTypes.Task);
        var c = Element("c", BpmnSemanticTypes.Task);
        var end = Element("end", BpmnSemanticTypes.EndEvent);
        var issues = Validate(
            [start, a, b, c, end],
            [
                Flow("start-a", start, a),
                Flow("a-end", a, end),
                Flow("b-end", b, end),
                Flow("start-c", start, c),
                Flow("c-c", c, c),
            ],
            visualSemanticId: b.Id);

        var unreachable = Assert.Single(issues, issue =>
            issue.Code == BpmnModelValidationCodes.NodeUnreachableFromStart);
        Assert.Equal(b.Id, unreachable.Target.SemanticElementId);
        Assert.Equal(new VisualStateId("visual:b"), unreachable.Target.VisualStateId);
        var deadEnd = Assert.Single(issues, issue =>
            issue.Code == BpmnModelValidationCodes.NodeCannotReachEnd);
        Assert.Equal(c.Id, deadEnd.Target.SemanticElementId);
    }

    [Fact]
    public void MultipleStartEndComponentsAndCyclesAreAcceptedByNormalTraversal()
    {
        var startA = Element("start-a", BpmnSemanticTypes.StartEvent);
        var startB = Element("start-b", BpmnSemanticTypes.StartEvent);
        var a = Element("a", BpmnSemanticTypes.Task);
        var b = Element("b", BpmnSemanticTypes.Task);
        var endA = Element("end-a", BpmnSemanticTypes.EndEvent);
        var endB = Element("end-b", BpmnSemanticTypes.EndEvent);
        var issues = Validate(
            [startA, startB, a, b, endA, endB],
            [
                Flow("start-a-a", startA, a),
                Flow("a-a", a, a),
                Flow("a-end-a", a, endA),
                Flow("start-b-b", startB, b),
                Flow("b-end-b", b, endB),
            ]);

        Assert.Empty(issues);
    }

    public static TheoryData<SemanticTypeId> OrdinaryFlowNodeTypes => new()
    {
        BpmnSemanticTypes.Task,
        BpmnSemanticTypes.MessageCatchEvent,
        BpmnSemanticTypes.TimerCatchEvent,
    };

    public static TheoryData<SemanticTypeId> GatewayTypes => new()
    {
        BpmnSemanticTypes.ExclusiveGateway,
        BpmnSemanticTypes.ParallelGateway,
        BpmnSemanticTypes.InclusiveGateway,
        BpmnSemanticTypes.EventBasedGateway,
    };

    private static System.Collections.Immutable.ImmutableArray<ModelValidationIssue> Validate(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot>? flows = null,
        SemanticElementId? visualSemanticId = null)
    {
        var snapshot = Snapshot(elements, flows, visualSemanticId);
        return new ModelValidationEngine(new ModelValidationCatalog(
            [new BpmnStructuralValidationRule()]))
            .Validate(new ModelValidationContext(snapshot))
            .Issues;
    }

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot>? flows,
        SemanticElementId? visualSemanticId)
    {
        VisualStateSnapshot[] visuals = visualSemanticId is null
            ? []
            :
            [
                new VisualStateSnapshot(
                    new VisualStateId($"visual:{visualSemanticId.Value}"),
                    visualSemanticId,
                    new PointD(10d, 10d),
                    new SizeD(20d, 20d),
                    VisualPlacementMode.Manual),
            ];
        return new DocumentSnapshot(
            new SemanticModelSnapshot(DocumentId, Revision, elements, flows),
            new VisualModelSnapshot(DocumentId, Revision, visuals),
            new DocumentMetadataSnapshot(DocumentId, Revision));
    }

    private static SemanticElementSnapshot Element(string id, SemanticTypeId typeId) =>
        new(new SemanticElementId(id), typeId);

    private static SemanticRelationshipSnapshot Flow(
        string id,
        SemanticElementSnapshot source,
        SemanticElementSnapshot target) =>
        new(
            new SemanticElementId(id),
            BpmnSemanticTypes.SequenceFlow,
            source.Id,
            target.Id);
}
