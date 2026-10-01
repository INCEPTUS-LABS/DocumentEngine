using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed partial class PhaseA1211MovePreviewIntegrationTests
{
    [Theory]
    [InlineData(1, false, 1d)]
    [InlineData(2, false, 1d)]
    [InlineData(1, true, 1d)]
    [InlineData(2, true, 1d)]
    [InlineData(1, false, 1.75d)]
    [InlineData(2, false, 1.75d)]
    public async Task ToolboxCreatedSelectedServiceTaskImmediatelyAndRepeatedlyMatchesFullScene(
        int poolIndex, bool hidden, double zoom)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        await test.EnablePoolsAsync();
        var poolC = new SemanticElementId("test:a1211:pool-c");
        await test.ExecuteAsync(new CreateOrganizationalPoolCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            poolC, test.State.ActiveScopeId, OrganizationalPoolCreationMode.Empty, "Third Pool"));
        if (hidden)
        {
            Assert.True((await test.Session.UpdateModelProfileViewStateAsync(
                new ModelProfileViewStateSnapshot([OrganizationalModelProfile.Id]))).Succeeded);
        }
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            viewport: new ViewportSnapshot(zoom, default)))).Succeeded);
        var pool = poolIndex == 1 ? new SemanticElementId("test:a122:pool-b") : poolC;
        var created = await test.PlaceServiceTaskAsync(pool, new PointD(180, 120));
        Assert.Equal(created.Id, Assert.Single(test.State.EditorState.Selection));
        await using var interaction = test.CreateInteractionController();
        for (var repeat = 0; repeat < 2; repeat++)
        {
            // Keep the exact post-placement/post-commit selection and Scene; no refresh before Down.
            var before = test.State;
            var snapshot = test.Snapshot;
            var scene = before.CurrentScene!;
            var body = scene.Items.Single(item => item.Origin.VisualStateId == created.Id && Canvas2DNodeBodyMetadata.IsNodeBody(item));
            var region = Assert.IsType<Canvas2DSpatialRegion>(body.SpatialRegion);
            Assert.Equal(pool, region.ContainerSemanticElementId);
            var start = new PointD(body.Bounds.X + body.Bounds.Width / 2, body.Bounds.Y + body.Bounds.Height / 2);
            Canvas2DPointerInput Pointer(VectorD delta) => new(1211, scene.ViewportTransform.TransformPoint(start + delta), buttons: 1);
            await interaction.PointerPressedAsync(Pointer(default));
            var fullRuns = test.Pipeline.FullRuns;
            var rebuilds = test.Pipeline.Rebuilds;
            var uploads = test.Execution.FullUploadCount;
            var calls = test.Contributions.Sum(item => item.Calls + item.Routes);
            foreach (var delta in new[] { new VectorD(18, 12), new VectorD(-12, -8), new VectorD(12, 8) })
            {
                Assert.Equal(Canvas2DInteractionStatus.Updated, (await interaction.PointerMovedAsync(Pointer(delta))).Status);
                await AssertFullSceneEquivalentAsync(test);
                var current = test.State.CurrentScene!;
                var ghost = current.Items.Single(item => item.Origin.RelatedSceneObjectIds.Contains(body.Id) &&
                    item.Origin.StableSourceKey?.StartsWith("move-preview:", StringComparison.Ordinal) == true);
                Assert.Equal(body.Bounds.Translate(delta), ghost.Bounds);
                Assert.Equal(region, ghost.SpatialRegion);
                Assert.True(scene.RenderContent == current.RenderContent);
                Assert.Same(scene.SpatialPresentationPlan, current.SpatialPresentationPlan);
                Assert.Same(snapshot, test.Snapshot);
                Assert.Equal(before.HistoryStatus, test.State.HistoryStatus);
            }
            Assert.Equal(fullRuns, test.Pipeline.FullRuns);
            Assert.Equal(rebuilds, test.Pipeline.Rebuilds);
            Assert.Equal(uploads, test.Execution.FullUploadCount);
            Assert.Equal(calls, test.Contributions.Sum(item => item.Calls + item.Routes));
            var up = new VectorD(13.25, -7.75);
            Assert.Equal(Canvas2DInteractionStatus.Committed, (await interaction.PointerReleasedAsync(Pointer(up))).Status);
            await test.Session.WaitForIdleAsync();
            var moved = test.Snapshot.VisualModel;
            Assert.Equal(region.MapSceneToLocal(body.Bounds).TopLeft + up,
                moved.VisualStates.Single(item => item.Id == created.Id).Position);
            Assert.Equal(before.HistoryStatus.EntryCount + 1, test.State.HistoryStatus.EntryCount);
            Assert.Null(test.State.EditorState.ActiveGesture);
            await AssertFullSceneEquivalentAsync(test);
            var movedBody = test.State.CurrentScene!.Items.Single(item => item.Id == body.Id);
            Assert.True((await test.Session.UndoAsync()).IsCommitted);
            await test.Session.WaitForIdleAsync();
            Assert.Equal(snapshot.VisualModel.VisualStates.ToArray(), test.Snapshot.VisualModel.VisualStates.ToArray());
            Assert.Equal(snapshot.VisualModel.ProfileElementPresentations.ToArray(), test.Snapshot.VisualModel.ProfileElementPresentations.ToArray());
            Assert.Equal(body, test.State.CurrentScene!.Items.Single(item => item.Id == body.Id));
            Assert.True((await test.Session.RedoAsync()).IsCommitted);
            await test.Session.WaitForIdleAsync();
            Assert.Equal(moved.VisualStates.ToArray(), test.Snapshot.VisualModel.VisualStates.ToArray());
            Assert.Equal(moved.ProfileElementPresentations.ToArray(), test.Snapshot.VisualModel.ProfileElementPresentations.ToArray());
            Assert.Equal(movedBody, test.State.CurrentScene!.Items.Single(item => item.Id == body.Id));
        }
        await test.AssertPanReusedAsync();
    }

    [Theory]
    [InlineData(0, false, 1d, false)]
    [InlineData(1, false, 1d, false)]
    [InlineData(2, false, 1d, false)]
    [InlineData(3, false, 1d, false)]
    [InlineData(1, true, 1d, false)]
    [InlineData(2, true, 1d, false)]
    [InlineData(1, false, 1.5d, false)]
    [InlineData(1, false, 1d, true)]
    [InlineData(2, false, 1d, true)]
    public async Task DistinctSpatialRegionsKeepBoundedPreviewEqualToFullScene(int regionIndex, bool hidden, double zoom, bool serviceTask)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        await test.EnablePoolsAsync();
        var poolC = new SemanticElementId("test:a1211:pool-c");
        await test.ExecuteAsync(new CreateOrganizationalPoolCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            poolC, test.State.ActiveScopeId, OrganizationalPoolCreationMode.Empty, "Third Pool"));
        await test.ExecuteAsync(new AssignOrganizationalElementCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            BpmnDemoPipeline.RejectedTaskId, poolC));
        var unassigned = new SemanticElementId("test:a1211:unassigned");
        await test.ExecuteAsync(new CreateBpmnTaskCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
            unassigned, new VisualStateId("test:a1211:unassigned:visual"), new PointD(120, 180), new SizeD(160, 100),
            "UNASSIGNED", "Unassigned Activity", 1, VisualPlacementMode.Pinned, targetScopeId: test.State.ActiveScopeId));
        if (hidden)
        {
            Assert.True((await test.Session.UpdateModelProfileViewStateAsync(
                new ModelProfileViewStateSnapshot([OrganizationalModelProfile.Id]))).Succeeded);
        }
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            viewport: new ViewportSnapshot(zoom, default)))).Succeeded);
        var semanticId = regionIndex switch
        {
            0 => BpmnDemoPipeline.TaskId,
            1 => BpmnDemoPipeline.ApprovedTaskId,
            2 => BpmnDemoPipeline.RejectedTaskId,
            _ => unassigned,
        };
        if (serviceTask)
        {
            semanticId = new SemanticElementId("test:a1211:service");
            var serviceVisual = new VisualStateId("test:a1211:service:visual");
            await test.ExecuteAsync(new CreateBpmnTaskCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
                semanticId, serviceVisual, new PointD(120, 180), new SizeD(160, 100),
                "SERVICE", "Service Task 302", 1, VisualPlacementMode.Pinned,
                taskTypeId: BpmnSemanticTypes.ServiceTask, targetScopeId: test.State.ActiveScopeId));
            await test.ExecuteAsync(new AssignOrganizationalElementCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
                semanticId, regionIndex == 1 ? new SemanticElementId("test:a122:pool-b") : poolC));
            await test.ExecuteAsync(new AddConnectorAnchorCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
                serviceVisual, new ConnectorAnchorId("test:a1211:service:in"), ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0));
            await test.ExecuteAsync(new AddConnectorAnchorCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
                serviceVisual, new ConnectorAnchorId("test:a1211:service:out"), ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0));
            var serviceBody = test.State.CurrentScene!.Items.First(item => item.Origin.VisualStateId == serviceVisual && item.Layer == Canvas2DSceneLayer.Content);
            Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
                selection: [serviceVisual], hoveredObjectId: serviceBody.Id, viewport: test.State.EditorState.Viewport))).Succeeded);
        }
        var original = test.State.CurrentScene!;
        var regions = original.SpatialPresentationPlan!.Regions;
        Assert.Equal(4, regions.Length);
        Assert.Equal(4, regions.Select(region => region.LocalToSceneTransform.OffsetY).Distinct().Count());
        var visual = test.Snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == semanticId);
        var body = original.Items.First(item => item.Origin.VisualStateId == visual.Id && item.Layer == Canvas2DSceneLayer.Content);
        var region = Assert.IsType<Canvas2DSpatialRegion>(body.SpatialRegion);
        Assert.Equal(regionIndex == 3 ? null : regionIndex == 2 ? poolC :
            new SemanticElementId(regionIndex == 0 ? "test:a122:pool-a" : "test:a122:pool-b"), region.ContainerSemanticElementId);
        var source = Assert.IsType<Canvas2DBoundedPresentation>(original.BoundedPresentation).Source;
        Assert.Same(body, source.ItemsById[body.Id]);
        Assert.Same(body, source.Families[visual.Id].Single(item => item.Id == body.Id));
        var start = new PointD(body.Bounds.X + body.Bounds.Width / 3, body.Bounds.Y + body.Bounds.Height / 2);
        Canvas2DPointerInput Pointer(VectorD delta) => new(1211, original.ViewportTransform.TransformPoint(start + delta), buttons: 1);
        await using var interaction = new Canvas2DInteractionController(test.Session);
        await interaction.PointerPressedAsync(Pointer(default));
        var reuses = test.Pipeline.MoveReuses;
        var calls = test.Contributions.Sum(item => item.Calls + item.Routes);
        var uploads = test.Execution.FullUploadCount;
        foreach (var delta in new[] { new VectorD(18, 12), new VectorD(32, 24), new VectorD(12, 8) })
        {
            Assert.Equal(Canvas2DInteractionStatus.Updated, (await interaction.PointerMovedAsync(Pointer(delta))).Status);
            await AssertFullSceneEquivalentAsync(test);
            var current = test.State.CurrentScene!;
            var ghost = current.Items.Single(item => item.Origin.RelatedSceneObjectIds.Contains(body.Id) &&
                item.Origin.StableSourceKey?.StartsWith("move-preview:", StringComparison.Ordinal) == true);
            Assert.Equal(body.Bounds.Translate(delta), ghost.Bounds);
            Assert.Equal(body.Transform.Then(Matrix2D.CreateTranslation(delta)), ghost.Transform);
            Assert.Equal(region, ghost.SpatialRegion);
            Assert.True(original.RenderContent == current.RenderContent);
            Assert.Same(original.SpatialPresentationPlan, current.SpatialPresentationPlan);
        }
        Assert.Equal(reuses + 3, test.Pipeline.MoveReuses);
        Assert.Equal(calls, test.Contributions.Sum(item => item.Calls + item.Routes));
        Assert.Equal(uploads, test.Execution.FullUploadCount);
        await interaction.PointerCancelledAsync(1211);
        await AssertFullSceneEquivalentAsync(test);
    }
}
