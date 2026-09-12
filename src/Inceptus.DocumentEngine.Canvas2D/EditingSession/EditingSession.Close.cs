using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Profiles;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    private async Task WaitForIdleCoreAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task<EditingSessionOperationResult>[] activeRuns;
            Task? closeTask;
            Task pulse;
            bool awaitingEvent;
            lock (_sync)
            {
                if (_closed)
                {
                    return;
                }

                closeTask = _closing ? _closeTask : null;
                activeRuns = [.. _activeRuns.Values];
                pulse = _statePulse.Task;
                awaitingEvent = _observedEventRevision < _expectedEventRevision;
            }

            if (closeTask is not null)
            {
                await closeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (awaitingEvent)
            {
                await pulse.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            foreach (var run in activeRuns)
            {
#pragma warning disable CA1031 // Idle observation waits through isolated run faults.
                try
                {
                    await run.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    IsNonFatal(exception) &&
                    exception is not OperationCanceledException)
                {
                    // Pipeline faults are represented by runtime diagnostics and status.
                }
#pragma warning restore CA1031
            }

            lock (_sync)
            {
                if (_observedEventRevision >= _expectedEventRevision &&
                    _activeRuns.Count == 0)
                {
                    return;
                }

                pulse = _statePulse.Task;
            }

            await pulse.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<EditingSessionOperationResult> CloseCoreAsync()
    {
        await _commandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await AwaitAllPipelineRunsAsync().ConfigureAwait(false);

            await _rendererGate.WaitAsync().ConfigureAwait(false);
            Canvas2DScene? current;
            Canvas2DScene? stale;
            Canvas2DScene? gestureFallback;
            try
            {
                lock (_sync)
                {
                    current = _currentScene;
                    stale = _lastKnownGoodScene;
                    gestureFallback = _activeGestureFallbackScene;
                    _currentScene = null;
                    _lastKnownGoodScene = null;
                    _activeGestureFallbackScene = null;
                    _currentSceneHasActiveGesture = false;
                    _lastKnownGoodSceneNeedsRender = false;
                    _artifacts = null;
                    _compatibleArtifacts = null;
                    _inactiveScopeViews.Clear();
                    _inactiveScopeArtifacts.Clear();
                    _activeModelProfileElementViewState =
                        ModelProfileElementViewStateSnapshot.Empty;
                    _lastDocumentRevision = _document?.Revision ?? _lastDocumentRevision;
                    _lastModelProfileState = _document?.SemanticModel.ModelProfiles ??
                        _lastModelProfileState;
                    _finalEditorState = _editorState?.CaptureSnapshot() ?? _finalEditorState;
                    _finalHistoryStatus = _history?.CaptureStatus() ?? _finalHistoryStatus;
                    _history = null;
                    _editorState = null;
                    _document = null;
                }

                current?.Dispose();
                if (!ReferenceEquals(current, stale))
                {
                    stale?.Dispose();
                }
                if (!ReferenceEquals(gestureFallback, current) &&
                    !ReferenceEquals(gestureFallback, stale))
                {
                    gestureFallback?.Dispose();
                }

                await _renderer.DisposeAsync().ConfigureAwait(false);
                lock (_sync)
                {
                    if (!_renderer.DisposalDiagnostics.IsEmpty)
                    {
                        _presentationDiagnostics = EditingSessionDiagnosticCollection.CopyAndOrder(
                            _renderer.DisposalDiagnostics,
                            nameof(_renderer.DisposalDiagnostics));
                    }

                    _closed = true;
                    PulseStateUnderLock();
                }
            }
            finally
            {
                _rendererGate.Release();
                _lifetime.Dispose();
            }
        }
        finally
        {
            _commandGate.Release();
        }

        return OperationResult(
            EditingSessionOperationStatus.Closed,
            _renderer.DisposalDiagnostics);
    }

    private async Task AwaitAllPipelineRunsAsync()
    {
        while (true)
        {
            Task<EditingSessionOperationResult>[] activeRuns;
            CancellationTokenSource? currentCancellation;
            lock (_sync)
            {
                currentCancellation = _runCancellation;
                activeRuns = [.. _activeRuns.Values];
            }

            CancelRun(currentCancellation);
            if (activeRuns.Length == 0)
            {
                return;
            }

            foreach (var run in activeRuns)
            {
#pragma warning disable CA1031 // Closing continues after an isolated pipeline fault.
                try
                {
                    await run.ConfigureAwait(false);
                }
                catch (Exception exception) when (IsNonFatal(exception))
                {
                    lock (_sync)
                    {
                        _runtimeDiagnostics = EditingSessionDiagnosticCollection.CopyAndOrder(
                            _runtimeDiagnostics.Add(Error(
                                EditingSessionDiagnosticCodes.ResourceDisposalFailed,
                                "An in-flight pipeline run failed while the session was closing.",
                                _documentId.Value,
                                new KeyValuePair<string, string>(
                                    "ExceptionType",
                                    exception.GetType().FullName ?? exception.GetType().Name))),
                            nameof(_runtimeDiagnostics));
                    }
                }
#pragma warning restore CA1031
            }

            Task trackingPulse;
            lock (_sync)
            {
                if (_activeRuns.Count == 0)
                {
                    return;
                }

                trackingPulse = _statePulse.Task;
            }

            await trackingPulse.ConfigureAwait(false);
        }
    }
}
