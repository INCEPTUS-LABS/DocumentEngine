using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class EditingSessionLifecycleTests
{
    [Fact]
    public async Task AttachOwnsSuppliedDocumentAndStartsReadyWithEmptyHistory()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var documentBefore = document.CaptureSnapshot();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();

        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var state = session.CaptureState();

        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        Assert.Equal(document.DocumentId, state.DocumentId);
        Assert.Equal(document.Revision, state.DocumentRevision);
        Assert.NotNull(state.CurrentScene);
        Assert.Null(state.LastKnownGoodScene);
        Assert.Same(inputs.Graph, state.ProjectedGraph);
        Assert.Same(inputs.Layout, state.LayoutResult);
        Assert.Same(inputs.Routing, state.RoutingResult);
        Assert.Same(EditorStateSnapshot.Empty, state.EditorState);
        Assert.Equal(new HistoryStatus(0, false, false), state.HistoryStatus);
        Assert.True(state.IsGraphicalInteractionEnabled);
        Assert.Equal(documentBefore, document.CaptureSnapshot());
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(["initialize:test-session-canvas", "render"], execution.Calls);
    }

    [Fact]
    public async Task InitialPipelineFailureAttachesFaultedWithoutPartialRuntimeResult()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var before = document.CaptureSnapshot();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Failure());
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();

        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var state = session.CaptureState();

        Assert.Equal(EditingSessionAttachStatus.RuntimeFaulted, attachment.Status);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, state.Status);
        Assert.Null(state.CurrentScene);
        Assert.Null(state.LastKnownGoodScene);
        Assert.Null(state.ProjectedGraph);
        Assert.Null(state.LayoutResult);
        Assert.Null(state.RoutingResult);
        Assert.False(state.IsGraphicalInteractionEnabled);
        Assert.Contains(state.RuntimeDiagnostics, diagnostic =>
            diagnostic.Code == "TEST_PIPELINE_FAILURE");
        Assert.Equal(before, document.CaptureSnapshot());
        Assert.DoesNotContain("render", execution.Calls);
    }

    [Fact]
    public async Task RetryUsesFreshCurrentDocumentSnapshotAndRecoversFaultedSession()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Failure("TEST_INITIAL_FAILURE"));
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);

        var retry = await session.RetryAsync();
        var state = session.CaptureState();

        Assert.True(retry.Succeeded);
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        Assert.NotNull(state.CurrentScene);
        Assert.Equal(2, pipeline.FullRunCount);
        Assert.Same(document.CaptureSnapshot(), pipeline.FullDocuments[^1]);
    }

    [Fact]
    public async Task PreCancelledRetryLeavesFaultedSessionUnchanged()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Failure("TEST_INITIAL_FAILURE"));
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var retry = await session.RetryAsync(cancellation.Token);
        var after = session.CaptureState();

        Assert.Equal(EditingSessionOperationStatus.Cancelled, retry.Status);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Generation, after.Generation);
        Assert.True(before.RuntimeDiagnostics.SequenceEqual(after.RuntimeDiagnostics));
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.DoesNotContain("render", execution.Calls);
    }

    [Fact]
    public async Task PreCancelledEditorUpdateLeavesReadySessionUnchanged()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var before = session.CaptureState();
        var callsBefore = execution.Calls.ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var update = await session.UpdateEditorStateAsync(
            new EditorStateSnapshot(activeToolId: "tool:must-not-apply"),
            cancellation.Token);
        var after = session.CaptureState();

        Assert.Equal(EditingSessionOperationStatus.Cancelled, update.Status);
        Assert.Equal(before.Generation, after.Generation);
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Equal(1, pipeline.FullRunCount);
        Assert.Equal(0, pipeline.SceneRebuildCount);
        Assert.Equal(callsBefore, execution.Calls);
    }

    [Fact]
    public async Task RenderingFailureAfterInstallationLeavesSessionReadyAndCurrent()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        execution.RenderResult = Canvas2DRendererTestExecution.Failure("TEST_RENDER_FAILURE");

        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var state = session.CaptureState();

        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        Assert.NotNull(state.CurrentScene);
        Assert.True(state.IsGraphicalInteractionEnabled);
        Assert.Contains(state.PresentationDiagnostics, diagnostic =>
            diagnostic.Code == Canvas2DRendererDiagnosticCodes.RenderingFailed);
        Assert.Empty(state.RuntimeDiagnostics);
    }

    [Fact]
    public async Task CloseIsIdempotentAndRejectsFurtherRuntimeWork()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var documentBefore = document.CaptureSnapshot();
        var pipeline = new ControlledEditingSessionPipeline();
        pipeline.EnqueueFull(ControlledEditingSessionPipeline.Success(inputs));
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        var session = Assert.IsType<EditingSession>(attachment.Session);

        var first = await session.CloseAsync();
        var second = await session.CloseAsync();
        var retry = await session.RetryAsync();
        var state = session.CaptureState();

        Assert.Equal(EditingSessionOperationStatus.Closed, first.Status);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(EditingSessionOperationStatus.Closed, retry.Status);
        Assert.True(state.IsClosed);
        Assert.Null(state.CurrentScene);
        Assert.Null(state.LastKnownGoodScene);
        Assert.Null(state.ProjectedGraph);
        Assert.Equal(documentBefore, document.CaptureSnapshot());
        Assert.Equal(1, execution.DisposeCount);
        Assert.Equal("dispose", execution.Calls[^1]);
    }

    [Fact]
    public async Task AttachmentRequiresInitializedRendererAndReturnsNoPartialSession()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var execution = new Canvas2DRendererTestExecution();
        var renderer = new Canvas2DRenderer(execution);

        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration());

        Assert.Equal(EditingSessionAttachStatus.Failed, attachment.Status);
        Assert.Null(attachment.Session);
        Assert.False(attachment.IsAttached);
        Assert.Contains(attachment.Diagnostics, diagnostic =>
            diagnostic.Code == EditingSessionDiagnosticCodes.InvalidAttachment &&
            diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(1, execution.DisposeCount);
    }

    [Fact]
    public async Task InvalidRuntimeCompositionFailsAttachmentAndDisposesOwnedRenderer()
    {
        var inputs = Canvas2DSceneTestData.Create();
        var document = EditingSessionTestHarness.CreateDocument(inputs);
        var commandTypeId = new CommandTypeId("test:duplicate-handler");
        var validator = new AcceptingEnvelopeValidator();
        var handler = new EchoHandler();
        var configuration = EditingSessionTestHarness.Configuration();
        configuration = new EditingSessionConfiguration(
            configuration.ProjectionEngine,
            configuration.LayoutEngine,
            configuration.LayoutAlgorithmId,
            configuration.RoutingEngine,
            configuration.RoutingAlgorithmId,
            configuration.SceneBuilder,
            commandHandlers:
            [
                new CommandHandlerRegistration(commandTypeId, validator, handler),
                new CommandHandlerRegistration(commandTypeId, validator, handler),
            ]);
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();

        var attachment = await EditingSession.AttachAsync(document, renderer, configuration);

        Assert.Equal(EditingSessionAttachStatus.Failed, attachment.Status);
        Assert.Null(attachment.Session);
        Assert.Contains(attachment.Diagnostics, diagnostic =>
            diagnostic.Code == EditingSessionDiagnosticCodes.InvalidAttachment &&
            diagnostic.Context.Any(pair => pair.Key == "ExceptionType"));
        Assert.True(renderer.IsDisposed);
        Assert.Equal(1, execution.DisposeCount);
    }

    [Fact]
    public async Task StateObserverFailureIsDiagnosticOnlyAndCannotCorruptRebuild()
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
        var (renderer, _) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            EditingSessionTestHarness.Configuration(),
            pipeline);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        session.StateChanged += static (_, _) => throw new InvalidOperationException("observer failed");

        var update = await session.UpdateEditorStateAsync(
            new EditorStateSnapshot(activeToolId: "tool:observer-failure"));
        var state = session.CaptureState();

        Assert.True(update.Succeeded);
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        Assert.NotNull(state.CurrentScene);
        Assert.Contains(state.PresentationDiagnostics, diagnostic =>
            diagnostic.Code == EditingSessionDiagnosticCodes.NotificationFailure);
        Assert.Empty(state.RuntimeDiagnostics);
    }

    private sealed class AcceptingEnvelopeValidator : ICommandEnvelopeValidator
    {
        public System.Collections.Immutable.ImmutableArray<Diagnostic> Validate(ICommand command) => [];
    }

    private sealed class EchoHandler : ICommandHandler
    {
        public ValueTask<CommandHandlerResult> HandleAsync(
            ICommand command,
            Contracts.Documents.DocumentSnapshot document,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(CommandHandlerResult.Success(document));
    }
}
