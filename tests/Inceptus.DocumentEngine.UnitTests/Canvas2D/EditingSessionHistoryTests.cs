using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class EditingSessionHistoryTests
{
    [Fact]
    public async Task UndoAndRedoWorkFromRuntimeFaultedAndUseCurrentRevisions()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var events = new RecordingSubscriber();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Failure("TEST_POST_COMMIT_FAILURE"));
        pipeline.EnqueueFull(SuccessForReceivedSnapshot(inputs));
        pipeline.EnqueueFull(SuccessForReceivedSnapshot(inputs));
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
        var originalPosition = visual.Position;
        var movedPosition = new PointD(480d, 320d);

        var move = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            movedPosition));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(move.IsCommitted);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, session.CaptureState().Status);
        Assert.Null(session.CaptureState().CurrentScene);
        Assert.Equal(new HistoryStatus(1, true, false), session.CaptureState().HistoryStatus);

        var transientEditorState = new EditorStateSnapshot(activeToolId: "tool:recovery");
        Assert.True((await session.UpdateEditorStateAsync(transientEditorState)).Succeeded);
        var undo = await session.UndoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(undo.IsCommitted);
        Assert.Equal(new DocumentRevision(5), document.Revision);
        Assert.Equal(originalPosition, document.VisualModel.VisualStates.First().Position);
        Assert.Equal(new HistoryStatus(1, false, true), session.CaptureState().HistoryStatus);
        Assert.Same(transientEditorState, session.CaptureState().EditorState);
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);

        var redo = await session.RedoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(redo.IsCommitted);
        Assert.Equal(new DocumentRevision(6), document.Revision);
        Assert.Equal(movedPosition, document.VisualModel.VisualStates.First().Position);
        Assert.Equal(new HistoryStatus(1, true, false), session.CaptureState().HistoryStatus);
        Assert.Same(transientEditorState, session.CaptureState().EditorState);
        Assert.Equal(3, events.Events.Count);
        Assert.Equal(
            [new DocumentRevision(4), new DocumentRevision(5), new DocumentRevision(6)],
            events.Events.Select(static change => change.CommittedRevision));
    }

    [Fact]
    public async Task UnavailableUndoRedoAndFailedCommandDoNotMoveCursorOrStartPipeline()
    {
        var inputs = Canvas2DSceneTestData.Create();
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
        var before = session.CaptureState();
        var visual = document.VisualModel.VisualStates.First();

        var undo = await session.UndoAsync();
        var redo = await session.RedoAsync();
        var failed = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            DocumentRevision.Zero,
            visual.Id,
            new PointD(10d, 10d)));

        Assert.Equal(HistoryOperationStatus.NothingToUndo, undo.Status);
        Assert.Equal(HistoryOperationStatus.NothingToRedo, redo.Status);
        Assert.False(failed.IsCommitted);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(before.DocumentRevision, document.Revision);
        Assert.Equal(1, pipeline.FullRunCount);
    }

    private static Func<
        Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot,
        EditorStateSnapshot,
        CancellationToken,
        ValueTask<EditingSessionPipelineResult>> SuccessForReceivedSnapshot(
        Canvas2DSceneTestData inputs) =>
        (snapshot, editorState, _) =>
        {
            var artifacts = EditingSessionTestHarness.CreateArtifacts(inputs, snapshot.Revision);
            return ValueTask.FromResult(ControlledEditingSessionPipeline.Success(
                artifacts,
                snapshot.VisualModel,
                editorState));
        };

    private sealed class RecordingSubscriber : IDocumentChangedSubscriber
    {
        internal ConcurrentQueue<DocumentChangedEvent> Events { get; } = new();

        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change)
        {
            Events.Enqueue(change);
            return ValueTask.CompletedTask;
        }
    }
}
