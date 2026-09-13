using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.EditorState;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

/// <summary>
/// Owns one active Document attachment and all transient editing runtime state.
/// </summary>
public sealed partial class EditingSession : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly object _notificationGate = new();
    private Document? _document;
    private readonly DocumentId _documentId;
    private readonly Canvas2DRenderer _renderer;
    private readonly ISessionPipelineProcessing _pipeline;
    private EditorStateStore? _editorState;
    private HistoryManager? _history;
    private readonly CommandProcessor _commandProcessor;
    private readonly EditingSessionDocumentChangedSubscriber _documentChangedSubscriber;
    private readonly SemaphoreSlim _rendererGate = new(1, 1);
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private EditingSessionStatus _status = EditingSessionStatus.Rebuilding;
    private EditingSessionGeneration _generation;
    private EditingSessionGeneration? _presentedGeneration;
    private Canvas2DScene? _currentScene;
    private Canvas2DScene? _lastKnownGoodScene;
    private Canvas2DScene? _activeGestureFallbackScene;
    private bool _currentSceneHasActiveGesture;
    private bool _lastKnownGoodSceneNeedsRender;
    private EditingSessionPipelineArtifacts? _artifacts;
    private EditingSessionPipelineArtifacts? _compatibleArtifacts;
    private ImmutableArray<NodeLayoutHistoryEntry> _nodeLayoutHistory = [];
    private ImmutableArray<Diagnostic> _runtimeDiagnostics = [];
    private ImmutableArray<Diagnostic> _presentationDiagnostics = [];
    private CancellationTokenSource? _runCancellation;
    private readonly Dictionary<long, Task<EditingSessionOperationResult>> _activeRuns = [];
    private readonly Dictionary<DocumentScopeId, ScopeViewRuntimeState>
        _inactiveScopeViews = [];
    private readonly Dictionary<DocumentScopeId, ScopePipelineRuntimeState>
        _inactiveScopeArtifacts = [];
    private readonly ModelProfileCatalog _modelProfileCatalog;
    private ModelProfileViewStateSnapshot _activeModelProfileViewState;
    private ModelProfileElementViewStateSnapshot _activeModelProfileElementViewState;
    private ModelProfileStateSnapshot _lastModelProfileState;
    private DocumentScopeId _activeScopeId;
    private ImmutableArray<DocumentScopeId> _activeScopePath;
    private Task? _closeTask;
    private TaskCompletionSource _statePulse = NewStatePulse();
    private DocumentRevision _expectedEventRevision;
    private DocumentRevision _observedEventRevision;
    private DocumentRevision _lastDocumentRevision;
    private EditorStateSnapshot _finalEditorState;
    private HistoryStatus _finalHistoryStatus;
    private bool _closing;
    private bool _closed;

    private EditingSession(
        Document document,
        Canvas2DRenderer renderer,
        EditingSessionConfiguration configuration,
        ISessionPipelineProcessing pipeline)
    {
        _document = document;
        _documentId = document.DocumentId;
        _renderer = renderer;
        _pipeline = pipeline;
        ConnectorAnchorPolicyProvider = configuration.ConnectorAnchorPolicyProvider;
        _modelProfileCatalog = configuration.ModelProfileCatalog;
        _activeScopeId = document.SemanticModel.RootScopeId;
        _activeScopePath = [_activeScopeId];
        _activeModelProfileViewState = configuration.InitialModelProfileViewState;
        _activeModelProfileElementViewState = ModelProfileElementViewStateSnapshot.Empty;
        _lastModelProfileState = document.SemanticModel.ModelProfiles;
        _editorState = new EditorStateStore(configuration.InitialEditorState);
        _history = new HistoryManager(document);
        _lastDocumentRevision = document.Revision;
        _finalEditorState = configuration.InitialEditorState;
        _finalHistoryStatus = _history.CaptureStatus();
        _expectedEventRevision = document.Revision;
        _observedEventRevision = document.Revision;
        _documentChangedSubscriber = new EditingSessionDocumentChangedSubscriber(this, _notificationGate);
        var subscribers = new IDocumentChangedSubscriber[]
            { _documentChangedSubscriber }
            .Concat(configuration.DocumentChangedSubscribers);
        _commandProcessor = new CommandProcessor(
            configuration.CommandHandlers,
            configuration.CommandValidators,
            subscribers,
            configuration.HistoryPolicies,
            configuration.ConnectorAnchorPolicyProvider);
    }

    public event EventHandler<EditingSessionStateChangedEventArgs>? StateChanged;

    public IElementConnectorAnchorPolicyProvider ConnectorAnchorPolicyProvider { get; }

    public ModelProfileCatalog ModelProfileCatalog => _modelProfileCatalog;

    public static ValueTask<EditingSessionAttachResult> AttachAsync(
        Document document,
        Canvas2DRenderer renderer,
        EditingSessionConfiguration configuration,
        CancellationToken cancellationToken = default) =>
        AttachCoreAsync(document, renderer, configuration, pipeline: null, cancellationToken);

    internal static ValueTask<EditingSessionAttachResult> AttachAsync(
        Document document,
        Canvas2DRenderer renderer,
        EditingSessionConfiguration configuration,
        ISessionPipelineProcessing pipeline,
        CancellationToken cancellationToken = default) =>
        AttachCoreAsync(document, renderer, configuration, pipeline, cancellationToken);

    public EditingSessionState CaptureState()
    {
        lock (_sync)
        {
            return CaptureStateUnderLock();
        }
    }

    public ValueTask<HistoryOperationResult> ExecuteAsync(
        ICommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteCoreAsync(command, cancellationToken);
    }

    /// <summary>
    /// Executes one Command only when the expected Visual State is still the sole canonical
    /// selection at the serialized session command boundary.
    /// </summary>
    public ValueTask<HistoryOperationResult> ExecuteAsync(
        ICommand command,
        VisualStateId expectedSelectedVisualStateId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(expectedSelectedVisualStateId);
        return ExecuteForSelectionCoreAsync(
            expectedSelectedVisualStateId,
            requireSoleSelection: true,
            command,
            cancellationToken);
    }

    /// <summary>
    /// Executes one Command only when the expected Visual State is still the sole canonical
    /// selection at the serialized session command boundary.
    /// </summary>
    public ValueTask<HistoryOperationResult> ExecuteForSingleSelectionAsync(
        VisualStateId expectedSelectedVisualStateId,
        ICommand command,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(command, expectedSelectedVisualStateId, cancellationToken);
    }

    /// <summary>
    /// Executes one Command only when the expected Visual State remains a member of the
    /// canonical selection at the serialized session command boundary.
    /// </summary>
    public ValueTask<HistoryOperationResult> ExecuteForSelectedVisualStateAsync(
        VisualStateId expectedSelectedVisualStateId,
        ICommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedSelectedVisualStateId);
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteForSelectionCoreAsync(
            expectedSelectedVisualStateId,
            requireSoleSelection: false,
            command,
            cancellationToken);
    }

    /// <summary>
    /// Executes one Command only while the expected semantic-only Scene target remains the
    /// canonical transient selection and ordinary Visual State selection is empty.
    /// </summary>
    public ValueTask<HistoryOperationResult> ExecuteForSemanticSceneSelectionAsync(
        SemanticElementId expectedSelectedSemanticElementId,
        ICommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedSelectedSemanticElementId);
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteForSemanticSceneSelectionCoreAsync(
            expectedSelectedSemanticElementId,
            command,
            cancellationToken);
    }

    public ValueTask<HistoryOperationResult> UndoAsync(
        CancellationToken cancellationToken = default)
    {
        return ExecuteRestorationCoreAsync(isUndo: true, cancellationToken);
    }

    public ValueTask<HistoryOperationResult> RedoAsync(
        CancellationToken cancellationToken = default)
    {
        return ExecuteRestorationCoreAsync(isUndo: false, cancellationToken);
    }

    public ValueTask<EditingSessionOperationResult> UpdateEditorStateAsync(
        EditorStateSnapshot editorState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(editorState);
        return UpdateEditorStateCoreAsync(editorState, cancellationToken);
    }

    /// <summary>
    /// Changes the transient semantic scope currently edited by this session without creating a
    /// Command or modifying the authoritative Document. Successful user navigation records one
    /// runtime entry in the session's global History.
    /// </summary>
    public ValueTask<EditingSessionOperationResult> NavigateToScopeAsync(
        DocumentScopeId scopeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        return NavigateToScopeCoreAsync(scopeId, cancellationToken);
    }

    /// <summary>
    /// Updates transient viewport zoom and pan while preserving every unrelated Editor State
    /// value and the current visible-document-region observation.
    /// </summary>
    public ValueTask<EditingSessionOperationResult> UpdateViewportAsync(
        ViewportSnapshot viewport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        return UpdateViewportCoreAsync(viewport, cancellationToken);
    }

    /// <summary>
    /// Atomically replaces the transient optional-profile visibility preferences for the
    /// active scope view. This operation never mutates the Document or global History.
    /// </summary>
    public ValueTask<EditingSessionOperationResult> UpdateModelProfileViewStateAsync(
        ModelProfileViewStateSnapshot modelProfileViewState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modelProfileViewState);
        return UpdateModelProfileViewStateCoreAsync(
            modelProfileViewState,
            cancellationToken);
    }

    /// <summary>
    /// Translates the rendered Document in Canvas-local CSS units without changing Zoom or any
    /// persistent Document state.
    /// </summary>
    public ValueTask<EditingSessionOperationResult> PanViewportAsync(
        VectorD canvasTranslation,
        CancellationToken cancellationToken = default) =>
        PanViewportCoreAsync(canvasTranslation, cancellationToken);

    public ValueTask<EditingSessionOperationResult> RetryAsync(
        CancellationToken cancellationToken = default) =>
        RetryCoreAsync(cancellationToken);

    public ValueTask<Canvas2DRendererResult> ResizeAsync(
        Canvas2DSurfaceSize surfaceSize,
        CancellationToken cancellationToken = default) =>
        ResizeCoreAsync(surfaceSize, cancellationToken);

    public ValueTask<Canvas2DRendererResult> RenderCurrentAsync(
        CancellationToken cancellationToken = default) =>
        RenderCurrentCoreAsync(cancellationToken);

    public ValueTask WaitForIdleAsync(CancellationToken cancellationToken = default) =>
        new(WaitForIdleCoreAsync(cancellationToken));

    public ValueTask<EditingSessionOperationResult> CloseAsync()
    {
        lock (_notificationGate)
        {
            Task<EditingSessionOperationResult> task;
            CancellationTokenSource? runCancellation;
            TaskCompletionSource<EditingSessionOperationResult> completion;
            lock (_sync)
            {
                if (_closeTask is Task<EditingSessionOperationResult> existing)
                {
                    return new ValueTask<EditingSessionOperationResult>(existing);
                }

                _closing = true;
                runCancellation = _runCancellation;
                completion = new TaskCompletionSource<EditingSessionOperationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _closeTask = task = completion.Task;
                PulseStateUnderLock();
            }

            _documentChangedSubscriber.Detach();
            CancelRun(runCancellation);
            _lifetime.Cancel();
            _ = CompleteCloseAsync(completion);
            return new ValueTask<EditingSessionOperationResult>(task);
        }
    }

    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

    internal void ObserveDocumentChanged(DocumentChangedEvent change) =>
        ObserveDocumentChangedCore(change);

    private Document AttachedDocument =>
        _document ?? throw new InvalidOperationException("The Document is no longer attached.");

    private EditorStateStore EditorState =>
        _editorState ?? throw new InvalidOperationException("Editor State is no longer active.");

    private HistoryManager History =>
        _history ?? throw new InvalidOperationException("History is no longer active.");

    private static void CancelRun(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The owning run completed and disposed its source after the caller captured
            // the reference. Completion already satisfies the requested cancellation.
        }
    }

    private async Task CompleteCloseAsync(
        TaskCompletionSource<EditingSessionOperationResult> completion)
    {
        try
        {
            completion.TrySetResult(await CloseCoreAsync().ConfigureAwait(false));
        }
#pragma warning disable CA1031 // The shared close task exposes unexpected cleanup faults.
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
#pragma warning restore CA1031
    }

    private static async ValueTask<EditingSessionAttachResult> AttachCoreAsync(
        Document document,
        Canvas2DRenderer renderer,
        EditingSessionConfiguration configuration,
        ISessionPipelineProcessing? pipeline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!renderer.IsInitialized || renderer.IsDisposed)
        {
            await renderer.DisposeAsync().ConfigureAwait(false);
            return new EditingSessionAttachResult(
                EditingSessionAttachStatus.Failed,
                null,
                [Error(
                    EditingSessionDiagnosticCodes.InvalidAttachment,
                    "Editing Session attachment requires one initialized, active Canvas2DRenderer.",
                    document.DocumentId.Value)]);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await renderer.DisposeAsync().ConfigureAwait(false);
            return new EditingSessionAttachResult(
                EditingSessionAttachStatus.Cancelled,
                null,
                [CancelledDiagnostic(document.DocumentId.Value)]);
        }

        EditingSession session;
#pragma warning disable CA1031 // Composition failures become stable attachment diagnostics.
        try
        {
            session = new EditingSession(
                document,
                renderer,
                configuration,
                pipeline ?? new EditingSessionPipeline(configuration, renderer));
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            await renderer.DisposeAsync().ConfigureAwait(false);
            return new EditingSessionAttachResult(
                EditingSessionAttachStatus.Failed,
                null,
                [Error(
                    EditingSessionDiagnosticCodes.InvalidAttachment,
                    "Editing Session runtime composition failed.",
                    document.DocumentId.Value,
                    new KeyValuePair<string, string>(
                        "ExceptionType",
                        exception.GetType().FullName ?? exception.GetType().Name))]);
        }
#pragma warning restore CA1031
        EditingSessionOperationResult initial;
#pragma warning disable CA1031 // Initial runtime failures become stable attachment diagnostics.
        try
        {
            initial = await session.StartFullRebuildAsync(
                document.CaptureSnapshot(),
                awaitCompletion: true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            await session.CloseAsync().ConfigureAwait(false);
            return new EditingSessionAttachResult(
                EditingSessionAttachStatus.Failed,
                null,
                [Error(
                    EditingSessionDiagnosticCodes.PipelineFailed,
                    "Initial Editing Session processing failed.",
                    document.DocumentId.Value,
                    new KeyValuePair<string, string>(
                        "ExceptionType",
                        exception.GetType().FullName ?? exception.GetType().Name))]);
        }
#pragma warning restore CA1031
        if (initial.Status == EditingSessionOperationStatus.Cancelled)
        {
            await session.CloseAsync().ConfigureAwait(false);
            return new EditingSessionAttachResult(
                EditingSessionAttachStatus.Cancelled,
                null,
                initial.Diagnostics);
        }

        if (initial.Status == EditingSessionOperationStatus.Superseded)
        {
            try
            {
                await session.WaitForIdleAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await session.CloseAsync().ConfigureAwait(false);
                return new EditingSessionAttachResult(
                    EditingSessionAttachStatus.Cancelled,
                    null,
                    [CancelledDiagnostic(document.DocumentId.Value)]);
            }
        }

        return new EditingSessionAttachResult(
            session.CaptureState().Status == EditingSessionStatus.Ready
                ? EditingSessionAttachStatus.Ready
                : EditingSessionAttachStatus.RuntimeFaulted,
            session,
            initial.Diagnostics);
    }
}
