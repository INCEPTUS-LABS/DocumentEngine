using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Validation;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN80ProcessScopeTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n8:document");
    private static readonly DocumentScopeId RootScopeId = new(DocumentId.Value);
    private static readonly DocumentScopeId NestedScopeId = new("bpmn:n8:scope:nested");

    private static readonly SemanticElementId RootSourceId = new("bpmn:n8:root-source");
    private static readonly SemanticElementId RootTargetId = new("bpmn:n8:root-target");
    private static readonly SemanticElementId NestedSourceId = new("bpmn:n8:nested-source");
    private static readonly SemanticElementId NestedTargetId = new("bpmn:n8:nested-target");
    private static readonly SemanticElementId RootFlowId = new("bpmn:n8:root-flow");
    private static readonly SemanticElementId NestedFlowId = new("bpmn:n8:nested-flow");
    private static readonly SemanticElementId CrossScopeFlowId =
        new("bpmn:n8:cross-scope-flow");

    private static readonly VisualStateId RootSourceVisualId =
        new("bpmn:n8:root-source:visual");
    private static readonly VisualStateId RootTargetVisualId =
        new("bpmn:n8:root-target:visual");
    private static readonly VisualStateId NestedSourceVisualId =
        new("bpmn:n8:nested-source:visual");
    private static readonly VisualStateId NestedTargetVisualId =
        new("bpmn:n8:nested-target:visual");
    private static readonly VisualStateId RootFlowVisualId =
        new("bpmn:n8:root-flow:visual");
    private static readonly VisualStateId NestedFlowVisualId =
        new("bpmn:n8:nested-flow:visual");
    private static readonly VisualStateId CrossScopeFlowVisualId =
        new("bpmn:n8:cross-scope-flow:visual");

    private static readonly ConnectorAnchorId RootSourceSourceAnchorId =
        new("bpmn:n8:root-source:source-anchor");
    private static readonly ConnectorAnchorId RootSourceTargetAnchorId =
        new("bpmn:n8:root-source:target-anchor");
    private static readonly ConnectorAnchorId RootTargetSourceAnchorId =
        new("bpmn:n8:root-target:source-anchor");
    private static readonly ConnectorAnchorId RootTargetTargetAnchorId =
        new("bpmn:n8:root-target:target-anchor");
    private static readonly ConnectorAnchorId NestedSourceSourceAnchorId =
        new("bpmn:n8:nested-source:source-anchor");
    private static readonly ConnectorAnchorId NestedSourceTargetAnchorId =
        new("bpmn:n8:nested-source:target-anchor");
    private static readonly ConnectorAnchorId NestedTargetSourceAnchorId =
        new("bpmn:n8:nested-target:source-anchor");
    private static readonly ConnectorAnchorId NestedTargetTargetAnchorId =
        new("bpmn:n8:nested-target:target-anchor");

    [Fact]
    public void SameScopeSequenceFlowsAreValidAtRootAndInANestedScope()
    {
        var rootFlow = Flow(RootFlowId, RootSourceId, RootTargetId);
        var nestedFlow = Flow(NestedFlowId, NestedSourceId, NestedTargetId);
        var snapshot = CreateValidationSnapshot([rootFlow, nestedFlow]);

        var issues = Validate(snapshot);

        Assert.DoesNotContain(issues, issue =>
            issue.Code == BpmnModelValidationCodes.SequenceFlowCrossesScope);
        Assert.Equal(RootScopeId, snapshot.SemanticModel.GetScope(rootFlow.Id).Id);
        Assert.Equal(NestedScopeId, snapshot.SemanticModel.GetScope(nestedFlow.Id).Id);
    }

    [Fact]
    public void CrossScopeSequenceFlowProducesOneReadableN5Error()
    {
        var flow = BpmnSemanticFactory.CreateSequenceFlow(
            CrossScopeFlowId,
            RootSourceId,
            NestedTargetId,
            "Cross Scope");
        var snapshot = CreateValidationSnapshot([flow]);

        var issue = Assert.Single(Validate(snapshot), candidate =>
            candidate.Code == BpmnModelValidationCodes.SequenceFlowCrossesScope);

        Assert.Equal(ModelValidationSeverity.Error, issue.Severity);
        Assert.Equal(flow.Id, issue.Target.SemanticElementId);
        Assert.Contains(
            "Sequence Flow \"Cross Scope\" [ID: bpmn:n8:cross-scope-flow]",
            issue.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "Task 1 \"Root Source\" [Code: RS, ID: bpmn:n8:root-source]",
            issue.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "Task 4 \"Nested Target\" [Code: NT, ID: bpmn:n8:nested-target]",
            issue.Message,
            StringComparison.Ordinal);
        Assert.Contains("across process scopes", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossScopeCreationIsRejectedAtomicallyByTheSharedBpmnRule()
    {
        var harness = CreateHarness();
        var before = harness.Document.CaptureSnapshot();
        var command = CreateFlowCommand(
            CrossScopeFlowId,
            CrossScopeFlowVisualId,
            RootSourceId,
            NestedTargetId,
            RootSourceSourceAnchorId,
            NestedTargetTargetAnchorId,
            harness.Document.Revision);

        var result = await harness.History.ExecuteAsync(harness.Processor, command);

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.SequenceFlowCrossesScope);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
    }

    [Theory]
    [InlineData("shared-source")]
    [InlineData("shared-target")]
    public async Task ImportedCrossScopeFlowSharingAnEndpointDoesNotRejectAValidCandidate(
        string scenario)
    {
        var harness = CreateHarness(includeImportedCrossFlow: true);
        var before = harness.Document.CaptureSnapshot();
        var command = scenario == "shared-source"
            ? CreateFlowCommand(
                RootFlowId,
                RootFlowVisualId,
                RootSourceId,
                RootTargetId,
                RootSourceSourceAnchorId,
                RootTargetTargetAnchorId,
                harness.Document.Revision)
            : CreateFlowCommand(
                NestedFlowId,
                NestedFlowVisualId,
                NestedSourceId,
                NestedTargetId,
                NestedSourceSourceAnchorId,
                NestedTargetTargetAnchorId,
                harness.Document.Revision);

        var result = await harness.History.ExecuteAsync(harness.Processor, command);

        Assert.True(result.IsCommitted);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.SequenceFlowCrossesScope);
        var committed = harness.Document.CaptureSnapshot();
        Assert.True(committed.SemanticModel.TryGetRelationship(
            command.RelationshipId,
            out _));
        Assert.True(committed.SemanticModel.TryGetRelationship(
            CrossScopeFlowId,
            out _));
        Assert.Equal(
            scenario == "shared-source" ? RootScopeId : NestedScopeId,
            committed.SemanticModel.GetScope(command.RelationshipId).Id);
        AssertContainmentEqual(before.SemanticModel, committed.SemanticModel);
    }

    [Fact]
    public async Task CrossScopeReconnectIsRejectedWithTheOriginalDocumentAndHistoryExact()
    {
        var harness = CreateHarness(includeRootFlow: true);
        var before = harness.Document.CaptureSnapshot();
        var command = new ReconnectBpmnSequenceFlowEndpointCommand(
            DocumentId,
            harness.Document.Revision,
            RootFlowId,
            RootFlowVisualId,
            ConnectorEndpointKind.Target,
            RootTargetId,
            RootTargetTargetAnchorId,
            NestedTargetId,
            NestedTargetTargetAnchorId);

        var result = await harness.History.ExecuteAsync(harness.Processor, command);

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.SequenceFlowCrossesScope);
        Assert.Equal(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task NestedSequenceFlowCreationAndHistoryPreserveContainmentExactly()
    {
        var harness = CreateHarness();
        var before = harness.Document.CaptureSnapshot();
        var command = CreateFlowCommand(
            NestedFlowId,
            NestedFlowVisualId,
            NestedSourceId,
            NestedTargetId,
            NestedSourceSourceAnchorId,
            NestedTargetTargetAnchorId,
            harness.Document.Revision);

        Assert.True((await harness.History.ExecuteAsync(
            harness.Processor,
            command)).IsCommitted);
        var committed = harness.Document.CaptureSnapshot();
        AssertContainmentEqual(before.SemanticModel, committed.SemanticModel);
        Assert.Equal(NestedScopeId, committed.SemanticModel.GetScope(NestedFlowId).Id);

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        var undone = harness.Document.CaptureSnapshot();
        AssertContainmentEqual(before.SemanticModel, undone.SemanticModel);
        Assert.False(undone.SemanticModel.TryGetRelationship(NestedFlowId, out _));

        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        var redone = harness.Document.CaptureSnapshot();
        AssertContainmentEqual(before.SemanticModel, redone.SemanticModel);
        Assert.Equal(NestedScopeId, redone.SemanticModel.GetScope(NestedFlowId).Id);
    }

    [Fact]
    public async Task FlowNodeDeletionRemovesOnlyItsMembershipAndHistoryRestoresItExactly()
    {
        var harness = CreateHarness();
        var before = harness.Document.CaptureSnapshot();
        var command = new DeleteBpmnFlowNodeCommand(
            DocumentId,
            harness.Document.Revision,
            NestedSourceId,
            NestedSourceVisualId);

        Assert.True((await harness.History.ExecuteAsync(
            harness.Processor,
            command)).IsCommitted);
        var committed = harness.Document.CaptureSnapshot();
        Assert.Equal(
            before.SemanticModel.NestedScopes.AsEnumerable(),
            committed.SemanticModel.NestedScopes.AsEnumerable());
        Assert.DoesNotContain(committed.SemanticModel.ScopeMemberships, membership =>
            membership.SemanticElementId == NestedSourceId);
        Assert.Contains(committed.SemanticModel.ScopeMemberships, membership =>
            membership.SemanticElementId == NestedTargetId &&
            membership.ScopeId == NestedScopeId);

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        AssertContainmentEqual(
            before.SemanticModel,
            harness.Document.CaptureSnapshot().SemanticModel);

        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        Assert.DoesNotContain(
            harness.Document.SemanticModel.ScopeMemberships,
            membership => membership.SemanticElementId == NestedSourceId);
    }

    [Fact]
    public void CreationHistoryRejectsAnOtherwiseCountExactContainmentMutation()
    {
        var before = CreateBaseSnapshot();
        var command = CreateFlowCommand(
            RootFlowId,
            RootFlowVisualId,
            RootSourceId,
            RootTargetId,
            RootSourceSourceAnchorId,
            RootTargetTargetAnchorId,
            before.Revision);
        var relationship = Flow(RootFlowId, RootSourceId, RootTargetId);
        var committed = new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                before.Revision,
                before.SemanticModel.Elements,
                before.SemanticModel.Relationships.Append(relationship),
                before.SemanticModel.NestedScopes,
                before.SemanticModel.ScopeMemberships.Append(
                    new SemanticElementScopeMembershipSnapshot(
                        RootTargetId,
                        NestedScopeId))),
            new VisualModelSnapshot(
                DocumentId,
                before.Revision,
                before.VisualModel.VisualStates.Append(ConnectorVisual(
                    RootFlowVisualId,
                    RootFlowId,
                    RootSourceSourceAnchorId,
                    RootTargetTargetAnchorId))),
            before.Metadata);
        var policy = BpmnPluginRegistration.N7.HistoryPolicies.Single(registration =>
            registration.TypeId == CreateBpmnSequenceFlowCommand.KnownTypeId).Policy;

        var result = policy.Prepare(command, before, committed);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.HistoryInvalid);
    }

    private static CreateBpmnSequenceFlowCommand CreateFlowCommand(
        SemanticElementId relationshipId,
        VisualStateId visualStateId,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId,
        DocumentRevision revision) =>
        new(
            DocumentId,
            revision,
            relationshipId,
            visualStateId,
            sourceId,
            targetId,
            sourceAnchorId,
            targetAnchorId);

    private static Harness CreateHarness(
        bool includeRootFlow = false,
        bool includeImportedCrossFlow = false)
    {
        var registration = BpmnPluginRegistration.N7;
        var policyProvider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.Create(
            CreateBaseSnapshot(includeRootFlow, includeImportedCrossFlow),
            policyProvider);
        var document = Assert.IsType<Document>(construction.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: policyProvider);
        return new Harness(document, processor, new HistoryManager(document));
    }

    private static DocumentSnapshot CreateBaseSnapshot(
        bool includeRootFlow = false,
        bool includeImportedCrossFlow = false)
    {
        var revision = DocumentRevision.Zero;
        var relationships = new List<SemanticRelationshipSnapshot>();
        if (includeRootFlow)
        {
            relationships.Add(Flow(RootFlowId, RootSourceId, RootTargetId));
        }

        if (includeImportedCrossFlow)
        {
            relationships.Add(Flow(
                CrossScopeFlowId,
                RootSourceId,
                NestedTargetId));
        }

        var visuals = NodeVisuals().ToList();
        if (includeRootFlow)
        {
            visuals.Add(ConnectorVisual(
                RootFlowVisualId,
                RootFlowId,
                RootSourceSourceAnchorId,
                RootTargetTargetAnchorId));
        }

        return new DocumentSnapshot(
            CreateSemanticModel(revision, relationships),
            new VisualModelSnapshot(DocumentId, revision, visuals),
            new DocumentMetadataSnapshot(DocumentId, revision));
    }

    private static DocumentSnapshot CreateValidationSnapshot(
        IEnumerable<SemanticRelationshipSnapshot> relationships)
    {
        var revision = new DocumentRevision(8);
        return new DocumentSnapshot(
            CreateSemanticModel(revision, relationships),
            new VisualModelSnapshot(DocumentId, revision),
            new DocumentMetadataSnapshot(DocumentId, revision));
    }

    private static SemanticModelSnapshot CreateSemanticModel(
        DocumentRevision revision,
        IEnumerable<SemanticRelationshipSnapshot> relationships) =>
        new(
            DocumentId,
            revision,
            Elements(),
            relationships,
            [new DocumentScopeSnapshot(NestedScopeId, RootScopeId)],
            [
                new SemanticElementScopeMembershipSnapshot(
                    NestedSourceId,
                    NestedScopeId),
                new SemanticElementScopeMembershipSnapshot(
                    NestedTargetId,
                    NestedScopeId),
            ]);

    private static SemanticElementSnapshot[] Elements() =>
    [
        BpmnSemanticFactory.CreateTask(RootSourceId, "RS", "Root Source", 1),
        BpmnSemanticFactory.CreateTask(RootTargetId, "RT", "Root Target", 2),
        BpmnSemanticFactory.CreateTask(NestedSourceId, "NS", "Nested Source", 3),
        BpmnSemanticFactory.CreateTask(NestedTargetId, "NT", "Nested Target", 4),
    ];

    private static IEnumerable<VisualStateSnapshot> NodeVisuals()
    {
        yield return NodeVisual(
            RootSourceVisualId,
            RootSourceId,
            20d,
            RootSourceSourceAnchorId,
            RootSourceTargetAnchorId);
        yield return NodeVisual(
            RootTargetVisualId,
            RootTargetId,
            220d,
            RootTargetSourceAnchorId,
            RootTargetTargetAnchorId);
        yield return NodeVisual(
            NestedSourceVisualId,
            NestedSourceId,
            20d,
            NestedSourceSourceAnchorId,
            NestedSourceTargetAnchorId);
        yield return NodeVisual(
            NestedTargetVisualId,
            NestedTargetId,
            220d,
            NestedTargetSourceAnchorId,
            NestedTargetTargetAnchorId);
    }

    private static VisualStateSnapshot NodeVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        double x,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId) =>
        new(
            visualStateId,
            semanticElementId,
            new PointD(x, 40d),
            new SizeD(120d, 80d),
            VisualPlacementMode.Pinned,
            connectorAnchors:
            [
                new ConnectorAnchor(
                    sourceAnchorId,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source,
                    0),
                new ConnectorAnchor(
                    targetAnchorId,
                    ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target,
                    0),
            ]);

    private static VisualStateSnapshot ConnectorVisual(
        VisualStateId visualStateId,
        SemanticElementId relationshipId,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId) =>
        new(
            visualStateId,
            relationshipId,
            new PointD(0d, 0d),
            new SizeD(0d, 0d),
            VisualPlacementMode.Manual,
            sourceAnchorId: sourceAnchorId,
            targetAnchorId: targetAnchorId);

    private static SemanticRelationshipSnapshot Flow(
        SemanticElementId id,
        SemanticElementId sourceId,
        SemanticElementId targetId) =>
        BpmnSemanticFactory.CreateSequenceFlow(id, sourceId, targetId);

    private static System.Collections.Immutable.ImmutableArray<ModelValidationIssue> Validate(
        DocumentSnapshot snapshot) =>
        new ModelValidationEngine(new ModelValidationCatalog(
            [new BpmnStructuralValidationRule()]))
            .Validate(new ModelValidationContext(snapshot))
            .Issues;

    private static void AssertContainmentEqual(
        SemanticModelSnapshot expected,
        SemanticModelSnapshot actual)
    {
        Assert.Equal(expected.RootScopeId, actual.RootScopeId);
        Assert.Equal(
            expected.NestedScopes.AsEnumerable(),
            actual.NestedScopes.AsEnumerable());
        Assert.Equal(
            expected.ScopeMemberships.AsEnumerable(),
            actual.ScopeMemberships.AsEnumerable());
    }

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History);
}
