using Inceptus.DocumentEngine.Contracts.Commands;
using System.Collections.Immutable;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class PreparedHistoryMutation
{
    internal PreparedHistoryMutation(
        HistoryState baseState,
        HistoryState proposedState,
        HistoryMutationKind kind,
        ICommand? restorationCommand = null,
        ScopeNavigationHistoryEntry? scopeNavigation = null,
        ImmutableArray<ConnectorRoutingIntent> routingIntents = default,
        ImmutableArray<SpatialRegionHeightIntent> spatialHeightIntents = default,
        ImmutableArray<NodeGeometryHistorySeed> nodeGeometrySeeds = default,
        ImmutableArray<SpatialScopeWidthIntent> spatialWidthIntents = default)
    {
        ArgumentNullException.ThrowIfNull(baseState);
        ArgumentNullException.ThrowIfNull(proposedState);
        BaseState = baseState;
        ProposedState = proposedState;
        Kind = kind;
        RestorationCommand = restorationCommand;
        ScopeNavigation = scopeNavigation;
        RoutingIntents = routingIntents.IsDefault ? [] : routingIntents;
        SpatialHeightIntents = spatialHeightIntents.IsDefault ? [] : spatialHeightIntents;
        SpatialWidthIntents = spatialWidthIntents.IsDefault ? [] : spatialWidthIntents;
        NodeGeometrySeeds = nodeGeometrySeeds.IsDefault ? [] : nodeGeometrySeeds;
    }

    internal HistoryState BaseState { get; }

    internal HistoryState ProposedState { get; }

    internal HistoryMutationKind Kind { get; }

    internal ICommand? RestorationCommand { get; }

    internal ScopeNavigationHistoryEntry? ScopeNavigation { get; }

    internal ImmutableArray<ConnectorRoutingIntent> RoutingIntents { get; }

    internal ImmutableArray<SpatialRegionHeightIntent> SpatialHeightIntents { get; }
    internal ImmutableArray<SpatialScopeWidthIntent> SpatialWidthIntents { get; }

    internal ImmutableArray<NodeGeometryHistorySeed> NodeGeometrySeeds { get; }
}

internal enum HistoryMutationKind
{
    Record = 0,
    Undo = 1,
    Redo = 2,
    DiscardRedo = 3,
}
