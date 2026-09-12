using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    internal VisualStateSnapshot? CaptureVisualStateForInteraction(
        Canvas2DScene expectedScene,
        EditingSessionGeneration expectedGeneration,
        EditorStateSnapshot expectedEditorState,
        VisualStateId visualStateId)
    {
        ArgumentNullException.ThrowIfNull(expectedScene);
        ArgumentNullException.ThrowIfNull(expectedEditorState);
        ArgumentNullException.ThrowIfNull(visualStateId);

        lock (_sync)
        {
            if (_closing || _closed ||
                _status != EditingSessionStatus.Ready ||
                !ReferenceEquals(_currentScene, expectedScene) ||
                _generation != expectedGeneration ||
                !ReferenceEquals(EditorState.CaptureSnapshot(), expectedEditorState))
            {
                return null;
            }

            var snapshot = AttachedDocument.CaptureSnapshot();
            if (snapshot.DocumentId != expectedScene.DocumentId ||
                snapshot.Revision != expectedScene.SourceRevision ||
                !snapshot.VisualModel.TryGetVisualState(visualStateId, out var visualState))
            {
                return null;
            }

            return visualState;
        }
    }

    internal ImmutableArray<VisualStateSnapshot>? CaptureVisualStatesForInteraction(
        Canvas2DScene expectedScene,
        EditingSessionGeneration expectedGeneration,
        EditorStateSnapshot expectedEditorState,
        IEnumerable<VisualStateId> visualStateIds)
    {
        ArgumentNullException.ThrowIfNull(expectedScene);
        ArgumentNullException.ThrowIfNull(expectedEditorState);
        ArgumentNullException.ThrowIfNull(visualStateIds);

        var ids = visualStateIds.ToArray();
        if (Array.Exists(ids, static id => id is null))
        {
            throw new ArgumentException(
                "Interaction Visual State identities cannot contain null values.",
                nameof(visualStateIds));
        }

        Array.Sort(ids, static (left, right) =>
            StringComparer.Ordinal.Compare(left.Value, right.Value));
        for (var index = 1; index < ids.Length; index++)
        {
            if (ids[index - 1] == ids[index])
            {
                throw new ArgumentException(
                    "Interaction Visual State identities cannot contain duplicates.",
                    nameof(visualStateIds));
            }
        }

        lock (_sync)
        {
            if (_closing || _closed ||
                _status != EditingSessionStatus.Ready ||
                !ReferenceEquals(_currentScene, expectedScene) ||
                _generation != expectedGeneration ||
                !ReferenceEquals(EditorState.CaptureSnapshot(), expectedEditorState))
            {
                return null;
            }

            var snapshot = AttachedDocument.CaptureSnapshot();
            if (snapshot.DocumentId != expectedScene.DocumentId ||
                snapshot.Revision != expectedScene.SourceRevision)
            {
                return null;
            }

            var result = ImmutableArray.CreateBuilder<VisualStateSnapshot>(ids.Length);
            foreach (var id in ids)
            {
                if (!snapshot.VisualModel.TryGetVisualState(id, out var visualState) ||
                    visualState is null)
                {
                    return null;
                }

                result.Add(visualState);
            }

            return result.MoveToImmutable();
        }
    }

    internal async ValueTask<EditingSessionOperationResult> ClearHoverFromInteractionAsync(
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested ||
            !await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            return OperationResult(
                EditingSessionOperationStatus.Cancelled,
                [CancelledDiagnostic(_documentId.Value)]);
        }

        Task<EditingSessionOperationResult>? runTask = null;
        try
        {
            lock (_sync)
            {
                if (_closing || _closed)
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Closed,
                        [Error(
                            Canvas2DInteractionDiagnosticCodes.Disposed,
                            "Interaction cannot update a closing or closed Editing Session.",
                            _documentId.Value)]);
                }

                var currentEditorState = EditorState.CaptureSnapshot();
                if (currentEditorState.HoveredObjectId is null)
                {
                    return OperationResult(EditingSessionOperationStatus.Succeeded);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Cancelled,
                        [CancelledDiagnostic(_documentId.Value)]);
                }

                var updatedEditorState = new EditorStateSnapshot(
                    currentEditorState.Selection,
                    hoveredObjectId: null,
                    currentEditorState.ActiveToolId,
                    currentEditorState.FocusTargetId,
                    currentEditorState.Viewport,
                    currentEditorState.ActiveGesture,
                    currentEditorState.TemporaryFeedback,
                    currentEditorState.ToolState,
                    currentEditorState.SemanticSceneSelection);
                if (!EditorState.TryUpdate(currentEditorState, updatedEditorState))
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Superseded,
                        [Error(
                            Canvas2DInteractionDiagnosticCodes.StaleScene,
                            "Editor State changed before hover could be cleared.",
                            _documentId.Value)]);
                }

                var snapshot = AttachedDocument.CaptureSnapshot();
                if (_compatibleArtifacts is { } artifacts &&
                    artifacts.IsCompatibleWith(
                        snapshot.DocumentId,
                        snapshot.Revision,
                        _activeScopeId))
                {
                    var started = BeginRun(
                        snapshot,
                        artifacts,
                        cancellationToken,
                        _generation);
                    runTask = started.Task;
                }
            }
        }
        finally
        {
            _commandGate.Release();
        }

        NotifyStateChanged();
        return runTask is null
            ? OperationResult(EditingSessionOperationStatus.Succeeded)
            : await runTask.ConfigureAwait(false);
    }

    internal async ValueTask<EditingSessionOperationResult> UpdateEditorStateFromInteractionAsync(
        Canvas2DScene expectedScene,
        EditingSessionGeneration expectedGeneration,
        EditorStateSnapshot expectedEditorState,
        EditorStateSnapshot updatedEditorState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedScene);
        ArgumentNullException.ThrowIfNull(expectedEditorState);
        ArgumentNullException.ThrowIfNull(updatedEditorState);
        if (cancellationToken.IsCancellationRequested ||
            !await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            return OperationResult(
                EditingSessionOperationStatus.Cancelled,
                [CancelledDiagnostic(_documentId.Value)]);
        }

        Task<EditingSessionOperationResult>? runTask = null;
        try
        {
            EditingSessionPipelineArtifacts artifacts;
            DocumentSnapshot snapshot;
            lock (_sync)
            {
                if (_closing || _closed)
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Closed,
                        [Error(
                            Canvas2DInteractionDiagnosticCodes.Disposed,
                            "Interaction cannot update a closing or closed Editing Session.",
                            _documentId.Value)]);
                }

                if (_status != EditingSessionStatus.Ready ||
                    !ReferenceEquals(_currentScene, expectedScene) ||
                    _generation != expectedGeneration ||
                    !ReferenceEquals(EditorState.CaptureSnapshot(), expectedEditorState))
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Superseded,
                        [Error(
                            Canvas2DInteractionDiagnosticCodes.StaleScene,
                            "The interaction target no longer belongs to the current Editing Session scene.",
                            expectedScene.DocumentId.Value)]);
                }

                if (expectedEditorState.Equals(updatedEditorState))
                {
                    return OperationResult(EditingSessionOperationStatus.Succeeded);
                }

                if (_artifacts is not { } currentArtifacts ||
                    !currentArtifacts.IsCompatibleWith(
                        expectedScene.DocumentId,
                        expectedScene.SourceRevision,
                        _activeScopeId))
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Rejected,
                        [Error(
                            Canvas2DInteractionDiagnosticCodes.UnavailableSession,
                            "The current Editing Session runtime generation is not compatible with interaction.",
                            expectedScene.DocumentId.Value)]);
                }

                snapshot = AttachedDocument.CaptureSnapshot();
                if (snapshot.DocumentId != expectedScene.DocumentId ||
                    snapshot.Revision != expectedScene.SourceRevision)
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Superseded,
                        [Error(
                            Canvas2DInteractionDiagnosticCodes.StaleScene,
                            "The interaction scene no longer represents the current Document revision.",
                            expectedScene.DocumentId.Value)]);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Cancelled,
                        [CancelledDiagnostic(_documentId.Value)]);
                }

                if (!EditorState.TryUpdate(expectedEditorState, updatedEditorState))
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Superseded,
                        [Error(
                            Canvas2DInteractionDiagnosticCodes.StaleScene,
                            "Editor State changed before the interaction update could be installed.",
                            expectedScene.DocumentId.Value)]);
                }

                artifacts = currentArtifacts;
                // The optimistic validation, Editor State replacement, and transition away
                // from the observed current scene are one session-critical operation. A
                // committed event cannot interleave and leave transient state installed from
                // a scene that has already become stale.
                var started = BeginRun(
                    snapshot,
                    artifacts,
                    cancellationToken,
                    expectedGeneration,
                    EditingSessionStatus.Ready);
                runTask = started.Task ?? throw new InvalidOperationException(
                    "A validated interaction scene rebuild could not be started atomically.");
            }
        }
        finally
        {
            _commandGate.Release();
        }

        NotifyStateChanged();
        return await runTask.ConfigureAwait(false);
    }

    internal ValueTask<EditingSessionOperationResult> ClearMoveGestureFromInteractionAsync(
        string expectedGestureId,
        CancellationToken cancellationToken = default) =>
        ClearPersistentGestureFromInteractionAsync(expectedGestureId, cancellationToken);

    internal async ValueTask<EditingSessionOperationResult> ClearPersistentGestureFromInteractionAsync(
        string expectedGestureId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedGestureId);
        if (cancellationToken.IsCancellationRequested ||
            !await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            return OperationResult(
                EditingSessionOperationStatus.Cancelled,
                [CancelledDiagnostic(_documentId.Value)]);
        }

        Task<EditingSessionOperationResult>? runTask = null;
        try
        {
            lock (_sync)
            {
                if (_closing || _closed)
                {
                    return OperationResult(EditingSessionOperationStatus.Closed);
                }

                var currentEditorState = EditorState.CaptureSnapshot();
                var gesture = currentEditorState.ActiveGesture;
                if (gesture is null ||
                    !StringComparer.Ordinal.Equals(gesture.Id, expectedGestureId))
                {
                    return OperationResult(EditingSessionOperationStatus.Succeeded);
                }

                var cleared = CopyEditorStateWithoutGesture(currentEditorState);
                if (!EditorState.TryUpdate(currentEditorState, cleared))
                {
                    return OperationResult(
                        EditingSessionOperationStatus.Superseded,
                        [Error(
                            Canvas2DInteractionDiagnosticCodes.StaleGesture,
                            "The active persistent gesture changed before it could be cleared.",
                            _documentId.Value)]);
                }

                var snapshot = AttachedDocument.CaptureSnapshot();
                if (_status == EditingSessionStatus.Ready &&
                    _artifacts is { } artifacts &&
                    artifacts.IsCompatibleWith(
                        snapshot.DocumentId,
                        snapshot.Revision,
                        _activeScopeId))
                {
                    var started = BeginRun(
                        snapshot,
                        artifacts,
                        CancellationToken.None,
                        _generation,
                        EditingSessionStatus.Ready);
                    runTask = started.Task;
                }
            }
        }
        finally
        {
            _commandGate.Release();
        }

        NotifyStateChanged();
        return runTask is null
            ? OperationResult(EditingSessionOperationStatus.Succeeded)
            : await runTask.ConfigureAwait(false);
    }

    internal ValueTask<HistoryOperationResult> CompleteMoveGestureAsync(
        Canvas2DScene expectedScene,
        EditingSessionGeneration expectedGeneration,
        EditorStateSnapshot expectedEditorState,
        EditorStateSnapshot clearedEditorState,
        MoveVisualStateCommand command,
        CancellationToken cancellationToken = default) =>
        CompletePersistentGestureAsync(
            expectedScene,
            expectedGeneration,
            expectedEditorState,
            clearedEditorState,
            command,
            cancellationToken);

    internal async ValueTask<HistoryOperationResult> CompletePersistentGestureAsync(
        Canvas2DScene expectedScene,
        EditingSessionGeneration expectedGeneration,
        EditorStateSnapshot expectedEditorState,
        EditorStateSnapshot clearedEditorState,
        ICommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedScene);
        ArgumentNullException.ThrowIfNull(expectedEditorState);
        ArgumentNullException.ThrowIfNull(clearedEditorState);
        ArgumentNullException.ThrowIfNull(command);
        if (cancellationToken.IsCancellationRequested ||
            !await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            var cancelled = CaptureState();
            return HistoryOperationResult.CreateNotCommitted(
                cancelled.DocumentId,
                command.TypeId,
                HistoryOperationStatus.Cancelled,
                cancelled.DocumentRevision,
                cancelled.HistoryStatus,
                [CancelledDiagnostic(cancelled.DocumentId.Value)]);
        }

        Task<EditingSessionOperationResult>? cleanupTask = null;
        HistoryOperationResult result;
        try
        {
            DocumentSnapshot snapshot;
            EditingSessionPipelineArtifacts artifacts;
            lock (_sync)
            {
                var state = CaptureStateUnderLock();
                if (_closing || _closed ||
                    _status != EditingSessionStatus.Ready ||
                    !ReferenceEquals(_currentScene, expectedScene) ||
                    _generation != expectedGeneration ||
                    !ReferenceEquals(EditorState.CaptureSnapshot(), expectedEditorState) ||
                    command.TargetDocumentId != expectedScene.DocumentId ||
                    command.ExpectedRevision != expectedScene.SourceRevision)
                {
                    return InteractionCommandRejected(
                        command,
                        state,
                        Canvas2DInteractionDiagnosticCodes.StaleGesture,
                        "The persistent gesture no longer belongs to the current Editing Session generation.");
                }

                snapshot = AttachedDocument.CaptureSnapshot();
                if (snapshot.DocumentId != command.TargetDocumentId ||
                    snapshot.Revision != command.ExpectedRevision ||
                    !IsCurrentVisualSelection(
                        snapshot, expectedScene, _activeScopeId, expectedEditorState) ||
                    _artifacts is not { } currentArtifacts ||
                    !currentArtifacts.IsCompatibleWith(
                        snapshot.DocumentId,
                        snapshot.Revision,
                        _activeScopeId))
                {
                    return InteractionCommandRejected(
                        command,
                        state,
                        Canvas2DInteractionDiagnosticCodes.StaleGesture,
                        "The persistent gesture no longer represents the current Document revision.");
                }

                if (!EditorState.TryUpdate(expectedEditorState, clearedEditorState))
                {
                    return InteractionCommandRejected(
                        command,
                        state,
                        Canvas2DInteractionDiagnosticCodes.StaleGesture,
                        "Editor State changed before the persistent gesture could complete.");
                }

                artifacts = currentArtifacts;
            }

            lock (_sync)
            {
                if (_closing || _closed)
                {
                    return InteractionCommandRejected(
                        command,
                        CaptureStateUnderLock(),
                        EditingSessionDiagnosticCodes.Closed,
                        "The persistent gesture cannot commit after the Editing Session begins closing.");
                }
            }

            using var commandCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetime.Token);
            result = await History.ExecuteAsync(
                _commandProcessor,
                command,
                commandCancellation.Token).ConfigureAwait(false);
            RecordExpectedEvent(result);

            if (!result.IsCommitted)
            {
                lock (_sync)
                {
                    if (!_closing && !_closed &&
                        _status == EditingSessionStatus.Ready &&
                        ReferenceEquals(_currentScene, expectedScene) &&
                        _generation == expectedGeneration &&
                        artifacts.IsCompatibleWith(
                            snapshot.DocumentId,
                            snapshot.Revision,
                            _activeScopeId))
                    {
                        var started = BeginRun(
                            snapshot,
                            artifacts,
                            CancellationToken.None,
                            expectedGeneration,
                            EditingSessionStatus.Ready);
                        cleanupTask = started.Task;
                    }
                }
            }
        }
        finally
        {
            _commandGate.Release();
        }

        if (cleanupTask is not null)
        {
            NotifyStateChanged();
            _ = await cleanupTask.ConfigureAwait(false);
        }

        return result;
    }

    private static HistoryOperationResult InteractionCommandRejected(
        ICommand command,
        EditingSessionState state,
        string diagnosticCode,
        string message) =>
        HistoryOperationResult.CreateNotCommitted(
            state.DocumentId,
            command.TypeId,
            HistoryOperationStatus.CommandFailed,
            state.DocumentRevision,
            state.HistoryStatus,
            [Error(diagnosticCode, message, state.DocumentId.Value)]);

    private static EditorStateSnapshot CopyEditorStateWithoutGesture(
        EditorStateSnapshot source) =>
        new(
            source.Selection,
            source.HoveredObjectId,
            source.ActiveToolId,
            source.FocusTargetId,
            source.Viewport,
            activeGesture: null,
            source.TemporaryFeedback,
            source.ToolState,
            source.SemanticSceneSelection);
}
