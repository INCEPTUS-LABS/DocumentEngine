using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Deletion;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN314DeletionIntegrationTests
{
    [Fact]
    public async Task EverySupportedFlowNodeAndSequenceFlowExposePluginDeletionActions()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        var document = harness.Composition.Document.CaptureSnapshot();
        var supportedTypes = new[]
        {
            BpmnSemanticTypes.StartEvent,
            BpmnSemanticTypes.Task,
            BpmnSemanticTypes.ExclusiveGateway,
            BpmnSemanticTypes.ParallelGateway,
            BpmnSemanticTypes.InclusiveGateway,
            BpmnSemanticTypes.EndEvent,
        };
        foreach (var typeId in supportedTypes)
        {
            var element = document.SemanticModel.Elements.First(candidate =>
                candidate.TypeId == typeId &&
                document.SemanticModel.GetScope(candidate.Id).Id ==
                    document.SemanticModel.RootScopeId);
            var visual = document.VisualModel.VisualStates.Single(candidate =>
                candidate.SemanticElementId == element.Id);
            var body = NodeBody(harness.Scene, visual.Id);

            await harness.Pointer.ContextMenuDocumentPointAsync(
                harness.Scene,
                Center(body.Bounds));

            var action = Assert.IsType<
                Inceptus.DocumentEngine.Bpmn.Blazor.Presentation.DocumentCanvasDeletionContextAction>(
                harness.Host.CaptureState().ContextMenu?.DeletionAction);
            Assert.Equal(DiagramDeletionTargetKind.Element, action.TargetKind);
            Assert.Equal(element.Id, action.SemanticId);
            harness.Host.CloseContextMenu();
        }

        var connector = ConnectorPath(
            harness.Scene,
            BpmnDemoPipeline.ThirdSequenceFlowVisualId);
        await harness.Pointer.ContextMenuDocumentPointAsync(
            harness.Scene,
            SegmentMiddle(connector));
        var connectorAction = Assert.IsType<
            Inceptus.DocumentEngine.Bpmn.Blazor.Presentation.DocumentCanvasDeletionContextAction>(
            harness.Host.CaptureState().ContextMenu?.DeletionAction);
        Assert.Equal(DiagramDeletionTargetKind.Connection, connectorAction.TargetKind);
        Assert.Equal(BpmnDemoPipeline.ThirdSequenceFlowId, connectorAction.SemanticId);
    }

    [Fact]
    public async Task DeleteNodeUndoRedoPreservesExactEffectiveGeometryAndAtomicCascade()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        var manualLabel = new NodeLabelVisualOverride(200d, 104d, 260d, 84d);
        var labelUpdate = await harness.Session.ExecuteAsync(
            new UpdateNodeLabelVisualOverrideCommand(
                harness.Composition.Document.DocumentId,
                harness.Composition.Document.Revision,
                BpmnDemoPipeline.ExclusiveGatewayVisualId,
                manualLabel));
        Assert.True(labelUpdate.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        var beforeDocument = harness.Composition.Document.CaptureSnapshot();
        var beforeBounds = NodeBounds(harness.Scene, beforeDocument);
        var beforeHistory = harness.State.HistoryStatus;
        var incidentIds = beforeDocument.SemanticModel.Relationships
            .Where(relationship =>
                relationship.SourceId == BpmnDemoPipeline.ExclusiveGatewayId ||
                relationship.TargetId == BpmnDemoPipeline.ExclusiveGatewayId)
            .Select(static relationship => relationship.Id)
            .ToArray();
        var body = NodeBody(
            harness.Scene,
            BpmnDemoPipeline.ExclusiveGatewayVisualId);
        await harness.Pointer.ContextMenuDocumentPointAsync(
            harness.Scene,
            Center(body.Bounds));

        var deleted = await harness.Host.ExecuteDeletionContextActionAsync();

        Assert.True(deleted?.IsCommitted);
        var afterDocument = harness.Composition.Document.CaptureSnapshot();
        Assert.Equal(beforeDocument.Revision.Increment(), afterDocument.Revision);
        Assert.Equal(beforeHistory.EntryCount + 1, harness.State.HistoryStatus.EntryCount);
        Assert.False(afterDocument.SemanticModel.TryGetElement(
            BpmnDemoPipeline.ExclusiveGatewayId,
            out _));
        Assert.All(incidentIds, relationshipId =>
            Assert.False(afterDocument.SemanticModel.TryGetRelationship(
                relationshipId,
                out _)));
        var afterBounds = NodeBounds(harness.Scene, afterDocument);
        Assert.DoesNotContain(
            BpmnDemoPipeline.ExclusiveGatewayVisualId,
            afterBounds.Keys);
        AssertSurvivingBounds(beforeBounds, afterBounds);

        await harness.Host.UndoAsync();
        var undoDocument = harness.Composition.Document.CaptureSnapshot();
        Assert.True(undoDocument.SemanticModel.TryGetElement(
            BpmnDemoPipeline.ExclusiveGatewayId,
            out _));
        Assert.All(incidentIds, relationshipId =>
            Assert.True(undoDocument.SemanticModel.TryGetRelationship(
                relationshipId,
                out _)));
        AssertBoundsEqual(beforeBounds, NodeBounds(harness.Scene, undoDocument));
        Assert.True(NodeLabelVisualOverride.TryRead(
            undoDocument.VisualModel.VisualStates.Single(visual =>
                visual.Id == BpmnDemoPipeline.ExclusiveGatewayVisualId).Properties,
            out var restoredLabel));
        Assert.Equal(manualLabel, restoredLabel);

        await harness.Host.RedoAsync();
        var redoDocument = harness.Composition.Document.CaptureSnapshot();
        Assert.False(redoDocument.SemanticModel.TryGetElement(
            BpmnDemoPipeline.ExclusiveGatewayId,
            out _));
        AssertBoundsEqual(afterBounds, NodeBounds(harness.Scene, redoDocument));
    }

    [Fact]
    public async Task DeleteConnectionPreservesAndFreesAnchorsForExactN2Reuse()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        var automaticConnector = ConnectorPath(
            harness.Scene,
            BpmnDemoPipeline.ThirdSequenceFlowVisualId);
        var automaticRoute = automaticConnector.Geometry.Points
            .Select(automaticConnector.Transform.TransformPoint)
            .ToArray();
        var source = automaticRoute[0];
        var target = automaticRoute[^1];
        var bend = new PointD(
            (source.X + target.X) / 2d,
            Math.Max(0d, Math.Min(source.Y, target.Y) - 60d));
        var manualRoute = new[] { source, bend, target };
        var routeUpdate = await harness.Session.ExecuteAsync(
            new UpdateConnectionRouteCommand(
                harness.Composition.Document.DocumentId,
                harness.Composition.Document.Revision,
                BpmnDemoPipeline.ThirdSequenceFlowVisualId,
                manualRoute));
        Assert.True(routeUpdate.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        var beforeDocument = harness.Composition.Document.CaptureSnapshot();
        var beforeBounds = NodeBounds(harness.Scene, beforeDocument);
        var relationship = beforeDocument.SemanticModel.Relationships.Single(candidate =>
            candidate.Id == BpmnDemoPipeline.ThirdSequenceFlowId);
        var visual = beforeDocument.VisualModel.VisualStates.Single(candidate =>
            candidate.Id == BpmnDemoPipeline.ThirdSequenceFlowVisualId);
        var connector = ConnectorPath(harness.Scene, visual.Id);
        await harness.Pointer.ContextMenuDocumentPointAsync(
            harness.Scene,
            SegmentMiddle(connector));

        var deleted = await harness.Host.ExecuteDeletionContextActionAsync();

        Assert.True(deleted?.IsCommitted);
        var afterDelete = harness.Composition.Document.CaptureSnapshot();
        Assert.False(afterDelete.SemanticModel.TryGetRelationship(relationship.Id, out _));
        Assert.Contains(afterDelete.VisualModel.VisualStates.SelectMany(
            static state => state.ConnectorAnchors), anchor => anchor.Id == visual.SourceAnchorId);
        Assert.Contains(afterDelete.VisualModel.VisualStates.SelectMany(
            static state => state.ConnectorAnchors), anchor => anchor.Id == visual.TargetAnchorId);
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            afterDelete.VisualModel,
            visual.SourceAnchorId!));
        Assert.False(ConnectorAnchorOccupancy.IsOccupied(
            afterDelete.VisualModel,
            visual.TargetAnchorId!));
        AssertBoundsEqual(beforeBounds, NodeBounds(harness.Scene, afterDelete));

        Assert.True((await harness.Session.UndoAsync()).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        var restored = harness.Composition.Document.CaptureSnapshot();
        Assert.Equal(
            manualRoute.AsEnumerable(),
            restored.VisualModel.VisualStates.Single(candidate =>
                candidate.Id == visual.Id).Route.AsEnumerable());
        Assert.True((await harness.Session.RedoAsync()).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        afterDelete = harness.Composition.Document.CaptureSnapshot();

        var replacementId = new SemanticElementId("test:n314:reuse-flow");
        var replacementVisualId = new VisualStateId("test:n314:reuse-flow:visual");
        var reused = await harness.Session.ExecuteAsync(new CreateBpmnSequenceFlowCommand(
            afterDelete.DocumentId,
            afterDelete.Revision,
            replacementId,
            replacementVisualId,
            relationship.SourceId,
            relationship.TargetId,
            visual.SourceAnchorId!,
            visual.TargetAnchorId!));
        await harness.Session.WaitForIdleAsync();

        Assert.True(reused.IsCommitted);
        var afterReuse = harness.Composition.Document.CaptureSnapshot();
        Assert.True(afterReuse.SemanticModel.TryGetRelationship(replacementId, out _));
        Assert.Equal(1, ConnectorAnchorOccupancy.CountEndpointReferences(
            afterReuse.VisualModel,
            visual.SourceAnchorId!));
        Assert.Equal(1, ConnectorAnchorOccupancy.CountEndpointReferences(
            afterReuse.VisualModel,
            visual.TargetAnchorId!));
        AssertBoundsEqual(beforeBounds, NodeBounds(harness.Scene, afterReuse));
    }

    private static Canvas2DSceneItem NodeBody(
        Canvas2DScene scene,
        VisualStateId visualStateId) => Assert.Single(scene.Items, item =>
        item.Layer == Canvas2DSceneLayer.Content &&
        item.Origin.VisualStateId == visualStateId &&
        item.IsVisible &&
        item.HitTestPolicy.Mode !=
            Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DHitTestMode.None);

    private static Canvas2DSceneItem ConnectorPath(
        Canvas2DScene scene,
        VisualStateId visualStateId) => Assert.Single(scene.Items, item =>
        item.Layer == Canvas2DSceneLayer.Connector &&
        item.Origin.VisualStateId == visualStateId &&
        item.Geometry.Kind ==
            Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneGeometryKind.Path &&
        !item.Metadata.ContainsKey(
            Inceptus.DocumentEngine.Canvas2D.Interaction.Canvas2DConnectorArrowMetadata
                .TargetArrow) &&
        item.HitTestPolicy.Mode !=
            Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DHitTestMode.None);

    private static PointD SegmentMiddle(Canvas2DSceneItem connector)
    {
        var start = connector.Transform.TransformPoint(connector.Geometry.Points[0]);
        var end = connector.Transform.TransformPoint(connector.Geometry.Points[1]);
        return new PointD((start.X + end.X) / 2d, (start.Y + end.Y) / 2d);
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
            .ToDictionary(item => item.Origin.VisualStateId!, static item => item.Bounds);
    }

    private static void AssertSurvivingBounds(
        Dictionary<VisualStateId, RectD> before,
        Dictionary<VisualStateId, RectD> after)
    {
        foreach (var (visualStateId, bounds) in after)
        {
            Assert.Equal(before[visualStateId], bounds);
        }
    }

    private static void AssertBoundsEqual(
        Dictionary<VisualStateId, RectD> expected,
        Dictionary<VisualStateId, RectD> actual)
    {
        Assert.Equal(expected.Keys, actual.Keys);
        foreach (var (visualStateId, bounds) in expected)
        {
            Assert.Equal(bounds, actual[visualStateId]);
        }
    }

    private static PointD Center(RectD bounds) => new(
        bounds.Left + (bounds.Width / 2d),
        bounds.Top + (bounds.Height / 2d));
}
