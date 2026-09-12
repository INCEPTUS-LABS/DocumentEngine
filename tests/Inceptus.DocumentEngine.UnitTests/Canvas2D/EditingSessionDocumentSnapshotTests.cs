using System.Diagnostics.CodeAnalysis;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class EditingSessionDocumentSnapshotTests
{
    [Fact]
    public async Task ActiveSessionExposesItsCurrentImmutableAuthoritativeSnapshot()
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

        var captured = session.TryCaptureDocumentSnapshot(out var snapshot);
        var authoritative = Assert.IsType<DocumentSnapshot>(snapshot);

        Assert.True(captured);
        Assert.Same(document.CaptureSnapshot(), authoritative);
        Assert.Equal(session.CaptureState().DocumentRevision, authoritative.Revision);
    }

    [Fact]
    public async Task RuntimeFaultedSessionStillExposesItsAuthoritativeSnapshot()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Failure());
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);

        var captured = session.TryCaptureDocumentSnapshot(out var snapshot);
        var authoritative = Assert.IsType<DocumentSnapshot>(snapshot);

        Assert.True(captured);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, session.CaptureState().Status);
        Assert.Same(document.CaptureSnapshot(), authoritative);
    }

    [Fact]
    public async Task SnapshotObservationAdvancesWithTheAuthoritativeDocument()
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
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var visual = document.CaptureSnapshot().VisualModel.VisualStates.First();

        var result = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            new PointD(480d, 320d)));
        await session.WaitForIdleAsync();
        var captured = session.TryCaptureDocumentSnapshot(out var snapshot);
        var authoritative = Assert.IsType<DocumentSnapshot>(snapshot);

        Assert.True(result.IsCommitted);
        Assert.True(captured);
        Assert.Same(document.CaptureSnapshot(), authoritative);
        Assert.Equal(new PointD(480d, 320d),
            authoritative.VisualModel.VisualStates.First().Position);
    }

    [Fact]
    public async Task ClosedSessionDoesNotExposeItsFormerDocumentSnapshot()
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
        var session = Assert.IsType<EditingSession>(attachment.Session);

        var closed = await session.CloseAsync();
        var captured = session.TryCaptureDocumentSnapshot(out var snapshot);

        Assert.Equal(EditingSessionOperationStatus.Closed, closed.Status);
        Assert.False(captured);
        Assert.Null(snapshot);
    }

    [Fact]
    public void SnapshotBoundaryHasTheExactNullableOutContractWithoutANewPublicType()
    {
        var method = Assert.Single(typeof(EditingSession).GetMethods(), static method =>
            method.Name == nameof(EditingSession.TryCaptureDocumentSnapshot));
        var parameter = Assert.Single(method.GetParameters());

        Assert.Equal(typeof(bool), method.ReturnType);
        Assert.True(parameter.IsOut);
        Assert.Equal(typeof(DocumentSnapshot).MakeByRefType(), parameter.ParameterType);
        var annotation = Assert.Single(
            parameter.GetCustomAttributes(typeof(NotNullWhenAttribute), inherit: false)
                .Cast<NotNullWhenAttribute>());
        Assert.True(annotation.ReturnValue);
        Assert.Same(typeof(EditingSession), method.DeclaringType);
    }

    [Fact]
    public async Task SelectionConditionedExecuteAsyncExecutesOneNormalHistoryCommand()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var visual = document.VisualModel.VisualStates.First();
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
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(new EditorStateSnapshot([visual.Id])),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();

        var result = await session.ExecuteAsync(
            new MoveVisualStateCommand(
                document.DocumentId,
                document.Revision,
                visual.Id,
                new PointD(480d, 320d)),
            visual.Id);
        await session.WaitForIdleAsync();
        var after = session.CaptureState();

        Assert.True(result.IsCommitted);
        Assert.Equal(before.DocumentRevision.Increment(), after.DocumentRevision);
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false), after.HistoryStatus);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.PreservingNodeLayoutRunCount);
        Assert.Equal(visual.Id, Assert.Single(after.EditorState.Selection));
    }

    [Fact]
    public async Task SelectedVisualStateExecutionPreservesMultiSelectionAndCommitsOneHistoryAction()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var visuals = document.VisualModel.VisualStates.Take(2).ToArray();
        var target = visuals[0];
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
        var initial = new EditorStateSnapshot(visuals.Select(static visual => visual.Id));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initial),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();

        var result = await session.ExecuteForSelectedVisualStateAsync(
            target.Id,
            new MoveVisualStateCommand(
                document.DocumentId,
                document.Revision,
                target.Id,
                new PointD(480d, 320d)));
        await session.WaitForIdleAsync();
        var after = session.CaptureState();

        Assert.True(result.IsCommitted);
        Assert.Equal(before.DocumentRevision.Increment(), after.DocumentRevision);
        Assert.Equal(new HistoryStatus(1, canUndo: true, canRedo: false), after.HistoryStatus);
        Assert.Equal(initial.Selection, after.EditorState.Selection);
        Assert.Equal(new PointD(480d, 320d), document.VisualModel.VisualStates
            .Single(visual => visual.Id == target.Id).Position);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.PreservingNodeLayoutRunCount);
    }

    [Fact]
    public async Task SelectedVisualStateExecutionRejectsATargetThatLeftTheSelection()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var visuals = document.VisualModel.VisualStates.Take(2).ToArray();
        var target = visuals[0];
        var selected = visuals[1];
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(new EditorStateSnapshot([selected.Id])),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();

        var result = await session.ExecuteForSelectedVisualStateAsync(
            target.Id,
            new MoveVisualStateCommand(
                document.DocumentId,
                document.Revision,
                target.Id,
                new PointD(480d, 320d)));
        var after = session.CaptureState();

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == EditingSessionDiagnosticCodes.SelectionChanged);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(target.Position, document.VisualModel.VisualStates
            .Single(visual => visual.Id == target.Id).Position);
        Assert.Equal(1, pipeline.FullRunCount);
    }

    [Fact]
    public async Task SelectionQueuedAheadOfConditionalCommandRejectsWithoutPersistentWork()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var visuals = document.VisualModel.VisualStates.Take(2).ToArray();
        var expected = visuals[0];
        var replacement = visuals[1];
        var initial = new EditorStateSnapshot([expected.Id]);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var sceneEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseScene = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.EnqueueScene(async (artifacts, visualModel, editorState, cancellationToken) =>
        {
            sceneEntered.TrySetResult();
            await releaseScene.Task.WaitAsync(cancellationToken);
            return ControlledEditingSessionPipeline.Success(artifacts, visualModel, editorState);
        });
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(initial),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var updated = CopyWithSelection(initial, replacement.Id);
        var update = session.UpdateEditorStateAsync(updated).AsTask();
        await sceneEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var before = session.CaptureState();

        var conditional = session.ExecuteAsync(
            new MoveVisualStateCommand(
                document.DocumentId,
                document.Revision,
                expected.Id,
                new PointD(480d, 320d)),
            expected.Id).AsTask();
        releaseScene.TrySetResult();
        Assert.True((await update.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
        var result = await conditional.WaitAsync(TimeSpan.FromSeconds(5));
        await session.WaitForIdleAsync();
        var after = session.CaptureState();

        Assert.False(result.IsCommitted);
        Assert.Equal(HistoryOperationStatus.CommandFailed, result.Status);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == EditingSessionDiagnosticCodes.SelectionChanged);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(1, pipeline.SceneRebuildCount);
        Assert.Equal(expected.Position, document.VisualModel.VisualStates.Single(item =>
            item.Id == expected.Id).Position);
        Assert.Equal(replacement.Id, Assert.Single(after.EditorState.Selection));
    }

    [Fact]
    public async Task RuntimeFaultedConditionalExecutionPreservesNormalExecuteSemantics()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var visual = document.VisualModel.VisualStates.First();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Failure());
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Failure());
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(new EditorStateSnapshot([visual.Id])),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, session.CaptureState().Status);

        var result = await session.ExecuteForSingleSelectionAsync(
            visual.Id,
            new MoveVisualStateCommand(
                document.DocumentId,
                document.Revision,
                visual.Id,
                new PointD(480d, 320d)));
        await session.WaitForIdleAsync();

        Assert.True(result.IsCommitted);
        Assert.Equal(new PointD(480d, 320d), document.VisualModel.VisualStates.First().Position);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, session.CaptureState().Status);
        Assert.Equal(2, pipeline.FullRunCount);
    }

    private static EditorStateSnapshot CopyWithSelection(
        EditorStateSnapshot source,
        params VisualStateId[] selection) =>
        new(
            selection,
            source.HoveredObjectId,
            source.ActiveToolId,
            source.FocusTargetId,
            source.Viewport,
            source.ActiveGesture,
            source.TemporaryFeedback,
            source.ToolState);
}
