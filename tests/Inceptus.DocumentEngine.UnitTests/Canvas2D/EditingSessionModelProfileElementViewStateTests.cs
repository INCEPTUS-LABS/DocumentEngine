using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class EditingSessionModelProfileElementViewStateTests
{
    private static readonly ModelProfileId ProfileId =
        new("test:profile-element/organizational");

    [Fact]
    public async Task UpdateIsTransientIndependentAndClearsSelectionHiddenByRecomposition()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var selectedVisualStateId = inputs.VisualModel.VisualStates[0].Id;
        var selectedSemanticElementId = inputs.VisualModel.VisualStates[0].SemanticElementId;
        using var sourceScene = EditingSessionTestHarness.CreateScene(inputs);
        var hoveredObjectId = sourceScene.Items.First(item =>
            item.Origin.VisualStateId == selectedVisualStateId &&
            item.HitTestPolicy.Mode != Canvas2DHitTestMode.None).Id;
        var initialEditorState = new EditorStateSnapshot(
            [selectedVisualStateId],
            hoveredObjectId,
            temporaryFeedback:
            [
                new EditorFeedbackSnapshot(
                    "feedback:profile-element-preview",
                    "test:profile-element-preview",
                    new RectD(20d, 30d, 40d, 50d)),
            ]);
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(
            inputs,
            initialEditorState));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initialEditorState),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var beforeDocument = document.CaptureSnapshot();
        var before = session.CaptureState();
        var requested = ModelProfileElementViewStateSnapshot.Empty.WithCollapsed(
            ProfileId,
            selectedSemanticElementId,
            isCollapsed: true);

        EnqueueWithoutVisualTarget(pipeline, selectedVisualStateId);
        EnqueueWithoutVisualTarget(pipeline, selectedVisualStateId);
        var result = await session.UpdateModelProfileElementViewStateAsync(requested);
        var after = session.CaptureState();

        Assert.True(result.Succeeded);
        Assert.Same(beforeDocument, document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
        Assert.Same(before.LayoutResult, after.LayoutResult);
        Assert.Same(before.RoutingResult, after.RoutingResult);
        Assert.Same(before.ModelProfileViewState, after.ModelProfileViewState);
        Assert.Same(requested, after.ModelProfileElementViewState);
        Assert.Empty(after.EditorState.Selection);
        Assert.Null(after.EditorState.HoveredObjectId);
        Assert.Empty(after.EditorState.TemporaryFeedback);
        Assert.All(
            pipeline.SceneEditorStates,
            state => Assert.Empty(state.TemporaryFeedback));
        Assert.DoesNotContain(
            after.CurrentScene!.Items,
            item => item.Origin.VisualStateId == selectedVisualStateId);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(2, pipeline.SceneRebuildCount);
    }

    [Fact]
    public async Task ElementStateSurvivesIndependentProfileHideAndShow()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var semanticElementId = inputs.VisualModel.VisualStates[0].SemanticElementId;
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var beforeDocument = document.CaptureSnapshot();
        var before = session.CaptureState();
        var collapsed = before.ModelProfileElementViewState.WithCollapsed(
            ProfileId,
            semanticElementId,
            isCollapsed: true);

        EnqueueOrdinaryScene(pipeline);
        Assert.True((await session.UpdateModelProfileElementViewStateAsync(collapsed)).Succeeded);
        var afterCollapse = session.CaptureState();
        EnqueueOrdinaryScene(pipeline);
        var hidden = await session.UpdateModelProfileViewStateAsync(
            new ModelProfileViewStateSnapshot([ProfileId]));
        Assert.True(hidden.Succeeded);
        var afterHide = session.CaptureState();
        EnqueueOrdinaryScene(pipeline);
        var shown = await session.UpdateModelProfileViewStateAsync(
            ModelProfileViewStateSnapshot.Empty);
        Assert.True(shown.Succeeded);
        var afterShow = session.CaptureState();

        Assert.Same(collapsed, afterCollapse.ModelProfileElementViewState);
        Assert.Same(collapsed, afterHide.ModelProfileElementViewState);
        Assert.Same(collapsed, afterShow.ModelProfileElementViewState);
        Assert.Contains(ProfileId, afterHide.ModelProfileViewState.HiddenProfileIds);
        Assert.Empty(afterShow.ModelProfileViewState.HiddenProfileIds);
        Assert.Same(beforeDocument, document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, afterShow.DocumentRevision);
        Assert.Equal(before.HistoryStatus, afterShow.HistoryStatus);
        Assert.Same(before.ProjectedGraph, afterShow.ProjectedGraph);
        Assert.Same(before.LayoutResult, afterShow.LayoutResult);
        Assert.Same(before.RoutingResult, afterShow.RoutingResult);
    }

    [Fact]
    public async Task CloseClearsSessionGlobalElementPresentationState()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var semanticElementId = inputs.VisualModel.VisualStates[0].SemanticElementId;
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        var session = Assert.IsType<EditingSession>(attachment.Session);
        var requested = ModelProfileElementViewStateSnapshot.Empty.WithCollapsed(
            ProfileId,
            semanticElementId,
            isCollapsed: true);
        EnqueueOrdinaryScene(pipeline);
        Assert.True((await session.UpdateModelProfileElementViewStateAsync(requested)).Succeeded);
        Assert.Same(requested, session.CaptureState().ModelProfileElementViewState);

        var close = await session.CloseAsync();
        var closed = session.CaptureState();

        Assert.Equal(EditingSessionOperationStatus.Closed, close.Status);
        Assert.True(closed.IsClosed);
        Assert.Same(
            ModelProfileElementViewStateSnapshot.Empty,
            closed.ModelProfileElementViewState);
    }

    [Fact]
    public async Task DocumentMutationRejectsInFlightElementRecompositionWithoutStaleInstall()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var visual = inputs.VisualModel.VisualStates[0];
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.EnqueueScene(async (artifacts, visualModel, editorState, _) =>
        {
            started.TrySetResult();
            await release.Task;
            return ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState);
        });
        pipeline.EnqueueFull((snapshot, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision),
                snapshot.VisualModel,
                editorState)));
        var requested = ModelProfileElementViewStateSnapshot.Empty.WithCollapsed(
            ProfileId,
            visual.SemanticElementId,
            isCollapsed: true);

        var staleUpdate = session.UpdateModelProfileElementViewStateAsync(requested).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var mutation = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            visual.Position,
            visual.PlacementMode));
        Assert.True(mutation.IsCommitted);
        release.TrySetResult();
        var stale = await staleUpdate.WaitAsync(TimeSpan.FromSeconds(5));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var final = session.CaptureState();

        Assert.True(stale.Status is
            EditingSessionOperationStatus.Failed or
            EditingSessionOperationStatus.Superseded);
        Assert.Same(
            ModelProfileElementViewStateSnapshot.Empty,
            final.ModelProfileElementViewState);
        Assert.Equal(document.Revision, final.DocumentRevision);
        Assert.Equal(EditingSessionStatus.Ready, final.Status);
        Assert.Equal(final.DocumentRevision, final.CurrentScene!.SourceRevision);
    }

    private static void EnqueueOrdinaryScene(ControlledEditingSessionPipeline pipeline) =>
        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));

    private static void EnqueueWithoutVisualTarget(
        ControlledEditingSessionPipeline pipeline,
        VisualStateId visualStateId) =>
        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
        {
            var source = EditingSessionTestHarness.CreateScene(
                artifacts,
                visualModel,
                editorState);
            var scene = new Canvas2DScene(
                source.DocumentId,
                source.SourceRevision,
                source.LayoutAlgorithmId,
                source.RoutingAlgorithmId,
                source.Configuration,
                source.Contributors,
                source.Viewport,
                source.ViewportTransform,
                source.ActiveToolId,
                source.FocusTargetId,
                source.ToolState,
                source.ContributorMetadata,
                source.Items.Where(item => item.Origin.VisualStateId != visualStateId),
                source.Diagnostics,
                source.SpatialPresentationPlan);
            source.Dispose();
            return ValueTask.FromResult(EditingSessionPipelineResult.Success(
                artifacts,
                scene));
        });
}
