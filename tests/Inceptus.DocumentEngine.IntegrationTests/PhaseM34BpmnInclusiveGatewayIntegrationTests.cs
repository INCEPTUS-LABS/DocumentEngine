using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;
using HostHarness = Inceptus.DocumentEngine.IntegrationTests.PhaseM31BpmnPropertiesIntegrationTests.HostHarness;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseM34BpmnInclusiveGatewayIntegrationTests
{
    private static readonly SemanticElementId[] SplitFlowIds =
    [
        BpmnDemoPipeline.EleventhSequenceFlowId,
        BpmnDemoPipeline.TwelfthSequenceFlowId,
        BpmnDemoPipeline.ThirteenthSequenceFlowId,
    ];

    private static readonly SemanticElementId[] JoinFlowIds =
    [
        BpmnDemoPipeline.FourteenthSequenceFlowId,
        BpmnDemoPipeline.FifteenthSequenceFlowId,
        BpmnDemoPipeline.SixteenthSequenceFlowId,
    ];

    [Fact]
    public async Task DemoCompletesInclusiveSplitAndJoinThroughTheWholePipeline()
    {
        await using var harness = await HostHarness.CreateAsync();
        var document = harness.Composition.Document.CaptureSnapshot();
        var graph = Assert.IsType<ProjectedGraph>(harness.State.ProjectedGraph);
        var layout = Assert.IsType<LayoutResult>(harness.State.LayoutResult);
        var routing = Assert.IsType<RoutingResult>(harness.State.RoutingResult);

        Assert.Equal(26, document.SemanticModel.ElementCount);
        Assert.Equal(25, document.SemanticModel.RelationshipCount);
        Assert.Equal(21, graph.NodeCount);
        Assert.Equal(21, graph.EdgeCount);
        Assert.Equal(21, layout.NodeCount);
        Assert.Equal(21, routing.RouteCount);
        Assert.Empty(harness.State.RuntimeDiagnostics);

        var semanticGateways = document.SemanticModel.Elements.Where(element =>
            element.TypeId == BpmnSemanticTypes.InclusiveGateway).ToArray();
        Assert.Equal(2, semanticGateways.Length);
        Assert.Equal(
            [
                BpmnDemoPipeline.InclusiveJoinGatewayId,
                BpmnDemoPipeline.InclusiveSplitGatewayId,
            ],
            semanticGateways.Select(static gateway => gateway.Id).ToArray());
        Assert.All(semanticGateways, gateway =>
        {
            Assert.True(gateway.Properties.ContainsKey(BpmnSemanticProperties.Code));
            Assert.True(gateway.Properties.ContainsKey(BpmnSemanticProperties.Name));
            Assert.True(gateway.Properties.ContainsKey(BpmnSemanticProperties.Description));
            Assert.False(gateway.Properties.ContainsKey(BpmnSemanticProperties.ElementNumber));
        });

        var splitNode = InclusiveNode(graph, BpmnDemoPipeline.InclusiveSplitGatewayId);
        var joinNode = InclusiveNode(graph, BpmnDemoPipeline.InclusiveJoinGatewayId);
        Assert.Equal(1, graph.Edges.Count(edge => edge.TargetNodeId == splitNode.Id));
        Assert.Equal(2, graph.Edges.Count(edge => edge.SourceNodeId == splitNode.Id));
        Assert.Equal(2, graph.Edges.Count(edge => edge.TargetNodeId == joinNode.Id));
        Assert.Equal(1, graph.Edges.Count(edge => edge.SourceNodeId == joinNode.Id));

        AssertInclusiveGatewayScene(
            harness,
            splitNode,
            BpmnDemoPipeline.InclusiveSplitGatewayVisualId,
            "Optional services");
        AssertInclusiveGatewayScene(
            harness,
            joinNode,
            BpmnDemoPipeline.InclusiveJoinGatewayVisualId,
            "Continue after selected services");
        Assert.Equal(2, harness.Scene.Items.Count(item =>
            item.Origin.StableSourceKey?.Contains(
                "inclusive-gateway-o",
                StringComparison.Ordinal) == true));
        Assert.Equal(1, harness.Scene.Items.Count(item =>
            item.Origin.StableSourceKey?.Contains(
                "exclusive-gateway-x",
                StringComparison.Ordinal) == true));
        Assert.Equal(2, harness.Scene.Items.Count(item =>
            item.Origin.StableSourceKey?.Contains(
                "parallel-gateway-plus",
                StringComparison.Ordinal) == true));

        AssertAnchorSet(
            Visual(harness, BpmnDemoPipeline.InclusiveSplitGatewayVisualId),
            leftTargets: 1,
            rightSources: 2);
        AssertAnchorSet(
            Visual(harness, BpmnDemoPipeline.InclusiveJoinGatewayVisualId),
            leftTargets: 2,
            rightSources: 1);
        Assert.Equal(3, graph.Ports.Count(port => port.OwnerNodeId == splitNode.Id));
        Assert.Equal(3, graph.Ports.Count(port => port.OwnerNodeId == joinNode.Id));
        Assert.Equal(2, graph.Ports.Count(port =>
            port.OwnerNodeId == joinNode.Id &&
            PortAllows(port, ConnectorAnchorRole.Target)));

        foreach (var flowId in SplitFlowIds.Concat(JoinFlowIds))
        {
            AssertRouteUsesExplicitAnchors(harness, flowId);
            Assert.All(Route(harness.State, flowId).Path, static point =>
            {
                Assert.True(double.IsFinite(point.X));
                Assert.True(double.IsFinite(point.Y));
            });
        }

        Assert.Equal(2, new[]
        {
            Route(harness.State, BpmnDemoPipeline.TwelfthSequenceFlowId).SourcePortId,
            Route(harness.State, BpmnDemoPipeline.ThirteenthSequenceFlowId).SourcePortId,
        }.Distinct().Count());
        Assert.Equal(2, new[]
        {
            Route(harness.State, BpmnDemoPipeline.FourteenthSequenceFlowId).TargetPortId,
            Route(harness.State, BpmnDemoPipeline.FifteenthSequenceFlowId).TargetPortId,
        }.Distinct().Count());
        Assert.False(Route(harness.State, BpmnDemoPipeline.TwelfthSequenceFlowId).Path
            .AsSpan().SequenceEqual(
                Route(harness.State, BpmnDemoPipeline.ThirteenthSequenceFlowId).Path.AsSpan()));
        Assert.False(Route(harness.State, BpmnDemoPipeline.FourteenthSequenceFlowId).Path
            .AsSpan().SequenceEqual(
                Route(harness.State, BpmnDemoPipeline.FifteenthSequenceFlowId).Path.AsSpan()));

        var insuranceBounds = Geometry(
            harness.State,
            BpmnDemoPipeline.AddInsuranceTaskId).Bounds;
        var giftWrapBounds = Geometry(
            harness.State,
            BpmnDemoPipeline.AddGiftWrapTaskId).Bounds;
        Assert.Equal(insuranceBounds.Left, giftWrapBounds.Left);
        Assert.False(insuranceBounds.Intersects(giftWrapBounds));
        Assert.True(
            Geometry(harness.State, BpmnDemoPipeline.InclusiveSplitGatewayId).Bounds.Right <
            insuranceBounds.Left);
        Assert.True(
            insuranceBounds.Right <
            Geometry(harness.State, BpmnDemoPipeline.InclusiveJoinGatewayId).Bounds.Left);

        await using var interaction = new Canvas2DInteractionController(harness.Session);
        var marker = InclusiveMarker(harness.Scene, BpmnDemoPipeline.InclusiveSplitGatewayId);
        var markerHit = Assert.IsType<Canvas2DSceneHitTestResult>(
            new Canvas2DSceneHitTestService().HitTest(harness.Scene, Center(marker.Bounds)));
        var body = NodeBody(harness.Scene, BpmnDemoPipeline.InclusiveSplitGatewayVisualId);
        Assert.Equal(body.Id, markerHit.SceneObjectId);

        var label = LabelInteractionBody(
            harness.Scene,
            BpmnDemoPipeline.InclusiveSplitGatewayId);
        var selected = await interaction.PointerReleasedAsync(Pointer(
            3400,
            harness.Scene,
            Center(label.Bounds),
            button: 0));
        Assert.Equal(Canvas2DInteractionStatus.Updated, selected.Status);
        Assert.Equal(
            BpmnDemoPipeline.InclusiveSplitGatewayVisualId,
            Assert.Single(selected.SessionState.EditorState.Selection));
    }

    [Fact]
    public async Task PropertiesEditCodeNameDescriptionAndHistoryWithoutElementNumber()
    {
        await using var harness = await HostHarness.CreateAsync();

        await EditPropertyRoundTripAsync(
            harness,
            "code",
            BpmnSemanticProperties.Code,
            "OPTIONAL_SERVICES",
            "OPTIONAL_SERVICES_UPDATED");
        await EditPropertyRoundTripAsync(
            harness,
            "name",
            BpmnSemanticProperties.Name,
            "Optional services",
            "Choose optional services");
        await EditPropertyRoundTripAsync(
            harness,
            "description",
            BpmnSemanticProperties.Description,
            "Select one or more optional process branches.",
            "Select one or more optional service branches.\nConditions remain deferred.");

        var properties = await harness.OpenNodePropertiesAsync(
            BpmnDemoPipeline.InclusiveSplitGatewayVisualId);
        Assert.Equal(BpmnDemoPipeline.InclusiveSplitGatewayId, properties.SemanticId);
        Assert.Equal(BpmnDemoPipeline.InclusiveSplitGatewayVisualId, properties.VisualStateId);
        Assert.Equal(BpmnSemanticTypes.InclusiveGateway, properties.TypeId);
        Assert.Collection(
            properties.DataFields,
            field => AssertField(
                field,
                "code",
                BpmnSemanticProperties.Code,
                "OPTIONAL_SERVICES_UPDATED"),
            field => AssertField(
                field,
                "name",
                BpmnSemanticProperties.Name,
                "Choose optional services"),
            field => AssertField(
                field,
                "description",
                BpmnSemanticProperties.Description,
                "Select one or more optional service branches.\nConditions remain deferred."));
        Assert.DoesNotContain(properties.DataFields, field =>
            StringComparer.Ordinal.Equals(
                field.Definition.SemanticPropertyKey,
                BpmnSemanticProperties.ElementNumber));
        Assert.Equal(
            "Choose optional services",
            string.Join(' ', GatewayTextLines(
                    harness.Scene,
                    BpmnDemoPipeline.InclusiveSplitGatewayId)
                .Select(static item => item.Geometry.Content)));
    }

    [Fact]
    public async Task ThreeWayBranchAddedThroughCommandsUsesIndependentQuarterAnchors()
    {
        var taskId = new SemanticElementId("test:bpmn:m34:optional-service-c");
        var taskVisualId = new VisualStateId("test:bpmn:m34:visual:optional-service-c");
        var splitSourceId = new ConnectorAnchorId("test:bpmn:m34:split:right:source-c");
        var taskTargetId = new ConnectorAnchorId("test:bpmn:m34:task-c:left:target");
        var taskSourceId = new ConnectorAnchorId("test:bpmn:m34:task-c:right:source");
        var joinTargetId = new ConnectorAnchorId("test:bpmn:m34:join:left:target-c");
        var outgoingId = new SemanticElementId("test:bpmn:m34:flow:split-to-c");
        var outgoingVisualId = new VisualStateId("test:bpmn:m34:visual:flow:split-to-c");
        var incomingId = new SemanticElementId("test:bpmn:m34:flow:c-to-join");
        var incomingVisualId = new VisualStateId("test:bpmn:m34:visual:flow:c-to-join");
        await using var harness = await HostHarness.CreateAsync();
        var historyBefore = harness.State.HistoryStatus.EntryCount;
        var lowerExistingTaskBounds = Geometry(
            harness.State,
            BpmnDemoPipeline.AddInsuranceTaskId).Bounds;

        await ExecuteAsync(harness, new CreateBpmnTaskCommand(
            harness.Composition.Document.DocumentId,
            harness.State.DocumentRevision,
            taskId,
            taskVisualId,
            new PointD(
                lowerExistingTaskBounds.X,
                lowerExistingTaskBounds.Bottom + 60d),
            new SizeD(180d, 92d),
            "OPTIONAL_SERVICE_C",
            "Optional service C",
            90,
            VisualPlacementMode.Pinned));
        await ExecuteAsync(harness, new AddConnectorAnchorCommand(
            harness.Composition.Document.DocumentId,
            harness.State.DocumentRevision,
            BpmnDemoPipeline.InclusiveSplitGatewayVisualId,
            splitSourceId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            2));
        await ExecuteAsync(harness, new AddConnectorAnchorCommand(
            harness.Composition.Document.DocumentId,
            harness.State.DocumentRevision,
            taskVisualId,
            taskTargetId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            0));
        await ExecuteAsync(harness, new AddConnectorAnchorCommand(
            harness.Composition.Document.DocumentId,
            harness.State.DocumentRevision,
            taskVisualId,
            taskSourceId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            0));
        await ExecuteAsync(harness, new AddConnectorAnchorCommand(
            harness.Composition.Document.DocumentId,
            harness.State.DocumentRevision,
            BpmnDemoPipeline.InclusiveJoinGatewayVisualId,
            joinTargetId,
            ConnectorAnchorSide.Left,
            ConnectorAnchorRole.Target,
            2));
        await ExecuteAsync(harness, new CreateBpmnSequenceFlowCommand(
            harness.Composition.Document.DocumentId,
            harness.State.DocumentRevision,
            outgoingId,
            outgoingVisualId,
            BpmnDemoPipeline.InclusiveSplitGatewayId,
            taskId,
            splitSourceId,
            taskTargetId));
        await ExecuteAsync(harness, new CreateBpmnSequenceFlowCommand(
            harness.Composition.Document.DocumentId,
            harness.State.DocumentRevision,
            incomingId,
            incomingVisualId,
            taskId,
            BpmnDemoPipeline.InclusiveJoinGatewayId,
            taskSourceId,
            joinTargetId));

        Assert.Equal(historyBefore + 7, harness.State.HistoryStatus.EntryCount);
        Assert.Empty(harness.State.RuntimeDiagnostics);
        AssertAnchorSet(
            Visual(harness, BpmnDemoPipeline.InclusiveSplitGatewayVisualId),
            leftTargets: 1,
            rightSources: 3);
        AssertAnchorSet(
            Visual(harness, BpmnDemoPipeline.InclusiveJoinGatewayVisualId),
            leftTargets: 3,
            rightSources: 1);

        var splitBounds = Geometry(
            harness.State,
            BpmnDemoPipeline.InclusiveSplitGatewayId).Bounds;
        var joinBounds = Geometry(
            harness.State,
            BpmnDemoPipeline.InclusiveJoinGatewayId).Bounds;
        var outgoingFlows = new[]
        {
            BpmnDemoPipeline.TwelfthSequenceFlowId,
            BpmnDemoPipeline.ThirteenthSequenceFlowId,
            outgoingId,
        };
        var incomingFlows = new[]
        {
            BpmnDemoPipeline.FourteenthSequenceFlowId,
            BpmnDemoPipeline.FifteenthSequenceFlowId,
            incomingId,
        };
        for (var index = 0; index < 3; index++)
        {
            var outgoing = Route(harness.State, outgoingFlows[index]);
            var incoming = Route(harness.State, incomingFlows[index]);
            Assert.Equal(
                splitBounds.Top + ((index + 1d) * splitBounds.Height / 4d),
                outgoing.SourceAnchor.Y,
                precision: 10);
            Assert.Equal(splitBounds.Right, outgoing.SourceAnchor.X, precision: 10);
            Assert.Equal(
                joinBounds.Top + ((index + 1d) * joinBounds.Height / 4d),
                incoming.DestinationAnchor.Y,
                precision: 10);
            Assert.Equal(joinBounds.Left, incoming.DestinationAnchor.X, precision: 10);
            AssertRouteUsesExplicitAnchors(harness, outgoingFlows[index]);
            AssertRouteUsesExplicitAnchors(harness, incomingFlows[index]);
        }

        Assert.Equal(3, outgoingFlows.Select(flowId =>
            Route(harness.State, flowId).SourcePortId).Distinct().Count());
        Assert.Equal(3, incomingFlows.Select(flowId =>
            Route(harness.State, flowId).TargetPortId).Distinct().Count());
        Assert.All(outgoingFlows.Concat(incomingFlows), flowId =>
            Assert.All(Route(harness.State, flowId).Path, static point =>
            {
                Assert.True(double.IsFinite(point.X));
                Assert.True(double.IsFinite(point.Y));
            }));
    }

    [Fact]
    public async Task GenericMoveAndResizePinGatewayAndRerouteExplicitEndpoints()
    {
        await using var harness = await HostHarness.CreateAsync();
        var semanticId = BpmnDemoPipeline.InclusiveSplitGatewayId;
        var visualId = BpmnDemoPipeline.InclusiveSplitGatewayVisualId;
        var before = harness.State;
        var beforeSnapshot = harness.Composition.Document.CaptureSnapshot();
        var beforeBounds = Geometry(before, semanticId).Bounds;
        var beforeLabel = LabelInteractionBody(harness.Scene, semanticId).Bounds;
        var beforeRoutes = CaptureRoutes(before, SplitFlowIds);
        var anchors = Visual(harness, visualId).ConnectorAnchors;
        var targetPosition = new PointD(1330d, 260d);
        var delta = targetPosition - beforeBounds.TopLeft;

        var moved = await harness.Session.ExecuteAsync(new MoveVisualStateCommand(
            beforeSnapshot.DocumentId,
            before.DocumentRevision,
            visualId,
            targetPosition,
            VisualPlacementMode.Pinned));
        Assert.True(moved.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        var movedBounds = Geometry(harness.State, semanticId).Bounds;
        var movedRoutes = CaptureRoutes(harness.State, SplitFlowIds);
        Assert.Equal(beforeBounds.Translate(delta), movedBounds);
        Assert.Equal(beforeLabel.Translate(delta),
            LabelInteractionBody(harness.Scene, semanticId).Bounds);
        Assert.Equal(VisualPlacementMode.Pinned, Visual(harness, visualId).PlacementMode);
        Assert.Equal(anchors.AsEnumerable(),
            Visual(harness, visualId).ConnectorAnchors.AsEnumerable());
        AssertRoutesChanged(beforeRoutes, movedRoutes);

        var resizedBounds = new RectD(
            movedBounds.X,
            movedBounds.Y,
            movedBounds.Width + 28d,
            movedBounds.Height + 22d);
        var resized = await harness.Session.ExecuteAsync(new ResizeVisualStateCommand(
            beforeSnapshot.DocumentId,
            harness.State.DocumentRevision,
            visualId,
            resizedBounds,
            VisualPlacementMode.Pinned));
        Assert.True(resized.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        var resizedRoutes = CaptureRoutes(harness.State, SplitFlowIds);
        Assert.Equal(resizedBounds, Geometry(harness.State, semanticId).Bounds);
        Assert.Equal(resizedBounds.Size, Visual(harness, visualId).Size);
        Assert.Equal(anchors.AsEnumerable(),
            Visual(harness, visualId).ConnectorAnchors.AsEnumerable());
        AssertRoutesChanged(movedRoutes, resizedRoutes);
        AssertAutomaticOutsideBelow(harness.Scene, semanticId, visualId);
        Assert.Equal(Canvas2DSceneGeometryKind.Path,
            NodeBody(harness.Scene, visualId).Geometry.Kind);
        Assert.Equal(Canvas2DSceneGeometryKind.Ellipse,
            InclusiveMarker(harness.Scene, semanticId).Geometry.Kind);
        Assert.All(SplitFlowIds, flowId => AssertRouteUsesExplicitAnchors(harness, flowId));

        await harness.Host.UndoAsync();
        Assert.Equal(movedBounds, Geometry(harness.State, semanticId).Bounds);
        AssertRoutesEqual(movedRoutes, CaptureRoutes(harness.State, SplitFlowIds));
        await harness.Host.UndoAsync();
        Assert.Equal(beforeBounds, Geometry(harness.State, semanticId).Bounds);
        AssertRoutesEqual(beforeRoutes, CaptureRoutes(harness.State, SplitFlowIds));
        await harness.Host.RedoAsync();
        await harness.Host.RedoAsync();
        Assert.Equal(resizedBounds, Geometry(harness.State, semanticId).Bounds);
        AssertRoutesEqual(resizedRoutes, CaptureRoutes(harness.State, SplitFlowIds));
    }

    [Fact]
    public async Task ManualLabelMoveResizeNameEditResetAndOwnerMoveRemainRelative()
    {
        await using var harness = await HostHarness.CreateAsync();
        await using var interaction = new Canvas2DInteractionController(harness.Session);
        var semanticId = BpmnDemoPipeline.InclusiveSplitGatewayId;
        var visualId = BpmnDemoPipeline.InclusiveSplitGatewayVisualId;
        var nodeBefore = NodeBody(harness.Scene, visualId).Bounds;
        var anchorsBefore = Visual(harness, visualId).ConnectorAnchors;
        var routesBefore = CaptureRoutes(harness.State, SplitFlowIds);
        var automaticLabel = LabelInteractionBody(harness.Scene, semanticId).Bounds;

        await MoveLabelAsync(
            harness,
            interaction,
            semanticId,
            new VectorD(83d, 31d),
            pointerId: 3410);
        var movedLabel = LabelInteractionBody(harness.Scene, semanticId).Bounds;
        Assert.Equal(automaticLabel.Translate(new VectorD(83d, 31d)), movedLabel);
        await ResizeLabelAsync(
            harness,
            interaction,
            visualId,
            new VectorD(47d, 18d),
            pointerId: 3411);
        var manualLabel = LabelInteractionBody(harness.Scene, semanticId).Bounds;
        var manualOverride = ReadOverride(harness, visualId);
        Assert.Equal(movedLabel.Width + 47d, manualLabel.Width, precision: 8);
        Assert.Equal(movedLabel.Height + 18d, manualLabel.Height, precision: 8);
        Assert.Equal(nodeBefore, NodeBody(harness.Scene, visualId).Bounds);
        Assert.Equal(anchorsBefore.AsEnumerable(),
            Visual(harness, visualId).ConnectorAnchors.AsEnumerable());
        AssertRoutesEqual(routesBefore, CaptureRoutes(harness.State, SplitFlowIds));

        var renamed = await harness.Session.ExecuteAsync(new UpdateSemanticElementNameCommand(
            harness.Composition.Document.DocumentId,
            harness.State.DocumentRevision,
            semanticId,
            BpmnSemanticProperties.Name,
            "Choose one or more services"));
        Assert.True(renamed.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(manualOverride, ReadOverride(harness, visualId));
        Assert.Equal(
            "Choose one or more services",
            string.Join(' ', GatewayTextLines(harness.Scene, semanticId)
                .Select(static item => item.Geometry.Content)));
        Assert.Equal(nodeBefore, NodeBody(harness.Scene, visualId).Bounds);
        AssertRoutesEqual(routesBefore, CaptureRoutes(harness.State, SplitFlowIds));

        await harness.Pointer.ContextMenuDocumentPointAsync(
            harness.Scene,
            Center(LabelInteractionBody(harness.Scene, semanticId).Bounds));
        var context = Assert.IsType<DocumentCanvasContextMenuState>(
            harness.Host.CaptureState().ContextMenu);
        var resetAction = Assert.IsType<Canvas2DNodeLabelContextAction>(
            context.NodeLabelAction);
        Assert.Equal(visualId, resetAction.TargetVisualStateId);
        var reset = Assert.IsType<Contracts.History.HistoryOperationResult>(
            await harness.Host.ExecuteNodeLabelContextActionAsync());
        await harness.Session.WaitForIdleAsync();
        Assert.True(reset.IsCommitted);
        Assert.False(NodeLabelVisualOverride.TryRead(
            Visual(harness, visualId).Properties,
            out _));
        AssertAutomaticOutsideBelow(harness.Scene, semanticId, visualId);

        await harness.Host.UndoAsync();
        Assert.Equal(manualOverride, ReadOverride(harness, visualId));
        var restoredManualLabel = LabelInteractionBody(harness.Scene, semanticId).Bounds;
        await harness.Host.RedoAsync();
        AssertAutomaticOutsideBelow(harness.Scene, semanticId, visualId);
        await harness.Host.UndoAsync();
        Assert.Equal(restoredManualLabel,
            LabelInteractionBody(harness.Scene, semanticId).Bounds);

        var ownerDelta = new VectorD(39d, -24d);
        var ownerTarget = nodeBefore.TopLeft + ownerDelta;
        var ownerMoved = await harness.Session.ExecuteAsync(new MoveVisualStateCommand(
            harness.Composition.Document.DocumentId,
            harness.State.DocumentRevision,
            visualId,
            ownerTarget,
            VisualPlacementMode.Pinned));
        Assert.True(ownerMoved.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(manualOverride, ReadOverride(harness, visualId));
        Assert.Equal(nodeBefore.Translate(ownerDelta), NodeBody(harness.Scene, visualId).Bounds);
        Assert.Equal(
            restoredManualLabel.Translate(ownerDelta),
            LabelInteractionBody(harness.Scene, semanticId).Bounds);
        Assert.Equal(anchorsBefore.AsEnumerable(),
            Visual(harness, visualId).ConnectorAnchors.AsEnumerable());
        AssertRoutesChanged(routesBefore, CaptureRoutes(harness.State, SplitFlowIds));
        Assert.All(SplitFlowIds, flowId => AssertRouteUsesExplicitAnchors(harness, flowId));
    }

    [Fact]
    public async Task InclusiveBranchBendAddMoveDeletePreservesEndpointsArrowAndHistory()
    {
        await using var harness = await HostHarness.CreateAsync();
        var flowId = BpmnDemoPipeline.TwelfthSequenceFlowId;
        var visualId = BpmnDemoPipeline.TwelfthSequenceFlowVisualId;
        var automatic = Route(harness.State, flowId);
        var visualBefore = Visual(harness, visualId);
        var historyBefore = harness.State.HistoryStatus;
        var firstBend = Midpoint(automatic.SourceAnchor, automatic.DestinationAnchor) +
            new VectorD(0d, -63d);
        var movedBend = firstBend + new VectorD(41d, 27d);

        await SetRouteAsync(
            harness,
            visualId,
            [automatic.SourceAnchor, firstBend, automatic.DestinationAnchor]);
        AssertBranchRoute(harness, flowId, visualId, [firstBend]);
        await SetRouteAsync(
            harness,
            visualId,
            [
                Route(harness.State, flowId).SourceAnchor,
                movedBend,
                Route(harness.State, flowId).DestinationAnchor,
            ]);
        AssertBranchRoute(harness, flowId, visualId, [movedBend]);
        await SetRouteAsync(
            harness,
            visualId,
            [
                Route(harness.State, flowId).SourceAnchor,
                Route(harness.State, flowId).DestinationAnchor,
            ]);
        AssertBranchRoute(harness, flowId, visualId, []);
        Assert.Equal(historyBefore.EntryCount + 3, harness.State.HistoryStatus.EntryCount);
        Assert.Equal(visualBefore.SourceAnchorId, Visual(harness, visualId).SourceAnchorId);
        Assert.Equal(visualBefore.TargetAnchorId, Visual(harness, visualId).TargetAnchorId);

        await harness.Host.UndoAsync();
        AssertBranchRoute(harness, flowId, visualId, [movedBend]);
        await harness.Host.UndoAsync();
        AssertBranchRoute(harness, flowId, visualId, [firstBend]);
        await harness.Host.UndoAsync();
        Assert.Equal(automatic.Path.AsEnumerable(),
            Route(harness.State, flowId).Path.AsEnumerable());
        Assert.Empty(Visual(harness, visualId).Route);

        await harness.Host.RedoAsync();
        await harness.Host.RedoAsync();
        await harness.Host.RedoAsync();
        AssertBranchRoute(harness, flowId, visualId, []);
        Assert.Equal(2, Visual(harness, visualId).Route.Length);
    }

    [Fact]
    public async Task ToolboxSelectionAndBlankCanvasClickRemainNonCreating()
    {
        await using var harness = await HostHarness.CreateAsync();
        var catalog = new ToolboxCatalog(BpmnPluginRegistration.M34.ToolboxContributions);
        Assert.Equal(
            [
                BpmnSemanticTypes.StartEvent,
                BpmnSemanticTypes.Task,
                BpmnSemanticTypes.ExclusiveGateway,
                BpmnSemanticTypes.ParallelGateway,
                BpmnSemanticTypes.InclusiveGateway,
                BpmnSemanticTypes.EndEvent,
            ],
            catalog.Items.Select(static item => item.ElementTypeId).ToArray());
        Assert.DoesNotContain(catalog.Items, item =>
            item.ElementTypeId == BpmnSemanticTypes.SequenceFlow);
        var item = Assert.Single(catalog.Items, candidate =>
            candidate.ElementTypeId == BpmnSemanticTypes.InclusiveGateway);
        var selection = new ToolboxSelectionState();
        var beforeDocument = harness.Composition.Document.CaptureSnapshot();
        var beforeState = harness.State;

        Assert.True(selection.Select(item.ItemId));
        Assert.Equal(item.ItemId, selection.SelectedItemId);
        await using var interaction = new Canvas2DInteractionController(harness.Session);
        var click = await interaction.PointerActivatedAsync(new PointD(-1000d, -1000d));
        await harness.Session.WaitForIdleAsync();

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, click.Status);
        Assert.Equal(item.ItemId, selection.SelectedItemId);
        Assert.Equal(beforeDocument, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(beforeState.DocumentRevision, harness.State.DocumentRevision);
        Assert.Equal(beforeState.HistoryStatus, harness.State.HistoryStatus);
        Assert.Equal(2, harness.Composition.Document.SemanticModel.Elements.Count(element =>
            element.TypeId == BpmnSemanticTypes.InclusiveGateway));
    }

    private static void AssertInclusiveGatewayScene(
        HostHarness harness,
        ProjectedNode node,
        VisualStateId visualStateId,
        string expectedLabel)
    {
        var body = NodeBody(harness.Scene, visualStateId);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, body.Geometry.Kind);
        Assert.True(body.Geometry.IsClosed);
        Assert.Equal(4, body.Geometry.Points.Length);
        Assert.Equal(node.Id, body.Origin.ProjectedObjectId);
        Assert.DoesNotContain(harness.Scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.ProjectedObjectId == node.Id &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle);

        var marker = InclusiveMarker(harness.Scene, node.Source.SemanticElementId);
        Assert.Contains("inclusive-gateway-o", marker.Origin.StableSourceKey,
            StringComparison.Ordinal);
        Assert.Equal(Canvas2DSceneLayer.Decoration, marker.Layer);
        Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, marker.Geometry.Kind);
        Assert.Null(marker.Style.Fill);
        Assert.Equal(Canvas2DHitTestMode.None, marker.HitTestPolicy.Mode);

        var label = Assert.Single(
            Assert.IsType<ProjectedGraph>(harness.State.ProjectedGraph).Labels,
            candidate => candidate.OwnerId == node.Id);
        var placement = Assert.IsType<NodeLabelPlacement>(label.NodePlacement);
        Assert.Equal(NodeLabelPlacementKind.OutsideBelow, placement.Kind);
        Assert.Equal(8d, placement.Gap);
        Assert.Equal(160d, placement.MaximumWidth);
        Assert.Equal(NodeLabelInteractionPolicy.MoveAndResize, label.NodeInteractionPolicy);
        Assert.Equal(expectedLabel, label.Text);
        Assert.True(LabelInteractionBody(
            harness.Scene,
            node.Source.SemanticElementId).Bounds.Top >= body.Bounds.Bottom + 8d);
    }

    private static Canvas2DSceneItem InclusiveMarker(
        Canvas2DScene scene,
        SemanticElementId semanticId) =>
        Assert.Single(scene.Items, item =>
            item.Origin.SemanticElementId == semanticId &&
            item.Origin.StableSourceKey?.Contains(
                "inclusive-gateway-o",
                StringComparison.Ordinal) == true);

    private static void AssertAnchorSet(
        VisualStateSnapshot visual,
        int leftTargets,
        int rightSources)
    {
        var targets = visual.ConnectorAnchors.Where(anchor =>
            anchor.Side == ConnectorAnchorSide.Left &&
            anchor.Role == ConnectorAnchorRole.Target).ToArray();
        var sources = visual.ConnectorAnchors.Where(anchor =>
            anchor.Side == ConnectorAnchorSide.Right &&
            anchor.Role == ConnectorAnchorRole.Source).ToArray();
        Assert.Equal(leftTargets, targets.Length);
        Assert.Equal(rightSources, sources.Length);
        Assert.Equal(Enumerable.Range(0, leftTargets),
            targets.Select(static anchor => anchor.Order));
        Assert.Equal(Enumerable.Range(0, rightSources),
            sources.Select(static anchor => anchor.Order));
        Assert.Equal(leftTargets + rightSources, visual.ConnectorAnchors.Length);
        Assert.Equal(visual.ConnectorAnchors.Length,
            visual.ConnectorAnchors.Select(static anchor => anchor.Id).Distinct().Count());
    }

    private static bool PortAllows(ProjectedPort port, ConnectorAnchorRole role)
    {
        Assert.True(ProjectedConnectorAnchorMetadata.TryDecode(port, out var anchor));
        return Assert.IsType<ProjectedConnectorAnchor>(anchor).Allows(role);
    }

    private static async Task EditPropertyRoundTripAsync(
        HostHarness harness,
        string fieldId,
        string propertyKey,
        string originalValue,
        string updatedValue)
    {
        var authoritative = await harness.OpenNodePropertiesAsync(
            BpmnDemoPipeline.InclusiveSplitGatewayVisualId);
        var draft = new DocumentCanvasPropertiesDraft(authoritative);
        Assert.True(draft.TryGetDataField(new ElementPropertyFieldId(fieldId), out var field));
        Assert.IsType<DocumentCanvasDataPropertyDraft>(field).EditorValue = updatedValue;
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            BpmnDemoPipeline.InclusiveSplitGatewayVisualId,
            isDirty: true));
        var before = harness.State;

        var result = await harness.Host.ApplyPropertiesAsync(draft);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, result.Status);
        Assert.Equal(updatedValue, GatewayProperty(harness, propertyKey).TextValue);
        Assert.Equal(before.DocumentRevision.Increment(), harness.State.DocumentRevision);
        Assert.Equal(before.HistoryStatus.EntryCount + 1,
            harness.State.HistoryStatus.EntryCount);

        await harness.Host.UndoAsync();
        Assert.Equal(originalValue, GatewayProperty(harness, propertyKey).TextValue);
        AssertCurrentField(harness, fieldId, originalValue);
        await harness.Host.RedoAsync();
        Assert.Equal(updatedValue, GatewayProperty(harness, propertyKey).TextValue);
        AssertCurrentField(harness, fieldId, updatedValue);
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: false,
            targetVisualStateId: null,
            isDirty: false));
    }

    private static void AssertField(
        DocumentCanvasDataPropertySnapshot field,
        string fieldId,
        string propertyKey,
        string expectedValue)
    {
        Assert.Equal(fieldId, field.FieldId.Value);
        Assert.Equal(propertyKey, field.Definition.SemanticPropertyKey);
        Assert.True(field.CanEdit);
        Assert.Equal(expectedValue, field.EditorValue);
        Assert.Equal(PropertyValueKind.Text, Assert.IsType<PropertyValue>(field.Value).Kind);
    }

    private static void AssertCurrentField(
        HostHarness harness,
        string fieldId,
        string expectedValue)
    {
        Assert.True(harness.Host.TryCaptureCurrentPropertiesForm(out var properties));
        var snapshot = Assert.IsType<DocumentCanvasPropertySnapshot>(properties);
        Assert.True(snapshot.TryGetDataField(new ElementPropertyFieldId(fieldId), out var field));
        Assert.Equal(expectedValue,
            Assert.IsType<DocumentCanvasDataPropertySnapshot>(field).EditorValue);
    }

    private static PropertyValue GatewayProperty(HostHarness harness, string propertyKey)
    {
        Assert.True(harness.Composition.Document.SemanticModel.TryGetElement(
            BpmnDemoPipeline.InclusiveSplitGatewayId,
            out var gateway));
        return gateway!.Properties[propertyKey];
    }

    private static async Task ExecuteAsync(HostHarness harness, ICommand command)
    {
        var result = await harness.Session.ExecuteAsync(command);
        Assert.True(result.IsCommitted,
            string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        await harness.Session.WaitForIdleAsync();
    }

    private static async Task SetRouteAsync(
        HostHarness harness,
        VisualStateId visualStateId,
        IEnumerable<PointD> path)
    {
        var result = await harness.Session.ExecuteAsync(new UpdateConnectionRouteCommand(
            harness.Composition.Document.DocumentId,
            harness.State.DocumentRevision,
            visualStateId,
            path));
        Assert.True(result.IsCommitted);
        await harness.Session.WaitForIdleAsync();
    }

    private static void AssertBranchRoute(
        HostHarness harness,
        SemanticElementId flowId,
        VisualStateId visualStateId,
        IEnumerable<PointD> expectedBends)
    {
        var route = Route(harness.State, flowId);
        var expectedWaypoints = expectedBends.ToArray();
        var persistent = Visual(harness, visualStateId);
        Assert.Equal(
            expectedWaypoints.AsEnumerable(),
            persistent.Route.Skip(1).SkipLast(1));
        Assert.Equal(route.SourceAnchor, persistent.Route[0]);
        Assert.Equal(route.DestinationAnchor, persistent.Route[^1]);
        AssertWaypointsInOrder(route.Path, expectedWaypoints);
        AssertRouteUsesExplicitAnchors(harness, flowId);
        var graph = Assert.IsType<ProjectedGraph>(harness.State.ProjectedGraph);
        var edge = Assert.Single(graph.Edges, candidate =>
            candidate.Source.SemanticElementId == flowId);
        var arrow = Assert.Single(harness.Scene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                edge.Id,
                "connector-target-arrow"));
        Assert.Equal(route.DestinationAnchor, arrow.Geometry.Points[0]);
        Assert.Equal(route.SourcePortId, Assert.IsType<ProjectedObjectId>(edge.SourcePortId));
        Assert.Equal(route.TargetPortId, Assert.IsType<ProjectedObjectId>(edge.TargetPortId));
        Assert.NotNull(Visual(harness, visualStateId).SourceAnchorId);
        Assert.NotNull(Visual(harness, visualStateId).TargetAnchorId);
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

    private static async Task MoveLabelAsync(
        HostHarness harness,
        Canvas2DInteractionController interaction,
        SemanticElementId semanticId,
        VectorD delta,
        long pointerId)
    {
        var start = Center(LabelInteractionBody(harness.Scene, semanticId).Bounds);
        var finish = start + delta;
        var pressed = await interaction.PointerPressedAsync(Pointer(
            pointerId,
            harness.Scene,
            start,
            button: 0,
            buttons: 1));
        var moved = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            Assert.IsType<Canvas2DScene>(pressed.SessionState.CurrentScene),
            finish,
            buttons: 1));
        var released = await interaction.PointerReleasedAsync(Pointer(
            pointerId,
            Assert.IsType<Canvas2DScene>(moved.SessionState.CurrentScene),
            finish,
            button: 0));
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
    }

    private static async Task ResizeLabelAsync(
        HostHarness harness,
        Canvas2DInteractionController interaction,
        VisualStateId visualStateId,
        VectorD delta,
        long pointerId)
    {
        var zone = Assert.Single(harness.Scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.StableSourceKey?.StartsWith(
                "node-label-resize-zone:southeast:",
                StringComparison.Ordinal) == true);
        var start = Center(zone.Bounds);
        var finish = start + delta;
        var pressed = await interaction.PointerPressedAsync(Pointer(
            pointerId,
            harness.Scene,
            start,
            button: 0,
            buttons: 1));
        var moved = await interaction.PointerMovedAsync(Pointer(
            pointerId,
            Assert.IsType<Canvas2DScene>(pressed.SessionState.CurrentScene),
            finish,
            buttons: 1));
        var released = await interaction.PointerReleasedAsync(Pointer(
            pointerId,
            Assert.IsType<Canvas2DScene>(moved.SessionState.CurrentScene),
            finish,
            button: 0));
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
    }

    private static ProjectedNode InclusiveNode(
        ProjectedGraph graph,
        SemanticElementId semanticId) =>
        Assert.Single(graph.Nodes, node =>
            node.Source.SemanticElementId == semanticId &&
            node.Source.SemanticTypeId == BpmnSemanticTypes.InclusiveGateway);

    private static LayoutNodeGeometry Geometry(
        EditingSessionState state,
        SemanticElementId semanticId)
    {
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var node = Assert.Single(graph.Nodes, candidate =>
            candidate.Source.SemanticElementId == semanticId);
        return Assert.Single(
            Assert.IsType<LayoutResult>(state.LayoutResult).Nodes,
            geometry => geometry.ProjectedObjectId == node.Id);
    }

    private static RoutedConnectorGeometry Route(
        EditingSessionState state,
        SemanticElementId flowId)
    {
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var edge = Assert.Single(graph.Edges, candidate =>
            candidate.Source.SemanticElementId == flowId);
        return Assert.Single(
            Assert.IsType<RoutingResult>(state.RoutingResult).Routes,
            route => route.ProjectedEdgeId == edge.Id);
    }

    private static ImmutableSortedDictionary<SemanticElementId, ImmutableArray<PointD>>
        CaptureRoutes(
            EditingSessionState state,
            IEnumerable<SemanticElementId> flowIds) =>
        flowIds.ToImmutableSortedDictionary(
            static id => id,
            id => Route(state, id).Path,
            Comparer<SemanticElementId>.Create(static (left, right) =>
                StringComparer.Ordinal.Compare(left.Value, right.Value)));

    private static void AssertRoutesEqual(
        ImmutableSortedDictionary<SemanticElementId, ImmutableArray<PointD>> expected,
        ImmutableSortedDictionary<SemanticElementId, ImmutableArray<PointD>> actual)
    {
        Assert.Equal(expected.Keys, actual.Keys);
        foreach (var flowId in expected.Keys)
        {
            Assert.True(expected[flowId].AsSpan().SequenceEqual(actual[flowId].AsSpan()),
                $"Expected route '{flowId}' to remain unchanged.");
        }
    }

    private static void AssertRoutesChanged(
        ImmutableSortedDictionary<SemanticElementId, ImmutableArray<PointD>> before,
        ImmutableSortedDictionary<SemanticElementId, ImmutableArray<PointD>> after)
    {
        Assert.Equal(before.Keys, after.Keys);
        Assert.All(before.Keys, flowId => Assert.False(
            before[flowId].AsSpan().SequenceEqual(after[flowId].AsSpan())));
    }

    private static void AssertRouteUsesExplicitAnchors(
        HostHarness harness,
        SemanticElementId flowId)
    {
        var document = harness.Composition.Document.CaptureSnapshot();
        var relationship = Assert.Single(document.SemanticModel.Relationships, candidate =>
            candidate.Id == flowId);
        var connector = Assert.Single(document.VisualModel.VisualStates, visual =>
            visual.SemanticElementId == flowId);
        var sourceAnchorId = Assert.IsType<ConnectorAnchorId>(connector.SourceAnchorId);
        var targetAnchorId = Assert.IsType<ConnectorAnchorId>(connector.TargetAnchorId);
        var sourceOwner = Assert.Single(document.VisualModel.VisualStates, visual =>
            visual.SemanticElementId == relationship.SourceId &&
            visual.ConnectorAnchors.Any(anchor => anchor.Id == sourceAnchorId));
        var targetOwner = Assert.Single(document.VisualModel.VisualStates, visual =>
            visual.SemanticElementId == relationship.TargetId &&
            visual.ConnectorAnchors.Any(anchor => anchor.Id == targetAnchorId));
        var sourceAnchor = Assert.Single(sourceOwner.ConnectorAnchors, anchor =>
            anchor.Id == sourceAnchorId);
        var targetAnchor = Assert.Single(targetOwner.ConnectorAnchors, anchor =>
            anchor.Id == targetAnchorId);
        Assert.Equal(ConnectorAnchorRole.Source, sourceAnchor.Role);
        Assert.Equal(ConnectorAnchorRole.Target, targetAnchor.Role);
        var expectedSource = ConnectorAnchorGeometryResolver.ResolvePoint(
            Geometry(harness.State, sourceOwner.SemanticElementId).Bounds,
            sourceAnchor.Side,
            sourceAnchor.Order,
            sourceOwner.ConnectorAnchors.Count(anchor => anchor.Side == sourceAnchor.Side));
        var expectedTarget = ConnectorAnchorGeometryResolver.ResolvePoint(
            Geometry(harness.State, targetOwner.SemanticElementId).Bounds,
            targetAnchor.Side,
            targetAnchor.Order,
            targetOwner.ConnectorAnchors.Count(anchor => anchor.Side == targetAnchor.Side));
        var route = Route(harness.State, flowId);
        Assert.Equal(expectedSource, route.SourceAnchor);
        Assert.Equal(expectedTarget, route.DestinationAnchor);
        Assert.Equal(expectedSource, route.Path[0]);
        Assert.Equal(expectedTarget, route.Path[^1]);
        Assert.NotNull(route.SourcePortId);
        Assert.NotNull(route.TargetPortId);

        var graph = Assert.IsType<ProjectedGraph>(harness.State.ProjectedGraph);
        var edge = Assert.Single(graph.Edges, candidate =>
            candidate.Source.SemanticElementId == flowId);
        Assert.Equal(edge.SourcePortId, route.SourcePortId);
        Assert.Equal(edge.TargetPortId, route.TargetPortId);
        var arrow = Assert.Single(harness.Scene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                edge.Id,
                "connector-target-arrow"));
        Assert.Equal(expectedTarget, arrow.Geometry.Points[0]);
    }

    private static Canvas2DSceneItem NodeBody(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.ProjectedObjectId is { } projectedObjectId &&
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(projectedObjectId, "node"));

    private static Canvas2DSceneItem LabelInteractionBody(
        Canvas2DScene scene,
        SemanticElementId semanticId) =>
        Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle &&
            item.Origin.SemanticElementId == semanticId &&
            item.Origin.ProjectedObjectId is { } projectedObjectId &&
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                projectedObjectId,
                "label-interaction"));

    private static Canvas2DSceneItem[] GatewayTextLines(
        Canvas2DScene scene,
        SemanticElementId semanticId) =>
        scene.Items.Where(item =>
                item.Layer == Canvas2DSceneLayer.Label &&
                item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
                item.Origin.SemanticElementId == semanticId)
            .OrderBy(static item => item.Bounds.Top)
            .ThenBy(static item => item.Id.Value, StringComparer.Ordinal)
            .ToArray();

    private static VisualStateSnapshot Visual(
        HostHarness harness,
        VisualStateId visualStateId) =>
        Assert.Single(harness.Composition.Document.VisualModel.VisualStates, visual =>
            visual.Id == visualStateId);

    private static NodeLabelVisualOverride ReadOverride(
        HostHarness harness,
        VisualStateId visualStateId)
    {
        Assert.True(NodeLabelVisualOverride.TryRead(
            Visual(harness, visualStateId).Properties,
            out var visualOverride));
        return Assert.IsType<NodeLabelVisualOverride>(visualOverride);
    }

    private static void AssertAutomaticOutsideBelow(
        Canvas2DScene scene,
        SemanticElementId semanticId,
        VisualStateId visualStateId)
    {
        var node = NodeBody(scene, visualStateId).Bounds;
        var label = LabelInteractionBody(scene, semanticId).Bounds;
        Assert.True(label.Top >= node.Bottom + 8d);
        Assert.Equal(Center(node).X, Center(label).X, precision: 8);
    }

    private static Canvas2DPointerInput Pointer(
        long pointerId,
        Canvas2DScene scene,
        PointD documentPoint,
        int button = -1,
        int buttons = 0) =>
        new(
            pointerId,
            scene.ViewportTransform.TransformPoint(documentPoint),
            isPrimary: true,
            button: button,
            buttons: buttons);

    private static PointD Center(RectD bounds) => new(
        bounds.Left + (bounds.Width / 2d),
        bounds.Top + (bounds.Height / 2d));

    private static PointD Midpoint(PointD start, PointD end) => new(
        (start.X / 2d) + (end.X / 2d),
        (start.Y / 2d) + (end.Y / 2d));
}
