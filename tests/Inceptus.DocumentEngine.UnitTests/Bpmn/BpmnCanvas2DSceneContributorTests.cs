using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnCanvas2DSceneContributorTests
{
    private const string ProjectedSemanticTypeProperty = "BPMN.ProjectedSemanticType";

    private static readonly DocumentId DocumentId = new("bpmn:m3-scene-unit-document");
    private static readonly DocumentRevision Revision = new(43);

    [Fact]
    public void FlowNodesOverrideOnlyCanonicalBodiesWithNotationSpecificGeometry()
    {
        var start = Node("start", BpmnSemanticTypes.StartEvent);
        var task = Node("task", BpmnSemanticTypes.Task);
        var end = Node("end", BpmnSemanticTypes.EndEvent);
        var startBounds = new RectD(10d, 20d, 36d, 36d);
        var taskBounds = new RectD(130d, 5d, 120d, 80d);
        var endBounds = new RectD(330d, 20d, 36d, 36d);

        var contribution = Contribute(
            new ProjectedGraph(DocumentId, Revision, nodes: [start, task, end]),
            Layout(
                Geometry(start, startBounds),
                Geometry(task, taskBounds),
                Geometry(end, endBounds)));

        Assert.Empty(contribution.Items);
        Assert.Equal(3, contribution.CanonicalItemVisualOverrides.Length);
        var byTarget = contribution.CanonicalItemVisualOverrides.ToDictionary(
            static visualOverride => visualOverride.TargetSceneObjectId);

        var startOverride = byTarget[CanonicalNodeId(start)];
        Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, startOverride.Geometry.Kind);
        Assert.Equal(new RectD(0d, 0d, 36d, 36d), startOverride.Geometry.Bounds);
        Assert.Equal(1.5d, startOverride.Style.StrokeWidth);

        var taskOverride = byTarget[CanonicalNodeId(task)];
        Assert.Equal(Canvas2DSceneGeometryKind.Path, taskOverride.Geometry.Kind);
        Assert.True(taskOverride.Geometry.IsClosed);
        Assert.Equal(new RectD(0d, 0d, 120d, 80d), taskOverride.Geometry.Bounds);
        Assert.Equal(20, taskOverride.Geometry.Points.Length);
        Assert.DoesNotContain(new PointD(0d, 0d), taskOverride.Geometry.Points);
        Assert.DoesNotContain(new PointD(120d, 0d), taskOverride.Geometry.Points);
        Assert.DoesNotContain(new PointD(120d, 80d), taskOverride.Geometry.Points);
        Assert.DoesNotContain(new PointD(0d, 80d), taskOverride.Geometry.Points);
        Assert.Equal(1.5d, taskOverride.Style.StrokeWidth);

        var endOverride = byTarget[CanonicalNodeId(end)];
        Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, endOverride.Geometry.Kind);
        Assert.Equal(new RectD(0d, 0d, 36d, 36d), endOverride.Geometry.Bounds);
        Assert.Equal(3.5d, endOverride.Style.StrokeWidth);
        Assert.True(endOverride.Style.StrokeWidth > startOverride.Style.StrokeWidth);

        Assert.All(contribution.CanonicalItemVisualOverrides, visualOverride =>
        {
            Assert.Equal("#ffffff", visualOverride.Style.Fill);
            Assert.Equal("#000000", visualOverride.Style.Stroke);
        });
    }

    [Fact]
    public void ExclusiveGatewayUsesOneCanonicalDiamondAndOneNonHittableOwnedXMarker()
    {
        var gateway = Node("exclusive-gateway", BpmnSemanticTypes.ExclusiveGateway);
        var bounds = new RectD(210d, 85d, 48d, 48d);
        var graph = new ProjectedGraph(DocumentId, Revision, nodes: [gateway]);
        var layout = Layout(Geometry(gateway, bounds));
        var visual = new VisualStateSnapshot(
            gateway.Source.VisualStateId!,
            gateway.Source.SemanticElementId,
            bounds.TopLeft,
            bounds.Size,
            VisualPlacementMode.Pinned);
        var contributionResult = Assert.Single(
            BpmnPluginRegistration.M32.SceneContributors).Contributor.Contribute(
                new Canvas2DSceneContributionContext(
                    graph,
                    layout,
                    new RoutingResult(
                        DocumentId,
                        Revision,
                        BpmnAlgorithmIds.DefaultLayout,
                        BpmnAlgorithmIds.DefaultRouting,
                        RoutingComputation.Empty),
                    new VisualModelSnapshot(DocumentId, Revision, [visual]),
                    EditorStateSnapshot.Empty,
                    Canvas2DSceneConfiguration.Default,
                    Assert.Single(BpmnPluginRegistration.M32.SceneContributors).Descriptor));

        Assert.True(contributionResult.Succeeded);
        var contribution = Assert.IsType<Canvas2DSceneContribution>(
            contributionResult.Contribution);
        var visualOverride = Assert.Single(contribution.CanonicalItemVisualOverrides);
        Assert.Equal(CanonicalNodeId(gateway), visualOverride.TargetSceneObjectId);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, visualOverride.Geometry.Kind);
        Assert.True(visualOverride.Geometry.IsClosed);
        PointD[] expectedDiamond =
        [
            new PointD(24d, 0d),
            new PointD(48d, 24d),
            new PointD(24d, 48d),
            new PointD(0d, 24d),
        ];
        Assert.Equal(
            expectedDiamond.AsEnumerable(),
            visualOverride.Geometry.Points.AsEnumerable());

        var marker = Assert.Single(contribution.Items);
        Assert.Equal(Canvas2DSceneLayer.Decoration, marker.Layer);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, marker.Geometry.Kind);
        Assert.True(marker.Geometry.IsClosed);
        Assert.Equal(12, marker.Geometry.Points.Length);
        Assert.Equal(Canvas2DHitTestMode.None, marker.HitTestPolicy.Mode);
        Assert.Equal(bounds, marker.Bounds);
        Assert.Equal(gateway.Source.SemanticElementId, marker.Origin.SemanticElementId);
        Assert.Equal(gateway.Source.VisualStateId, marker.Origin.VisualStateId);
        Assert.Equal(gateway.Id, marker.Origin.ProjectedObjectId);
        Assert.True(marker.Origin.Categories.HasFlag(
            Canvas2DSceneOriginCategory.RegisteredExtension));

        var result = new Canvas2DSceneBuilder(
            contributors: BpmnPluginRegistration.M32.SceneContributors).Build(
                graph,
                layout,
                new RoutingResult(
                    DocumentId,
                    Revision,
                    BpmnAlgorithmIds.DefaultLayout,
                    BpmnAlgorithmIds.DefaultRouting,
                    RoutingComputation.Empty),
                new VisualModelSnapshot(DocumentId, Revision, [visual]),
                new EditorStateSnapshot(selection: [visual.Id]));

        Assert.True(result.Succeeded);
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var body = Assert.Single(scene.Items, item => item.Id == CanonicalNodeId(gateway));
        Assert.Equal(Canvas2DSceneGeometryKind.Path, body.Geometry.Kind);
        Assert.Equal(bounds, body.Bounds);
        Assert.DoesNotContain(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.ProjectedObjectId == gateway.Id &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle);
        Assert.Single(scene.Items, item =>
            item.Origin.ProjectedObjectId == gateway.Id &&
            item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.RegisteredExtension));
        Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.RelatedSceneObjectIds.Contains(body.Id) &&
            item.Geometry.Equals(body.Geometry));

        var hit = new Canvas2DSceneHitTestService().HitTest(
            scene,
            new PointD(bounds.X + 24d, bounds.Y + 24d));
        Assert.NotNull(hit);
        Assert.Equal(gateway.Source.SemanticElementId, hit.Origin.SemanticElementId);
        Assert.Equal(gateway.Source.VisualStateId, hit.Origin.VisualStateId);
        Assert.Equal(body.Id, hit.SceneObjectId);
    }

    [Fact]
    public void MessageAndTimerCatchEventsUseDoubleCircleAndGenericNonHittableMarkers()
    {
        var message = Node("message-catch", BpmnSemanticTypes.MessageCatchEvent);
        var timer = Node("timer-catch", BpmnSemanticTypes.TimerCatchEvent);
        var contribution = Contribute(
            new ProjectedGraph(DocumentId, Revision, nodes: [message, timer]),
            Layout(
                Geometry(message, new RectD(20d, 30d, 36d, 36d)),
                Geometry(timer, new RectD(100d, 30d, 36d, 36d))));

        Assert.Equal(2, contribution.CanonicalItemVisualOverrides.Length);
        Assert.All(contribution.CanonicalItemVisualOverrides, visualOverride =>
        {
            Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, visualOverride.Geometry.Kind);
            Assert.Equal(new RectD(0d, 0d, 36d, 36d), visualOverride.Geometry.Bounds);
            Assert.Equal(1.5d, visualOverride.Style.StrokeWidth);
        });
        var messageItems = contribution.Items.Where(item =>
            item.Origin.SemanticElementId == message.Source.SemanticElementId).ToArray();
        Assert.Equal(3, messageItems.Length);
        Assert.Single(messageItems, item =>
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Ellipse);
        Assert.Equal(2, messageItems.Count(item =>
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Path));
        var timerItems = contribution.Items.Where(item =>
            item.Origin.SemanticElementId == timer.Source.SemanticElementId).ToArray();
        Assert.Equal(3, timerItems.Length);
        Assert.Equal(2, timerItems.Count(item =>
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Ellipse));
        Assert.Single(timerItems, item =>
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Path);
        Assert.All(contribution.Items, item =>
        {
            Assert.Equal(Canvas2DSceneLayer.Decoration, item.Layer);
            Assert.Equal(Canvas2DHitTestMode.None, item.HitTestPolicy.Mode);
            Assert.True(item.Origin.Categories.HasFlag(
                Canvas2DSceneOriginCategory.RegisteredExtension));
        });
    }

    [Fact]
    public void EventBasedGatewayUsesCanonicalDiamondDoubleCircleAndPentagon()
    {
        var gateway = Node("event-based", BpmnSemanticTypes.EventBasedGateway);
        var bounds = new RectD(30d, 40d, 48d, 48d);
        var contribution = Contribute(
            new ProjectedGraph(DocumentId, Revision, nodes: [gateway]),
            Layout(Geometry(gateway, bounds)));

        var visualOverride = Assert.Single(contribution.CanonicalItemVisualOverrides);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, visualOverride.Geometry.Kind);
        Assert.True(visualOverride.Geometry.IsClosed);
        Assert.Equal(new RectD(0d, 0d, 48d, 48d), visualOverride.Geometry.Bounds);
        Assert.Equal(3, contribution.Items.Length);
        Assert.Equal(2, contribution.Items.Count(item =>
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Ellipse));
        var pentagon = Assert.Single(contribution.Items, item =>
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Path);
        Assert.True(pentagon.Geometry.IsClosed);
        Assert.Equal(5, pentagon.Geometry.Points.Length);
        Assert.All(contribution.Items, item =>
        {
            Assert.Equal(bounds, item.Bounds);
            Assert.Equal(Canvas2DHitTestMode.None, item.HitTestPolicy.Mode);
            Assert.Equal(gateway.Source.SemanticElementId, item.Origin.SemanticElementId);
            Assert.Equal(gateway.Source.VisualStateId, item.Origin.VisualStateId);
            Assert.Equal(gateway.Id, item.Origin.ProjectedObjectId);
        });
    }

    [Fact]
    public void ContributorIsExactlyDeterministicAndIgnoresUnclaimedNotationNodes()
    {
        var task = Node("deterministic-task", BpmnSemanticTypes.Task);
        var foreign = Node("foreign", new SemanticTypeId("other:node"));
        var graph = new ProjectedGraph(DocumentId, Revision, nodes: [foreign, task]);
        var layout = Layout(
            Geometry(foreign, new RectD(0d, 0d, 50d, 40d)),
            Geometry(task, new RectD(100d, 0d, 140d, 70d)));

        var first = Contribute(graph, layout);
        var second = Contribute(graph, layout);

        Assert.Equal(first, second);
        var visualOverride = Assert.Single(first.CanonicalItemVisualOverrides);
        Assert.Equal(CanonicalNodeId(task), visualOverride.TargetSceneObjectId);
        Assert.Equal(new RectD(0d, 0d, 140d, 70d), visualOverride.Geometry.Bounds);
    }

    [Fact]
    public void ContributorRejectsInconsistentProjectedTypeAndMissingLayoutDeterministically()
    {
        var invalid = Node(
            "invalid-task",
            BpmnSemanticTypes.Task,
            projectedType: BpmnSemanticTypes.EndEvent);
        var invalidResult = Invoke(
            new ProjectedGraph(DocumentId, Revision, nodes: [invalid]),
            Layout(Geometry(invalid, new RectD(0d, 0d, 120d, 80d))));

        Assert.False(invalidResult.Succeeded);
        Assert.Equal(
            "BPMN_SCENE_INVALID_PROJECTED_NODE",
            Assert.Single(invalidResult.Diagnostics).Code);

        var task = Node("missing-layout-task", BpmnSemanticTypes.Task);
        var missingResult = Invoke(
            new ProjectedGraph(DocumentId, Revision, nodes: [task]),
            Layout());

        Assert.False(missingResult.Succeeded);
        Assert.Equal(
            "BPMN_SCENE_MISSING_LAYOUT_GEOMETRY",
            Assert.Single(missingResult.Diagnostics).Code);
    }

    [Fact]
    public void ContributorDoesNotDuplicateLabelsConnectorsOrTargetArrows()
    {
        var start = Node("flow-start", BpmnSemanticTypes.StartEvent);
        var task = Node("flow-task", BpmnSemanticTypes.Task);
        var edge = Edge("flow", start, task);
        var graph = new ProjectedGraph(
            DocumentId,
            Revision,
            nodes: [start, task],
            edges: [edge]);
        var route = new RoutedConnectorGeometry(
            edge.Id,
            new PointD(36d, 18d),
            new PointD(100d, 40d),
            bendPoints: [new PointD(70d, 18d)]);
        var contribution = Contribute(
            graph,
            Layout(
                Geometry(start, new RectD(0d, 0d, 36d, 36d)),
                Geometry(task, new RectD(100d, 0d, 120d, 80d))),
            new RoutingResult(
                DocumentId,
                Revision,
                BpmnAlgorithmIds.DefaultLayout,
                BpmnAlgorithmIds.DefaultRouting,
                new RoutingComputation([route])));

        Assert.Empty(contribution.Items);
        Assert.Equal(2, contribution.CanonicalItemVisualOverrides.Length);
        Assert.DoesNotContain(
            contribution.CanonicalItemVisualOverrides,
            visualOverride => visualOverride.TargetSceneObjectId ==
                Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector"));
    }

    [Fact]
    public void SceneBuilderPreservesCanonicalIdentityTraceInteractionAndLabelOwnership()
    {
        var task = Node("canonical-task", BpmnSemanticTypes.Task);
        var bounds = new RectD(125d, 75d, 140d, 90d);
        var label = new ProjectedLabel(
            new ProjectionSourceTrace(
                DocumentId,
                task.Source.RuleId,
                ProjectionSourceKind.SemanticElement,
                task.Source.SemanticElementId,
                BpmnSemanticTypes.Task,
                "name-label",
                task.Source.VisualStateId),
            task.Id,
            "Review order",
            semanticProperties:
            [
                new("Code", PropertyValue.FromText("REVIEW_ORDER")),
                new("Name", PropertyValue.FromText("Review order")),
            ]);
        var graph = new ProjectedGraph(
            DocumentId,
            Revision,
            nodes: [task],
            labels: [label]);
        var visual = new VisualStateSnapshot(
            task.Source.VisualStateId!,
            task.Source.SemanticElementId,
            bounds.TopLeft,
            bounds.Size,
            VisualPlacementMode.Pinned,
            properties:
            [
                new("test:appearance", PropertyValue.FromText("preserved")),
            ]);
        var builder = new Canvas2DSceneBuilder(
            contributors: BpmnPluginRegistration.M3.SceneContributors);
        var result = builder.Build(
            graph,
            Layout(Geometry(task, bounds)),
            new RoutingResult(
                DocumentId,
                Revision,
                BpmnAlgorithmIds.DefaultLayout,
                BpmnAlgorithmIds.DefaultRouting,
                RoutingComputation.Empty),
            new VisualModelSnapshot(DocumentId, Revision, [visual]),
            new EditorStateSnapshot(selection: [visual.Id]));

        Assert.True(result.Succeeded);
        var scene = Assert.IsType<Canvas2DScene>(result.Scene);
        var canonicalId = CanonicalNodeId(task);
        var body = Assert.Single(scene.Items, item => item.Id == canonicalId);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, body.Geometry.Kind);
        Assert.Equal(bounds, body.Bounds);
        Assert.Equal(task.Source.SemanticElementId, body.Origin.SemanticElementId);
        Assert.Equal(task.Source.VisualStateId, body.Origin.VisualStateId);
        Assert.Equal(task.Id, body.Origin.ProjectedObjectId);
        Assert.Equal("preserved", body.PersistentAppearance["test:appearance"].TextValue);
        Assert.Equal(Canvas2DHitTestMode.FillOrStroke, body.HitTestPolicy.Mode);
        Assert.True(body.Metadata["inceptus.canvas2d:move-capable"].BooleanValue);
        Assert.True(body.Metadata["inceptus.canvas2d:resize-capable"].BooleanValue);

        var logicalBodyHits = scene.Items.Where(item =>
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
            item.Layer is Canvas2DSceneLayer.Content or Canvas2DSceneLayer.Label &&
            (item.Origin.ProjectedObjectId == task.Id ||
             item.Origin.RelatedProjectedObjectIds.Contains(task.Id))).ToArray();
        Assert.Equal(2, logicalBodyHits.Length);
        Assert.Contains(logicalBodyHits, item => item.Id == canonicalId);
        var labelItem = Assert.Single(
            logicalBodyHits,
            item => item.Geometry.Kind == Canvas2DSceneGeometryKind.Text);
        Assert.Equal("Review order", labelItem.Geometry.Content);
        Assert.DoesNotContain("REVIEW_ORDER", labelItem.Geometry.Content, StringComparison.Ordinal);
        Assert.Equal(body.Origin.SemanticElementId, labelItem.Origin.SemanticElementId);
        Assert.Equal(body.Origin.VisualStateId, labelItem.Origin.VisualStateId);
        Assert.Contains(task.Id, labelItem.Origin.RelatedProjectedObjectIds);

        var selectionOverlay = Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.RelatedSceneObjectIds.Contains(canonicalId) &&
            item.Geometry.Equals(body.Geometry));
        Assert.Equal(body.Bounds, selectionOverlay.Bounds);
        Assert.Equal(body.Transform, selectionOverlay.Transform);
        Assert.Equal(Canvas2DHitTestMode.None, selectionOverlay.HitTestPolicy.Mode);

        var hit = new Canvas2DSceneHitTestService().HitTest(
            scene,
            new PointD(
                bounds.X + (bounds.Width / 2d),
                bounds.Y + (bounds.Height / 2d)));
        Assert.NotNull(hit);
        Assert.Equal(body.Origin.SemanticElementId, hit.Origin.SemanticElementId);
        Assert.Equal(body.Origin.VisualStateId, hit.Origin.VisualStateId);
    }

    private static Canvas2DSceneContribution Contribute(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingResult? routing = null)
    {
        var result = Invoke(graph, layout, routing);
        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        return Assert.IsType<Canvas2DSceneContribution>(result.Contribution);
    }

    private static Canvas2DSceneContributionResult Invoke(
        ProjectedGraph graph,
        LayoutResult layout,
        RoutingResult? routing = null)
    {
        var registration = Assert.Single(BpmnPluginRegistration.M3.SceneContributors);
        var context = new Canvas2DSceneContributionContext(
            graph,
            layout,
            routing ?? new RoutingResult(
                DocumentId,
                Revision,
                BpmnAlgorithmIds.DefaultLayout,
                BpmnAlgorithmIds.DefaultRouting,
                RoutingComputation.Empty),
            new VisualModelSnapshot(DocumentId, Revision),
            EditorStateSnapshot.Empty,
            Canvas2DSceneConfiguration.Default,
            registration.Descriptor);
        return registration.Contributor.Contribute(context);
    }

    private static ProjectedNode Node(
        string key,
        SemanticTypeId semanticType,
        SemanticTypeId? projectedType = null)
    {
        var trace = new ProjectionSourceTrace(
            DocumentId,
            new ProjectionRuleId($"bpmn:m3-test/{key}"),
            ProjectionSourceKind.SemanticElement,
            new SemanticElementId($"bpmn:m3-test/{key}"),
            semanticType,
            "node",
            new VisualStateId($"bpmn:m3-test/{key}/visual"));
        return new ProjectedNode(
            trace,
            projectedProperties:
            [
                new(
                    ProjectedSemanticTypeProperty,
                    PropertyValue.FromText((projectedType ?? semanticType).Value)),
            ]);
    }

    private static ProjectedEdge Edge(string key, ProjectedNode source, ProjectedNode target)
    {
        var trace = new ProjectionSourceTrace(
            DocumentId,
            new ProjectionRuleId($"bpmn:m3-test/{key}"),
            ProjectionSourceKind.SemanticRelationship,
            new SemanticElementId($"bpmn:m3-test/{key}"),
            BpmnSemanticTypes.SequenceFlow,
            "edge",
            new VisualStateId($"bpmn:m3-test/{key}/visual"));
        return new ProjectedEdge(
            trace,
            source.Id,
            target.Id,
            projectedProperties:
            [
                new(
                    ProjectedSemanticTypeProperty,
                    PropertyValue.FromText(BpmnSemanticTypes.SequenceFlow.Value)),
            ]);
    }

    private static LayoutNodeGeometry Geometry(ProjectedNode node, RectD bounds) =>
        new(
            node.Id,
            bounds,
            Matrix2D.CreateTranslation(bounds.X, bounds.Y));

    private static LayoutResult Layout(params LayoutNodeGeometry[] geometries) =>
        new(
            DocumentId,
            Revision,
            BpmnAlgorithmIds.DefaultLayout,
            new LayoutComputation(geometries));

    private static SceneObjectId CanonicalNodeId(ProjectedNode node) =>
        Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node");
}
