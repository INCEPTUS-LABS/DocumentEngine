using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseD2EditingSessionIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteSessionLifecyclePreservesAuthoritativeAndTransientBoundaries(
        bool reconstructBeforeAttach)
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var document = reconstructBeforeAttach
            ? Assert.IsType<Document>(DocumentReconstructor.Reconstruct(
                composition.Document.CaptureSnapshot()).Document)
            : composition.Document;
        var initialDocument = document.CaptureSnapshot();
        var events = new RecordingSubscriber();
        var sourceConfiguration = composition.Configuration;
        var configuration = new EditingSessionConfiguration(
            sourceConfiguration.ProjectionEngine,
            sourceConfiguration.LayoutEngine,
            sourceConfiguration.LayoutAlgorithmId,
            sourceConfiguration.RoutingEngine,
            sourceConfiguration.RoutingAlgorithmId,
            sourceConfiguration.SceneBuilder,
            sourceConfiguration.ProjectionContext,
            sourceConfiguration.LayoutContext,
            sourceConfiguration.RoutingContext,
            sourceConfiguration.InitialEditorState,
            sourceConfiguration.CommandHandlers,
            sourceConfiguration.CommandValidators,
            sourceConfiguration.HistoryPolicies,
            [events]);
        var execution = new IntegrationRenderExecution();
        var renderer = new Canvas2DRenderer(
            execution,
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "demo:font:dejavu",
                        "2.37",
                        "DejaVu Sans",
                        "/fonts/DejaVuSans-2.37.ttf",
                        400,
                        TextFontStyle.Normal),
                ],
                defaultFontFamily: "DejaVu Sans"));
        Assert.True((await renderer.InitializeAsync(
            "phase-d2-canvas",
            new Canvas2DSurfaceSize(900d, 600d, 1.5d))).Succeeded);

        var attachment = await EditingSession.AttachAsync(document, renderer, configuration);
        var session = Assert.IsType<EditingSession>(attachment.Session);
        var initialSession = session.CaptureState();
        var visual = document.VisualModel.VisualStates.First();
        var editorBefore = initialSession.EditorState;

        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        Assert.Equal(EditingSessionStatus.Ready, initialSession.Status);
        Assert.NotNull(initialSession.CurrentScene);
        Assert.Equal(1, composition.Counters.LayoutInvocationCount);
        Assert.Equal(1, composition.Counters.RoutingInvocationCount);
        Assert.Equal(1, composition.Counters.SceneContributionInvocationCount);

        var move = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            new PointD(420d, 260d)));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var afterMove = session.CaptureState();

        Assert.True(move.IsCommitted);
        Assert.Equal(new HistoryStatus(1, true, false), afterMove.HistoryStatus);
        Assert.Equal(1UL, document.Revision.Value - initialDocument.Revision.Value);
        Assert.Equal(document.Revision, afterMove.CurrentScene!.SourceRevision);
        Assert.Equal(1, composition.Counters.LayoutInvocationCount);
        Assert.Equal(2, composition.Counters.RoutingInvocationCount);
        Assert.Equal(2, composition.Counters.SceneContributionInvocationCount);
        Assert.Single(events.Events);

        var targetItem = afterMove.CurrentScene.Items.First(item =>
            item.Origin.SemanticElementId == visual.SemanticElementId);
        var target = targetItem.Id;
        var changedEditorState = new EditorStateSnapshot(
            selection: [targetItem.Origin.VisualStateId!],
            hoveredObjectId: target,
            activeToolId: "tool:session-integration",
            viewport: new ViewportSnapshot(1.25d, new VectorD(40d, 25d)));
        var graphBeforeEditorUpdate = afterMove.ProjectedGraph;
        var layoutBeforeEditorUpdate = afterMove.LayoutResult;
        var routingBeforeEditorUpdate = afterMove.RoutingResult;
        var authoritativeBeforeEditorUpdate = document.CaptureSnapshot();

        var editorUpdate = await session.UpdateEditorStateAsync(changedEditorState);
        var afterEditorUpdate = session.CaptureState();

        Assert.True(editorUpdate.Succeeded);
        Assert.Same(graphBeforeEditorUpdate, afterEditorUpdate.ProjectedGraph);
        Assert.Same(layoutBeforeEditorUpdate, afterEditorUpdate.LayoutResult);
        Assert.Same(routingBeforeEditorUpdate, afterEditorUpdate.RoutingResult);
        Assert.Same(changedEditorState, afterEditorUpdate.EditorState);
        Assert.NotEqual(editorBefore, afterEditorUpdate.EditorState);
        Assert.Equal(1, composition.Counters.LayoutInvocationCount);
        Assert.Equal(2, composition.Counters.RoutingInvocationCount);
        Assert.Equal(3, composition.Counters.SceneContributionInvocationCount);
        Assert.Equal(authoritativeBeforeEditorUpdate, document.CaptureSnapshot());
        Assert.Equal(new HistoryStatus(1, true, false), afterEditorUpdate.HistoryStatus);
        Assert.Single(events.Events);

        var undo = await session.UndoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var afterUndo = session.CaptureState();

        Assert.True(undo.IsCommitted);
        Assert.Equal(initialDocument.VisualModel.VisualStates.First().Position,
            document.VisualModel.VisualStates.First().Position);
        Assert.Same(changedEditorState, afterUndo.EditorState);
        Assert.Equal(new HistoryStatus(1, false, true), afterUndo.HistoryStatus);
        Assert.Equal(EditingSessionStatus.Ready, afterUndo.Status);
        Assert.Equal(2, events.Events.Count);

        var documentBeforeClose = document.CaptureSnapshot();
        var close = await session.CloseAsync();
        var closed = session.CaptureState();

        Assert.Equal(EditingSessionOperationStatus.Closed, close.Status);
        Assert.True(closed.IsClosed);
        Assert.Null(closed.CurrentScene);
        Assert.Null(closed.LastKnownGoodScene);
        Assert.Null(closed.ProjectedGraph);
        Assert.Equal(documentBeforeClose, document.CaptureSnapshot());
        Assert.Equal(1, execution.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostCommitRuntimeFailureKeepsCommitAndUndoRecoversThroughOneFreshRun(
        bool reconstructBeforeAttach)
    {
        var composition = NeutralDemoPipeline.CreateComposition();
        var document = reconstructBeforeAttach
            ? Assert.IsType<Document>(DocumentReconstructor.Reconstruct(
                composition.Document.CaptureSnapshot()).Document)
            : composition.Document;
        var events = new RecordingSubscriber();
        var source = composition.Configuration;
        var configuration = new EditingSessionConfiguration(
            source.ProjectionEngine,
            source.LayoutEngine,
            source.LayoutAlgorithmId,
            source.RoutingEngine,
            source.RoutingAlgorithmId,
            source.SceneBuilder,
            source.ProjectionContext,
            source.LayoutContext,
            source.RoutingContext,
            source.InitialEditorState,
            source.CommandHandlers,
            source.CommandValidators,
            source.HistoryPolicies,
            [events]);
        var pipeline = new FailSecondFullRunPipeline(new EditingSessionPipeline(configuration));
        var execution = new IntegrationRenderExecution();
        var renderer = new Canvas2DRenderer(
            execution,
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "demo:font:dejavu",
                        "2.37",
                        "DejaVu Sans",
                        "/fonts/DejaVuSans-2.37.ttf"),
                ],
                defaultFontFamily: "DejaVu Sans"));
        Assert.True((await renderer.InitializeAsync(
            "phase-d2-recovery-canvas",
            new Canvas2DSurfaceSize(900d, 600d, 1.5d))).Succeeded);
        var attachment = await EditingSession.AttachAsync(
            document,
            renderer,
            configuration,
            pipeline);
        var session = Assert.IsType<EditingSession>(attachment.Session);
        await using var ownedSession = session;
        var initialScene = Assert.IsType<Canvas2D.Scene.Canvas2DScene>(
            session.CaptureState().CurrentScene);
        var visual = document.VisualModel.VisualStates.First();
        var originalPosition = visual.Position;
        var movedPosition = new PointD(470d, 310d);

        var move = await session.ExecuteAsync(new MoveVisualStateCommand(
            document.DocumentId,
            document.Revision,
            visual.Id,
            movedPosition));
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var faulted = session.CaptureState();

        Assert.True(move.IsCommitted);
        Assert.Equal(new DocumentRevision(1), document.Revision);
        Assert.Equal(movedPosition, document.VisualModel.VisualStates.First().Position);
        Assert.Equal(EditingSessionStatus.RuntimeFaulted, faulted.Status);
        Assert.Null(faulted.CurrentScene);
        Assert.Same(initialScene, faulted.LastKnownGoodScene);
        Assert.Equal(new HistoryStatus(1, true, false), faulted.HistoryStatus);
        Assert.Contains(faulted.RuntimeDiagnostics, diagnostic =>
            diagnostic.Code == "TEST_POST_COMMIT_RUNTIME_FAILURE");
        Assert.Equal(2, pipeline.RunCount);
        Assert.Single(events.Events);

        var undo = await session.UndoAsync();
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await CommandProcessor.WaitForEventDispatchIdleAsync(document)
            .WaitAsync(TimeSpan.FromSeconds(5));
        var recovered = session.CaptureState();

        Assert.True(undo.IsCommitted);
        Assert.Equal(new DocumentRevision(2), document.Revision);
        Assert.Equal(originalPosition, document.VisualModel.VisualStates.First().Position);
        Assert.Equal(EditingSessionStatus.Ready, recovered.Status);
        Assert.Equal(document.Revision, recovered.CurrentScene?.SourceRevision);
        Assert.Null(recovered.LastKnownGoodScene);
        Assert.Equal(new HistoryStatus(1, false, true), recovered.HistoryStatus);
        Assert.Equal(3, pipeline.RunCount);
        Assert.Equal(
            [new DocumentRevision(1), new DocumentRevision(2)],
            events.Events.Select(static change => change.CommittedRevision));
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

    private sealed class FailSecondFullRunPipeline(ISessionPipelineProcessing inner) :
        ISessionPipelineProcessing
    {
        private int _runCount;

        internal int RunCount => Volatile.Read(ref _runCount);

        public ValueTask<EditingSessionPipelineResult> RunFullAsync(
            DocumentSnapshot document,
            DocumentScopeId activeScopeId,
            EditorStateSnapshot editorState,
            CancellationToken cancellationToken)
        {
            var run = Interlocked.Increment(ref _runCount);
            return run == 2
                ? ValueTask.FromResult(EditingSessionPipelineResult.Failure(
                [
                    new Diagnostic(
                        "TEST_POST_COMMIT_RUNTIME_FAILURE",
                        DiagnosticSeverity.Error,
                        "Injected post-commit pipeline failure."),
                ]))
                : inner.RunFullAsync(
                    document,
                    activeScopeId,
                    editorState,
                    cancellationToken);
        }

        public ValueTask<EditingSessionPipelineResult> RunPreservingNodeLayoutAsync(
            DocumentSnapshot document,
            DocumentScopeId activeScopeId,
            EditingSessionPipelineArtifacts previousArtifacts,
            ImmutableArray<EditingSessionPipelineArtifacts> nodeLayoutHistory,
            NodeGeometryPipelineImpact nodeGeometryImpact,
            EditorStateSnapshot editorState,
            CancellationToken cancellationToken)
        {
            var run = Interlocked.Increment(ref _runCount);
            return run == 2
                ? ValueTask.FromResult(EditingSessionPipelineResult.Failure(
                [
                    new Diagnostic(
                        "TEST_POST_COMMIT_RUNTIME_FAILURE",
                        DiagnosticSeverity.Error,
                        "Injected post-commit pipeline failure."),
                ]))
                : inner.RunPreservingNodeLayoutAsync(
                    document,
                    activeScopeId,
                    previousArtifacts,
                    nodeLayoutHistory,
                    nodeGeometryImpact,
                    editorState,
                    cancellationToken);
        }

        public ValueTask<EditingSessionPipelineResult> RebuildSceneAsync(
            EditingSessionPipelineArtifacts artifacts,
            Contracts.Visuals.VisualModelSnapshot visualModel,
            EditorStateSnapshot editorState,
            CancellationToken cancellationToken) =>
            inner.RebuildSceneAsync(artifacts, visualModel, editorState, cancellationToken);
    }

    private sealed class IntegrationRenderExecution : ICanvas2DRenderExecution
    {
        internal int DisposeCount { get; private set; }

        public ValueTask<Canvas2DInteropOperationResult> InitializeAsync(
            string canvasElementId,
            Canvas2DSurfaceSize surfaceSize,
            ImmutableSortedDictionary<string, string> imageResources,
            ImmutableArray<Canvas2DFontResource> fontResources,
            string? defaultFontFamily) =>
            ValueTask.FromResult(Success());

        public ValueTask<Canvas2DInteropOperationResult> ResizeAsync(
            Canvas2DSurfaceSize surfaceSize) =>
            ValueTask.FromResult(Success());

        public ValueTask<Canvas2DInteropOperationResult> RenderAsync(
            Canvas2DRenderFrame frame) =>
            ValueTask.FromResult(Success());

        public ValueTask<Canvas2DTextMeasurementInteropResult> MeasureTextAsync(
            Canvas2DTextMeasurementRequestData request) =>
            ValueTask.FromResult(new Canvas2DTextMeasurementInteropResult
            {
                Succeeded = true,
                Width = 80d,
                Ascent = 10d,
                Descent = 3d,
                LineHeight = 18d,
                BoundingX = 0d,
                BoundingY = -10d,
                BoundingWidth = 80d,
                BoundingHeight = 13d,
                ResolvedFontIdentity = "demo:font:dejavu@2.37",
            });

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        private static Canvas2DInteropOperationResult Success() => new() { Succeeded = true };
    }
}
