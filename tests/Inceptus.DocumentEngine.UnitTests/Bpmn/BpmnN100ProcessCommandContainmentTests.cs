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

public sealed class BpmnN100ProcessCommandContainmentTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n10:containment:document");
    private static readonly SemanticElementId SourceId = new("bpmn:n10:containment:source");
    private static readonly SemanticElementId CurrentTargetId =
        new("bpmn:n10:containment:current-target");
    private static readonly SemanticElementId DocumentActivityId =
        new("bpmn:n10:containment:document-activity");
    private static readonly SemanticElementId FlowId = new("bpmn:n10:containment:flow");
    private static readonly SemanticElementId CreatedFlowId =
        new("bpmn:n10:containment:created-flow");
    private static readonly SemanticElementId BoundaryId =
        new("bpmn:n10:containment:boundary");
    private static readonly VisualStateId SourceVisualId =
        new("bpmn:n10:containment:source:visual");
    private static readonly VisualStateId CurrentTargetVisualId =
        new("bpmn:n10:containment:current-target:visual");
    private static readonly VisualStateId DocumentActivityVisualId =
        new("bpmn:n10:containment:document-activity:visual");
    private static readonly VisualStateId FlowVisualId =
        new("bpmn:n10:containment:flow:visual");
    private static readonly VisualStateId CreatedFlowVisualId =
        new("bpmn:n10:containment:created-flow:visual");
    private static readonly VisualStateId BoundaryVisualId =
        new("bpmn:n10:containment:boundary:visual");
    private static readonly ConnectorAnchorId SourceAnchorId =
        new("bpmn:n10:containment:source:anchor");
    private static readonly ConnectorAnchorId CurrentTargetAnchorId =
        new("bpmn:n10:containment:current-target:anchor");
    private static readonly ConnectorAnchorId DocumentTargetAnchorId =
        new("bpmn:n10:containment:document-activity:target-anchor");
    private static readonly RectD DocumentActivityBounds =
        new(240d, 40d, 160d, 100d);

    [Theory]
    [InlineData("task")]
    [InlineData("sub-process")]
    public async Task SequenceFlowCreationRejectsDocumentContainedFlowNodeWithoutResidue(
        string activityKind)
    {
        var initial = SequenceCreationSnapshot(ActivityType(activityKind));
        var command = new CreateBpmnSequenceFlowCommand(
            DocumentId,
            DocumentRevision.Zero,
            CreatedFlowId,
            CreatedFlowVisualId,
            SourceId,
            DocumentActivityId,
            SourceAnchorId,
            DocumentTargetAnchorId);

        await AssertRejectedWithoutResidueAsync(
            initial,
            command,
            BpmnCommandDiagnosticCodes.EndpointTypeInvalid,
            static snapshot =>
            {
                Assert.False(snapshot.SemanticModel.TryGetRelationship(
                    CreatedFlowId,
                    out _));
                Assert.False(snapshot.VisualModel.TryGetVisualState(
                    CreatedFlowVisualId,
                    out _));
            });
    }

    [Theory]
    [InlineData("task")]
    [InlineData("sub-process")]
    public async Task SequenceFlowReconnectionRejectsDocumentContainedFlowNodeWithoutResidue(
        string activityKind)
    {
        var initial = ReconnectionSnapshot(ActivityType(activityKind));
        var command = new ReconnectBpmnSequenceFlowEndpointCommand(
            DocumentId,
            DocumentRevision.Zero,
            FlowId,
            FlowVisualId,
            ConnectorEndpointKind.Target,
            CurrentTargetId,
            CurrentTargetAnchorId,
            DocumentActivityId,
            DocumentTargetAnchorId);

        await AssertRejectedWithoutResidueAsync(
            initial,
            command,
            BpmnCommandDiagnosticCodes.EndpointTypeInvalid,
            static snapshot =>
            {
                Assert.True(snapshot.SemanticModel.TryGetRelationship(
                    FlowId,
                    out var relationship));
                Assert.Equal(CurrentTargetId, relationship!.TargetId);
                Assert.True(snapshot.VisualModel.TryGetVisualState(
                    FlowVisualId,
                    out var connector));
                Assert.Equal(CurrentTargetAnchorId, connector!.TargetAnchorId);
            });
    }

    [Theory]
    [InlineData("task")]
    [InlineData("sub-process")]
    public async Task BoundaryEventCreationRejectsDocumentContainedActivityWithoutResidue(
        string activityKind)
    {
        var initial = BoundaryCreationSnapshot(ActivityType(activityKind));
        var command = new CreateBpmnTimerBoundaryEventCommand(
            DocumentId,
            DocumentRevision.Zero,
            BoundaryId,
            BoundaryVisualId,
            DocumentActivityId,
            BoundaryAttachmentSide.Right,
            0.5d,
            DocumentActivityBounds,
            "Timeout");

        await AssertRejectedWithoutResidueAsync(
            initial,
            command,
            BpmnCommandDiagnosticCodes.BoundaryEventAttachmentOwnerInvalid,
            static snapshot =>
            {
                Assert.False(snapshot.SemanticModel.TryGetElement(BoundaryId, out _));
                Assert.False(snapshot.VisualModel.TryGetVisualState(
                    BoundaryVisualId,
                    out _));
            });
    }

    private static async Task AssertRejectedWithoutResidueAsync(
        DocumentSnapshot initial,
        ICommand command,
        string diagnosticCode,
        Action<DocumentSnapshot> assertAuthoritativeState)
    {
        var registration = BpmnPluginRegistration.N100;
        var anchorPolicies = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var creation = DocumentFactory.Create(initial, anchorPolicies);
        Assert.True(
            creation.Succeeded,
            string.Join(Environment.NewLine, creation.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var document = Assert.IsType<Document>(creation.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: anchorPolicies);
        var history = new HistoryManager(document);
        var before = document.CaptureSnapshot();

        var result = await history.ExecuteAsync(processor, command);

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == diagnosticCode);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(0, history.CaptureStatus().EntryCount);
        assertAuthoritativeState(document.CaptureSnapshot());
    }

    private static DocumentSnapshot SequenceCreationSnapshot(
        SemanticTypeId documentActivityType) => Snapshot(
        [
            ScopeElement(SourceId, BpmnSemanticTypes.Task),
            DocumentElement(DocumentActivityId, documentActivityType),
        ],
        relationships: null,
        [
            Node(
                SourceVisualId,
                SourceId,
                new RectD(20d, 40d, 120d, 80d),
                new ConnectorAnchor(
                    SourceAnchorId,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source,
                    0)),
            Node(
                DocumentActivityVisualId,
                DocumentActivityId,
                DocumentActivityBounds,
                new ConnectorAnchor(
                    DocumentTargetAnchorId,
                    ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target,
                    0)),
        ]);

    private static DocumentSnapshot ReconnectionSnapshot(
        SemanticTypeId documentActivityType) => Snapshot(
        [
            ScopeElement(SourceId, BpmnSemanticTypes.Task),
            ScopeElement(CurrentTargetId, BpmnSemanticTypes.Task),
            DocumentElement(DocumentActivityId, documentActivityType),
        ],
        [new SemanticRelationshipSnapshot(
            FlowId,
            BpmnSemanticTypes.SequenceFlow,
            SourceId,
            CurrentTargetId)],
        [
            Node(
                SourceVisualId,
                SourceId,
                new RectD(20d, 40d, 120d, 80d),
                new ConnectorAnchor(
                    SourceAnchorId,
                    ConnectorAnchorSide.Right,
                    ConnectorAnchorRole.Source,
                    0)),
            Node(
                CurrentTargetVisualId,
                CurrentTargetId,
                new RectD(180d, 40d, 120d, 80d),
                new ConnectorAnchor(
                    CurrentTargetAnchorId,
                    ConnectorAnchorSide.Left,
                    ConnectorAnchorRole.Target,
                    0)),
            Node(
                DocumentActivityVisualId,
                DocumentActivityId,
                new RectD(360d, 40d, 160d, 100d),
                new ConnectorAnchor(
                    DocumentTargetAnchorId,
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
                targetAnchorId: CurrentTargetAnchorId),
        ]);

    private static DocumentSnapshot BoundaryCreationSnapshot(
        SemanticTypeId documentActivityType) => Snapshot(
        [DocumentElement(DocumentActivityId, documentActivityType)],
        relationships: null,
        [Node(
            DocumentActivityVisualId,
            DocumentActivityId,
            DocumentActivityBounds)]);

    private static SemanticTypeId ActivityType(string activityKind) => activityKind switch
    {
        "task" => BpmnSemanticTypes.Task,
        "sub-process" => BpmnSemanticTypes.SubProcess,
        _ => throw new ArgumentOutOfRangeException(nameof(activityKind)),
    };

    private static SemanticElementSnapshot ScopeElement(
        SemanticElementId id,
        SemanticTypeId typeId) => new(id, typeId);

    private static SemanticElementSnapshot DocumentElement(
        SemanticElementId id,
        SemanticTypeId typeId) => new(
        id,
        typeId,
        containmentKind: SemanticElementContainmentKind.Document);

    private static VisualStateSnapshot Node(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        RectD bounds,
        params ConnectorAnchor[] anchors) => new(
        visualStateId,
        semanticElementId,
        bounds.TopLeft,
        bounds.Size,
        VisualPlacementMode.Pinned,
        connectorAnchors: anchors);

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot>? relationships,
        IEnumerable<VisualStateSnapshot> visuals) => new(
        new SemanticModelSnapshot(
            DocumentId,
            DocumentRevision.Zero,
            elements,
            relationships),
        new VisualModelSnapshot(DocumentId, DocumentRevision.Zero, visuals),
        new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));
}
