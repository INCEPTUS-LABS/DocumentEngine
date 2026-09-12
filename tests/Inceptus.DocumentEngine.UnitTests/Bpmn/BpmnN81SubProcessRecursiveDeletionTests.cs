using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN81SubProcessRecursiveDeletionTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n8.1:deletion-document");
    private static readonly DocumentScopeId RootScopeId = new(DocumentId.Value);
    private static readonly DocumentScopeId ScopeAId = new("bpmn:n8.1:scope-a");
    private static readonly DocumentScopeId ScopeBId = new("bpmn:n8.1:scope-b");

    private static readonly SemanticElementId SubProcessAId =
        new("bpmn:n8.1:subprocess-a");
    private static readonly SemanticElementId SubProcessBId =
        new("bpmn:n8.1:subprocess-b");
    private static readonly SemanticElementId KeepId = new("bpmn:n8.1:keep");
    private static readonly SemanticElementId TaskAId = new("bpmn:n8.1:task-a");
    private static readonly SemanticElementId TaskBId = new("bpmn:n8.1:task-b");
    private static readonly SemanticElementId EndBId = new("bpmn:n8.1:end-b");

    private static readonly VisualStateId SubProcessAVisualId =
        new("bpmn:n8.1:subprocess-a:visual");

    [Fact]
    public async Task DeletingParentRecursivelyRemovesTheOwnedNestedSubtreeAndUndoRedoIsExact()
    {
        var before = PopulatedNestedSnapshot();
        var harness = CreateHarness(before);
        var command = new DeleteBpmnFlowNodeCommand(
            DocumentId,
            harness.Document.Revision,
            SubProcessAId,
            SubProcessAVisualId);

        var result = await harness.History.ExecuteAsync(harness.Processor, command);

        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var deleted = harness.Document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), deleted.Revision);
        Assert.Equal([KeepId],
            deleted.SemanticModel.Elements.Select(static element => element.Id));
        Assert.Empty(deleted.SemanticModel.Relationships);
        Assert.Empty(deleted.SemanticModel.NestedScopes);
        Assert.Empty(deleted.SemanticModel.ScopeMemberships);
        Assert.Equal([new VisualStateId("bpmn:n8.1:keep:visual")],
            deleted.VisualModel.VisualStates.Select(static visual => visual.Id));
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);

        var undo = await harness.History.UndoAsync(harness.Processor);

        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        var restored = harness.Document.CaptureSnapshot();
        AssertAuthoritativeContentEqual(before, restored);
        Assert.Equal(ScopeAId, restored.SemanticModel.GetScope(SubProcessBId).Id);
        Assert.Equal(ScopeBId, restored.SemanticModel.GetScope(TaskBId).Id);
        var restoredScopeA = Assert.Single(restored.SemanticModel.NestedScopes, scope =>
            scope.Id == ScopeAId);
        Assert.Equal(RootScopeId, restoredScopeA.ParentScopeId);
        Assert.Equal(SubProcessAId, restoredScopeA.OwnerSemanticElementId);
        var restoredScopeB = Assert.Single(restored.SemanticModel.NestedScopes, scope =>
            scope.Id == ScopeBId);
        Assert.Equal(ScopeAId, restoredScopeB.ParentScopeId);
        Assert.Equal(SubProcessBId, restoredScopeB.OwnerSemanticElementId);

        var redo = await harness.History.RedoAsync(harness.Processor);

        Assert.True(redo.IsCommitted, Diagnostics(redo.Diagnostics));
        var redeleted = harness.Document.CaptureSnapshot();
        AssertAuthoritativeContentEqual(deleted, redeleted);
    }

    [Fact]
    public void ReconstructionPreservesEveryNestedSubProcessIdentityAndMembership()
    {
        var snapshot = PopulatedNestedSnapshot();
        var registration = BpmnPluginRegistration.N81;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);

        var result = DocumentReconstructor.Reconstruct(snapshot, provider);

        Assert.True(result.Succeeded, Diagnostics(result.Diagnostics));
        Assert.Empty(result.Diagnostics);
        var document = Assert.IsType<Document>(result.Document);
        AssertAuthoritativeContentEqual(snapshot, document.CaptureSnapshot());
    }

    private static Harness CreateHarness(DocumentSnapshot snapshot)
    {
        var registration = BpmnPluginRegistration.N81;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentReconstructor.Reconstruct(snapshot, provider);
        Assert.True(construction.Succeeded, Diagnostics(construction.Diagnostics));
        var document = Assert.IsType<Document>(construction.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: provider);
        return new Harness(document, processor, new HistoryManager(document));
    }

    private static DocumentSnapshot PopulatedNestedSnapshot()
    {
        var revision = new DocumentRevision(11);
        var rootFlowId = new SemanticElementId("bpmn:n8.1:flow:keep-a");
        var scopeAFlowId = new SemanticElementId("bpmn:n8.1:flow:b-task-a");
        var scopeBFlowId = new SemanticElementId("bpmn:n8.1:flow:task-b-end-b");
        var keepSourceAnchor = new ConnectorAnchorId("bpmn:n8.1:keep:source");
        var aTargetAnchor = new ConnectorAnchorId("bpmn:n8.1:a:target");
        var bSourceAnchor = new ConnectorAnchorId("bpmn:n8.1:b:source");
        var taskATargetAnchor = new ConnectorAnchorId("bpmn:n8.1:task-a:target");
        var taskBSourceAnchor = new ConnectorAnchorId("bpmn:n8.1:task-b:source");
        var endBTargetAnchor = new ConnectorAnchorId("bpmn:n8.1:end-b:target");
        var elements = new[]
        {
            BpmnSemanticFactory.CreateSubProcess(
                SubProcessAId,
                "SUBPROCESS_A",
                "SubProcess A",
                "Owns Scope A."),
            BpmnSemanticFactory.CreateTask(KeepId, "KEEP", "Keep", 1),
            BpmnSemanticFactory.CreateSubProcess(
                SubProcessBId,
                "SUBPROCESS_B",
                "SubProcess B",
                "Owns Scope B."),
            BpmnSemanticFactory.CreateTask(TaskAId, "TASK_A", "Task A", 2),
            BpmnSemanticFactory.CreateTask(
                TaskBId,
                BpmnSemanticTypes.ServiceTask,
                "TASK_B",
                "Task B",
                3,
                "Nested service task."),
            BpmnSemanticFactory.CreateEndEvent(EndBId, "End B", "Nested end."),
        };
        var relationships = new[]
        {
            BpmnSemanticFactory.CreateSequenceFlow(
                rootFlowId,
                KeepId,
                SubProcessAId,
                "Enter A",
                "Root incident flow."),
            BpmnSemanticFactory.CreateSequenceFlow(
                scopeAFlowId,
                SubProcessBId,
                TaskAId,
                "Leave B"),
            BpmnSemanticFactory.CreateSequenceFlow(
                scopeBFlowId,
                TaskBId,
                EndBId,
                "Finish B"),
        };
        var scopes = new[]
        {
            new DocumentScopeSnapshot(ScopeAId, RootScopeId, SubProcessAId),
            new DocumentScopeSnapshot(ScopeBId, ScopeAId, SubProcessBId),
        };
        var memberships = new[]
        {
            new SemanticElementScopeMembershipSnapshot(SubProcessBId, ScopeAId),
            new SemanticElementScopeMembershipSnapshot(TaskAId, ScopeAId),
            new SemanticElementScopeMembershipSnapshot(TaskBId, ScopeBId),
            new SemanticElementScopeMembershipSnapshot(EndBId, ScopeBId),
        };
        var visuals = new[]
        {
            NodeVisual(
                new VisualStateId("bpmn:n8.1:keep:visual"),
                KeepId,
                new PointD(20d, 40d),
                new ConnectorAnchor(
                    keepSourceAnchor,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source,
                    0)),
            NodeVisual(
                SubProcessAVisualId,
                SubProcessAId,
                new PointD(220d, 40d),
                new ConnectorAnchor(
                    aTargetAnchor,
                    ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target,
                    0)),
            NodeVisual(
                new VisualStateId("bpmn:n8.1:subprocess-b:visual"),
                SubProcessBId,
                new PointD(30d, 50d),
                new ConnectorAnchor(
                    bSourceAnchor,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source,
                    0)),
            NodeVisual(
                new VisualStateId("bpmn:n8.1:task-a:visual"),
                TaskAId,
                new PointD(230d, 50d),
                new ConnectorAnchor(
                    taskATargetAnchor,
                    ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target,
                    0)),
            NodeVisual(
                new VisualStateId("bpmn:n8.1:task-b:visual"),
                TaskBId,
                new PointD(40d, 60d),
                new ConnectorAnchor(
                    taskBSourceAnchor,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source,
                    0)),
            NodeVisual(
                new VisualStateId("bpmn:n8.1:end-b:visual"),
                EndBId,
                new PointD(260d, 70d),
                new ConnectorAnchor(
                    endBTargetAnchor,
                    ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target,
                    0),
                size: new SizeD(36d, 36d)),
            ConnectorVisual(
                new VisualStateId("bpmn:n8.1:flow:keep-a:visual"),
                rootFlowId,
                keepSourceAnchor,
                aTargetAnchor,
                [new PointD(140d, 80d), new PointD(220d, 80d)]),
            ConnectorVisual(
                new VisualStateId("bpmn:n8.1:flow:b-task-a:visual"),
                scopeAFlowId,
                bSourceAnchor,
                taskATargetAnchor,
                [new PointD(150d, 90d), new PointD(230d, 90d)]),
            ConnectorVisual(
                new VisualStateId("bpmn:n8.1:flow:task-b-end-b:visual"),
                scopeBFlowId,
                taskBSourceAnchor,
                endBTargetAnchor,
                [new PointD(160d, 100d), new PointD(260d, 88d)]),
        };
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                elements,
                relationships,
                scopes,
                memberships),
            new VisualModelSnapshot(DocumentId, revision, visuals),
            new DocumentMetadataSnapshot(DocumentId, revision));
    }

    private static VisualStateSnapshot NodeVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        PointD position,
        ConnectorAnchor connectorAnchor,
        SizeD? size = null) =>
        new(
            visualStateId,
            semanticElementId,
            position,
            size ?? new SizeD(120d, 80d),
            VisualPlacementMode.Pinned,
            connectorAnchors: [connectorAnchor]);

    private static VisualStateSnapshot ConnectorVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId,
        IEnumerable<PointD> route) =>
        new(
            visualStateId,
            semanticElementId,
            new PointD(0d, 0d),
            new SizeD(0d, 0d),
            VisualPlacementMode.Manual,
            route,
            sourceAnchorId: sourceAnchorId,
            targetAnchorId: targetAnchorId);

    private static void AssertAuthoritativeContentEqual(
        DocumentSnapshot expected,
        DocumentSnapshot actual)
    {
        Assert.Equal(expected.SemanticModel.Elements.AsEnumerable(),
            actual.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(expected.SemanticModel.Relationships.AsEnumerable(),
            actual.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(expected.SemanticModel.NestedScopes.AsEnumerable(),
            actual.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(expected.SemanticModel.ScopeMemberships.AsEnumerable(),
            actual.SemanticModel.ScopeMemberships.AsEnumerable());
        Assert.Equal(expected.VisualModel.VisualStates.AsEnumerable(),
            actual.VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(expected.Metadata.SystemManagedProperties,
            actual.Metadata.SystemManagedProperties);
        Assert.Equal(expected.Metadata.ExtensionProperties,
            actual.Metadata.ExtensionProperties);
    }

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History);
}
