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

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN51HumanReadableValidationIntegrationTests
{
    private static readonly DocumentId DocumentId = new("n51:integration");
    private static readonly DocumentRevision Revision = new(4);

    [Fact]
    public void ValidSimpleBpmnRemainsIssueFree()
    {
        var start = BpmnSemanticFactory.CreateStartEvent(
            new SemanticElementId("element:start"),
            "Order received");
        var task = BpmnSemanticFactory.CreateTask(
            new SemanticElementId("element:task"),
            "REVIEW_ORDER",
            "Review order",
            17);
        var end = BpmnSemanticFactory.CreateEndEvent(
            new SemanticElementId("element:end"),
            "Order completed");

        var issues = Validate(
            [start, task, end],
            [
                BpmnSemanticFactory.CreateSequenceFlow(
                    new SemanticElementId("flow:start-task"), start.Id, task.Id),
                BpmnSemanticFactory.CreateSequenceFlow(
                    new SemanticElementId("flow:task-end"), task.Id, end.Id),
            ]);

        Assert.Empty(issues);
    }

    [Fact]
    public void DuplicateNamesAndEmptyNameRemainUnambiguouslyIdentified()
    {
        var start = BpmnSemanticFactory.CreateStartEvent(
            new SemanticElementId("element:start"));
        var end = BpmnSemanticFactory.CreateEndEvent(
            new SemanticElementId("element:end"));
        var first = BpmnSemanticFactory.CreateTask(
            new SemanticElementId("element:review-a"),
            "REVIEW_A",
            "Review order",
            17);
        var second = BpmnSemanticFactory.CreateTask(
            new SemanticElementId("element:review-b"),
            "REVIEW_B",
            "Review order",
            18);
        var emptyName = new SemanticElementSnapshot(
            new SemanticElementId("element:review-empty"),
            BpmnSemanticTypes.Task,
            [
                new(BpmnSemanticProperties.Code,
                    PropertyValue.FromText("REVIEW_EMPTY")),
                new(BpmnSemanticProperties.Name,
                    PropertyValue.FromText(string.Empty)),
                new(BpmnSemanticProperties.ElementNumber,
                    PropertyValue.FromInteger(19)),
            ]);

        var issues = Validate(
            [start, end, first, second, emptyName],
            [BpmnSemanticFactory.CreateSequenceFlow(
                new SemanticElementId("flow:start-end"), start.Id, end.Id)]);
        var isolated = issues.Where(issue =>
            issue.Code == BpmnModelValidationCodes.FlowNodeIsolated).ToArray();

        Assert.Equal(3, isolated.Length);
        Assert.Contains(isolated, issue => issue.Message.StartsWith(
            "Task 17 \"Review order\" [Code: REVIEW_A, ID: element:review-a]",
            StringComparison.Ordinal));
        Assert.Contains(isolated, issue => issue.Message.StartsWith(
            "Task 18 \"Review order\" [Code: REVIEW_B, ID: element:review-b]",
            StringComparison.Ordinal));
        Assert.Contains(isolated, issue => issue.Message.StartsWith(
            "Task 19 [Code: REVIEW_EMPTY, ID: element:review-empty]",
            StringComparison.Ordinal));
        Assert.DoesNotContain(isolated, issue => issue.Message.Contains("\"\"",
            StringComparison.Ordinal));
    }

    private static System.Collections.Immutable.ImmutableArray<ModelValidationIssue> Validate(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot> flows)
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
}
