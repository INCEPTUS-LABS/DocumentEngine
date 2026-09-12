using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN42ObstacleAwareRoutingIntegrationTests
{
    [Fact]
    public async Task DocumentPipelineRoutesSequenceFlowAroundPinnedTaskWithoutNodeReflow()
    {
        var model = await CreateModelAsync(
            "obstacle",
            [
                Node("source", new RectD(40d, 100d, 120d, 80d)),
                Node("obstacle", new RectD(230d, 90d, 120d, 100d)),
                Node("target", new RectD(440d, 100d, 120d, 80d)),
            ],
            [
                Flow(
                    "flow",
                    "source",
                    ConnectorAnchorSide.Right,
                    "target",
                    ConnectorAnchorSide.Left),
            ]);
        var snapshot = model.Document.CaptureSnapshot();

        var artifacts = RunPipeline(snapshot);

        var route = Assert.Single(artifacts.Routing.Routes);
        var obstacle = NodeBounds(artifacts, model.Nodes["obstacle"].SemanticId);
        var inflated = Inflate(obstacle, 10d);
        Assert.Equal(new RectD(230d, 90d, 120d, 100d), obstacle);
        Assert.All(RouteSegments(route.Path), segment =>
            Assert.False(CrossesStrictInterior(segment.Start, segment.End, inflated)));
        Assert.Contains(route.Path, point => point.Y <= inflated.Top || point.Y >= inflated.Bottom);
        Assert.All(model.Nodes, pair => Assert.Equal(
            pair.Value.Bounds,
            NodeBounds(artifacts, pair.Value.SemanticId)));
        Assert.Equal(snapshot, model.Document.CaptureSnapshot());

        var connector = Assert.Single(artifacts.Scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == model.Flows["flow"].VisualId &&
            !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));
        Assert.Equal(route.Path[0], Canvas2DConnectorPathMetadata.Resolve(connector)[0]);
        Assert.Equal(route.Path[^1], Canvas2DConnectorPathMetadata.Resolve(connector)[^1]);
    }

    [Fact]
    public async Task CrossingCommittedFlowsProduceOneDerivedHorizontalBridgeWithoutJunction()
    {
        var model = await CreateModelAsync(
            "crossing",
            [
                Node("left", new RectD(40d, 200d, 120d, 80d)),
                Node("right", new RectD(440d, 200d, 120d, 80d)),
                Node("top", new RectD(260d, 20d, 120d, 80d)),
                Node("bottom", new RectD(260d, 360d, 120d, 80d)),
            ],
            [
                Flow(
                    "horizontal",
                    "left",
                    ConnectorAnchorSide.Right,
                    "right",
                    ConnectorAnchorSide.Left),
                Flow(
                    "vertical",
                    "top",
                    ConnectorAnchorSide.Bottom,
                    "bottom",
                    ConnectorAnchorSide.Top),
            ]);
        var snapshot = model.Document.CaptureSnapshot();
        var relationshipCount = snapshot.SemanticModel.RelationshipCount;
        var anchorCount = snapshot.VisualModel.VisualStates.Sum(static visual =>
            visual.ConnectorAnchors.Length);

        var artifacts = RunPipeline(snapshot);

        var horizontalRoute = Route(artifacts, model.Flows["horizontal"].SemanticId);
        var verticalRoute = Route(artifacts, model.Flows["vertical"].SemanticId);
        var horizontal = Connector(artifacts.Scene, model.Flows["horizontal"].VisualId);
        var vertical = Connector(artifacts.Scene, model.Flows["vertical"].VisualId);
        var horizontalLogical = Canvas2DConnectorPathMetadata.Resolve(horizontal);
        var verticalLogical = Canvas2DConnectorPathMetadata.Resolve(vertical);

        Assert.Equal(horizontalRoute.Path.AsEnumerable(), horizontalLogical.AsEnumerable());
        Assert.Equal(verticalRoute.Path.AsEnumerable(), verticalLogical.AsEnumerable());
        Assert.Contains(horizontal.Geometry.Points, point => point.Y > 240d);
        Assert.Equal(verticalLogical.AsEnumerable(), vertical.Geometry.Points.AsEnumerable());
        Assert.Equal(relationshipCount, model.Document.SemanticModel.RelationshipCount);
        Assert.Equal(anchorCount, model.Document.VisualModel.VisualStates.Sum(static visual =>
            visual.ConnectorAnchors.Length));
        Assert.Equal(snapshot, model.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task FallbackRouteStillDerivesLineJumpFromEffectiveCrossingGeometry()
    {
        var model = await CreateModelAsync(
            "fallback-crossing",
            [
                Node("left", new RectD(40d, 200d, 120d, 80d)),
                Node("right", new RectD(440d, 200d, 120d, 80d)),
                Node("top", new RectD(260d, 20d, 120d, 80d)),
                Node("bottom", new RectD(260d, 360d, 120d, 80d)),
                Node("guidance-obstacle", new RectD(190d, 100d, 60d, 80d)),
            ],
            [
                Flow(
                    "horizontal",
                    "left",
                    ConnectorAnchorSide.Right,
                    "right",
                    ConnectorAnchorSide.Left),
                Flow(
                    "vertical",
                    "top",
                    ConnectorAnchorSide.Bottom,
                    "bottom",
                    ConnectorAnchorSide.Top),
            ]);
        PointD[] blockedGuidance =
        [
            new PointD(160d, 240d),
            new PointD(220d, 140d),
            new PointD(440d, 240d),
        ];
        await ExecuteAsync(
            model.History,
            model.Processor,
            new UpdateConnectionRouteCommand(
                model.Document.DocumentId,
                model.Document.Revision,
                model.Flows["horizontal"].VisualId,
                blockedGuidance));
        var snapshot = model.Document.CaptureSnapshot();
        var history = model.History.CaptureStatus();

        var artifacts = RunPipeline(snapshot);

        var horizontalRoute = Route(artifacts, model.Flows["horizontal"].SemanticId);
        var verticalRoute = Route(artifacts, model.Flows["vertical"].SemanticId);
        var horizontal = Connector(artifacts.Scene, model.Flows["horizontal"].VisualId);
        var vertical = Connector(artifacts.Scene, model.Flows["vertical"].VisualId);
        var horizontalLogical = Canvas2DConnectorPathMetadata.Resolve(horizontal);
        var verticalLogical = Canvas2DConnectorPathMetadata.Resolve(vertical);
        var horizontalEditable = Canvas2DConnectorPathMetadata.ResolveEditable(horizontal)
            .Select(horizontal.Transform.TransformPoint);

        Assert.Equal(
            new[] { blockedGuidance[0], blockedGuidance[^1] },
            horizontalRoute.Path.AsEnumerable());
        Assert.DoesNotContain(blockedGuidance[1], horizontalRoute.Path);
        Assert.Equal(horizontalRoute.Path.AsEnumerable(), horizontalLogical.AsEnumerable());
        Assert.Equal(blockedGuidance, horizontalEditable);
        Assert.Contains(horizontal.Geometry.Points, point => point.Y > 240d);
        Assert.Equal(verticalRoute.Path.AsEnumerable(), verticalLogical.AsEnumerable());
        Assert.Equal(verticalLogical.AsEnumerable(), vertical.Geometry.Points.AsEnumerable());
        Assert.Equal(
            blockedGuidance,
            Visual(snapshot, model.Flows["horizontal"].VisualId).Route.AsEnumerable());
        Assert.Equal(snapshot, model.Document.CaptureSnapshot());
        Assert.Equal(history, model.History.CaptureStatus());
    }

    [Fact]
    public async Task AutomaticDemoRouteAddsOnlySparseMilestonesAndRoundTripsManualEdits()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        await using var interaction = new Canvas2DInteractionController(harness.Session);
        var document = harness.Composition.Document;
        var baseline = document.CaptureSnapshot();
        var baselineHistory = harness.State.HistoryStatus;
        var flowId = BpmnDemoPipeline.SixthSequenceFlowId;
        var visualId = BpmnDemoPipeline.SixthSequenceFlowVisualId;
        var automatic = Route(harness.State, flowId);
        var automaticPath = automatic.Path;
        var firstMilestone = new PointD(868d, 254d);
        var secondMilestone = new PointD(1712d, 254d);
        Assert.True(automaticPath.Length > 3);
        Assert.Contains(firstMilestone, automaticPath);
        Assert.Contains(secondMilestone, automaticPath);
        Assert.Empty(Visual(baseline, visualId).Route);

        var initialConnector = Connector(harness.Scene, visualId);
        var initialLogical = Canvas2DConnectorPathMetadata.Resolve(initialConnector)
            .Select(initialConnector.Transform.TransformPoint)
            .ToArray();
        var displayOnlyPoints = initialConnector.Geometry.Points
            .Select(initialConnector.Transform.TransformPoint)
            .Where(point => !initialLogical.Contains(point))
            .ToArray();

        var firstAction = await AddRoutePointThroughHostAsync(
            harness,
            firstMilestone);
        Assert.Equal(firstMilestone, firstAction.RoutePoint);
        var afterFirst = document.CaptureSnapshot();
        var firstRoute = Visual(afterFirst, visualId).Route;
        Assert.Equal(
            new[] { automatic.SourceAnchor, firstMilestone, automatic.DestinationAnchor },
            firstRoute.AsEnumerable());
        Assert.Equal(baselineHistory.EntryCount + 1,
            harness.State.HistoryStatus.EntryCount);
        Assert.Equal(firstMilestone, Center(Assert.Single(
            RouteBendHandles(harness.Scene, visualId)).Bounds));
        Assert.True(Route(harness.State, flowId).Path.Length > firstRoute.Length);
        AssertEffectiveRouteObstacleSafe(harness.State, flowId);
        Assert.DoesNotContain(firstRoute, displayOnlyPoints.Contains);

        var secondAction = await AddRoutePointThroughHostAsync(
            harness,
            secondMilestone);
        Assert.Equal(secondMilestone, secondAction.RoutePoint);
        var afterSecond = document.CaptureSnapshot();
        var secondRoute = Visual(afterSecond, visualId).Route;
        Assert.Equal(
            new[]
            {
                automatic.SourceAnchor,
                firstMilestone,
                secondMilestone,
                automatic.DestinationAnchor,
            },
            secondRoute.AsEnumerable());
        Assert.Equal(baselineHistory.EntryCount + 2,
            harness.State.HistoryStatus.EntryCount);
        Assert.Equal(
            new[] { firstMilestone, secondMilestone },
            RouteBendHandles(harness.Scene, visualId)
                .Select(handle => Center(handle.Bounds))
                .OrderBy(static point => point.X));
        AssertEffectiveRouteObstacleSafe(harness.State, flowId);

        var movedMilestone = secondMilestone + new VectorD(0d, 30d);
        var secondHandle = Assert.Single(RouteBendHandles(harness.Scene, visualId), handle =>
            Center(handle.Bounds) == secondMilestone);
        var pointerId = 42001L;
        Assert.Equal(
            Canvas2DInteractionStatus.Updated,
            (await interaction.PointerPressedAsync(Pointer(
                pointerId,
                harness.Scene,
                Center(secondHandle.Bounds),
                buttons: 1))).Status);
        Assert.Equal(
            Canvas2DInteractionStatus.Updated,
            (await interaction.PointerMovedAsync(Pointer(
                pointerId,
                harness.Scene,
                movedMilestone,
                buttons: 1))).Status);
        Assert.Equal(
            Canvas2DInteractionStatus.Committed,
            (await interaction.PointerReleasedAsync(Pointer(
                pointerId,
                harness.Scene,
                movedMilestone))).Status);
        await harness.Session.WaitForIdleAsync();

        var movedRoute = Visual(document.CaptureSnapshot(), visualId).Route;
        Assert.Equal(
            new[]
            {
                automatic.SourceAnchor,
                firstMilestone,
                movedMilestone,
                automatic.DestinationAnchor,
            },
            movedRoute.AsEnumerable());
        Assert.Equal(baselineHistory.EntryCount + 3,
            harness.State.HistoryStatus.EntryCount);
        Assert.Equal(2, RouteBendHandles(harness.Scene, visualId).Length);
        AssertEffectiveRouteObstacleSafe(harness.State, flowId);

        var movedHandle = Assert.Single(RouteBendHandles(harness.Scene, visualId), handle =>
            Center(handle.Bounds) == movedMilestone);
        await harness.Pointer.ContextMenuDocumentPointAsync(
            harness.Scene,
            Center(movedHandle.Bounds));
        var deleteAction = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            harness.Host.CaptureState().ContextMenu?.ConnectorRouteAction);
        Assert.Equal(Canvas2DConnectorRouteContextActionKind.DeletePoint, deleteAction.Kind);
        var deleted = await harness.Host.ExecuteConnectorRouteContextActionAsync();
        Assert.True(deleted?.IsCommitted);
        await harness.Session.WaitForIdleAsync();

        var deletedRoute = Visual(document.CaptureSnapshot(), visualId).Route;
        Assert.Equal(firstRoute.AsEnumerable(), deletedRoute.AsEnumerable());
        Assert.Equal(baselineHistory.EntryCount + 4,
            harness.State.HistoryStatus.EntryCount);
        Assert.Single(RouteBendHandles(harness.Scene, visualId));

        Assert.True((await harness.Session.UndoAsync()).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(movedRoute.AsEnumerable(),
            Visual(document.CaptureSnapshot(), visualId).Route.AsEnumerable());
        Assert.Equal(2, RouteBendHandles(harness.Scene, visualId).Length);
        Assert.True(harness.State.HistoryStatus.CanRedo);

        Assert.True((await harness.Session.UndoAsync()).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(secondRoute.AsEnumerable(),
            Visual(document.CaptureSnapshot(), visualId).Route.AsEnumerable());
        Assert.Equal(2, RouteBendHandles(harness.Scene, visualId).Length);

        Assert.True((await harness.Session.RedoAsync()).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(movedRoute.AsEnumerable(),
            Visual(document.CaptureSnapshot(), visualId).Route.AsEnumerable());
        Assert.True((await harness.Session.RedoAsync()).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(deletedRoute.AsEnumerable(),
            Visual(document.CaptureSnapshot(), visualId).Route.AsEnumerable());
        Assert.Single(RouteBendHandles(harness.Scene, visualId));
        Assert.Equal(baselineHistory.EntryCount + 4,
            harness.State.HistoryStatus.EntryCount);

        var remainingHandle = Assert.Single(RouteBendHandles(harness.Scene, visualId));
        await harness.Pointer.ContextMenuDocumentPointAsync(
            harness.Scene,
            Center(remainingHandle.Bounds));
        Assert.Equal(
            Canvas2DConnectorRouteContextActionKind.DeletePoint,
            harness.Host.CaptureState().ContextMenu?.ConnectorRouteAction?.Kind);
        var cleared = await harness.Host.ExecuteConnectorRouteContextActionAsync();
        Assert.True(cleared?.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Empty(Visual(document.CaptureSnapshot(), visualId).Route);
        Assert.Empty(RouteBendHandles(harness.Scene, visualId));
        Assert.Equal(baselineHistory.EntryCount + 5,
            harness.State.HistoryStatus.EntryCount);

        Assert.True((await harness.Session.UndoAsync()).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(deletedRoute.AsEnumerable(),
            Visual(document.CaptureSnapshot(), visualId).Route.AsEnumerable());
        Assert.Single(RouteBendHandles(harness.Scene, visualId));
        Assert.True((await harness.Session.RedoAsync()).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Empty(Visual(document.CaptureSnapshot(), visualId).Route);
        Assert.Empty(RouteBendHandles(harness.Scene, visualId));
        Assert.Equal(automaticPath.AsEnumerable(),
            Route(harness.State, flowId).Path.AsEnumerable());
        AssertSemanticModelEqual(baseline, document.CaptureSnapshot());
        AssertEffectiveRouteObstacleSafe(harness.State, flowId);
    }

    [Fact]
    public async Task SparseMilestoneSurvivesAdaptiveClearanceAndEndpointReconnection()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        var document = harness.Composition.Document;
        var flowId = BpmnDemoPipeline.SixthSequenceFlowId;
        var visualId = BpmnDemoPipeline.SixthSequenceFlowVisualId;
        var milestone = new PointD(868d, 254d);
        var baselineHistory = harness.State.HistoryStatus.EntryCount;

        _ = await AddRoutePointThroughHostAsync(harness, milestone);
        var sparseRoute = Visual(document.CaptureSnapshot(), visualId).Route;
        Assert.Equal(3, sparseRoute.Length);
        Assert.Single(RouteBendHandles(harness.Scene, visualId));
        Assert.Contains(milestone, Route(harness.State, flowId).Path);

        var prepare = Visual(
            document.CaptureSnapshot(),
            BpmnDemoPipeline.PrepareShipmentTaskVisualId);
        var movedPreparePosition = NodeBounds(
            harness.State,
            BpmnDemoPipeline.PrepareShipmentTaskId).TopLeft + new VectorD(-20d, 10d);
        var move = await harness.Session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            harness.State.DocumentRevision,
            prepare.Id,
            movedPreparePosition,
            VisualPlacementMode.Pinned));
        Assert.True(move.IsCommitted);
        await harness.Session.WaitForIdleAsync();

        var afterMove = document.CaptureSnapshot();
        var movedPrepare = Visual(afterMove, prepare.Id);
        var movedActualObstacle = Bounds(movedPrepare);
        var movedObstacle = Inflate(movedActualObstacle, 10d);
        Assert.True(
            milestone.X > movedObstacle.Left && milestone.X < movedObstacle.Right &&
            milestone.Y > movedObstacle.Top && milestone.Y < movedObstacle.Bottom);
        Assert.False(
            milestone.X > movedActualObstacle.Left &&
            milestone.X < movedActualObstacle.Right &&
            milestone.Y > movedActualObstacle.Top &&
            milestone.Y < movedActualObstacle.Bottom);
        Assert.Equal(sparseRoute.AsEnumerable(),
            Visual(afterMove, visualId).Route.AsEnumerable());
        Assert.Contains(milestone, Route(harness.State, flowId).Path);
        Assert.Equal(milestone, Center(Assert.Single(
            RouteBendHandles(harness.Scene, visualId)).Bounds));
        Assert.Equal(baselineHistory + 2, harness.State.HistoryStatus.EntryCount);
        AssertEffectiveRouteObstacleSafe(harness.State, flowId, clearance: 0d);

        Assert.True((await harness.Session.UndoAsync()).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(prepare.Position,
            Visual(document.CaptureSnapshot(), prepare.Id).Position);
        Assert.Contains(milestone, Route(harness.State, flowId).Path);
        Assert.Single(RouteBendHandles(harness.Scene, visualId));
        Assert.True((await harness.Session.RedoAsync()).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Contains(milestone, Route(harness.State, flowId).Path);
        Assert.Single(RouteBendHandles(harness.Scene, visualId));

        var beforeReconnect = Visual(document.CaptureSnapshot(), visualId);
        var reconnect = await harness.Session.ExecuteAsync(
            new ReconnectBpmnSequenceFlowEndpointCommand(
                document.DocumentId,
                harness.State.DocumentRevision,
                flowId,
                visualId,
                ConnectorEndpointKind.Source,
                BpmnDemoPipeline.RejectedTaskId,
                BpmnDemoPipeline.RejectedTaskSourceAnchorId,
                BpmnDemoPipeline.RejectedTaskId,
                BpmnDemoPipeline.RejectedTaskReconnectSourceAnchorId));
        Assert.True(reconnect.IsCommitted);
        await harness.Session.WaitForIdleAsync();

        var reconnected = Visual(document.CaptureSnapshot(), visualId);
        Assert.Equal(sparseRoute.AsEnumerable(), reconnected.Route.AsEnumerable());
        Assert.Equal(BpmnDemoPipeline.RejectedTaskReconnectSourceAnchorId,
            reconnected.SourceAnchorId);
        Assert.Equal(beforeReconnect.TargetAnchorId, reconnected.TargetAnchorId);
        Assert.Contains(milestone, Route(harness.State, flowId).Path);
        Assert.Equal(milestone, Center(Assert.Single(
            RouteBendHandles(harness.Scene, visualId)).Bounds));
        Assert.Equal(baselineHistory + 3, harness.State.HistoryStatus.EntryCount);
        AssertEffectiveRouteObstacleSafe(harness.State, flowId, clearance: 0d);

        Assert.True((await harness.Session.UndoAsync()).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(BpmnDemoPipeline.RejectedTaskSourceAnchorId,
            Visual(document.CaptureSnapshot(), visualId).SourceAnchorId);
        Assert.Equal(sparseRoute.AsEnumerable(),
            Visual(document.CaptureSnapshot(), visualId).Route.AsEnumerable());
        Assert.Single(RouteBendHandles(harness.Scene, visualId));
        Assert.True((await harness.Session.RedoAsync()).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(BpmnDemoPipeline.RejectedTaskReconnectSourceAnchorId,
            Visual(document.CaptureSnapshot(), visualId).SourceAnchorId);
        Assert.Equal(sparseRoute.AsEnumerable(),
            Visual(document.CaptureSnapshot(), visualId).Route.AsEnumerable());
        Assert.Single(RouteBendHandles(harness.Scene, visualId));
        Assert.Empty(harness.State.RuntimeDiagnostics);
    }

    [Fact]
    public async Task LineJumpContextAddPersistsLogicalCenterlinePointNotDisplayArc()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        var document = harness.Composition.Document;
        var snapshot = document.CaptureSnapshot();
        var candidate = harness.Scene.Items
            .Where(item =>
                item.Layer == Canvas2DSceneLayer.Connector &&
                item.Origin.VisualStateId is not null &&
                item.Origin.SemanticElementId is not null &&
                item.Metadata.ContainsKey(
                    Canvas2DConnectorPathMetadata.LogicalPathPointCount) &&
                !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow) &&
                Visual(snapshot, item.Origin.VisualStateId).Route.IsEmpty)
            .Select(item => new
            {
                Item = item,
                Logical = Canvas2DConnectorPathMetadata.Resolve(item)
                    .Select(item.Transform.TransformPoint)
                    .ToArray(),
                Display = item.Geometry.Points
                    .Select(item.Transform.TransformPoint)
                    .ToArray(),
            })
            .Select(candidate => new
            {
                candidate.Item,
                candidate.Logical,
                DisplayPoint = candidate.Display
                    .Select(point => new
                    {
                        Point = point,
                        Projection = Canvas2DConnectorPathGeometry.FindNearest(
                            candidate.Logical,
                            point),
                    })
                    .Where(point => point.Point != point.Projection.RoutePoint)
                    .OrderByDescending(point => DistanceSquared(
                        point.Point,
                        point.Projection.RoutePoint))
                    .FirstOrDefault(),
            })
            .First(candidate => candidate.DisplayPoint is not null);
        var displayPoint = candidate.DisplayPoint!.Point;
        var projectedPoint = candidate.DisplayPoint.Projection.RoutePoint;
        var visualId = candidate.Item.Origin.VisualStateId!;
        var flowId = candidate.Item.Origin.SemanticElementId!;

        var action = await AddRoutePointThroughHostAsync(harness, displayPoint);
        Assert.Equal(projectedPoint, action.RoutePoint);
        Assert.NotEqual(displayPoint, action.RoutePoint);
        var persisted = Visual(document.CaptureSnapshot(), visualId).Route;
        Assert.Equal(
            new[] { candidate.Logical[0], projectedPoint, candidate.Logical[^1] },
            persisted.AsEnumerable());
        Assert.DoesNotContain(displayPoint, persisted);
        Assert.Equal(projectedPoint, Center(Assert.Single(
            RouteBendHandles(harness.Scene, visualId)).Bounds));
        AssertEffectiveRouteObstacleSafe(harness.State, flowId);
        Assert.Empty(harness.State.RuntimeDiagnostics);
    }

    [Fact]
    public async Task HostPipelineShortensEndpointLeadAndKeepsRoutedSceneReady()
    {
        var model = await CreateModelAsync(
            "adaptive-endpoint-lead-host",
            [
                Node("source", new RectD(20d, 100d, 80d, 60d)),
                Node("obstacle", new RectD(119d, 139d, 80d, 80d)),
                Node("target", new RectD(300d, 100d, 80d, 60d)),
            ],
            [
                Flow(
                    "flow",
                    "source",
                    ConnectorAnchorSide.Right,
                    "target",
                    ConnectorAnchorSide.Left),
            ]);
        var baselineComposition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var composition = new DocumentCanvasComposition(
            model.Document,
            baselineComposition.Configuration,
            baselineComposition.PropertiesSchemaCatalog,
            toolboxPlacementCatalog: baselineComposition.ToolboxPlacementCatalog,
            anchorConnectionCreationCatalog:
                baselineComposition.AnchorConnectionCreationCatalog,
            documentCreationIdentityProvider:
                baselineComposition.DocumentCreationIdentityProvider,
            endpointReconnectionCatalog: baselineComposition.EndpointReconnectionCatalog,
            deletionCatalog: baselineComposition.DeletionCatalog);
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync(composition);
        var document = harness.Composition.Document;
        var snapshot = document.CaptureSnapshot();
        var routed = harness.State;
        var graph = Assert.IsType<ProjectedGraph>(routed.ProjectedGraph);
        var edge = Assert.Single(graph.Edges, candidate =>
            candidate.Source.SemanticElementId == model.Flows["flow"].SemanticId);
        var routing = Assert.IsType<RoutingResult>(routed.RoutingResult);
        var route = Assert.Single(routing.Routes, candidate =>
            candidate.ProjectedEdgeId == edge.Id);
        var scene = Assert.IsType<Canvas2DScene>(routed.CurrentScene);
        var obstacleBounds = NodeBounds(routed, model.Nodes["obstacle"].SemanticId);
        var preferredInflatedObstacle = Inflate(obstacleBounds, 10d);
        var preferredSourceLead = new PointD(route.SourceAnchor.X + 10d, route.SourceAnchor.Y);

        Assert.Equal(Canvas2D.EditingSession.EditingSessionStatus.Ready, routed.Status);
        Assert.True(routed.IsGraphicalInteractionEnabled);
        Assert.False(routed.IsDisplayingStaleScene);
        Assert.Null(routed.LastKnownGoodScene);
        Assert.Equal(routed.DocumentRevision, scene.SourceRevision);
        Assert.Equal(snapshot.Revision, routed.DocumentRevision);
        Assert.Equal(new PointD(100d, 130d), route.SourceAnchor);
        Assert.Equal(new PointD(300d, 130d), route.DestinationAnchor);
        Assert.Equal(route.SourceAnchor.Y, route.Path[1].Y);
        Assert.True(route.Path[1].X > route.SourceAnchor.X);
        Assert.True(route.Path[1].X < preferredSourceLead.X);
        Assert.True(CrossesStrictInterior(
            route.SourceAnchor,
            preferredSourceLead,
            preferredInflatedObstacle));
        Assert.All(RouteSegments(route.Path), segment => Assert.False(
            CrossesStrictInterior(
                segment.Start,
                segment.End,
                preferredInflatedObstacle)));
        Assert.Empty(routing.NoRouteEdgeIds);
        Assert.DoesNotContain(routed.RuntimeDiagnostics, diagnostic =>
            diagnostic.Code == BpmnAlgorithmDiagnosticCodes.NoLegalRoute);
        Assert.Contains(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == model.Flows["flow"].VisualId);
        Assert.All(RouteSegments(route.Path), segment => Assert.True(
            segment.Start.X.Equals(segment.End.X) ||
            segment.Start.Y.Equals(segment.End.Y)));
        Assert.Equal(snapshot, document.CaptureSnapshot());
        Assert.Equal(0, routed.HistoryStatus.EntryCount);
    }

    [Fact]
    public async Task DemoReviewMoveTransitionsRoutedNoRouteAndBackWithoutRecreatingFlow()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        var document = harness.Composition.Document;
        var before = document.CaptureSnapshot();
        var beforeState = harness.State;
        var beforeHistory = beforeState.HistoryStatus;
        var beforeRelationship = Assert.Single(before.SemanticModel.Relationships, relationship =>
            relationship.Id == BpmnDemoPipeline.SecondSequenceFlowId);
        var beforeConnector = Visual(before, BpmnDemoPipeline.SecondSequenceFlowVisualId);
        var beforeReviewBounds = NodeBounds(beforeState, BpmnDemoPipeline.TaskId);
        var beforeGraph = Assert.IsType<ProjectedGraph>(beforeState.ProjectedGraph);
        var beforeEdge = Assert.Single(beforeGraph.Edges, edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId);
        var beforeRoute = Assert.Single(
            Assert.IsType<RoutingResult>(beforeState.RoutingResult).Routes,
            route => route.ProjectedEdgeId == beforeEdge.Id);

        Assert.Equal(new RectD(122d, 76d, 180d, 92d), beforeReviewBounds);
        Assert.Equal(
            new[] { new PointD(302d, 122d), new PointD(382d, 122d) },
            beforeRoute.Path.AsEnumerable());
        var move = await harness.Session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            beforeState.DocumentRevision,
            BpmnDemoPipeline.TaskVisualId,
            beforeReviewBounds.TopLeft + new VectorD(90d, 45d),
            VisualPlacementMode.Pinned));
        Assert.True(move.IsCommitted);
        await harness.Session.WaitForIdleAsync();

        var blocked = harness.State;
        var blockedSnapshot = document.CaptureSnapshot();
        var blockedGraph = Assert.IsType<ProjectedGraph>(blocked.ProjectedGraph);
        var blockedEdge = Assert.Single(blockedGraph.Edges, edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId);
        var blockedRouting = Assert.IsType<RoutingResult>(blocked.RoutingResult);
        var blockedScene = Assert.IsType<Canvas2DScene>(blocked.CurrentScene);

        Assert.Equal(Canvas2D.EditingSession.EditingSessionStatus.Ready, blocked.Status);
        Assert.True(blocked.IsGraphicalInteractionEnabled);
        Assert.False(blocked.IsDisplayingStaleScene);
        Assert.Null(blocked.LastKnownGoodScene);
        Assert.Equal(blocked.DocumentRevision, blockedScene.SourceRevision);
        Assert.Equal(new RectD(212d, 121d, 180d, 92d),
            NodeBounds(blocked, BpmnDemoPipeline.TaskId));
        Assert.Equal(new RectD(382d, 98d, 48d, 48d),
            NodeBounds(blocked, BpmnDemoPipeline.ExclusiveGatewayId));
        Assert.Equal(beforeEdge.Id, blockedEdge.Id);
        Assert.Equal(blockedGraph.Edges.Length - 1, blockedRouting.Routes.Length);
        Assert.Equal(blockedEdge.Id, Assert.Single(blockedRouting.NoRouteEdgeIds));
        Assert.DoesNotContain(blockedRouting.Routes, route =>
            route.ProjectedEdgeId == blockedEdge.Id);
        var expectedFallbackPath = new[]
        {
            new PointD(392d, 167d),
            new PointD(382d, 122d),
        };
        var blockedFallback = Connector(
            blockedScene,
            BpmnDemoPipeline.SecondSequenceFlowVisualId);
        Assert.Equal(
            expectedFallbackPath,
            Canvas2DConnectorPathMetadata.Resolve(blockedFallback)
                .Select(blockedFallback.Transform.TransformPoint));
        Assert.Contains(blockedScene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                blockedEdge.Id,
                "connector-target-arrow") &&
            item.Origin.VisualStateId == BpmnDemoPipeline.SecondSequenceFlowVisualId);
        Assert.Contains(blockedScene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == BpmnDemoPipeline.FirstSequenceFlowVisualId);
        Assert.All(blockedGraph.Nodes, node => Assert.Contains(blockedScene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.ProjectedObjectId == node.Id &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None));
        Assert.Contains(blocked.RuntimeDiagnostics, diagnostic =>
            diagnostic.Code == BpmnAlgorithmDiagnosticCodes.NoLegalRoute &&
            diagnostic.SourceIdentity == blockedEdge.Id.Value);
        Assert.Equal(before.Revision.Increment(), blockedSnapshot.Revision);
        Assert.Equal(beforeHistory.EntryCount + 1, blocked.HistoryStatus.EntryCount);
        Assert.Equal(beforeRelationship, Assert.Single(
            blockedSnapshot.SemanticModel.Relationships,
            relationship => relationship.Id == beforeRelationship.Id));
        Assert.Equal(beforeConnector,
            Visual(blockedSnapshot, BpmnDemoPipeline.SecondSequenceFlowVisualId));

        var undo = await harness.Session.UndoAsync();
        Assert.True(undo.IsCommitted);
        await harness.Session.WaitForIdleAsync();

        var restored = harness.State;
        var restoredGraph = Assert.IsType<ProjectedGraph>(restored.ProjectedGraph);
        var restoredEdge = Assert.Single(restoredGraph.Edges, edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId);
        var restoredRouting = Assert.IsType<RoutingResult>(restored.RoutingResult);
        Assert.Equal(Canvas2D.EditingSession.EditingSessionStatus.Ready, restored.Status);
        Assert.Empty(restoredRouting.NoRouteEdgeIds);
        Assert.Equal(beforeRoute.Path.AsEnumerable(), Assert.Single(
            restoredRouting.Routes,
            route => route.ProjectedEdgeId == restoredEdge.Id).Path.AsEnumerable());
        Assert.Equal(beforeReviewBounds, NodeBounds(restored, BpmnDemoPipeline.TaskId));
        Assert.Equal(beforeRelationship, Assert.Single(
            document.SemanticModel.Relationships,
            relationship => relationship.Id == beforeRelationship.Id));
        Assert.Equal(beforeConnector,
            Visual(document.CaptureSnapshot(), BpmnDemoPipeline.SecondSequenceFlowVisualId));
        Assert.Equal(beforeHistory.EntryCount + 1, restored.HistoryStatus.EntryCount);
        Assert.True(restored.HistoryStatus.CanRedo);

        var redo = await harness.Session.RedoAsync();
        Assert.True(redo.IsCommitted);
        await harness.Session.WaitForIdleAsync();

        var reblocked = harness.State;
        var reblockedGraph = Assert.IsType<ProjectedGraph>(reblocked.ProjectedGraph);
        var reblockedEdge = Assert.Single(reblockedGraph.Edges, edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId);
        Assert.Equal(Canvas2D.EditingSession.EditingSessionStatus.Ready, reblocked.Status);
        Assert.Equal(reblockedEdge.Id, Assert.Single(
            Assert.IsType<RoutingResult>(reblocked.RoutingResult).NoRouteEdgeIds));
        var reblockedFallback = Connector(
            reblocked.CurrentScene!,
            BpmnDemoPipeline.SecondSequenceFlowVisualId);
        Assert.Equal(
            expectedFallbackPath,
            Canvas2DConnectorPathMetadata.Resolve(reblockedFallback)
                .Select(reblockedFallback.Transform.TransformPoint));
        Assert.Equal(beforeRelationship, Assert.Single(
            document.SemanticModel.Relationships,
            relationship => relationship.Id == beforeRelationship.Id));
        Assert.Equal(beforeConnector,
            Visual(document.CaptureSnapshot(), BpmnDemoPipeline.SecondSequenceFlowVisualId));
        Assert.Equal(beforeHistory.EntryCount + 1, reblocked.HistoryStatus.EntryCount);
        Assert.True(reblocked.HistoryStatus.CanUndo);
        Assert.False(reblocked.HistoryStatus.CanRedo);
    }

    [Fact]
    public async Task DemoReviewResizeTransitionsRoutedNoRouteAndUndoRecoversIdentity()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        var document = harness.Composition.Document;
        var before = document.CaptureSnapshot();
        var beforeState = harness.State;
        var beforeHistory = beforeState.HistoryStatus;
        var beforeRelationship = Assert.Single(before.SemanticModel.Relationships, relationship =>
            relationship.Id == BpmnDemoPipeline.SecondSequenceFlowId);
        var beforeConnector = Visual(before, BpmnDemoPipeline.SecondSequenceFlowVisualId);
        var beforeReviewBounds = NodeBounds(beforeState, BpmnDemoPipeline.TaskId);
        var beforeGraph = Assert.IsType<ProjectedGraph>(beforeState.ProjectedGraph);
        var beforeEdge = Assert.Single(beforeGraph.Edges, edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId);
        var beforeRoute = Assert.Single(
            Assert.IsType<RoutingResult>(beforeState.RoutingResult).Routes,
            route => route.ProjectedEdgeId == beforeEdge.Id);
        var resizedBounds = new RectD(
            beforeReviewBounds.X,
            beforeReviewBounds.Y,
            270d,
            beforeReviewBounds.Height);

        var resize = await harness.Session.ExecuteAsync(new ResizeVisualStateCommand(
            document.DocumentId,
            beforeState.DocumentRevision,
            BpmnDemoPipeline.TaskVisualId,
            resizedBounds,
            VisualPlacementMode.Pinned));
        Assert.True(resize.IsCommitted);
        await harness.Session.WaitForIdleAsync();

        var blocked = harness.State;
        var blockedSnapshot = document.CaptureSnapshot();
        var blockedGraph = Assert.IsType<ProjectedGraph>(blocked.ProjectedGraph);
        var blockedEdge = Assert.Single(blockedGraph.Edges, edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId);
        var blockedRouting = Assert.IsType<RoutingResult>(blocked.RoutingResult);
        var blockedScene = Assert.IsType<Canvas2DScene>(blocked.CurrentScene);
        var gatewayBounds = NodeBounds(blocked, BpmnDemoPipeline.ExclusiveGatewayId);
        var targetAnchor = new PointD(gatewayBounds.Left, gatewayBounds.Top +
            (gatewayBounds.Height / 2d));

        Assert.Equal(Canvas2D.EditingSession.EditingSessionStatus.Ready, blocked.Status);
        Assert.True(blocked.IsGraphicalInteractionEnabled);
        Assert.False(blocked.IsDisplayingStaleScene);
        Assert.Null(blocked.LastKnownGoodScene);
        Assert.Equal(blocked.DocumentRevision, blockedScene.SourceRevision);
        Assert.Equal(resizedBounds, NodeBounds(blocked, BpmnDemoPipeline.TaskId));
        Assert.True(targetAnchor.X > resizedBounds.Left && targetAnchor.X < resizedBounds.Right);
        Assert.True(targetAnchor.Y > resizedBounds.Top && targetAnchor.Y < resizedBounds.Bottom);
        Assert.Equal(beforeEdge.Id, blockedEdge.Id);
        Assert.Equal(blockedEdge.Id, Assert.Single(blockedRouting.NoRouteEdgeIds));
        Assert.DoesNotContain(blockedRouting.Routes, route =>
            route.ProjectedEdgeId == blockedEdge.Id);
        var blockedFallback = Connector(
            blockedScene,
            BpmnDemoPipeline.SecondSequenceFlowVisualId);
        Assert.Equal(
            new[] { new PointD(392d, 122d), targetAnchor },
            Canvas2DConnectorPathMetadata.Resolve(blockedFallback)
                .Select(blockedFallback.Transform.TransformPoint));
        Assert.Contains(blockedScene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                blockedEdge.Id,
                "connector-target-arrow") &&
            item.Origin.VisualStateId == BpmnDemoPipeline.SecondSequenceFlowVisualId);
        Assert.Contains(blocked.RuntimeDiagnostics, diagnostic =>
            diagnostic.Code == BpmnAlgorithmDiagnosticCodes.NoLegalRoute &&
            diagnostic.SourceIdentity == blockedEdge.Id.Value);
        Assert.Equal(before.Revision.Increment(), blockedSnapshot.Revision);
        Assert.Equal(beforeHistory.EntryCount + 1, blocked.HistoryStatus.EntryCount);
        Assert.Equal(beforeRelationship, Assert.Single(
            blockedSnapshot.SemanticModel.Relationships,
            relationship => relationship.Id == beforeRelationship.Id));
        Assert.Equal(beforeConnector,
            Visual(blockedSnapshot, BpmnDemoPipeline.SecondSequenceFlowVisualId));

        var undo = await harness.Session.UndoAsync();
        Assert.True(undo.IsCommitted);
        await harness.Session.WaitForIdleAsync();

        var recovered = harness.State;
        var recoveredSnapshot = document.CaptureSnapshot();
        var recoveredGraph = Assert.IsType<ProjectedGraph>(recovered.ProjectedGraph);
        var recoveredEdge = Assert.Single(recoveredGraph.Edges, edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.SecondSequenceFlowId);
        var recoveredRouting = Assert.IsType<RoutingResult>(recovered.RoutingResult);
        var recoveredScene = Assert.IsType<Canvas2DScene>(recovered.CurrentScene);

        Assert.Equal(Canvas2D.EditingSession.EditingSessionStatus.Ready, recovered.Status);
        Assert.True(recovered.IsGraphicalInteractionEnabled);
        Assert.False(recovered.IsDisplayingStaleScene);
        Assert.Null(recovered.LastKnownGoodScene);
        Assert.Equal(recovered.DocumentRevision, recoveredScene.SourceRevision);
        Assert.Equal(beforeReviewBounds, NodeBounds(recovered, BpmnDemoPipeline.TaskId));
        Assert.Equal(beforeEdge.Id, recoveredEdge.Id);
        Assert.Empty(recoveredRouting.NoRouteEdgeIds);
        Assert.Equal(beforeRoute.Path.AsEnumerable(), Assert.Single(
            recoveredRouting.Routes,
            route => route.ProjectedEdgeId == recoveredEdge.Id).Path.AsEnumerable());
        Assert.Contains(recoveredScene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == BpmnDemoPipeline.SecondSequenceFlowVisualId);
        Assert.Equal(beforeRelationship, Assert.Single(
            recoveredSnapshot.SemanticModel.Relationships,
            relationship => relationship.Id == beforeRelationship.Id));
        Assert.Equal(beforeConnector,
            Visual(recoveredSnapshot, BpmnDemoPipeline.SecondSequenceFlowVisualId));
        Assert.Equal(beforeHistory.EntryCount + 1, recovered.HistoryStatus.EntryCount);
        Assert.False(recovered.HistoryStatus.CanUndo);
        Assert.True(recovered.HistoryStatus.CanRedo);
    }

    [Fact]
    public async Task MovingResizingAndDeletingObstacleReroutesWithoutMovingSurvivors()
    {
        var model = await CreateModelAsync(
            "obstacle-edit",
            [
                Node("source", new RectD(40d, 100d, 120d, 80d)),
                Node("obstacle", new RectD(230d, 300d, 120d, 100d)),
                Node("target", new RectD(440d, 100d, 120d, 80d)),
            ],
            [
                Flow(
                    "flow",
                    "source",
                    ConnectorAnchorSide.Right,
                    "target",
                    ConnectorAnchorSide.Left),
            ]);
        var initial = RunPipeline(model.Document.CaptureSnapshot());
        var initialRoute = Assert.Single(initial.Routing.Routes);
        Assert.Equal(2, initialRoute.Path.Length);

        var obstacle = model.Nodes["obstacle"];
        await ExecuteAsync(model.History, model.Processor, new MoveVisualStateCommand(
            model.Document.DocumentId,
            model.Document.Revision,
            obstacle.VisualId,
            new PointD(230d, 90d),
            VisualPlacementMode.Pinned));
        var moved = RunPipeline(model.Document.CaptureSnapshot());
        var movedRoute = Assert.Single(moved.Routing.Routes);
        Assert.True(movedRoute.Path.Length > 2);
        Assert.Equal(model.Nodes["source"].Bounds,
            NodeBounds(moved, model.Nodes["source"].SemanticId));
        Assert.Equal(model.Nodes["target"].Bounds,
            NodeBounds(moved, model.Nodes["target"].SemanticId));

        await ExecuteAsync(model.History, model.Processor, new ResizeVisualStateCommand(
            model.Document.DocumentId,
            model.Document.Revision,
            obstacle.VisualId,
            new RectD(230d, 70d, 160d, 160d),
            VisualPlacementMode.Pinned));
        var resized = RunPipeline(model.Document.CaptureSnapshot());
        var resizedRoute = Assert.Single(resized.Routing.Routes);
        Assert.NotEqual(movedRoute.Path, resizedRoute.Path);
        AssertObstacleClear(resizedRoute.Path, new RectD(220d, 60d, 180d, 180d));

        await ExecuteAsync(
            model.History,
            model.Processor,
            new DeleteBpmnFlowNodeCommand(
                model.Document.DocumentId,
                model.Document.Revision,
                obstacle.SemanticId,
                obstacle.VisualId));
        var deleted = RunPipeline(model.Document.CaptureSnapshot());
        var deletedRoute = Assert.Single(deleted.Routing.Routes);
        Assert.Equal(2, deletedRoute.Path.Length);
        Assert.Equal(model.Nodes["source"].Bounds,
            NodeBounds(deleted, model.Nodes["source"].SemanticId));
        Assert.Equal(model.Nodes["target"].Bounds,
            NodeBounds(deleted, model.Nodes["target"].SemanticId));
        Assert.False(model.Document.SemanticModel.TryGetElement(obstacle.SemanticId, out _));
    }

    [Fact]
    public async Task MovingObstacleOverAuthoredGuidanceFallsBackWithoutPersistingDerivedRoute()
    {
        var model = await CreateGuidedObstacleModelAsync(
            "guided-obstacle-move",
            new RectD(230d, 300d, 120d, 100d));
        var authoredRoute = AuthoredDetour();
        await UpdateRouteAsync(model, authoredRoute);
        var beforeMove = model.Document.CaptureSnapshot();
        var beforeHistory = model.History.CaptureStatus();
        var initial = RunPipeline(beforeMove);
        var initialRoute = Assert.Single(initial.Routing.Routes);
        AssertWaypointsInOrder(initialRoute.Path, authoredRoute[1..^1]);

        var obstacle = model.Nodes["obstacle"];
        await ExecuteAsync(model.History, model.Processor, new MoveVisualStateCommand(
            model.Document.DocumentId,
            model.Document.Revision,
            obstacle.VisualId,
            new PointD(230d, 210d),
            VisualPlacementMode.Pinned));

        var movedSnapshot = model.Document.CaptureSnapshot();
        var movedHistory = model.History.CaptureStatus();
        var moved = RunPipeline(movedSnapshot);
        var fallback = Assert.Single(moved.Routing.Routes);

        Assert.Equal(beforeHistory.EntryCount + 1, movedHistory.EntryCount);
        AssertSemanticModelEqual(beforeMove, movedSnapshot);
        Assert.Equal(FlowVisual(beforeMove, model), FlowVisual(movedSnapshot, model));
        Assert.Equal(authoredRoute, FlowVisual(movedSnapshot, model).Route.AsEnumerable());
        Assert.Equal(new[] { new PointD(160d, 140d), new PointD(440d, 140d) },
            fallback.Path.AsEnumerable());
        Assert.DoesNotContain(authoredRoute[1..^1], fallback.Path.Contains);
        AssertObstacleClear(fallback.Path, new RectD(220d, 200d, 140d, 120d));
        Assert.Equal(movedSnapshot, model.Document.CaptureSnapshot());
        Assert.Equal(movedHistory, model.History.CaptureStatus());

        var undo = await model.History.UndoAsync(model.Processor);
        Assert.True(undo.IsCommitted);
        var restoredSnapshot = model.Document.CaptureSnapshot();
        var restored = RunPipeline(restoredSnapshot);
        Assert.Equal(authoredRoute, FlowVisual(restoredSnapshot, model).Route.AsEnumerable());
        Assert.Equal(
            initialRoute.Path.AsEnumerable(),
            Assert.Single(restored.Routing.Routes).Path.AsEnumerable());
        Assert.Equal(new PointD(230d, 300d),
            Visual(restoredSnapshot, obstacle.VisualId).Position);
        Assert.Equal(movedHistory.EntryCount, model.History.CaptureStatus().EntryCount);
        Assert.True(model.History.CaptureStatus().CanRedo);

        var redo = await model.History.RedoAsync(model.Processor);
        Assert.True(redo.IsCommitted);
        Assert.Equal(
            fallback.Path.AsEnumerable(),
            Assert.Single(RunPipeline(model.Document.CaptureSnapshot()).Routing.Routes)
                .Path.AsEnumerable());
        Assert.Equal(authoredRoute,
            FlowVisual(model.Document.CaptureSnapshot(), model).Route.AsEnumerable());
    }

    [Fact]
    public async Task ResizingObstacleOverAuthoredGuidanceFallsBackAndUndoRestoresGuidedRoute()
    {
        var model = await CreateGuidedObstacleModelAsync(
            "guided-obstacle-resize",
            new RectD(250d, 300d, 40d, 40d));
        var authoredRoute = AuthoredDetour();
        await UpdateRouteAsync(model, authoredRoute);
        var beforeResize = model.Document.CaptureSnapshot();
        var initial = RunPipeline(beforeResize);
        var initialRoute = Assert.Single(initial.Routing.Routes);
        var beforeHistory = model.History.CaptureStatus();

        var obstacle = model.Nodes["obstacle"];
        await ExecuteAsync(model.History, model.Processor, new ResizeVisualStateCommand(
            model.Document.DocumentId,
            model.Document.Revision,
            obstacle.VisualId,
            new RectD(230d, 210d, 120d, 100d),
            VisualPlacementMode.Pinned));

        var resizedSnapshot = model.Document.CaptureSnapshot();
        var resizedHistory = model.History.CaptureStatus();
        var resized = RunPipeline(resizedSnapshot);
        var fallback = Assert.Single(resized.Routing.Routes);
        Assert.Equal(beforeHistory.EntryCount + 1, resizedHistory.EntryCount);
        AssertSemanticModelEqual(beforeResize, resizedSnapshot);
        Assert.Equal(FlowVisual(beforeResize, model), FlowVisual(resizedSnapshot, model));
        Assert.Equal(authoredRoute, FlowVisual(resizedSnapshot, model).Route.AsEnumerable());
        Assert.Equal(new[] { new PointD(160d, 140d), new PointD(440d, 140d) },
            fallback.Path.AsEnumerable());
        Assert.DoesNotContain(authoredRoute[1..^1], fallback.Path.Contains);
        Assert.Equal(resizedSnapshot, model.Document.CaptureSnapshot());
        Assert.Equal(resizedHistory, model.History.CaptureStatus());

        var undo = await model.History.UndoAsync(model.Processor);
        Assert.True(undo.IsCommitted);
        var restoredSnapshot = model.Document.CaptureSnapshot();
        Assert.Equal(authoredRoute, FlowVisual(restoredSnapshot, model).Route.AsEnumerable());
        Assert.Equal(new RectD(250d, 300d, 40d, 40d),
            Bounds(Visual(restoredSnapshot, obstacle.VisualId)));
        Assert.Equal(
            initialRoute.Path.AsEnumerable(),
            Assert.Single(RunPipeline(restoredSnapshot).Routing.Routes).Path.AsEnumerable());
        Assert.Equal(resizedHistory.EntryCount, model.History.CaptureStatus().EntryCount);
        Assert.True(model.History.CaptureStatus().CanRedo);
    }

    [Fact]
    public async Task RejectedToEndDemoFlowFallsBackAsWholeGuidanceAndRepairIsUndoable()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        var document = harness.Composition.Document;
        var initialSnapshot = document.CaptureSnapshot();
        var initialRoute = Route(harness.State, BpmnDemoPipeline.SixthSequenceFlowId);
        var initialPath = initialRoute.Path;
        Assert.Equal(
            new[]
            {
                new PointD(670d, 194d),
                new PointD(868d, 194d),
                new PointD(868d, 254d),
                new PointD(1712d, 254d),
                new PointD(1712d, 129d),
                new PointD(1782d, 129d),
            },
            initialPath.AsEnumerable());

        var blockedGuidance = new[]
        {
            initialRoute.SourceAnchor,
            new PointD(900d, 194d),
            initialRoute.DestinationAnchor,
        };
        var blockedResult = await harness.Session.ExecuteAsync(new UpdateConnectionRouteCommand(
            document.DocumentId,
            harness.State.DocumentRevision,
            BpmnDemoPipeline.SixthSequenceFlowVisualId,
            blockedGuidance));
        Assert.True(blockedResult.IsCommitted);
        await harness.Session.WaitForIdleAsync();

        var blockedSnapshot = document.CaptureSnapshot();
        AssertSemanticModelEqual(initialSnapshot, blockedSnapshot);
        Assert.Equal(blockedGuidance,
            Visual(blockedSnapshot, BpmnDemoPipeline.SixthSequenceFlowVisualId)
                .Route.AsEnumerable());
        Assert.Equal(initialPath.AsEnumerable(),
            Route(harness.State, BpmnDemoPipeline.SixthSequenceFlowId).Path.AsEnumerable());
        Assert.Equal(1, harness.State.HistoryStatus.EntryCount);
        Assert.True(harness.State.HistoryStatus.CanUndo);
        Assert.Empty(harness.State.RuntimeDiagnostics);
        var fallbackGraph = Assert.IsType<ProjectedGraph>(harness.State.ProjectedGraph);
        var fallbackEdge = Assert.Single(fallbackGraph.Edges, edge =>
            edge.Source.SemanticElementId == BpmnDemoPipeline.SixthSequenceFlowId);
        var fallbackConnector = harness.State.CurrentScene!.Items.Single(item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                fallbackEdge.Id,
                "connector"));
        Assert.Equal(
            initialPath.AsEnumerable(),
            Canvas2DConnectorPathMetadata.Resolve(fallbackConnector)
                .Select(fallbackConnector.Transform.TransformPoint));
        Assert.Equal(
            blockedGuidance,
            Canvas2DConnectorPathMetadata.ResolveEditable(fallbackConnector)
                .Select(fallbackConnector.Transform.TransformPoint));
        Assert.Equal(blockedSnapshot, document.CaptureSnapshot());

        var repairedResult = await harness.Session.ExecuteAsync(new UpdateConnectionRouteCommand(
            document.DocumentId,
            harness.State.DocumentRevision,
            BpmnDemoPipeline.SixthSequenceFlowVisualId,
            initialPath));
        Assert.True(repairedResult.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(initialPath.AsEnumerable(),
            Visual(document.CaptureSnapshot(), BpmnDemoPipeline.SixthSequenceFlowVisualId)
                .Route.AsEnumerable());
        Assert.Equal(initialPath.AsEnumerable(),
            Route(harness.State, BpmnDemoPipeline.SixthSequenceFlowId).Path.AsEnumerable());
        Assert.Equal(2, harness.State.HistoryStatus.EntryCount);

        var undoRepair = await harness.Session.UndoAsync();
        Assert.True(undoRepair.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(blockedGuidance,
            Visual(document.CaptureSnapshot(), BpmnDemoPipeline.SixthSequenceFlowVisualId)
                .Route.AsEnumerable());
        Assert.Equal(initialPath.AsEnumerable(),
            Route(harness.State, BpmnDemoPipeline.SixthSequenceFlowId).Path.AsEnumerable());
        Assert.Equal(2, harness.State.HistoryStatus.EntryCount);
        Assert.True(harness.State.HistoryStatus.CanRedo);

        var undoBlockedGuidance = await harness.Session.UndoAsync();
        Assert.True(undoBlockedGuidance.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Empty(Visual(document.CaptureSnapshot(),
            BpmnDemoPipeline.SixthSequenceFlowVisualId).Route);
        Assert.Equal(initialPath.AsEnumerable(),
            Route(harness.State, BpmnDemoPipeline.SixthSequenceFlowId).Path.AsEnumerable());
        Assert.False(harness.State.HistoryStatus.CanUndo);
        Assert.True(harness.State.HistoryStatus.CanRedo);
        Assert.Empty(harness.State.RuntimeDiagnostics);
    }

    private static async Task<Model> CreateModelAsync(
        string key,
        IReadOnlyList<NodeDefinition> nodes,
        IReadOnlyList<FlowDefinition> flows)
    {
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(
            new DocumentId($"test:n4.2:{key}")).Document);
        var registration = BpmnPluginRegistration.N4;
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies);
        var history = new HistoryManager(document);
        var nodesByKey = nodes.ToDictionary(static node => node.Key);
        var flowsByKey = flows.ToDictionary(static flow => flow.Key);

        var number = 1L;
        foreach (var node in nodes)
        {
            await ExecuteAsync(history, processor, new CreateBpmnTaskCommand(
                document.DocumentId,
                document.Revision,
                node.SemanticId,
                node.VisualId,
                node.Bounds.TopLeft,
                node.Bounds.Size,
                node.Key.ToUpperInvariant(),
                node.Key,
                number++,
                VisualPlacementMode.Pinned));
        }

        foreach (var flow in flows)
        {
            var source = nodesByKey[flow.SourceKey];
            var target = nodesByKey[flow.TargetKey];
            await AddAnchorAsync(
                history,
                processor,
                document,
                source.VisualId,
                flow.SourceAnchorId,
                flow.SourceSide,
                ConnectorAnchorRole.Source);
            await AddAnchorAsync(
                history,
                processor,
                document,
                target.VisualId,
                flow.TargetAnchorId,
                flow.TargetSide,
                ConnectorAnchorRole.Target);
            await ExecuteAsync(history, processor, new CreateBpmnSequenceFlowCommand(
                document.DocumentId,
                document.Revision,
                flow.SemanticId,
                flow.VisualId,
                source.SemanticId,
                target.SemanticId,
                flow.SourceAnchorId,
                flow.TargetAnchorId));
        }

        return new Model(document, processor, history, nodesByKey, flowsByKey);
    }

    private static Task<Model> CreateGuidedObstacleModelAsync(
        string key,
        RectD obstacleBounds) =>
        CreateModelAsync(
            key,
            [
                Node("source", new RectD(40d, 100d, 120d, 80d)),
                Node("obstacle", obstacleBounds),
                Node("target", new RectD(440d, 100d, 120d, 80d)),
            ],
            [
                Flow(
                    "flow",
                    "source",
                    ConnectorAnchorSide.Right,
                    "target",
                    ConnectorAnchorSide.Left),
            ]);

    private static PointD[] AuthoredDetour() =>
    [
        new PointD(160d, 140d),
        new PointD(200d, 140d),
        new PointD(200d, 260d),
        new PointD(280d, 260d),
        new PointD(280d, 180d),
        new PointD(400d, 180d),
        new PointD(400d, 140d),
        new PointD(440d, 140d),
    ];

    private static ValueTask UpdateRouteAsync(Model model, IReadOnlyList<PointD> route) =>
        ExecuteAsync(model.History, model.Processor, new UpdateConnectionRouteCommand(
            model.Document.DocumentId,
            model.Document.Revision,
            model.Flows["flow"].VisualId,
            route));

    private static PipelineArtifacts RunPipeline(DocumentSnapshot snapshot)
    {
        var registration = BpmnPluginRegistration.N4;
        var projection = new ProjectionEngine(registration.ProjectionRules).Project(snapshot);
        Assert.True(projection.IsSuccessful);
        var graph = Assert.IsType<ProjectedGraph>(projection.Graph);
        var layoutExecution = new LayoutEngine(registration.LayoutAlgorithms).Layout(
            graph,
            BpmnAlgorithmIds.DefaultLayout);
        Assert.True(layoutExecution.IsSuccessful);
        var layout = Assert.IsType<LayoutResult>(layoutExecution.Result);
        var routingExecution = new RoutingEngine(registration.RoutingAlgorithms).Route(
            graph,
            layout,
            BpmnAlgorithmIds.DefaultRouting);
        Assert.True(
            routingExecution.IsSuccessful,
            string.Join(Environment.NewLine, routingExecution.Diagnostics.Select(static item =>
                $"{item.Code}: {item.Message}")));
        var routing = Assert.IsType<RoutingResult>(routingExecution.Result);
        var sceneExecution = new Canvas2DSceneBuilder(
            contributors: registration.SceneContributors).Build(
            graph,
            layout,
            routing,
            snapshot.VisualModel,
            new EditorStateSnapshot());
        Assert.True(sceneExecution.Succeeded, string.Join(
            Environment.NewLine,
            sceneExecution.Diagnostics.Select(static item => $"{item.Code}: {item.Message}")));
        return new PipelineArtifacts(
            graph,
            layout,
            routing,
            Assert.IsType<Canvas2DScene>(sceneExecution.Scene));
    }

    private static async ValueTask AddAnchorAsync(
        HistoryManager history,
        CommandProcessor processor,
        Document document,
        VisualStateId visualId,
        ConnectorAnchorId anchorId,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role)
    {
        var visual = Assert.Single(document.VisualModel.VisualStates, item => item.Id == visualId);
        var insertionIndex = visual.ConnectorAnchors.Count(anchor => anchor.Side == side);
        await ExecuteAsync(history, processor, new AddConnectorAnchorCommand(
            document.DocumentId,
            document.Revision,
            visualId,
            anchorId,
            side,
            role,
            insertionIndex));
    }

    private static async ValueTask ExecuteAsync(
        HistoryManager history,
        CommandProcessor processor,
        ICommand command)
    {
        var result = await history.ExecuteAsync(processor, command);
        Assert.True(result.IsCommitted, string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(static item => $"{item.Code}: {item.Message}")));
    }

    private static NodeDefinition Node(string key, RectD bounds) =>
        new(
            key,
            new SemanticElementId($"test:n4.2:node:{key}"),
            new VisualStateId($"test:n4.2:visual:{key}"),
            bounds);

    private static FlowDefinition Flow(
        string key,
        string sourceKey,
        ConnectorAnchorSide sourceSide,
        string targetKey,
        ConnectorAnchorSide targetSide) =>
        new(
            key,
            new SemanticElementId($"test:n4.2:flow:{key}"),
            new VisualStateId($"test:n4.2:flow-visual:{key}"),
            sourceKey,
            new ConnectorAnchorId($"test:n4.2:anchor:{key}:source"),
            sourceSide,
            targetKey,
            new ConnectorAnchorId($"test:n4.2:anchor:{key}:target"),
            targetSide);

    private static RectD NodeBounds(PipelineArtifacts artifacts, SemanticElementId id)
    {
        var projected = Assert.Single(artifacts.Graph.Nodes, node =>
            node.Source.SemanticElementId == id);
        return Assert.Single(artifacts.Layout.Nodes, node =>
            node.ProjectedObjectId == projected.Id).Bounds;
    }

    private static RectD NodeBounds(
        Canvas2D.EditingSession.EditingSessionState state,
        SemanticElementId id)
    {
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var projected = Assert.Single(graph.Nodes, node =>
            node.Source.SemanticElementId == id);
        return Assert.Single(
            Assert.IsType<LayoutResult>(state.LayoutResult).Nodes,
            node => node.ProjectedObjectId == projected.Id).Bounds;
    }

    private static RoutedConnectorGeometry Route(
        PipelineArtifacts artifacts,
        SemanticElementId id)
    {
        var projected = Assert.Single(artifacts.Graph.Edges, edge =>
            edge.Source.SemanticElementId == id);
        return Assert.Single(artifacts.Routing.Routes, route =>
            route.ProjectedEdgeId == projected.Id);
    }

    private static RoutedConnectorGeometry Route(
        Canvas2D.EditingSession.EditingSessionState state,
        SemanticElementId id)
    {
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var projected = Assert.Single(graph.Edges, edge =>
            edge.Source.SemanticElementId == id);
        return Assert.Single(
            Assert.IsType<RoutingResult>(state.RoutingResult).Routes,
            route => route.ProjectedEdgeId == projected.Id);
    }

    private static VisualStateSnapshot FlowVisual(DocumentSnapshot snapshot, Model model) =>
        Visual(snapshot, model.Flows["flow"].VisualId);

    private static VisualStateSnapshot Visual(
        DocumentSnapshot snapshot,
        VisualStateId visualId) =>
        Assert.Single(snapshot.VisualModel.VisualStates, visual => visual.Id == visualId);

    private static RectD Bounds(VisualStateSnapshot visual) =>
        new(visual.Position.X, visual.Position.Y, visual.Size.Width, visual.Size.Height);

    private static void AssertSemanticModelEqual(
        DocumentSnapshot expected,
        DocumentSnapshot actual)
    {
        Assert.Equal(expected.SemanticModel.DocumentId, actual.SemanticModel.DocumentId);
        Assert.Equal(
            expected.SemanticModel.Elements.AsEnumerable(),
            actual.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            expected.SemanticModel.Relationships.AsEnumerable(),
            actual.SemanticModel.Relationships.AsEnumerable());
    }

    private static void AssertWaypointsInOrder(
        IReadOnlyList<PointD> path,
        IReadOnlyList<PointD> waypoints)
    {
        var searchIndex = 0;
        foreach (var waypoint in waypoints)
        {
            while (searchIndex < path.Count && path[searchIndex] != waypoint)
            {
                searchIndex++;
            }

            Assert.True(searchIndex < path.Count, $"Route omits authored waypoint {waypoint}.");
            searchIndex++;
        }
    }

    private static async Task<Canvas2DConnectorRouteContextAction>
        AddRoutePointThroughHostAsync(
            PhaseM31BpmnPropertiesIntegrationTests.HostHarness harness,
            PointD documentPoint)
    {
        await harness.Pointer.ContextMenuDocumentPointAsync(harness.Scene, documentPoint);
        var action = Assert.IsType<Canvas2DConnectorRouteContextAction>(
            harness.Host.CaptureState().ContextMenu?.ConnectorRouteAction);
        Assert.Equal(Canvas2DConnectorRouteContextActionKind.AddPoint, action.Kind);
        var result = await harness.Host.ExecuteConnectorRouteContextActionAsync();
        Assert.True(result?.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        return action;
    }

    private static Canvas2DSceneItem[] RouteBendHandles(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items.Where(item =>
            item.Origin.VisualStateId == visualStateId &&
            item.Metadata.TryGetValue(
                Canvas2DRouteGestureMetadata.HandleRole,
                out var role) &&
            role.Kind == PropertyValueKind.Text &&
            StringComparer.Ordinal.Equals(
                role.TextValue,
                Canvas2DRouteGestureMetadata.BendRole)).ToArray();

    private static void AssertEffectiveRouteObstacleSafe(
        Canvas2D.EditingSession.EditingSessionState state,
        SemanticElementId semanticId,
        double clearance = 10d)
    {
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var layout = Assert.IsType<LayoutResult>(state.LayoutResult);
        var projected = Assert.Single(graph.Edges, edge =>
            edge.Source.SemanticElementId == semanticId);
        var route = Assert.Single(
            Assert.IsType<RoutingResult>(state.RoutingResult).Routes,
            candidate => candidate.ProjectedEdgeId == projected.Id);
        Assert.All(RouteSegments(route.Path), segment => Assert.True(
            segment.Start.X.Equals(segment.End.X) ||
            segment.Start.Y.Equals(segment.End.Y),
            $"Route segment {segment.Start} -> {segment.End} is not orthogonal."));
        foreach (var obstacle in layout.Nodes.Where(node =>
                     node.ProjectedObjectId != projected.SourceNodeId &&
                     node.ProjectedObjectId != projected.TargetNodeId))
        {
            AssertObstacleClear(route.Path, Inflate(obstacle.Bounds, clearance));
        }
    }

    private static Canvas2DPointerInput Pointer(
        long pointerId,
        Canvas2DScene scene,
        PointD documentPoint,
        int buttons = 0) =>
        new(
            pointerId,
            scene.ViewportTransform.TransformPoint(documentPoint),
            isPrimary: true,
            button: 0,
            buttons);

    private static PointD Center(RectD bounds) =>
        new(bounds.Left + (bounds.Width / 2d), bounds.Top + (bounds.Height / 2d));

    private static double DistanceSquared(PointD left, PointD right)
    {
        var delta = left - right;
        return (delta.X * delta.X) + (delta.Y * delta.Y);
    }

    private static Canvas2DSceneItem Connector(Canvas2DScene scene, VisualStateId visualId) =>
        Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == visualId &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None &&
            item.Metadata.ContainsKey(
                Canvas2DConnectorPathMetadata.LogicalPathPointCount) &&
            !item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));

    private static RectD Inflate(RectD bounds, double clearance) =>
        new(
            bounds.X - clearance,
            bounds.Y - clearance,
            bounds.Width + (2d * clearance),
            bounds.Height + (2d * clearance));

    private static IEnumerable<(PointD Start, PointD End)> RouteSegments(
        IReadOnlyList<PointD> path)
    {
        for (var index = 0; index < path.Count - 1; index++)
        {
            yield return (path[index], path[index + 1]);
        }
    }

    private static bool CrossesStrictInterior(PointD start, PointD end, RectD obstacle)
    {
        if (start.X.Equals(end.X))
        {
            return start.X > obstacle.Left && start.X < obstacle.Right &&
                Math.Max(Math.Min(start.Y, end.Y), obstacle.Top) <
                Math.Min(Math.Max(start.Y, end.Y), obstacle.Bottom);
        }

        return start.Y > obstacle.Top && start.Y < obstacle.Bottom &&
            Math.Max(Math.Min(start.X, end.X), obstacle.Left) <
            Math.Min(Math.Max(start.X, end.X), obstacle.Right);
    }

    private static void AssertObstacleClear(IReadOnlyList<PointD> path, RectD obstacle)
    {
        Assert.All(RouteSegments(path), segment =>
            Assert.False(CrossesStrictInterior(segment.Start, segment.End, obstacle)));
    }

    private sealed record NodeDefinition(
        string Key,
        SemanticElementId SemanticId,
        VisualStateId VisualId,
        RectD Bounds);

    private sealed record FlowDefinition(
        string Key,
        SemanticElementId SemanticId,
        VisualStateId VisualId,
        string SourceKey,
        ConnectorAnchorId SourceAnchorId,
        ConnectorAnchorSide SourceSide,
        string TargetKey,
        ConnectorAnchorId TargetAnchorId,
        ConnectorAnchorSide TargetSide);

    private sealed record Model(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History,
        IReadOnlyDictionary<string, NodeDefinition> Nodes,
        IReadOnlyDictionary<string, FlowDefinition> Flows);

    private sealed record PipelineArtifacts(
        ProjectedGraph Graph,
        LayoutResult Layout,
        RoutingResult Routing,
        Canvas2DScene Scene);
}
