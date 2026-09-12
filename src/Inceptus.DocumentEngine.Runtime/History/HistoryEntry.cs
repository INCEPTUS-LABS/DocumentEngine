using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Runtime.History;

/// <summary>
/// Immutable session-only restoration data for one committed logical modification.
/// </summary>
internal sealed class HistoryEntry
{
    internal HistoryEntry(
        CommandTypeId sourceCommandTypeId,
        IHistoryCommandFactory undoFactory,
        IHistoryCommandFactory redoFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceCommandTypeId);
        ArgumentNullException.ThrowIfNull(undoFactory);
        ArgumentNullException.ThrowIfNull(redoFactory);
        SourceCommandTypeId = sourceCommandTypeId;
        UndoFactory = undoFactory;
        RedoFactory = redoFactory;
        Kind = HistoryEntryKind.DocumentMutation;
    }

    internal HistoryEntry(ScopeNavigationHistoryEntry scopeNavigation)
    {
        ArgumentNullException.ThrowIfNull(scopeNavigation);
        ScopeNavigation = scopeNavigation;
        Kind = HistoryEntryKind.ScopeNavigation;
    }

    internal HistoryEntryKind Kind { get; }

    internal CommandTypeId? SourceCommandTypeId { get; }

    internal IHistoryCommandFactory? UndoFactory { get; }

    internal IHistoryCommandFactory? RedoFactory { get; }

    internal ScopeNavigationHistoryEntry? ScopeNavigation { get; }
}

internal enum HistoryEntryKind
{
    DocumentMutation = 0,
    ScopeNavigation = 1,
}

/// <summary>
/// Immutable, session-only identity for one runtime process-scope navigation action.
/// </summary>
internal sealed record ScopeNavigationHistoryEntry
{
    internal ScopeNavigationHistoryEntry(
        DocumentScopeId fromScopeId,
        DocumentScopeId toScopeId)
    {
        ArgumentNullException.ThrowIfNull(fromScopeId);
        ArgumentNullException.ThrowIfNull(toScopeId);
        if (fromScopeId == toScopeId)
        {
            throw new ArgumentException(
                "Scope-navigation History requires distinct source and target scopes.",
                nameof(toScopeId));
        }

        FromScopeId = fromScopeId;
        ToScopeId = toScopeId;
    }

    internal DocumentScopeId FromScopeId { get; }

    internal DocumentScopeId ToScopeId { get; }
}
