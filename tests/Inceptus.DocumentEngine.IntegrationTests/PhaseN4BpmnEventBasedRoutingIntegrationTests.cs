using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN4BpmnEventBasedRoutingIntegrationTests
{
    [Fact]
    public async Task NeutralViewportKnownPlacementOriginAndDocumentMarginStayAligned()
    {
        var composition = await CreateEmptyHostCompositionAsync();
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync(composition);
        var before = harness.Composition.Document.CaptureSnapshot();
        var item = new ToolboxCatalog(BpmnPluginRegistration.N4.ToolboxContributions)
            .Items.Single(candidate =>
                candidate.ElementTypeId == BpmnSemanticTypes.MessageCatchEvent);
        var initialScene = harness.Scene;
        var selection = new ToolboxSelectionState();
        var controller = new ToolboxPlacementController(
            harness.Composition.ToolboxPlacementCatalog,
            selection,
            new PhaseN1ToolboxPlacementIntegrationTests.SequenceIdentityProvider(
                new DocumentCreationIdentity(
                    new SemanticElementId("test:bpmn:n4:origin:message"),
                    new VisualStateId("test:bpmn:n4:origin:message-visual"))));

        Assert.Equal(ViewportSnapshot.Default,
            harness.Composition.Configuration.InitialEditorState.Viewport);
        Assert.Equal(
            new ViewportSnapshot(1d, default, new RectD(0d, 0d, 900d, 600d)),
            initialScene.Viewport);
        Assert.Equal(Matrix2D.Identity, initialScene.ViewportTransform);
        Assert.Equal(2, initialScene.Items.Count(sceneItem =>
            sceneItem.Origin.StableSourceKey?.StartsWith(
                "document-boundary:",
                StringComparison.Ordinal) == true &&
            sceneItem.Layer == Canvas2DSceneLayer.Background &&
            sceneItem.HitTestPolicy.Mode == Canvas2DHitTestMode.None));
        Assert.True(selection.Select(item.ItemId));

        var invalid = await controller.TryPlaceAtCssPointAsync(
            harness.Session,
            new PointD(10d, 10d));

        Assert.True(invalid.Handled);
        Assert.False(invalid.IsCommitted);
        Assert.Null(invalid.CreatedVisualStateId);
        Assert.Equal(before, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(0, harness.Session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(item.ItemId, selection.SelectedItemId);

        var placed = await controller.TryPlaceAtCssPointAsync(
            harness.Session,
            new PointD(100d, 100d));
        await WaitForPipelineAsync(harness);

        Assert.True(placed.Handled);
        Assert.True(placed.IsCommitted);
        var after = harness.Composition.Document.CaptureSnapshot();
        var createdElement = Assert.Single(after.SemanticModel.Elements.Where(element =>
            element.TypeId == BpmnSemanticTypes.MessageCatchEvent &&
            !before.SemanticModel.Elements.Any(candidate => candidate.Id == element.Id)));
        var createdVisual = Assert.Single(after.VisualModel.VisualStates, visual =>
            visual.SemanticElementId == createdElement.Id);
        AssertAlignedNodeGeometry(
            harness,
            createdElement.Id,
            createdVisual.Id,
            new RectD(82d, 82d, 36d, 36d));

        var movedToOrigin = await harness.Session.ExecuteAsync(new MoveVisualStateCommand(
            after.DocumentId,
            after.Revision,
            createdVisual.Id,
            new PointD(0d, 0d),
            VisualPlacementMode.Pinned));
        Assert.True(movedToOrigin.IsCommitted);
        await WaitForPipelineAsync(harness);
        AssertAlignedNodeGeometry(
            harness,
            createdElement.Id,
            createdVisual.Id,
            new RectD(0d, 0d, 36d, 36d));
        var originScene = Assert.IsType<Canvas2DScene>(harness.Session.CaptureState().CurrentScene);
        Assert.All(originScene.Items.Where(sceneItem =>
                sceneItem.Origin.SemanticElementId == createdElement.Id &&
                !sceneItem.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.EditorState)),
            sceneItem =>
            {
                Assert.True(sceneItem.Bounds.Left >= 0d);
                Assert.True(sceneItem.Bounds.Top >= 0d);
            });
        Assert.Equal(
            new PointD(0d, 0d),
            originScene.ViewportTransform.TransformPoint(new PointD(0d, 0d)));

        var originSnapshot = harness.Composition.Document.CaptureSnapshot();
        var movedToMargin = await harness.Session.ExecuteAsync(new MoveVisualStateCommand(
            originSnapshot.DocumentId,
            originSnapshot.Revision,
            createdVisual.Id,
            new PointD(80d, 80d),
            VisualPlacementMode.Pinned));
        Assert.True(movedToMargin.IsCommitted);
        await WaitForPipelineAsync(harness);
        AssertAlignedNodeGeometry(
            harness,
            createdElement.Id,
            createdVisual.Id,
            new RectD(80d, 80d, 36d, 36d));
    }

    [Fact]
    public async Task DemoCompletesN4SemanticsProjectionLayoutRoutingSceneAndProperties()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var snapshot = composition.Document.CaptureSnapshot();
        var configuration = composition.Configuration;
        var projectionExecution = configuration.ProjectionEngine.Project(snapshot);
        Assert.True(projectionExecution.IsSuccessful);
        var graph = Assert.IsType<ProjectedGraph>(projectionExecution.Graph);
        var layoutExecution = configuration.LayoutEngine.Layout(
            graph,
            configuration.LayoutAlgorithmId);
        Assert.True(layoutExecution.IsSuccessful);
        var layout = Assert.IsType<LayoutResult>(layoutExecution.Result);
        var routingExecution = configuration.RoutingEngine.Route(
            graph,
            layout,
            configuration.RoutingAlgorithmId);
        Assert.True(routingExecution.IsSuccessful);
        var routing = Assert.IsType<RoutingResult>(routingExecution.Result);
        var sceneExecution = configuration.SceneBuilder.Build(
            graph,
            layout,
            routing,
            snapshot.VisualModel,
            configuration.InitialEditorState);
        Assert.True(sceneExecution.Succeeded);
        var scene = Assert.IsType<Canvas2DScene>(sceneExecution.Scene);

        AssertElement(
            snapshot,
            BpmnDemoPipeline.MessageCatchEventId,
            BpmnSemanticTypes.MessageCatchEvent,
            (BpmnSemanticProperties.Name, "Customer message"),
            (BpmnSemanticProperties.Description,
                "A descriptive message intermediate catch event."));
        AssertElement(
            snapshot,
            BpmnDemoPipeline.TimerCatchEventId,
            BpmnSemanticTypes.TimerCatchEvent,
            (BpmnSemanticProperties.Name, "Response timeout"),
            (BpmnSemanticProperties.TimerDefinition, string.Empty),
            (BpmnSemanticProperties.Description,
                "A descriptive timer intermediate catch event."));
        AssertElement(
            snapshot,
            BpmnDemoPipeline.EventBasedGatewayId,
            BpmnSemanticTypes.EventBasedGateway,
            (BpmnSemanticProperties.Code, "AWAIT_EVENT"),
            (BpmnSemanticProperties.Name, "Await customer event"),
            (BpmnSemanticProperties.Description,
                "Wait for a modeled message or timer catch event."));

        AssertFlow(
            snapshot,
            BpmnDemoPipeline.SeventeenthSequenceFlowId,
            BpmnDemoPipeline.AwaitEventTaskId,
            BpmnDemoPipeline.EventBasedGatewayId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.EighteenthSequenceFlowId,
            BpmnDemoPipeline.EventBasedGatewayId,
            BpmnDemoPipeline.MessageCatchEventId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.NineteenthSequenceFlowId,
            BpmnDemoPipeline.EventBasedGatewayId,
            BpmnDemoPipeline.TimerCatchEventId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.TwentiethSequenceFlowId,
            BpmnDemoPipeline.MessageCatchEventId,
            BpmnDemoPipeline.ProcessMessageTaskId);
        AssertFlow(
            snapshot,
            BpmnDemoPipeline.TwentyFirstSequenceFlowId,
            BpmnDemoPipeline.TimerCatchEventId,
            BpmnDemoPipeline.HandleTimeoutTaskId);

        Assert.Equal(36d, NodeGeometry(graph, layout,
            BpmnDemoPipeline.MessageCatchEventId).Bounds.Width);
        Assert.Equal(36d, NodeGeometry(graph, layout,
            BpmnDemoPipeline.TimerCatchEventId).Bounds.Height);
        Assert.Equal(new SizeD(48d, 48d), NodeGeometry(graph, layout,
            BpmnDemoPipeline.EventBasedGatewayId).Bounds.Size);
        var n4ProjectedEdgeIds = graph.Edges.Where(edge =>
                N4SemanticFlowIds.Contains(edge.Source.SemanticElementId))
            .Select(edge => edge.Id)
            .ToHashSet();
        Assert.Equal(5, routing.Routes.Count(route =>
            n4ProjectedEdgeIds.Contains(route.ProjectedEdgeId)));

        AssertExternalLabel(graph, BpmnDemoPipeline.MessageCatchEventId);
        AssertExternalLabel(graph, BpmnDemoPipeline.TimerCatchEventId);
        AssertExternalLabel(graph, BpmnDemoPipeline.EventBasedGatewayId);
        AssertVisual(scene, BpmnDemoPipeline.MessageCatchEventVisualId,
            Canvas2DSceneGeometryKind.Ellipse, expectedMarkerCount: 3);
        AssertVisual(scene, BpmnDemoPipeline.TimerCatchEventVisualId,
            Canvas2DSceneGeometryKind.Ellipse, expectedMarkerCount: 3);
        AssertVisual(scene, BpmnDemoPipeline.EventBasedGatewayVisualId,
            Canvas2DSceneGeometryKind.Path, expectedMarkerCount: 3);

        var schemas = composition.PropertiesSchemaCatalog.Schemas.ToDictionary(
            schema => schema.SemanticTypeId);
        Assert.Contains(BpmnSemanticTypes.MessageCatchEvent, schemas.Keys);
        Assert.Contains(BpmnSemanticTypes.TimerCatchEvent, schemas.Keys);
        Assert.Contains(BpmnSemanticTypes.EventBasedGateway, schemas.Keys);
    }

    [Fact]
    public async Task DeletingEveryN4NodeCascadesAndUndoRestoresExactStableGeometry()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        SemanticElementId[] targets =
        [
            BpmnDemoPipeline.MessageCatchEventId,
            BpmnDemoPipeline.TimerCatchEventId,
            BpmnDemoPipeline.EventBasedGatewayId,
        ];
        foreach (var targetId in targets)
        {
            var before = harness.Composition.Document.CaptureSnapshot();
            var beforeBounds = NodeBounds(harness.Scene, before);
            var targetVisual = Assert.Single(before.VisualModel.VisualStates, visual =>
                visual.SemanticElementId == targetId);
            var incident = before.SemanticModel.Relationships.Where(relationship =>
                relationship.SourceId == targetId || relationship.TargetId == targetId).ToArray();

            var deleted = await harness.Session.ExecuteAsync(
                new Inceptus.DocumentEngine.Bpmn.Commands.DeleteBpmnFlowNodeCommand(
                    before.DocumentId,
                    before.Revision,
                    targetId,
                    targetVisual.Id));
            Assert.True(deleted.IsCommitted);
            await harness.Session.WaitForIdleAsync();
            var after = harness.Composition.Document.CaptureSnapshot();
            Assert.False(after.SemanticModel.TryGetElement(targetId, out _));
            Assert.All(incident, relationship =>
                Assert.False(after.SemanticModel.TryGetRelationship(relationship.Id, out _)));
            AssertSurvivorsUnchanged(
                beforeBounds,
                NodeBounds(harness.Scene, after),
                targetVisual.Id);

            Assert.True((await harness.Session.UndoAsync()).IsCommitted);
            await harness.Session.WaitForIdleAsync();
            var restored = harness.Composition.Document.CaptureSnapshot();
            Assert.True(restored.SemanticModel.TryGetElement(targetId, out var element));
            Assert.Equal(before.SemanticModel.Elements.Single(candidate =>
                candidate.Id == targetId), element);
            Assert.Equal(targetVisual, restored.VisualModel.VisualStates.Single(visual =>
                visual.Id == targetVisual.Id));
            Assert.All(incident, relationship => Assert.Equal(
                relationship,
                restored.SemanticModel.Relationships.Single(candidate =>
                    candidate.Id == relationship.Id)));
            AssertBoundsEqual(beforeBounds, NodeBounds(harness.Scene, restored));
        }
    }

    [Fact]
    public async Task N4PropertyUpdatesRoundTripThroughHistoryWithoutGeometryOrRouteDrift()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        var updates = new[]
        {
            (BpmnDemoPipeline.MessageCatchEventId,
                BpmnSemanticProperties.Name, "Updated message event"),
            (BpmnDemoPipeline.TimerCatchEventId,
                BpmnSemanticProperties.TimerDefinition, "PT30M"),
            (BpmnDemoPipeline.EventBasedGatewayId,
                BpmnSemanticProperties.Code, "WAIT_FOR_RESPONSE"),
        };

        foreach (var (elementId, propertyKey, updatedValue) in updates)
        {
            var before = harness.Composition.Document.CaptureSnapshot();
            var beforeBounds = NodeBounds(harness.Scene, before);
            var beforeRoutes = RoutePaths(harness);
            var beforeElement = before.SemanticModel.Elements.Single(element =>
                element.Id == elementId);
            var hadOriginal = beforeElement.Properties.TryGetValue(propertyKey, out var original);

            var result = await harness.Session.ExecuteAsync(
                new UpdateSemanticElementPropertyCommand(
                    before.DocumentId,
                    before.Revision,
                    elementId,
                    propertyKey,
                    PropertyValue.FromText(updatedValue)));

            Assert.True(result.IsCommitted);
            await harness.Session.WaitForIdleAsync();
            var updated = harness.Composition.Document.CaptureSnapshot();
            Assert.Equal(
                updatedValue,
                updated.SemanticModel.Elements.Single(element =>
                    element.Id == elementId).Properties[propertyKey].TextValue);
            AssertBoundsEqual(beforeBounds, NodeBounds(harness.Scene, updated));
            AssertRoutePathsEqual(beforeRoutes, RoutePaths(harness));

            Assert.True((await harness.Session.UndoAsync()).IsCommitted);
            await harness.Session.WaitForIdleAsync();
            var restored = harness.Composition.Document.CaptureSnapshot();
            var restoredElement = restored.SemanticModel.Elements.Single(element =>
                element.Id == elementId);
            if (hadOriginal)
            {
                Assert.Equal(original, restoredElement.Properties[propertyKey]);
            }
            else
            {
                Assert.False(restoredElement.Properties.ContainsKey(propertyKey));
            }
            AssertBoundsEqual(beforeBounds, NodeBounds(harness.Scene, restored));
            AssertRoutePathsEqual(beforeRoutes, RoutePaths(harness));
        }
    }

    private static readonly SemanticElementId[] N4SemanticFlowIds =
    [
        BpmnDemoPipeline.SeventeenthSequenceFlowId,
        BpmnDemoPipeline.EighteenthSequenceFlowId,
        BpmnDemoPipeline.NineteenthSequenceFlowId,
        BpmnDemoPipeline.TwentiethSequenceFlowId,
        BpmnDemoPipeline.TwentyFirstSequenceFlowId,
    ];

    private static void AssertElement(
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot snapshot,
        SemanticElementId id,
        SemanticTypeId type,
        params (string Key, string Value)[] properties)
    {
        Assert.True(snapshot.SemanticModel.TryGetElement(id, out var element));
        Assert.Equal(type, element!.TypeId);
        Assert.Equal(
            properties.Select(property => property.Key).Order(),
            element.Properties.Keys.Order());
        Assert.All(properties, property =>
            Assert.Equal(property.Value, element.Properties[property.Key].TextValue));
        Assert.False(element.Properties.ContainsKey(BpmnSemanticProperties.ElementNumber));
    }

    private static void AssertFlow(
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot snapshot,
        SemanticElementId relationshipId,
        SemanticElementId sourceId,
        SemanticElementId targetId)
    {
        Assert.True(snapshot.SemanticModel.TryGetRelationship(relationshipId, out var flow));
        Assert.Equal(BpmnSemanticTypes.SequenceFlow, flow!.TypeId);
        Assert.Equal(sourceId, flow.SourceId);
        Assert.Equal(targetId, flow.TargetId);
        var visual = Assert.Single(snapshot.VisualModel.VisualStates, candidate =>
            candidate.SemanticElementId == relationshipId);
        Assert.NotNull(visual.SourceAnchorId);
        Assert.NotNull(visual.TargetAnchorId);
    }

    private static LayoutNodeGeometry NodeGeometry(
        ProjectedGraph graph,
        LayoutResult layout,
        SemanticElementId elementId)
    {
        var node = Assert.Single(graph.Nodes, candidate =>
            candidate.Source.SemanticElementId == elementId);
        return Assert.Single(layout.Nodes, candidate => candidate.ProjectedObjectId == node.Id);
    }

    private static void AssertAlignedNodeGeometry(
        PhaseM31BpmnPropertiesIntegrationTests.HostHarness harness,
        SemanticElementId elementId,
        VisualStateId visualStateId,
        RectD expectedBounds)
    {
        var snapshot = harness.Composition.Document.CaptureSnapshot();
        var visual = Assert.Single(snapshot.VisualModel.VisualStates, candidate =>
            candidate.Id == visualStateId);
        var configuration = harness.Composition.Configuration;
        var projection = configuration.ProjectionEngine.Project(snapshot);
        Assert.True(projection.IsSuccessful);
        var graph = Assert.IsType<ProjectedGraph>(projection.Graph);
        var layoutExecution = configuration.LayoutEngine.Layout(
            graph,
            configuration.LayoutAlgorithmId);
        Assert.True(layoutExecution.IsSuccessful);
        var layout = Assert.IsType<LayoutResult>(layoutExecution.Result);
        var geometry = NodeGeometry(graph, layout, elementId);
        var state = harness.Session.CaptureState();
        Assert.True(
            state.CurrentScene is not null,
            $"Session {state.Status}: {string.Join("; ", state.RuntimeDiagnostics.Select(
                diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"))}");
        var scene = Assert.IsType<Canvas2DScene>(state.CurrentScene);
        var body = Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.ProjectedRuntimeObject));

        Assert.Equal(new PointD(expectedBounds.X, expectedBounds.Y), visual.Position);
        Assert.Equal(expectedBounds.Size, visual.Size);
        Assert.Equal(VisualPlacementMode.Pinned, visual.PlacementMode);
        Assert.Equal(expectedBounds, geometry.Bounds);
        Assert.Equal(expectedBounds, body.Bounds);
    }

    private static async Task WaitForPipelineAsync(
        PhaseM31BpmnPropertiesIntegrationTests.HostHarness harness)
    {
        await harness.Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await CommandProcessor.WaitForEventDispatchIdleAsync(harness.Composition.Document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async ValueTask<DocumentCanvasComposition>
        CreateEmptyHostCompositionAsync()
    {
        var baseline = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(
            new DocumentId("test:bpmn:n4:empty-host"),
            connectorAnchorPolicyProvider:
                baseline.Configuration.ConnectorAnchorPolicyProvider).Document);
        return new DocumentCanvasComposition(
            document,
            baseline.Configuration,
            baseline.PropertiesSchemaCatalog,
            baseline.Counters,
            baseline.ToolboxPlacementCatalog,
            baseline.AnchorConnectionCreationCatalog,
            baseline.DocumentCreationIdentityProvider,
            baseline.EndpointReconnectionCatalog,
            baseline.DeletionCatalog);
    }

    private static void AssertExternalLabel(ProjectedGraph graph, SemanticElementId elementId)
    {
        var node = Assert.Single(graph.Nodes, candidate =>
            candidate.Source.SemanticElementId == elementId);
        var label = Assert.Single(graph.Labels, candidate => candidate.OwnerId == node.Id);
        var placement = Assert.IsType<NodeLabelPlacement>(label.NodePlacement);
        Assert.Equal(NodeLabelPlacementKind.OutsideBelow, placement.Kind);
        Assert.Equal(8d, placement.Gap);
        Assert.Equal(160d, placement.MaximumWidth);
        Assert.Equal(NodeLabelInteractionPolicy.MoveAndResize, label.NodeInteractionPolicy);
    }

    private static void AssertVisual(
        Canvas2DScene scene,
        VisualStateId visualStateId,
        Canvas2DSceneGeometryKind bodyGeometry,
        int expectedMarkerCount)
    {
        var body = Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.ProjectedRuntimeObject));
        Assert.Equal(bodyGeometry, body.Geometry.Kind);
        var markers = scene.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Decoration &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.RegisteredExtension))
            .ToArray();
        Assert.Equal(expectedMarkerCount, markers.Length);
        Assert.All(markers, marker =>
            Assert.Equal(Canvas2DHitTestMode.None, marker.HitTestPolicy.Mode));
    }

    private static Dictionary<VisualStateId, RectD> NodeBounds(
        Canvas2DScene scene,
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot document)
    {
        var nodeVisualIds = document.VisualModel.VisualStates
            .Where(visual => document.SemanticModel.TryGetElement(
                visual.SemanticElementId,
                out _))
            .Select(static visual => visual.Id)
            .ToHashSet();
        return scene.Items
            .Where(item =>
                item.Layer == Canvas2DSceneLayer.Content &&
                item.Origin.VisualStateId is { } visualStateId &&
                nodeVisualIds.Contains(visualStateId) &&
                item.IsVisible)
            .ToDictionary(item => item.Origin.VisualStateId!, item => item.Bounds);
    }

    private static void AssertSurvivorsUnchanged(
        Dictionary<VisualStateId, RectD> before,
        Dictionary<VisualStateId, RectD> after,
        VisualStateId removed)
    {
        Assert.DoesNotContain(removed, after.Keys);
        Assert.Equal(before.Count - 1, after.Count);
        foreach (var (id, bounds) in before)
        {
            if (id != removed)
            {
                Assert.Equal(bounds, after[id]);
            }
        }
    }

    private static void AssertBoundsEqual(
        Dictionary<VisualStateId, RectD> expected,
        Dictionary<VisualStateId, RectD> actual)
    {
        Assert.Equal(expected.Keys.OrderBy(id => id.Value), actual.Keys.OrderBy(id => id.Value));
        Assert.All(expected, pair => Assert.Equal(pair.Value, actual[pair.Key]));
    }

    private static Dictionary<ProjectedObjectId, System.Collections.Immutable.ImmutableArray<PointD>>
        RoutePaths(PhaseM31BpmnPropertiesIntegrationTests.HostHarness harness) =>
        Assert.IsType<RoutingResult>(harness.State.RoutingResult).Routes.ToDictionary(
            static route => route.ProjectedEdgeId,
            static route => route.Path);

    private static void AssertRoutePathsEqual(
        Dictionary<ProjectedObjectId, System.Collections.Immutable.ImmutableArray<PointD>> expected,
        Dictionary<ProjectedObjectId, System.Collections.Immutable.ImmutableArray<PointD>> actual)
    {
        Assert.Equal(
            expected.Keys.OrderBy(static id => id.Value),
            actual.Keys.OrderBy(static id => id.Value));
        Assert.All(expected, pair => Assert.True(
            pair.Value.SequenceEqual(actual[pair.Key]),
            $"Route {pair.Key.Value} changed."));
    }
}
