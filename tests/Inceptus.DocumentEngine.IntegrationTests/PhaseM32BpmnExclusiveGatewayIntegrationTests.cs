using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn;
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
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;
using HostHarness = Inceptus.DocumentEngine.IntegrationTests.PhaseM31BpmnPropertiesIntegrationTests.HostHarness;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseM32BpmnExclusiveGatewayIntegrationTests
{
    private static readonly SemanticElementId[] GatewayFlowIds =
    [
        BpmnDemoPipeline.SecondSequenceFlowId,
        BpmnDemoPipeline.ThirdSequenceFlowId,
        BpmnDemoPipeline.FourthSequenceFlowId,
    ];

    [Fact]
    public async Task DemoTraversesSemanticsProjectionLayoutRoutingAndCanonicalSceneBranching()
    {
        await using var harness = await HostHarness.CreateAsync();
        var document = harness.Composition.Document.CaptureSnapshot();
        var state = harness.State;
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var layout = Assert.IsType<LayoutResult>(state.LayoutResult);
        var routing = Assert.IsType<RoutingResult>(state.RoutingResult);
        var scene = harness.Scene;

        Assert.Equal(26, document.SemanticModel.ElementCount);
        Assert.Equal(25, document.SemanticModel.RelationshipCount);
        Assert.Equal(21, graph.NodeCount);
        Assert.Equal(21, graph.EdgeCount);
        Assert.Equal(21, layout.NodeCount);
        Assert.Equal(21, routing.RouteCount);
        var gatewayNode = Assert.Single(graph.Nodes, node =>
            node.Source.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId);
        Assert.Equal(BpmnSemanticTypes.ExclusiveGateway, gatewayNode.Source.SemanticTypeId);
        var gatewayLabel = Assert.Single(graph.Labels, label => label.OwnerId == gatewayNode.Id);
        Assert.Equal("Approved?", gatewayLabel.Text);
        var gatewayPlacement = Assert.IsType<NodeLabelPlacement>(gatewayLabel.NodePlacement);
        Assert.Equal(NodeLabelPlacementKind.OutsideBelow, gatewayPlacement.Kind);
        Assert.Equal(8d, gatewayPlacement.Gap);
        Assert.Equal(160d, gatewayPlacement.MaximumWidth);

        var canonicalId = Canvas2DSceneObjectIdentity.ForProjected(gatewayNode.Id, "node");
        var diamond = Assert.Single(scene.Items, item => item.Id == canonicalId);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, diamond.Geometry.Kind);
        Assert.True(diamond.Geometry.IsClosed);
        Assert.Equal(4, diamond.Geometry.Points.Length);
        Assert.Equal(gatewayNode.Source.SemanticElementId, diamond.Origin.SemanticElementId);
        Assert.Equal(gatewayNode.Source.VisualStateId, diamond.Origin.VisualStateId);
        Assert.Equal(gatewayNode.Id, diamond.Origin.ProjectedObjectId);
        Assert.Equal(Canvas2DHitTestMode.FillOrStroke, diamond.HitTestPolicy.Mode);

        var marker = Assert.Single(scene.Items, item =>
            item.Origin.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId &&
            item.Origin.Categories.HasFlag(Canvas2DSceneOriginCategory.RegisteredExtension));
        Assert.Equal(Canvas2DSceneLayer.Decoration, marker.Layer);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, marker.Geometry.Kind);
        Assert.True(marker.Geometry.IsClosed);
        Assert.Equal(12, marker.Geometry.Points.Length);
        Assert.Equal(Canvas2DHitTestMode.None, marker.HitTestPolicy.Mode);
        Assert.Equal(gatewayNode.Id, marker.Origin.ProjectedObjectId);
        var gatewayText = Assert.Single(scene.Items, item =>
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            item.Origin.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId);
        Assert.Equal("Approved?", gatewayText.Geometry.Content);
        Assert.True(gatewayText.Bounds.Top >= diamond.Bounds.Bottom + gatewayPlacement.Gap);
        Assert.Equal(Center(diamond.Bounds).X, Center(gatewayText.Bounds).X, precision: 8);
        Assert.DoesNotContain(scene.Items, item =>
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            item.Origin.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId &&
            item.Bounds.Intersects(diamond.Bounds));
        Assert.Equal(canonicalId, new Canvas2DSceneHitTestService().HitTest(
            scene,
            Center(diamond.Bounds))?.SceneObjectId);

        var outgoingEdges = graph.Edges
            .Where(edge => edge.SourceNodeId == gatewayNode.Id)
            .OrderBy(static edge => edge.Id.Value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, outgoingEdges.Length);
        var outgoingRoutes = outgoingEdges.Select(edge =>
            routing.Routes.Single(route => route.ProjectedEdgeId == edge.Id)).ToArray();
        Assert.NotEqual(outgoingRoutes[0].ProjectedEdgeId, outgoingRoutes[1].ProjectedEdgeId);
        Assert.False(outgoingRoutes[0].Path.AsSpan().SequenceEqual(
            outgoingRoutes[1].Path.AsSpan()));
        Assert.NotEqual(outgoingRoutes[0].SourceAnchor, outgoingRoutes[1].SourceAnchor);
        Assert.NotEqual(outgoingRoutes[0].SourcePortId, outgoingRoutes[1].SourcePortId);
        Assert.All(outgoingRoutes, route =>
        {
            Assert.NotNull(route.SourcePortId);
            Assert.NotNull(route.TargetPortId);
        });
        foreach (var route in outgoingRoutes)
        {
            var edge = graph.Edges.Single(candidate => candidate.Id == route.ProjectedEdgeId);
            var connector = Assert.Single(scene.Items, item =>
                item.Id == Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector"));
            var arrow = Assert.Single(scene.Items, item =>
                item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                    edge.Id,
                    "connector-target-arrow"));
            Assert.Equal(route.Path.AsEnumerable(), connector.Geometry.Points.AsEnumerable());
            Assert.Equal(route.DestinationAnchor, arrow.Geometry.Points[0]);
        }

        var approvedBounds = Geometry(state, BpmnDemoPipeline.ApprovedTaskId).Bounds;
        var rejectedBounds = Geometry(state, BpmnDemoPipeline.RejectedTaskId).Bounds;
        Assert.Equal(approvedBounds.Left, rejectedBounds.Left);
        Assert.NotEqual(Center(approvedBounds).Y, Center(rejectedBounds).Y);
        Assert.False(approvedBounds.Intersects(rejectedBounds));
    }

    [Theory]
    [InlineData("code", BpmnSemanticProperties.Code, "APPROVED", "CHECK_APPROVAL")]
    [InlineData("name", BpmnSemanticProperties.Name, "Approved?", "Approval accepted?")]
    [InlineData(
        "description",
        BpmnSemanticProperties.Description,
        "Route the process according to the approval decision.",
        "Check approval policy.\nRoute the selected branch.")]
    public async Task GenericPropertiesCommandEditsOneGatewayFieldWithOneUndoableHistoryEntry(
        string fieldId,
        string propertyKey,
        string originalValue,
        string updatedValue)
    {
        await using var harness = await HostHarness.CreateAsync();
        var original = await harness.OpenNodePropertiesAsync(
            BpmnDemoPipeline.ExclusiveGatewayVisualId);
        AssertGatewayProperties(original);
        var draft = new DocumentCanvasPropertiesDraft(original);
        var field = DraftField(draft, fieldId);
        field.EditorValue = updatedValue;
        Assert.True(draft.IsDirty);
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            isDirty: true));
        var stateBefore = harness.State;

        var result = await harness.Host.ApplyPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, result.Status);
        Assert.Equal(updatedValue, GatewayProperty(harness, propertyKey).TextValue);
        Assert.Equal(
            stateBefore.DocumentRevision.Value + 1,
            harness.State.DocumentRevision.Value);
        Assert.Equal(
            stateBefore.HistoryStatus.EntryCount + 1,
            harness.State.HistoryStatus.EntryCount);
        var committedGatewayLabel = Assert.Single(harness.State.ProjectedGraph!.Labels, label =>
            label.Source.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId);
        Assert.Equal(
            propertyKey == BpmnSemanticProperties.Name ? updatedValue : "Approved?",
            committedGatewayLabel.Text);

        await harness.Host.UndoAsync();
        Assert.Equal(originalValue, GatewayProperty(harness, propertyKey).TextValue);
        AssertCurrentField(harness, fieldId, originalValue);

        await harness.Host.RedoAsync();
        Assert.Equal(updatedValue, GatewayProperty(harness, propertyKey).TextValue);
        AssertCurrentField(harness, fieldId, updatedValue);
        var gatewaySceneLabel = Assert.Single(harness.Scene.Items, item =>
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            item.Origin.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId);
        Assert.Equal(
            propertyKey == BpmnSemanticProperties.Name ? updatedValue : "Approved?",
            gatewaySceneLabel.Geometry.Content);
    }

    [Fact]
    public async Task GenericMovePinsGatewayAndReroutesIncomingAndOutgoingFlowsWithHistory()
    {
        await using var harness = await HostHarness.CreateAsync();
        var documentBefore = harness.Composition.Document.CaptureSnapshot();
        var stateBefore = harness.State;
        var boundsBefore = Geometry(stateBefore, BpmnDemoPipeline.ExclusiveGatewayId).Bounds;
        var labelBoundsBefore = GatewayTextBounds(harness.Scene);
        var routesBefore = GatewayFlowIds.ToDictionary(id => id, id => Route(stateBefore, id).Path);
        var anchorsBefore = GatewayVisual(harness).ConnectorAnchors;
        var targetPosition = new PointD(440d, 900d);

        var result = await harness.Session.ExecuteAsync(new MoveVisualStateCommand(
            documentBefore.DocumentId,
            stateBefore.DocumentRevision,
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            targetPosition,
            VisualPlacementMode.Pinned));
        Assert.True(result.IsCommitted);
        await harness.Session.WaitForIdleAsync();

        var moved = harness.State;
        var movedVisual = GatewayVisual(harness);
        var movedBounds = Geometry(moved, BpmnDemoPipeline.ExclusiveGatewayId).Bounds;
        Assert.Equal(VisualPlacementMode.Pinned, movedVisual.PlacementMode);
        Assert.Equal(targetPosition, movedVisual.Position);
        Assert.Equal(anchorsBefore.AsEnumerable(), movedVisual.ConnectorAnchors.AsEnumerable());
        Assert.Equal(
            new RectD(
                targetPosition.X,
                targetPosition.Y,
                boundsBefore.Width,
                boundsBefore.Height),
            movedBounds);
        Assert.Equal(movedBounds, NodeBody(harness.Scene, BpmnDemoPipeline.ExclusiveGatewayVisualId).Bounds);
        AssertOutsideBelow(movedBounds, GatewayTextBounds(harness.Scene));
        Assert.Equal(
            labelBoundsBefore.TopLeft + (targetPosition - boundsBefore.TopLeft),
            GatewayTextBounds(harness.Scene).TopLeft);
        Assert.Equal(
            stateBefore.HistoryStatus.EntryCount + 1,
            moved.HistoryStatus.EntryCount);
        Assert.Equal(
            documentBefore.SemanticModel.Elements.AsEnumerable(),
            harness.Composition.Document.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            documentBefore.SemanticModel.Relationships.AsEnumerable(),
            harness.Composition.Document.SemanticModel.Relationships.AsEnumerable());
        Assert.All(GatewayFlowIds, id => Assert.False(routesBefore[id].AsSpan().SequenceEqual(
            Route(moved, id).Path.AsSpan())));
        Assert.All(GatewayFlowIds, id => AssertRouteUsesExplicitAnchors(harness, id));

        await harness.Host.UndoAsync();
        var undone = harness.State;
        Assert.Equal(boundsBefore, Geometry(undone, BpmnDemoPipeline.ExclusiveGatewayId).Bounds);
        Assert.Equal(labelBoundsBefore, GatewayTextBounds(harness.Scene));
        Assert.All(GatewayFlowIds, id => Assert.Equal(
            routesBefore[id].AsEnumerable(),
            Route(undone, id).Path.AsEnumerable()));
        Assert.All(GatewayFlowIds, id => AssertRouteUsesExplicitAnchors(harness, id));

        await harness.Host.RedoAsync();
        var redone = harness.State;
        Assert.Equal(movedBounds, Geometry(redone, BpmnDemoPipeline.ExclusiveGatewayId).Bounds);
        AssertOutsideBelow(movedBounds, GatewayTextBounds(harness.Scene));
        Assert.All(GatewayFlowIds, id => Assert.Equal(
            Route(moved, id).Path.AsEnumerable(),
            Route(redone, id).Path.AsEnumerable()));
        Assert.All(GatewayFlowIds, id => AssertRouteUsesExplicitAnchors(harness, id));
    }

    [Fact]
    public async Task GenericResizePreviewsAndCommitsGatewayWithoutFaultingTheScene()
    {
        await using var harness = await HostHarness.CreateAsync();
        await using var interaction = new Canvas2DInteractionController(harness.Session);
        var initial = harness.State;
        var initialDocument = harness.Composition.Document.CaptureSnapshot();
        var initialBounds = Geometry(initial, BpmnDemoPipeline.ExclusiveGatewayId).Bounds;
        var initialAnchors = GatewayVisual(harness).ConnectorAnchors;
        var initialRoutes = GatewayFlowIds.ToDictionary(
            id => id,
            id => Route(initial, id).Path);
        var initialBody = NodeBody(
            Assert.IsType<Canvas2DScene>(initial.CurrentScene),
            BpmnDemoPipeline.ExclusiveGatewayVisualId);
        var selected = await interaction.PointerReleasedAsync(Pointer(
            3200,
            initial.CurrentScene!,
            Center(initialBody.Bounds),
            button: 0));
        Assert.Equal(Canvas2DInteractionStatus.Updated, selected.Status);
        var selectedScene = Assert.IsType<Canvas2DScene>(selected.SessionState.CurrentScene);
        var handle = Assert.Single(selectedScene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.VisualStateId == BpmnDemoPipeline.ExclusiveGatewayVisualId &&
            item.Origin.StableSourceKey?.StartsWith(
                "resize-handle:southeast:",
                StringComparison.Ordinal) == true &&
            item.HitTestPolicy.Mode == Canvas2DHitTestMode.Bounds);
        var start = Center(handle.Bounds);
        var finish = start + new VectorD(20d, 15d);

        var pressed = await interaction.PointerPressedAsync(Pointer(
            3201,
            selectedScene,
            start,
            button: 0,
            buttons: 1));
        var moved = await interaction.PointerMovedAsync(Pointer(
            3201,
            Assert.IsType<Canvas2DScene>(pressed.SessionState.CurrentScene),
            finish,
            buttons: 1));
        AssertReady(moved.SessionState);
        var movedScene = Assert.IsType<Canvas2DScene>(moved.SessionState.CurrentScene);
        var previewBody = Assert.Single(movedScene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Path &&
            item.Geometry.Points.Length == 4 &&
            item.Origin.VisualStateId == BpmnDemoPipeline.ExclusiveGatewayVisualId &&
            item.Origin.StableSourceKey?.StartsWith("resize-preview:",
                StringComparison.Ordinal) == true);
        var previewLabels = movedScene.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            item.Origin.VisualStateId == BpmnDemoPipeline.ExclusiveGatewayVisualId &&
            item.Origin.StableSourceKey?.StartsWith("resize-preview:",
                StringComparison.Ordinal) == true).ToArray();
        Assert.NotEmpty(previewLabels);
        AssertOutsideBelow(previewBody.Bounds, UnionBounds(previewLabels));

        var released = await interaction.PointerReleasedAsync(Pointer(
            3201,
            Assert.IsType<Canvas2DScene>(moved.SessionState.CurrentScene),
            finish,
            button: 0));
        await harness.Session.WaitForIdleAsync();

        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        AssertReady(harness.State);
        var resized = GatewayVisual(harness);
        Assert.Equal(new SizeD(68d, 63d), resized.Size);
        Assert.Equal(VisualPlacementMode.Pinned, resized.PlacementMode);
        Assert.Equal(initialAnchors.AsEnumerable(), resized.ConnectorAnchors.AsEnumerable());
        Assert.Equal(
            new RectD(resized.Position.X, resized.Position.Y, 68d, 63d),
            Geometry(harness.State, BpmnDemoPipeline.ExclusiveGatewayId).Bounds);
        Assert.Equal(
            initial.HistoryStatus.EntryCount + 1,
            harness.State.HistoryStatus.EntryCount);
        Assert.Equal(
            initialDocument.SemanticModel.Elements.AsEnumerable(),
            harness.Composition.Document.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            initialDocument.SemanticModel.Relationships.AsEnumerable(),
            harness.Composition.Document.SemanticModel.Relationships.AsEnumerable());
        Assert.All(GatewayFlowIds, id => Assert.False(initialRoutes[id].AsSpan().SequenceEqual(
            Route(harness.State, id).Path.AsSpan())));
        Assert.All(GatewayFlowIds, id => AssertRouteUsesExplicitAnchors(harness, id));
        var body = NodeBody(harness.Scene, BpmnDemoPipeline.ExclusiveGatewayVisualId);
        Assert.Equal(Canvas2DSceneGeometryKind.Path, body.Geometry.Kind);
        Assert.Equal(4, body.Geometry.Points.Length);
        AssertOutsideBelow(body.Bounds, GatewayTextBounds(harness.Scene));
        Assert.Single(harness.Scene.Items, item =>
            item.Origin.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId &&
            item.Origin.Categories.HasFlag(
                Canvas2DSceneOriginCategory.RegisteredExtension));

        await harness.Host.UndoAsync();
        AssertReady(harness.State);
        Assert.Equal(
            initialBounds,
            Geometry(harness.State, BpmnDemoPipeline.ExclusiveGatewayId).Bounds);
        AssertOutsideBelow(initialBounds, GatewayTextBounds(harness.Scene));
        Assert.All(GatewayFlowIds, id => Assert.Equal(
            initialRoutes[id].AsEnumerable(),
            Route(harness.State, id).Path.AsEnumerable()));
        Assert.All(GatewayFlowIds, id => AssertRouteUsesExplicitAnchors(harness, id));

        await harness.Host.RedoAsync();
        AssertReady(harness.State);
        Assert.Equal(
            new RectD(resized.Position.X, resized.Position.Y, 68d, 63d),
            Geometry(harness.State, BpmnDemoPipeline.ExclusiveGatewayId).Bounds);
        AssertOutsideBelow(
            Geometry(harness.State, BpmnDemoPipeline.ExclusiveGatewayId).Bounds,
            GatewayTextBounds(harness.Scene));
        Assert.Single(harness.Scene.Items, item =>
            item.Origin.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId &&
            item.Origin.Categories.HasFlag(
                Canvas2DSceneOriginCategory.RegisteredExtension));
        Assert.All(GatewayFlowIds, id => AssertRouteUsesExplicitAnchors(harness, id));
    }

    [Fact]
    public async Task GatewayOutgoingPersistentBendAddMoveDeleteAndHistoryRemainGeneric()
    {
        await using var harness = await HostHarness.CreateAsync();
        var stateBefore = harness.State;
        var automatic = Route(stateBefore, BpmnDemoPipeline.ThirdSequenceFlowId);
        var sourceAnchorId = GatewayFlowVisual(harness).SourceAnchorId;
        var targetAnchorId = GatewayFlowVisual(harness).TargetAnchorId;
        var firstBend = Midpoint(automatic.SourceAnchor, automatic.DestinationAnchor) +
            new VectorD(0d, 85d);
        var movedBend = firstBend + new VectorD(0d, 30d);

        await SetRouteAsync(
            harness,
            [automatic.SourceAnchor, firstBend, automatic.DestinationAnchor]);
        AssertPersistentManualRoute(harness, [firstBend]);
        AssertRouteUsesExplicitAnchors(harness, BpmnDemoPipeline.ThirdSequenceFlowId);

        var afterAdd = Route(harness.State, BpmnDemoPipeline.ThirdSequenceFlowId);
        await SetRouteAsync(
            harness,
            [afterAdd.SourceAnchor, movedBend, afterAdd.DestinationAnchor]);
        AssertPersistentManualRoute(harness, [movedBend]);
        AssertRouteUsesExplicitAnchors(harness, BpmnDemoPipeline.ThirdSequenceFlowId);

        var afterMove = Route(harness.State, BpmnDemoPipeline.ThirdSequenceFlowId);
        await SetRouteAsync(
            harness,
            [afterMove.SourceAnchor, afterMove.DestinationAnchor]);
        AssertPersistentManualRoute(harness, []);
        AssertRouteUsesExplicitAnchors(harness, BpmnDemoPipeline.ThirdSequenceFlowId);
        Assert.Equal(
            stateBefore.HistoryStatus.EntryCount + 3,
            harness.State.HistoryStatus.EntryCount);

        await harness.Host.UndoAsync();
        AssertPersistentManualRoute(harness, [movedBend]);
        await harness.Host.UndoAsync();
        AssertPersistentManualRoute(harness, [firstBend]);
        await harness.Host.UndoAsync();
        Assert.Equal(
            automatic.Path.AsEnumerable(),
            Route(harness.State, BpmnDemoPipeline.ThirdSequenceFlowId).Path.AsEnumerable());
        Assert.Empty(GatewayFlowVisual(harness).Route);

        await harness.Host.RedoAsync();
        await harness.Host.RedoAsync();
        await harness.Host.RedoAsync();
        AssertPersistentManualRoute(harness, []);
        Assert.Equal(2, GatewayFlowVisual(harness).Route.Length);
        Assert.Equal(sourceAnchorId, GatewayFlowVisual(harness).SourceAnchorId);
        Assert.Equal(targetAnchorId, GatewayFlowVisual(harness).TargetAnchorId);
        AssertRouteUsesExplicitAnchors(harness, BpmnDemoPipeline.ThirdSequenceFlowId);
    }

    [Fact]
    public async Task GatewayToolboxSelectionAndCanvasClickRemainSelectionOnly()
    {
        await using var harness = await HostHarness.CreateAsync();
        var catalog = new ToolboxCatalog(BpmnPluginRegistration.M32.ToolboxContributions);
        var gatewayItem = Assert.Single(catalog.Items, item =>
            item.ElementTypeId == BpmnSemanticTypes.ExclusiveGateway);
        var selection = new ToolboxSelectionState();
        var documentBefore = harness.Composition.Document.CaptureSnapshot();
        var stateBefore = harness.State;

        Assert.True(selection.Select(gatewayItem.ItemId));
        Assert.Equal(gatewayItem.ItemId, selection.SelectedItemId);
        Assert.Same(documentBefore, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(stateBefore.DocumentRevision, harness.State.DocumentRevision);
        Assert.Equal(stateBefore.HistoryStatus, harness.State.HistoryStatus);

        await using var interaction = new Canvas2DInteractionController(harness.Session);
        var click = await interaction.PointerActivatedAsync(new PointD(-1000d, -1000d));
        await harness.Session.WaitForIdleAsync();

        Assert.Equal(Canvas2DInteractionStatus.Unchanged, click.Status);
        Assert.Equal(gatewayItem.ItemId, selection.SelectedItemId);
        Assert.Equal(documentBefore, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(stateBefore.DocumentRevision, harness.State.DocumentRevision);
        Assert.Equal(stateBefore.HistoryStatus, harness.State.HistoryStatus);
        Assert.Single(harness.Composition.Document.SemanticModel.Elements, element =>
            element.TypeId == BpmnSemanticTypes.ExclusiveGateway);
    }

    private static void AssertGatewayProperties(DocumentCanvasPropertySnapshot properties)
    {
        Assert.Equal(BpmnDemoPipeline.ExclusiveGatewayId, properties.SemanticId);
        Assert.Equal(BpmnDemoPipeline.ExclusiveGatewayVisualId, properties.VisualStateId);
        Assert.Equal(BpmnSemanticTypes.ExclusiveGateway, properties.TypeId);
        Assert.Collection(
            properties.DataFields,
            field => AssertField(field, "code", BpmnSemanticProperties.Code, "APPROVED"),
            field => AssertField(field, "name", BpmnSemanticProperties.Name, "Approved?"),
            field => AssertField(
                field,
                "description",
                BpmnSemanticProperties.Description,
                "Route the process according to the approval decision."));
        Assert.DoesNotContain(properties.DataFields, field =>
            StringComparer.Ordinal.Equals(
                field.Definition.SemanticPropertyKey,
                BpmnSemanticProperties.ElementNumber));
    }

    private static void AssertField(
        DocumentCanvasDataPropertySnapshot field,
        string fieldId,
        string propertyKey,
        string value)
    {
        Assert.Equal(fieldId, field.FieldId.Value);
        Assert.Equal(propertyKey, field.Definition.SemanticPropertyKey);
        Assert.True(field.CanEdit);
        Assert.Equal(value, field.EditorValue);
        Assert.Equal(PropertyValueKind.Text, Assert.IsType<PropertyValue>(field.Value).Kind);
    }

    private static DocumentCanvasDataPropertyDraft DraftField(
        DocumentCanvasPropertiesDraft draft,
        string fieldId)
    {
        Assert.True(draft.TryGetDataField(new ElementPropertyFieldId(fieldId), out var field));
        return Assert.IsType<DocumentCanvasDataPropertyDraft>(field);
    }

    private static void AssertCurrentField(HostHarness harness, string fieldId, string value)
    {
        Assert.True(harness.Host.TryCaptureCurrentPropertiesForm(out var properties));
        var snapshot = Assert.IsType<DocumentCanvasPropertySnapshot>(properties);
        Assert.True(snapshot.TryGetDataField(new ElementPropertyFieldId(fieldId), out var field));
        Assert.Equal(value, Assert.IsType<DocumentCanvasDataPropertySnapshot>(field).EditorValue);
    }

    private static PropertyValue GatewayProperty(HostHarness harness, string propertyKey)
    {
        Assert.True(harness.Composition.Document.SemanticModel.TryGetElement(
            BpmnDemoPipeline.ExclusiveGatewayId,
            out var gateway));
        return gateway!.Properties[propertyKey];
    }

    private static VisualStateSnapshot GatewayVisual(HostHarness harness) =>
        harness.Composition.Document.VisualModel.VisualStates.Single(visual =>
            visual.Id == BpmnDemoPipeline.ExclusiveGatewayVisualId);

    private static VisualStateSnapshot GatewayFlowVisual(HostHarness harness) =>
        harness.Composition.Document.VisualModel.VisualStates.Single(visual =>
            visual.Id == BpmnDemoPipeline.ThirdSequenceFlowVisualId);

    private static void AssertPersistentManualRoute(
        HostHarness harness,
        IReadOnlyList<PointD> expectedWaypoints)
    {
        var persistent = GatewayFlowVisual(harness);
        Assert.Equal(
            expectedWaypoints.AsEnumerable(),
            persistent.Route.Skip(1).SkipLast(1));

        var route = Route(harness.State, BpmnDemoPipeline.ThirdSequenceFlowId);
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

    private static LayoutNodeGeometry Geometry(
        Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionState state,
        SemanticElementId semanticElementId)
    {
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var node = graph.Nodes.Single(candidate =>
            candidate.Source.SemanticElementId == semanticElementId);
        return Assert.IsType<LayoutResult>(state.LayoutResult).Nodes.Single(geometry =>
            geometry.ProjectedObjectId == node.Id);
    }

    private static RoutedConnectorGeometry Route(
        Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionState state,
        SemanticElementId relationshipId)
    {
        var graph = Assert.IsType<ProjectedGraph>(state.ProjectedGraph);
        var edge = graph.Edges.Single(candidate =>
            candidate.Source.SemanticElementId == relationshipId);
        return Assert.IsType<RoutingResult>(state.RoutingResult).Routes.Single(route =>
            route.ProjectedEdgeId == edge.Id);
    }

    private static void AssertRouteUsesExplicitAnchors(
        HostHarness harness,
        SemanticElementId relationshipId)
    {
        var document = harness.Composition.Document.CaptureSnapshot();
        var relationship = Assert.Single(document.SemanticModel.Relationships, candidate =>
            candidate.Id == relationshipId);
        var connector = Assert.Single(document.VisualModel.VisualStates, visual =>
            visual.SemanticElementId == relationshipId);
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
        var route = Route(harness.State, relationshipId);
        Assert.Equal(expectedSource, route.SourceAnchor);
        Assert.Equal(expectedTarget, route.DestinationAnchor);
        Assert.Equal(expectedSource, route.Path[0]);
        Assert.Equal(expectedTarget, route.Path[^1]);

        var graph = Assert.IsType<ProjectedGraph>(harness.State.ProjectedGraph);
        var edge = Assert.Single(graph.Edges, candidate =>
            candidate.Source.SemanticElementId == relationshipId);
        var arrow = Assert.Single(harness.Scene.Items, item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                edge.Id,
                "connector-target-arrow"));
        Assert.Equal(expectedTarget, arrow.Geometry.Points[0]);
    }

    private static Canvas2DSceneItem NodeBody(Canvas2DScene scene, VisualStateId visualStateId) =>
        Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.ProjectedObjectId is not null &&
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                item.Origin.ProjectedObjectId,
                "node"));

    private static async Task SetRouteAsync(HostHarness harness, IEnumerable<PointD> path)
    {
        var result = await harness.Session.ExecuteAsync(new UpdateConnectionRouteCommand(
            harness.Composition.Document.DocumentId,
            harness.State.DocumentRevision,
            BpmnDemoPipeline.ThirdSequenceFlowVisualId,
            path));
        Assert.True(result.IsCommitted);
        await harness.Session.WaitForIdleAsync();
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

    private static void AssertReady(EditingSessionState state)
    {
        var diagnostics = string.Join(
            Environment.NewLine,
            state.RuntimeDiagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}"));
        Assert.True(
            state.Status == EditingSessionStatus.Ready,
            $"Expected a Ready Editing Session.{Environment.NewLine}{diagnostics}");
    }

    private static PointD Center(RectD bounds) => new(
        bounds.Left + (bounds.Width / 2d),
        bounds.Top + (bounds.Height / 2d));

    private static PointD Midpoint(PointD start, PointD end) => new(
        (start.X / 2d) + (end.X / 2d),
        (start.Y / 2d) + (end.Y / 2d));

    private static RectD GatewayTextBounds(Canvas2DScene scene) => UnionBounds(
        scene.Items.Where(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            item.Origin.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId));

    private static RectD UnionBounds(IEnumerable<Canvas2DSceneItem> items)
    {
        var copy = items.ToArray();
        Assert.NotEmpty(copy);
        var left = copy.Min(static item => item.Bounds.Left);
        var top = copy.Min(static item => item.Bounds.Top);
        var right = copy.Max(static item => item.Bounds.Right);
        var bottom = copy.Max(static item => item.Bounds.Bottom);
        return new RectD(left, top, right - left, bottom - top);
    }

    private static void AssertOutsideBelow(RectD nodeBounds, RectD labelBounds)
    {
        Assert.True(labelBounds.Top >= nodeBounds.Bottom + 8d);
        Assert.Equal(Center(nodeBounds).X, Center(labelBounds).X, precision: 8);
    }
}
