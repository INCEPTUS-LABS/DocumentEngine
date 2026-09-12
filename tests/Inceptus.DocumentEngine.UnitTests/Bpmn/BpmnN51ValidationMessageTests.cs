using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Validation;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN51ValidationMessageTests
{
    private static readonly DocumentId DocumentId = new("n51:document");
    private static readonly DocumentRevision Revision = new(9);

    [Theory]
    [InlineData("Handle timeout", "HANDLE_TIMEOUT",
        "Task 17 \"Handle timeout\" [Code: HANDLE_TIMEOUT, ID: element:task]")]
    [InlineData("Handle timeout", null,
        "Task 17 \"Handle timeout\" [ID: element:task]")]
    [InlineData(null, "HANDLE_TIMEOUT",
        "Task 17 [Code: HANDLE_TIMEOUT, ID: element:task]")]
    [InlineData(null, null, "Task 17 [ID: element:task]")]
    public void TaskReferenceUsesNumberAndOnlyAvailableNameAndCode(
        string? name,
        string? code,
        string expectedReference)
    {
        var task = Element(
            "element:task",
            BpmnSemanticTypes.Task,
            name,
            code,
            elementNumber: 17);

        var issue = IsolatedIssue(task);

        Assert.Equal(
            $"{expectedReference} is isolated from every Sequence Flow.",
            issue.Message);
    }

    [Theory]
    [InlineData("Approved?", "APPROVAL",
        "Exclusive Gateway \"Approved?\" [Code: APPROVAL, ID: element:gateway]")]
    [InlineData("Approved?", null,
        "Exclusive Gateway \"Approved?\" [ID: element:gateway]")]
    [InlineData(null, "APPROVAL",
        "Exclusive Gateway [Code: APPROVAL, ID: element:gateway]")]
    [InlineData(null, null, "Exclusive Gateway [ID: element:gateway]")]
    public void GatewayReferenceUsesOnlyAvailableNameAndCode(
        string? name,
        string? code,
        string expectedReference)
    {
        var gateway = Element(
            "element:gateway",
            BpmnSemanticTypes.ExclusiveGateway,
            name,
            code,
            elementNumber: 99);
        var issues = ValidateWithValidStartEnd(gateway);

        var issue = Assert.Single(issues, item =>
            item.Code == BpmnModelValidationCodes.GatewayNoIncoming);
        var outgoingIssue = Assert.Single(issues, item =>
            item.Code == BpmnModelValidationCodes.GatewayNoOutgoing);

        Assert.Equal(
            $"{expectedReference} has no incoming Sequence Flow.",
            issue.Message);
        Assert.Equal(
            $"{expectedReference} has no outgoing Sequence Flow.",
            outgoingIssue.Message);
        Assert.DoesNotContain("99", issue.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(FlowNodeDisplayNames))]
    public void EverySupportedFlowNodeUsesItsUserFacingBpmnTypeName(
        SemanticTypeId typeId,
        string expectedTypeName,
        string expectedCode)
    {
        var element = Element(
            "element:target",
            typeId,
            "Readable name",
            "READABLE_CODE",
            elementNumber: 23);
        var issues = typeId == BpmnSemanticTypes.StartEvent ||
            typeId == BpmnSemanticTypes.EndEvent
            ? Validate(
                [
                    element,
                    Element(
                        typeId == BpmnSemanticTypes.StartEvent
                            ? "element:end"
                            : "element:start",
                        typeId == BpmnSemanticTypes.StartEvent
                            ? BpmnSemanticTypes.EndEvent
                            : BpmnSemanticTypes.StartEvent),
                ])
            : ValidateWithValidStartEnd(element);

        var issue = Assert.Single(issues, item => item.Code == expectedCode);
        Assert.StartsWith(expectedTypeName, issue.Message, StringComparison.Ordinal);
        Assert.Contains("ID: element:target]", issue.Message, StringComparison.Ordinal);
        if (typeId != BpmnSemanticTypes.Task &&
            !IsGateway(typeId))
        {
            Assert.DoesNotContain("READABLE_CODE", issue.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("23", issue.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TaskReachabilityMessagesContainTheSameCompleteReadableReference()
    {
        var start = Element("element:start", BpmnSemanticTypes.StartEvent);
        var end = Element("element:end", BpmnSemanticTypes.EndEvent);
        var valid = Element("element:valid", BpmnSemanticTypes.Task, elementNumber: 1);
        var unreachable = Element(
            "element:unreachable",
            BpmnSemanticTypes.Task,
            "Review order",
            "REVIEW_ORDER",
            15);
        var deadEnd = Element(
            "element:dead-end",
            BpmnSemanticTypes.Task,
            "Handle timeout",
            "HANDLE_TIMEOUT",
            17);
        var issues = Validate(
            [start, end, valid, unreachable, deadEnd],
            [
                Flow("flow:start-valid", start, valid),
                Flow("flow:valid-end", valid, end),
                Flow("flow:unreachable-end", unreachable, end),
                Flow("flow:start-dead-end", start, deadEnd),
            ]);

        Assert.Equal(
            "Task 15 \"Review order\" [Code: REVIEW_ORDER, ID: element:unreachable] " +
            "is not reachable from any Start Event.",
            Assert.Single(issues, item =>
                item.Code == BpmnModelValidationCodes.NodeUnreachableFromStart).Message);
        Assert.Equal(
            "Task 17 \"Handle timeout\" [Code: HANDLE_TIMEOUT, ID: element:dead-end] " +
            "cannot reach any End Event.",
            Assert.Single(issues, item =>
                item.Code == BpmnModelValidationCodes.NodeCannotReachEnd).Message);
    }

    [Fact]
    public void EventsUseNameAndIdWithoutFabricatedCodeOrElementNumber()
    {
        var message = Element(
            "element:message",
            BpmnSemanticTypes.MessageCatchEvent,
            "Customer message",
            "NOT_SUPPORTED",
            88);
        var timer = Element(
            "element:timer",
            BpmnSemanticTypes.TimerCatchEvent,
            name: "   ",
            code: "NOT_SUPPORTED",
            elementNumber: 89);

        var messageIssue = IsolatedIssue(message);
        var timerIssue = IsolatedIssue(timer);

        Assert.Equal(
            "Message Catch Event \"Customer message\" [ID: element:message] " +
            "is isolated from every Sequence Flow.",
            messageIssue.Message);
        Assert.Equal(
            "Timer Catch Event [ID: element:timer] is isolated from every Sequence Flow.",
            timerIssue.Message);
        Assert.DoesNotContain("NOT_SUPPORTED", messageIssue.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("88", messageIssue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EventBasedGatewayAndInvalidTargetMessagesIdentifyEverySemanticObject()
    {
        var start = Element("element:start", BpmnSemanticTypes.StartEvent);
        var gateway = Element(
            "element:gateway",
            BpmnSemanticTypes.EventBasedGateway,
            "Await customer event",
            "AWAIT_EVENT");
        var task = Element(
            "element:task",
            BpmnSemanticTypes.Task,
            "Process message",
            "PROCESS_MESSAGE",
            12);
        var end = Element("element:end", BpmnSemanticTypes.EndEvent);
        var invalidFlow = Flow("flow:gateway-task", gateway, task);
        var issues = Validate(
            [start, gateway, task, end],
            [
                Flow("flow:start-gateway", start, gateway),
                invalidFlow,
                Flow("flow:task-end", task, end),
            ]);

        Assert.Equal(
            "Event-Based Gateway \"Await customer event\" " +
            "[Code: AWAIT_EVENT, ID: element:gateway] " +
            "has fewer than two outgoing event branches.",
            Assert.Single(issues, item =>
                item.Code == BpmnModelValidationCodes.EventBasedGatewayIncomplete).Message);
        Assert.Equal(
            "Sequence Flow [ID: flow:gateway-task] from " +
            "Event-Based Gateway \"Await customer event\" " +
            "[Code: AWAIT_EVENT, ID: element:gateway] to " +
            "Task 12 \"Process message\" " +
            "[Code: PROCESS_MESSAGE, ID: element:task] " +
            "has an invalid target for an Event-Based Gateway.",
            Assert.Single(issues, item =>
                item.Code == BpmnModelValidationCodes.EventBasedGatewayInvalidTarget).Message);
    }

    [Fact]
    public void DocumentLevelMessagesRemainGlobalAndDoNotFabricateTargets()
    {
        var task = Element("element:task", BpmnSemanticTypes.Task, elementNumber: 1);
        var end = Element("element:end", BpmnSemanticTypes.EndEvent);
        var noStart = Validate(
            [task, end],
            [Flow("flow:task-end", task, end)]);
        var noStartIssue = Assert.Single(noStart);
        Assert.Equal("The BPMN model has no Start Event.", noStartIssue.Message);
        Assert.True(noStartIssue.Target.IsDocument);

        var start = Element("element:start", BpmnSemanticTypes.StartEvent);
        var noEnd = Validate(
            [start, task],
            [Flow("flow:start-task", start, task)]);
        var noEndIssue = Assert.Single(noEnd);
        Assert.Equal("The BPMN model has no End Event.", noEndIssue.Message);
        Assert.True(noEndIssue.Target.IsDocument);
    }

    [Fact]
    public void NameAndCodeChangesDoNotChangeIssueIdentityOrFindingSemantics()
    {
        var firstTask = Element(
            "element:task",
            BpmnSemanticTypes.Task,
            "First name",
            "FIRST_CODE",
            17);
        var changedTask = Element(
            "element:task",
            BpmnSemanticTypes.Task,
            "Changed name",
            "CHANGED_CODE",
            17);

        var first = IsolatedIssue(firstTask);
        var changed = IsolatedIssue(changedTask);

        Assert.Equal(first.Id, changed.Id);
        Assert.Equal(first.Code, changed.Code);
        Assert.Equal(first.Severity, changed.Severity);
        Assert.Equal(first.Target, changed.Target);
        Assert.NotEqual(first.Message, changed.Message);
    }

    [Fact]
    public void ReadablePropertiesDoNotChangeIssueCountCodesSeveritiesOrTargets()
    {
        var anonymous = Element(
            "element:task",
            BpmnSemanticTypes.Task,
            elementNumber: 17);
        var readable = Element(
            "element:task",
            BpmnSemanticTypes.Task,
            "Readable",
            "READABLE",
            17);

        var before = IsolatedIssues(anonymous);
        var after = IsolatedIssues(readable);

        Assert.Equal(before.Length, after.Length);
        Assert.Equal(before.Select(static issue => issue.Code),
            after.Select(static issue => issue.Code));
        Assert.Equal(before.Select(static issue => issue.Severity),
            after.Select(static issue => issue.Severity));
        Assert.Equal(before.Select(static issue => issue.Target),
            after.Select(static issue => issue.Target));
    }

    [Fact]
    public void MessageRemainsPlainTextForHtmlSensitiveNameAndCode()
    {
        var task = Element(
            "element:task-with-a-deliberately-long-canonical-identifier",
            BpmnSemanticTypes.Task,
            "A < B & C",
            "TEST_\"A\"",
            17);

        var issue = IsolatedIssue(task);

        Assert.Equal(
            "Task 17 \"A < B & C\" " +
            "[Code: TEST_\"A\", ID: " +
            "element:task-with-a-deliberately-long-canonical-identifier] " +
            "is isolated from every Sequence Flow.",
            issue.Message);
        Assert.DoesNotContain("<span", issue.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("&lt;", issue.Message, StringComparison.Ordinal);
    }

    public static TheoryData<SemanticTypeId, string, string> FlowNodeDisplayNames => new()
    {
        { BpmnSemanticTypes.StartEvent, "Start Event", BpmnModelValidationCodes.StartEventNoOutgoing },
        { BpmnSemanticTypes.EndEvent, "End Event", BpmnModelValidationCodes.EndEventNoIncoming },
        { BpmnSemanticTypes.Task, "Task 23", BpmnModelValidationCodes.FlowNodeIsolated },
        { BpmnSemanticTypes.ExclusiveGateway, "Exclusive Gateway", BpmnModelValidationCodes.GatewayNoIncoming },
        { BpmnSemanticTypes.ParallelGateway, "Parallel Gateway", BpmnModelValidationCodes.GatewayNoIncoming },
        { BpmnSemanticTypes.InclusiveGateway, "Inclusive Gateway", BpmnModelValidationCodes.GatewayNoIncoming },
        { BpmnSemanticTypes.EventBasedGateway, "Event-Based Gateway", BpmnModelValidationCodes.GatewayNoIncoming },
        { BpmnSemanticTypes.MessageCatchEvent, "Message Catch Event", BpmnModelValidationCodes.FlowNodeIsolated },
        { BpmnSemanticTypes.TimerCatchEvent, "Timer Catch Event", BpmnModelValidationCodes.FlowNodeIsolated },
    };

    private static ModelValidationIssue IsolatedIssue(SemanticElementSnapshot element) =>
        Assert.Single(IsolatedIssues(element), issue =>
            issue.Code == BpmnModelValidationCodes.FlowNodeIsolated);

    private static System.Collections.Immutable.ImmutableArray<ModelValidationIssue>
        IsolatedIssues(SemanticElementSnapshot element) => ValidateWithValidStartEnd(element);

    private static System.Collections.Immutable.ImmutableArray<ModelValidationIssue>
        ValidateWithValidStartEnd(SemanticElementSnapshot element)
    {
        var start = Element("element:start", BpmnSemanticTypes.StartEvent);
        var end = Element("element:end", BpmnSemanticTypes.EndEvent);
        return Validate(
            [start, element, end],
            [Flow("flow:start-end", start, end)]);
    }

    private static System.Collections.Immutable.ImmutableArray<ModelValidationIssue> Validate(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot>? flows = null)
    {
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(DocumentId, Revision, elements, flows),
            new VisualModelSnapshot(DocumentId, Revision),
            new DocumentMetadataSnapshot(DocumentId, Revision));
        return new ModelValidationEngine(new ModelValidationCatalog(
            [new BpmnStructuralValidationRule()]))
            .Validate(new ModelValidationContext(snapshot))
            .Issues;
    }

    private static SemanticElementSnapshot Element(
        string id,
        SemanticTypeId typeId,
        string? name = null,
        string? code = null,
        long? elementNumber = null)
    {
        var properties = new List<KeyValuePair<string, PropertyValue>>();
        if (name is not null)
        {
            properties.Add(new(
                BpmnSemanticProperties.Name,
                PropertyValue.FromText(name)));
        }

        if (code is not null)
        {
            properties.Add(new(
                BpmnSemanticProperties.Code,
                PropertyValue.FromText(code)));
        }

        if (elementNumber is not null)
        {
            properties.Add(new(
                BpmnSemanticProperties.ElementNumber,
                PropertyValue.FromInteger(elementNumber.Value)));
        }

        return new SemanticElementSnapshot(new SemanticElementId(id), typeId, properties);
    }

    private static SemanticRelationshipSnapshot Flow(
        string id,
        SemanticElementSnapshot source,
        SemanticElementSnapshot target) => new(
            new SemanticElementId(id),
            BpmnSemanticTypes.SequenceFlow,
            source.Id,
            target.Id);

    private static bool IsGateway(SemanticTypeId typeId) =>
        typeId == BpmnSemanticTypes.ExclusiveGateway ||
        typeId == BpmnSemanticTypes.ParallelGateway ||
        typeId == BpmnSemanticTypes.InclusiveGateway ||
        typeId == BpmnSemanticTypes.EventBasedGateway;
}
