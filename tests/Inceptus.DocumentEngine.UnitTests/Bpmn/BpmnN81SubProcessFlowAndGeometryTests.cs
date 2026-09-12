using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
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

public sealed class BpmnN81SubProcessFlowAndGeometryTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n8.1:flow-document");
    private static readonly SemanticElementId SourceId = new("bpmn:n8.1:source");
    private static readonly SemanticElementId TargetId = new("bpmn:n8.1:target");
    private static readonly SemanticElementId AlternateTargetId =
        new("bpmn:n8.1:alternate-target");
    private static readonly SemanticElementId FlowId = new("bpmn:n8.1:flow");
    private static readonly VisualStateId FlowVisualId = new("bpmn:n8.1:flow:visual");
    private static readonly ConnectorAnchorId SourceAnchorId =
        new("bpmn:n8.1:source:anchor");
    private static readonly ConnectorAnchorId TargetAnchorId =
        new("bpmn:n8.1:target:anchor");
    private static readonly ConnectorAnchorId AlternateTargetAnchorId =
        new("bpmn:n8.1:alternate-target:anchor");

    [Fact]
    public async Task OrdinaryTaskGatewayAndSubProcessSequenceFlowsCommitInBothDirections()
    {
        var scenarios = new[]
        {
            (BpmnSemanticTypes.Task, BpmnSemanticTypes.SubProcess),
            (BpmnSemanticTypes.SubProcess, BpmnSemanticTypes.Task),
            (BpmnSemanticTypes.ExclusiveGateway, BpmnSemanticTypes.SubProcess),
            (BpmnSemanticTypes.SubProcess, BpmnSemanticTypes.ExclusiveGateway),
        };

        foreach (var (sourceType, targetType) in scenarios)
        {
            var harness = CreateFlowHarness(sourceType, targetType);
            var beforeScopes = harness.Document.SemanticModel.NestedScopes;
            var result = await harness.History.ExecuteAsync(
                harness.Processor,
                new CreateBpmnSequenceFlowCommand(
                    DocumentId,
                    harness.Document.Revision,
                    FlowId,
                    FlowVisualId,
                    SourceId,
                    TargetId,
                    SourceAnchorId,
                    TargetAnchorId));

            Assert.True(result.IsCommitted,
                $"{sourceType}->{targetType}: {Diagnostics(result.Diagnostics)}");
            var relationship = Assert.Single(harness.Document.SemanticModel.Relationships);
            Assert.Equal(FlowId, relationship.Id);
            Assert.Equal(SourceId, relationship.SourceId);
            Assert.Equal(TargetId, relationship.TargetId);
            var connector = Assert.Single(harness.Document.VisualModel.VisualStates, visual =>
                visual.Id == FlowVisualId);
            Assert.Equal(SourceAnchorId, connector.SourceAnchorId);
            Assert.Equal(TargetAnchorId, connector.TargetAnchorId);
            Assert.Equal(beforeScopes.AsEnumerable(),
                harness.Document.SemanticModel.NestedScopes.AsEnumerable());
            Assert.Equal(1, harness.History.CaptureStatus().EntryCount);
        }
    }

    [Fact]
    public async Task EventBasedGatewayToSubProcessCreationIsRejectedWithoutAnyMutation()
    {
        var harness = CreateFlowHarness(
            BpmnSemanticTypes.EventBasedGateway,
            BpmnSemanticTypes.SubProcess);
        var before = harness.Document.CaptureSnapshot();
        var historyBefore = harness.History.CaptureStatus();

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            new CreateBpmnSequenceFlowCommand(
                DocumentId,
                harness.Document.Revision,
                FlowId,
                FlowVisualId,
                SourceId,
                TargetId,
                SourceAnchorId,
                TargetAnchorId));

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.EventBasedGatewayTargetInvalid);
        Assert.Same(before, harness.Document.CaptureSnapshot());
        Assert.Equal(historyBefore, harness.History.CaptureStatus());
        Assert.False(harness.Document.SemanticModel.TryGetRelationship(FlowId, out _));
        Assert.False(harness.Document.VisualModel.TryGetVisualState(FlowVisualId, out _));
    }

    [Fact]
    public async Task SmartTargetAnchorCreationToSubProcessCommitsAtomicallyWithExactIdentity()
    {
        var harness = CreateSmartTargetHarness(BpmnSemanticTypes.Task);
        var before = harness.Document.CaptureSnapshot();

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            SmartTargetCommand(harness.Document.Revision));

        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var committed = harness.Document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), committed.Revision);
        var relationship = Assert.Single(committed.SemanticModel.Relationships);
        Assert.Equal(FlowId, relationship.Id);
        Assert.Equal(SourceId, relationship.SourceId);
        Assert.Equal(TargetId, relationship.TargetId);
        var target = Assert.Single(committed.VisualModel.VisualStates, visual =>
            visual.SemanticElementId == TargetId);
        var targetAnchor = Assert.Single(target.ConnectorAnchors);
        Assert.Equal(TargetAnchorId, targetAnchor.Id);
        Assert.Equal(ConnectorAnchorSide.Left, targetAnchor.Side);
        Assert.Equal(ConnectorAnchorRole.Target, targetAnchor.Role);
        Assert.Equal(0, targetAnchor.Order);
        var connector = Assert.Single(committed.VisualModel.VisualStates, visual =>
            visual.Id == FlowVisualId);
        Assert.Equal(FlowId, connector.SemanticElementId);
        Assert.Equal(SourceAnchorId, connector.SourceAnchorId);
        Assert.Equal(TargetAnchorId, connector.TargetAnchorId);
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.False(harness.Document.SemanticModel.TryGetRelationship(FlowId, out _));
        Assert.False(harness.Document.VisualModel.TryGetVisualState(FlowVisualId, out _));
        Assert.Empty(Assert.Single(
            harness.Document.VisualModel.VisualStates,
            visual => visual.SemanticElementId == TargetId).ConnectorAnchors);

        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        var redone = harness.Document.CaptureSnapshot();
        Assert.Equal(committed.SemanticModel.Relationships.AsEnumerable(),
            redone.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(committed.VisualModel.VisualStates.AsEnumerable(),
            redone.VisualModel.VisualStates.AsEnumerable());
    }

    [Fact]
    public async Task EventBasedSmartTargetToSubProcessRejectsWithoutOrphanAnchor()
    {
        var harness = CreateSmartTargetHarness(BpmnSemanticTypes.EventBasedGateway);
        var before = harness.Document.CaptureSnapshot();
        var historyBefore = harness.History.CaptureStatus();

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            SmartTargetCommand(harness.Document.Revision));

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.EventBasedGatewayTargetInvalid);
        Assert.Same(before, harness.Document.CaptureSnapshot());
        Assert.Equal(before.Revision, harness.Document.Revision);
        Assert.Equal(historyBefore, harness.History.CaptureStatus());
        Assert.False(harness.Document.SemanticModel.TryGetRelationship(FlowId, out _));
        Assert.False(harness.Document.VisualModel.TryGetVisualState(FlowVisualId, out _));
        Assert.Empty(Assert.Single(
            harness.Document.VisualModel.VisualStates,
            visual => visual.SemanticElementId == TargetId).ConnectorAnchors);
    }

    [Fact]
    public async Task OrdinaryReconnectToSubProcessPreservesConnectorIdentity()
    {
        var harness = CreateReconnectHarness(BpmnSemanticTypes.Task);

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            Reconnect(harness.Document.Revision));

        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var relationship = Assert.Single(harness.Document.SemanticModel.Relationships);
        Assert.Equal(FlowId, relationship.Id);
        Assert.Equal(SourceId, relationship.SourceId);
        Assert.Equal(AlternateTargetId, relationship.TargetId);
        var connector = Assert.Single(harness.Document.VisualModel.VisualStates, visual =>
            visual.Id == FlowVisualId);
        Assert.Equal(FlowVisualId, connector.Id);
        Assert.Equal(SourceAnchorId, connector.SourceAnchorId);
        Assert.Equal(AlternateTargetAnchorId, connector.TargetAnchorId);
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task EventBasedReconnectToSubProcessIsRejectedAtomically()
    {
        var harness = CreateReconnectHarness(BpmnSemanticTypes.EventBasedGateway);
        var before = harness.Document.CaptureSnapshot();

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            Reconnect(harness.Document.Revision));

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.EventBasedGatewayTargetInvalid);
        Assert.Same(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task CrossScopeReconnectToNestedSubProcessPreservesTheExactConnectorSnapshot()
    {
        var harness = CreateCrossScopeReconnectHarness();
        var before = harness.Document.CaptureSnapshot();
        var relationshipBefore = Assert.Single(before.SemanticModel.Relationships);
        var connectorBefore = Assert.Single(before.VisualModel.VisualStates, visual =>
            visual.Id == FlowVisualId);

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            Reconnect(harness.Document.Revision));

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.SequenceFlowCrossesScope);
        Assert.Same(before, harness.Document.CaptureSnapshot());
        Assert.Equal(before.Revision, harness.Document.Revision);
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
        var relationshipAfter = Assert.Single(harness.Document.SemanticModel.Relationships);
        Assert.Equal(relationshipBefore, relationshipAfter);
        Assert.Equal(FlowId, relationshipAfter.Id);
        Assert.Equal(SourceId, relationshipAfter.SourceId);
        Assert.Equal(TargetId, relationshipAfter.TargetId);
        var connectorAfter = Assert.Single(harness.Document.VisualModel.VisualStates, visual =>
            visual.Id == FlowVisualId);
        Assert.Equal(connectorBefore, connectorAfter);
        Assert.Equal(FlowVisualId, connectorAfter.Id);
        Assert.Equal(SourceAnchorId, connectorAfter.SourceAnchorId);
        Assert.Equal(TargetAnchorId, connectorAfter.TargetAnchorId);
    }

    [Fact]
    public async Task MovingAndResizingParentNeverChangesChildVisualGeometry()
    {
        var parentId = new SemanticElementId("bpmn:n8.1:geometry:parent");
        var childId = new SemanticElementId("bpmn:n8.1:geometry:child");
        var parentVisualId = new VisualStateId("bpmn:n8.1:geometry:parent:visual");
        var childVisualId = new VisualStateId("bpmn:n8.1:geometry:child:visual");
        var childScopeId = new DocumentScopeId("bpmn:n8.1:geometry:child-scope");
        var snapshot = Snapshot(
            [
                BpmnSemanticFactory.CreateSubProcess(
                    parentId,
                    "PARENT",
                    "Parent"),
                BpmnSemanticFactory.CreateTask(childId, "CHILD", "Child", 1),
            ],
            relationships: null,
            [
                NodeVisual(
                    parentVisualId,
                    parentId,
                    new PointD(100d, 100d),
                    new SizeD(120d, 80d)),
                NodeVisual(
                    childVisualId,
                    childId,
                    new PointD(840d, 620d),
                    new SizeD(120d, 80d)),
            ],
            [
                new DocumentScopeSnapshot(
                    childScopeId,
                    new DocumentScopeId(DocumentId.Value),
                    parentId),
            ],
            [
                new SemanticElementScopeMembershipSnapshot(childId, childScopeId),
            ]);
        var registration = BpmnPluginRegistration.N81;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.Create(snapshot, provider);
        Assert.True(construction.Succeeded, Diagnostics(construction.Diagnostics));
        var document = Assert.IsType<Document>(construction.Document);
        var processor = new CommandProcessor();
        var originalChild = Assert.Single(document.VisualModel.VisualStates, visual =>
            visual.Id == childVisualId);

        var move = await processor.ExecuteAsync(
            document,
            new MoveVisualStateCommand(
                DocumentId,
                document.Revision,
                parentVisualId,
                new PointD(440d, 320d),
                VisualPlacementMode.Pinned));

        Assert.True(move.IsCommitted, Diagnostics(move.Diagnostics));
        Assert.Equal(originalChild, Assert.Single(
            document.VisualModel.VisualStates,
            visual => visual.Id == childVisualId));

        var resize = await processor.ExecuteAsync(
            document,
            new ResizeVisualStateCommand(
                DocumentId,
                document.Revision,
                parentVisualId,
                new RectD(440d, 320d, 180d, 110d),
                VisualPlacementMode.Pinned));

        Assert.True(resize.IsCommitted, Diagnostics(resize.Diagnostics));
        Assert.Equal(originalChild, Assert.Single(
            document.VisualModel.VisualStates,
            visual => visual.Id == childVisualId));
        var parent = Assert.Single(document.VisualModel.VisualStates, visual =>
            visual.Id == parentVisualId);
        Assert.Equal(new PointD(440d, 320d), parent.Position);
        Assert.Equal(new SizeD(180d, 110d), parent.Size);
    }

    private static ReconnectBpmnSequenceFlowEndpointCommand Reconnect(
        DocumentRevision revision) =>
        new(
            DocumentId,
            revision,
            FlowId,
            FlowVisualId,
            ConnectorEndpointKind.Target,
            TargetId,
            TargetAnchorId,
            AlternateTargetId,
            AlternateTargetAnchorId);

    private static CreateBpmnSequenceFlowWithTargetAnchorCommand SmartTargetCommand(
        DocumentRevision revision) =>
        new(
            DocumentId,
            revision,
            FlowId,
            FlowVisualId,
            SourceId,
            TargetId,
            SourceAnchorId,
            new VisualStateId("bpmn:n8.1:target:visual"),
            TargetAnchorId,
            ConnectorAnchorSide.Left,
            0);

    private static Harness CreateFlowHarness(
        SemanticTypeId sourceType,
        SemanticTypeId targetType)
    {
        var elements = new[]
        {
            Element(SourceId, sourceType),
            Element(TargetId, targetType),
        };
        var scopes = new List<DocumentScopeSnapshot>();
        AddOwnedScope(scopes, SourceId, sourceType, "source");
        AddOwnedScope(scopes, TargetId, targetType, "target");
        var snapshot = Snapshot(
            elements,
            relationships: null,
            [
                NodeVisual(
                    new VisualStateId("bpmn:n8.1:source:visual"),
                    SourceId,
                    new PointD(20d, 40d),
                    LogicalSize(sourceType),
                    new ConnectorAnchor(
                        SourceAnchorId,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        0)),
                NodeVisual(
                    new VisualStateId("bpmn:n8.1:target:visual"),
                    TargetId,
                    new PointD(260d, 40d),
                    LogicalSize(targetType),
                    new ConnectorAnchor(
                        TargetAnchorId,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        0)),
            ],
            scopes);
        return CreateHarness(snapshot);
    }

    private static Harness CreateReconnectHarness(SemanticTypeId sourceType)
    {
        var oldTargetType = sourceType == BpmnSemanticTypes.EventBasedGateway
            ? BpmnSemanticTypes.ReceiveTask
            : BpmnSemanticTypes.Task;
        var snapshot = Snapshot(
            [
                Element(SourceId, sourceType),
                Element(TargetId, oldTargetType),
                Element(AlternateTargetId, BpmnSemanticTypes.SubProcess),
            ],
            [
                BpmnSemanticFactory.CreateSequenceFlow(FlowId, SourceId, TargetId),
            ],
            [
                NodeVisual(
                    new VisualStateId("bpmn:n8.1:source:visual"),
                    SourceId,
                    new PointD(20d, 40d),
                    LogicalSize(sourceType),
                    new ConnectorAnchor(
                        SourceAnchorId,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        0)),
                NodeVisual(
                    new VisualStateId("bpmn:n8.1:target:visual"),
                    TargetId,
                    new PointD(240d, 40d),
                    LogicalSize(oldTargetType),
                    new ConnectorAnchor(
                        TargetAnchorId,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        0)),
                NodeVisual(
                    new VisualStateId("bpmn:n8.1:alternate-target:visual"),
                    AlternateTargetId,
                    new PointD(460d, 40d),
                    new SizeD(120d, 80d),
                    new ConnectorAnchor(
                        AlternateTargetAnchorId,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        0)),
                new VisualStateSnapshot(
                    FlowVisualId,
                    FlowId,
                    new PointD(0d, 0d),
                    new SizeD(0d, 0d),
                    VisualPlacementMode.Manual,
                    sourceAnchorId: SourceAnchorId,
                    targetAnchorId: TargetAnchorId),
            ],
            [
                new DocumentScopeSnapshot(
                    new DocumentScopeId("bpmn:n8.1:alternate-target:scope"),
                    new DocumentScopeId(DocumentId.Value),
                    AlternateTargetId),
            ]);
        return CreateHarness(snapshot);
    }

    private static Harness CreateSmartTargetHarness(SemanticTypeId sourceType)
    {
        var snapshot = Snapshot(
            [
                Element(SourceId, sourceType),
                Element(TargetId, BpmnSemanticTypes.SubProcess),
            ],
            relationships: null,
            [
                NodeVisual(
                    new VisualStateId("bpmn:n8.1:source:visual"),
                    SourceId,
                    new PointD(20d, 40d),
                    LogicalSize(sourceType),
                    new ConnectorAnchor(
                        SourceAnchorId,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        0)),
                NodeVisual(
                    new VisualStateId("bpmn:n8.1:target:visual"),
                    TargetId,
                    new PointD(260d, 40d),
                    new SizeD(120d, 80d)),
            ],
            [
                new DocumentScopeSnapshot(
                    new DocumentScopeId("bpmn:n8.1:target:scope"),
                    new DocumentScopeId(DocumentId.Value),
                    TargetId),
            ]);
        return CreateHarness(snapshot);
    }

    private static Harness CreateCrossScopeReconnectHarness()
    {
        var ownerId = new SemanticElementId("bpmn:n8.1:scope-owner");
        var ownerVisualId = new VisualStateId("bpmn:n8.1:scope-owner:visual");
        var parentScopeId = new DocumentScopeId("bpmn:n8.1:parent-scope");
        var nestedChildScopeId = new DocumentScopeId(
            "bpmn:n8.1:alternate-target:child-scope");
        var snapshot = Snapshot(
            [
                Element(SourceId, BpmnSemanticTypes.Task),
                Element(TargetId, BpmnSemanticTypes.Task),
                Element(ownerId, BpmnSemanticTypes.SubProcess),
                Element(AlternateTargetId, BpmnSemanticTypes.SubProcess),
            ],
            [
                BpmnSemanticFactory.CreateSequenceFlow(FlowId, SourceId, TargetId),
            ],
            [
                NodeVisual(
                    new VisualStateId("bpmn:n8.1:source:visual"),
                    SourceId,
                    new PointD(20d, 40d),
                    new SizeD(120d, 80d),
                    new ConnectorAnchor(
                        SourceAnchorId,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        0)),
                NodeVisual(
                    new VisualStateId("bpmn:n8.1:target:visual"),
                    TargetId,
                    new PointD(240d, 40d),
                    new SizeD(120d, 80d),
                    new ConnectorAnchor(
                        TargetAnchorId,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        0)),
                NodeVisual(
                    ownerVisualId,
                    ownerId,
                    new PointD(460d, 40d),
                    new SizeD(120d, 80d)),
                NodeVisual(
                    new VisualStateId("bpmn:n8.1:alternate-target:visual"),
                    AlternateTargetId,
                    new PointD(40d, 40d),
                    new SizeD(120d, 80d),
                    new ConnectorAnchor(
                        AlternateTargetAnchorId,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        0)),
                new VisualStateSnapshot(
                    FlowVisualId,
                    FlowId,
                    new PointD(0d, 0d),
                    new SizeD(0d, 0d),
                    VisualPlacementMode.Manual,
                    sourceAnchorId: SourceAnchorId,
                    targetAnchorId: TargetAnchorId),
            ],
            [
                new DocumentScopeSnapshot(
                    parentScopeId,
                    new DocumentScopeId(DocumentId.Value),
                    ownerId),
                new DocumentScopeSnapshot(
                    nestedChildScopeId,
                    parentScopeId,
                    AlternateTargetId),
            ],
            [
                new SemanticElementScopeMembershipSnapshot(
                    AlternateTargetId,
                    parentScopeId),
            ]);
        return CreateHarness(snapshot);
    }

    private static Harness CreateHarness(DocumentSnapshot snapshot)
    {
        var registration = BpmnPluginRegistration.N81;
        var provider = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.Create(snapshot, provider);
        Assert.True(construction.Succeeded, Diagnostics(construction.Diagnostics));
        var document = Assert.IsType<Document>(construction.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: provider);
        return new Harness(document, processor, new HistoryManager(document));
    }

    private static SemanticElementSnapshot Element(
        SemanticElementId id,
        SemanticTypeId typeId) =>
        typeId == BpmnSemanticTypes.SubProcess
            ? BpmnSemanticFactory.CreateSubProcess(id, "SUBPROCESS", "SubProcess")
            : typeId == BpmnSemanticTypes.ExclusiveGateway
                ? BpmnSemanticFactory.CreateExclusiveGateway(id, "GATEWAY", "Gateway")
                : typeId == BpmnSemanticTypes.EventBasedGateway
                    ? BpmnSemanticFactory.CreateEventBasedGateway(
                        id,
                        "EVENT_BASED",
                        "Event-Based Gateway")
                    : BpmnTaskSemanticTypes.IsTask(typeId)
                        ? BpmnSemanticFactory.CreateTask(
                            id,
                            typeId,
                            "TASK",
                            "Task",
                            1)
                        : throw new ArgumentOutOfRangeException(nameof(typeId));

    private static void AddOwnedScope(
        List<DocumentScopeSnapshot> scopes,
        SemanticElementId elementId,
        SemanticTypeId typeId,
        string suffix)
    {
        if (typeId == BpmnSemanticTypes.SubProcess)
        {
            scopes.Add(new DocumentScopeSnapshot(
                new DocumentScopeId($"bpmn:n8.1:{suffix}:scope"),
                new DocumentScopeId(DocumentId.Value),
                elementId));
        }
    }

    private static SizeD LogicalSize(SemanticTypeId typeId) =>
        typeId == BpmnSemanticTypes.ExclusiveGateway ||
        typeId == BpmnSemanticTypes.EventBasedGateway
            ? new SizeD(48d, 48d)
            : new SizeD(120d, 80d);

    private static VisualStateSnapshot NodeVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        PointD position,
        SizeD size,
        ConnectorAnchor? anchor = null) =>
        new(
            visualStateId,
            semanticElementId,
            position,
            size,
            VisualPlacementMode.Pinned,
            connectorAnchors: anchor is null ? null : [anchor]);

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot>? relationships,
        IEnumerable<VisualStateSnapshot> visuals,
        IEnumerable<DocumentScopeSnapshot>? scopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                elements,
                relationships,
                scopes,
                memberships),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero, visuals),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History);
}
