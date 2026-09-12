using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN7BpmnIntermediateEventIntegrationTests
{
    private static readonly SemanticElementId SourceTaskId = new("n7:source-task");
    private static readonly SemanticElementId MessageThrowId = new("n7:message-throw");
    private static readonly SemanticElementId SignalCatchId = new("n7:signal-catch");
    private static readonly SemanticElementId SignalThrowId = new("n7:signal-throw");
    private static readonly SemanticElementId TargetTaskId = new("n7:target-task");

    private static readonly VisualStateId SourceTaskVisualId = new("n7:source-task:visual");
    private static readonly VisualStateId MessageThrowVisualId =
        new("n7:message-throw:visual");
    private static readonly VisualStateId SignalCatchVisualId =
        new("n7:signal-catch:visual");
    private static readonly VisualStateId SignalThrowVisualId =
        new("n7:signal-throw:visual");
    private static readonly VisualStateId TargetTaskVisualId = new("n7:target-task:visual");

    [Fact]
    public async Task CompleteN7VerticalSliceRunsThroughTheEditingSessionPipeline()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        await CreateChainAsync(harness);
        var document = harness.Composition.Document.CaptureSnapshot();
        var graph = Assert.IsType<ProjectedGraph>(harness.State.ProjectedGraph);

        var expectedTypes = new Dictionary<SemanticElementId, SemanticTypeId>
        {
            [MessageThrowId] = BpmnSemanticTypes.MessageThrowEvent,
            [SignalCatchId] = BpmnSemanticTypes.SignalCatchEvent,
            [SignalThrowId] = BpmnSemanticTypes.SignalThrowEvent,
        };
        foreach (var (elementId, typeId) in expectedTypes)
        {
            var element = Assert.Single(document.SemanticModel.Elements, candidate =>
                candidate.Id == elementId);
            Assert.Equal(typeId, element.TypeId);
            Assert.Equal(
                [BpmnSemanticProperties.Description, BpmnSemanticProperties.Name],
                element.Properties.Keys.Order(StringComparer.Ordinal));
            var visual = Assert.Single(document.VisualModel.VisualStates, candidate =>
                candidate.SemanticElementId == elementId);
            Assert.Equal(new SizeD(36d, 36d), visual.Size);
            Assert.Equal(VisualPlacementMode.Pinned, visual.PlacementMode);
            Assert.Equal(2, visual.ConnectorAnchors.Length);

            var node = Assert.Single(graph.Nodes, candidate =>
                candidate.Source.SemanticElementId == elementId);
            var label = Assert.Single(graph.Labels, candidate => candidate.OwnerId == node.Id);
            Assert.Equal(NodeLabelPlacementKind.OutsideBelow, label.NodePlacement!.Kind);
            Assert.Equal(NodeLabelInteractionPolicy.MoveAndResize,
                label.NodeInteractionPolicy);
            var body = Assert.Single(harness.Scene.Items, item =>
                item.Layer == Canvas2DSceneLayer.Content &&
                item.Origin.SemanticElementId == elementId &&
                item.Origin.Categories.HasFlag(
                    Canvas2DSceneOriginCategory.ProjectedRuntimeObject));
            Assert.Equal(Canvas2DSceneGeometryKind.Ellipse, body.Geometry.Kind);
            var markers = harness.Scene.Items.Where(item =>
                item.Layer == Canvas2DSceneLayer.Decoration &&
                item.Origin.SemanticElementId == elementId &&
                item.Origin.Categories.HasFlag(
                    Canvas2DSceneOriginCategory.RegisteredExtension)).ToArray();
            Assert.NotEmpty(markers);
            Assert.All(markers, marker =>
                Assert.Equal(Canvas2DHitTestMode.None, marker.HitTestPolicy.Mode));
        }

        var n7Flows = document.SemanticModel.Relationships.Where(relationship =>
            relationship.Id.Value.StartsWith("n7:flow:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, n7Flows.Length);
        Assert.All(n7Flows, relationship =>
        {
            var edge = Assert.Single(graph.Edges, candidate =>
                candidate.Source.SemanticElementId == relationship.Id);
            Assert.Contains(harness.State.RoutingResult!.Routes, geometry =>
                geometry.ProjectedEdgeId == edge.Id);
            Assert.DoesNotContain(edge.Id, harness.State.RoutingResult.NoRouteEdgeIds);
        });

        foreach (var elementId in expectedTypes.Keys)
        {
            var updated = await harness.Session.ExecuteAsync(
                new UpdateSemanticElementPropertyCommand(
                    document.DocumentId,
                    harness.Composition.Document.Revision,
                    elementId,
                    BpmnSemanticProperties.Name,
                    PropertyValue.FromText($"Updated {elementId.Value}")));
            Assert.True(updated.IsCommitted);
        }

        var labelOverride = new NodeLabelVisualOverride(0d, 54d, 130d, 44d);
        Assert.True((await harness.Session.ExecuteAsync(
            new UpdateNodeLabelVisualOverrideCommand(
                document.DocumentId,
                harness.Composition.Document.Revision,
                SignalCatchVisualId,
                labelOverride))).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.True(NodeLabelVisualOverride.TryRead(
            harness.Composition.Document.VisualModel.VisualStates.Single(visual =>
                visual.Id == SignalCatchVisualId).Properties,
            out var storedOverride));
        Assert.Equal(labelOverride, storedOverride);
        await harness.Host.UndoAsync();
        Assert.False(NodeLabelVisualOverride.TryRead(
            harness.Composition.Document.VisualModel.VisualStates.Single(visual =>
                visual.Id == SignalCatchVisualId).Properties,
            out _));
        await harness.Host.RedoAsync();
        Assert.True(NodeLabelVisualOverride.TryRead(
            harness.Composition.Document.VisualModel.VisualStates.Single(visual =>
                visual.Id == SignalCatchVisualId).Properties,
            out storedOverride));
        Assert.Equal(labelOverride, storedOverride);
        Assert.True((await harness.Session.ExecuteAsync(
            new UpdateNodeLabelVisualOverrideCommand(
                document.DocumentId,
                harness.Composition.Document.Revision,
                SignalCatchVisualId,
                null))).IsCommitted);

        var beforeGeometryEdit = harness.Composition.Document.CaptureSnapshot();
        var signalVisual = beforeGeometryEdit.VisualModel.VisualStates.Single(visual =>
            visual.Id == SignalCatchVisualId);
        Assert.True((await harness.Session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            beforeGeometryEdit.Revision,
            SignalCatchVisualId,
            signalVisual.Position + new VectorD(12d, 18d),
            signalVisual.PlacementMode))).IsCommitted);
        var afterMove = harness.Composition.Document.CaptureSnapshot();
        var moved = afterMove.VisualModel.VisualStates.Single(visual =>
            visual.Id == SignalCatchVisualId);
        Assert.True((await harness.Session.ExecuteAsync(new ResizeVisualStateCommand(
            document.DocumentId,
            afterMove.Revision,
            SignalCatchVisualId,
            new RectD(moved.Position.X, moved.Position.Y, 44d, 42d),
            moved.PlacementMode))).IsCommitted);
        await harness.Session.WaitForIdleAsync();
        Assert.Equal(
            beforeGeometryEdit.VisualModel.VisualStates.Single(visual =>
                visual.Id == SourceTaskVisualId),
            harness.Composition.Document.VisualModel.VisualStates.Single(visual =>
                visual.Id == SourceTaskVisualId));
        Assert.NotNull(harness.State.CurrentScene);
        Assert.NotNull(harness.State.RoutingResult);

        var validation = Assert.IsType<ValidationSnapshot>(await harness.Host.ValidateAsync());
        Assert.Contains(validation.Issues, issue =>
            issue.Code == BpmnModelValidationCodes.NodeUnreachableFromStart &&
            issue.Message.StartsWith("Message Throw Event", StringComparison.Ordinal));
        Assert.Contains(validation.Issues, issue =>
            issue.Code == BpmnModelValidationCodes.NodeUnreachableFromStart &&
            issue.Message.StartsWith("Signal Catch Event", StringComparison.Ordinal));
        Assert.Contains(validation.Issues, issue =>
            issue.Code == BpmnModelValidationCodes.NodeUnreachableFromStart &&
            issue.Message.StartsWith("Signal Throw Event", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteUndoRedoCascadesEveryN7EventAndItsIncidentFlowsExactly()
    {
        await using var harness =
            await PhaseM31BpmnPropertiesIntegrationTests.HostHarness.CreateAsync();
        await CreateChainAsync(harness);
        var rows = new[]
        {
            (MessageThrowId, MessageThrowVisualId),
            (SignalCatchId, SignalCatchVisualId),
            (SignalThrowId, SignalThrowVisualId),
        };

        foreach (var (elementId, visualId) in rows)
        {
            var before = harness.Composition.Document.CaptureSnapshot();
            var incidentIds = before.SemanticModel.Relationships.Where(relationship =>
                    relationship.SourceId == elementId || relationship.TargetId == elementId)
                .Select(static relationship => relationship.Id)
                .ToHashSet();
            Assert.Equal(2, incidentIds.Count);

            var deleted = await harness.Session.ExecuteAsync(new DeleteBpmnFlowNodeCommand(
                before.DocumentId,
                before.Revision,
                elementId,
                visualId));
            Assert.True(deleted.IsCommitted);
            await harness.Session.WaitForIdleAsync();
            var afterDelete = harness.Composition.Document.CaptureSnapshot();
            Assert.False(afterDelete.SemanticModel.TryGetElement(elementId, out _));
            Assert.False(afterDelete.VisualModel.TryGetVisualState(visualId, out _));
            Assert.DoesNotContain(afterDelete.SemanticModel.Relationships, relationship =>
                incidentIds.Contains(relationship.Id));

            await harness.Host.UndoAsync();
            var restored = harness.Composition.Document.CaptureSnapshot();
            AssertAuthoritativeStateEqual(before, restored);
            await harness.Host.RedoAsync();
            Assert.False(harness.Composition.Document.SemanticModel.TryGetElement(
                elementId,
                out _));
            await harness.Host.UndoAsync();
            AssertAuthoritativeStateEqual(before,
                harness.Composition.Document.CaptureSnapshot());
        }
    }

    private static async Task CreateChainAsync(
        PhaseM31BpmnPropertiesIntegrationTests.HostHarness harness)
    {
        var documentId = harness.Composition.Document.DocumentId;
        await ExecuteAsync(harness, revision => new CreateBpmnTaskCommand(
            documentId, revision, SourceTaskId, SourceTaskVisualId,
            new PointD(40d, 690d), new SizeD(120d, 80d), "N7_SOURCE", "N7 source",
            201, VisualPlacementMode.Pinned, "N7 source task."));
        await ExecuteAsync(harness, revision => new CreateBpmnMessageThrowEventCommand(
            documentId, revision, MessageThrowId, MessageThrowVisualId,
            new PointD(240d, 712d), new SizeD(36d, 36d), "Send notification",
            VisualPlacementMode.Pinned, "Message throw event."));
        await ExecuteAsync(harness, revision => new CreateBpmnSignalCatchEventCommand(
            documentId, revision, SignalCatchId, SignalCatchVisualId,
            new PointD(390d, 712d), new SizeD(36d, 36d), "Cancellation signal",
            VisualPlacementMode.Pinned, "Signal catch event."));
        await ExecuteAsync(harness, revision => new CreateBpmnSignalThrowEventCommand(
            documentId, revision, SignalThrowId, SignalThrowVisualId,
            new PointD(540d, 712d), new SizeD(36d, 36d), "Cancellation raised",
            VisualPlacementMode.Pinned, "Signal throw event."));
        await ExecuteAsync(harness, revision => new CreateBpmnTaskCommand(
            documentId, revision, TargetTaskId, TargetTaskVisualId,
            new PointD(700d, 690d), new SizeD(120d, 80d), "N7_TARGET", "N7 target",
            202, VisualPlacementMode.Pinned, "N7 target task."));

        var nodes = new[]
        {
            (SourceTaskId, SourceTaskVisualId),
            (MessageThrowId, MessageThrowVisualId),
            (SignalCatchId, SignalCatchVisualId),
            (SignalThrowId, SignalThrowVisualId),
            (TargetTaskId, TargetTaskVisualId),
        };
        foreach (var (elementId, visualId) in nodes)
        {
            await ExecuteAsync(harness, revision => new AddConnectorAnchorCommand(
                documentId,
                revision,
                visualId,
                TargetAnchor(elementId),
                ConnectorAnchorSide.Left,
                ConnectorAnchorRole.Target,
                0));
            await ExecuteAsync(harness, revision => new AddConnectorAnchorCommand(
                documentId,
                revision,
                visualId,
                SourceAnchor(elementId),
                ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Source,
                0));
        }

        var chain = new[]
        {
            (SourceTaskId, MessageThrowId),
            (MessageThrowId, SignalCatchId),
            (SignalCatchId, SignalThrowId),
            (SignalThrowId, TargetTaskId),
        };
        foreach (var (sourceId, targetId) in chain)
        {
            var flowId = new SemanticElementId($"n7:flow:{sourceId.Value}-to-{targetId.Value}");
            await ExecuteAsync(harness, revision => new CreateBpmnSequenceFlowCommand(
                documentId,
                revision,
                flowId,
                new VisualStateId($"{flowId.Value}:visual"),
                sourceId,
                targetId,
                SourceAnchor(sourceId),
                TargetAnchor(targetId)));
        }

        await harness.Session.WaitForIdleAsync();
        Assert.NotNull(harness.State.ProjectedGraph);
        Assert.NotNull(harness.State.LayoutResult);
        Assert.NotNull(harness.State.RoutingResult);
        Assert.NotNull(harness.State.CurrentScene);
    }

    private static async Task ExecuteAsync(
        PhaseM31BpmnPropertiesIntegrationTests.HostHarness harness,
        Func<DocumentRevision, ICommand> command)
    {
        var result = await harness.Session.ExecuteAsync(command(
            harness.Composition.Document.Revision));
        Assert.True(result.IsCommitted, string.Join(" | ", result.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
    }

    private static ConnectorAnchorId SourceAnchor(SemanticElementId elementId) =>
        new($"{elementId.Value}:right:source");

    private static ConnectorAnchorId TargetAnchor(SemanticElementId elementId) =>
        new($"{elementId.Value}:left:target");

    private static void AssertAuthoritativeStateEqual(
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot expected,
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot actual)
    {
        Assert.Equal(expected.SemanticModel.ElementCount, actual.SemanticModel.ElementCount);
        Assert.All(expected.SemanticModel.Elements, expectedElement =>
            Assert.Equal(expectedElement, actual.SemanticModel.Elements.Single(candidate =>
                candidate.Id == expectedElement.Id)));
        Assert.Equal(expected.SemanticModel.RelationshipCount,
            actual.SemanticModel.RelationshipCount);
        Assert.All(expected.SemanticModel.Relationships, expectedRelationship =>
            Assert.Equal(expectedRelationship,
                actual.SemanticModel.Relationships.Single(candidate =>
                    candidate.Id == expectedRelationship.Id)));
        Assert.Equal(expected.VisualModel.Count, actual.VisualModel.Count);
        Assert.All(expected.VisualModel.VisualStates, expectedVisual =>
            Assert.Equal(expectedVisual, actual.VisualModel.VisualStates.Single(candidate =>
                candidate.Id == expectedVisual.Id)));
    }
}
