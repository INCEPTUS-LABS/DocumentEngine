using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

/// <summary>
/// Applies the canonical mixed-role, edge-local connector-anchor insertion ordering.
/// Policy and document-wide identity validation remain the caller's responsibility.
/// </summary>
public static class ConnectorAnchorInsertion
{
    public static VisualStateSnapshot Insert(
        VisualStateSnapshot visualState,
        ConnectorAnchorId anchorId,
        ConnectorAnchorSide side,
        ConnectorAnchorRole role,
        int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(visualState);
        ArgumentNullException.ThrowIfNull(anchorId);
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(insertionIndex);
        if (visualState.ConnectorAnchors.Any(anchor => anchor.Id == anchorId))
        {
            throw new ArgumentException(
                $"Connector-anchor ID '{anchorId}' already exists on visual state '{visualState.Id}'.",
                nameof(anchorId));
        }

        var sideCount = visualState.ConnectorAnchors.Count(anchor => anchor.Side == side);
        if (insertionIndex > sideCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(insertionIndex),
                insertionIndex,
                $"The insertion index must be between zero and {sideCount} inclusive.");
        }

        var sideAnchors = visualState.ConnectorAnchors.Where(anchor => anchor.Side == side).ToArray();
        double? position = null;
        var originals = visualState.ConnectorAnchors.AsEnumerable();
        if (sideAnchors.Any(anchor => anchor.NormalizedPosition.HasValue))
        {
            var positions = sideAnchors.Select(anchor => anchor.NormalizedPosition ??
                ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(anchor.Order, sideCount)).Order().ToArray();
            var start = insertionIndex == 0 ? 0d : positions[insertionIndex - 1];
            var end = insertionIndex == sideCount ? 1d : positions[insertionIndex];
            position = (start + end) / 2;
            originals = originals.Select(anchor => anchor.Side == side && anchor.NormalizedPosition is null
                ? new ConnectorAnchor(anchor.Id, anchor.Side, anchor.Role, anchor.Order,
                    ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(anchor.Order, sideCount)) : anchor);
        }
        var anchors = originals.Select(anchor =>
            anchor.Side == side && anchor.Order >= insertionIndex
                ? new ConnectorAnchor(anchor.Id, anchor.Side, anchor.Role, anchor.Order + 1, anchor.NormalizedPosition)
                : anchor).Append(new ConnectorAnchor(anchorId, side, role, insertionIndex, position));
        return new VisualStateSnapshot(
            visualState.Id,
            visualState.SemanticElementId,
            visualState.Position,
            visualState.Size,
            visualState.PlacementMode,
            visualState.Route,
            visualState.Properties,
            anchors,
            visualState.SourceAnchorId,
            visualState.TargetAnchorId,
            visualState.BoundaryAttachment);
    }
}
