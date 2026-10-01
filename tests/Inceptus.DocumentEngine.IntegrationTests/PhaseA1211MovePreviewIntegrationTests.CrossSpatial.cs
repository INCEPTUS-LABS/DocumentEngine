using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed partial class PhaseA1211MovePreviewIntegrationTests
{
    private static readonly string[] CrossSpatialShapes = ["manual", "service", "gateway", "event", "subprocess"];
    private static readonly (int Source, int Target)[] CrossSpatialDirections = [(2, 2), (2, 1), (1, 2), (2, 3), (3, 1)];

    public static IEnumerable<object[]> CrossSpatialPreviewCases =>
        from shape in CrossSpatialShapes
        from direction in CrossSpatialDirections
        select new object[] { shape, direction.Source, direction.Target, "short" };

    [Theory]
    [MemberData(nameof(CrossSpatialPreviewCases))]
    [InlineData("manual", 2, 1, "multiline")]
    [InlineData("event", 2, 1, "empty")]
    [InlineData("gateway", 2, 1, "manual")]
    public async Task CrossSpatialMoveKeepsEntirePreviewFamilyCoherent(
        string shape, int sourceIndex, int targetIndex, string label)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var visual = await PrepareCrossSpatialNodeAsync(test, shape, sourceIndex, label);
        var before = test.State;
        var snapshot = test.Snapshot;
        var scene = before.CurrentScene!;
        if (label == "empty")
        {
            Assert.DoesNotContain(scene.Items, item => item.Origin.VisualStateId == visual && item.Layer == Canvas2DSceneLayer.Label);
        }
        var body = scene.Items.Single(item => item.Origin.VisualStateId == visual && Canvas2DNodeBodyMetadata.IsNodeBody(item));
        var source = body.SpatialRegion!;
        var destination = CrossRegion(test, targetIndex);
        var origin = CrossCenter(body.Bounds);
        var target = destination.MapLocalToScene(new PointD(260, 170));
        var samples = new[]
        {
            origin + new VectorD(12, -8),
            new PointD(origin.X, source.Bounds.Top + 1),
            new PointD(origin.X, source.Bounds.Top - 1),
            target,
            origin + new VectorD(8, 4),
            target + new VectorD(7, 9),
            origin + new VectorD(-8, 6),
        };
        Canvas2DPointerInput Pointer(PointD point) => new(1211, scene.ViewportTransform.TransformPoint(point), buttons: 1);
        await using var interaction = test.CreateInteractionController();
        await interaction.PointerPressedAsync(Pointer(origin));
        var reuses = test.Pipeline.MoveReuses;
        var rebuilds = test.Pipeline.Rebuilds;
        var uploads = test.Execution.FullUploadCount;
        var calls = test.Contributions.Sum(item => item.Calls + item.Routes);
        foreach (var current in samples)
        {
            var delta = current - origin;
            Assert.Equal(Canvas2DInteractionStatus.Updated, (await interaction.PointerMovedAsync(Pointer(current))).Status);
            await AssertFullSceneEquivalentAsync(test);
            var preview = test.State.CurrentScene!;
            Assert.Equal(current, test.State.EditorState.ActiveGesture!.Current);
            Assert.Same(snapshot, test.Snapshot);
            Assert.Equal(before.HistoryStatus, test.State.HistoryStatus);
            Assert.Same(before.ProjectedGraph, test.State.ProjectedGraph);
            Assert.Same(before.LayoutResult, test.State.LayoutResult);
            Assert.Same(before.RoutingResult, test.State.RoutingResult);
            Assert.True(scene.RenderContent == preview.RenderContent);
            Assert.Same(scene.SpatialPresentationPlan, preview.SpatialPresentationPlan);
            Assert.Equal(scene.Items.Where(item => item.Layer == Canvas2DSceneLayer.Connector),
                preview.Items.Where(item => item.Layer == Canvas2DSceneLayer.Connector));
            foreach (var item in scene.Items.Where(item => item.Origin.VisualStateId == visual &&
                         item.Layer is Canvas2DSceneLayer.Content or Canvas2DSceneLayer.Label or Canvas2DSceneLayer.Decoration))
            {
                var ghost = preview.Items.Single(candidate => candidate.Origin.RelatedSceneObjectIds.Contains(item.Id) &&
                    candidate.Origin.StableSourceKey?.StartsWith("move-preview:", StringComparison.Ordinal) == true);
                Assert.Same(item.Geometry, ghost.Geometry);
                Assert.Equal(item.Transform.Then(Matrix2D.CreateTranslation(delta)), ghost.Transform);
                Assert.Equal(item.Clip?.Translate(delta), ghost.Clip);
                Assert.Equal(item.SpatialRegion, ghost.SpatialRegion);
                Assert.Equal(Canvas2DHitTestMode.None, ghost.HitTestPolicy.Mode);
                // Marker source bounds can conservatively cover the entire owner body.
                // Its exact geometry and displayed transform are checked above.
                if (item.Layer != Canvas2DSceneLayer.Decoration)
                {
                    AssertCrossBounds(item.Bounds.Translate(delta), ghost.Bounds);
                }
            }
            foreach (var handle in scene.Items.Where(item => item.Origin.VisualStateId == visual &&
                         item.Origin.StableSourceKey?.StartsWith("connector-anchor-handle:", StringComparison.Ordinal) == true))
            {
                var movedHandle = preview.Items.Single(item => item.Id == handle.Id);
                AssertCrossBounds(handle.Bounds.Translate(delta), movedHandle.Bounds);
                Assert.Equal(handle.Metadata, movedHandle.Metadata);
                Assert.Equal(handle.SpatialRegion, movedHandle.SpatialRegion);
            }
            // These existing source affordances still describe the persistent original,
            // while ghosts and connector anchors describe the moving candidate.
            foreach (var fixedItem in scene.Items.Where(item => item.Origin.VisualStateId == visual &&
                         item.Layer == Canvas2DSceneLayer.Overlay &&
                         item.Origin.StableSourceKey?.StartsWith("connector-anchor-handle:", StringComparison.Ordinal) != true))
            {
                Assert.Equal(fixedItem, preview.Items.Single(item => item.Id == fixedItem.Id));
            }
        }
        Assert.Equal(reuses + samples.Length, test.Pipeline.MoveReuses);
        Assert.Equal(rebuilds, test.Pipeline.Rebuilds);
        Assert.Equal(uploads, test.Execution.FullUploadCount);
        Assert.Equal(calls, test.Contributions.Sum(item => item.Calls + item.Routes));
        await interaction.PointerCancelledAsync(1211);
        Assert.Null(test.State.EditorState.ActiveGesture);
        Assert.Same(snapshot, test.Snapshot);
        await AssertFullSceneEquivalentAsync(test);
        await test.AssertPanReusedAsync();
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 1)]
    public async Task CrossSpatialFinalUpAssignsThroughExistingPlannerAndUndoRedoAreExact(int sourceIndex, int targetIndex)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        var visual = await PrepareCrossSpatialNodeAsync(test, "manual", sourceIndex, "short");
        var before = test.State;
        var original = test.Snapshot;
        var scene = before.CurrentScene!;
        var body = scene.Items.Single(item => item.Origin.VisualStateId == visual && Canvas2DNodeBodyMetadata.IsNodeBody(item));
        var destination = CrossRegion(test, targetIndex);
        var origin = CrossCenter(body.Bounds);
        var lastPreview = destination.MapLocalToScene(new PointD(280, 180));
        var up = lastPreview + new VectorD(13.25, -7.75);
        Canvas2DPointerInput Pointer(PointD point) => new(1211, scene.ViewportTransform.TransformPoint(point), buttons: 1);
        await using var interaction = test.CreateInteractionController();
        await interaction.PointerPressedAsync(Pointer(origin));
        await interaction.PointerMovedAsync(Pointer(lastPreview));
        Assert.Equal(Canvas2DInteractionStatus.Committed, (await interaction.PointerReleasedAsync(Pointer(up))).Status);
        await test.Session.WaitForIdleAsync();
        var moved = test.Snapshot;
        var movedState = test.State;
        var movedVisual = moved.VisualModel.VisualStates.Single(item => item.Id == visual);
        Assert.Equal(destination.MapSceneToLocal(body.Bounds.Translate(up - origin)).TopLeft, movedVisual.Position);
        Assert.Equal(original.Revision.Value + 1, moved.Revision.Value);
        Assert.Equal(before.HistoryStatus.EntryCount + 1, movedState.HistoryStatus.EntryCount);
        OrganizationalSemantics.TryGetAssignedPoolId(moved.SemanticModel, movedVisual.SemanticElementId, out var assigned);
        Assert.Equal(destination.ContainerSemanticElementId, assigned);
        Assert.Null(movedState.EditorState.ActiveGesture);
        Assert.DoesNotContain(movedState.CurrentScene!.Items, item => item.Origin.StableSourceKey?.StartsWith("move-preview:", StringComparison.Ordinal) == true);
        await AssertFullSceneEquivalentAsync(test);
        Assert.True((await test.Session.UndoAsync()).IsCommitted);
        await test.Session.WaitForIdleAsync();
        AssertCrossDocumentValues(original, test.Snapshot);
        Assert.Equal(original.Revision.Value + 2, test.Snapshot.Revision.Value);
        Assert.Equal(scene.Items.Where(item => item.Layer != Canvas2DSceneLayer.Overlay),
            test.State.CurrentScene!.Items.Where(item => item.Layer != Canvas2DSceneLayer.Overlay));
        Assert.True((await test.Session.RedoAsync()).IsCommitted);
        await test.Session.WaitForIdleAsync();
        AssertCrossDocumentValues(moved, test.Snapshot);
        Assert.Equal(original.Revision.Value + 3, test.Snapshot.Revision.Value);
        Assert.Equal(movedState.CurrentScene.Items.ToArray(), test.State.CurrentScene!.Items.ToArray());
        await test.AssertPanReusedAsync();
    }

    private static async Task<VisualStateId> PrepareCrossSpatialNodeAsync(
        PhaseA122PanSceneReuseIntegrationTests.Fixture test, string shape, int sourceIndex, string label)
    {
        await test.EnablePoolsAsync();
        var poolC = new SemanticElementId("test:a1211:cross:pool-c");
        await test.ExecuteAsync(new CreateOrganizationalPoolCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            poolC, test.State.ActiveScopeId, OrganizationalPoolCreationMode.Empty, "Codex"));
        var node = shape switch
        {
            "gateway" => BpmnDemoPipeline.ExclusiveGatewayId,
            "event" => BpmnDemoPipeline.StartEventId,
            "subprocess" => BpmnDemoPipeline.ProcessOrderSubProcessId,
            _ => new SemanticElementId("test:a1211:cross:activity"),
        };
        if (label == "empty")
        {
            node = new SemanticElementId("test:a1211:cross:unnamed-event");
            await test.ExecuteAsync(new CreateBpmnStartEventCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
                node, new VisualStateId("test:a1211:cross:unnamed-event:visual"), new PointD(180, 120), new SizeD(40, 40),
                VisualPlacementMode.Pinned, targetScopeId: test.State.ActiveScopeId));
        }
        if (shape is "manual" or "service")
        {
            var name = label == "multiline" ? "Manual Task 301\nSecond line\nThird line" : "Manual Task 301";
            await test.ExecuteAsync(new CreateBpmnTaskCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
                node, new VisualStateId("test:a1211:cross:activity:visual"), new PointD(180, 120), new SizeD(160, 100), "ACTIVITY", name, 301,
                VisualPlacementMode.Pinned, taskTypeId: shape == "manual" ? BpmnSemanticTypes.ManualTask : BpmnSemanticTypes.ServiceTask,
                targetScopeId: test.State.ActiveScopeId));
        }
        var visual = test.Snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == node).Id;
        if (sourceIndex == 3)
        {
            OrganizationalSemantics.TryGetAssignedPoolId(test.Snapshot.SemanticModel, node, out var assigned);
            if (assigned is not null)
            {
                await test.ExecuteAsync(new UnassignOrganizationalElementCommand(test.Snapshot.DocumentId, test.Snapshot.Revision, node));
            }
        }
        else
        {
            await test.ExecuteAsync(new AssignOrganizationalElementCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
                node, sourceIndex == 2 ? poolC : new SemanticElementId("test:a122:pool-b")));
        }
        if (label == "manual")
        {
            await test.ExecuteAsync(new UpdateNodeLabelVisualOverrideCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
                visual, new NodeLabelVisualOverride(0, 30, 140, 45)));
        }
        if (shape is "manual" or "service")
        {
            await AddCrossFlowAsync(test, BpmnDemoPipeline.TaskId, node, "incoming");
            await AddCrossFlowAsync(test, node, BpmnDemoPipeline.ApprovedTaskId, "outgoing");
        }
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [visual],
            viewport: test.State.EditorState.Viewport))).Succeeded);
        Assert.Equal(4, test.State.CurrentScene!.SpatialPresentationPlan!.Regions.Select(region => region.LocalToSceneTransform).Distinct().Count());
        return visual;
    }

    private static async Task AddCrossFlowAsync(PhaseA122PanSceneReuseIntegrationTests.Fixture test,
        SemanticElementId source, SemanticElementId target, string key)
    {
        var sourceAnchor = new ConnectorAnchorId($"test:a1211:cross:{key}:source");
        var targetAnchor = new ConnectorAnchorId($"test:a1211:cross:{key}:target");
        foreach (var (owner, anchor, side, role) in new[]
        {
            (source, sourceAnchor, ConnectorAnchorSide.Right, ConnectorAnchorRole.Source),
            (target, targetAnchor, ConnectorAnchorSide.Left, ConnectorAnchorRole.Target),
        })
        {
            var ownerVisual = test.Snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == owner).Id;
            await test.ExecuteAsync(new AddConnectorAnchorCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
                ownerVisual, anchor, side, role, 0));
        }
        await test.ExecuteAsync(new CreateBpmnSequenceFlowCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            new SemanticElementId($"test:a1211:cross:{key}"), new VisualStateId($"test:a1211:cross:{key}:visual"),
            source, target, sourceAnchor, targetAnchor));
    }

    private static Canvas2DSpatialRegion CrossRegion(PhaseA122PanSceneReuseIntegrationTests.Fixture test, int index) =>
        test.State.CurrentScene!.SpatialPresentationPlan!.Regions.Single(region => region.ContainerSemanticElementId ==
            (index == 3 ? null : new SemanticElementId(index == 2 ? "test:a1211:cross:pool-c" : "test:a122:pool-b")));

    private static PointD CrossCenter(RectD bounds) => new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

    private static void AssertCrossDocumentValues(DocumentSnapshot expected, DocumentSnapshot actual)
    {
        Assert.Equal(expected.DocumentId, actual.DocumentId);
        Assert.Equal(expected.VisualModel.VisualStates.ToArray(), actual.VisualModel.VisualStates.ToArray());
        Assert.Equal(expected.VisualModel.ProfileElementPresentations.ToArray(), actual.VisualModel.ProfileElementPresentations.ToArray());
        Assert.Equal(expected.SemanticModel.Elements.ToArray(), actual.SemanticModel.Elements.ToArray());
        Assert.Equal(expected.SemanticModel.Relationships.ToArray(), actual.SemanticModel.Relationships.ToArray());
        Assert.Equal(expected.SemanticModel.ProfileAssignments.ToArray(), actual.SemanticModel.ProfileAssignments.ToArray());
        Assert.Equal(expected.SemanticModel.ModelProfiles, actual.SemanticModel.ModelProfiles);
        Assert.Equal(expected.SemanticModel.NestedScopes.ToArray(), actual.SemanticModel.NestedScopes.ToArray());
        Assert.Equal(expected.SemanticModel.ScopeMemberships.ToArray(), actual.SemanticModel.ScopeMemberships.ToArray());
    }

    private static void AssertCrossBounds(RectD expected, RectD actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
        Assert.Equal(expected.Width, actual.Width, 8);
        Assert.Equal(expected.Height, actual.Height, 8);
    }
}
