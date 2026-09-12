using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    private async ValueTask<HistoryOperationResult> ExecuteCoreAsync(
        ICommand command,
        CancellationToken cancellationToken)
    {
        if (!await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            var cancelledState = CaptureState();
            return HistoryOperationResult.CreateNotCommitted(
                cancelledState.DocumentId,
                command.TypeId,
                HistoryOperationStatus.Cancelled,
                cancelledState.DocumentRevision,
                cancelledState.HistoryStatus,
                [CancelledDiagnostic(cancelledState.DocumentId.Value)]);
        }

        try
        {
            if (IsUnavailable(out var state))
            {
                return UnavailableHistoryResult(command, state);
            }

            var result = await History.ExecuteAsync(
                _commandProcessor,
                command,
                cancellationToken).ConfigureAwait(false);
            RecordExpectedEvent(result);
            return result;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async ValueTask<HistoryOperationResult> ExecuteForSelectionCoreAsync(
        VisualStateId expectedSelectedVisualStateId,
        bool requireSoleSelection,
        ICommand command,
        CancellationToken cancellationToken)
    {
        if (!await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            var cancelledState = CaptureState();
            return HistoryOperationResult.CreateNotCommitted(
                cancelledState.DocumentId,
                command.TypeId,
                HistoryOperationStatus.Cancelled,
                cancelledState.DocumentRevision,
                cancelledState.HistoryStatus,
                [CancelledDiagnostic(cancelledState.DocumentId.Value)]);
        }

        try
        {
            if (IsUnavailable(out var state))
            {
                return UnavailableHistoryResult(command, state);
            }

            var selectionMatches = requireSoleSelection
                ? state.EditorState.Selection.Length == 1 &&
                    state.EditorState.Selection[0] == expectedSelectedVisualStateId
                : state.EditorState.Selection.Contains(expectedSelectedVisualStateId);
            if (!selectionMatches)
            {
                return HistoryOperationResult.CreateNotCommitted(
                    state.DocumentId,
                    command.TypeId,
                    HistoryOperationStatus.CommandFailed,
                    state.DocumentRevision,
                    state.HistoryStatus,
                    [Error(
                        EditingSessionDiagnosticCodes.SelectionChanged,
                        requireSoleSelection
                            ? "The Command was not executed because the expected Visual State is no longer the sole selection."
                            : "The Command was not executed because the expected Visual State is no longer selected.",
                        expectedSelectedVisualStateId.Value)]);
            }

            var result = await History.ExecuteAsync(
                _commandProcessor,
                command,
                cancellationToken).ConfigureAwait(false);
            RecordExpectedEvent(result);
            return result;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async ValueTask<HistoryOperationResult> ExecuteRestorationCoreAsync(
        bool isUndo,
        CancellationToken cancellationToken)
    {
        if (!await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            var cancelledState = CaptureState();
            return HistoryOperationResult.CreateNotCommitted(
                cancelledState.DocumentId,
                commandTypeId: null,
                HistoryOperationStatus.Cancelled,
                cancelledState.DocumentRevision,
                cancelledState.HistoryStatus,
                [CancelledDiagnostic(cancelledState.DocumentId.Value)]);
        }

        try
        {
            if (IsHistoryRestorationUnavailable(out var state))
            {
                return UnavailableHistoryResult(command: null, state);
            }

            var result = isUndo
                ? await History.UndoAsync(
                    _commandProcessor,
                    ReplayScopeNavigationUnderCommandGateAsync,
                    cancellationToken)
                    .ConfigureAwait(false)
                : await History.RedoAsync(
                    _commandProcessor,
                    ReplayScopeNavigationUnderCommandGateAsync,
                    cancellationToken)
                    .ConfigureAwait(false);
            RecordExpectedEvent(result);
            if (result.IsApplied)
            {
                NotifyStateChanged();
            }
            return result;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async ValueTask<HistoryOperationResult> ExecuteForSemanticSceneSelectionCoreAsync(
        SemanticElementId expectedSelectedSemanticElementId,
        ICommand command,
        CancellationToken cancellationToken)
    {
        if (!await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            var cancelledState = CaptureState();
            return HistoryOperationResult.CreateNotCommitted(
                cancelledState.DocumentId,
                command.TypeId,
                HistoryOperationStatus.Cancelled,
                cancelledState.DocumentRevision,
                cancelledState.HistoryStatus,
                [CancelledDiagnostic(cancelledState.DocumentId.Value)]);
        }

        try
        {
            if (IsUnavailable(out var state))
            {
                return UnavailableHistoryResult(command, state);
            }

            if (!state.EditorState.Selection.IsEmpty ||
                state.EditorState.SemanticSceneSelection != expectedSelectedSemanticElementId)
            {
                return HistoryOperationResult.CreateNotCommitted(
                    state.DocumentId,
                    command.TypeId,
                    HistoryOperationStatus.CommandFailed,
                    state.DocumentRevision,
                    state.HistoryStatus,
                    [Error(
                        EditingSessionDiagnosticCodes.SelectionChanged,
                        "The Command was not executed because the expected semantic Scene target is no longer selected.",
                        expectedSelectedSemanticElementId.Value)]);
            }

            var result = await History.ExecuteAsync(
                _commandProcessor,
                command,
                cancellationToken).ConfigureAwait(false);
            RecordExpectedEvent(result);
            return result;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async ValueTask<bool> TryEnterCommandAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void RecordExpectedEvent(HistoryOperationResult result)
    {
        if (result.CommittedRevision is not { } revision)
        {
            return;
        }

        lock (_sync)
        {
            if (revision > _expectedEventRevision)
            {
                _expectedEventRevision = revision;
            }

            PulseStateUnderLock();
        }
    }
}
