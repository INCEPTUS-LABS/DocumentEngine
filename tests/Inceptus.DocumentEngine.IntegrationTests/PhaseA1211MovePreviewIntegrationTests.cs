using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed partial class PhaseA1211MovePreviewIntegrationTests
{
    [Theory]
    [InlineData("task", 1d, false)]
    [InlineData("gateway", 1d, false)]
    [InlineData("event", 1d, false)]
    [InlineData("subprocess", 1d, false)]
    [InlineData("task", 1.75d, false)]
    [InlineData("task", 1d, true)]
    [InlineData("cross-pool", 1d, true)]
    [InlineData("zero-flow", 1d, false)]
    public async Task PointerActivationReversalNoOpCancelAndPanKeepExactLogicalScene(
        string shape, double zoom, bool organizational)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        if (organizational) { await test.EnablePoolsAsync(); }
        var isolated = new SemanticElementId("test:a1211:isolated");
        if (shape == "zero-flow")
        {
            await test.ExecuteAsync(new CreateBpmnTaskCommand(test.Snapshot.DocumentId, test.Snapshot.Revision,
                isolated, new VisualStateId("test:a1211:isolated-visual"), new PointD(600, 400), new SizeD(120, 80),
                "ISOLATED", "Isolated", 1, VisualPlacementMode.Pinned, targetScopeId: test.State.ActiveScopeId));
        }
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            viewport: new ViewportSnapshot(zoom, default)))).Succeeded);
        var semanticId = shape switch
        {
            "gateway" => BpmnDemoPipeline.ExclusiveGatewayId,
            "event" => BpmnDemoPipeline.StartEventId,
            "subprocess" => BpmnDemoPipeline.ProcessOrderSubProcessId,
            "zero-flow" => isolated,
            "cross-pool" => test.Snapshot.SemanticModel.Relationships.Single(
                flow => flow.Id == BpmnDemoPipeline.ThirdSequenceFlowId).TargetId,
            _ => BpmnDemoPipeline.TaskId,
        };
        var visual = test.Snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == semanticId);
        var body = test.State.CurrentScene!.Items.First(item => item.Origin.VisualStateId == visual.Id &&
            item.Layer == Canvas2DSceneLayer.Content && item.IsVisible);
        var start = new PointD(body.Bounds.X + body.Bounds.Width / 2, body.Bounds.Y + body.Bounds.Height / 2);
        Canvas2DPointerInput Pointer(VectorD delta) => new(1211,
            test.State.CurrentScene!.ViewportTransform.TransformPoint(start + delta), buttons: 1);
        var before = test.State;
        var snapshot = test.Snapshot;
        var content = before.CurrentScene!.RenderContent;
        var uploads = test.Execution.FullUploadCount;
        var rebuilds = test.Pipeline.Rebuilds;
        var full = test.Pipeline.FullRuns;
        var contributions = test.Contributions.Sum(item => item.Calls + item.Routes);
        var reuses = test.Pipeline.MoveReuses;
        await using var interaction = new Canvas2DInteractionController(test.Session);
        await interaction.PointerPressedAsync(Pointer(default));
        foreach (var delta in new[] { new VectorD(18, 12), new VectorD(32, 24), new VectorD(12, 8) })
        {
            Assert.Equal(Canvas2DInteractionStatus.Updated, (await interaction.PointerMovedAsync(Pointer(delta))).Status);
            Assert.True(content == test.State.CurrentScene!.RenderContent);
            Assert.Same(snapshot, test.Snapshot);
            Assert.Same(before.ProjectedGraph, test.State.ProjectedGraph);
            Assert.Same(before.LayoutResult, test.State.LayoutResult);
            Assert.Same(before.RoutingResult, test.State.RoutingResult);
            Assert.Same(before.CurrentScene.SpatialPresentationPlan, test.State.CurrentScene.SpatialPresentationPlan);
            Assert.Equal(before.HistoryStatus, test.State.HistoryStatus);
            await AssertFullSceneEquivalentAsync(test);
        }
        var generation = test.State.Generation;
        var renders = test.Execution.RenderCount;
        Assert.Equal(Canvas2DInteractionStatus.Unchanged,
            (await interaction.PointerMovedAsync(Pointer(new VectorD(12, 8)))).Status);
        Assert.Equal(generation, test.State.Generation);
        Assert.Equal(renders, test.Execution.RenderCount);
        Assert.Equal(reuses + 3, test.Pipeline.MoveReuses);
        Assert.Equal(rebuilds, test.Pipeline.Rebuilds);
        Assert.Equal(full, test.Pipeline.FullRuns);
        Assert.Equal(contributions, test.Contributions.Sum(item => item.Calls + item.Routes));
        Assert.Equal(uploads, test.Execution.FullUploadCount);
        await interaction.PointerCancelledAsync(1211);
        Assert.Null(test.State.EditorState.ActiveGesture);
        Assert.Same(snapshot, test.Snapshot);
        Assert.Equal(before.HistoryStatus, test.State.HistoryStatus);
        Assert.Equal(reuses + 4, test.Pipeline.MoveReuses);
        await AssertFullSceneEquivalentAsync(test);
        await test.AssertPanReusedAsync();
    }

    [Fact]
    public async Task FinalUpDifferentFromLastPreviewCommitsOnceAndUndoRedoRestoreExactGeometry()
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(
            viewport: test.State.EditorState.Viewport))).Succeeded);
        var original = test.Snapshot.VisualModel.VisualStates.Single(item => item.SemanticElementId == BpmnDemoPipeline.TaskId);
        var body = test.State.CurrentScene!.Items.First(item => item.Origin.VisualStateId == original.Id && item.Layer == Canvas2DSceneLayer.Content);
        var start = new PointD(body.Bounds.X + body.Bounds.Width / 2, body.Bounds.Y + body.Bounds.Height / 2);
        Canvas2DPointerInput Pointer(VectorD delta) => new(1211,
            test.State.CurrentScene!.ViewportTransform.TransformPoint(start + delta), buttons: 1);
        var history = test.State.HistoryStatus.EntryCount;
        var events = test.Events.Count;
        await using var interaction = new Canvas2DInteractionController(test.Session);
        await interaction.PointerPressedAsync(Pointer(default));
        await interaction.PointerMovedAsync(Pointer(new VectorD(12, 8)));
        Assert.NotNull(test.State.CurrentScene!.BoundedPresentation);
        var oldContent = test.State.CurrentScene.RenderContent;
        var result = await interaction.PointerReleasedAsync(Pointer(new VectorD(48, 32)));
        Assert.Equal(Canvas2DInteractionStatus.Committed, result.Status);
        await test.Session.WaitForIdleAsync();
        var moved = test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == original.Id);
        Assert.Equal(body.Bounds.TopLeft + new VectorD(48, 32), moved.Position);
        Assert.Equal(history + 1, test.State.HistoryStatus.EntryCount);
        Assert.Equal(events + 1, test.Events.Count);
        Assert.Null(test.State.EditorState.ActiveGesture);
        Assert.False(oldContent == test.State.CurrentScene!.RenderContent);
        Assert.DoesNotContain(test.State.CurrentScene.Items, item => item.Origin.StableSourceKey?.StartsWith("move-preview:", StringComparison.Ordinal) == true);
        Assert.True((await test.Session.UndoAsync()).IsCommitted);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(original, test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == original.Id));
        Assert.True((await test.Session.RedoAsync()).IsCommitted);
        await test.Session.WaitForIdleAsync();
        Assert.Equal(moved, test.Snapshot.VisualModel.VisualStates.Single(item => item.Id == original.Id));
        await test.AssertPanReusedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedBoundedPreviewCannotInstallOverNewGenerationOrSurface(bool resize)
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(viewport: test.State.EditorState.Viewport))).Succeeded);
        var body = test.State.CurrentScene!.Items.First(item => item.Origin.SemanticElementId == BpmnDemoPipeline.TaskId && item.Layer == Canvas2DSceneLayer.Content);
        var start = new PointD(body.Bounds.X + body.Bounds.Width / 2, body.Bounds.Y + body.Bounds.Height / 2);
        PointD Css(VectorD delta) => test.State.CurrentScene!.ViewportTransform.TransformPoint(start + delta);
        await using var interaction = new Canvas2DInteractionController(test.Session);
        await interaction.PointerPressedAsync(new(1211, Css(default), buttons: 1));
        test.Pipeline.BlockMove = true;
        var move = interaction.PointerMovedAsync(new Canvas2DPointerInput(1211, Css(new VectorD(20, 10)), buttons: 1)).AsTask();
        await test.Pipeline.MoveBuilt.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(test.Pipeline.BlockedResult!.ReusedMoveContent);
        if (resize)
        {
            await test.Session.ResizeAsync(new Canvas2DSurfaceSize(1000, 700, 2));
        }
        else
        {
            await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(viewport: new ViewportSnapshot(2, default)));
        }
        test.Pipeline.ReleaseMove.TrySetResult();
        await move.WaitAsync(TimeSpan.FromSeconds(10));
        await test.Session.WaitForIdleAsync();
        Assert.Equal(EditingSessionStatus.Ready, test.State.Status);
        Assert.NotSame(test.Pipeline.BlockedResult.Scene, test.State.CurrentScene);
        await interaction.CancelActiveGestureAsync();
        await AssertFullSceneEquivalentAsync(test);
    }

    private static async Task AssertFullSceneEquivalentAsync(PhaseA122PanSceneReuseIntegrationTests.Fixture test)
    {
        var eligibility = new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode);
        var registrations = BpmnPluginRegistration.N100.SceneContributors.AddRange(
            OrganizationalPluginRegistration.Create(eligibility,
                OrganizationalPoolSceneContributor.CreateRegistration(eligibility)).SceneContributors);
        var builder = new Canvas2DSceneBuilder(contributors: registrations);
        var state = test.State;
        var result = await builder.BuildMeasuredAsync(test.Snapshot, state.ActiveScopeId,
            state.ModelProfileViewState, state.ModelProfileElementViewState, state.ProjectedGraph!,
            state.LayoutResult!, state.RoutingResult!, test.Snapshot.VisualModel, state.EditorState,
            test.Renderer, test.Renderer.CreateTextMeasurementRequest, CancellationToken.None);
        Assert.Equal(result.Scene, state.CurrentScene);
        var hits = new Canvas2DSceneHitTestService();
        foreach (var item in state.CurrentScene!.Items.Where(item => item.HitTestPolicy.Mode != Canvas2DHitTestMode.None))
        {
            var point = state.CurrentScene.ViewportTransform.TransformPoint(item.Bounds.TopLeft);
            Assert.Equal(hits.HitTest(result.Scene!, point)?.SceneObjectId, hits.HitTest(state.CurrentScene, point)?.SceneObjectId);
        }
    }

    [Fact]
    public async Task AttachedNodesAndTheirOwnersCannotEnterOrdinaryMoveReuse()
    {
        await using var test = await PhaseA122PanSceneReuseIntegrationTests.Fixture.CreateAsync();
        Assert.True((await test.Session.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        Assert.True((await test.Session.UpdateEditorStateAsync(new EditorStateSnapshot(viewport: test.State.EditorState.Viewport))).Succeeded);
        var source = Assert.IsType<Canvas2DBoundedPresentation>(test.State.CurrentScene!.BoundedPresentation).Source;
        var attached = test.State.ProjectedGraph!.Nodes.Where(node => node.PlacementHint?.BoundaryAttachment is not null).ToArray();
        Assert.NotEmpty(attached);
        foreach (var node in attached)
        {
            Assert.DoesNotContain(node.Source.VisualStateId!, source.MovableNodes);
            var owner = test.State.ProjectedGraph.Nodes.Single(candidate => candidate.Source.SemanticElementId == node.PlacementHint!.BoundaryAttachment!.AttachedToElementId);
            Assert.DoesNotContain(owner.Source.VisualStateId!, source.MovableNodes);
        }
    }
}
