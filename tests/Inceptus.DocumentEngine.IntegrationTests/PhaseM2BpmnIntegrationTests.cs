using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Layout;
using Inceptus.DocumentEngine.Runtime.Projection;
using Inceptus.DocumentEngine.Runtime.Routing;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseM2BpmnIntegrationTests
{
    private static readonly DocumentId DocumentId = new("bpmn:m2-document");
    private static readonly SemanticElementId StartId = new("bpmn:m2:start");
    private static readonly SemanticElementId TaskId = new("bpmn:m2:task");
    private static readonly SemanticElementId EndId = new("bpmn:m2:end");
    private static readonly SemanticElementId FirstFlowId = new("bpmn:m2:flow:start-task");
    private static readonly SemanticElementId SecondFlowId = new("bpmn:m2:flow:task-end");
    private static readonly VisualStateId StartVisualId = new("bpmn:m2:visual:start");
    private static readonly VisualStateId TaskVisualId = new("bpmn:m2:visual:task");
    private static readonly VisualStateId EndVisualId = new("bpmn:m2:visual:end");
    private static readonly VisualStateId FirstFlowVisualId = new("bpmn:m2:visual:flow-1");
    private static readonly VisualStateId SecondFlowVisualId = new("bpmn:m2:visual:flow-2");
    private static readonly ConnectorAnchorId StartSourceAnchorId =
        new("bpmn:m2:anchor:start-source");
    private static readonly ConnectorAnchorId TaskTargetAnchorId =
        new("bpmn:m2:anchor:task-target");
    private static readonly ConnectorAnchorId TaskSourceAnchorId =
        new("bpmn:m2:anchor:task-source");
    private static readonly ConnectorAnchorId EndTargetAnchorId =
        new("bpmn:m2:anchor:end-target");

    [Fact]
    public async Task CompleteM1SliceProjectsLayoutsAndRoutesDeterministicallyThroughM2()
    {
        var harness = await CreateModelAsync();
        var authoritativeBefore = harness.Document.CaptureSnapshot();

        var first = RunPipeline(authoritativeBefore);
        var second = RunPipeline(authoritativeBefore);

        Assert.Equal(3, authoritativeBefore.SemanticModel.ElementCount);
        Assert.Equal(2, authoritativeBefore.SemanticModel.RelationshipCount);
        Assert.True(authoritativeBefore.SemanticModel.TryGetElement(TaskId, out var task));
        Assert.Equal("REVIEW_ORDER", task!.Properties[BpmnSemanticProperties.Code].TextValue);
        Assert.Equal("Review order", task.Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(
            20L,
            task.Properties[BpmnSemanticProperties.ElementNumber].IntegerValue);
        Assert.Equal(3, first.Graph.NodeCount);
        Assert.Equal(2, first.Graph.EdgeCount);
        Assert.Equal("Review order", Assert.Single(first.Graph.Labels).Text);
        Assert.Equal(3, first.Layout.NodeCount);
        Assert.Equal(2, first.Routing.RouteCount);

        var layoutBySemanticId = first.Layout.Nodes.ToDictionary(geometry =>
            first.Graph.Nodes.Single(node => node.Id == geometry.ProjectedObjectId)
                .Source.SemanticElementId);
        Assert.True(layoutBySemanticId[StartId].Bounds.X < layoutBySemanticId[TaskId].Bounds.X);
        Assert.True(layoutBySemanticId[TaskId].Bounds.X < layoutBySemanticId[EndId].Bounds.X);

        foreach (var route in first.Routing.Routes)
        {
            var edge = first.Graph.Edges.Single(candidate => candidate.Id == route.ProjectedEdgeId);
            Assert.Equal(
                layoutBySemanticId[edge.Source.SemanticElementId == FirstFlowId ? StartId : TaskId]
                    .Bounds.Right,
                route.SourceAnchor.X);
            Assert.Equal(
                layoutBySemanticId[edge.Source.SemanticElementId == FirstFlowId ? TaskId : EndId]
                    .Bounds.Left,
                route.DestinationAnchor.X);
        }

        Assert.Equal(first.Graph, second.Graph);
        Assert.Equal(first.Layout, second.Layout);
        Assert.Equal(first.Routing, second.Routing);
        Assert.Equal(authoritativeBefore, harness.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task GenericPinnedMoveAndResizeRemainAuthoritativeAndRerouteWithoutPersistence()
    {
        var harness = await CreateModelAsync();
        var automatic = RunPipeline(harness.Document.CaptureSnapshot());
        var automaticFirstRoute = Route(automatic, FirstFlowId);

        var move = await harness.History.ExecuteAsync(
            harness.Processor,
            new MoveVisualStateCommand(
                harness.Document.DocumentId,
                harness.Document.Revision,
                TaskVisualId,
                new PointD(500d, 200d),
                VisualPlacementMode.Pinned));
        Assert.True(move.IsCommitted);
        var movedSnapshot = harness.Document.CaptureSnapshot();
        var moved = RunPipeline(movedSnapshot);
        var movedTask = Geometry(moved, TaskId);
        Assert.Equal(new RectD(500d, 200d, 120d, 80d), movedTask.Bounds);
        Assert.NotEqual(automaticFirstRoute, Route(moved, FirstFlowId));
        Assert.Equal(movedSnapshot, harness.Document.CaptureSnapshot());

        var resize = await harness.History.ExecuteAsync(
            harness.Processor,
            new ResizeVisualStateCommand(
                harness.Document.DocumentId,
                harness.Document.Revision,
                TaskVisualId,
                new RectD(500d, 200d, 180d, 100d),
                VisualPlacementMode.Pinned));
        Assert.True(resize.IsCommitted);
        var resizedSnapshot = harness.Document.CaptureSnapshot();
        var resized = RunPipeline(resizedSnapshot);
        Assert.Equal(new RectD(500d, 200d, 180d, 100d), Geometry(resized, TaskId).Bounds);
        Assert.NotEqual(Route(moved, SecondFlowId), Route(resized, SecondFlowId));
        Assert.Equal(
            movedSnapshot.SemanticModel.Elements.AsEnumerable(),
            resizedSnapshot.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            movedSnapshot.SemanticModel.Relationships.AsEnumerable(),
            resizedSnapshot.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(resizedSnapshot, harness.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task GenericPersistentRouteHistoryControlsBpmnManualBends()
    {
        var harness = await CreateModelAsync();
        var automatic = RunPipeline(harness.Document.CaptureSnapshot());
        var automaticRoute = Route(automatic, FirstFlowId);
        Assert.Empty(automaticRoute.BendPoints);
        var persistentBend = new PointD(70d, 140d);
        var persistentPath = new[]
        {
            automaticRoute.SourceAnchor,
            persistentBend,
            automaticRoute.DestinationAnchor,
        };

        var update = await harness.History.ExecuteAsync(
            harness.Processor,
            new UpdateConnectionRouteCommand(
                harness.Document.DocumentId,
                harness.Document.Revision,
                FirstFlowVisualId,
                persistentPath));
        Assert.True(update.IsCommitted);
        var manual = RunPipeline(harness.Document.CaptureSnapshot());
        Assert.Contains(persistentBend, Route(manual, FirstFlowId).Path);

        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        var undone = RunPipeline(harness.Document.CaptureSnapshot());
        Assert.Empty(Route(undone, FirstFlowId).BendPoints);

        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        var redone = RunPipeline(harness.Document.CaptureSnapshot());
        Assert.Contains(persistentBend, Route(redone, FirstFlowId).Path);
        Assert.Equal(persistentPath, harness.Document.VisualModel.VisualStates
            .Single(visual => visual.Id == FirstFlowVisualId).Route);
    }

    [Fact]
    public async Task GenericDynamicAnchorReferenceProjectsAsPortAndControlsRouteEndpoint()
    {
        var harness = await CreateModelAsync();
        var snapshot = harness.Document.CaptureSnapshot();
        var anchorId = new ConnectorAnchorId("bpmn:m2:anchor:start-top");
        var anchored = WithSourceAnchor(snapshot, anchorId);

        var artifacts = RunPipeline(anchored);
        var startNode = artifacts.Graph.Nodes.Single(node =>
            node.Source.SemanticElementId == StartId);
        var flow = artifacts.Graph.Edges.Single(edge =>
            edge.Source.SemanticElementId == FirstFlowId);
        Assert.Equal(4, artifacts.Graph.Ports.Length);
        var port = Assert.Single(artifacts.Graph.Ports, candidate =>
            ProjectedConnectorAnchorMetadata.TryDecode(candidate, out var metadata) &&
            metadata?.Id == anchorId);
        Assert.Equal(startNode.Id, port.OwnerNodeId);
        Assert.Equal(port.Id, flow.SourcePortId);
        Assert.NotNull(flow.TargetPortId);
        Assert.True(ProjectedConnectorAnchorMetadata.TryDecode(port, out var projectedAnchor));
        Assert.Equal(anchorId, projectedAnchor!.Id);

        var startBounds = Geometry(artifacts, StartId).Bounds;
        var expected = ConnectorAnchorGeometryResolver.ResolvePoint(
            startBounds,
            ConnectorAnchorSide.Top,
            0,
            1);
        var route = Route(artifacts, FirstFlowId);
        Assert.Equal(expected, route.SourceAnchor);
        Assert.Equal(port.Id, route.SourcePortId);
        Assert.Equal(snapshot, harness.Document.CaptureSnapshot());
    }

    private static async Task<Harness> CreateModelAsync()
    {
        var document = Assert.IsType<Document>(DocumentFactory.CreateEmpty(DocumentId).Document);
        var registration = BpmnPluginRegistration.M2;
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies);
        var history = new HistoryManager(document);

        Assert.True((await history.ExecuteAsync(processor, new CreateBpmnStartEventCommand(
            document.DocumentId,
            document.Revision,
            StartId,
            StartVisualId,
            new PointD(700d, 400d),
            new SizeD(36d, 36d),
            VisualPlacementMode.Automatic))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, new CreateBpmnTaskCommand(
            document.DocumentId,
            document.Revision,
            TaskId,
            TaskVisualId,
            new PointD(20d, 300d),
            new SizeD(120d, 80d),
            "REVIEW_ORDER",
            "Review order",
            20L,
            VisualPlacementMode.Automatic))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, new CreateBpmnEndEventCommand(
            document.DocumentId,
            document.Revision,
            EndId,
            EndVisualId,
            new PointD(10d, 10d),
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
            document.DocumentId,
            document.Revision,
            FirstFlowId,
            FirstFlowVisualId,
            StartId,
            TaskId,
            StartSourceAnchorId,
            TaskTargetAnchorId))).IsCommitted);
        Assert.True((await history.ExecuteAsync(processor, new CreateBpmnSequenceFlowCommand(
            document.DocumentId,
            document.Revision,
            SecondFlowId,
            SecondFlowVisualId,
            TaskId,
            EndId,
            TaskSourceAnchorId,
            EndTargetAnchorId))).IsCommitted);

        return new Harness(document, processor, history);
    }

    private static PipelineArtifacts RunPipeline(DocumentSnapshot snapshot)
    {
        var registration = BpmnPluginRegistration.M2;
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
        Assert.True(routingExecution.IsSuccessful);
        return new PipelineArtifacts(
            graph,
            layout,
            Assert.IsType<RoutingResult>(routingExecution.Result));
    }

    private static LayoutNodeGeometry Geometry(
        PipelineArtifacts artifacts,
        SemanticElementId semanticElementId)
    {
        var nodeId = artifacts.Graph.Nodes.Single(node =>
            node.Source.SemanticElementId == semanticElementId).Id;
        return artifacts.Layout.Nodes.Single(geometry => geometry.ProjectedObjectId == nodeId);
    }

    private static RoutedConnectorGeometry Route(
        PipelineArtifacts artifacts,
        SemanticElementId relationshipId)
    {
        var edgeId = artifacts.Graph.Edges.Single(edge =>
            edge.Source.SemanticElementId == relationshipId).Id;
        return artifacts.Routing.Routes.Single(route => route.ProjectedEdgeId == edgeId);
    }

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
            new VisualModelSnapshot(
                snapshot.DocumentId,
                snapshot.Revision,
                visuals),
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

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History);

    private sealed record PipelineArtifacts(
        ProjectedGraph Graph,
        LayoutResult Layout,
        RoutingResult Routing);
}
