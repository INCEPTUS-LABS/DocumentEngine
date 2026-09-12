using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using HostHarness = Inceptus.DocumentEngine.IntegrationTests.PhaseM31BpmnPropertiesIntegrationTests.HostHarness;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseM321BpmnConnectorAnchorBindingIntegrationTests
{
    private static readonly (SemanticElementId FlowId, VisualStateId FlowVisualId,
        ConnectorAnchorId SourceAnchorId, ConnectorAnchorId TargetAnchorId)[] DemoBindings =
    [
        (
            BpmnDemoPipeline.FirstSequenceFlowId,
            BpmnDemoPipeline.FirstSequenceFlowVisualId,
            BpmnDemoPipeline.StartEventSourceAnchorId,
            BpmnDemoPipeline.TaskTargetAnchorId),
        (
            BpmnDemoPipeline.SecondSequenceFlowId,
            BpmnDemoPipeline.SecondSequenceFlowVisualId,
            BpmnDemoPipeline.TaskSourceAnchorId,
            BpmnDemoPipeline.ExclusiveGatewayTargetAnchorId),
        (
            BpmnDemoPipeline.ThirdSequenceFlowId,
            BpmnDemoPipeline.ThirdSequenceFlowVisualId,
            BpmnDemoPipeline.ExclusiveGatewayApprovedSourceAnchorId,
            BpmnDemoPipeline.ApprovedTaskTargetAnchorId),
        (
            BpmnDemoPipeline.FourthSequenceFlowId,
            BpmnDemoPipeline.FourthSequenceFlowVisualId,
            BpmnDemoPipeline.ExclusiveGatewayRejectedSourceAnchorId,
            BpmnDemoPipeline.RejectedTaskTargetAnchorId),
        (
            BpmnDemoPipeline.FifthSequenceFlowId,
            BpmnDemoPipeline.FifthSequenceFlowVisualId,
            BpmnDemoPipeline.ApprovedTaskSourceAnchorId,
            BpmnDemoPipeline.ParallelSplitTargetAnchorId),
        (
            BpmnDemoPipeline.SixthSequenceFlowId,
            BpmnDemoPipeline.SixthSequenceFlowVisualId,
            BpmnDemoPipeline.RejectedTaskSourceAnchorId,
            BpmnDemoPipeline.EndEventRejectedTargetAnchorId),
        (
            BpmnDemoPipeline.SeventhSequenceFlowId,
            BpmnDemoPipeline.SeventhSequenceFlowVisualId,
            BpmnDemoPipeline.ParallelSplitPrepareSourceAnchorId,
            BpmnDemoPipeline.PrepareShipmentTargetAnchorId),
        (
            BpmnDemoPipeline.EighthSequenceFlowId,
            BpmnDemoPipeline.EighthSequenceFlowVisualId,
            BpmnDemoPipeline.ParallelSplitNotifySourceAnchorId,
            BpmnDemoPipeline.NotifyCustomerTargetAnchorId),
        (
            BpmnDemoPipeline.NinthSequenceFlowId,
            BpmnDemoPipeline.NinthSequenceFlowVisualId,
            BpmnDemoPipeline.PrepareShipmentSourceAnchorId,
            BpmnDemoPipeline.ParallelJoinPrepareTargetAnchorId),
        (
            BpmnDemoPipeline.TenthSequenceFlowId,
            BpmnDemoPipeline.TenthSequenceFlowVisualId,
            BpmnDemoPipeline.NotifyCustomerSourceAnchorId,
            BpmnDemoPipeline.ParallelJoinNotifyTargetAnchorId),
        (
            BpmnDemoPipeline.EleventhSequenceFlowId,
            BpmnDemoPipeline.EleventhSequenceFlowVisualId,
            BpmnDemoPipeline.ParallelJoinSourceAnchorId,
            BpmnDemoPipeline.InclusiveSplitTargetAnchorId),
        (
            BpmnDemoPipeline.TwelfthSequenceFlowId,
            BpmnDemoPipeline.TwelfthSequenceFlowVisualId,
            BpmnDemoPipeline.InclusiveSplitInsuranceSourceAnchorId,
            BpmnDemoPipeline.AddInsuranceTargetAnchorId),
        (
            BpmnDemoPipeline.ThirteenthSequenceFlowId,
            BpmnDemoPipeline.ThirteenthSequenceFlowVisualId,
            BpmnDemoPipeline.InclusiveSplitGiftWrapSourceAnchorId,
            BpmnDemoPipeline.AddGiftWrapTargetAnchorId),
        (
            BpmnDemoPipeline.FourteenthSequenceFlowId,
            BpmnDemoPipeline.FourteenthSequenceFlowVisualId,
            BpmnDemoPipeline.AddInsuranceSourceAnchorId,
            BpmnDemoPipeline.InclusiveJoinInsuranceTargetAnchorId),
        (
            BpmnDemoPipeline.FifteenthSequenceFlowId,
            BpmnDemoPipeline.FifteenthSequenceFlowVisualId,
            BpmnDemoPipeline.AddGiftWrapSourceAnchorId,
            BpmnDemoPipeline.InclusiveJoinGiftWrapTargetAnchorId),
        (
            BpmnDemoPipeline.SixteenthSequenceFlowId,
            BpmnDemoPipeline.SixteenthSequenceFlowVisualId,
            BpmnDemoPipeline.InclusiveJoinSourceAnchorId,
            BpmnDemoPipeline.EndEventInclusiveTargetAnchorId),
    ];

    [Fact]
    public async Task DemoProjectsAndRoutesEveryFlowThroughItsExplicitOwnedRoleAnchor()
    {
        await using var harness = await HostHarness.CreateAsync();
        var document = harness.Composition.Document.CaptureSnapshot();
        var graph = Assert.IsType<ProjectedGraph>(harness.State.ProjectedGraph);
        var routing = Assert.IsType<RoutingResult>(harness.State.RoutingResult);

        Assert.Equal(52, document.VisualModel.VisualStates.Sum(static visual =>
            visual.ConnectorAnchors.Length));
        Assert.Equal(44, graph.Ports.Length);

        foreach (var binding in DemoBindings)
        {
            var relationship = Assert.Single(document.SemanticModel.Relationships, candidate =>
                candidate.Id == binding.FlowId);
            var flowVisual = Visual(document, binding.FlowVisualId);
            Assert.Equal(binding.SourceAnchorId, flowVisual.SourceAnchorId);
            Assert.Equal(binding.TargetAnchorId, flowVisual.TargetAnchorId);

            var sourceOwner = AnchorOwner(document, binding.SourceAnchorId);
            var targetOwner = AnchorOwner(document, binding.TargetAnchorId);
            Assert.Equal(relationship.SourceId, sourceOwner.Visual.SemanticElementId);
            Assert.Equal(relationship.TargetId, targetOwner.Visual.SemanticElementId);
            Assert.Equal(ConnectorAnchorRole.Source, sourceOwner.Anchor.Role);
            Assert.Equal(ConnectorAnchorRole.Target, targetOwner.Anchor.Role);

            var edge = Assert.Single(graph.Edges, candidate =>
                candidate.Source.SemanticElementId == binding.FlowId);
            var sourcePortId = Assert.IsType<ProjectedObjectId>(edge.SourcePortId);
            var targetPortId = Assert.IsType<ProjectedObjectId>(edge.TargetPortId);
            AssertProjectedAnchor(graph, sourcePortId, binding.SourceAnchorId);
            AssertProjectedAnchor(graph, targetPortId, binding.TargetAnchorId);

            var route = Assert.Single(routing.Routes, candidate =>
                candidate.ProjectedEdgeId == edge.Id);
            var expectedSource = ResolvePoint(
                harness.State,
                sourceOwner.Visual,
                sourceOwner.Anchor);
            var expectedTarget = ResolvePoint(
                harness.State,
                targetOwner.Visual,
                targetOwner.Anchor);
            Assert.Equal(expectedSource, route.SourceAnchor);
            Assert.Equal(expectedTarget, route.DestinationAnchor);
            Assert.Equal(expectedSource, route.Path[0]);
            Assert.Equal(expectedTarget, route.Path[^1]);
            Assert.Equal(sourcePortId, route.SourcePortId);
            Assert.Equal(targetPortId, route.TargetPortId);

            var connector = Assert.Single(harness.Scene.Items, item =>
                item.Id == Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector"));
            var arrow = Assert.Single(harness.Scene.Items, item =>
                item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                    edge.Id,
                    "connector-target-arrow"));
            Assert.Equal(
                route.Path.AsEnumerable(),
                Canvas2DConnectorPathMetadata.Resolve(connector).AsEnumerable());
            Assert.Equal(expectedTarget, arrow.Geometry.Points[0]);
        }

        var gatewayBounds = Geometry(
            harness.State,
            BpmnDemoPipeline.ExclusiveGatewayId).Bounds;
        Assert.Equal(
            gatewayBounds.Top + (gatewayBounds.Height / 3d),
            Route(harness.State, BpmnDemoPipeline.ThirdSequenceFlowId).SourceAnchor.Y,
            precision: 10);
        Assert.Equal(
            gatewayBounds.Top + ((gatewayBounds.Height * 2d) / 3d),
            Route(harness.State, BpmnDemoPipeline.FourthSequenceFlowId).SourceAnchor.Y,
            precision: 10);

        var endBounds = Geometry(harness.State, BpmnDemoPipeline.EndEventId).Bounds;
        Assert.Equal(
            endBounds.Top + (endBounds.Height / 3d),
            Route(harness.State, BpmnDemoPipeline.SixteenthSequenceFlowId).DestinationAnchor.Y,
            precision: 10);
        Assert.Equal(
            endBounds.Top + ((endBounds.Height * 2d) / 3d),
            Route(harness.State, BpmnDemoPipeline.SixthSequenceFlowId).DestinationAnchor.Y,
            precision: 10);
    }

    [Theory]
    [MemberData(nameof(NodeAnchorExpectations))]
    public async Task SelectedNodesExposeActualAnchorsAndRoleSensitiveGenericActions(
        VisualStateId visualStateId,
        ConnectorAnchorSide actionSide,
        int expectedAnchorCount,
        bool canAddSource,
        bool canAddTarget)
    {
        await using var harness = await HostHarness.CreateAsync();
        await using var interaction = new Canvas2DInteractionController(harness.Session);
        var body = NodeBody(harness.Scene, visualStateId);

        var selected = await interaction.PointerReleasedAsync(Pointer(
            4100,
            harness.Scene,
            Center(body.Bounds),
            button: 0));
        Assert.Equal(Canvas2DInteractionStatus.Updated, selected.Status);
        await harness.Session.WaitForIdleAsync();

        var selectedScene = harness.Scene;
        var handles = ConnectorAnchorHandles(selectedScene, visualStateId);
        Assert.Equal(expectedAnchorCount, handles.Length);
        var persisted = harness.Composition.Document.VisualModel.VisualStates.Single(
            visual => visual.Id == visualStateId).ConnectorAnchors;
        Assert.Equal(
            persisted.Select(static anchor => anchor.Id).OrderBy(static id => id.Value),
            handles.Select(AnchorId).OrderBy(static id => id.Value));

        var edge = Assert.Single(selectedScene.Items, item =>
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.StableSourceKey?.StartsWith(
                $"resize-edge-zone:{ResizeRole(actionSide)}:",
                StringComparison.Ordinal) == true);
        await harness.Pointer.ContextMenuDocumentPointAsync(
            selectedScene,
            EdgePoint(body.Bounds, actionSide, 0.15d));
        var action = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            harness.Host.CaptureState().ContextMenu?.ConnectorAnchorAction);
        Assert.Equal(Canvas2DConnectorAnchorContextActionKind.AddAnchor, action.Kind);
        Assert.Equal(canAddSource, action.CanAdd(ConnectorAnchorRole.Source));
        Assert.Equal(canAddTarget, action.CanAdd(ConnectorAnchorRole.Target));
        harness.Host.CloseContextMenu();

        var usedHandle = handles[0];
        await harness.Pointer.ContextMenuDocumentPointAsync(
            selectedScene,
            Center(usedHandle.Bounds));
        var delete = Assert.IsType<Canvas2DConnectorAnchorContextAction>(
            harness.Host.CaptureState().ContextMenu?.ConnectorAnchorAction);
        Assert.Equal(Canvas2DConnectorAnchorContextActionKind.DeleteAnchor, delete.Kind);
        Assert.False(delete.CanDelete);
    }

    [Fact]
    public async Task AddingUnusedGatewayAnchorRedistributesRoutesWithoutChangingBindings()
    {
        await using var harness = await HostHarness.CreateAsync();
        var before = harness.Composition.Document.CaptureSnapshot();
        var stateBefore = harness.State;
        var approvedBefore = Route(stateBefore, BpmnDemoPipeline.ThirdSequenceFlowId);
        var rejectedBefore = Route(stateBefore, BpmnDemoPipeline.FourthSequenceFlowId);
        var historyBefore = stateBefore.HistoryStatus;
        var insertedId = new ConnectorAnchorId(
            "test:bpmn:m321:gateway:right:inserted-source");

        var result = await harness.Session.ExecuteAsync(new AddConnectorAnchorCommand(
            before.DocumentId,
            before.Revision,
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            insertedId,
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            insertionIndex: 1));
        Assert.True(result.IsCommitted);
        await harness.Session.WaitForIdleAsync();

        var changedDocument = harness.Composition.Document.CaptureSnapshot();
        var gateway = Visual(changedDocument, BpmnDemoPipeline.ExclusiveGatewayVisualId);
        var right = gateway.ConnectorAnchors
            .Where(static anchor => anchor.Side == ConnectorAnchorSide.Right)
            .OrderBy(static anchor => anchor.Order)
            .ToArray();
        Assert.Collection(
            right,
            anchor => Assert.Equal(
                BpmnDemoPipeline.ExclusiveGatewayApprovedSourceAnchorId,
                anchor.Id),
            anchor => Assert.Equal(insertedId, anchor.Id),
            anchor => Assert.Equal(
                BpmnDemoPipeline.ExclusiveGatewayRejectedSourceAnchorId,
                anchor.Id));
        Assert.Equal([0, 1, 2], right.Select(static anchor => anchor.Order));
        Assert.Equal(
            BpmnDemoPipeline.ExclusiveGatewayApprovedSourceAnchorId,
            Visual(changedDocument, BpmnDemoPipeline.ThirdSequenceFlowVisualId)
                .SourceAnchorId);
        Assert.Equal(
            BpmnDemoPipeline.ExclusiveGatewayRejectedSourceAnchorId,
            Visual(changedDocument, BpmnDemoPipeline.FourthSequenceFlowVisualId)
                .SourceAnchorId);
        var approvedChanged = Route(harness.State, BpmnDemoPipeline.ThirdSequenceFlowId);
        var rejectedChanged = Route(harness.State, BpmnDemoPipeline.FourthSequenceFlowId);
        Assert.NotEqual(approvedBefore.SourceAnchor, approvedChanged.SourceAnchor);
        Assert.NotEqual(rejectedBefore.SourceAnchor, rejectedChanged.SourceAnchor);
        Assert.Equal(
            stateBefore.DocumentRevision.Value + 1,
            harness.State.DocumentRevision.Value);
        Assert.Equal(historyBefore.EntryCount + 1, harness.State.HistoryStatus.EntryCount);

        await harness.Host.UndoAsync();
        Assert.Equal(
            approvedBefore.Path.AsEnumerable(),
            Route(harness.State, BpmnDemoPipeline.ThirdSequenceFlowId).Path.AsEnumerable());
        Assert.Equal(
            rejectedBefore.Path.AsEnumerable(),
            Route(harness.State, BpmnDemoPipeline.FourthSequenceFlowId).Path.AsEnumerable());
        Assert.DoesNotContain(
            Visual(
                harness.Composition.Document.CaptureSnapshot(),
                BpmnDemoPipeline.ExclusiveGatewayVisualId).ConnectorAnchors,
            anchor => anchor.Id == insertedId);

        await harness.Host.RedoAsync();
        Assert.Contains(
            Visual(
                harness.Composition.Document.CaptureSnapshot(),
                BpmnDemoPipeline.ExclusiveGatewayVisualId).ConnectorAnchors,
            anchor => anchor.Id == insertedId);
        Assert.Equal(approvedChanged.SourceAnchor,
            Route(harness.State, BpmnDemoPipeline.ThirdSequenceFlowId).SourceAnchor);
        Assert.Equal(rejectedChanged.SourceAnchor,
            Route(harness.State, BpmnDemoPipeline.FourthSequenceFlowId).SourceAnchor);
    }

    [Fact]
    public async Task ReferencedBpmnAnchorCannotBeRemovedOrDetachItsSequenceFlow()
    {
        await using var harness = await HostHarness.CreateAsync();
        var before = harness.Composition.Document.CaptureSnapshot();
        var stateBefore = harness.State;

        var result = await harness.Session.ExecuteAsync(new RemoveConnectorAnchorCommand(
            before.DocumentId,
            before.Revision,
            BpmnDemoPipeline.StartEventVisualId,
            BpmnDemoPipeline.StartEventSourceAnchorId));
        await harness.Session.WaitForIdleAsync();

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandExecutionDiagnosticCodes.ConnectorAnchorInUse);
        Assert.Same(before, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(stateBefore.DocumentRevision, harness.State.DocumentRevision);
        Assert.Equal(stateBefore.HistoryStatus, harness.State.HistoryStatus);
        Assert.Equal(
            BpmnDemoPipeline.StartEventSourceAnchorId,
            Visual(before, BpmnDemoPipeline.FirstSequenceFlowVisualId).SourceAnchorId);
    }

    [Fact]
    public async Task AnchorAndFlowCreationRemainThreeIndependentHistoryOperations()
    {
        await using var harness = await HostHarness.CreateAsync();
        var sourceAnchorId = new ConnectorAnchorId("test:bpmn:m321:history:source");
        var targetAnchorId = new ConnectorAnchorId("test:bpmn:m321:history:target");
        var flowId = new SemanticElementId("test:bpmn:m321:history:flow");
        var flowVisualId = new VisualStateId("test:bpmn:m321:history:flow-visual");
        var initialHistory = harness.State.HistoryStatus.EntryCount;

        Assert.True((await harness.Session.ExecuteAsync(new AddConnectorAnchorCommand(
            harness.Composition.Document.DocumentId,
            harness.State.DocumentRevision,
            BpmnDemoPipeline.TaskVisualId,
            sourceAnchorId,
            ConnectorAnchorSide.Top,
            ConnectorAnchorRole.Source,
            0))).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.True((await harness.Session.ExecuteAsync(new AddConnectorAnchorCommand(
            harness.Composition.Document.DocumentId,
            harness.State.DocumentRevision,
            BpmnDemoPipeline.EndEventVisualId,
            targetAnchorId,
            ConnectorAnchorSide.Top,
            ConnectorAnchorRole.Target,
            0))).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.True((await harness.Session.ExecuteAsync(
            new CreateBpmnSequenceFlowCommand(
                harness.Composition.Document.DocumentId,
                harness.State.DocumentRevision,
                flowId,
                flowVisualId,
                BpmnDemoPipeline.TaskId,
                BpmnDemoPipeline.EndEventId,
                sourceAnchorId,
                targetAnchorId))).IsCommitted);
        await harness.Session.WaitForIdleAsync();

        Assert.Equal(initialHistory + 3, harness.State.HistoryStatus.EntryCount);
        var created = harness.Composition.Document.CaptureSnapshot();
        Assert.True(created.SemanticModel.TryGetRelationship(flowId, out _));
        Assert.Equal(sourceAnchorId, Visual(created, flowVisualId).SourceAnchorId);
        Assert.Equal(targetAnchorId, Visual(created, flowVisualId).TargetAnchorId);

        await harness.Host.UndoAsync();
        var withoutFlow = harness.Composition.Document.CaptureSnapshot();
        Assert.False(withoutFlow.SemanticModel.TryGetRelationship(flowId, out _));
        Assert.Contains(
            Visual(withoutFlow, BpmnDemoPipeline.TaskVisualId).ConnectorAnchors,
            anchor => anchor.Id == sourceAnchorId);
        Assert.Contains(
            Visual(withoutFlow, BpmnDemoPipeline.EndEventVisualId).ConnectorAnchors,
            anchor => anchor.Id == targetAnchorId);

        await harness.Host.UndoAsync();
        var withoutTarget = harness.Composition.Document.CaptureSnapshot();
        Assert.DoesNotContain(
            Visual(withoutTarget, BpmnDemoPipeline.EndEventVisualId).ConnectorAnchors,
            anchor => anchor.Id == targetAnchorId);
        Assert.Contains(
            Visual(withoutTarget, BpmnDemoPipeline.TaskVisualId).ConnectorAnchors,
            anchor => anchor.Id == sourceAnchorId);

        await harness.Host.UndoAsync();
        Assert.DoesNotContain(
            Visual(
                harness.Composition.Document.CaptureSnapshot(),
                BpmnDemoPipeline.TaskVisualId).ConnectorAnchors,
            anchor => anchor.Id == sourceAnchorId);

        await harness.Host.RedoAsync();
        await harness.Host.RedoAsync();
        await harness.Host.RedoAsync();
        var redone = harness.Composition.Document.CaptureSnapshot();
        Assert.True(redone.SemanticModel.TryGetRelationship(flowId, out _));
        Assert.Equal(sourceAnchorId, Visual(redone, flowVisualId).SourceAnchorId);
        Assert.Equal(targetAnchorId, Visual(redone, flowVisualId).TargetAnchorId);
    }

    public static TheoryData<VisualStateId, ConnectorAnchorSide, int, bool, bool>
        NodeAnchorExpectations => new()
        {
            {
                BpmnDemoPipeline.StartEventVisualId,
                ConnectorAnchorSide.Right,
                1,
                true,
                false
            },
            {
                BpmnDemoPipeline.TaskVisualId,
                ConnectorAnchorSide.Right,
                2,
                true,
                true
            },
            {
                BpmnDemoPipeline.ExclusiveGatewayVisualId,
                ConnectorAnchorSide.Right,
                3,
                true,
                true
            },
            {
                BpmnDemoPipeline.EndEventVisualId,
                ConnectorAnchorSide.Top,
                2,
                false,
                true
            },
        };

    private static VisualStateSnapshot Visual(DocumentSnapshot document, VisualStateId id) =>
        Assert.Single(document.VisualModel.VisualStates, visual => visual.Id == id);

    private static (VisualStateSnapshot Visual, ConnectorAnchor Anchor) AnchorOwner(
        DocumentSnapshot document,
        ConnectorAnchorId anchorId)
    {
        var matches = document.VisualModel.VisualStates
            .SelectMany(visual => visual.ConnectorAnchors.Select(anchor => (visual, anchor)))
            .Where(candidate => candidate.anchor.Id == anchorId)
            .ToArray();
        var match = Assert.Single(matches);
        return (match.visual, match.anchor);
    }

    private static void AssertProjectedAnchor(
        ProjectedGraph graph,
        ProjectedObjectId portId,
        ConnectorAnchorId anchorId)
    {
        var port = Assert.Single(graph.Ports, candidate => candidate.Id == portId);
        Assert.True(ProjectedConnectorAnchorMetadata.TryDecode(port, out var projected));
        Assert.Equal(anchorId, Assert.IsType<ProjectedConnectorAnchor>(projected).Id);
    }

    private static PointD ResolvePoint(
        EditingSessionState state,
        VisualStateSnapshot owner,
        ConnectorAnchor anchor)
    {
        var sideCount = owner.ConnectorAnchors.Count(candidate =>
            candidate.Side == anchor.Side);
        return ConnectorAnchorGeometryResolver.ResolvePoint(
            Geometry(state, owner.SemanticElementId).Bounds,
            anchor.Side,
            anchor.Order,
            sideCount);
    }

    private static Inceptus.DocumentEngine.Contracts.Layout.LayoutNodeGeometry Geometry(
        EditingSessionState state,
        SemanticElementId semanticElementId)
    {
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var node = Assert.Single(graph.Nodes, candidate =>
            candidate.Source.SemanticElementId == semanticElementId);
        return Assert.Single(state.LayoutResult!.Nodes, candidate =>
            candidate.ProjectedObjectId == node.Id);
    }

    private static RoutedConnectorGeometry Route(
        EditingSessionState state,
        SemanticElementId relationshipId)
    {
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var edge = Assert.Single(graph.Edges, candidate =>
            candidate.Source.SemanticElementId == relationshipId);
        return Assert.Single(state.RoutingResult!.Routes, candidate =>
            candidate.ProjectedEdgeId == edge.Id);
    }

    private static Canvas2DSceneItem NodeBody(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.ProjectedObjectId is not null &&
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                item.Origin.ProjectedObjectId,
                "node"));

    private static Canvas2DSceneItem[] ConnectorAnchorHandles(
        Canvas2DScene scene,
        VisualStateId visualStateId) =>
        scene.Items
            .Where(item =>
                item.Layer == Canvas2DSceneLayer.Overlay &&
                item.Origin.VisualStateId == visualStateId &&
                item.Metadata.TryGetValue(
                    Canvas2DConnectorAnchorMetadata.AnchorId,
                    out var anchorId) &&
                anchorId.Kind == PropertyValueKind.Text)
            .OrderBy(static item => item.Id.Value, StringComparer.Ordinal)
            .ToArray();

    private static ConnectorAnchorId AnchorId(Canvas2DSceneItem item) =>
        new(item.Metadata[Canvas2DConnectorAnchorMetadata.AnchorId].TextValue);

    private static string ResizeRole(ConnectorAnchorSide side) => side switch
    {
        ConnectorAnchorSide.Top => "north",
        ConnectorAnchorSide.Right => "east",
        ConnectorAnchorSide.Bottom => "south",
        ConnectorAnchorSide.Left => "west",
        _ => throw new ArgumentOutOfRangeException(nameof(side)),
    };

    private static PointD EdgePoint(RectD bounds, ConnectorAnchorSide side, double parameter) =>
        side switch
        {
            ConnectorAnchorSide.Top =>
                new PointD(bounds.Left + (bounds.Width * parameter), bounds.Top),
            ConnectorAnchorSide.Right =>
                new PointD(bounds.Right, bounds.Top + (bounds.Height * parameter)),
            ConnectorAnchorSide.Bottom =>
                new PointD(bounds.Left + (bounds.Width * parameter), bounds.Bottom),
            ConnectorAnchorSide.Left =>
                new PointD(bounds.Left, bounds.Top + (bounds.Height * parameter)),
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };

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
}
