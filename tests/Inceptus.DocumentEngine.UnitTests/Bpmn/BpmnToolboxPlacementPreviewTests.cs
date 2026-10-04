using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
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

public sealed class BpmnToolboxPlacementPreviewTests
{
    public static IEnumerable<object[]> OrdinaryTools() =>
        BpmnPluginRegistration.N100.ToolboxPlacementRegistrations
            .Where(static registration => registration.CandidateProvider is null)
            .Select(static registration => new object[] { registration.ToolboxItemId.Value });

    public static IEnumerable<object[]> AllTools() =>
        BpmnPluginRegistration.N100.ToolboxPlacementRegistrations
            .Select(static registration => new object[] { registration.ToolboxItemId.Value });

    [Theory]
    [MemberData(nameof(OrdinaryTools))]
    public void EveryOrdinaryPreviewMatchesTheRealCommandDefaultsWithoutAllocating(string itemId)
    {
        var registration = Registration(itemId);
        var document = Document();
        var pointer = new PointD(500d, 300d);
        var provider = Assert.IsAssignableFrom<IToolboxPlacementPreviewProvider>(registration.PreviewProvider);
        var identities = new Identities();
        var preview = provider.Evaluate(new(registration.ToolboxItemId, document, pointer));
        Assert.True(preview.IsAllowed);
        Assert.Equal(pointer, preview.Hotspot);
        Assert.Null(preview.AttachmentCandidate);
        Assert.Equal(0, identities.Count);
        Assert.Equal(preview, provider.Evaluate(new(registration.ToolboxItemId, document, pointer)));
        var planned = registration.CommandFactory.CreatePlan(new(registration.ToolboxItemId, document,
            document.Revision, pointer, identities));
        Assert.True(planned.Succeeded);
        var command = Assert.IsAssignableFrom<BpmnElementCreationCommand>(planned.Plan!.Command);
        Assert.Equal(preview.Bounds.TopLeft, command.Position);
        Assert.Equal(preview.Bounds.Size, command.Size);
        Assert.Equal(preview.Label, command.GetType().GetProperty("Name")!.GetValue(command));
        Assert.Equal(1, identities.Count);
        var expected = BpmnActivitySemanticTypes.IsActivity(preview.SemanticTypeId) ? new SizeD(120d, 80d)
            : preview.SemanticTypeId == BpmnSemanticTypes.ExclusiveGateway ||
                preview.SemanticTypeId == BpmnSemanticTypes.ParallelGateway ||
                preview.SemanticTypeId == BpmnSemanticTypes.InclusiveGateway ||
                preview.SemanticTypeId == BpmnSemanticTypes.EventBasedGateway
                    ? new SizeD(48d, 48d) : new SizeD(36d, 36d);
        Assert.Equal(expected, preview.Bounds.Size);
    }

    [Theory]
    [InlineData("bpmn:toolbox:message-boundary-event")]
    [InlineData("bpmn:toolbox:timer-boundary-event")]
    [InlineData("bpmn:toolbox:signal-boundary-event")]
    public void AttachmentPreviewUsesExistingResolverAndMissingHostStillHasAnEventBody(string itemId)
    {
        var registration = Registration(itemId);
        var hostId = new SemanticElementId("host");
        var document = Document([new(hostId, BpmnSemanticTypes.Task)]);
        var target = new ToolboxPlacementTarget(hostId, BpmnSemanticTypes.Task, new("host:visual"),
            new("host:projected"), new(100d, 100d, 120d, 80d));
        var pointer = new PointD(160d, 180d);
        var provider = registration.PreviewProvider!;
        var rejected = provider.Evaluate(new(registration.ToolboxItemId, document, pointer));
        Assert.False(rejected.IsAllowed);
        Assert.Equal(new RectD(142d, 162d, 36d, 36d), rejected.Bounds);
        Assert.Null(rejected.AttachmentCandidate);
        var preview = provider.Evaluate(new(registration.ToolboxItemId, document, pointer, visibleTargets: [target]));
        Assert.True(preview.IsAllowed);
        var request = new ToolboxPlacementRequest(registration.ToolboxItemId, document, document.Revision,
            pointer, new Identities(), visibleTargets: [target]);
        var candidate = registration.CandidateProvider!.ResolveCandidate(request)!;
        Assert.Equal(candidate.PreviewBounds, preview.Bounds);
        Assert.Equal(candidate.Target, preview.AttachmentCandidate!.Target);
        var planned = registration.CommandFactory.CreatePlan(new(registration.ToolboxItemId, document,
            document.Revision, pointer, new Identities(), visibleTargets: [target], candidate: preview.AttachmentCandidate));
        Assert.True(planned.Succeeded);
        Assert.Equal(preview.Label, planned.Plan!.Command.GetType().GetProperty("Name")!.GetValue(planned.Plan.Command));
        Assert.Equal(preview, provider.Evaluate(new(registration.ToolboxItemId, document, pointer, visibleTargets: [target])));
    }

    [Fact]
    public void NegativeBoundsAndExhaustedTaskNumberAreRejectedWithoutChangingDefaults()
    {
        var registration = Registration("bpmn:toolbox:task");
        Assert.False(registration.PreviewProvider!.Evaluate(new(registration.ToolboxItemId, Document(), default)).IsAllowed);
        var document = Document([new(new("existing"), BpmnSemanticTypes.ManualTask,
            properties: [new(BpmnSemanticProperties.ElementNumber, PropertyValue.FromInteger(41))])]);
        var before = registration.PreviewProvider.Evaluate(new(registration.ToolboxItemId, document, new(200d, 100d)));
        Assert.Equal("Task 42", before.Label);
        Assert.Equal(before, registration.PreviewProvider.Evaluate(new(registration.ToolboxItemId, document, new(200d, 100d))));
        var exhausted = Document([new(new("existing"), BpmnSemanticTypes.Task,
            properties: [new(BpmnSemanticProperties.ElementNumber, PropertyValue.FromInteger(long.MaxValue))])]);
        Assert.False(registration.PreviewProvider.Evaluate(new(registration.ToolboxItemId, exhausted, new(200d, 100d))).IsAllowed);
    }

    [Theory]
    [MemberData(nameof(AllTools))]
    public void PreviewBodiesAndMarkersReuseNormalAppearanceGeometryWithoutPersistentOrigins(string itemId)
    {
        var registration = Registration(itemId);
        var document = Document();
        var preview = registration.PreviewProvider!.Evaluate(new(registration.ToolboxItemId, document, new(250d, 180d)));
        var feedback = new EditorFeedbackSnapshot("preview", new(800d, 600d, preview.Bounds.Width, preview.Bounds.Height), preview);
        var feedbackContributor = Assert.Single(BpmnPluginRegistration.N100.SceneContributors,
            static contribution => contribution.Descriptor.PlacementDependency == Canvas2DScenePlacementDependency.BoundedFeedbackOnly);
        var graph = new ProjectedGraph(document.DocumentId, document.Revision);
        var previewItems = feedbackContributor.Contributor.Contribute(Context(feedbackContributor, graph,
            LayoutComputation.Empty, new(temporaryFeedback: [feedback]))).Contribution!.Items;
        var body = Assert.Single(previewItems, static item => item.Origin.StableSourceKey == "feedback:preview:body");
        Assert.Equal(preview.IsAllowed ? "#15803d" : "#b91c1c", body.Style.Stroke);
        Assert.All(previewItems, item =>
        {
            Assert.Equal(Canvas2DHitTestPolicy.None, item.HitTestPolicy);
            Assert.Null(item.Origin.SemanticElementId);
            Assert.Null(item.Origin.VisualStateId);
            Assert.Null(item.Origin.ProjectedObjectId);
            Assert.Equal(feedback.Bounds, item.Bounds);
            Assert.Equal(new PointD(800d, 600d), item.Transform.TransformPoint(default));
        });
        var node = new ProjectedNode(new ProjectionSourceTrace(document.DocumentId, new("preview:test-rule"),
            ProjectionSourceKind.SemanticElement, new("actual"), preview.SemanticTypeId, "node", new("actual:visual")),
            projectedProperties: [new("BPMN.ProjectedSemanticType", PropertyValue.FromText(preview.SemanticTypeId.Value))]);
        var normalContributor = Assert.Single(BpmnPluginRegistration.N100.SceneContributors,
            static contribution => contribution.Descriptor.PlacementDependency == Canvas2DScenePlacementDependency.Invariant);
        var normal = normalContributor.Contributor.Contribute(Context(normalContributor,
            new(document.DocumentId, document.Revision, nodes: [node]),
            new([new(node.Id, preview.Bounds, Matrix2D.CreateTranslation(preview.Bounds.X, preview.Bounds.Y))]),
            EditorStateSnapshot.Empty)).Contribution!;
        Assert.Equal(Assert.Single(normal.CanonicalItemVisualOverrides).Geometry, body.Geometry);
        Assert.Equal(normal.Items.Length + 1, previewItems.Length);
        foreach (var marker in normal.Items)
        {
            Assert.Contains(previewItems, item => item != body && item.Geometry.Equals(marker.Geometry));
        }
    }

    [Theory]
    [InlineData("bpmn:toolbox:task")]
    [InlineData("bpmn:toolbox:timer-boundary-event")]
    public void HistoricalPublicContributorRendersTypedPreviewWithItsOwnIdentity(string itemId)
    {
        var registration = Registration(itemId);
        var document = Document();
        var preview = registration.PreviewProvider!.Evaluate(new(registration.ToolboxItemId,
            document, new(250d, 180d)));
        var feedback = new EditorFeedbackSnapshot("legacy-preview", preview.Bounds, preview);
        var historical = Assert.Single(BpmnPluginRegistration.N91.SceneContributors);
        Assert.Equal(Canvas2DScenePlacementDependency.Unknown, historical.Descriptor.PlacementDependency);
        var result = historical.Contributor.Contribute(Context(historical,
            new(document.DocumentId, document.Revision), LayoutComputation.Empty,
            new(temporaryFeedback: [feedback])));
        Assert.True(result.Succeeded);
        var body = Assert.Single(result.Contribution!.Items,
            static item => item.Origin.StableSourceKey == "feedback:legacy-preview:body");
        Assert.Equal(preview.Bounds, body.Bounds);
        Assert.Equal(preview.IsAllowed ? "#15803d" : "#b91c1c", body.Style.Stroke);
        Assert.All(result.Contribution.Items, item =>
        {
            Assert.Equal(Canvas2DSceneObjectIdentity.ForExtension(historical.Descriptor.ContributorId,
                item.Origin.StableSourceKey!), item.Id);
            Assert.Equal(Canvas2DHitTestPolicy.None, item.HitTestPolicy);
            Assert.Null(item.Origin.SemanticElementId);
            Assert.Null(item.Origin.VisualStateId);
            Assert.Null(item.Origin.ProjectedObjectId);
        });
    }

    private static Canvas2DSceneContributionContext Context(Canvas2DSceneContributorRegistration registration,
        ProjectedGraph graph, LayoutComputation layout, EditorStateSnapshot state) =>
        new(graph, new(graph.DocumentId, graph.SourceRevision, BpmnAlgorithmIds.DefaultLayout, layout),
            new(graph.DocumentId, graph.SourceRevision, BpmnAlgorithmIds.DefaultLayout, BpmnAlgorithmIds.DefaultRouting,
                RoutingComputation.Empty), new(graph.DocumentId, graph.SourceRevision), state,
            Canvas2DSceneConfiguration.Default, registration.Descriptor);

    private static ToolboxPlacementRegistration Registration(string id) =>
        Assert.Single(BpmnPluginRegistration.N100.ToolboxPlacementRegistrations, item => item.ToolboxItemId.Value == id);

    private static DocumentSnapshot Document(IEnumerable<SemanticElementSnapshot>? elements = null)
    {
        var id = new DocumentId("preview:document");
        return new(new SemanticModelSnapshot(id, DocumentRevision.Zero, elements),
            new VisualModelSnapshot(id, DocumentRevision.Zero), new DocumentMetadataSnapshot(id, DocumentRevision.Zero));
    }

    private sealed class Identities : IDocumentCreationIdentityProvider
    {
        internal int Count { get; private set; }
        public DocumentCreationIdentity CreateIdentity()
        {
            Count++;
            return new(new("created"), new("created:visual"));
        }
    }
}
