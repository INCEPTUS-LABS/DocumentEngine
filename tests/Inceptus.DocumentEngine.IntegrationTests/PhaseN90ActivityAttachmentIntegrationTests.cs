using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN90ActivityAttachmentIntegrationTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n9:integration");

    private static readonly SemanticElementId StartId = new("bpmn:n9:start");
    private static readonly SemanticElementId ActivityId = new("bpmn:n9:activity");
    private static readonly SemanticElementId BoundaryId = new("bpmn:n9:boundary");
    private static readonly SemanticElementId HandlerId = new("bpmn:n9:handler");
    private static readonly SemanticElementId EndId = new("bpmn:n9:end");

    private static readonly VisualStateId StartVisualId = new("bpmn:n9:start:visual");
    private static readonly VisualStateId ActivityVisualId =
        new("bpmn:n9:activity:visual");
    private static readonly VisualStateId BoundaryVisualId =
        new("bpmn:n9:boundary:visual");
    private static readonly VisualStateId HandlerVisualId =
        new("bpmn:n9:handler:visual");
    private static readonly VisualStateId EndVisualId = new("bpmn:n9:end:visual");

    private static readonly SemanticElementId StartActivityFlowId =
        new("bpmn:n9:flow:start-activity");
    private static readonly SemanticElementId ActivityEndFlowId =
        new("bpmn:n9:flow:activity-end");
    private static readonly SemanticElementId BoundaryHandlerFlowId =
        new("bpmn:n9:flow:boundary-handler");
    private static readonly SemanticElementId HandlerEndFlowId =
        new("bpmn:n9:flow:handler-end");

    private static readonly ConnectorAnchorId StartSourceAnchorId =
        new("bpmn:n9:start:source");
    private static readonly ConnectorAnchorId ActivityTargetAnchorId =
        new("bpmn:n9:activity:target");
    private static readonly ConnectorAnchorId ActivitySourceAnchorId =
        new("bpmn:n9:activity:source");
    private static readonly ConnectorAnchorId BoundarySourceAnchorId =
        new("bpmn:n9:boundary:source");
    private static readonly ConnectorAnchorId HandlerTargetAnchorId =
        new("bpmn:n9:handler:target");
    private static readonly ConnectorAnchorId HandlerSourceAnchorId =
        new("bpmn:n9:handler:source");
    private static readonly ConnectorAnchorId EndActivityTargetAnchorId =
        new("bpmn:n9:end:activity-target");
    private static readonly ConnectorAnchorId EndHandlerTargetAnchorId =
        new("bpmn:n9:end:handler-target");

    private static readonly RectD InitialActivityBounds =
        new(180d, 140d, 140d, 90d);
    private static readonly BoundaryAttachmentPlacement InitialAttachment =
        new(BoundaryAttachmentSide.Bottom, 0.5d);
    private static readonly SizeD BoundarySize = new(36d, 36d);

    [Fact]
    public async Task RootAttachmentRunsThroughCommandsHistoryProjectionLayoutRoutingSceneAndN5()
    {
        var harness = await CreateRootModelAsync();
        var snapshot = harness.Document.CaptureSnapshot();
        var expectedBoundaryBounds = InitialAttachment.ResolveBounds(
            InitialActivityBounds,
            BoundarySize);

        var boundary = Assert.Single(snapshot.SemanticModel.Elements, element =>
            element.Id == BoundaryId);
        Assert.Equal(BpmnSemanticTypes.TimerBoundaryEvent, boundary.TypeId);
        Assert.Equal(ActivityId, boundary.AttachedToElementId);
        Assert.True(boundary.Properties[BpmnSemanticProperties.CancelActivity].BooleanValue);
        Assert.Equal(
            snapshot.SemanticModel.RootScopeId,
            snapshot.SemanticModel.GetScope(BoundaryId).Id);
        Assert.DoesNotContain(snapshot.SemanticModel.ScopeMemberships, membership =>
            membership.SemanticElementId == BoundaryId);

        var boundaryVisual = Assert.Single(snapshot.VisualModel.VisualStates, visual =>
            visual.Id == BoundaryVisualId);
        Assert.Equal(expectedBoundaryBounds.TopLeft, boundaryVisual.Position);
        Assert.Equal(BoundarySize, boundaryVisual.Size);
        Assert.Equal(VisualPlacementMode.Manual, boundaryVisual.PlacementMode);
        Assert.Equal(InitialAttachment, boundaryVisual.BoundaryAttachment);
        var sourceAnchor = Assert.Single(boundaryVisual.ConnectorAnchors);
        Assert.Equal(BoundarySourceAnchorId, sourceAnchor.Id);
        Assert.Equal(ConnectorAnchorRole.Source, sourceAnchor.Role);

        var pipeline = RunPipeline(snapshot);
        var boundaryNode = Assert.Single(pipeline.Graph.Nodes, node =>
            node.Source.SemanticElementId == BoundaryId);
        Assert.Equal(
            NodeGeometryInteractionPolicy.AttachedBoundaryMoveFixedSize,
            boundaryNode.GeometryInteractionPolicy);
        Assert.Equal(
            ActivityId,
            boundaryNode.PlacementHint?.BoundaryAttachment?.AttachedToElementId);
        Assert.Equal(
            InitialAttachment,
            boundaryNode.PlacementHint?.BoundaryAttachment?.Placement);
        var boundaryGeometry = Assert.Single(pipeline.Layout.Nodes, geometry =>
            geometry.ProjectedObjectId == boundaryNode.Id);
        Assert.Equal(expectedBoundaryBounds, boundaryGeometry.Bounds);

        var boundaryFlowEdge = Assert.Single(pipeline.Graph.Edges, edge =>
            edge.Source.SemanticElementId == BoundaryHandlerFlowId);
        var boundaryFlowRoute = Assert.Single(pipeline.Routing.Routes, route =>
            route.ProjectedEdgeId == boundaryFlowEdge.Id);
        Assert.Equal(
            ConnectorAnchorGeometryResolver.ResolvePoint(
                expectedBoundaryBounds,
                ConnectorAnchorSide.Right,
                0,
                1),
            boundaryFlowRoute.SourceAnchor);
        Assert.Equal(boundaryFlowRoute.SourceAnchor, boundaryFlowRoute.Path[0]);
        Assert.Equal(
            boundaryFlowRoute.DestinationAnchor,
            boundaryFlowRoute.Path[^1]);

        var boundaryBody = Assert.Single(pipeline.Scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.SemanticElementId == BoundaryId &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Ellipse);
        Assert.Equal(expectedBoundaryBounds, boundaryBody.Bounds);
        Assert.Contains(pipeline.Scene.Items, item =>
            item.Origin.SemanticElementId == BoundaryId &&
            item.Origin.StableSourceKey?.Contains(":clock:",
                StringComparison.Ordinal) == true);

        var validation = new BpmnStructuralValidationRule().Validate(
            new ModelValidationContext(snapshot));
        Assert.DoesNotContain(validation, issue =>
            (issue.Target.SemanticElementId == BoundaryId ||
                issue.Target.SemanticElementId == HandlerId) &&
            (issue.Code == BpmnModelValidationCodes.FlowNodeIsolated ||
                issue.Code == BpmnModelValidationCodes.NodeUnreachableFromStart));

        var beforeRejectedIncoming = harness.Document.CaptureSnapshot();
        var historyBeforeRejectedIncoming = harness.History.CaptureStatus();
        var rejectedIncomingId = new SemanticElementId(
            "bpmn:n9:flow:invalid-incoming-boundary");
        var rejectedIncoming = await harness.History.ExecuteAsync(
            harness.Processor,
            new CreateBpmnSequenceFlowCommand(
                DocumentId,
                harness.Document.Revision,
                rejectedIncomingId,
                new VisualStateId($"{rejectedIncomingId.Value}:visual"),
                HandlerId,
                BoundaryId,
                HandlerSourceAnchorId,
                BoundarySourceAnchorId));

        Assert.False(rejectedIncoming.IsCommitted);
        Assert.Contains(rejectedIncoming.Diagnostics, diagnostic =>
            diagnostic.Code == BpmnCommandDiagnosticCodes.IncomingBoundaryEvent);
        Assert.Same(beforeRejectedIncoming, harness.Document.CaptureSnapshot());
        Assert.Equal(historyBeforeRejectedIncoming, harness.History.CaptureStatus());
        Assert.False(harness.Document.SemanticModel.TryGetRelationship(
            rejectedIncomingId,
            out _));
    }

    [Fact]
    public async Task OwnerGeometryAttachmentHistoryAndDeletionCascadeRemainExact()
    {
        var harness = await CreateRootModelAsync();
        var movedOwnerBounds = new RectD(220d, 170d, 140d, 90d);
        await ExecuteCommittedAsync(
            harness,
            revision => new MoveVisualStateCommand(
                DocumentId,
                revision,
                ActivityVisualId,
                movedOwnerBounds.TopLeft,
                VisualPlacementMode.Pinned));
        AssertBoundaryVisual(
            harness.Document.CaptureSnapshot(),
            InitialAttachment,
            InitialAttachment.ResolveBounds(movedOwnerBounds, BoundarySize));

        var resizedOwnerBounds = new RectD(220d, 170d, 180d, 110d);
        await ExecuteCommittedAsync(
            harness,
            revision => new ResizeVisualStateCommand(
                DocumentId,
                revision,
                ActivityVisualId,
                resizedOwnerBounds,
                VisualPlacementMode.Pinned));
        var resizedBoundaryBounds = InitialAttachment.ResolveBounds(
            resizedOwnerBounds,
            BoundarySize);
        AssertBoundaryVisual(
            harness.Document.CaptureSnapshot(),
            InitialAttachment,
            resizedBoundaryBounds);
        Assert.Equal(
            resizedBoundaryBounds,
            BoundaryGeometry(RunPipeline(harness.Document.CaptureSnapshot())).Bounds);

        var beforeAttachmentMove = harness.Document.CaptureSnapshot();
        var movedAttachment = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Right,
            0.25d);
        await ExecuteCommittedAsync(
            harness,
            revision => new UpdateBoundaryAttachmentCommand(
                DocumentId,
                revision,
                BoundaryVisualId,
                movedAttachment,
                resizedOwnerBounds));
        var afterAttachmentMove = harness.Document.CaptureSnapshot();
        var movedBoundaryBounds = movedAttachment.ResolveBounds(
            resizedOwnerBounds,
            BoundarySize);
        AssertBoundaryVisual(afterAttachmentMove, movedAttachment, movedBoundaryBounds);

        var undoAttachment = await harness.History.UndoAsync(harness.Processor);
        Assert.True(undoAttachment.IsCommitted, Diagnostics(undoAttachment.Diagnostics));
        AssertAuthoritativeContentEqual(
            beforeAttachmentMove,
            harness.Document.CaptureSnapshot());
        AssertBoundaryVisual(
            harness.Document.CaptureSnapshot(),
            InitialAttachment,
            resizedBoundaryBounds);

        var redoAttachment = await harness.History.RedoAsync(harness.Processor);
        Assert.True(redoAttachment.IsCommitted, Diagnostics(redoAttachment.Diagnostics));
        AssertAuthoritativeContentEqual(
            afterAttachmentMove,
            harness.Document.CaptureSnapshot());
        AssertBoundaryVisual(
            harness.Document.CaptureSnapshot(),
            movedAttachment,
            movedBoundaryBounds);

        var beforeDelete = harness.Document.CaptureSnapshot();
        var removedElementIds = new HashSet<SemanticElementId>
        {
            ActivityId,
            BoundaryId,
        };
        var incidentFlowIds = beforeDelete.SemanticModel.Relationships
            .Where(relationship =>
                removedElementIds.Contains(relationship.SourceId) ||
                removedElementIds.Contains(relationship.TargetId))
            .Select(static relationship => relationship.Id)
            .ToHashSet();
        Assert.Equal(3, incidentFlowIds.Count);

        await ExecuteCommittedAsync(
            harness,
            revision => new DeleteBpmnFlowNodeCommand(
                DocumentId,
                revision,
                ActivityId,
                ActivityVisualId));
        var deleted = harness.Document.CaptureSnapshot();
        Assert.DoesNotContain(deleted.SemanticModel.Elements, element =>
            removedElementIds.Contains(element.Id));
        Assert.DoesNotContain(deleted.VisualModel.VisualStates, visual =>
            visual.Id == ActivityVisualId || visual.Id == BoundaryVisualId);
        Assert.DoesNotContain(deleted.SemanticModel.Relationships, relationship =>
            incidentFlowIds.Contains(relationship.Id));
        Assert.DoesNotContain(deleted.VisualModel.VisualStates, visual =>
            incidentFlowIds.Contains(visual.SemanticElementId));
        Assert.Contains(deleted.SemanticModel.Elements, element =>
            element.Id == HandlerId);
        Assert.Contains(deleted.SemanticModel.Relationships, relationship =>
            relationship.Id == HandlerEndFlowId);

        var undoDelete = await harness.History.UndoAsync(harness.Processor);
        Assert.True(undoDelete.IsCommitted, Diagnostics(undoDelete.Diagnostics));
        var restored = harness.Document.CaptureSnapshot();
        AssertAuthoritativeContentEqual(beforeDelete, restored);
        var restoredBoundary = Assert.Single(restored.SemanticModel.Elements, element =>
            element.Id == BoundaryId);
        Assert.Equal(ActivityId, restoredBoundary.AttachedToElementId);
        AssertBoundaryVisual(restored, movedAttachment, movedBoundaryBounds);
        Assert.Contains(restored.SemanticModel.Relationships, relationship =>
            relationship.Id == BoundaryHandlerFlowId);
    }

    private static async Task<Harness> CreateRootModelAsync()
    {
        var registration = BpmnPluginRegistration.N90;
        var anchorPolicies = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var construction = DocumentFactory.CreateEmpty(
            DocumentId,
            connectorAnchorPolicyProvider: anchorPolicies);
        Assert.True(construction.Succeeded, Diagnostics(construction.Diagnostics));
        var document = Assert.IsType<Document>(construction.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: anchorPolicies);
        var harness = new Harness(document, processor, new HistoryManager(document));

        await ExecuteCommittedAsync(harness, revision => new CreateBpmnStartEventCommand(
            DocumentId,
            revision,
            StartId,
            StartVisualId,
            new PointD(40d, 165d),
            new SizeD(36d, 36d),
            VisualPlacementMode.Pinned,
            "Start"));
        await ExecuteCommittedAsync(harness, revision => new CreateBpmnTaskCommand(
            DocumentId,
            revision,
            ActivityId,
            ActivityVisualId,
            InitialActivityBounds.TopLeft,
            InitialActivityBounds.Size,
            "REVIEW",
            "Review",
            1,
            VisualPlacementMode.Pinned));
        await ExecuteCommittedAsync(
            harness,
            revision => new CreateBpmnTimerBoundaryEventCommand(
                DocumentId,
                revision,
                BoundaryId,
                BoundaryVisualId,
                ActivityId,
                InitialAttachment.Side,
                InitialAttachment.PositionOnSide,
                InitialActivityBounds,
                "Review timeout",
                "PT5M",
                cancelActivity: true,
                "Escalate an overdue review."));
        await ExecuteCommittedAsync(harness, revision => new CreateBpmnTaskCommand(
            DocumentId,
            revision,
            HandlerId,
            HandlerVisualId,
            new PointD(460d, 250d),
            new SizeD(120d, 80d),
            "ESCALATE",
            "Escalate",
            2,
            VisualPlacementMode.Pinned));
        await ExecuteCommittedAsync(harness, revision => new CreateBpmnEndEventCommand(
            DocumentId,
            revision,
            EndId,
            EndVisualId,
            new PointD(700d, 170d),
            new SizeD(36d, 36d),
            VisualPlacementMode.Pinned,
            "End"));

        await AddAnchorAsync(harness, StartVisualId, StartSourceAnchorId,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        await AddAnchorAsync(harness, ActivityVisualId, ActivityTargetAnchorId,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0);
        await AddAnchorAsync(harness, ActivityVisualId, ActivitySourceAnchorId,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        await AddAnchorAsync(harness, BoundaryVisualId, BoundarySourceAnchorId,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        await AddAnchorAsync(harness, HandlerVisualId, HandlerTargetAnchorId,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0);
        await AddAnchorAsync(harness, HandlerVisualId, HandlerSourceAnchorId,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        await AddAnchorAsync(harness, EndVisualId, EndActivityTargetAnchorId,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0);
        await AddAnchorAsync(harness, EndVisualId, EndHandlerTargetAnchorId,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 1);

        await CreateFlowAsync(
            harness,
            StartActivityFlowId,
            StartId,
            ActivityId,
            StartSourceAnchorId,
            ActivityTargetAnchorId);
        await CreateFlowAsync(
            harness,
            ActivityEndFlowId,
            ActivityId,
            EndId,
            ActivitySourceAnchorId,
            EndActivityTargetAnchorId);
        await CreateFlowAsync(
            harness,
            BoundaryHandlerFlowId,
            BoundaryId,
            HandlerId,
            BoundarySourceAnchorId,
            HandlerTargetAnchorId);
        await CreateFlowAsync(
            harness,
            HandlerEndFlowId,
            HandlerId,
            EndId,
            HandlerSourceAnchorId,
            EndHandlerTargetAnchorId);

        return harness;
    }

    private static Task AddAnchorAsync(
        Harness harness,
        VisualStateId visualStateId,
        ConnectorAnchorId anchorId,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role,
        int insertionIndex) =>
        ExecuteCommittedAsync(harness, revision => new AddConnectorAnchorCommand(
            DocumentId,
            revision,
            visualStateId,
            anchorId,
            side,
            role,
            insertionIndex));

    private static Task CreateFlowAsync(
        Harness harness,
        SemanticElementId relationshipId,
        SemanticElementId sourceId,
        SemanticElementId targetId,
        ConnectorAnchorId sourceAnchorId,
        ConnectorAnchorId targetAnchorId) =>
        ExecuteCommittedAsync(harness, revision => new CreateBpmnSequenceFlowCommand(
            DocumentId,
            revision,
            relationshipId,
            new VisualStateId($"{relationshipId.Value}:visual"),
            sourceId,
            targetId,
            sourceAnchorId,
            targetAnchorId));

    private static async Task ExecuteCommittedAsync(
        Harness harness,
        Func<DocumentRevision, ICommand> createCommand)
    {
        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            createCommand(harness.Document.Revision));
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
    }

    private static PipelineArtifacts RunPipeline(DocumentSnapshot snapshot)
    {
        var registration = BpmnPluginRegistration.N90;
        var projection = new ProjectionEngine(registration.ProjectionRules).Project(snapshot);
        Assert.True(projection.IsSuccessful, Diagnostics(projection.Diagnostics));
        var graph = Assert.IsType<ProjectedGraph>(projection.Graph);

        var layoutExecution = new LayoutEngine(registration.LayoutAlgorithms).Layout(
            graph,
            BpmnAlgorithmIds.DefaultLayout);
        Assert.True(
            layoutExecution.IsSuccessful,
            Diagnostics(layoutExecution.Diagnostics));
        var layout = Assert.IsType<LayoutResult>(layoutExecution.Result);

        var routingExecution = new RoutingEngine(registration.RoutingAlgorithms).Route(
            graph,
            layout,
            BpmnAlgorithmIds.DefaultRouting);
        Assert.True(
            routingExecution.IsSuccessful,
            Diagnostics(routingExecution.Diagnostics));
        var routing = Assert.IsType<RoutingResult>(routingExecution.Result);

        var sceneExecution = new Canvas2DSceneBuilder(
            contributors: registration.SceneContributors).Build(
                graph,
                layout,
                routing,
                snapshot.VisualModel,
                EditorStateSnapshot.Empty);
        Assert.True(sceneExecution.Succeeded, Diagnostics(sceneExecution.Diagnostics));
        return new PipelineArtifacts(
            graph,
            layout,
            routing,
            Assert.IsType<Canvas2DScene>(sceneExecution.Scene));
    }

    private static LayoutNodeGeometry BoundaryGeometry(PipelineArtifacts pipeline)
    {
        var node = Assert.Single(pipeline.Graph.Nodes, candidate =>
            candidate.Source.SemanticElementId == BoundaryId);
        return Assert.Single(pipeline.Layout.Nodes, geometry =>
            geometry.ProjectedObjectId == node.Id);
    }

    private static void AssertBoundaryVisual(
        DocumentSnapshot snapshot,
        BoundaryAttachmentPlacement expectedPlacement,
        RectD expectedBounds)
    {
        var visual = Assert.Single(snapshot.VisualModel.VisualStates, candidate =>
            candidate.Id == BoundaryVisualId);
        Assert.Equal(expectedPlacement, visual.BoundaryAttachment);
        Assert.Equal(expectedBounds.TopLeft, visual.Position);
        Assert.Equal(expectedBounds.Size, visual.Size);
    }

    private static void AssertAuthoritativeContentEqual(
        DocumentSnapshot expected,
        DocumentSnapshot actual)
    {
        Assert.Equal(
            expected.SemanticModel.Elements.AsEnumerable(),
            actual.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            expected.SemanticModel.Relationships.AsEnumerable(),
            actual.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(
            expected.SemanticModel.NestedScopes.AsEnumerable(),
            actual.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(
            expected.SemanticModel.ScopeMemberships.AsEnumerable(),
            actual.SemanticModel.ScopeMemberships.AsEnumerable());
        Assert.Equal(
            expected.VisualModel.VisualStates.AsEnumerable(),
            actual.VisualModel.VisualStates.AsEnumerable());
    }

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History);

    private sealed record PipelineArtifacts(
        ProjectedGraph Graph,
        LayoutResult Layout,
        RoutingResult Routing,
        Canvas2DScene Scene);
}
