using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;

namespace Inceptus.DocumentEngine.Runtime.History;

/// <summary>
/// Immutable session-only restoration data for one committed logical modification.
/// </summary>
internal sealed class HistoryEntry
{
    internal HistoryEntry(
        CommandTypeId sourceCommandTypeId,
        IHistoryCommandFactory undoFactory,
        IHistoryCommandFactory redoFactory,
        ImmutableArray<ConnectorRoutingTypeHistoryDelta> routingTypeDeltas = default,
        ImmutableArray<SpatialRegionHeightHistoryDelta> spatialHeightDeltas = default,
        ImmutableArray<NodeGeometryHistoryDelta> nodeGeometryDeltas = default,
        ImmutableArray<SpatialScopeWidthHistoryDelta> spatialWidthDeltas = default,
        ImmutableArray<ManualRouteHistoryDelta> manualRouteDeltas = default)
    {
        ArgumentNullException.ThrowIfNull(sourceCommandTypeId);
        ArgumentNullException.ThrowIfNull(undoFactory);
        ArgumentNullException.ThrowIfNull(redoFactory);
        SourceCommandTypeId = sourceCommandTypeId;
        UndoFactory = undoFactory;
        RedoFactory = redoFactory;
        RoutingTypeDeltas = routingTypeDeltas.IsDefault ? [] : routingTypeDeltas;
        SpatialHeightDeltas = spatialHeightDeltas.IsDefault ? [] : spatialHeightDeltas;
        SpatialWidthDeltas = spatialWidthDeltas.IsDefault ? [] : spatialWidthDeltas;
        NodeGeometryDeltas = nodeGeometryDeltas.IsDefault ? [] : nodeGeometryDeltas;
        ManualRouteDeltas = manualRouteDeltas.IsDefault ? [] : manualRouteDeltas;
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

    internal ConnectorRoutingHistoryOperation? RoutingOperation =>
        SourceCommandTypeId == SetConnectorRoutingTypeCommand.KnownTypeId
            ? ConnectorRoutingHistoryOperation.ChangeRoutingMode
            : (UndoFactory as UpdateConnectionRouteHistoryCommandFactory)?.Operation;

    internal IHistoryCommandFactory? UndoFactory { get; }

    internal IHistoryCommandFactory? RedoFactory { get; }

    internal ScopeNavigationHistoryEntry? ScopeNavigation { get; }

    internal ImmutableArray<ConnectorRoutingTypeHistoryDelta> RoutingTypeDeltas { get; } = [];

    internal ImmutableArray<SpatialRegionHeightHistoryDelta> SpatialHeightDeltas { get; } = [];
    internal ImmutableArray<SpatialScopeWidthHistoryDelta> SpatialWidthDeltas { get; } = [];

    internal ImmutableArray<NodeGeometryHistoryDelta> NodeGeometryDeltas { get; } = [];

    internal ImmutableArray<ManualRouteHistoryDelta> ManualRouteDeltas { get; } = [];
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
