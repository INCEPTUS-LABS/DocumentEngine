using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.History;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    private EditingSessionState CaptureStateUnderLock()
    {
        var snapshot = _document?.CaptureSnapshot();
        if (snapshot is not null)
        {
            _lastDocumentRevision = snapshot.Revision;
            _lastModelProfileState = snapshot.SemanticModel.ModelProfiles;
        }
        var observedStatus = _status;
        var observedCurrent = _currentScene;
        var observedStale = _lastKnownGoodScene;
        var observedArtifacts = _artifacts;
        if (observedStatus == EditingSessionStatus.Ready &&
            _currentScene is not null &&
            snapshot is not null &&
            (_currentScene.SourceRevision != snapshot.Revision ||
             _artifacts?.ScopeId != _activeScopeId))
        {
            // Atomic Document installation precedes outside-gate event dispatch. During that
            // short window report conservative Rebuilding currency without mutating session
            // ownership or starting a duplicate pipeline run; the queued event performs the
            // actual invalidation exactly once.
            observedStatus = EditingSessionStatus.Rebuilding;
            observedStale = _currentScene;
            observedCurrent = null;
            observedArtifacts = null;
        }

        return new EditingSessionState(
            _documentId,
            snapshot?.Revision ?? _lastDocumentRevision,
            _activeScopeId,
            observedStatus,
            _generation,
            observedCurrent,
            observedStale,
            observedStatus == EditingSessionStatus.Ready ? observedArtifacts?.ProjectedGraph : null,
            observedStatus == EditingSessionStatus.Ready ? observedArtifacts?.LayoutResult : null,
            observedStatus == EditingSessionStatus.Ready ? observedArtifacts?.RoutingResult : null,
            _editorState?.CaptureSnapshot() ?? _finalEditorState,
            _history?.CaptureStatus() ?? _finalHistoryStatus,
            _runtimeDiagnostics,
            _presentationDiagnostics,
            _closed,
            _modelProfileCatalog,
            snapshot?.SemanticModel.ModelProfiles ?? _lastModelProfileState,
            _activeModelProfileViewState,
            _activeModelProfileElementViewState,
            _presentedGeneration == _generation);
    }

    private bool IsUnavailable(out EditingSessionState state)
    {
        lock (_sync)
        {
            state = CaptureStateUnderLock();
            return _closing || _closed;
        }
    }

    private bool IsHistoryRestorationUnavailable(out EditingSessionState state)
    {
        lock (_sync)
        {
            state = CaptureStateUnderLock();
            return _closing || _closed || _status == EditingSessionStatus.Rebuilding;
        }
    }

    private HistoryOperationResult UnavailableHistoryResult(
        ICommand? command,
        EditingSessionState state) =>
        HistoryOperationResult.CreateNotCommitted(
            state.DocumentId,
            command?.TypeId,
            HistoryOperationStatus.CommandFailed,
            state.DocumentRevision,
            state.HistoryStatus,
            [Error(
                _closed || _closing
                    ? EditingSessionDiagnosticCodes.Closed
                    : EditingSessionDiagnosticCodes.InvalidOperation,
                _closed || _closing
                    ? "The Editing Session is closed."
                    : "Undo and Redo cannot begin while the Editing Session is rebuilding.",
                state.DocumentId.Value)]);

    private EditingSessionOperationResult OperationResult(
        EditingSessionOperationStatus status,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        lock (_sync)
        {
            return new EditingSessionOperationResult(
                status,
                CaptureStateUnderLock(),
                diagnostics);
        }
    }

    private void NotifyStateChanged()
    {
        lock (_notificationGate)
        {
            EventHandler<EditingSessionStateChangedEventArgs>? handlers;
            EditingSessionState state;
            lock (_sync)
            {
                if (_closing || _closed)
                {
                    return;
                }

                handlers = StateChanged;
                state = CaptureStateUnderLock();
            }

            if (handlers is null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList()
                         .Cast<EventHandler<EditingSessionStateChangedEventArgs>>())
            {
#pragma warning disable CA1031 // Presentation observers cannot corrupt session state.
                try
                {
                    handler(this, new EditingSessionStateChangedEventArgs(state));
                }
                catch (Exception exception) when (IsNonFatal(exception))
                {
                    lock (_sync)
                    {
                        _presentationDiagnostics = EditingSessionDiagnosticCollection.CopyAndOrder(
                            _presentationDiagnostics.Add(Error(
                                EditingSessionDiagnosticCodes.NotificationFailure,
                                "An Editing Session state observer failed.",
                                state.DocumentId.Value,
                                new KeyValuePair<string, string>(
                                    "ExceptionType",
                                    exception.GetType().FullName ?? exception.GetType().Name))),
                            nameof(_presentationDiagnostics));
                    }
                }
#pragma warning restore CA1031
            }
        }
    }

    private static Diagnostic Error(
        string code,
        string message,
        string sourceIdentity,
        params KeyValuePair<string, string>[] context) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity, context);

    private static Diagnostic CancelledDiagnostic(string sourceIdentity) =>
        new(
            EditingSessionDiagnosticCodes.PipelineCancelled,
            DiagnosticSeverity.Information,
            "The Editing Session operation was cancelled.",
            sourceIdentity);

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;

    private static TaskCompletionSource NewStatePulse() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void PulseStateUnderLock()
    {
        var preceding = _statePulse;
        _statePulse = NewStatePulse();
        preceding.TrySetResult();
    }
}
