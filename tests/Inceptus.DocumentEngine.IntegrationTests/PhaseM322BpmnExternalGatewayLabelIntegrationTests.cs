using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;
using HostHarness = Inceptus.DocumentEngine.IntegrationTests.PhaseM31BpmnPropertiesIntegrationTests.HostHarness;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseM322BpmnExternalGatewayLabelIntegrationTests
{
    [Fact]
    public async Task ExternalNameIsMeasuredOwnedHittableAndNormalizesToCanonicalGateway()
    {
        await using var harness = await HostHarness.CreateAsync();
        var graph = Assert.IsType<ProjectedGraph>(harness.State.ProjectedGraph);
        var gatewayNode = Assert.Single(graph.Nodes, node =>
            node.Source.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId);
        var projectedLabel = Assert.Single(graph.Labels, label => label.OwnerId == gatewayNode.Id);
        var placement = Assert.IsType<NodeLabelPlacement>(projectedLabel.NodePlacement);
        var scene = harness.Scene;
        var body = NodeBody(scene);
        var marker = Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Decoration &&
            item.Origin.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId);
        var label = Assert.Single(GatewayLabels(scene));

        Assert.Equal("Approved?", projectedLabel.Text);
        Assert.Equal(NodeLabelPlacementKind.OutsideBelow, placement.Kind);
        Assert.Equal(8d, placement.Gap);
        Assert.Equal(160d, placement.MaximumWidth);
        Assert.Equal("Approved?", label.Geometry.Content);
        Assert.Equal(gatewayNode.Source.SemanticElementId, label.Origin.SemanticElementId);
        Assert.Equal(gatewayNode.Source.VisualStateId, label.Origin.VisualStateId);
        Assert.Equal(projectedLabel.Id, label.Origin.ProjectedObjectId);
        Assert.Contains(gatewayNode.Id, label.Origin.RelatedProjectedObjectIds);
        Assert.NotNull(label.Clip);
        Assert.True(label.Clip!.Value.Top >= body.Bounds.Bottom + placement.Gap);
        AssertOutsideBelow(body.Bounds, LabelBounds([label]));
        Assert.Equal(Canvas2DHitTestMode.None, marker.HitTestPolicy.Mode);
        var labelHit = Assert.IsType<Canvas2DSceneHitTestResult>(
            new Canvas2DSceneHitTestService().HitTest(scene, Center(label.Bounds)));
        var labelInteractionBody = Assert.Single(scene.Items, item =>
            item.Id == labelHit.SceneObjectId);
        Assert.NotEqual(label.Id, labelInteractionBody.Id);
        Assert.Equal(Canvas2DSceneLayer.Label, labelInteractionBody.Layer);
        Assert.Equal(projectedLabel.Id, labelInteractionBody.Origin.ProjectedObjectId);
        Assert.Equal(gatewayNode.Source.VisualStateId,
            labelInteractionBody.Origin.VisualStateId);
        Assert.Equal(label.Clip, labelInteractionBody.Clip);
        Assert.Equal(label.Clip, labelInteractionBody.Bounds);
        Assert.Equal(0d, labelInteractionBody.Style.Opacity);

        await using var interaction = new Canvas2DInteractionController(harness.Session);
        var selected = await interaction.PointerReleasedAsync(Pointer(
            8220,
            scene,
            Center(label.Bounds),
            button: 0));
        Assert.Equal(Canvas2DInteractionStatus.Updated, selected.Status);
        Assert.Equal(
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            Assert.Single(selected.SessionState.EditorState.Selection));
        var selectedScene = Assert.IsType<Canvas2DScene>(selected.SessionState.CurrentScene);
        var overlay = Assert.Single(selectedScene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Origin.StableSourceKey == $"selection:{body.Id.Value}");
        Assert.Equal(body.Bounds, overlay.Bounds);
        Assert.Equal(body.Geometry, overlay.Geometry);

        await harness.Pointer.ContextMenuDocumentPointAsync(
            selectedScene,
            Center(Assert.Single(GatewayLabels(selectedScene)).Bounds));
        Assert.Equal(
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            harness.Host.CaptureState().ContextMenu?.TargetVisualStateId);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(
            await harness.Host.OpenPropertiesAsync(BpmnDemoPipeline.ExclusiveGatewayVisualId));
        Assert.Equal(
            ["code", "name", "description"],
            properties.DataFields.Select(static field => field.FieldId.Value).ToArray());
        Assert.DoesNotContain(properties.DataFields, field =>
            field.Definition.SemanticPropertyKey == "BPMN.ElementNumber");
    }

    [Fact]
    public async Task LongNameUsesGenericExternalWrappingAndUndoRedoWithoutLayoutOrRouteChanges()
    {
        const string longName = "Is the customer order approved?";
        await using var harness = await HostHarness.CreateAsync();
        var originalLayout = Assert.IsType<Contracts.Layout.LayoutResult>(
            harness.State.LayoutResult);
        var originalRouting = Assert.IsType<RoutingResult>(harness.State.RoutingResult);
        var originalBody = NodeBody(harness.Scene);
        var originalHistory = harness.State.HistoryStatus;
        var properties = await harness.OpenNodePropertiesAsync(
            BpmnDemoPipeline.ExclusiveGatewayVisualId);
        var draft = new DocumentCanvasPropertiesDraft(properties);
        Assert.True(draft.TryGetDataField(new ElementPropertyFieldId("name"), out var nameField));
        Assert.IsType<DocumentCanvasDataPropertyDraft>(nameField).EditorValue = longName;
        Assert.True(harness.Host.UpdatePropertiesFormState(
            isOpen: true,
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            isDirty: true));

        var committed = await harness.Host.ApplyPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, committed.Status);
        var gateway = harness.Composition.Document.SemanticModel.Elements.Single(element =>
            element.Id == BpmnDemoPipeline.ExclusiveGatewayId);
        Assert.Equal(longName, gateway.Properties["BPMN.Name"].TextValue);
        var projected = Assert.Single(harness.State.ProjectedGraph!.Labels, label =>
            label.Source.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId);
        Assert.Equal(longName, projected.Text);
        var lines = GatewayLabels(harness.Scene);
        Assert.True(lines.Length >= 2);
        Assert.Equal(longName, string.Join(' ', lines
            .OrderBy(static item => item.Bounds.Top)
            .Select(static item => item.Geometry.Content)));
        Assert.All(lines, line =>
        {
            Assert.Equal(Center(originalBody.Bounds).X,
                line.Transform.TransformPoint(line.Geometry.TextAnchor).X,
                precision: 8);
            Assert.True(line.Bounds.Top >= originalBody.Bounds.Bottom + 8d);
            Assert.NotNull(line.Clip);
            Assert.True(line.Clip!.Value.Top >= originalBody.Bounds.Bottom + 8d);
        });
        Assert.Equal(originalBody.Bounds, NodeBody(harness.Scene).Bounds);
        var updatedLayout = Assert.IsType<Contracts.Layout.LayoutResult>(
            harness.State.LayoutResult);
        var updatedRouting = Assert.IsType<RoutingResult>(harness.State.RoutingResult);
        Assert.Equal(originalLayout.AlgorithmId, updatedLayout.AlgorithmId);
        Assert.Equal(originalLayout.Computation, updatedLayout.Computation);
        Assert.Equal(
            originalRouting.Routes.AsEnumerable(),
            updatedRouting.Routes.AsEnumerable());
        Assert.Equal(originalHistory.EntryCount + 1, harness.State.HistoryStatus.EntryCount);

        var reviewTaskLabel = Assert.Single(harness.Scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            item.Origin.SemanticElementId == BpmnDemoPipeline.TaskId);
        var reviewTaskBody = NodeBody(harness.Scene, BpmnDemoPipeline.TaskVisualId);
        Assert.True(reviewTaskBody.Bounds.Contains(Center(reviewTaskLabel.Bounds)));
        Assert.DoesNotContain(harness.Scene.Items, item =>
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            (item.Origin.SemanticElementId == BpmnDemoPipeline.StartEventId ||
             item.Origin.SemanticElementId == BpmnDemoPipeline.EndEventId));

        await harness.Host.UndoAsync();
        Assert.Equal("Approved?", GatewayName(harness));
        Assert.Equal("Approved?", Assert.Single(GatewayLabels(harness.Scene)).Geometry.Content);

        await harness.Host.RedoAsync();
        Assert.Equal(longName, GatewayName(harness));
        Assert.True(GatewayLabels(harness.Scene).Length >= 2);
    }

    [Fact]
    public async Task MovePreviewTranslatesExternalLabelWithoutPersistentPipelineWork()
    {
        await using var harness = await HostHarness.CreateAsync();
        await using var interaction = new Canvas2DInteractionController(harness.Session);
        var initialDocument = harness.Composition.Document.CaptureSnapshot();
        var initialState = harness.State;
        var initialScene = harness.Scene;
        var body = NodeBody(initialScene);
        var label = Assert.Single(GatewayLabels(initialScene));
        var start = Center(body.Bounds);
        var delta = new VectorD(37d, 29d);
        var finish = start + delta;

        var pressed = await interaction.PointerPressedAsync(Pointer(
            8221,
            initialScene,
            start,
            button: 0,
            buttons: 1));
        var moved = await interaction.PointerMovedAsync(Pointer(
            8221,
            Assert.IsType<Canvas2DScene>(pressed.SessionState.CurrentScene),
            finish,
            buttons: 1));

        Assert.Equal(Canvas2DInteractionStatus.Updated, moved.Status);
        var previewScene = Assert.IsType<Canvas2DScene>(moved.SessionState.CurrentScene);
        var previewBody = Assert.Single(previewScene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Path &&
            item.Geometry.Points.Length == 4 &&
            item.Origin.VisualStateId == BpmnDemoPipeline.ExclusiveGatewayVisualId &&
            item.Origin.StableSourceKey?.StartsWith("move-preview:",
                StringComparison.Ordinal) == true);
        var previewLabel = Assert.Single(previewScene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Overlay &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            item.Origin.VisualStateId == BpmnDemoPipeline.ExclusiveGatewayVisualId &&
            item.Origin.StableSourceKey?.StartsWith("move-preview:",
                StringComparison.Ordinal) == true);
        Assert.Equal(body.Bounds.Translate(delta), previewBody.Bounds);
        Assert.Equal(label.Bounds.Translate(delta), previewLabel.Bounds);
        AssertOutsideBelow(previewBody.Bounds, previewLabel.Bounds);
        Assert.Same(initialState.ProjectedGraph, moved.SessionState.ProjectedGraph);
        Assert.Same(initialState.LayoutResult, moved.SessionState.LayoutResult);
        Assert.Same(initialState.RoutingResult, moved.SessionState.RoutingResult);
        Assert.Equal(initialDocument, harness.Composition.Document.CaptureSnapshot());
        Assert.Equal(initialState.DocumentRevision, moved.SessionState.DocumentRevision);
        Assert.Equal(initialState.HistoryStatus, moved.SessionState.HistoryStatus);

        var released = await interaction.PointerReleasedAsync(Pointer(
            8221,
            previewScene,
            finish,
            button: 0));
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(Canvas2DInteractionStatus.Committed, released.Status);
        AssertOutsideBelow(NodeBody(harness.Scene).Bounds, LabelBounds(GatewayLabels(harness.Scene)));
        Assert.Equal(initialState.HistoryStatus.EntryCount + 1,
            harness.State.HistoryStatus.EntryCount);
        Assert.Equal("Approved?", GatewayName(harness));
    }

    [Fact]
    public async Task AnchorRedistributionChangesOnlyAnchorsAndRoutesNotExternalLabelGeometry()
    {
        await using var harness = await HostHarness.CreateAsync();
        var initial = harness.State;
        var initialDocument = harness.Composition.Document.CaptureSnapshot();
        var initialLabelBounds = LabelBounds(GatewayLabels(harness.Scene));
        var initialBodyBounds = NodeBody(harness.Scene).Bounds;
        var outgoingIds = new[]
        {
            BpmnDemoPipeline.ThirdSequenceFlowId,
            BpmnDemoPipeline.FourthSequenceFlowId,
        };
        var initialRoutes = outgoingIds.ToDictionary(id => id, id => Route(harness, id));
        var initialReferences = outgoingIds.ToDictionary(
            id => id,
            id => FlowVisual(harness, id).SourceAnchorId);

        var added = await harness.Session.ExecuteAsync(new AddConnectorAnchorCommand(
            initialDocument.DocumentId,
            initial.DocumentRevision,
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            new ConnectorAnchorId("bpmn:demo:anchor:gateway:source:extra"),
            ConnectorAnchorSide.Right,
            ConnectorAnchorRole.Source,
            insertionIndex: 2));
        Assert.True(added.IsCommitted);
        await harness.Session.WaitForIdleAsync();

        Assert.Equal(initialBodyBounds, NodeBody(harness.Scene).Bounds);
        Assert.Equal(initialLabelBounds, LabelBounds(GatewayLabels(harness.Scene)));
        Assert.Equal("Approved?", GatewayName(harness));
        Assert.Equal(initial.HistoryStatus.EntryCount + 1, harness.State.HistoryStatus.EntryCount);
        Assert.Equal(4, GatewayVisual(harness).ConnectorAnchors.Length);
        Assert.All(outgoingIds, id =>
        {
            Assert.Equal(initialReferences[id], FlowVisual(harness, id).SourceAnchorId);
            Assert.NotEqual(initialRoutes[id].SourceAnchor, Route(harness, id).SourceAnchor);
            Assert.Equal(Route(harness, id).Path[^1], Route(harness, id).DestinationAnchor);
        });

        await harness.Host.UndoAsync();
        Assert.Equal(initialLabelBounds, LabelBounds(GatewayLabels(harness.Scene)));
        Assert.Equal(3, GatewayVisual(harness).ConnectorAnchors.Length);
        Assert.All(outgoingIds, id => Assert.Equal(initialRoutes[id], Route(harness, id)));
    }

    private static Canvas2DSceneItem[] GatewayLabels(Canvas2DScene scene) => scene.Items
        .Where(item =>
            item.Layer == Canvas2DSceneLayer.Label &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            item.Origin.SemanticElementId == BpmnDemoPipeline.ExclusiveGatewayId)
        .OrderBy(static item => item.Bounds.Top)
        .ThenBy(static item => item.Id.Value, StringComparer.Ordinal)
        .ToArray();

    private static Canvas2DSceneItem NodeBody(Canvas2DScene scene) =>
        NodeBody(scene, BpmnDemoPipeline.ExclusiveGatewayVisualId);

    private static Canvas2DSceneItem NodeBody(Canvas2DScene scene, VisualStateId visualStateId) =>
        Assert.Single(scene.Items, item =>
            item.Layer == Canvas2DSceneLayer.Content &&
            item.Origin.VisualStateId == visualStateId &&
            item.Origin.ProjectedObjectId is { } projectedObjectId &&
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(projectedObjectId, "node"));

    private static string GatewayName(HostHarness harness) => harness.Composition.Document
        .SemanticModel.Elements.Single(element =>
            element.Id == BpmnDemoPipeline.ExclusiveGatewayId)
        .Properties["BPMN.Name"].TextValue;

    private static VisualStateSnapshot GatewayVisual(HostHarness harness) =>
        harness.Composition.Document.VisualModel.VisualStates.Single(visual =>
            visual.Id == BpmnDemoPipeline.ExclusiveGatewayVisualId);

    private static VisualStateSnapshot FlowVisual(
        HostHarness harness,
        SemanticElementId semanticId) =>
        harness.Composition.Document.VisualModel.VisualStates.Single(visual =>
            visual.SemanticElementId == semanticId);

    private static RoutedConnectorGeometry Route(HostHarness harness, SemanticElementId id)
    {
        var edge = harness.State.ProjectedGraph!.Edges.Single(candidate =>
            candidate.Source.SemanticElementId == id);
        return harness.State.RoutingResult!.Routes.Single(route =>
            route.ProjectedEdgeId == edge.Id);
    }

    private static RectD LabelBounds(IEnumerable<Canvas2DSceneItem> labels)
    {
        var copy = labels.ToArray();
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
