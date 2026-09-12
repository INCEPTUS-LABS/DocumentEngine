using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Runtime.History;

/// <summary>
/// Session-owned linear History. It stores no Document snapshots and exposes no
/// Document mutation path.
/// </summary>
internal sealed class HistoryStore
{
    private HistoryState _state = new([], 0, 0);

    internal int Count => CaptureState().Entries.Length;

    internal int Cursor => CaptureState().Cursor;

    internal bool CanUndo => CaptureState().Cursor > 0;

    internal bool CanRedo
    {
        get
        {
            var state = CaptureState();
            return state.Cursor < state.Entries.Length;
        }
    }

    internal HistoryStatus CaptureStatus()
    {
        var state = CaptureState();
        return new HistoryStatus(
            state.Entries.Length,
            state.Cursor > 0,
            state.Cursor < state.Entries.Length);
    }

    internal HistoryState CaptureState() => Volatile.Read(ref _state);

    internal HistoryMutationPreparationResult PrepareRecord(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var baseline = CaptureState();

        try
        {
            var retained = baseline.Entries.Take(baseline.Cursor);
            var entries = retained.Append(entry).ToImmutableArray();
            var proposed = new HistoryState(
                entries,
                entries.Length,
                checked(baseline.Version + 1));
            return HistoryMutationPreparationResult.Success(
                new PreparedHistoryMutation(
                    baseline,
                    proposed,
                    HistoryMutationKind.Record));
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return HistoryMutationPreparationResult.Failure(
            [
                Error(
                    HistoryDiagnosticCodes.InvalidPreparation,
                    "The History record could not be prepared.",
                    exception),
            ]);
        }
    }

    internal bool TryPeekScopeNavigation(
        bool isUndo,
        out ScopeNavigationHistoryEntry? scopeNavigation)
    {
        var state = CaptureState();
        var entryIndex = isUndo ? state.Cursor - 1 : state.Cursor;
        if (entryIndex < 0 || entryIndex >= state.Entries.Length)
        {
            scopeNavigation = null;
            return false;
        }

        scopeNavigation = state.Entries[entryIndex].ScopeNavigation;
        return scopeNavigation is not null;
    }

    internal HistoryMutationPreparationResult PrepareNotUndoable()
    {
        var baseline = CaptureState();
        if (baseline.Cursor == baseline.Entries.Length)
        {
            return HistoryMutationPreparationResult.Success();
        }

        try
        {
            var entries = baseline.Entries.Take(baseline.Cursor).ToImmutableArray();
            var proposed = new HistoryState(
                entries,
                baseline.Cursor,
                checked(baseline.Version + 1));
            return HistoryMutationPreparationResult.Success(
                new PreparedHistoryMutation(
                    baseline,
                    proposed,
                    HistoryMutationKind.DiscardRedo));
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return HistoryMutationPreparationResult.Failure(
            [
                Error(
                    HistoryDiagnosticCodes.InvalidPreparation,
                    "The non-undoable History transition could not be prepared.",
                    exception),
            ]);
        }
    }

    internal HistoryMutationPreparationResult PrepareUndo(
        DocumentId documentId,
        DocumentRevision currentRevision) =>
        PrepareNavigation(documentId, currentRevision, isUndo: true);

    internal HistoryMutationPreparationResult PrepareRedo(
        DocumentId documentId,
        DocumentRevision currentRevision) =>
        PrepareNavigation(documentId, currentRevision, isUndo: false);

    /// <summary>
    /// Allocation-free installation seam for use after a successful Document commit.
    /// The future caller must hold the same Document execution gate used during preparation.
    /// </summary>
    internal bool TryInstallPrepared(PreparedHistoryMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        return ReferenceEquals(
            Interlocked.CompareExchange(
                ref _state,
                mutation.ProposedState,
                mutation.BaseState),
            mutation.BaseState);
    }

    /// <summary>
    /// Installs a mutation whose base state was verified while the owning Document's
    /// execution gate was held. This reference replacement performs no allocation,
    /// validation, callbacks, or other fallible work after the Document commit.
    /// </summary>
    internal void InstallPrepared(PreparedHistoryMutation mutation) =>
        Volatile.Write(ref _state, mutation.ProposedState);

    internal bool IsCurrent(PreparedHistoryMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        return ReferenceEquals(CaptureState(), mutation.BaseState);
    }

    private HistoryMutationPreparationResult PrepareNavigation(
        DocumentId documentId,
        DocumentRevision currentRevision,
        bool isUndo)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        var baseline = CaptureState();
        var entryIndex = isUndo ? baseline.Cursor - 1 : baseline.Cursor;
        if (entryIndex < 0 || entryIndex >= baseline.Entries.Length)
        {
            return HistoryMutationPreparationResult.Failure(
            [
                new Diagnostic(
                    isUndo
                        ? HistoryDiagnosticCodes.UndoUnavailable
                        : HistoryDiagnosticCodes.RedoUnavailable,
                    DiagnosticSeverity.Error,
                    isUndo
                        ? "No committed History entry is available to undo."
                        : "No undone History entry is available to redo.",
                    documentId.Value),
            ]);
        }

        try
        {
            var entry = baseline.Entries[entryIndex];
            var nextCursor = isUndo ? baseline.Cursor - 1 : baseline.Cursor + 1;
            var proposed = new HistoryState(
                baseline.Entries,
                nextCursor,
                checked(baseline.Version + 1));
            if (entry.Kind == HistoryEntryKind.ScopeNavigation &&
                entry.ScopeNavigation is { } scopeNavigation)
            {
                return HistoryMutationPreparationResult.Success(
                    new PreparedHistoryMutation(
                        baseline,
                        proposed,
                        isUndo ? HistoryMutationKind.Undo : HistoryMutationKind.Redo,
                        scopeNavigation: scopeNavigation));
            }

            var factory = isUndo ? entry.UndoFactory : entry.RedoFactory;
            if (entry.Kind != HistoryEntryKind.DocumentMutation ||
                entry.SourceCommandTypeId is null ||
                factory is null)
            {
                return HistoryMutationPreparationResult.Failure(
                [
                    new Diagnostic(
                        HistoryDiagnosticCodes.InvalidPreparation,
                        DiagnosticSeverity.Error,
                        "The History entry does not contain valid restoration data.",
                        documentId.Value),
                ]);
            }

            var command = factory.Create(documentId, currentRevision);
            if (command is null ||
                command.TargetDocumentId != documentId ||
                command.ExpectedRevision != currentRevision)
            {
                return HistoryMutationPreparationResult.Failure(
                [
                    new Diagnostic(
                        HistoryDiagnosticCodes.InvalidRestorationCommand,
                        DiagnosticSeverity.Error,
                        "The History factory produced a Command for a different Document or revision.",
                        entry.SourceCommandTypeId.Value),
                ]);
            }

            return HistoryMutationPreparationResult.Success(
                new PreparedHistoryMutation(
                    baseline,
                    proposed,
                    isUndo ? HistoryMutationKind.Undo : HistoryMutationKind.Redo,
                    command));
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return HistoryMutationPreparationResult.Failure(
            [
                Error(
                    HistoryDiagnosticCodes.CommandFactoryFailure,
                    "The History restoration Command could not be constructed.",
                    exception),
            ]);
        }
    }

    private static Diagnostic Error(string code, string message, Exception exception) =>
        new(
            code,
            DiagnosticSeverity.Error,
            message,
            null,
            [new("ExceptionType", exception.GetType().FullName ?? exception.GetType().Name)]);

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;
}
