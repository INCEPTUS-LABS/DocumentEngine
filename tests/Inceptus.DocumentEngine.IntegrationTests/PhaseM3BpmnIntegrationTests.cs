using System.Text;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseM3BpmnIntegrationTests
{
    private static readonly DocumentId DocumentId = new("bpmn:m3-document");
    private static readonly SemanticElementId StartId = new("bpmn:m3:start");
    private static readonly SemanticElementId TaskId = new("bpmn:m3:task");
    private static readonly SemanticElementId EndId = new("bpmn:m3:end");
    private static readonly SemanticElementId FirstFlowId = new("bpmn:m3:flow:start-task");
    private static readonly SemanticElementId SecondFlowId = new("bpmn:m3:flow:task-end");
    private static readonly VisualStateId StartVisualId = new("bpmn:m3:visual:start");
    private static readonly VisualStateId TaskVisualId = new("bpmn:m3:visual:task");
    private static readonly VisualStateId EndVisualId = new("bpmn:m3:visual:end");
    private static readonly VisualStateId FirstFlowVisualId = new("bpmn:m3:visual:flow-1");
    private static readonly VisualStateId SecondFlowVisualId = new("bpmn:m3:visual:flow-2");
    private static readonly ConnectorAnchorId StartSourceAnchorId =
        new("bpmn:m3:anchor:start-source");
    private static readonly ConnectorAnchorId TaskTargetAnchorId =
        new("bpmn:m3:anchor:task-target");
    private static readonly ConnectorAnchorId TaskSourceAnchorId =
        new("bpmn:m3:anchor:task-source");
    private static readonly ConnectorAnchorId EndTargetAnchorId =
        new("bpmn:m3:anchor:end-target");
    private const string LongTaskName =
        "Review submitted international customer order and verify approval documents";

    [Fact]
    public async Task CompleteM1ToM3PipelineProducesOneCanonicalBpmnVisualPerLogicalObject()
    {
        var harness = await CreateModelAsync();
        var snapshot = harness.Document.CaptureSnapshot();

        var first = RunPipeline(snapshot, new EditorStateSnapshot(selection: [TaskVisualId]));
        var repeated = RunPipeline(snapshot, new EditorStateSnapshot(selection: [TaskVisualId]));

        Assert.Equal(first.Graph, repeated.Graph);
        Assert.Equal(first.Layout, repeated.Layout);
        Assert.Equal(first.Routing, repeated.Routing);
        Assert.Equal(first.Scene, repeated.Scene);
        Assert.Equal(3, first.Graph.NodeCount);
        Assert.Equal(2, first.Graph.EdgeCount);

        var start = NodeItem(first, StartId);
        var task = NodeItem(first, TaskId);
        var end = NodeItem(first, EndId);
        Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, start.Geometry.Kind);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, task.Geometry.Kind);
        Assert.True(task.Geometry.IsClosed);
        Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, end.Geometry.Kind);
        Assert.True(end.Style.StrokeWidth > start.Style.StrokeWidth);

        Assert.All(first.Graph.Nodes, node =>
        {
            var canonicalId = Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node");
            var item = Assert.Single(first.Scene.Items, candidate => candidate.Id == canonicalId);
            Assert.Equal(node.Source.SemanticElementId, item.Origin.SemanticElementId);
            Assert.Equal(node.Source.VisualStateId, item.Origin.VisualStateId);
            Assert.Equal(node.Id, item.Origin.ProjectedObjectId);
            Assert.False(item.Origin.Categories.HasFlag(
                Canvas2DSceneOriginCategory.RegisteredExtension));
            Assert.True(item.Metadata[Canvas2DMoveGestureMetadata.MoveCapable].BooleanValue);
            Assert.True(item.Metadata[Canvas2DResizeGestureMetadata.ResizeCapable].BooleanValue);
        });

        var label = Assert.Single(first.Graph.Labels);
        var labelItem = Assert.Single(first.Scene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(label.Id, "label"));
        Assert.Equal(Canvas2DSceneGeometryKind.Text, labelItem.Geometry.Kind);
        Assert.Equal("Review order", labelItem.Geometry.Content);
        Assert.Equal(TaskId, labelItem.Origin.SemanticElementId);
        Assert.Equal(TaskVisualId, labelItem.Origin.VisualStateId);
        Assert.DoesNotContain("REVIEW_ORDER", labelItem.Geometry.Content, StringComparison.Ordinal);

        foreach (var route in first.Routing.Routes)
        {
            var edge = first.Graph.Edges.Single(candidate => candidate.Id == route.ProjectedEdgeId);
            var connector = Assert.Single(first.Scene.Items, item =>
                item.Id == Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector"));
            var arrow = Assert.Single(first.Scene.Items, item =>
                item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                    edge.Id,
                    "connector-target-arrow"));
            Assert.Equal(route.Path.AsEnumerable(), connector.Geometry.Points.AsEnumerable());
            Assert.Equal(route.DestinationAnchor, arrow.Geometry.Points[0]);
            Assert.Equal(edge.Source.SemanticElementId, connector.Origin.SemanticElementId);
            Assert.Equal(connector.Origin, arrow.Origin);
        }

        var hitTesting = new Canvas2DSceneHitTestService();
        Assert.Equal(StartVisualId, hitTesting.HitTest(first.Scene, Center(start.Bounds))?
            .Origin.VisualStateId);
        Assert.Equal(TaskVisualId, hitTesting.HitTest(first.Scene, Center(task.Bounds))?
            .Origin.VisualStateId);
        Assert.Equal(EndVisualId, hitTesting.HitTest(first.Scene, Center(end.Bounds))?
            .Origin.VisualStateId);
        var firstRoute = Route(first, FirstFlowId);
        Assert.Equal(
            FirstFlowVisualId,
            hitTesting.HitTest(
                first.Scene,
                Midpoint(firstRoute.Path[0], firstRoute.Path[1]))?.Origin.VisualStateId);

        var taskSelectionOutline = Assert.Single(first.Scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.HitTestPolicy.Mode == Canvas2DHitTestMode.None &&
            item.Origin.RelatedSceneObjectIds.Contains(task.Id) &&
            item.Geometry.Equals(task.Geometry));
        Assert.Equal(task.Bounds, taskSelectionOutline.Bounds);
        Assert.Equal(snapshot, harness.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task GenericMoveResizeUndoAndRedoKeepBpmnShapeLabelAndRoutesSynchronized()
    {
        var harness = await CreateModelAsync();
        var semanticBefore = harness.Document.CaptureSnapshot().SemanticModel;
        var automatic = RunPipeline(harness.Document.CaptureSnapshot());

        var move = await harness.History.ExecuteAsync(
            harness.Processor,
            new MoveVisualStateCommand(
                DocumentId,
                harness.Document.Revision,
                TaskVisualId,
                new PointD(480d, 180d),
                VisualPlacementMode.Pinned));
        Assert.True(move.IsCommitted);
        var moved = RunPipeline(harness.Document.CaptureSnapshot());
        Assert.Equal(new RectD(480d, 180d, 120d, 80d), NodeItem(moved, TaskId).Bounds);
        Assert.NotEqual(Route(automatic, FirstFlowId), Route(moved, FirstFlowId));

        var resize = await harness.History.ExecuteAsync(
            harness.Processor,
            new ResizeVisualStateCommand(
                DocumentId,
                harness.Document.Revision,
                TaskVisualId,
                new RectD(480d, 180d, 200d, 110d),
                VisualPlacementMode.Pinned));
        Assert.True(resize.IsCommitted);
        var resized = RunPipeline(harness.Document.CaptureSnapshot());
        var resizedTask = NodeItem(resized, TaskId);
        Assert.Equal(new RectD(480d, 180d, 200d, 110d), resizedTask.Bounds);
        Assert.Equal(new RectD(0d, 0d, 200d, 110d), resizedTask.Geometry.Bounds);
        var resizedLabel = resized.Scene.Items.Single(item =>
            item.Origin.SemanticElementId == TaskId &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text);
        Assert.Equal(resizedTask.Bounds, resizedLabel.Bounds);
        Assert.Equal(new PointD(100d, 55d), resizedLabel.Geometry.TextAnchor);
        Assert.NotEqual(Route(moved, SecondFlowId), Route(resized, SecondFlowId));
        AssertSemanticContentEqual(
            semanticBefore,
            harness.Document.CaptureSnapshot().SemanticModel);

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(
            new RectD(480d, 180d, 120d, 80d),
            NodeItem(RunPipeline(harness.Document.CaptureSnapshot()), TaskId).Bounds);
        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(
            resizedTask,
            NodeItem(RunPipeline(harness.Document.CaptureSnapshot()), TaskId));
    }

    [Fact]
    public async Task LongBpmnTaskNameUsesGenericMeasuredWrappingAndReflowsAfterResize()
    {
        var harness = await CreateModelAsync(LongTaskName);
        var narrow = await RunMeasuredPipelineAsync(harness.Document.CaptureSnapshot());
        var narrowTask = NodeItem(narrow, TaskId);
        var narrowLines = TaskLabelLines(narrow);

        Assert.True(narrowLines.Length > 1);
        Assert.Equal(LongTaskName, string.Join(
            " ",
            narrowLines.Select(static line => line.Geometry.Content)));
        AssertMeasuredLinesCenteredInside(narrowTask, narrowLines);

        var resize = await harness.History.ExecuteAsync(
            harness.Processor,
            new ResizeVisualStateCommand(
                DocumentId,
                harness.Document.Revision,
                TaskVisualId,
                new RectD(narrowTask.Bounds.X, narrowTask.Bounds.Y, 280d, 100d),
                VisualPlacementMode.Pinned));
        Assert.True(resize.IsCommitted);
        var wide = await RunMeasuredPipelineAsync(harness.Document.CaptureSnapshot());
        var wideTask = NodeItem(wide, TaskId);
        var wideLines = TaskLabelLines(wide);

        Assert.True(wideLines.Length < narrowLines.Length);
        AssertMeasuredLinesCenteredInside(wideTask, wideLines);
        Assert.True(harness.Document.SemanticModel.TryGetElement(TaskId, out var semanticTask));
        Assert.Equal(LongTaskName, semanticTask!.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, wideTask.Geometry.Kind);
    }

    [Fact]
    public async Task GenericManualBendHistoryRendersOnlyTheAuthoritativeM2Route()
    {
        var harness = await CreateModelAsync();
        var semanticBefore = harness.Document.CaptureSnapshot().SemanticModel;
        var automatic = RunPipeline(harness.Document.CaptureSnapshot());
        var automaticRoute = Route(automatic, FirstFlowId);
        var addedBend = new PointD(75d, 125d);

        Assert.True((await SetRouteAsync(harness,
        [
            automaticRoute.SourceAnchor,
            addedBend,
            automaticRoute.DestinationAnchor,
        ])).IsCommitted);
        var added = RunPipeline(harness.Document.CaptureSnapshot());
        AssertPersistentManualRoute(
            harness.Document.CaptureSnapshot(),
            added,
            [addedBend]);
        AssertConnectorMatchesRoute(added, FirstFlowId);

        var movedBend = new PointD(86d, 147d);
        var addedRoute = Route(added, FirstFlowId);
        Assert.True((await SetRouteAsync(harness,
        [
            addedRoute.SourceAnchor,
            movedBend,
            addedRoute.DestinationAnchor,
        ])).IsCommitted);
        var moved = RunPipeline(harness.Document.CaptureSnapshot());
        AssertPersistentManualRoute(
            harness.Document.CaptureSnapshot(),
            moved,
            [movedBend]);
        AssertConnectorMatchesRoute(moved, FirstFlowId);

        var movedRoute = Route(moved, FirstFlowId);
        Assert.True((await SetRouteAsync(harness,
        [
            movedRoute.SourceAnchor,
            movedRoute.DestinationAnchor,
        ])).IsCommitted);
        var deleted = RunPipeline(harness.Document.CaptureSnapshot());
        AssertPersistentManualRoute(
            harness.Document.CaptureSnapshot(),
            deleted,
            []);
        AssertConnectorMatchesRoute(deleted, FirstFlowId);

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        var undoneSnapshot = harness.Document.CaptureSnapshot();
        AssertPersistentManualRoute(
            undoneSnapshot,
            RunPipeline(undoneSnapshot),
            [movedBend]);
        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        var redoneSnapshot = harness.Document.CaptureSnapshot();
        AssertPersistentManualRoute(
            redoneSnapshot,
            RunPipeline(redoneSnapshot),
            []);
        AssertSemanticContentEqual(
            semanticBefore,
            harness.Document.CaptureSnapshot().SemanticModel);
    }

    [Fact]
    public async Task BpmnVisualOverridesRemainCompatibleWithGenericAnchorsAndSelectionOverlays()
    {
        var harness = await CreateModelAsync();
        var snapshot = harness.Document.CaptureSnapshot();
        var anchorId = new ConnectorAnchorId("bpmn:m3:anchor:start-top");
        var anchored = WithSourceAnchor(snapshot, anchorId);
        var artifacts = RunPipeline(
            anchored,
            new EditorStateSnapshot(selection: [StartVisualId]));
        var start = NodeItem(artifacts, StartId);
        var startNode = artifacts.Graph.Nodes.Single(node =>
            node.Source.SemanticElementId == StartId);
        var flow = artifacts.Graph.Edges.Single(edge =>
            edge.Source.SemanticElementId == FirstFlowId);
        Assert.Equal(4, artifacts.Graph.Ports.Length);
        var port = Assert.Single(artifacts.Graph.Ports, candidate =>
            ProjectedConnectorAnchorMetadata.TryDecode(candidate, out var metadata) &&
            metadata?.Id == anchorId);

        Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, start.Geometry.Kind);
        Assert.Equal(startNode.Id, port.OwnerNodeId);
        Assert.Equal(port.Id, flow.SourcePortId);
        Assert.Equal(port.Id, Route(artifacts, FirstFlowId).SourcePortId);
        Assert.Contains(artifacts.Scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Metadata.TryGetValue(Canvas2DConnectorAnchorMetadata.AnchorId, out var value) &&
            value.TextValue == anchorId.Value);
        var selectionOutline = Assert.Single(artifacts.Scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.HitTestPolicy.Mode == Canvas2DHitTestMode.None &&
            item.Origin.RelatedSceneObjectIds.Contains(start.Id) &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Ellipse);
        Assert.Equal(start.Bounds, selectionOutline.Bounds);
        Assert.Equal(snapshot, harness.Document.CaptureSnapshot());
    }

    [Theory]
    [InlineData(0.75d)]
    [InlineData(1d)]
    [InlineData(1.5d)]
    public async Task ZoomChangesOnlyViewportPresentationAndLeavesM3LogicalSceneItemsInvariant(
        double zoom)
    {
        var harness = await CreateModelAsync();
        var snapshot = harness.Document.CaptureSnapshot();
        var baseline = RunPipeline(snapshot);
        var zoomed = RunPipeline(
            snapshot,
            new EditorStateSnapshot(
                viewport: new ViewportSnapshot(zoom, new VectorD(41d, -17d))));

        Assert.Equal(baseline.Scene.Items.AsEnumerable(), zoomed.Scene.Items.AsEnumerable());
        Assert.Equal(baseline.Graph, zoomed.Graph);
        Assert.Equal(baseline.Layout, zoomed.Layout);
        Assert.Equal(baseline.Routing, zoomed.Routing);
    }

    private static async Task<Harness> CreateModelAsync(string taskName = "Review order")
    {
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(DocumentId).Document);
        var registration = BpmnPluginRegistration.M3;
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies);
        var history = new HistoryManager(document);

        Assert.True((await history.ExecuteAsync(processor, new CreateBpmnStartEventCommand(
            DocumentId,
            document.Revision,
            StartId,
            StartVisualId,
            new PointD(0d, 0d),
            new SizeD(36d, 36d),
            VisualPlacementMode.Automatic))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, new CreateBpmnTaskCommand(
            DocumentId,
            document.Revision,
            TaskId,
            TaskVisualId,
            new PointD(0d, 0d),
            new SizeD(120d, 80d),
            "REVIEW_ORDER",
            taskName,
            20L,
            VisualPlacementMode.Automatic))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, new CreateBpmnEndEventCommand(
            DocumentId,
            document.Revision,
            EndId,
            EndVisualId,
            new PointD(0d, 0d),
            new SizeD(36d, 36d),
            VisualPlacementMode.Automatic))).IsCommitted);
        await AddAnchorAsync(
            history, processor, document, StartVisualId, StartSourceAnchorId,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        await AddAnchorAsync(
            history, processor, document, TaskVisualId, TaskTargetAnchorId,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0);
        await AddAnchorAsync(
            history, processor, document, TaskVisualId, TaskSourceAnchorId,
            ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0);
        await AddAnchorAsync(
            history, processor, document, EndVisualId, EndTargetAnchorId,
            ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0);
        Assert.True((await history.ExecuteAsync(processor, new CreateBpmnSequenceFlowCommand(
            DocumentId,
            document.Revision,
            FirstFlowId,
            FirstFlowVisualId,
            StartId,
            TaskId,
            StartSourceAnchorId,
            TaskTargetAnchorId))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, new CreateBpmnSequenceFlowCommand(
            DocumentId,
            document.Revision,
            SecondFlowId,
            SecondFlowVisualId,
            TaskId,
            EndId,
            TaskSourceAnchorId,
            EndTargetAnchorId))).IsCommitted);

        return new Harness(document, processor, history);
    }

    private static Artifacts RunPipeline(
        DocumentSnapshot snapshot,
        EditorStateSnapshot? editorState = null)
    {
        var registration = BpmnPluginRegistration.M3;
        var projectionExecution = new ProjectionEngine(registration.ProjectionRules).Project(snapshot);
        Assert.True(projectionExecution.IsSuccessful);
        var graph = Assert.IsType<ProjectedGraph>(projectionExecution.Graph);
        var layoutExecution = new LayoutEngine(registration.LayoutAlgorithms).Layout(
            graph,
            BpmnAlgorithmIds.DefaultLayout);
        Assert.True(layoutExecution.IsSuccessful);
        var layout = Assert.IsType<LayoutResult>(layoutExecution.Result);
        var routingExecution = new RoutingEngine(registration.RoutingAlgorithms).Route(
            graph,
            layout,
            BpmnAlgorithmIds.DefaultRouting);
        Assert.True(routingExecution.IsSuccessful);
        var routing = Assert.IsType<RoutingResult>(routingExecution.Result);
        var sceneExecution = new Canvas2DSceneBuilder(
            contributors: registration.SceneContributors).Build(
                graph,
                layout,
                routing,
                snapshot.VisualModel,
                editorState ?? EditorStateSnapshot.Empty);
        Assert.True(sceneExecution.Succeeded);
        return new Artifacts(
            graph,
            layout,
            routing,
            Assert.IsType<Canvas2DScene>(sceneExecution.Scene));
    }

    private static async Task<Artifacts> RunMeasuredPipelineAsync(DocumentSnapshot snapshot)
    {
        var registration = BpmnPluginRegistration.M3;
        var projectionExecution = new ProjectionEngine(registration.ProjectionRules).Project(snapshot);
        Assert.True(projectionExecution.IsSuccessful);
        var graph = Assert.IsType<ProjectedGraph>(projectionExecution.Graph);
        var layoutExecution = new LayoutEngine(registration.LayoutAlgorithms).Layout(
            graph,
            BpmnAlgorithmIds.DefaultLayout);
        Assert.True(layoutExecution.IsSuccessful);
        var layout = Assert.IsType<LayoutResult>(layoutExecution.Result);
        var routingExecution = new RoutingEngine(registration.RoutingAlgorithms).Route(
            graph,
            layout,
            BpmnAlgorithmIds.DefaultRouting);
        Assert.True(routingExecution.IsSuccessful);
        var routing = Assert.IsType<RoutingResult>(routingExecution.Result);
        var sceneExecution = await new Canvas2DSceneBuilder(
            contributors: registration.SceneContributors).BuildMeasuredAsync(
                graph,
                layout,
                routing,
                snapshot.VisualModel,
                EditorStateSnapshot.Empty,
                new FixedMetricsService(),
                CreateMeasurementRequest);
        Assert.True(sceneExecution.Succeeded);
        return new Artifacts(
            graph,
            layout,
            routing,
            Assert.IsType<Canvas2DScene>(sceneExecution.Scene));
    }

    private static Canvas2DSceneItem NodeItem(
        Artifacts artifacts,
        SemanticElementId semanticElementId)
    {
        var node = artifacts.Graph.Nodes.Single(candidate =>
            candidate.Source.SemanticElementId == semanticElementId);
        return artifacts.Scene.Items.Single(item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node"));
    }

    private static RoutedConnectorGeometry Route(
        Artifacts artifacts,
        SemanticElementId relationshipId)
    {
        var edge = artifacts.Graph.Edges.Single(candidate =>
            candidate.Source.SemanticElementId == relationshipId);
        return artifacts.Routing.Routes.Single(route => route.ProjectedEdgeId == edge.Id);
    }

    private static void AssertConnectorMatchesRoute(
        Artifacts artifacts,
        SemanticElementId relationshipId)
    {
        var edge = artifacts.Graph.Edges.Single(candidate =>
            candidate.Source.SemanticElementId == relationshipId);
        var route = Route(artifacts, relationshipId);
        var connector = artifacts.Scene.Items.Single(item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector"));
        var arrow = artifacts.Scene.Items.Single(item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                edge.Id,
                "connector-target-arrow"));
        Assert.Equal(
            route.Path.AsEnumerable(),
            Canvas2DConnectorPathMetadata.Resolve(connector).AsEnumerable());
        Assert.Equal(route.DestinationAnchor, arrow.Geometry.Points[0]);
    }

    private static void AssertPersistentManualRoute(
        DocumentSnapshot snapshot,
        Artifacts artifacts,
        IReadOnlyList<PointD> expectedWaypoints)
    {
        var persistent = Assert.Single(snapshot.VisualModel.VisualStates, visual =>
            visual.Id == FirstFlowVisualId);
        Assert.Equal(
            expectedWaypoints.AsEnumerable(),
            persistent.Route.Skip(1).SkipLast(1));

        var route = Route(artifacts, FirstFlowId);
        Assert.Equal(route.SourceAnchor, persistent.Route[0]);
        Assert.Equal(route.DestinationAnchor, persistent.Route[^1]);
        AssertWaypointsInOrder(route.Path, expectedWaypoints);
    }

    private static void AssertWaypointsInOrder(
        IReadOnlyList<PointD> routedPath,
        IReadOnlyList<PointD> expectedWaypoints)
    {
        var searchIndex = 0;
        foreach (var expected in expectedWaypoints)
        {
            while (searchIndex < routedPath.Count && routedPath[searchIndex] != expected)
            {
                searchIndex++;
            }

            Assert.True(
                searchIndex < routedPath.Count,
                $"The routed path does not preserve manual waypoint {expected} in order.");
            searchIndex++;
        }
    }

    private static Canvas2DSceneItem[] TaskLabelLines(Artifacts artifacts)
    {
        var label = Assert.Single(artifacts.Graph.Labels);
        return artifacts.Scene.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Origin.ProjectedObjectId == label.Id).ToArray();
    }

    private static void AssertMeasuredLinesCenteredInside(
        Canvas2DSceneItem task,
        Canvas2DSceneItem[] lines)
    {
        var innerBounds = new RectD(
            task.Bounds.X + 8d,
            task.Bounds.Y + 8d,
            task.Bounds.Width - 16d,
            task.Bounds.Height - 16d);
        Assert.All(lines, line =>
        {
            Assert.Equal(innerBounds, line.Clip);
            Assert.Equal(Canvas2DTextAlignment.Center, line.Geometry.TextAlignment);
            Assert.Equal(Canvas2DTextBaseline.Middle, line.Geometry.TextBaseline);
            Assert.True(line.Geometry.Bounds.Width <= innerBounds.Width);
            Assert.Equal(Center(task.Bounds).X, DocumentAnchor(line).X, 9);
        });
        Assert.Equal(
            Center(task.Bounds).Y,
            (DocumentAnchor(lines[0]).Y + DocumentAnchor(lines[^1]).Y) / 2d,
            9);
    }

    private static ValueTask<HistoryOperationResult> SetRouteAsync(
        Harness harness,
        IEnumerable<PointD> path) =>
        harness.History.ExecuteAsync(
            harness.Processor,
            new UpdateConnectionRouteCommand(
                DocumentId,
                harness.Document.Revision,
                FirstFlowVisualId,
                path));

    private static DocumentSnapshot WithSourceAnchor(
        DocumentSnapshot snapshot,
        ConnectorAnchorId anchorId)
    {
        var visuals = snapshot.VisualModel.VisualStates.Select(visual =>
        {
            if (visual.Id == StartVisualId)
            {
                return Copy(
                    visual,
                    connectorAnchors:
                    [
                        new ConnectorAnchor(
                            anchorId,
                            ConnectorAnchorSide.Top,
                            ConnectorAnchorRole.Source,
                            0),
                    ]);
            }

            return visual.Id == FirstFlowVisualId
                ? Copy(visual, sourceAnchorId: anchorId)
                : visual;
        });
        return new DocumentSnapshot(
            snapshot.SemanticModel,
            new VisualModelSnapshot(snapshot.DocumentId, snapshot.Revision, visuals),
            snapshot.Metadata);
    }

    private static async ValueTask AddAnchorAsync(
        HistoryManager history,
        CommandProcessor processor,
        Document document,
        VisualStateId visualStateId,
        ConnectorAnchorId anchorId,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role,
        int insertionIndex)
    {
        var result = await history.ExecuteAsync(
            processor,
            new AddConnectorAnchorCommand(
                document.DocumentId,
                document.Revision,
                visualStateId,
                anchorId,
                side,
                role,
                insertionIndex));
        Assert.True(result.IsCommitted);
    }

    private static VisualStateSnapshot Copy(
        VisualStateSnapshot visual,
        IEnumerable<ConnectorAnchor>? connectorAnchors = null,
        ConnectorAnchorId? sourceAnchorId = null) =>
        new(
            visual.Id,
            visual.SemanticElementId,
            visual.Position,
            visual.Size,
            visual.PlacementMode,
            visual.Route,
            visual.Properties,
            connectorAnchors ?? visual.ConnectorAnchors,
            sourceAnchorId ?? visual.SourceAnchorId,
            visual.TargetAnchorId);

    private static PointD Center(RectD bounds) =>
        new(bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static PointD Midpoint(PointD left, PointD right) =>
        new((left.X + right.X) / 2d, (left.Y + right.Y) / 2d);

    private static PointD DocumentAnchor(Canvas2DSceneItem item) =>
        item.Transform.TransformPoint(item.Geometry.TextAnchor);

    private static TextMeasurementRequest CreateMeasurementRequest(
        string text,
        Canvas2DSceneStyle style,
        double lineHeight) =>
        new(
            text,
            "Test Sans",
            "test:sans",
            "1",
            style.FontSize,
            lineHeight,
            400,
            TextFontStyle.Normal,
            "und",
            TextDirection.LeftToRight,
            TextWritingMode.HorizontalTopToBottom,
            1d,
            "test.metrics",
            "1");

    private static void AssertSemanticContentEqual(
        SemanticModelSnapshot expected,
        SemanticModelSnapshot actual)
    {
        Assert.Equal(expected.Elements.AsEnumerable(), actual.Elements.AsEnumerable());
        Assert.Equal(expected.Relationships.AsEnumerable(), actual.Relationships.AsEnumerable());
    }

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History);

    private sealed record Artifacts(
        ProjectedGraph Graph,
        LayoutResult Layout,
        RoutingResult Routing,
        Canvas2DScene Scene);

    private sealed class FixedMetricsService : ITextMetricsService
    {
        public ValueTask<TextMeasurementResult> MeasureAsync(
            TextMeasurementRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var width = request.Text.EnumerateRunes().Count() * 5d;
            return ValueTask.FromResult(TextMeasurementResult.Success(new TextMetrics(
                width,
                9d,
                3d,
                request.LineHeight,
                new RectD(0d, -9d, width, 12d),
                $"{request.FontIdentity}@{request.FontVersion}")));
        }
    }
}
