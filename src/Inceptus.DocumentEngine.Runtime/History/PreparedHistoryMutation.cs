using Inceptus.DocumentEngine.Contracts.Commands;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class PreparedHistoryMutation
{
    internal PreparedHistoryMutation(
        HistoryState baseState,
        HistoryState proposedState,
        HistoryMutationKind kind,
        ICommand? restorationCommand = null,
        ScopeNavigationHistoryEntry? scopeNavigation = null)
    {
        ArgumentNullException.ThrowIfNull(baseState);
        ArgumentNullException.ThrowIfNull(proposedState);
        BaseState = baseState;
        ProposedState = proposedState;
        Kind = kind;
        RestorationCommand = restorationCommand;
        ScopeNavigation = scopeNavigation;
    }

    internal HistoryState BaseState { get; }

    internal HistoryState ProposedState { get; }

    internal HistoryMutationKind Kind { get; }

    internal ICommand? RestorationCommand { get; }

    internal ScopeNavigationHistoryEntry? ScopeNavigation { get; }
}

internal enum HistoryMutationKind
{
    Record = 0,
    Undo = 1,
    Redo = 2,
    DiscardRedo = 3,
}
