using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class EditingSessionInvalidationTests
{
    [Fact]
    public async Task EditorStateChangeRebuildsOnlySceneAndReusesExactCompatibleArtifacts()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();
        var documentBefore = document.CaptureSnapshot();
        var selectedItem = before.CurrentScene!.Items.First(item => item.Origin.VisualStateId is not null);
        var selected = selectedItem.Id;
        var updatedEditorState = new EditorStateSnapshot(
            selection: [selectedItem.Origin.VisualStateId!],
            hoveredObjectId: selected,
            activeToolId: "tool:move",
            viewport: new ViewportSnapshot(1.5d, new VectorD(20d, 30d)));

        var update = await session.UpdateEditorStateAsync(updatedEditorState);
        var after = session.CaptureState();

        Assert.True(update.Succeeded);
        Assert.Equal(EditingSessionStatus.Ready, after.Status);
        Assert.Same(updatedEditorState, after.EditorState);
        Assert.NotSame(before.CurrentScene, after.CurrentScene);
        Assert.Same(before.ProjectedGraph, after.ProjectedGraph);
        Assert.Same(before.LayoutResult, after.LayoutResult);
        Assert.Same(before.RoutingResult, after.RoutingResult);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.SceneRebuildCount);
        Assert.Same(before.ProjectedGraph, Assert.Single(pipeline.SceneArtifacts).ProjectedGraph);
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));
        Assert.Equal(documentBefore, document.CaptureSnapshot());
        Assert.Equal(new HistoryStatus(0, false, false), after.HistoryStatus);
    }

    [Fact]
    public async Task StructurallyUnchangedEditorStateIsNoOp()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var initialEditorState = new EditorStateSnapshot(activeToolId: "tool:select");
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, initialEditorState));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initialEditorState),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var current = session.CaptureState().CurrentScene;

        var update = await session.UpdateEditorStateAsync(
            new EditorStateSnapshot(activeToolId: "tool:select"));

        Assert.True(update.Succeeded);
        Assert.Same(current, session.CaptureState().CurrentScene);
        Assert.Equal(0, pipeline.SceneRebuildCount);
        Assert.Equal(1, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task InstalledSceneReconcilesStaleLogicalSelectionAndExactHoverReferences()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var retained = inputs.Graph.Nodes[0].Source.VisualStateId!;
        var initial = new EditorStateSnapshot(
            selection: [new VisualStateId("test:visual:missing"), retained],
            hoveredObjectId: new SceneObjectId("scene:missing"),
            activeToolId: "tool:select",
            viewport: new ViewportSnapshot(1.25d, new VectorD(8d, -3d)));
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var before = document.CaptureSnapshot();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, initial));
        var reconciliationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReconciliation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.EnqueueScene(async (artifacts, visualModel, editorState, _) =>
        {
            reconciliationStarted.TrySetResult();
            await releaseReconciliation.Task;
            return ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState);
        });
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();

        var attachmentTask = EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initial),
            pipeline).AsTask();
        await reconciliationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(attachmentTask.IsCompleted);
        releaseReconciliation.TrySetResult();
        var attachment = await attachmentTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var state = session.CaptureState();

        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        Assert.Equal(retained, Assert.Single(state.EditorState.Selection));
        Assert.Null(state.EditorState.HoveredObjectId);
        Assert.Equal(initial.ActiveToolId, state.EditorState.ActiveToolId);
        Assert.Equal(initial.Viewport, state.EditorState.Viewport);
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.SceneRebuildCount);
        Assert.Equal(1, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task SceneOnlyFailureLeavesChangedEditorStateAndDemotesCurrentToStale()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        pipeline.EnqueueScene(ControlledEditingSessionPipeline.Failure("TEST_SCENE_REBUILD_FAILED"));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var previous = session.CaptureState().CurrentScene;
        var updated = new EditorStateSnapshot(activeToolId: "tool:changed");

        var result = await session.UpdateEditorStateAsync(updated);
        var state = session.CaptureState();

        Assert.Equal(EditingSessionOperationStatus.Failed, result.Status);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, state.Status);
        Assert.Same(updated, state.EditorState);
        Assert.Null(state.CurrentScene);
        Assert.Same(previous, state.LastKnownGoodScene);
        Assert.True(state.IsDisplayingStaleScene);
        Assert.False(state.IsGraphicalInteractionEnabled);
    }

    [Fact]
    public async Task CommittedManualGeometryCommandStartsOneSelectiveRunFromExactSnapshot()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        pipeline.EnqueueFull((snapshot, editorState, _) =>
        {
            var artifacts = EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision);
            return ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                snapshot.VisualModel,
                editorState));
        });
        var externalEvents = new RecordingSubscriber();
        var configuration = EditingSessionTestHarness.Configuration();
        configuration = new EditingSessionConfiguration(
            configuration.ProjectionEngine,
            configuration.LayoutEngine,
            configuration.LayoutAlgorithmId,
            configuration.RoutingEngine,
            configuration.RoutingAlgorithmId,
            configuration.SceneBuilder,
            documentChangedSubscribers: [externalEvents]);
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(document, renderer, configuration, pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var visual = document.VisualModel.VisualStates.First();
        var statuses = new ConcurrentQueue<EditingSessionStatus>();
        session.StateChanged += (_, args) => statuses.Enqueue(args.State.Status);

        var command = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            new PointD(333d, 222d)));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var state = session.CaptureState();
        var changed = Assert.Single(externalEvents.Events);

        Assert.True(command.IsCommitted);
        Assert.Equal(new DocumentRevision(4), document.Revision);
        Assert.Equal(new HistoryStatus(1, true, false), state.HistoryStatus);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.PreservingNodeLayoutRunCount);
        Assert.Same(changed.CommittedSnapshot, pipeline.DocumentRuns[^1]);
        Assert.Equal(changed.CommittedRevision, state.CurrentScene!.SourceRevision);
        Assert.Contains(EditingSessionStatus.Rebuilding, statuses);
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
    }

    [Fact]
    public async Task LabelVisualCommitUndoAndRedoRebuildOnlySceneWithCurrentReboundArtifacts()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        EnqueueSceneSuccess(pipeline);
        EnqueueSceneSuccess(pipeline);
        EnqueueSceneSuccess(pipeline);
        var events = new RecordingSubscriber();
        var baseConfiguration = EditingSessionTestHarness.Configuration();
        var configuration = new EditingSessionConfiguration(
            baseConfiguration.ProjectionEngine,
            baseConfiguration.LayoutEngine,
            baseConfiguration.LayoutAlgorithmId,
            baseConfiguration.RoutingEngine,
            baseConfiguration.RoutingAlgorithmId,
            baseConfiguration.SceneBuilder,
            documentChangedSubscribers: [events]);
        var (renderer, execution) =
            await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            configuration,
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();
        var targetVisualStateId = inputs.Graph.Nodes[0].Source.VisualStateId!;
        var targetOverride = new NodeLabelVisualOverride(150d, 73d, 300d, 120d);

        var committed = await session.ExecuteAsync(
            new UpdateNodeLabelVisualOverrideCommand(
                document.DocumentId,
                document.Revision,
                targetVisualStateId,
                targetOverride));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var afterCommit = session.CaptureState();

        Assert.True(committed.IsCommitted);
        Assert.Equal(PipelineInvalidation.Scene,
            Assert.Single(events.Events).PipelineInvalidation);
        AssertArtifactGeometryWasRebound(before, afterCommit);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(0, pipeline.PreservingNodeLayoutRunCount);
        Assert.Equal(1, pipeline.SceneRebuildCount);
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));
        Assert.True(NodeLabelVisualOverride.TryRead(
            document.VisualModel.VisualStates.Single(visual =>
                visual.Id == targetVisualStateId).Properties,
            out var committedOverride));
        Assert.Equal(targetOverride, committedOverride);

        var undone = await session.UndoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var afterUndo = session.CaptureState();
        Assert.True(undone.IsCommitted);
        Assert.False(NodeLabelVisualOverride.TryRead(
            document.VisualModel.VisualStates.Single(visual =>
                visual.Id == targetVisualStateId).Properties,
            out _));
        AssertArtifactGeometryWasRebound(afterCommit, afterUndo);

        var redone = await session.RedoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var afterRedo = session.CaptureState();
        Assert.True(redone.IsCommitted);
        Assert.True(NodeLabelVisualOverride.TryRead(
            document.VisualModel.VisualStates.Single(visual =>
                visual.Id == targetVisualStateId).Properties,
            out var redoneOverride));
        Assert.Equal(targetOverride, redoneOverride);
        AssertArtifactGeometryWasRebound(afterUndo, afterRedo);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(0, pipeline.PreservingNodeLayoutRunCount);
        Assert.Equal(3, pipeline.SceneRebuildCount);
        Assert.Equal(4, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task CommittedCommandClearsActiveGestureBeforeItsSelectiveRebuild()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var selectedItem = EditingSessionTestHarness.CreateScene(inputs).Items
            .First(item => item.Origin.VisualStateId is not null);
        var selected = selectedItem.Id;
        var activeEditorState = new EditorStateSnapshot(
            selection: [selectedItem.Origin.VisualStateId!],
            hoveredObjectId: selected,
            activeToolId: "tool:move",
            viewport: new ViewportSnapshot(1.5d, new VectorD(12d, -8d)),
            activeGesture: new EditorGestureSnapshot(
                "test:external-commit-gesture",
                "test:transient-preview",
                new PointD(10d, 20d),
                new PointD(40d, 50d)));
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs, activeEditorState));
        pipeline.EnqueueFull((snapshot, editorState, _) =>
        {
            var artifacts = EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision);
            return ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                snapshot.VisualModel,
                editorState));
        });
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(activeEditorState),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var visual = document.VisualModel.VisualStates.First();

        var command = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            visual.Position + new VectorD(15d, 10d)));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var state = session.CaptureState();

        Assert.True(command.IsCommitted);
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        Assert.Null(state.EditorState.ActiveGesture);
        Assert.True(activeEditorState.Selection.AsSpan().SequenceEqual(
            state.EditorState.Selection.AsSpan()));
        Assert.Equal(activeEditorState.HoveredObjectId, state.EditorState.HoveredObjectId);
        Assert.Equal(activeEditorState.Viewport, state.EditorState.Viewport);
        Assert.Null(pipeline.DocumentEditorStates[^1].ActiveGesture);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.PreservingNodeLayoutRunCount);
        Assert.Equal(0, pipeline.SceneRebuildCount);
        Assert.Equal(2, execution.Calls.Count(call => call == "render"));
    }

    [Fact]
    public async Task FailedCommandCreatesNoHistoryEventOrPipelineRun()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var events = new RecordingSubscriber();
        var baseConfiguration = EditingSessionTestHarness.Configuration();
        var configuration = new EditingSessionConfiguration(
            baseConfiguration.ProjectionEngine,
            baseConfiguration.LayoutEngine,
            baseConfiguration.LayoutAlgorithmId,
            baseConfiguration.RoutingEngine,
            baseConfiguration.RoutingAlgorithmId,
            baseConfiguration.SceneBuilder,
            documentChangedSubscribers: [events]);
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(document, renderer, configuration, pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var visual = document.VisualModel.VisualStates.First();

        var failure = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            DocumentRevision.Zero,
            visual.Id,
            new PointD(50d, 50d)));
        await session.WaitForIdleAsync();
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(failure.IsCommitted);
        Assert.Equal(inputs.Graph.SourceRevision, document.Revision);
        Assert.Equal(new HistoryStatus(0, false, false), session.CaptureState().HistoryStatus);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Empty(events.Events);
    }

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }

    private static void EnqueueSceneSuccess(ControlledEditingSessionPipeline pipeline) =>
        pipeline.EnqueueScene((artifacts, visualModel, editorState, _) =>
            ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                visualModel,
                editorState)));

    private static void AssertArtifactGeometryWasRebound(
        EditingSessionState before,
        EditingSessionState after)
    {
        Assert.Equal(before.DocumentRevision.Increment(), after.DocumentRevision);
        Assert.NotSame(before.ProjectedGraph, after.ProjectedGraph);
        Assert.NotSame(before.LayoutResult, after.LayoutResult);
        Assert.NotSame(before.RoutingResult, after.RoutingResult);
        Assert.Equal(after.DocumentRevision, after.ProjectedGraph!.SourceRevision);
        Assert.Equal(after.DocumentRevision, after.LayoutResult!.SourceRevision);
        Assert.Equal(after.DocumentRevision, after.RoutingResult!.SourceRevision);
        Assert.True(before.ProjectedGraph!.Nodes.AsSpan().SequenceEqual(
            after.ProjectedGraph.Nodes.AsSpan()));
        Assert.True(before.ProjectedGraph.Edges.AsSpan().SequenceEqual(
            after.ProjectedGraph.Edges.AsSpan()));
        Assert.Same(before.LayoutResult!.Computation, after.LayoutResult.Computation);
        Assert.Same(before.RoutingResult!.Computation, after.RoutingResult.Computation);
    }
}
