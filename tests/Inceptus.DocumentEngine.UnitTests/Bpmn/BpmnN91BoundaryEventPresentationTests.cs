using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Scene;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Creation;
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
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN91BoundaryEventPresentationTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n9.1:presentation");
    private static readonly SemanticElementId ActivityId = new("bpmn:n9.1:activity");
    private static readonly RectD ActivityBounds = new(100d, 100d, 120d, 80d);
    private static readonly SemanticTypeId[] NewBoundaryTypes =
    [
        BpmnSemanticTypes.MessageBoundaryEvent,
        BpmnSemanticTypes.SignalBoundaryEvent,
    ];
    private static readonly string[] MessagePreviewKeys =
        ["body", "envelope", "envelope-flap", "inner-ring"];
    private static readonly string[] TimerPreviewKeys =
        ["body", "clock", "clock-hands", "inner-ring"];
    private static readonly string[] SignalPreviewKeys =
        ["body", "inner-ring", "signal"];

    [Fact]
    public void N91ToolboxHasExactStableEventOrderAndPreservesN90Definition()
    {
        var n90Definition = Assert.Single(
            BpmnPluginRegistration.N90.ToolboxContributions);
        var n91Definition = Assert.Single(
            BpmnPluginRegistration.N91.ToolboxContributions);
        var n90EventItems = EventItems(n90Definition);
        var n91EventItems = EventItems(n91Definition);

        Assert.Equal(
            [
                "Start Event",
                "Message Catch Event",
                "Message Throw Event",
                "Timer Catch Event",
                "Timer Boundary Event",
                "Signal Catch Event",
                "Signal Throw Event",
                "End Event",
            ],
            n90EventItems.Select(static item => item.DisplayName));
        Assert.Equal(
            [
                "Start Event",
                "Message Catch Event",
                "Message Throw Event",
                "Message Boundary Event",
                "Timer Catch Event",
                "Timer Boundary Event",
                "Signal Catch Event",
                "Signal Throw Event",
                "Signal Boundary Event",
                "End Event",
            ],
            n91EventItems.Select(static item => item.DisplayName));
        Assert.Equal(Enumerable.Range(0, 10), n91EventItems.Select(static item => item.Order));

        AssertToolboxItem(
            n91EventItems[3],
            "bpmn:toolbox:message-boundary-event",
            BpmnSemanticTypes.MessageBoundaryEvent,
            "bpmn:message-boundary-event",
            "▭◎▱");
        AssertToolboxItem(
            n91EventItems[8],
            "bpmn:toolbox:signal-boundary-event",
            BpmnSemanticTypes.SignalBoundaryEvent,
            "bpmn:signal-boundary-event",
            "▭◎△");
        Assert.Equal(
            n90Definition.Groups.Select(static group => group.GroupId),
            n91Definition.Groups.Select(static group => group.GroupId));
        Assert.Equal(
            n90Definition.Sections.Select(static section => section.SectionId),
            n91Definition.Sections.Select(static section => section.SectionId));
    }

    [Fact]
    public void N91ProjectionSchemasAndAnchorPoliciesAreExactAndSourceOnly()
    {
        var n91 = BpmnPluginRegistration.N91;
        Assert.Equal(
            "bpmn:projection/message-boundary-event",
            Assert.Single(n91.ProjectionRules, static registration =>
                registration.SemanticTypeId ==
                    BpmnSemanticTypes.MessageBoundaryEvent).RuleId.Value);
        Assert.Equal(
            "bpmn:projection/signal-boundary-event",
            Assert.Single(n91.ProjectionRules, static registration =>
                registration.SemanticTypeId ==
                    BpmnSemanticTypes.SignalBoundaryEvent).RuleId.Value);

        AssertBoundarySchema(
            Assert.Single(n91.PropertiesSchemas, static schema =>
                schema.SemanticTypeId == BpmnSemanticTypes.MessageBoundaryEvent),
            BpmnSemanticTypes.MessageBoundaryEvent);
        AssertBoundarySchema(
            Assert.Single(n91.PropertiesSchemas, static schema =>
                schema.SemanticTypeId == BpmnSemanticTypes.SignalBoundaryEvent),
            BpmnSemanticTypes.SignalBoundaryEvent);

        Assert.Equal(
            BpmnPluginRegistration.N90.ConnectorAnchorPolicies.Select(
                static registration => registration.ElementTypeId),
            n91.ConnectorAnchorPolicies
                .Take(BpmnPluginRegistration.N90.ConnectorAnchorPolicies.Length)
                .Select(static registration => registration.ElementTypeId));
        var registry = new ElementConnectorAnchorPolicyRegistry(
            n91.ConnectorAnchorPolicies);
        foreach (var typeId in NewBoundaryTypes)
        {
            var policy = registry.Resolve(typeId);
            foreach (var side in Enum.GetValues<ConnectorAnchorSide>())
            {
                var edge = policy.ForSide(side);
                Assert.Equal(ConnectorAnchorPolicyMode.DynamicUnlimited, edge.Mode);
                Assert.Equal(ConnectorAnchorRoleCapability.Source, edge.AllowedRoles);
                Assert.True(edge.Allows(ConnectorAnchorRole.Source));
                Assert.False(edge.Allows(ConnectorAnchorRole.Target));
            }
        }
    }

    [Fact]
    public void N91BoundaryPlacementUsesTypeSpecificContributorOnlyCandidatesAndCommands()
    {
        Assert.Equal(
            BpmnPluginRegistration.N90.ToolboxPlacementRegistrations.Select(
                static registration => registration.ToolboxItemId),
            BpmnPluginRegistration.N91.ToolboxPlacementRegistrations
                .Take(BpmnPluginRegistration.N90.ToolboxPlacementRegistrations.Length)
                .Select(static registration => registration.ToolboxItemId));

        var document = Document(BpmnSemanticFactory.CreateTask(
            ActivityId,
            "ACTIVITY",
            "Activity",
            1L));
        var target = new ToolboxPlacementTarget(
            ActivityId,
            BpmnSemanticTypes.Task,
            new VisualStateId("bpmn:n9.1:activity:visual"),
            new ProjectedObjectId("bpmn:n9.1:activity:projected"),
            ActivityBounds);
        var cases = new[]
        {
            new PlacementCase(
                new ToolboxItemId("bpmn:toolbox:message-boundary-event"),
                BpmnMessageBoundaryEventSceneFeedback.AttachmentCandidateKind,
                "BpmnMessageBoundaryEventPlacementCandidateProvider",
                "BpmnMessageBoundaryEventToolboxPlacementCommandFactory"),
            new PlacementCase(
                new ToolboxItemId("bpmn:toolbox:timer-boundary-event"),
                BpmnTimerBoundaryEventSceneFeedback.AttachmentCandidateKind,
                "BpmnTimerBoundaryEventPlacementCandidateProvider",
                "BpmnTimerBoundaryEventToolboxPlacementCommandFactory"),
            new PlacementCase(
                new ToolboxItemId("bpmn:toolbox:signal-boundary-event"),
                BpmnSignalBoundaryEventSceneFeedback.AttachmentCandidateKind,
                "BpmnSignalBoundaryEventPlacementCandidateProvider",
                "BpmnSignalBoundaryEventToolboxPlacementCommandFactory"),
        };

        foreach (var placementCase in cases)
        {
            var registration = Assert.Single(
                BpmnPluginRegistration.N91.ToolboxPlacementRegistrations,
                registration => registration.ToolboxItemId == placementCase.ItemId);
            Assert.Equal(
                placementCase.ProviderTypeName,
                registration.CandidateProvider?.GetType().Name);
            Assert.Equal(
                placementCase.FactoryTypeName,
                registration.CommandFactory.GetType().Name);
            var identity = new DocumentCreationIdentity(
                new SemanticElementId($"{placementCase.ItemId.Value}:created"),
                new VisualStateId($"{placementCase.ItemId.Value}:created:visual"));
            var identityProvider = new FixedIdentityProvider(identity);
            var previewRequest = Request(
                placementCase.ItemId,
                document,
                target,
                identityProvider);
            var candidate = Assert.IsType<ToolboxPlacementCandidate>(
                registration.CandidateProvider!.ResolveCandidate(previewRequest));

            Assert.Equal(placementCase.FeedbackKind, candidate.FeedbackKind);
            Assert.Equal(
                EditorFeedbackPresentationMode.ContributorOnly,
                candidate.FeedbackPresentationMode);
            Assert.Equal(new RectD(202d, 122d, 36d, 36d), candidate.PreviewBounds);
            Assert.True(candidate.Properties[BpmnSemanticProperties.CancelActivity].BooleanValue);

            var planResult = registration.CommandFactory.CreatePlan(Request(
                placementCase.ItemId,
                document,
                target,
                identityProvider,
                candidate));
            Assert.True(planResult.Succeeded);
            var plan = Assert.IsType<ToolboxPlacementPlan>(planResult.Plan);
            AssertBoundaryCommand(plan.Command, placementCase.ItemId);
        }
    }

    [Fact]
    public void PermanentMessageAndSignalBoundaryEventsUseCanonicalOutlineMarkers()
    {
        var messageId = new SemanticElementId("bpmn:n9.1:message-boundary");
        var signalId = new SemanticElementId("bpmn:n9.1:signal-boundary");
        var message = ProjectedBoundary(
            messageId,
            BpmnSemanticTypes.MessageBoundaryEvent,
            BpmnSemanticFactory.CreateMessageBoundaryEvent(
                messageId,
                ActivityId,
                "Message",
                cancelActivity: false).Properties);
        var signal = ProjectedBoundary(
            signalId,
            BpmnSemanticTypes.SignalBoundaryEvent,
            BpmnSemanticFactory.CreateSignalBoundaryEvent(
                signalId,
                ActivityId,
                "Signal",
                cancelActivity: false).Properties);
        var result = new BpmnCanvas2DSceneContributor().Contribute(
            SceneContext(
                [message, signal],
                [
                    NodeGeometry(message, new RectD(100d, 100d, 36d, 36d)),
                    NodeGeometry(signal, new RectD(160d, 100d, 36d, 36d)),
                ],
                EditorStateSnapshot.Empty));

        Assert.True(
            result.Succeeded,
            string.Join(" | ", result.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var contribution = Assert.IsType<Canvas2DSceneContribution>(result.Contribution);
        Assert.Equal(2, contribution.CanonicalItemVisualOverrides.Length);
        Assert.All(contribution.CanonicalItemVisualOverrides, visualOverride =>
        {
            Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, visualOverride.Geometry.Kind);
            Assert.NotEmpty(visualOverride.Style.DashPattern);
        });

        var messageItems = SemanticItems(contribution, messageId);
        Assert.Equal(4, messageItems.Length);
        AssertOutlineBoundaryBody(messageItems);
        var envelope = Assert.Single(messageItems, item =>
            item.Origin.StableSourceKey!.Contains(":envelope:", StringComparison.Ordinal));
        Assert.Equal("#ffffff", envelope.Style.Fill);
        Assert.Contains(messageItems, item =>
            item.Origin.StableSourceKey!.Contains("envelope-flap", StringComparison.Ordinal));

        var signalItems = SemanticItems(contribution, signalId);
        Assert.Equal(3, signalItems.Length);
        AssertOutlineBoundaryBody(signalItems);
        var signalMarker = Assert.Single(signalItems, item =>
            item.Origin.StableSourceKey!.Contains(":signal:", StringComparison.Ordinal));
        Assert.Equal("#ffffff", signalMarker.Style.Fill);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, signalMarker.Geometry.Kind);
    }

    [Fact]
    public void InterruptingMessageAndSignalBoundaryEventsUseSolidRings()
    {
        foreach (var typeId in NewBoundaryTypes)
        {
            var elementId = new SemanticElementId(
                $"bpmn:n9.1:interrupting:{typeId.Value}");
            var properties = typeId == BpmnSemanticTypes.MessageBoundaryEvent
                ? BpmnSemanticFactory.CreateMessageBoundaryEvent(
                    elementId,
                    ActivityId,
                    "Message",
                    cancelActivity: true).Properties
                : BpmnSemanticFactory.CreateSignalBoundaryEvent(
                    elementId,
                    ActivityId,
                    "Signal",
                    cancelActivity: true).Properties;
            var node = ProjectedBoundary(elementId, typeId, properties);
            var result = new BpmnCanvas2DSceneContributor().Contribute(
                SceneContext(
                    [node],
                    [NodeGeometry(node, new RectD(100d, 100d, 36d, 36d))],
                    EditorStateSnapshot.Empty));

            Assert.True(result.Succeeded);
            var contribution = Assert.IsType<Canvas2DSceneContribution>(
                result.Contribution);
            Assert.Empty(Assert.Single(
                contribution.CanonicalItemVisualOverrides).Style.DashPattern);
            var ring = Assert.Single(SemanticItems(contribution, elementId), item =>
                item.Origin.StableSourceKey?.Contains(
                    "inner-ring",
                    StringComparison.Ordinal) == true);
            Assert.Empty(ring.Style.DashPattern);
        }
    }

    [Fact]
    public void BoundaryBodyWinsHitTestingOverItsActivityAndMarkersStayNonHittable()
    {
        var boundaryId = new SemanticElementId("bpmn:n9.1:hit:message-boundary");
        var activity = ProjectedNode(
            ActivityId,
            BpmnSemanticTypes.Task,
            BpmnSemanticFactory.CreateTask(
                ActivityId,
                "ACTIVITY",
                "Activity",
                1).Properties,
            NodeGeometryInteractionPolicy.FreeMoveAndResize);
        var boundary = ProjectedBoundary(
            boundaryId,
            BpmnSemanticTypes.MessageBoundaryEvent,
            BpmnSemanticFactory.CreateMessageBoundaryEvent(
                boundaryId,
                ActivityId,
                "Message").Properties);
        var activityBounds = new RectD(100d, 100d, 120d, 80d);
        var placement = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Bottom,
            0.5d);
        var boundaryBounds = placement.ResolveBounds(
            activityBounds,
            new SizeD(36d, 36d));
        var result = new Canvas2DSceneBuilder(
            contributors: BpmnPluginRegistration.N91.SceneContributors).Build(
                new ProjectedGraph(
                    DocumentId,
                    DocumentRevision.Zero,
                    nodes: [activity, boundary]),
                new LayoutResult(
                    DocumentId,
                    DocumentRevision.Zero,
                    BpmnAlgorithmIds.DefaultLayout,
                    new LayoutComputation(
                    [
                        NodeGeometry(activity, activityBounds),
                        NodeGeometry(boundary, boundaryBounds),
                    ])),
                new RoutingResult(
                    DocumentId,
                    DocumentRevision.Zero,
                    BpmnAlgorithmIds.DefaultLayout,
                    BpmnAlgorithmIds.DefaultRouting,
                    RoutingComputation.Empty),
                new VisualModelSnapshot(
                    DocumentId,
                    DocumentRevision.Zero,
                    [
                        new VisualStateSnapshot(
                            new VisualStateId("bpmn:n9.1:activity:visual"),
                            ActivityId,
                            activityBounds.TopLeft,
                            activityBounds.Size,
                            VisualPlacementMode.Pinned),
                        new VisualStateSnapshot(
                            new VisualStateId("bpmn:n9.1:hit:message-boundary:visual"),
                            boundaryId,
                            boundaryBounds.TopLeft,
                            boundaryBounds.Size,
                            VisualPlacementMode.Manual,
                            boundaryAttachment: placement),
                    ]),
                EditorStateSnapshot.Empty);

        Assert.True(
            result.Succeeded,
            string.Join(" | ", result.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var expectedBodyId = Canvas2DSceneObjectIdentity.ForProjected(
            boundary.Id,
            "node");
        var hit = new Canvas2DSceneHitTestService().HitTest(
            scene,
            new PointD(
                boundaryBounds.X + (boundaryBounds.Width / 2d),
                boundaryBounds.Y + (boundaryBounds.Height / 2d)));

        Assert.NotNull(hit);
        Assert.Equal(expectedBodyId, hit.SceneObjectId);
        Assert.All(
            scene.Items.Where(item =>
                item.Origin.SemanticElementId == boundaryId &&
                item.Id != expectedBodyId),
            item => Assert.Equal(
                Canvas2DHitTestMode.None,
                item.HitTestPolicy.Mode));
    }

    [Fact]
    public void ContributorOnlyBoundaryPreviewsHaveCanonicalShapesAndNoGenericRectangle()
    {
        var cases = new[]
        {
            new PreviewCase(
                "message",
                BpmnMessageBoundaryEventSceneFeedback.AttachmentCandidateKind,
                MessagePreviewKeys),
            new PreviewCase(
                "timer",
                BpmnTimerBoundaryEventSceneFeedback.AttachmentCandidateKind,
                TimerPreviewKeys),
            new PreviewCase(
                "signal",
                BpmnSignalBoundaryEventSceneFeedback.AttachmentCandidateKind,
                SignalPreviewKeys),
        };
        var editorState = new EditorStateSnapshot(
            temporaryFeedback: cases.Select((previewCase, index) =>
                new EditorFeedbackSnapshot(
                    $"bpmn:n9.1:{previewCase.Id}",
                    previewCase.FeedbackKind,
                    new RectD(100d + (index * 50d), 100d, 36d, 36d),
                    properties:
                    [
                        new(
                            BpmnSemanticProperties.CancelActivity,
                            PropertyValue.FromBoolean(false)),
                    ],
                    presentationMode: EditorFeedbackPresentationMode.ContributorOnly)));
        var result = new Canvas2DSceneBuilder(
            contributors: BpmnPluginRegistration.N91.SceneContributors).Build(
                new ProjectedGraph(DocumentId, DocumentRevision.Zero),
                new LayoutResult(
                    DocumentId,
                    DocumentRevision.Zero,
                    BpmnAlgorithmIds.DefaultLayout,
                    LayoutComputation.Empty),
                new RoutingResult(
                    DocumentId,
                    DocumentRevision.Zero,
                    BpmnAlgorithmIds.DefaultLayout,
                    BpmnAlgorithmIds.DefaultRouting,
                    RoutingComputation.Empty),
                new VisualModelSnapshot(DocumentId, DocumentRevision.Zero),
                editorState);

        Assert.True(result.Succeeded);
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        foreach (var previewCase in cases)
        {
            var prefix = $"feedback:bpmn:n9.1:{previewCase.Id}";
            Assert.DoesNotContain(scene.Items, item =>
                string.Equals(
                    item.Origin.StableSourceKey,
                    prefix,
                    StringComparison.Ordinal));
            var items = scene.Items.Where(item =>
                item.Origin.StableSourceKey?.StartsWith(
                    $"{prefix}:",
                    StringComparison.Ordinal) == true).ToArray();
            Assert.Equal(previewCase.LocalKeys.Length, items.Length);
            Assert.Equal(
                previewCase.LocalKeys,
                items.Select(item => item.Origin.StableSourceKey!.Split(':')[^1])
                    .Order(StringComparer.Ordinal));
            Assert.All(items, item =>
            {
                Assert.Equal(Canvas2DSceneLayer.Overlay, item.Layer);
                Assert.Equal(Canvas2DHitTestMode.None, item.HitTestPolicy.Mode);
            });
            var body = Assert.Single(items, item =>
                item.Origin.StableSourceKey!.EndsWith(":body", StringComparison.Ordinal));
            Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, body.Geometry.Kind);
            Assert.NotEmpty(body.Style.DashPattern);
        }
    }

    private static ToolboxItemDefinition[] EventItems(ToolboxContribution contribution)
    {
        var groupId = Assert.Single(
            contribution.Groups,
            static group => group.DisplayName == "Events").GroupId;
        return contribution.Items
            .Where(item => item.GroupId == groupId)
            .ToArray();
    }

    private static void AssertToolboxItem(
        ToolboxItemDefinition item,
        string itemId,
        SemanticTypeId semanticTypeId,
        string iconKey,
        string fallbackGlyph)
    {
        Assert.Equal(itemId, item.ItemId.Value);
        Assert.Equal(semanticTypeId, item.ElementTypeId);
        Assert.Equal(iconKey, item.Icon.IconKey);
        Assert.Equal(fallbackGlyph, item.Icon.FallbackGlyph);
    }

    private static void AssertBoundarySchema(
        ElementPropertiesSchema schema,
        SemanticTypeId semanticTypeId)
    {
        Assert.Equal(semanticTypeId, schema.SemanticTypeId);
        Assert.Equal(
            ["name", "interrupting", "description"],
            schema.Fields.Select(static field => field.FieldId.Value));
        Assert.Equal(Enumerable.Range(0, 3), schema.Fields.Select(static field => field.Order));
        Assert.All(schema.Fields, static field => Assert.True(field.IsEditable));
        Assert.Equal(
            ElementPropertyEditorKind.Boolean,
            schema.Fields[1].EditorKind);
        Assert.Equal(
            BpmnSemanticProperties.CancelActivity,
            schema.Fields[1].SemanticPropertyKey);
    }

    private static ToolboxPlacementRequest Request(
        ToolboxItemId itemId,
        DocumentSnapshot document,
        ToolboxPlacementTarget target,
        IDocumentCreationIdentityProvider identityProvider,
        ToolboxPlacementCandidate? candidate = null) =>
        new(
            itemId,
            document,
            document.Revision,
            new PointD(ActivityBounds.Right, 140d),
            identityProvider,
            visibleTargets: [target],
            candidate: candidate);

    private static void AssertBoundaryCommand(
        Inceptus.DocumentEngine.Contracts.Commands.ICommand command,
        ToolboxItemId itemId)
    {
        if (StringComparer.Ordinal.Equals(
                itemId.Value,
                "bpmn:toolbox:message-boundary-event"))
        {
            var typed = Assert.IsType<CreateBpmnMessageBoundaryEventCommand>(command);
            Assert.Equal("Message Boundary Event 1", typed.Name);
            Assert.Equal("Message Boundary Event created from the Toolbox.", typed.Description);
            AssertCommonBoundaryCommand(typed.AttachedToActivityId, typed.EffectiveOwnerBounds);
            return;
        }

        if (StringComparer.Ordinal.Equals(
                itemId.Value,
                "bpmn:toolbox:timer-boundary-event"))
        {
            var typed = Assert.IsType<CreateBpmnTimerBoundaryEventCommand>(command);
            Assert.Equal("Timer Boundary Event 1", typed.Name);
            Assert.Equal("Timer Boundary Event created from the Toolbox.", typed.Description);
            AssertCommonBoundaryCommand(typed.AttachedToActivityId, typed.EffectiveOwnerBounds);
            return;
        }

        var signal = Assert.IsType<CreateBpmnSignalBoundaryEventCommand>(command);
        Assert.Equal("Signal Boundary Event 1", signal.Name);
        Assert.Equal("Signal Boundary Event created from the Toolbox.", signal.Description);
        AssertCommonBoundaryCommand(signal.AttachedToActivityId, signal.EffectiveOwnerBounds);
    }

    private static void AssertCommonBoundaryCommand(
        SemanticElementId attachedToActivityId,
        RectD effectiveOwnerBounds)
    {
        Assert.Equal(ActivityId, attachedToActivityId);
        Assert.Equal(ActivityBounds, effectiveOwnerBounds);
    }

    private static ProjectedNode ProjectedBoundary(
        SemanticElementId id,
        SemanticTypeId typeId,
        IEnumerable<KeyValuePair<string, PropertyValue>> properties)
    {
        return ProjectedNode(
            id,
            typeId,
            properties,
            NodeGeometryInteractionPolicy.AttachedBoundaryMoveFixedSize);
    }

    private static ProjectedNode ProjectedNode(
        SemanticElementId id,
        SemanticTypeId typeId,
        IEnumerable<KeyValuePair<string, PropertyValue>> properties,
        NodeGeometryInteractionPolicy geometryInteractionPolicy)
    {
        var visualId = new VisualStateId($"{id.Value}:visual");
        var ruleId = Assert.Single(
            BpmnPluginRegistration.N91.ProjectionRules,
            registration => registration.SemanticTypeId == typeId).RuleId;
        return new ProjectedNode(
            new ProjectionSourceTrace(
                DocumentId,
                ruleId,
                ProjectionSourceKind.SemanticElement,
                id,
                typeId,
                "node",
                visualId),
            semanticProperties: properties,
            projectedProperties:
            [
                new(
                    "BPMN.ProjectedSemanticType",
                    PropertyValue.FromText(typeId.Value)),
            ],
            geometryInteractionPolicy: geometryInteractionPolicy);
    }

    private static LayoutNodeGeometry NodeGeometry(ProjectedNode node, RectD bounds) =>
        new(
            node.Id,
            bounds,
            Matrix2D.CreateTranslation(bounds.X, bounds.Y));

    private static Canvas2DSceneContributionContext SceneContext(
        IEnumerable<ProjectedNode> nodes,
        IEnumerable<LayoutNodeGeometry> geometries,
        EditorStateSnapshot editorState) =>
        new(
            new ProjectedGraph(DocumentId, DocumentRevision.Zero, nodes: nodes),
            new LayoutResult(
                DocumentId,
                DocumentRevision.Zero,
                BpmnAlgorithmIds.DefaultLayout,
                new LayoutComputation(geometries)),
            new RoutingResult(
                DocumentId,
                DocumentRevision.Zero,
                BpmnAlgorithmIds.DefaultLayout,
                BpmnAlgorithmIds.DefaultRouting,
                RoutingComputation.Empty),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero),
            editorState,
            Canvas2DSceneConfiguration.Default,
            new Canvas2DSceneContributorDescriptor(
                new Canvas2DSceneContributorId("bpmn:n9.1:test"),
                "1"));

    private static Canvas2DSceneItem[] SemanticItems(
        Canvas2DSceneContribution contribution,
        SemanticElementId id) =>
        contribution.Items.Where(item => item.Origin.SemanticElementId == id).ToArray();

    private static void AssertOutlineBoundaryBody(Canvas2DSceneItem[] items)
    {
        var body = Assert.Single(items, item =>
            item.Origin.StableSourceKey!.Contains("body-appearance", StringComparison.Ordinal));
        var ring = Assert.Single(items, item =>
            item.Origin.StableSourceKey!.Contains("inner-ring", StringComparison.Ordinal));
        Assert.Equal("#ffffff", body.Style.Fill);
        Assert.Equal(Canvas2DHitTestMode.None, body.HitTestPolicy.Mode);
        Assert.NotEmpty(body.Style.DashPattern);
        Assert.NotEmpty(ring.Style.DashPattern);
    }

    private static DocumentSnapshot Document(params SemanticElementSnapshot[] elements) =>
        new(
            new SemanticModelSnapshot(DocumentId, DocumentRevision.Zero, elements),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));

    private sealed class FixedIdentityProvider(DocumentCreationIdentity identity) :
        IDocumentCreationIdentityProvider
    {
        public DocumentCreationIdentity CreateIdentity() => identity;
    }

    private sealed record PlacementCase(
        ToolboxItemId ItemId,
        string FeedbackKind,
        string ProviderTypeName,
        string FactoryTypeName);

    private sealed record PreviewCase(
        string Id,
        string FeedbackKind,
        string[] LocalKeys);
}
