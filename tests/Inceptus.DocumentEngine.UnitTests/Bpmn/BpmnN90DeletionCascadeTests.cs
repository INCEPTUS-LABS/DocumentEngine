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

public sealed class BpmnN90DeletionCascadeTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n9:deletion-matrix");
    private static readonly SemanticElementId OwnerId = new("bpmn:n9:owner");
    private static readonly SemanticElementId BoundaryId = new("bpmn:n9:boundary");
    private static readonly SemanticElementId KeepId = new("bpmn:n9:keep");
    private static readonly SemanticElementId FlowId = new("bpmn:n9:boundary-flow");
    private static readonly VisualStateId OwnerVisualId = new("bpmn:n9:owner:visual");
    private static readonly VisualStateId BoundaryVisualId = new("bpmn:n9:boundary:visual");
    private static readonly ConnectorAnchorId BoundarySourceAnchorId =
        new("bpmn:n9:boundary:source");
    private static readonly ConnectorAnchorId KeepTargetAnchorId =
        new("bpmn:n9:keep:target");
    private static readonly BoundaryAttachmentPlacement BottomCenter =
        new(BoundaryAttachmentSide.Bottom, 0.5d);

    public static IEnumerable<object[]> ActivityTypes() =>
        BpmnActivitySemanticTypes.All.Select(static typeId => new object[] { typeId });

    [Theory]
    [MemberData(nameof(ActivityTypes))]
    public async Task DeletingEveryActivityFamilyCascadesItsBoundaryAndOutgoingFlowAtomically(
        SemanticTypeId activityTypeId)
    {
        var before = ActivitySnapshot(activityTypeId);
        var harness = CreateHarness(before);

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            new DeleteBpmnFlowNodeCommand(
                DocumentId,
                harness.Document.Revision,
                OwnerId,
                OwnerVisualId));

        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var deleted = harness.Document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), deleted.Revision);
        Assert.Equal([KeepId],
            deleted.SemanticModel.Elements.Select(static element => element.Id));
        Assert.Empty(deleted.SemanticModel.Relationships);
        Assert.Equal(
            [new VisualStateId("bpmn:n9:keep:visual")],
            deleted.VisualModel.VisualStates.Select(static visual => visual.Id));
        Assert.Empty(deleted.SemanticModel.NestedScopes);
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);

        var survivingTarget = Assert.Single(
            deleted.VisualModel.VisualStates.Single().ConnectorAnchors);
        Assert.Equal(KeepTargetAnchorId, survivingTarget.Id);
        Assert.Equal(ConnectorAnchorRole.Target, survivingTarget.Role);

        var undo = await harness.History.UndoAsync(harness.Processor);
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        AssertAuthoritativeContentEqual(before, harness.Document.CaptureSnapshot());

        var redo = await harness.History.RedoAsync(harness.Processor);
        Assert.True(redo.IsCommitted, Diagnostics(redo.Diagnostics));
        AssertAuthoritativeContentEqual(deleted, harness.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task DeletingOnlyBoundaryLeavesActivityAndFreesOppositeAnchorWithExactHistory()
    {
        var before = ActivitySnapshot(BpmnSemanticTypes.UserTask);
        var harness = CreateHarness(before);

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            new DeleteBpmnFlowNodeCommand(
                DocumentId,
                harness.Document.Revision,
                BoundaryId,
                BoundaryVisualId));

        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var deleted = harness.Document.CaptureSnapshot();
        Assert.Contains(deleted.SemanticModel.Elements, element => element.Id == OwnerId);
        Assert.Contains(deleted.VisualModel.VisualStates, visual => visual.Id == OwnerVisualId);
        Assert.DoesNotContain(deleted.SemanticModel.Elements, element => element.Id == BoundaryId);
        Assert.DoesNotContain(deleted.VisualModel.VisualStates,
            visual => visual.Id == BoundaryVisualId || visual.SemanticElementId == FlowId);
        Assert.DoesNotContain(deleted.SemanticModel.Relationships,
            relationship => relationship.Id == FlowId);
        var keepVisual = Assert.Single(deleted.VisualModel.VisualStates,
            visual => visual.SemanticElementId == KeepId);
        Assert.Equal(KeepTargetAnchorId, Assert.Single(keepVisual.ConnectorAnchors).Id);
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        AssertAuthoritativeContentEqual(before, harness.Document.CaptureSnapshot());
        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        AssertAuthoritativeContentEqual(deleted, harness.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task DeletingParentSubProcessRemovesParentChildAndNestedAttachmentsExactly()
    {
        var before = NestedAttachmentSnapshot();
        var harness = CreateHarness(before);
        var subProcessAId = new SemanticElementId("bpmn:n9:nested:subprocess-a");
        var subProcessAVisualId = new VisualStateId("bpmn:n9:nested:subprocess-a:visual");
        var keepId = new SemanticElementId("bpmn:n9:nested:keep");

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            new DeleteBpmnFlowNodeCommand(
                DocumentId,
                harness.Document.Revision,
                subProcessAId,
                subProcessAVisualId));

        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var deleted = harness.Document.CaptureSnapshot();
        Assert.Equal([keepId],
            deleted.SemanticModel.Elements.Select(static element => element.Id));
        Assert.Equal(
            [new VisualStateId("bpmn:n9:nested:keep:visual")],
            deleted.VisualModel.VisualStates.Select(static visual => visual.Id));
        Assert.Empty(deleted.SemanticModel.NestedScopes);
        Assert.Empty(deleted.SemanticModel.ScopeMemberships);
        Assert.Empty(deleted.SemanticModel.Relationships);
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);

        var undo = await harness.History.UndoAsync(harness.Processor);
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        var restored = harness.Document.CaptureSnapshot();
        AssertAuthoritativeContentEqual(before, restored);
        AssertNestedAttachments(restored);

        var redo = await harness.History.RedoAsync(harness.Processor);
        Assert.True(redo.IsCommitted, Diagnostics(redo.Diagnostics));
        AssertAuthoritativeContentEqual(deleted, harness.Document.CaptureSnapshot());
    }

    private static DocumentSnapshot ActivitySnapshot(SemanticTypeId activityTypeId)
    {
        var revision = new DocumentRevision(7);
        var owner = activityTypeId == BpmnSemanticTypes.SubProcess
            ? BpmnSemanticFactory.CreateSubProcess(
                OwnerId,
                "OWNER_SUBPROCESS",
                "Owner SubProcess")
            : BpmnSemanticFactory.CreateTask(
                OwnerId,
                activityTypeId,
                "OWNER_ACTIVITY",
                "Owner Activity",
                1);
        var ownerBounds = new RectD(100d, 100d, 120d, 80d);
        var elements = new[]
        {
            owner,
            BpmnSemanticFactory.CreateTimerBoundaryEvent(
                BoundaryId,
                OwnerId,
                "Timeout",
                "PT5M",
                description: "Escalate this activity."),
            BpmnSemanticFactory.CreateTask(KeepId, "KEEP", "Keep", 2),
        };
        var relationship = BpmnSemanticFactory.CreateSequenceFlow(
            FlowId,
            BoundaryId,
            KeepId,
            "Handle timeout");
        var scopes = activityTypeId == BpmnSemanticTypes.SubProcess
            ? new[]
            {
                new DocumentScopeSnapshot(
                    new DocumentScopeId("bpmn:n9:owner:scope"),
                    new DocumentScopeId(DocumentId.Value),
                    OwnerId),
            }
            : [];
        var boundaryBounds = BottomCenter.ResolveBounds(ownerBounds, new SizeD(36d, 36d));
        var visuals = new[]
        {
            NodeVisual(OwnerVisualId, OwnerId, ownerBounds),
            NodeVisual(
                BoundaryVisualId,
                BoundaryId,
                boundaryBounds,
                [new ConnectorAnchor(
                    BoundarySourceAnchorId,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source,
                    0)],
                BottomCenter),
            NodeVisual(
                new VisualStateId("bpmn:n9:keep:visual"),
                KeepId,
                new RectD(400d, 100d, 120d, 80d),
                [new ConnectorAnchor(
                    KeepTargetAnchorId,
                    ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target,
                    0)]),
            ConnectorVisual(
                new VisualStateId("bpmn:n9:boundary-flow:visual"),
                FlowId,
                BoundarySourceAnchorId,
                KeepTargetAnchorId),
        };
        return Snapshot(revision, elements, [relationship], visuals, scopes);
    }

    private static DocumentSnapshot NestedAttachmentSnapshot()
    {
        var revision = new DocumentRevision(13);
        var rootScopeId = new DocumentScopeId(DocumentId.Value);
        var scopeAId = new DocumentScopeId("bpmn:n9:nested:scope-a");
        var scopeBId = new DocumentScopeId("bpmn:n9:nested:scope-b");
        var subProcessAId = new SemanticElementId("bpmn:n9:nested:subprocess-a");
        var boundaryAId = new SemanticElementId("bpmn:n9:nested:boundary-a");
        var subProcessBId = new SemanticElementId("bpmn:n9:nested:subprocess-b");
        var taskXId = new SemanticElementId("bpmn:n9:nested:task-x");
        var boundaryXId = new SemanticElementId("bpmn:n9:nested:boundary-x");
        var taskYId = new SemanticElementId("bpmn:n9:nested:task-y");
        var boundaryYId = new SemanticElementId("bpmn:n9:nested:boundary-y");
        var keepId = new SemanticElementId("bpmn:n9:nested:keep");
        var elements = new[]
        {
            BpmnSemanticFactory.CreateSubProcess(
                subProcessAId,
                "SUBPROCESS_A",
                "SubProcess A"),
            BpmnSemanticFactory.CreateTimerBoundaryEvent(
                boundaryAId,
                subProcessAId,
                "Parent timeout",
                "PT20M"),
            BpmnSemanticFactory.CreateTask(keepId, "KEEP", "Keep", 1),
            BpmnSemanticFactory.CreateSubProcess(
                subProcessBId,
                "SUBPROCESS_B",
                "SubProcess B"),
            BpmnSemanticFactory.CreateTask(
                taskXId,
                BpmnSemanticTypes.UserTask,
                "TASK_X",
                "Task X",
                2),
            BpmnSemanticFactory.CreateTimerBoundaryEvent(
                boundaryXId,
                taskXId,
                "Child timeout",
                "PT10M"),
            BpmnSemanticFactory.CreateTask(
                taskYId,
                BpmnSemanticTypes.ServiceTask,
                "TASK_Y",
                "Task Y",
                3),
            BpmnSemanticFactory.CreateTimerBoundaryEvent(
                boundaryYId,
                taskYId,
                "Nested timeout",
                "PT5M",
                cancelActivity: false),
        };
        var scopes = new[]
        {
            new DocumentScopeSnapshot(scopeAId, rootScopeId, subProcessAId),
            new DocumentScopeSnapshot(scopeBId, scopeAId, subProcessBId),
        };
        var memberships = new[]
        {
            new SemanticElementScopeMembershipSnapshot(subProcessBId, scopeAId),
            new SemanticElementScopeMembershipSnapshot(taskXId, scopeAId),
            new SemanticElementScopeMembershipSnapshot(boundaryXId, scopeAId),
            new SemanticElementScopeMembershipSnapshot(taskYId, scopeBId),
            new SemanticElementScopeMembershipSnapshot(boundaryYId, scopeBId),
        };
        var subProcessABounds = new RectD(100d, 100d, 180d, 120d);
        var subProcessBBounds = new RectD(80d, 80d, 160d, 110d);
        var taskXBounds = new RectD(320d, 90d, 120d, 80d);
        var taskYBounds = new RectD(100d, 100d, 120d, 80d);
        var visuals = new[]
        {
            NodeVisual(
                new VisualStateId("bpmn:n9:nested:subprocess-a:visual"),
                subProcessAId,
                subProcessABounds),
            AttachedVisual(
                "boundary-a",
                boundaryAId,
                subProcessABounds,
                new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Right, 0.5d)),
            NodeVisual(
                new VisualStateId("bpmn:n9:nested:keep:visual"),
                keepId,
                new RectD(520d, 100d, 120d, 80d)),
            NodeVisual(
                new VisualStateId("bpmn:n9:nested:subprocess-b:visual"),
                subProcessBId,
                subProcessBBounds),
            NodeVisual(
                new VisualStateId("bpmn:n9:nested:task-x:visual"),
                taskXId,
                taskXBounds),
            AttachedVisual(
                "boundary-x",
                boundaryXId,
                taskXBounds,
                new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.25d)),
            NodeVisual(
                new VisualStateId("bpmn:n9:nested:task-y:visual"),
                taskYId,
                taskYBounds),
            AttachedVisual(
                "boundary-y",
                boundaryYId,
                taskYBounds,
                new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Right, 0.75d)),
        };
        return Snapshot(
            revision,
            elements,
            relationships: [],
            visuals,
            scopes,
            memberships);
    }

    private static void AssertNestedAttachments(DocumentSnapshot snapshot)
    {
        Assert.Equal(
            new SemanticElementId("bpmn:n9:nested:subprocess-a"),
            snapshot.SemanticModel.Elements.Single(element =>
                element.Id == new SemanticElementId("bpmn:n9:nested:boundary-a"))
                .AttachedToElementId);
        Assert.Equal(
            new SemanticElementId("bpmn:n9:nested:task-x"),
            snapshot.SemanticModel.Elements.Single(element =>
                element.Id == new SemanticElementId("bpmn:n9:nested:boundary-x"))
                .AttachedToElementId);
        Assert.Equal(
            new SemanticElementId("bpmn:n9:nested:task-y"),
            snapshot.SemanticModel.Elements.Single(element =>
                element.Id == new SemanticElementId("bpmn:n9:nested:boundary-y"))
                .AttachedToElementId);
        Assert.Equal(3, snapshot.VisualModel.VisualStates.Count(static visual =>
            visual.BoundaryAttachment is not null));
    }

    private static Harness CreateHarness(DocumentSnapshot snapshot)
    {
        var registration = BpmnPluginRegistration.N90;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var reconstruction = DocumentReconstructor.Reconstruct(snapshot, provider);
        Assert.True(reconstruction.Succeeded, Diagnostics(reconstruction.Diagnostics));
        var document = Assert.IsType<Document>(reconstruction.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: provider);
        return new Harness(document, processor, new HistoryManager(document));
    }

    private static DocumentSnapshot Snapshot(
        DocumentRevision revision,
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot> relationships,
        IEnumerable<VisualStateSnapshot> visuals,
        IEnumerable<DocumentScopeSnapshot>? scopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                revision,
                elements,
                relationships,
                scopes,
                memberships),
            new VisualModelSnapshot(DocumentId, revision, visuals),
            new DocumentMetadataSnapshot(DocumentId, revision));

    private static VisualStateSnapshot NodeVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        RectD bounds,
        IEnumerable<ConnectorAnchor>? anchors = null,
        BoundaryAttachmentPlacement? attachment = null) =>
        new(
            visualStateId,
            semanticElementId,
            bounds.TopLeft,
            bounds.Size,
            VisualPlacementMode.Pinned,
            connectorAnchors: anchors,
            boundaryAttachment: attachment);

    private static VisualStateSnapshot AttachedVisual(
        string localId,
        SemanticElementId semanticElementId,
        RectD ownerBounds,
        BoundaryAttachmentPlacement placement)
    {
        var bounds = placement.ResolveBounds(ownerBounds, new SizeD(36d, 36d));
        return NodeVisual(
            new VisualStateId($"bpmn:n9:nested:{localId}:visual"),
            semanticElementId,
            bounds,
            [new ConnectorAnchor(
                new ConnectorAnchorId($"bpmn:n9:nested:{localId}:source"),
                ConnectorAnchorSide.Bottom,
                ConnectorAnchorRole.Source,
                0)],
            placement);
    }

    private static VisualStateSnapshot ConnectorVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId) =>
        new(
            visualStateId,
            semanticElementId,
            new PointD(0d, 0d),
            new SizeD(0d, 0d),
            VisualPlacementMode.Manual,
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
