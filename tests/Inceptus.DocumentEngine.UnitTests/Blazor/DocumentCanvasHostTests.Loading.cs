using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Profiles;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task LoadPreservesExactSnapshotWithFreshIndependentSessionAndHistory()
    {
        var initialExecution = new RecordingRenderExecution();
        var replacementExecution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        var composition = await CreateNewDiagramComplexCompositionAsync();
        await using var host = CreateHost(
            initialExecution,
            surface,
            compositionFactory: new NewDiagramFixedCompositionFactory(composition),
            replacementRendererFactory: () => CreateRenderer(replacementExecution));
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var oldSession = Session(host);
        await EnableOrganizationalProfileAsync(oldSession);
        var poolId = await AddOrganizationalPoolFromMenuAsync(host);
        Assert.True((await oldSession.UpdateModelProfileElementViewStateAsync(
            oldSession.CaptureState().ModelProfileElementViewState.WithCollapsed(
                BpmnModelProfiles.OrganizationalId, poolId, isCollapsed: true))).Succeeded);
        Assert.True((await oldSession.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        await oldSession.WaitForIdleAsync();
        var visualId = oldSession.CaptureState().CurrentScene!.Items
            .Select(static item => item.Origin.VisualStateId)
            .First(static id => id is not null)!;
        Assert.True((await oldSession.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [visualId],
            activeToolId: "test:p1.3:old-tool",
            viewport: new ViewportSnapshot(1.8d, new VectorD(72d, -43d))))).Succeeded);
        await oldSession.WaitForIdleAsync();
        Assert.NotNull(await host.ValidateAsync());
        var oldDocument = AttachedDocument(oldSession);
        var original = oldDocument.CaptureSnapshot();
        var snapshot = new DocumentSnapshot(
            original.SemanticModel,
            original.VisualModel,
            original.Metadata,
            new DocumentPublicationSnapshot("loaded-process", "Loaded process"));
        var expectedBytes = NativeDocumentSerializer.Export(snapshot);
        var beforeGeneration = host.CaptureState().DocumentSessionVersion;
        Assert.NotEmpty(snapshot.SemanticModel.Elements);
        Assert.NotEmpty(snapshot.SemanticModel.Relationships);
        Assert.NotEmpty(snapshot.SemanticModel.NestedScopes);
        Assert.NotEmpty(snapshot.SemanticModel.ProfileAssignments);
        Assert.NotEmpty(snapshot.VisualModel.ProfileElementPresentations);
        Assert.Contains(snapshot.VisualModel.VisualStates, static visual =>
            !visual.ConnectorAnchors.IsEmpty);
        Assert.Contains(snapshot.VisualModel.VisualStates, static visual =>
            visual.BoundaryAttachment is not null);
        Assert.NotEmpty(snapshot.Metadata.ExtensionProperties);
        Assert.True(oldSession.CaptureState().HistoryStatus.EntryCount > 0);

        var result = await host.LoadDocumentAsync(snapshot);

        Assert.True(result.Succeeded, NewDiagramDiagnostics(result.Diagnostics));
        var session = Session(host);
        var state = session.CaptureState();
        var document = AttachedDocument(session);
        Assert.NotSame(oldSession, session);
        Assert.NotSame(oldDocument, document);
        Assert.True(oldSession.CaptureState().IsClosed);
        Assert.Equal(snapshot, document.CaptureSnapshot());
        Assert.NotSame(snapshot, document.CaptureSnapshot());
        Assert.True(expectedBytes.AsSpan().SequenceEqual(
            NativeDocumentSerializer.Export(snapshot).AsSpan()));
        Assert.True(expectedBytes.AsSpan().SequenceEqual(
            NativeDocumentSerializer.Export(document).AsSpan()));
        Assert.Equal(snapshot.DocumentId, state.DocumentId);
        Assert.Equal(snapshot.Revision, state.DocumentRevision);
        Assert.Equal(EditingSessionStatus.Ready, state.Status);
        Assert.Equal(snapshot.SemanticModel.RootScopeId, state.ActiveScopeId);
        Assert.Equal(0, state.HistoryStatus.EntryCount);
        Assert.False(state.HistoryStatus.CanUndo);
        Assert.False(state.HistoryStatus.CanRedo);
        Assert.Empty(state.EditorState.Selection);
        Assert.Null(state.EditorState.ActiveToolId);
        Assert.Null(state.EditorState.ActiveGesture);
        Assert.Equal(composition.Configuration.InitialEditorState.Viewport.Zoom,
            state.EditorState.Viewport.Zoom);
        Assert.Equal(composition.Configuration.InitialEditorState.Viewport.Pan,
            state.EditorState.Viewport.Pan);
        Assert.Equal(ModelProfileElementViewStateSnapshot.Empty,
            state.ModelProfileElementViewState);
        Assert.Null(host.CaptureState().ValidationSnapshot);
        Assert.Equal(beforeGeneration + 1, host.CaptureState().DocumentSessionVersion);
        Assert.Equal("standby-canvas", host.CaptureState().ActiveCanvasElementId);
        Assert.Equal(1, initialExecution.DisposeCount);
        Assert.Equal(0, replacementExecution.DisposeCount);

        Assert.False((await session.UndoAsync()).Succeeded);
        Assert.Equal(snapshot, document.CaptureSnapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadRejectsNullOrInvalidSnapshotWithoutChangingActiveDocument(bool nullInput)
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(
            execution,
            surface,
            replacementRendererFactory: () =>
                throw new InvalidOperationException("Rejected Load must not create a renderer."));
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var session = Session(host);
        var before = session.CaptureState();
        var snapshot = AttachedDocument(session).CaptureSnapshot();
        var generation = host.CaptureState().DocumentSessionVersion;
        var id = new DocumentId("test:p1.3:invalid-load");
        var revision = new DocumentRevision(23);
        var invalid = new DocumentSnapshot(
            new SemanticModelSnapshot(id, revision, relationships:
            [
                new SemanticRelationshipSnapshot(
                    new SemanticElementId("test:p1.3:flow"),
                    BpmnSemanticTypes.SequenceFlow,
                    new SemanticElementId("test:p1.3:missing-source"),
                    new SemanticElementId("test:p1.3:missing-target")),
            ]),
            new VisualModelSnapshot(id, revision),
            new DocumentMetadataSnapshot(id, revision));

        var result = await host.LoadDocumentAsync(nullInput ? null : invalid);

        Assert.Equal(NativeDocumentHostOperationStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code ==
            (nullInput ? "CANVAS_DOCUMENT_LOAD_REQUIRED" : "DOC_RELATIONSHIP_SOURCE_MISSING"));
        Assert.Same(session, Session(host));
        Assert.Same(snapshot, AttachedDocument(session).CaptureSnapshot());
        var after = session.CaptureState();
        Assert.Same(before.EditorState, after.EditorState);
        Assert.Same(before.CurrentScene, after.CurrentScene);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(generation, host.CaptureState().DocumentSessionVersion);
        Assert.Equal("active-canvas", host.CaptureState().ActiveCanvasElementId);
        Assert.Equal(0, execution.DisposeCount);
    }

    [Fact]
    public async Task LoadCandidatePresentationFailurePreservesExactActiveSession()
    {
        var initialExecution = new RecordingRenderExecution();
        var candidateExecution = new RecordingRenderExecution
        {
            RenderResult = new Canvas2DInteropOperationResult
            {
                Succeeded = false,
                Code = "TEST_LOAD_RENDER_FAILED",
            },
        };
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(initialExecution, surface,
            replacementRendererFactory: () => CreateRenderer(candidateExecution));
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var session = Session(host);
        var before = session.CaptureState();
        var snapshot = AttachedDocument(session).CaptureSnapshot();

        var result = await host.LoadDocumentAsync(
            CreateImportedDocument("test:p1.3:failed-load", 17).CaptureSnapshot());

        Assert.Equal(NativeDocumentHostOperationStatus.Failed, result.Status);
        Assert.Equal("CANVAS_DOCUMENT_LOAD_REPLACEMENT_FAILED",
            Assert.Single(result.Diagnostics).Code);
        Assert.Same(session, Session(host));
        Assert.Same(snapshot, AttachedDocument(session).CaptureSnapshot());
        Assert.Same(before.EditorState, session.CaptureState().EditorState);
        Assert.Same(before.CurrentScene, session.CaptureState().CurrentScene);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal("active-canvas", host.CaptureState().ActiveCanvasElementId);
        Assert.Equal(0, initialExecution.DisposeCount);
        Assert.Equal(1, candidateExecution.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadCancellationAtFinalCandidateBoundaryDoesNotInstallCandidate(bool dispose)
    {
        var initialExecution = new RecordingRenderExecution();
        var candidateExecution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        var pointers = new LoadingPointerObserverFactory();
        await using var host = new DocumentCanvasHost(
            BpmnModelerTestComposition.DemoFactory,
            CreateRenderer(initialExecution),
            new RecordingSurfaceObserverFactory(surface),
            pointers,
            replacementRendererFactory: () => CreateRenderer(candidateExecution));
        await host.InitializeAsync("active-canvas", "standby-canvas", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var snapshot = document.CaptureSnapshot();
        var before = session.CaptureState();
        var generation = host.CaptureState().DocumentSessionVersion;
        using var cancellation = new CancellationTokenSource();
        var loading = host.LoadDocumentAsync(
            CreateImportedDocument("test:p1.3:late-cancel", 29).CaptureSnapshot(),
            cancellation.Token).AsTask();
        await pointers.Candidate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(session, Session(host));
        Assert.Equal(0, candidateExecution.DisposeCount);

        Task? disposal = null;
        if (dispose)
        {
            disposal = host.DisposeAsync().AsTask();
        }
        else
        {
            cancellation.Cancel();
        }
        // Simulate an observer that completed successfully just as cancellation arrived.
        pointers.Candidate.Release.TrySetResult();
        var result = await loading.WaitAsync(TimeSpan.FromSeconds(10));
        if (disposal is not null)
        {
            await disposal.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(NativeDocumentHostOperationStatus.Cancelled, result.Status);
        Assert.Same(session, Session(host));
        Assert.Same(snapshot, document.CaptureSnapshot());
        Assert.Equal(generation, host.CaptureState().DocumentSessionVersion);
        Assert.Equal("active-canvas", host.CaptureState().ActiveCanvasElementId);
        Assert.Equal(1, candidateExecution.DisposeCount);
        Assert.Equal(1, pointers.Candidate.DisposeCount);
        Assert.Equal(dispose ? 1 : 0, initialExecution.DisposeCount);
        if (!dispose)
        {
            var after = session.CaptureState();
            Assert.Equal(EditingSessionStatus.Ready, after.Status);
            Assert.Same(before.EditorState, after.EditorState);
            Assert.Same(before.CurrentScene, after.CurrentScene);
            Assert.Equal(before.HistoryStatus, after.HistoryStatus);
            Assert.True((await host.ExportNativeDocumentAsync()).Succeeded);
        }
    }

    [Fact]
    public async Task LoadBeforeInitializationAndAfterDisposalHasBoundedOutcome()
    {
        var execution = new RecordingRenderExecution();
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900d, 600d, 1d));
        await using var host = CreateHost(execution, surface);
        var snapshot = CreateImportedDocument("test:p1.3:not-ready-load", 3).CaptureSnapshot();

        var before = await host.LoadDocumentAsync(snapshot);
        Assert.Equal(NativeDocumentHostOperationStatus.Unavailable, before.Status);
        Assert.Equal("CANVAS_DOCUMENT_LOAD_UNAVAILABLE", Assert.Single(before.Diagnostics).Code);
        await host.DisposeAsync();
        var after = await host.LoadDocumentAsync(snapshot);
        Assert.Equal(NativeDocumentHostOperationStatus.Cancelled, after.Status);
    }

    private sealed class LoadingPointerObserverFactory : ICanvasPresentationPointerObserverFactory
    {
        private bool _initialIssued;

        internal LoadingCandidatePointerObserver Candidate { get; } = new();

        public ValueTask<ICanvasPresentationPointerObserver> CreateAsync(
            string canvasElementId,
            Func<CanvasPointerInput, Task> onPointerInput,
            Func<CanvasWheelInput, Task> onWheelInput,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ICanvasPresentationPointerObserver observer = _initialIssued
                ? Candidate
                : new RecordingPointerObserver(null);
            _initialIssued = true;
            return ValueTask.FromResult(observer);
        }
    }

    private sealed class LoadingCandidatePointerObserver : ICanvasPresentationPointerObserver
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int DisposeCount { get; private set; }

        public async ValueTask StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Started.TrySetResult();
            await Release.Task.ConfigureAwait(false);
        }

        public ValueTask ReleaseCaptureAsync(long captureGeneration, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask SetCursorAsync(string cssCursor, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
