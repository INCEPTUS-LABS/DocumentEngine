using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

/// <summary>
/// Describes one transient, revision-bound connector-route action resolved from a Scene hit.
/// </summary>
public sealed class Canvas2DConnectorRouteContextAction
{
    internal Canvas2DConnectorRouteContextAction(
        Canvas2DConnectorRouteContextActionKind kind,
        VisualStateId targetVisualStateId,
        SceneObjectId sourceSceneObjectId,
        int routeIndex,
        PointD documentPoint,
        PointD routePoint)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The action kind must be defined.");
        }

        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        ArgumentNullException.ThrowIfNull(sourceSceneObjectId);
        ArgumentOutOfRangeException.ThrowIfNegative(routeIndex);

        Kind = kind;
        TargetVisualStateId = targetVisualStateId;
        SourceSceneObjectId = sourceSceneObjectId;
        RouteIndex = routeIndex;
        DocumentPoint = documentPoint;
        RoutePoint = routePoint;
    }

    public Canvas2DConnectorRouteContextActionKind Kind { get; }

    public VisualStateId TargetVisualStateId { get; }

    /// <summary>
    /// Gets transient Scene traceability only; persistent addressing uses
    /// <see cref="TargetVisualStateId"/> against the expected revision.
    /// </summary>
    public SceneObjectId SourceSceneObjectId { get; }

    /// <summary>
    /// Gets the hit segment index for Add Point or the source internal-point index for Delete Point.
    /// </summary>
    public int RouteIndex { get; }

    /// <summary>
    /// Gets the original document-space context-menu point used to reproject Add Point.
    /// </summary>
    public PointD DocumentPoint { get; }

    /// <summary>
    /// Gets the projected document-space insertion point or targeted bend point.
    /// </summary>
    public PointD RoutePoint { get; }

    /// <summary>
    /// Revalidates this transient action against the current authoritative Scene and persistent
    /// route, then resolves the complete route for one atomic Visual command.
    /// </summary>
    public bool TryResolveTargetRoute(
        Canvas2DScene scene,
        IEnumerable<PointD> persistentRoute,
        out ImmutableArray<PointD> targetRoute)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(persistentRoute);
        targetRoute = [];
        var persisted = persistentRoute.ToImmutableArray();
        if (persisted.Length == 1)
        {
            return false;
        }

        var connector = scene.Items.SingleOrDefault(item =>
            item.Id == SourceSceneObjectId &&
            item.Layer == Canvas2DSceneLayer.Connector &&
            item.Origin.VisualStateId == TargetVisualStateId &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Path);
        if (connector is null || Canvas2DConnectorPathMetadata.Resolve(connector).Length < 2)
        {
            return false;
        }

        if (connector.ConnectorPresentationMapping is { } mapping)
        {
            return TryResolveSpatialRoute(connector, mapping, persisted, out targetRoute);
        }

        return Kind switch
        {
            Canvas2DConnectorRouteContextActionKind.AddPoint => TryResolveAdd(
                connector,
                persisted,
                out targetRoute),
            Canvas2DConnectorRouteContextActionKind.DeletePoint => TryResolveDelete(
                connector,
                persisted,
                out targetRoute),
            _ => false,
        };
    }

    private bool TryResolveSpatialRoute(
        Canvas2DSceneItem connector,
        Canvas2DConnectorPresentationMapping mapping,
        ImmutableArray<PointD> persisted,
        out ImmutableArray<PointD> targetRoute)
    {
        targetRoute = [];
        var canonical = mapping.CanonicalEditablePath;
        if (!persisted.IsEmpty && (persisted.Length != canonical.Length ||
            !persisted.AsSpan(1, persisted.Length - 2).SequenceEqual(canonical.AsSpan(1, canonical.Length - 2))))
        {
            return false;
        }

        if (Kind == Canvas2DConnectorRouteContextActionKind.DeletePoint)
        {
            if (persisted.Length < 3 || RouteIndex <= 0 || RouteIndex >= persisted.Length - 1 ||
                persisted[RouteIndex] != RoutePoint)
            {
                return false;
            }
            targetRoute = persisted.Length == 3 ? [] : persisted.RemoveAt(RouteIndex);
            return true;
        }

        var displayed = Canvas2DConnectorPathMetadata.Resolve(connector)
            .Select(connector.Transform.TransformPoint).ToImmutableArray();
        if (Kind != Canvas2DConnectorRouteContextActionKind.AddPoint || RouteIndex >= displayed.Length - 1)
        {
            return false;
        }
        var projected = ProjectOntoSegment(displayed[RouteIndex], displayed[RouteIndex + 1],
            mapping.MapCanonicalGuidanceToScene(DocumentPoint));
        var canonicalPoint = mapping.MapSceneGuidanceToCanonical(projected);
        if (!DocumentGeometryBoundary.Contains(canonicalPoint) ||
            DistanceSquared(canonicalPoint, RoutePoint) > 1e-20 ||
            mapping.DisplayedEditablePath.Any(point => IsWithinPointTolerance(point, projected)))
        {
            return false;
        }

        if (persisted.IsEmpty)
        {
            targetRoute = [canonical[0], canonicalPoint, canonical[^1]];
            return true;
        }

        // A displayed segment must have proven authored-waypoint provenance. Do not guess an
        // insertion index in a recovery path that omitted canonical guidance.
        if (!TryMapOrderedSubsequence(displayed, mapping.DisplayedEditablePath, out var indexes))
        {
            return false;
        }
        var insertion = Array.FindIndex(indexes, index => index > RouteIndex);
        if (insertion <= 0)
        {
            return false;
        }
        targetRoute = persisted.Insert(insertion, canonicalPoint);
        return true;
    }

    private bool TryResolveAdd(
        Canvas2DSceneItem connector,
        ImmutableArray<PointD> persistentRoute,
        out ImmutableArray<PointD> targetRoute)
    {
        targetRoute = [];
        var logicalPath = Canvas2DConnectorPathMetadata.Resolve(connector);
        if (RouteIndex >= logicalPath.Length - 1)
        {
            return false;
        }

        var documentPath = ResolveProcessLocalPath(connector, logicalPath);
        var projection = ProjectOntoSegment(
            documentPath[RouteIndex],
            documentPath[RouteIndex + 1],
            DocumentPoint);
        if (!DocumentGeometryBoundary.Contains(DocumentPoint) ||
            DistanceSquared(projection, RoutePoint) > 1e-20 ||
            IsWithinPointTolerance(documentPath[0], projection) ||
            IsWithinPointTolerance(documentPath[^1], projection))
        {
            return false;
        }

        if (persistentRoute.IsEmpty)
        {
            targetRoute = [documentPath[0], projection, documentPath[^1]];
            return true;
        }

        var editablePath = Canvas2DConnectorPathMetadata.ResolveEditable(connector);
        editablePath = ResolveProcessLocalPath(connector, editablePath);
        if (persistentRoute.Length != editablePath.Length ||
            persistentRoute.Length > 2 &&
            !persistentRoute.AsSpan(1, persistentRoute.Length - 2)
                .SequenceEqual(editablePath.AsSpan(1, editablePath.Length - 2)) ||
            editablePath.Any(point => IsWithinPointTolerance(point, projection)))
        {
            return false;
        }

        int insertionIndex;
        if (TryMapOrderedSubsequence(documentPath, editablePath, out var editableIndexes) &&
            editableIndexes[0] == 0 &&
            editableIndexes[^1] == documentPath.Length - 1)
        {
            insertionIndex = Array.FindIndex(
                editableIndexes,
                logicalIndex => logicalIndex > RouteIndex);
        }
        else
        {
            // A recovery route may intentionally omit infeasible persistent guidance. Preserve
            // repairability by inserting relative to the nearest authored segment instead of
            // requiring the editable path to occur in the effective path.
            var editableProjection = Canvas2DConnectorPathGeometry.FindNearest(
                editablePath,
                projection);
            insertionIndex = editableProjection.SegmentIndex + 1;
        }

        if (insertionIndex <= 0)
        {
            return false;
        }

        targetRoute = persistentRoute
            .Take(insertionIndex)
            .Append(projection)
            .Concat(persistentRoute.Skip(insertionIndex))
            .ToImmutableArray();
        return targetRoute.Length >= 3;
    }

    private bool TryResolveDelete(
        Canvas2DSceneItem connector,
        ImmutableArray<PointD> persistentRoute,
        out ImmutableArray<PointD> targetRoute)
    {
        targetRoute = [];
        var editableRoute = ResolveProcessLocalPath(
            connector,
            Canvas2DConnectorPathMetadata.ResolveEditable(connector));
        if (persistentRoute.Length < 3 ||
            RouteIndex <= 0 ||
            RouteIndex >= persistentRoute.Length - 1 ||
            editableRoute.Length != persistentRoute.Length ||
            editableRoute[RouteIndex] != persistentRoute[RouteIndex] ||
            DistanceSquared(editableRoute[RouteIndex], RoutePoint) > 1e-20)
        {
            return false;
        }

        var remainingRoute = persistentRoute.RemoveAt(RouteIndex);
        targetRoute = remainingRoute.Length == 2 ? [] : remainingRoute;
        return true;
    }

    private static ImmutableArray<PointD> ResolveProcessLocalPath(
        Canvas2DSceneItem connector,
        IEnumerable<PointD> points) =>
        points
            .Select(connector.Transform.TransformPoint)
            .Select(point => connector.SpatialRegion?.MapSceneToLocal(point) ?? point)
            .ToImmutableArray();

    private static PointD ProjectOntoSegment(PointD start, PointD end, PointD point)
    {
        var delta = end - start;
        var lengthSquared = (delta.X * delta.X) + (delta.Y * delta.Y);
        if (lengthSquared == 0d)
        {
            return start;
        }

        var ratio = Math.Clamp(
            (((point.X - start.X) * delta.X) + ((point.Y - start.Y) * delta.Y)) /
                lengthSquared,
            0d,
            1d);
        return new PointD(
            start.X + (delta.X * ratio),
            start.Y + (delta.Y * ratio));
    }

    private static bool TryMapOrderedSubsequence(
        ImmutableArray<PointD> logicalPath,
        ImmutableArray<PointD> editablePath,
        out int[] logicalIndexes)
    {
        logicalIndexes = new int[editablePath.Length];
        if (editablePath.IsEmpty || logicalPath.IsEmpty ||
            editablePath.Length > logicalPath.Length ||
            editablePath[0] != logicalPath[0] ||
            editablePath[^1] != logicalPath[^1])
        {
            return false;
        }

        logicalIndexes[0] = 0;
        logicalIndexes[^1] = logicalPath.Length - 1;
        var nextLogicalIndex = 1;
        for (var editableIndex = 1; editableIndex < editablePath.Length - 1; editableIndex++)
        {
            while (nextLogicalIndex < logicalPath.Length - 1 &&
                logicalPath[nextLogicalIndex] != editablePath[editableIndex])
            {
                nextLogicalIndex++;
            }

            if (nextLogicalIndex >= logicalPath.Length - 1)
            {
                logicalIndexes = [];
                return false;
            }

            logicalIndexes[editableIndex] = nextLogicalIndex;
            nextLogicalIndex++;
        }

        return true;
    }

    private static bool IsWithinPointTolerance(PointD left, PointD right)
    {
        var tolerance = Canvas2DConnectorInteractionConfiguration.Default
            .RoutePointProximityTolerance;
        return DistanceSquared(left, right) <= tolerance * tolerance;
    }

    private static double DistanceSquared(PointD left, PointD right)
    {
        var delta = left - right;
        return (delta.X * delta.X) + (delta.Y * delta.Y);
    }
}

public enum Canvas2DConnectorRouteContextActionKind
{
    AddPoint,
    DeletePoint,
}
