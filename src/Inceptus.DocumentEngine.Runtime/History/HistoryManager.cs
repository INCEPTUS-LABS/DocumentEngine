using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.Runtime.History;

/// <summary>
/// Public session-scoped owner of one Document's linear History. It exposes status
/// and operations, never entries, cursor mutation, snapshots, or storage internals.
/// </summary>
public sealed class HistoryManager
{
    private readonly Document _document;
    private readonly HistoryStore _store = new();

    public HistoryManager(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
    }

    public HistoryStatus CaptureStatus() => _store.CaptureStatus();

    public async ValueTask<HistoryOperationResult> ExecuteAsync(
        CommandProcessor commandProcessor,
        ICommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandProcessor);
        ArgumentNullException.ThrowIfNull(command);
        return await commandProcessor.ExecuteWithHistoryAsync(
            _document,
            command,
            _store,
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<HistoryOperationResult> UndoAsync(
        CommandProcessor commandProcessor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandProcessor);
        return commandProcessor.ExecuteHistoryOperationAsync(
            _document,
            _store,
            isUndo: true,
            cancellationToken);
    }

    public ValueTask<HistoryOperationResult> RedoAsync(
        CommandProcessor commandProcessor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandProcessor);
        return commandProcessor.ExecuteHistoryOperationAsync(
            _document,
            _store,
            isUndo: false,
            cancellationToken);
    }

    internal HistoryMutationPreparationResult PrepareScopeNavigation(
        DocumentScopeId fromScopeId,
        DocumentScopeId toScopeId)
    {
        ArgumentNullException.ThrowIfNull(fromScopeId);
        ArgumentNullException.ThrowIfNull(toScopeId);
        return _store.PrepareRecord(new HistoryEntry(
            new ScopeNavigationHistoryEntry(fromScopeId, toScopeId)));
    }

    internal void InstallPrepared(PreparedHistoryMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        _store.InstallPrepared(mutation);
    }

    internal ValueTask<HistoryOperationResult> UndoAsync(
        CommandProcessor commandProcessor,
        Func<DocumentScopeId, CancellationToken, ValueTask<bool>> replayScopeNavigation,
        CancellationToken cancellationToken = default) =>
        ExecuteMixedHistoryOperationAsync(
            commandProcessor,
            replayScopeNavigation,
            isUndo: true,
            cancellationToken);

    internal ValueTask<HistoryOperationResult> RedoAsync(
        CommandProcessor commandProcessor,
        Func<DocumentScopeId, CancellationToken, ValueTask<bool>> replayScopeNavigation,
        CancellationToken cancellationToken = default) =>
        ExecuteMixedHistoryOperationAsync(
            commandProcessor,
            replayScopeNavigation,
            isUndo: false,
            cancellationToken);

    private async ValueTask<HistoryOperationResult> ExecuteMixedHistoryOperationAsync(
        CommandProcessor commandProcessor,
        Func<DocumentScopeId, CancellationToken, ValueTask<bool>> replayScopeNavigation,
        bool isUndo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commandProcessor);
        ArgumentNullException.ThrowIfNull(replayScopeNavigation);
        if (!_store.TryPeekScopeNavigation(isUndo, out var scopeNavigation) ||
            scopeNavigation is null)
        {
            return isUndo
                ? await UndoAsync(commandProcessor, cancellationToken).ConfigureAwait(false)
                : await RedoAsync(commandProcessor, cancellationToken).ConfigureAwait(false);
        }

        var snapshot = _document.CaptureSnapshot();
        var preparation = isUndo
            ? _store.PrepareUndo(snapshot.DocumentId, snapshot.Revision)
            : _store.PrepareRedo(snapshot.DocumentId, snapshot.Revision);
        if (!preparation.Succeeded ||
            preparation.Mutation?.ScopeNavigation is not { } preparedNavigation ||
            preparedNavigation != scopeNavigation)
        {
            return HistoryOperationResult.CreateNotCommitted(
                snapshot.DocumentId,
                commandTypeId: null,
                HistoryOperationStatus.InternalFailure,
                snapshot.Revision,
                _store.CaptureStatus(),
                preparation.Diagnostics.IsEmpty
                    ?
                    [
                        RuntimeReplayDiagnostic(
                            isUndo,
                            scopeNavigation,
                            "The scope-navigation History transition could not be prepared."),
                    ]
                    : preparation.Diagnostics);
        }

        var targetScopeId = isUndo
            ? scopeNavigation.FromScopeId
            : scopeNavigation.ToScopeId;
        bool replayed;
        try
        {
            replayed = await replayScopeNavigation(targetScopeId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return HistoryOperationResult.CreateNotCommitted(
                snapshot.DocumentId,
                commandTypeId: null,
                HistoryOperationStatus.Cancelled,
                _document.Revision,
                _store.CaptureStatus(),
                [RuntimeReplayDiagnostic(
                    isUndo,
                    scopeNavigation,
                    "The scope-navigation History replay was cancelled.")]);
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return HistoryOperationResult.CreateNotCommitted(
                snapshot.DocumentId,
                commandTypeId: null,
                HistoryOperationStatus.InternalFailure,
                _document.Revision,
                _store.CaptureStatus(),
                [new Diagnostic(
                    HistoryDiagnosticCodes.RuntimeReplayFailed,
                    DiagnosticSeverity.Error,
                    "The scope-navigation History replay failed unexpectedly.",
                    targetScopeId.Value,
                    [new(
                        "ExceptionType",
                        exception.GetType().FullName ?? exception.GetType().Name)])]);
        }

        if (!replayed)
        {
            return HistoryOperationResult.CreateNotCommitted(
                snapshot.DocumentId,
                commandTypeId: null,
                HistoryOperationStatus.InternalFailure,
                _document.Revision,
                _store.CaptureStatus(),
                [RuntimeReplayDiagnostic(
                    isUndo,
                    scopeNavigation,
                    $"The scope-navigation History target '{targetScopeId}' is unavailable.")]);
        }

        // EditingSession owns this manager and serializes normal Commands, navigation,
        // and replay through one command gate. After successful replay this installation
        // is the only remaining, non-fallible state transition.
        _store.InstallPrepared(preparation.Mutation);
        return HistoryOperationResult.CreateApplied(
            snapshot.DocumentId,
            _document.Revision,
            _store.CaptureStatus());
    }

    private static Diagnostic RuntimeReplayDiagnostic(
        bool isUndo,
        ScopeNavigationHistoryEntry navigation,
        string message) =>
        new(
            HistoryDiagnosticCodes.RuntimeReplayFailed,
            DiagnosticSeverity.Error,
            message,
            isUndo ? navigation.FromScopeId.Value : navigation.ToScopeId.Value);

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;

}
