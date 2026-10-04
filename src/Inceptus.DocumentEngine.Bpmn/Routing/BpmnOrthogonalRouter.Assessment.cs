using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Bpmn.Routing;

internal static partial class BpmnOrthogonalRouter
{
    /// <summary>Checks the saved attempt directly. It neither searches nor rewrites path points.</summary>
    internal static bool IsValidSavedPath(ImmutableArray<PointD> path, BpmnRoutingEndpoint source,
        BpmnRoutingEndpoint target, OperationContext operation, double clearance, double leadDistance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!BpmnRoutingPolicy.ObstacleClearanceAttempts.Contains(clearance) ||
            !BpmnRoutingPolicy.EndpointLeadDistanceAttempts.Contains(leadDistance) ||
            path.Length < 2 || path[0] != source.Point || path[^1] != target.Point || source.Point == target.Point ||
            path.Any(static point => !DocumentGeometryBoundary.Contains(point))) return false;
        for (var i = 1; i < path.Length; i++)
            if (path[i - 1].X != path[i].X && path[i - 1].Y != path[i].Y) return false;
        if (!TryCreateLead(source.Point, source.Side, leadDistance, out var sourceLead) ||
            !TryCreateLead(target.Point, target.Side, leadDistance, out var targetLead) ||
            !TryFindLead(path, sourceLead, fromStart: true, out var sourceEnd) ||
            !TryFindLead(path, targetLead, fromStart: false, out var targetStart)) return false;

        var obstacles = operation.GetAttempt(source.OwnerNodeId, target.OwnerNodeId, clearance, leadDistance,
            cancellationToken).Obstacles;
        if (!IsLeadSegmentLegal(source.Point, sourceLead, source.OwnerNodeId, obstacles) ||
            !IsLeadSegmentLegal(targetLead, target.Point, target.OwnerNodeId, obstacles) ||
            !IsHardBodySafe(path, source.OwnerNodeId, target.OwnerNodeId, operation.ActualObstacles)) return false;

        var previous = sourceLead;
        for (var i = sourceEnd; i <= targetStart; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (obstacles.Any(obstacle => SegmentCrossesStrictInterior(previous, path[i], obstacle.Bounds))) return false;
            previous = path[i];
        }
        return obstacles.All(obstacle => !SegmentCrossesStrictInterior(previous, targetLead, obstacle.Bounds));
    }

    private static bool TryFindLead(ImmutableArray<PointD> path, PointD lead, bool fromStart, out int boundaryIndex)
    {
        var origin = fromStart ? path[0] : path[^1];
        var direction = lead - origin;
        var index = fromStart ? 1 : path.Length - 2;
        var step = fromStart ? 1 : -1;
        var previousDistance = 0d;
        while (index >= 0 && index < path.Length)
        {
            var offset = path[index] - origin;
            if (direction.X == 0d ? offset.X != 0d || offset.Y * direction.Y < 0d : offset.Y != 0d || offset.X * direction.X < 0d)
                break;
            var distance = Math.Abs(offset.X) + Math.Abs(offset.Y);
            if (distance < previousDistance) break;
            if (distance >= Math.Abs(direction.X) + Math.Abs(direction.Y))
            {
                boundaryIndex = index;
                return true;
            }
            previousDistance = distance;
            index += step;
        }
        boundaryIndex = -1;
        return false;
    }
}
