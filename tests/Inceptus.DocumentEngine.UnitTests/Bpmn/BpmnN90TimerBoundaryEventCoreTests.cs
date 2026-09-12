using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Scene;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN90TimerBoundaryEventCoreTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n9:core-document");
    private static readonly DocumentRevision Revision = DocumentRevision.Zero;
    private static readonly SemanticElementId ActivityId = new("bpmn:n9:activity");
    private static readonly SemanticElementId BoundaryId = new("bpmn:n9:boundary");
    private static readonly VisualStateId ActivityVisualId =
        new("bpmn:n9:activity:visual");
    private static readonly VisualStateId BoundaryVisualId =
        new("bpmn:n9:boundary:visual");

    [Fact]
    public void TimerBoundaryEventHasPreciseClassificationAndStructuralProperties()
    {
        Assert.Equal("BPMN.TimerBoundaryEvent", BpmnSemanticTypes.TimerBoundaryEvent.Value);
        Assert.Equal(
            [
                BpmnSemanticTypes.TimerBoundaryEvent,
                BpmnSemanticTypes.MessageBoundaryEvent,
                BpmnSemanticTypes.SignalBoundaryEvent,
            ],
            BpmnBoundaryEventSemanticTypes.All.AsEnumerable());
        Assert.True(BpmnSemanticTypes.IsFlowNode(BpmnSemanticTypes.TimerBoundaryEvent));
        Assert.True(BpmnSemanticTypes.IsEvent(BpmnSemanticTypes.TimerBoundaryEvent));
        Assert.True(BpmnSemanticTypes.IsCatchEvent(BpmnSemanticTypes.TimerBoundaryEvent));
        Assert.True(BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(
            BpmnSemanticTypes.TimerBoundaryEvent));
        Assert.False(BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(
            BpmnSemanticTypes.TimerBoundaryEvent));
        Assert.False(BpmnIntermediateEventSemanticTypes.IsThrowEvent(
            BpmnSemanticTypes.TimerBoundaryEvent));
        Assert.False(BpmnActivitySemanticTypes.IsActivity(
            BpmnSemanticTypes.TimerBoundaryEvent));
        Assert.False(BpmnTaskSemanticTypes.IsTask(BpmnSemanticTypes.TimerBoundaryEvent));

        var boundary = Boundary();

        Assert.Equal(ActivityId, boundary.AttachedToElementId);
        Assert.Equal(
            [
                BpmnSemanticProperties.CancelActivity,
                BpmnSemanticProperties.Description,
                BpmnSemanticProperties.Name,
                BpmnSemanticProperties.TimerDefinition,
            ],
            boundary.Properties.Keys.Order(StringComparer.Ordinal));
        Assert.True(boundary.Properties[BpmnSemanticProperties.CancelActivity].BooleanValue);
        Assert.Equal("PT5M",
            boundary.Properties[BpmnSemanticProperties.TimerDefinition].TextValue);
        Assert.DoesNotContain(BpmnSemanticProperties.Code, boundary.Properties.Keys);
        Assert.DoesNotContain(BpmnSemanticProperties.ElementNumber, boundary.Properties.Keys);

        var command = new CreateBpmnTimerBoundaryEventCommand(
            DocumentId,
            Revision,
            BoundaryId,
            BoundaryVisualId,
            ActivityId,
            BoundaryAttachmentSide.Bottom,
            0.25d,
            new RectD(100d, 100d, 120d, 80d),
            "Timeout");
        Assert.Equal(ActivityId, command.AttachedToActivityId);
        Assert.Equal(BoundaryAttachmentSide.Bottom, command.Side);
        Assert.Equal(0.25d, command.PositionOnSide);
        Assert.Equal(new RectD(100d, 100d, 120d, 80d),
            command.EffectiveOwnerBounds);
        Assert.True(command.CancelActivity);
    }

    [Fact]
    public void AttachmentPlacementDerivesAllSidesAndRejectsInvalidNormalizedPositions()
    {
        var owner = new RectD(100d, 50d, 120d, 80d);
        var size = new SizeD(36d, 36d);
        Assert.Equal(new RectD(82d, 32d, 36d, 36d),
            new BoundaryAttachmentPlacement(
                BoundaryAttachmentSide.Top,
                0d).ResolveBounds(owner, size));
        Assert.Equal(new RectD(202d, 112d, 36d, 36d),
            new BoundaryAttachmentPlacement(
                BoundaryAttachmentSide.Right,
                1d).ResolveBounds(owner, size));
        Assert.Equal(new RectD(142d, 112d, 36d, 36d),
            new BoundaryAttachmentPlacement(
                BoundaryAttachmentSide.Bottom,
                0.5d).ResolveBounds(owner, size));
        Assert.Equal(new RectD(82d, 72d, 36d, 36d),
            new BoundaryAttachmentPlacement(
                BoundaryAttachmentSide.Left,
                0.5d).ResolveBounds(owner, size));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, -0.01d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 1.01d));
    }

    [Fact]
    public void StructuralValidationUsesAttachmentForReachabilityAndRejectsIncomingFlow()
    {
        var start = BpmnSemanticFactory.CreateStartEvent(
            new SemanticElementId("bpmn:n9:start"));
        var activity = BpmnSemanticFactory.CreateTask(ActivityId, "A", "Activity", 1);
        var boundary = Boundary();
        var handler = BpmnSemanticFactory.CreateTask(
            new SemanticElementId("bpmn:n9:handler"),
            "H",
            "Handler",
            2);
        var end = BpmnSemanticFactory.CreateEndEvent(
            new SemanticElementId("bpmn:n9:end"));
        var validFlows = new[]
        {
            Flow("start-activity", start.Id, activity.Id),
            Flow("activity-end", activity.Id, end.Id),
            Flow("boundary-handler", boundary.Id, handler.Id),
            Flow("handler-end", handler.Id, end.Id),
        };

        var validIssues = Validate(Snapshot(
            [start, activity, boundary, handler, end],
            validFlows));

        Assert.DoesNotContain(validIssues, issue =>
            issue.Target.SemanticElementId == BoundaryId &&
            (issue.Code == BpmnModelValidationCodes.FlowNodeIsolated ||
                issue.Code == BpmnModelValidationCodes.NodeUnreachableFromStart));
        Assert.DoesNotContain(validIssues, issue =>
            issue.Target.SemanticElementId == handler.Id &&
            issue.Code == BpmnModelValidationCodes.NodeUnreachableFromStart);

        var incoming = Flow("incoming", activity.Id, boundary.Id);
        var incomingIssue = Assert.Single(
            Validate(Snapshot([activity, boundary], [incoming])),
            issue => issue.Code ==
                BpmnModelValidationCodes.BoundaryEventIncomingSequenceFlow);
        Assert.Contains(
            "Timer Boundary Event \"Timeout\" [ID: bpmn:n9:boundary]",
            incomingIssue.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StructuralValidationReportsMissingInvalidAndScopeMismatchedOwners()
    {
        var activity = BpmnSemanticFactory.CreateTask(ActivityId, "A", "Activity", 1);
        var missing = new SemanticElementSnapshot(
            BoundaryId,
            BpmnSemanticTypes.TimerBoundaryEvent,
            BoundaryProperties());
        Assert.Contains(
            Validate(Snapshot([activity, missing])),
            issue => issue.Code ==
                BpmnModelValidationCodes.BoundaryEventAttachmentMissing);

        var startId = new SemanticElementId("bpmn:n9:start");
        var invalid = BpmnSemanticFactory.CreateTimerBoundaryEvent(
            BoundaryId,
            startId,
            "Timeout");
        Assert.Contains(
            Validate(Snapshot([BpmnSemanticFactory.CreateStartEvent(startId), invalid])),
            issue => issue.Code ==
                BpmnModelValidationCodes.BoundaryEventAttachmentOwnerInvalid);

        var scopeOwnerId = new SemanticElementId("bpmn:n9:scope-owner");
        var childScopeId = new DocumentScopeId("bpmn:n9:child-scope");
        var crossScope = Snapshot(
            [
                activity,
                BpmnSemanticFactory.CreateSubProcess(scopeOwnerId, "S", "Scope"),
                Boundary(),
            ],
            nestedScopes:
            [
                new DocumentScopeSnapshot(
                    childScopeId,
                    new DocumentScopeId(DocumentId.Value),
                    scopeOwnerId),
            ],
            memberships:
            [new SemanticElementScopeMembershipSnapshot(BoundaryId, childScopeId)]);
        Assert.Contains(
            Validate(crossScope, childScopeId),
            issue => issue.Code ==
                BpmnModelValidationCodes.BoundaryEventAttachmentScopeMismatch);
    }

    [Fact]
    public void SceneUsesDoubleCircleClockDashedBodyAndStableCandidateFeedbackKind()
    {
        var boundary = Boundary(cancelActivity: false);
        var trace = new ProjectionSourceTrace(
            DocumentId,
            new ProjectionRuleId("bpmn:n9:test-projection"),
            ProjectionSourceKind.SemanticElement,
            BoundaryId,
            BpmnSemanticTypes.TimerBoundaryEvent,
            "node",
            BoundaryVisualId);
        var node = new ProjectedNode(
            trace,
            semanticProperties: boundary.Properties,
            projectedProperties:
            [
                new(
                    "BPMN.ProjectedSemanticType",
                    PropertyValue.FromText(BpmnSemanticTypes.TimerBoundaryEvent.Value)),
            ]);
        var bounds = new RectD(120d, 120d, 36d, 36d);
        var descriptor = new Canvas2DSceneContributorDescriptor(
            new Canvas2DSceneContributorId("bpmn:n9:test-scene"),
            "1");
        var result = new BpmnCanvas2DSceneContributor().Contribute(
            new Canvas2DSceneContributionContext(
                new ProjectedGraph(DocumentId, Revision, nodes: [node]),
                new LayoutResult(
                    DocumentId,
                    Revision,
                    BpmnAlgorithmIds.DefaultLayout,
                    new LayoutComputation(
                    [
                        new LayoutNodeGeometry(
                            node.Id,
                            bounds,
                            Matrix2D.CreateTranslation(bounds.X, bounds.Y)),
                    ])),
                new RoutingResult(
                    DocumentId,
                    Revision,
                    BpmnAlgorithmIds.DefaultLayout,
                    BpmnAlgorithmIds.DefaultRouting,
                    RoutingComputation.Empty),
                new VisualModelSnapshot(DocumentId, Revision),
                new EditorStateSnapshot(
                    temporaryFeedback:
                    [
                        new EditorFeedbackSnapshot(
                            "bpmn:n9:candidate",
                            BpmnTimerBoundaryEventSceneFeedback.AttachmentCandidateKind,
                            new RectD(200d, 120d, 36d, 36d)),
                    ]),
                Canvas2DSceneConfiguration.Default,
                descriptor));

        Assert.True(result.Succeeded);
        var contribution = Assert.IsType<Canvas2DSceneContribution>(result.Contribution);
        Assert.NotEmpty(Assert.Single(
            contribution.CanonicalItemVisualOverrides).Style.DashPattern);
        var semanticMarkers = contribution.Items.Where(item =>
            item.Origin.SemanticElementId == BoundaryId).ToArray();
        Assert.Equal(4, semanticMarkers.Length);
        Assert.Contains(semanticMarkers, item =>
            item.Origin.StableSourceKey!.Contains("body-appearance", StringComparison.Ordinal) &&
            item.Style.Fill == "#ffffff" &&
            item.HitTestPolicy.Mode == Canvas2DHitTestMode.None);
        Assert.Contains(semanticMarkers, item =>
            item.Origin.StableSourceKey!.Contains("inner-ring", StringComparison.Ordinal) &&
            !item.Style.DashPattern.IsEmpty);
        Assert.Contains(semanticMarkers, item =>
            item.Origin.StableSourceKey!.Contains(":clock:", StringComparison.Ordinal));
        Assert.Equal(4, contribution.Items.Count(item =>
            item.Origin.StableSourceKey?.StartsWith(
                "feedback:bpmn:n9:candidate:",
                StringComparison.Ordinal) == true));
    }

    [Fact]
    public void SubProcessBottomCenterBoundaryAppearanceOccludesMarkerWithoutStealingHit()
    {
        var activity = ProjectedFlowNode(
            ActivityId,
            ActivityVisualId,
            BpmnSemanticTypes.SubProcess);
        var boundary = ProjectedFlowNode(
            BoundaryId,
            BoundaryVisualId,
            BpmnSemanticTypes.TimerBoundaryEvent,
            Boundary().Properties,
            NodeGeometryInteractionPolicy.AttachedBoundaryMoveFixedSize);
        var activityBounds = new RectD(100d, 100d, 120d, 80d);
        var placement = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Bottom,
            0.5d);
        var boundaryBounds = placement.ResolveBounds(activityBounds, new SizeD(36d, 36d));
        var graph = new ProjectedGraph(DocumentId, Revision, nodes: [activity, boundary]);
        var layout = new LayoutResult(
            DocumentId,
            Revision,
            BpmnAlgorithmIds.DefaultLayout,
            new LayoutComputation(
            [
                NodeGeometry(activity, activityBounds),
                NodeGeometry(boundary, boundaryBounds),
            ]));
        var visuals = new VisualModelSnapshot(
            DocumentId,
            Revision,
            [
                new VisualStateSnapshot(
                    ActivityVisualId,
                    ActivityId,
                    activityBounds.TopLeft,
                    activityBounds.Size,
                    VisualPlacementMode.Pinned),
                new VisualStateSnapshot(
                    BoundaryVisualId,
                    BoundaryId,
                    boundaryBounds.TopLeft,
                    boundaryBounds.Size,
                    VisualPlacementMode.Manual,
                    boundaryAttachment: placement),
            ]);
        var result = new Canvas2DSceneBuilder(
            contributors: BpmnPluginRegistration.N90.SceneContributors).Build(
                graph,
                layout,
                new RoutingResult(
                    DocumentId,
                    Revision,
                    BpmnAlgorithmIds.DefaultLayout,
                    BpmnAlgorithmIds.DefaultRouting,
                    RoutingComputation.Empty),
                visuals,
                EditorStateSnapshot.Empty);

        Assert.True(result.Succeeded);
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var activityMarkers = scene.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Decoration &&
            item.Origin.SemanticElementId == ActivityId).ToArray();
        Assert.Equal(2, activityMarkers.Length);
        var activityMarkerBox = Assert.Single(activityMarkers, item =>
            item.Origin.StableSourceKey?.Contains(
                "marker-box",
                StringComparison.Ordinal) == true);
        var activityMarkerDocumentBounds = activityMarkerBox.Geometry.Bounds.Translate(
            new VectorD(activityMarkerBox.Transform.OffsetX, activityMarkerBox.Transform.OffsetY));
        Assert.True(activityMarkerDocumentBounds.Intersects(boundaryBounds));

        var boundaryAppearance = Assert.Single(scene.Items, item =>
            item.Origin.SemanticElementId == BoundaryId &&
            item.Origin.StableSourceKey?.Contains(
                "body-appearance",
                StringComparison.Ordinal) == true);
        var boundaryInnerRing = Assert.Single(scene.Items, item =>
            item.Origin.SemanticElementId == BoundaryId &&
            item.Origin.StableSourceKey?.Contains(
                "inner-ring",
                StringComparison.Ordinal) == true);
        Assert.Equal(Canvas2DSceneLayer.Decoration, boundaryAppearance.Layer);
        Assert.Equal(99, boundaryAppearance.ZIndex);
        Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, boundaryAppearance.Geometry.Kind);
        Assert.Equal(new RectD(0d, 0d, 36d, 36d), boundaryAppearance.Geometry.Bounds);
        Assert.Equal(boundaryBounds, boundaryAppearance.Bounds);
        Assert.Equal("#ffffff", boundaryAppearance.Style.Fill);
        Assert.Equal("#000000", boundaryAppearance.Style.Stroke);
        Assert.Equal(Canvas2DHitTestMode.None, boundaryAppearance.HitTestPolicy.Mode);
        Assert.All(activityMarkers, marker =>
        {
            Assert.True(marker.ZIndex < boundaryAppearance.ZIndex);
            Assert.True(scene.Items.IndexOf(marker) < scene.Items.IndexOf(boundaryAppearance));
        });
        Assert.True(boundaryAppearance.ZIndex < boundaryInnerRing.ZIndex);
        Assert.True(
            scene.Items.IndexOf(boundaryAppearance) < scene.Items.IndexOf(boundaryInnerRing));

        var canonicalBodyId = Canvas2DSceneObjectIdentity.ForProjected(boundary.Id, "node");
        var canonicalBody = Assert.Single(scene.Items, item => item.Id == canonicalBodyId);
        Assert.Equal(Canvas2DSceneLayer.Content, canonicalBody.Layer);
        Assert.Equal(1, canonicalBody.ZIndex);
        Assert.Equal(Canvas2DHitTestMode.FillOrStroke, canonicalBody.HitTestPolicy.Mode);
        var hit = new Canvas2DSceneHitTestService().HitTest(
            scene,
            new PointD(
                boundaryBounds.X + (boundaryBounds.Width / 2d),
                boundaryBounds.Y + (boundaryBounds.Height / 2d)));
        Assert.NotNull(hit);
        Assert.Equal(canonicalBodyId, hit.SceneObjectId);
    }

    [Fact]
    public void SceneBuilderAcceptsNamespacedNonHittableTimerBoundaryCandidatePreview()
    {
        const string feedbackId = "bpmn:n9:builder-candidate";
        var registration = Assert.Single(BpmnPluginRegistration.N90.SceneContributors);
        var editorState = new EditorStateSnapshot(
            temporaryFeedback:
            [
                new EditorFeedbackSnapshot(
                    feedbackId,
                    BpmnTimerBoundaryEventSceneFeedback.AttachmentCandidateKind,
                    new RectD(200d, 120d, 36d, 36d),
                    properties:
                    [
                        new(
                            BpmnSemanticProperties.CancelActivity,
                            PropertyValue.FromBoolean(true)),
                    ]),
            ]);

        var result = new Canvas2DSceneBuilder(
            contributors: BpmnPluginRegistration.N90.SceneContributors).Build(
                new ProjectedGraph(DocumentId, Revision),
                new LayoutResult(
                    DocumentId,
                    Revision,
                    BpmnAlgorithmIds.DefaultLayout,
                    LayoutComputation.Empty),
                new RoutingResult(
                    DocumentId,
                    Revision,
                    BpmnAlgorithmIds.DefaultLayout,
                    BpmnAlgorithmIds.DefaultRouting,
                    RoutingComputation.Empty),
                new VisualModelSnapshot(DocumentId, Revision),
                editorState);

        Assert.True(result.Succeeded);
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var previewItems = scene.Items.Where(item =>
            item.Origin.StableSourceKey?.StartsWith(
                $"feedback:{feedbackId}:",
                StringComparison.Ordinal) == true).ToArray();

        Assert.Equal(4, previewItems.Length);
        Assert.Equal(
            ["body", "clock", "clock-hands", "inner-ring"],
            previewItems
                .Select(item => item.Origin.StableSourceKey!.Split(':')[^1])
                .Order(StringComparer.Ordinal));
        Assert.All(previewItems, item =>
        {
            Assert.Equal(Canvas2DSceneLayer.Overlay, item.Layer);
            Assert.Equal(Canvas2DHitTestMode.None, item.HitTestPolicy.Mode);
            Assert.Equal(
                Canvas2DSceneOriginCategory.EditorState |
                Canvas2DSceneOriginCategory.RegisteredExtension,
                item.Origin.Categories);
            Assert.Equal(
                Canvas2DSceneObjectIdentity.ForExtension(
                    registration.Descriptor.ContributorId,
                    item.Origin.StableSourceKey!),
                item.Id);
        });
    }

    private static SemanticElementSnapshot Boundary(
        bool cancelActivity = true) =>
        BpmnSemanticFactory.CreateTimerBoundaryEvent(
            BoundaryId,
            ActivityId,
            "Timeout",
            "PT5M",
            cancelActivity,
            "Escalate delay.");

    private static ProjectedNode ProjectedFlowNode(
        SemanticElementId elementId,
        VisualStateId visualStateId,
        SemanticTypeId semanticTypeId,
        IEnumerable<KeyValuePair<string, PropertyValue>>? semanticProperties = null,
        NodeGeometryInteractionPolicy geometryInteractionPolicy =
            NodeGeometryInteractionPolicy.FreeMoveAndResize)
    {
        var trace = new ProjectionSourceTrace(
            DocumentId,
            new ProjectionRuleId($"bpmn:n9:test:{elementId.Value}"),
            ProjectionSourceKind.SemanticElement,
            elementId,
            semanticTypeId,
            "node",
            visualStateId);
        return new ProjectedNode(
            trace,
            semanticProperties: semanticProperties,
            projectedProperties:
            [
                new(
                    "BPMN.ProjectedSemanticType",
                    PropertyValue.FromText(semanticTypeId.Value)),
            ],
            geometryInteractionPolicy: geometryInteractionPolicy);
    }

    private static LayoutNodeGeometry NodeGeometry(ProjectedNode node, RectD bounds) =>
        new(
            node.Id,
            bounds,
            Matrix2D.CreateTranslation(bounds.X, bounds.Y));

    private static IEnumerable<KeyValuePair<string, PropertyValue>> BoundaryProperties() =>
    [
        new(BpmnSemanticProperties.Name, PropertyValue.FromText("Timeout")),
        new(BpmnSemanticProperties.TimerDefinition, PropertyValue.FromText("PT5M")),
        new(BpmnSemanticProperties.CancelActivity, PropertyValue.FromBoolean(true)),
    ];

    private static SemanticRelationshipSnapshot Flow(
        string id,
        SemanticElementId sourceId,
        SemanticElementId targetId) =>
        BpmnSemanticFactory.CreateSequenceFlow(
            new SemanticElementId($"bpmn:n9:{id}"),
            sourceId,
            targetId);

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot>? relationships = null,
        IEnumerable<VisualStateSnapshot>? visuals = null,
        IEnumerable<DocumentScopeSnapshot>? nestedScopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                Revision,
                elements,
                relationships,
                nestedScopes,
                memberships),
            new VisualModelSnapshot(DocumentId, Revision, visuals),
            new DocumentMetadataSnapshot(DocumentId, Revision));

    private static System.Collections.Immutable.ImmutableArray<ModelValidationIssue>
        Validate(DocumentSnapshot document, DocumentScopeId? activeScopeId = null) =>
        new BpmnStructuralValidationRule().Validate(
            activeScopeId is null
                ? new ModelValidationContext(document)
                : new ModelValidationContext(document, activeScopeId));
}
