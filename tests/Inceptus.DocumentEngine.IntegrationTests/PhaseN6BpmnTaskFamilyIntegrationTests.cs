using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Validation;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN6BpmnTaskFamilyIntegrationTests
{
    public static TheoryData<SemanticElementId, VisualStateId, SemanticTypeId>
        SpecializedDemoTasks => new()
    {
        {
            BpmnDemoPipeline.TaskId,
            BpmnDemoPipeline.TaskVisualId,
            BpmnSemanticTypes.UserTask
        },
        {
            BpmnDemoPipeline.PrepareShipmentTaskId,
            BpmnDemoPipeline.PrepareShipmentTaskVisualId,
            BpmnSemanticTypes.ManualTask
        },
        {
            BpmnDemoPipeline.NotifyCustomerTaskId,
            BpmnDemoPipeline.NotifyCustomerTaskVisualId,
            BpmnSemanticTypes.SendTask
        },
        {
            BpmnDemoPipeline.AwaitEventTaskId,
            BpmnDemoPipeline.AwaitEventTaskVisualId,
            BpmnSemanticTypes.ReceiveTask
        },
        {
            BpmnDemoPipeline.ProcessMessageTaskId,
            BpmnDemoPipeline.ProcessMessageTaskVisualId,
            BpmnSemanticTypes.ServiceTask
        },
    };

    [Fact]
    public async Task DemoProjectsRoutesAndValidatesEverySpecializedTaskAsATask()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        var snapshot = harness.Composition.Document.CaptureSnapshot();
        var graph = Assert.IsType<ProjectedGraph>(harness.State.ProjectedGraph);
        var expectedMarkers = new Dictionary<SemanticTypeId, int>
        {
            [BpmnSemanticTypes.UserTask] = 2,
            [BpmnSemanticTypes.ManualTask] = 1,
            [BpmnSemanticTypes.ServiceTask] = 2,
            [BpmnSemanticTypes.SendTask] = 2,
            [BpmnSemanticTypes.ReceiveTask] = 2,
        };

        foreach (var row in SpecializedDemoTasks)
        {
            var elementId = (SemanticElementId)row[0]!;
            var visualId = (VisualStateId)row[1]!;
            var typeId = (SemanticTypeId)row[2]!;
            Assert.True(snapshot.SemanticModel.TryGetElement(elementId, out var element));
            Assert.Equal(typeId, element!.TypeId);
            Assert.True(BpmnTaskSemanticTypes.IsTask(element.TypeId));
            Assert.Equal(
                [
                    BpmnSemanticProperties.Code,
                    BpmnSemanticProperties.Description,
                    BpmnSemanticProperties.ElementNumber,
                    BpmnSemanticProperties.Name,
                ],
                element.Properties.Keys.Order(StringComparer.Ordinal));

            var node = Assert.Single(graph.Nodes, candidate =>
                candidate.Source.SemanticElementId == elementId);
            var label = Assert.Single(graph.Labels, candidate => candidate.OwnerId == node.Id);
            Assert.Null(label.NodePlacement);
            Assert.Equal(NodeLabelInteractionPolicy.Fixed, label.NodeInteractionPolicy);
            var body = Assert.Single(harness.Scene.Items, item =>
                item.Layer == Canvas2DSceneLayer.Content &&
                item.Origin.VisualStateId == visualId &&
                item.Origin.Categories.HasFlag(
                    Canvas2DSceneOriginCategory.ProjectedRuntimeObject));
            Assert.Equal(Canvas2DSceneGeometryKind.Path, body.Geometry.Kind);
            var markers = harness.Scene.Items.Where(item =>
                item.Layer == Canvas2DSceneLayer.Decoration &&
                item.Origin.VisualStateId == visualId &&
                item.Origin.Categories.HasFlag(
                    Canvas2DSceneOriginCategory.RegisteredExtension)).ToArray();
            Assert.Equal(expectedMarkers[typeId], markers.Length);
            Assert.All(markers, marker =>
                Assert.Equal(Canvas2DHitTestMode.None, marker.HitTestPolicy.Mode));
            Assert.Contains(snapshot.SemanticModel.Relationships, relationship =>
                relationship.SourceId == elementId || relationship.TargetId == elementId);
        }

        var validation = Assert.IsType<ValidationSnapshot>(await harness.Host.ValidateAsync());
        Assert.Equal(13, validation.Issues.Length);
        Assert.DoesNotContain(validation.Issues, issue =>
            issue.Code == BpmnModelValidationCodes.EventBasedGatewayInvalidTarget ||
            issue.Code == BpmnModelValidationCodes
                .EventBasedGatewayMixedMessageReceptionModes ||
            issue.Code == BpmnModelValidationCodes.EventBasedTargetAdditionalIncoming);
        Assert.Contains(validation.Issues, issue =>
            issue.Message.StartsWith(
                "Receive Task 90 \"Await customer event\"",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReceiveTaskMoveResizeUndoRedoPreservesTaskIdentityAndRoutingReadiness()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        var before = harness.Composition.Document.CaptureSnapshot();
        var visual = Assert.Single(before.VisualModel.VisualStates, candidate =>
            candidate.Id == BpmnDemoPipeline.AwaitEventTaskVisualId);

        var moved = await harness.Session.ExecuteAsync(new MoveVisualStateCommand(
            before.DocumentId,
            before.Revision,
            visual.Id,
            visual.Position + new VectorD(18d, 12d),
            visual.PlacementMode));
        Assert.True(moved.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        var afterMove = harness.Composition.Document.CaptureSnapshot();
        var movedVisual = Assert.Single(afterMove.VisualModel.VisualStates, candidate =>
            candidate.Id == visual.Id);

        var resized = await harness.Session.ExecuteAsync(new ResizeVisualStateCommand(
            afterMove.DocumentId,
            afterMove.Revision,
            visual.Id,
            new RectD(
                movedVisual.Position.X,
                movedVisual.Position.Y,
                movedVisual.Size.Width + 24d,
                movedVisual.Size.Height + 12d),
            movedVisual.PlacementMode));
        Assert.True(resized.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(BpmnSemanticTypes.ReceiveTask, harness.Composition.Document.SemanticModel
            .Elements.Single(element => element.Id == BpmnDemoPipeline.AwaitEventTaskId).TypeId);
        Assert.NotNull(harness.State.RoutingResult);
        Assert.NotNull(harness.State.CurrentScene);

        await harness.Host.UndoAsync();
        await harness.Host.UndoAsync();
        var undone = harness.Composition.Document.CaptureSnapshot();
        AssertAuthoritativeStateEqual(before, undone);
        await harness.Host.RedoAsync();
        await harness.Host.RedoAsync();
        Assert.Equal(BpmnSemanticTypes.ReceiveTask, harness.Composition.Document.SemanticModel
            .Elements.Single(element => element.Id == BpmnDemoPipeline.AwaitEventTaskId).TypeId);
    }

    [Theory]
    [MemberData(nameof(SpecializedDemoTasks))]
    public async Task DeletionCascadesEachSpecializedTaskAndUndoRedoRestoresExactState(
        SemanticElementId elementId,
        VisualStateId visualStateId,
        SemanticTypeId typeId)
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        var before = harness.Composition.Document.CaptureSnapshot();
        var incidentIds = before.SemanticModel.Relationships
            .Where(relationship =>
                relationship.SourceId == elementId || relationship.TargetId == elementId)
            .Select(static relationship => relationship.Id)
            .ToHashSet();

        var deleted = await harness.Session.ExecuteAsync(new DeleteBpmnFlowNodeCommand(
            before.DocumentId,
            before.Revision,
            elementId,
            visualStateId));
        Assert.True(deleted.IsCommitted);
        await harness.Session.WaitForIdleAsync();
        var after = harness.Composition.Document.CaptureSnapshot();
        Assert.False(after.SemanticModel.TryGetElement(elementId, out _));
        Assert.False(after.VisualModel.TryGetVisualState(visualStateId, out _));
        Assert.DoesNotContain(after.SemanticModel.Relationships, relationship =>
            incidentIds.Contains(relationship.Id));

        await harness.Host.UndoAsync();
        var restored = harness.Composition.Document.CaptureSnapshot();
        AssertAuthoritativeStateEqual(before, restored);
        Assert.Equal(typeId, harness.Composition.Document.SemanticModel.Elements.Single(
            element => element.Id == elementId).TypeId);
        await harness.Host.RedoAsync();
        Assert.False(harness.Composition.Document.SemanticModel.TryGetElement(
            elementId,
            out _));
    }

    private static void AssertAuthoritativeStateEqual(
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot expected,
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot actual)
    {
        Assert.Equal(expected.SemanticModel.ElementCount, actual.SemanticModel.ElementCount);
        Assert.All(expected.SemanticModel.Elements, expectedElement =>
            Assert.Equal(expectedElement, actual.SemanticModel.Elements.Single(
                candidate => candidate.Id == expectedElement.Id)));
        Assert.Equal(
            expected.SemanticModel.RelationshipCount,
            actual.SemanticModel.RelationshipCount);
        Assert.All(expected.SemanticModel.Relationships, expectedRelationship =>
            Assert.Equal(expectedRelationship, actual.SemanticModel.Relationships.Single(
                candidate => candidate.Id == expectedRelationship.Id)));
        Assert.Equal(expected.VisualModel.Count, actual.VisualModel.Count);
        Assert.All(expected.VisualModel.VisualStates, expectedVisual =>
            Assert.Equal(expectedVisual, actual.VisualModel.VisualStates.Single(
                candidate => candidate.Id == expectedVisual.Id)));
    }
}
