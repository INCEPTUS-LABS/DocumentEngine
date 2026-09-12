using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Bpmn.Routing;

internal readonly record struct BpmnRoutingEndpoint(
    ProjectedObjectId OwnerNodeId,
    PointD Point,
    ConnectorAnchorSide Side);

internal readonly record struct BpmnRoutingObstacle(
    ProjectedObjectId NodeId,
    RectD Bounds);

internal enum BpmnRoutingOutcome
{
    Routed,
    NoRoute,
    InvalidOutput,
}

/// <summary>
/// Deterministic sparse rectilinear visibility search in logical Document coordinates.
/// </summary>
internal static class BpmnOrthogonalRouter
{
    private enum SegmentDirection
    {
        None,
        Horizontal,
        Vertical,
    }

    internal static BpmnRoutingOutcome TryRoute(
        BpmnRoutingEndpoint source,
        BpmnRoutingEndpoint target,
        IReadOnlyList<BpmnRoutingObstacle> actualObstacles,
        IReadOnlyList<PointD> mandatoryWaypoints,
        bool allowPolicyRelaxation,
        CancellationToken cancellationToken,
        out ImmutableArray<PointD> path,
        out string failureReason)
    {
        ArgumentNullException.ThrowIfNull(actualObstacles);
        ArgumentNullException.ThrowIfNull(mandatoryWaypoints);
        path = [];
        failureReason = "No legal orthogonal route satisfies the endpoint constraints.";

        if (source.Point == target.Point)
        {
            failureReason =
                "Coincident endpoints cannot satisfy non-zero initial and final directions.";
            return BpmnRoutingOutcome.NoRoute;
        }

        if (actualObstacles.Any(obstacle =>
                obstacle.NodeId != source.OwnerNodeId &&
                IsStrictlyInside(source.Point, obstacle.Bounds)) ||
            actualObstacles.Any(obstacle =>
                obstacle.NodeId != target.OwnerNodeId &&
                IsStrictlyInside(target.Point, obstacle.Bounds)))
        {
            failureReason = "An endpoint attachment lies inside an unrelated node body.";
            return BpmnRoutingOutcome.NoRoute;
        }

        var clearanceAttempts = BpmnRoutingPolicy.ObstacleClearanceAttempts;
        var leadDistanceAttempts = BpmnRoutingPolicy.EndpointLeadDistanceAttempts;
        var clearanceAttemptCount = allowPolicyRelaxation ? clearanceAttempts.Length : 1;
        var leadDistanceAttemptCount = allowPolicyRelaxation ? leadDistanceAttempts.Length : 1;
        for (var clearanceIndex = 0;
             clearanceIndex < clearanceAttemptCount;
             clearanceIndex++)
        {
            var clearance = clearanceAttempts[clearanceIndex];
            for (var leadDistanceIndex = 0;
                 leadDistanceIndex < leadDistanceAttemptCount;
                 leadDistanceIndex++)
            {
                var leadDistance = leadDistanceAttempts[leadDistanceIndex];
                cancellationToken.ThrowIfCancellationRequested();
                var obstacles = CreateAttemptObstacles(
                    actualObstacles,
                    source.OwnerNodeId,
                    target.OwnerNodeId,
                    clearance,
                    leadDistance);
                var outcome = TryRouteAttempt(
                    source,
                    target,
                    obstacles,
                    mandatoryWaypoints,
                    leadDistance,
                    cancellationToken,
                    out path,
                    out failureReason);
                if (outcome != BpmnRoutingOutcome.NoRoute)
                {
                    if (outcome == BpmnRoutingOutcome.Routed &&
                        (!ExitsSide(path, source.Side) ||
                         !ApproachesSide(path, target.Side) ||
                         !IsHardBodySafe(
                             path,
                             source.OwnerNodeId,
                             target.OwnerNodeId,
                             actualObstacles)))
                    {
                        path = [];
                        failureReason =
                            "The obstacle router violated a hard endpoint-direction or node-body constraint.";
                        return BpmnRoutingOutcome.InvalidOutput;
                    }

                    return outcome;
                }
            }
        }

        path = [];
        return BpmnRoutingOutcome.NoRoute;
    }

    private static BpmnRoutingOutcome TryRouteAttempt(
        BpmnRoutingEndpoint source,
        BpmnRoutingEndpoint target,
        IReadOnlyList<BpmnRoutingObstacle> obstacles,
        IReadOnlyList<PointD> mandatoryWaypoints,
        double leadDistance,
        CancellationToken cancellationToken,
        out ImmutableArray<PointD> path,
        out string failureReason)
    {
        path = [];
        failureReason = string.Empty;

        if (!TryCreateLead(source.Point, source.Side, leadDistance, out var sourceLead) ||
            !TryCreateLead(target.Point, target.Side, leadDistance, out var targetLead))
        {
            failureReason =
                "An endpoint cannot escape in its anchor-side direction inside the positive Document boundary.";
            return BpmnRoutingOutcome.NoRoute;
        }

        if (!IsLeadSegmentLegal(source.Point, sourceLead, source.OwnerNodeId, obstacles) ||
            !IsLeadSegmentLegal(targetLead, target.Point, target.OwnerNodeId, obstacles))
        {
            failureReason = "An endpoint lead intersects an unrelated routing obstacle.";
            return BpmnRoutingOutcome.NoRoute;
        }

        foreach (var waypoint in mandatoryWaypoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!DocumentGeometryBoundary.Contains(waypoint) ||
                obstacles.Any(obstacle => IsStrictlyInside(waypoint, obstacle.Bounds)))
            {
                failureReason =
                    "Persistent route guidance contains a waypoint inside a routing obstacle or outside the positive Document boundary.";
                return BpmnRoutingOutcome.NoRoute;
            }
        }

        var controls = new List<(PointD Point, bool IsProtected)>(mandatoryWaypoints.Count + 2)
        {
            (sourceLead, false),
        };
        controls.AddRange(mandatoryWaypoints.Select(static point => (point, true)));
        controls.Add((targetLead, false));

        var sourceDirection = Direction(source.Point, sourceLead);
        var candidates = new Dictionary<SegmentDirection, RouteCandidate>
        {
            [sourceDirection] = new(
                new RouteCost(
                    Manhattan(source.Point, sourceLead),
                    0,
                    Manhattan(source.Point, sourceLead)),
                sourceDirection,
                []),
        };
        for (var index = 0; index < controls.Count - 1; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextCandidates = new Dictionary<SegmentDirection, RouteCandidate>();
            foreach (var candidate in candidates.Values.OrderBy(static candidate => candidate.Direction))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TrySearch(
                        controls[index].Point,
                        controls[index + 1].Point,
                        candidate.Direction,
                        obstacles,
                        cancellationToken,
                        out var legResults))
                {
                    continue;
                }

                foreach (var legResult in legResults)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var proposed = new RouteCandidate(
                        candidate.Cost.Combine(legResult.Cost),
                        legResult.FinalDirection,
                        candidate.Legs.Add(legResult.Path));
                    if (!nextCandidates.TryGetValue(legResult.FinalDirection, out var current) ||
                        CompareCandidates(proposed, current) < 0)
                    {
                        nextCandidates[legResult.FinalDirection] = proposed;
                    }
                }
            }

            if (nextCandidates.Count == 0)
            {
                failureReason =
                    $"No legal orthogonal route exists between route control points {index} and {index + 1}.";
                return BpmnRoutingOutcome.NoRoute;
            }

            candidates = nextCandidates;
        }

        var targetDirection = Direction(targetLead, target.Point);
        RouteCandidate? selected = null;
        RouteCost selectedCost = RouteCost.Infinite;
        foreach (var candidate in candidates.Values.OrderBy(static candidate => candidate.Direction))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var finalCost = candidate.Cost.Add(
                Manhattan(targetLead, target.Point),
                candidate.Direction != targetDirection);
            if (selected is null ||
                finalCost.CompareTo(selectedCost) < 0 ||
                finalCost == selectedCost &&
                CompareFinalCandidates(candidate, selected, targetDirection) < 0)
            {
                selected = candidate;
                selectedCost = finalCost;
            }
        }

        if (selected is null)
        {
            failureReason = "No legal orthogonal route satisfies the endpoint-side directions.";
            return BpmnRoutingOutcome.NoRoute;
        }

        var protectedPoints = new List<bool> { true };
        var combined = new List<PointD> { source.Point };
        AppendPoint(combined, protectedPoints, sourceLead, isProtected: false);
        for (var index = 0; index < selected.Legs.Length; index++)
        {
            var leg = selected.Legs[index];
            if (leg.Length == 1 && controls[index + 1].IsProtected)
            {
                AppendPoint(
                    combined,
                    protectedPoints,
                    controls[index + 1].Point,
                    isProtected: true);
                continue;
            }

            for (var legIndex = 1; legIndex < leg.Length; legIndex++)
            {
                var isLast = legIndex == leg.Length - 1;
                AppendPoint(
                    combined,
                    protectedPoints,
                    leg[legIndex],
                    isLast && controls[index + 1].IsProtected);
            }
        }

        AppendPoint(combined, protectedPoints, target.Point, isProtected: true);
        SimplifyGeneratedPoints(combined, protectedPoints);
        if (combined.Count < 2 ||
            combined.Any(static point =>
                !double.IsFinite(point.X) ||
                !double.IsFinite(point.Y) ||
                !DocumentGeometryBoundary.Contains(point)) ||
            !IsOrthogonal(combined))
        {
            failureReason = "The obstacle router produced invalid or non-orthogonal geometry.";
            return BpmnRoutingOutcome.InvalidOutput;
        }

        path = [.. combined];
        return BpmnRoutingOutcome.Routed;
    }

    private static bool TryCreateLead(
        PointD anchor,
        ConnectorAnchorSide side,
        double distance,
        out PointD lead)
    {
        lead = side switch
        {
            ConnectorAnchorSide.Top => new PointD(anchor.X, anchor.Y - distance),
            ConnectorAnchorSide.Right => new PointD(anchor.X + distance, anchor.Y),
            ConnectorAnchorSide.Bottom => new PointD(anchor.X, anchor.Y + distance),
            ConnectorAnchorSide.Left => new PointD(anchor.X - distance, anchor.Y),
            _ => default,
        };
        return Enum.IsDefined(side) && DocumentGeometryBoundary.Contains(lead);
    }

    private static BpmnRoutingObstacle[] CreateAttemptObstacles(
        IReadOnlyList<BpmnRoutingObstacle> actualObstacles,
        ProjectedObjectId sourceOwnerNodeId,
        ProjectedObjectId targetOwnerNodeId,
        double clearance,
        double leadDistance)
    {
        var obstacles = new BpmnRoutingObstacle[actualObstacles.Count];
        for (var index = 0; index < obstacles.Length; index++)
        {
            var obstacle = actualObstacles[index];
            var attemptClearance =
                obstacle.NodeId == sourceOwnerNodeId || obstacle.NodeId == targetOwnerNodeId
                    ? leadDistance
                    : clearance;
            obstacles[index] = obstacle with
            {
                Bounds = Inflate(obstacle.Bounds, attemptClearance),
            };
        }

        return obstacles;
    }

    private static RectD Inflate(RectD bounds, double clearance) =>
        new(
            bounds.X - clearance,
            bounds.Y - clearance,
            bounds.Width + (2d * clearance),
            bounds.Height + (2d * clearance));

    private static bool IsLeadSegmentLegal(
        PointD start,
        PointD end,
        ProjectedObjectId ownerNodeId,
        IReadOnlyList<BpmnRoutingObstacle> obstacles) =>
        obstacles.All(obstacle =>
            obstacle.NodeId == ownerNodeId ||
            !SegmentCrossesStrictInterior(start, end, obstacle.Bounds));

    private static bool ExitsSide(
        ImmutableArray<PointD> path,
        ConnectorAnchorSide side) =>
        path.Length >= 2 && side switch
        {
            ConnectorAnchorSide.Top => path[1].X.Equals(path[0].X) && path[1].Y < path[0].Y,
            ConnectorAnchorSide.Right => path[1].Y.Equals(path[0].Y) && path[1].X > path[0].X,
            ConnectorAnchorSide.Bottom => path[1].X.Equals(path[0].X) && path[1].Y > path[0].Y,
            ConnectorAnchorSide.Left => path[1].Y.Equals(path[0].Y) && path[1].X < path[0].X,
            _ => false,
        };

    private static bool ApproachesSide(
        ImmutableArray<PointD> path,
        ConnectorAnchorSide side) =>
        path.Length >= 2 && side switch
        {
            ConnectorAnchorSide.Top => path[^2].X.Equals(path[^1].X) && path[^2].Y < path[^1].Y,
            ConnectorAnchorSide.Right => path[^2].Y.Equals(path[^1].Y) && path[^2].X > path[^1].X,
            ConnectorAnchorSide.Bottom => path[^2].X.Equals(path[^1].X) && path[^2].Y > path[^1].Y,
            ConnectorAnchorSide.Left => path[^2].Y.Equals(path[^1].Y) && path[^2].X < path[^1].X,
            _ => false,
        };

    private static bool IsHardBodySafe(
        ImmutableArray<PointD> path,
        ProjectedObjectId sourceOwnerNodeId,
        ProjectedObjectId targetOwnerNodeId,
        IReadOnlyList<BpmnRoutingObstacle> actualObstacles)
    {
        for (var segmentIndex = 0; segmentIndex < path.Length - 1; segmentIndex++)
        {
            foreach (var obstacle in actualObstacles)
            {
                if (segmentIndex == 0 && obstacle.NodeId == sourceOwnerNodeId ||
                    segmentIndex == path.Length - 2 && obstacle.NodeId == targetOwnerNodeId)
                {
                    continue;
                }

                if (SegmentCrossesStrictInterior(
                        path[segmentIndex],
                        path[segmentIndex + 1],
                        obstacle.Bounds))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TrySearch(
        PointD start,
        PointD end,
        SegmentDirection incomingDirection,
        IReadOnlyList<BpmnRoutingObstacle> obstacles,
        CancellationToken cancellationToken,
        out ImmutableArray<LegRoutingResult> results)
    {
        results = [];
        if (start == end)
        {
            results = [new([start], incomingDirection, RouteCost.Zero)];
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (IsStrictlyInsideAny(start, obstacles) || IsStrictlyInsideAny(end, obstacles))
        {
            return false;
        }

        var xCoordinates = new SortedSet<double> { 0d, start.X, end.X };
        var yCoordinates = new SortedSet<double> { 0d, start.Y, end.Y };
        foreach (var obstacle in obstacles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (obstacle.Bounds.Left >= 0d)
            {
                xCoordinates.Add(obstacle.Bounds.Left);
            }

            if (obstacle.Bounds.Right >= 0d)
            {
                xCoordinates.Add(obstacle.Bounds.Right);
            }

            if (obstacle.Bounds.Top >= 0d)
            {
                yCoordinates.Add(obstacle.Bounds.Top);
            }

            if (obstacle.Bounds.Bottom >= 0d)
            {
                yCoordinates.Add(obstacle.Bounds.Bottom);
            }
        }

        var candidateGraph = BuildCandidateGraph(
            [.. xCoordinates],
            [.. yCoordinates],
            obstacles,
            cancellationToken);
        var points = candidateGraph.Points;
        var pointIndexes = candidateGraph.PointIndexes;
        if (!pointIndexes.TryGetValue(start, out var startIndex) ||
            !pointIndexes.TryGetValue(end, out var endIndex))
        {
            return false;
        }

        var neighbors = BuildNeighbors(candidateGraph, cancellationToken);
        var stateCount = points.Length * 3;
        var costs = Enumerable.Repeat(RouteCost.Infinite, stateCount).ToArray();
        var predecessors = Enumerable.Repeat(-1, stateCount).ToArray();
        var startState = StateIndex(startIndex, incomingDirection);
        costs[startState] = RouteCost.Zero;
        var pending = new SortedSet<QueueEntry>(QueueEntryComparer.Instance)
        {
            new(startState, points[startIndex], incomingDirection, costs[startState]),
        };

        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Min;
            pending.Remove(current);
            if (current.Cost != costs[current.StateIndex])
            {
                continue;
            }

            var currentPointIndex = PointIndex(current.StateIndex);
            if (currentPointIndex == endIndex)
            {
                continue;
            }

            foreach (var neighborIndex in neighbors[currentPointIndex])
            {
                cancellationToken.ThrowIfCancellationRequested();
                var direction = Direction(points[currentPointIndex], points[neighborIndex]);
                var nextState = StateIndex(neighborIndex, direction);
                var length = Manhattan(points[currentPointIndex], points[neighborIndex]);
                var bend = current.Direction != SegmentDirection.None &&
                    current.Direction != direction;
                var nextCost = current.Cost.Add(length, bend);
                if (nextCost.CompareTo(costs[nextState]) >= 0)
                {
                    continue;
                }

                if (!costs[nextState].IsInfinite)
                {
                    pending.Remove(new QueueEntry(
                        nextState,
                        points[neighborIndex],
                        direction,
                        costs[nextState]));
                }

                costs[nextState] = nextCost;
                predecessors[nextState] = current.StateIndex;
                pending.Add(new QueueEntry(
                    nextState,
                    points[neighborIndex],
                    direction,
                    nextCost));
            }
        }

        var routedLegs = ImmutableArray.CreateBuilder<LegRoutingResult>(2);
        foreach (var finalDirection in new[] { SegmentDirection.Horizontal, SegmentDirection.Vertical })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var finalState = StateIndex(endIndex, finalDirection);
            if (costs[finalState].IsInfinite)
            {
                continue;
            }

            var reversed = new List<PointD>();
            for (var state = finalState; state >= 0; state = predecessors[state])
            {
                reversed.Add(points[PointIndex(state)]);
            }

            reversed.Reverse();
            routedLegs.Add(new LegRoutingResult(
                [.. reversed],
                finalDirection,
                costs[finalState]));
        }

        results = routedLegs.ToImmutable();
        return results.Length != 0;
    }

    private static CandidateGraph BuildCandidateGraph(
        double[] xCoordinates,
        double[] yCoordinates,
        IReadOnlyList<BpmnRoutingObstacle> obstacles,
        CancellationToken cancellationToken)
    {
        var points = new List<PointD>(xCoordinates.Length * yCoordinates.Length);
        var pointIndexes = new Dictionary<PointD, int>();
        var rows = new int[yCoordinates.Length][];
        var columns = Enumerable.Range(0, xCoordinates.Length)
            .Select(static _ => new List<int>())
            .ToArray();
        var rowBlocks = new ImmutableArray<BlockedInterval>[yCoordinates.Length];

        for (var yIndex = 0; yIndex < yCoordinates.Length; yIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var y = yCoordinates[yIndex];
            rowBlocks[yIndex] = BuildBlockedIntervals(
                y,
                alongHorizontalLine: true,
                obstacles,
                cancellationToken);
            var row = new List<int>(xCoordinates.Length);
            var intervalIndex = 0;
            for (var xIndex = 0; xIndex < xCoordinates.Length; xIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var x = xCoordinates[xIndex];
                while (intervalIndex < rowBlocks[yIndex].Length &&
                    x >= rowBlocks[yIndex][intervalIndex].End -
                    BpmnRoutingPolicy.GeometryTolerance)
                {
                    intervalIndex++;
                }

                if (intervalIndex < rowBlocks[yIndex].Length &&
                    IsStrictlyInside(x, rowBlocks[yIndex][intervalIndex]))
                {
                    continue;
                }

                var point = new PointD(x, y);
                var pointIndex = points.Count;
                points.Add(point);
                pointIndexes.Add(point, pointIndex);
                row.Add(pointIndex);
                columns[xIndex].Add(pointIndex);
            }

            rows[yIndex] = [.. row];
        }

        var columnBlocks = new ImmutableArray<BlockedInterval>[xCoordinates.Length];
        for (var xIndex = 0; xIndex < xCoordinates.Length; xIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            columnBlocks[xIndex] = BuildBlockedIntervals(
                xCoordinates[xIndex],
                alongHorizontalLine: false,
                obstacles,
                cancellationToken);
        }

        return new CandidateGraph(
            [.. points],
            pointIndexes,
            rows,
            columns.Select(static column => column.ToArray()).ToArray(),
            rowBlocks,
            columnBlocks);
    }

    private static int[][] BuildNeighbors(
        CandidateGraph graph,
        CancellationToken cancellationToken)
    {
        var neighbors = Enumerable.Range(0, graph.Points.Length)
            .Select(static _ => new List<int>(4))
            .ToArray();
        for (var index = 0; index < graph.Rows.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddConsecutiveVisibilityEdges(
                graph.Points,
                neighbors,
                graph.Rows[index],
                graph.RowBlocks[index],
                alongHorizontalLine: true,
                cancellationToken);
        }

        for (var index = 0; index < graph.Columns.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddConsecutiveVisibilityEdges(
                graph.Points,
                neighbors,
                graph.Columns[index],
                graph.ColumnBlocks[index],
                alongHorizontalLine: false,
                cancellationToken);
        }

        return neighbors.Select(static list =>
        {
            list.Sort();
            return list.ToArray();
        }).ToArray();
    }

    private static void AddConsecutiveVisibilityEdges(
        PointD[] points,
        List<int>[] neighbors,
        int[] orderedPointIndexes,
        ImmutableArray<BlockedInterval> blockedIntervals,
        bool alongHorizontalLine,
        CancellationToken cancellationToken)
    {
        var intervalIndex = 0;
        for (var index = 0; index < orderedPointIndexes.Length - 1; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = orderedPointIndexes[index];
            var second = orderedPointIndexes[index + 1];
            var firstCoordinate = alongHorizontalLine
                ? points[first].X
                : points[first].Y;
            var secondCoordinate = alongHorizontalLine
                ? points[second].X
                : points[second].Y;
            var start = Math.Min(firstCoordinate, secondCoordinate);
            var end = Math.Max(firstCoordinate, secondCoordinate);
            while (intervalIndex < blockedIntervals.Length &&
                blockedIntervals[intervalIndex].End <=
                start + BpmnRoutingPolicy.GeometryTolerance)
            {
                intervalIndex++;
            }

            if (intervalIndex < blockedIntervals.Length &&
                SegmentCrossesStrictInterior(start, end, blockedIntervals[intervalIndex]))
            {
                continue;
            }

            neighbors[first].Add(second);
            neighbors[second].Add(first);
        }
    }

    private static ImmutableArray<BlockedInterval> BuildBlockedIntervals(
        double fixedCoordinate,
        bool alongHorizontalLine,
        IReadOnlyList<BpmnRoutingObstacle> obstacles,
        CancellationToken cancellationToken)
    {
        var intervals = new List<BlockedInterval>(obstacles.Count);
        foreach (var obstacle in obstacles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bounds = obstacle.Bounds;
            if (alongHorizontalLine
                    ? fixedCoordinate > bounds.Top + BpmnRoutingPolicy.GeometryTolerance &&
                      fixedCoordinate < bounds.Bottom - BpmnRoutingPolicy.GeometryTolerance
                    : fixedCoordinate > bounds.Left + BpmnRoutingPolicy.GeometryTolerance &&
                      fixedCoordinate < bounds.Right - BpmnRoutingPolicy.GeometryTolerance)
            {
                intervals.Add(alongHorizontalLine
                    ? new BlockedInterval(bounds.Left, bounds.Right)
                    : new BlockedInterval(bounds.Top, bounds.Bottom));
            }
        }

        intervals.Sort(static (left, right) =>
        {
            var comparison = left.Start.CompareTo(right.Start);
            return comparison != 0 ? comparison : left.End.CompareTo(right.End);
        });
        if (intervals.Count < 2)
        {
            return [.. intervals];
        }

        var merged = new List<BlockedInterval>(intervals.Count) { intervals[0] };
        for (var index = 1; index < intervals.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = merged[^1];
            var next = intervals[index];
            if (next.Start < current.End - BpmnRoutingPolicy.GeometryTolerance)
            {
                merged[^1] = current with { End = Math.Max(current.End, next.End) };
            }
            else
            {
                merged.Add(next);
            }
        }

        return [.. merged];
    }

    private static bool IsStrictlyInside(double coordinate, BlockedInterval interval) =>
        coordinate > interval.Start + BpmnRoutingPolicy.GeometryTolerance &&
        coordinate < interval.End - BpmnRoutingPolicy.GeometryTolerance;

    private static bool SegmentCrossesStrictInterior(
        double start,
        double end,
        BlockedInterval interval) =>
        Math.Max(start, interval.Start + BpmnRoutingPolicy.GeometryTolerance) <
        Math.Min(end, interval.End - BpmnRoutingPolicy.GeometryTolerance);

    private static bool IsStrictlyInsideAny(
        PointD point,
        IReadOnlyList<BpmnRoutingObstacle> obstacles) =>
        obstacles.Any(obstacle => IsStrictlyInside(point, obstacle.Bounds));

    private static bool IsStrictlyInside(PointD point, RectD bounds)
    {
        var tolerance = BpmnRoutingPolicy.GeometryTolerance;
        return point.X > bounds.Left + tolerance &&
            point.X < bounds.Right - tolerance &&
            point.Y > bounds.Top + tolerance &&
            point.Y < bounds.Bottom - tolerance;
    }

    private static bool SegmentCrossesStrictInterior(
        PointD start,
        PointD end,
        RectD bounds)
    {
        var tolerance = BpmnRoutingPolicy.GeometryTolerance;
        if (start.X.Equals(end.X))
        {
            return start.X > bounds.Left + tolerance &&
                start.X < bounds.Right - tolerance &&
                Math.Max(Math.Min(start.Y, end.Y), bounds.Top + tolerance) <
                Math.Min(Math.Max(start.Y, end.Y), bounds.Bottom - tolerance);
        }

        if (start.Y.Equals(end.Y))
        {
            return start.Y > bounds.Top + tolerance &&
                start.Y < bounds.Bottom - tolerance &&
                Math.Max(Math.Min(start.X, end.X), bounds.Left + tolerance) <
                Math.Min(Math.Max(start.X, end.X), bounds.Right - tolerance);
        }

        return true;
    }

    private static void AppendPoint(
        List<PointD> points,
        List<bool> protectedPoints,
        PointD point,
        bool isProtected)
    {
        points.Add(point);
        protectedPoints.Add(isProtected);
    }

    private static void SimplifyGeneratedPoints(
        List<PointD> points,
        List<bool> protectedPoints)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var index = 1; index < points.Count - 1; index++)
            {
                if (protectedPoints[index])
                {
                    continue;
                }

                var previous = points[index - 1];
                var current = points[index];
                var next = points[index + 1];
                if (current == previous || current == next ||
                    previous.X.Equals(current.X) && current.X.Equals(next.X) ||
                    previous.Y.Equals(current.Y) && current.Y.Equals(next.Y))
                {
                    points.RemoveAt(index);
                    protectedPoints.RemoveAt(index);
                    changed = true;
                    break;
                }
            }
        }
    }

    private static bool IsOrthogonal(List<PointD> points)
    {
        for (var index = 0; index < points.Count - 1; index++)
        {
            if (!points[index].X.Equals(points[index + 1].X) &&
                !points[index].Y.Equals(points[index + 1].Y))
            {
                return false;
            }
        }

        return true;
    }

    private static SegmentDirection Direction(PointD start, PointD end) =>
        start.X.Equals(end.X)
            ? SegmentDirection.Vertical
            : SegmentDirection.Horizontal;

    private static double Manhattan(PointD left, PointD right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);

    private static int StateIndex(int pointIndex, SegmentDirection direction) =>
        (pointIndex * 3) + (int)direction;

    private static int PointIndex(int stateIndex) => stateIndex / 3;

    private static int CompareCandidates(RouteCandidate left, RouteCandidate right)
    {
        var comparison = left.Cost.CompareTo(right.Cost);
        if (comparison != 0)
        {
            return comparison;
        }

        var leftPoints = left.Legs.SelectMany(static leg => leg).ToArray();
        var rightPoints = right.Legs.SelectMany(static leg => leg).ToArray();
        var pointCount = Math.Min(leftPoints.Length, rightPoints.Length);
        for (var index = 0; index < pointCount; index++)
        {
            comparison = leftPoints[index].Y.CompareTo(rightPoints[index].Y);
            comparison = comparison != 0
                ? comparison
                : leftPoints[index].X.CompareTo(rightPoints[index].X);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        comparison = leftPoints.Length.CompareTo(rightPoints.Length);
        return comparison != 0 ? comparison : left.Direction.CompareTo(right.Direction);
    }

    private static int CompareFinalCandidates(
        RouteCandidate left,
        RouteCandidate right,
        SegmentDirection targetDirection)
    {
        var comparison = (left.Direction != targetDirection)
            .CompareTo(right.Direction != targetDirection);
        if (comparison != 0)
        {
            return comparison;
        }

        var leftPoints = left.Legs.SelectMany(static leg => leg).ToArray();
        var rightPoints = right.Legs.SelectMany(static leg => leg).ToArray();
        var pointCount = Math.Min(leftPoints.Length, rightPoints.Length);
        for (var index = 0; index < pointCount; index++)
        {
            comparison = leftPoints[index].X.CompareTo(rightPoints[index].X);
            comparison = comparison != 0
                ? comparison
                : leftPoints[index].Y.CompareTo(rightPoints[index].Y);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        comparison = leftPoints.Length.CompareTo(rightPoints.Length);
        return comparison != 0 ? comparison : left.Direction.CompareTo(right.Direction);
    }

    private readonly record struct BlockedInterval(double Start, double End);

    private sealed record CandidateGraph(
        PointD[] Points,
        Dictionary<PointD, int> PointIndexes,
        int[][] Rows,
        int[][] Columns,
        ImmutableArray<BlockedInterval>[] RowBlocks,
        ImmutableArray<BlockedInterval>[] ColumnBlocks);

    private readonly record struct LegRoutingResult(
        ImmutableArray<PointD> Path,
        SegmentDirection FinalDirection,
        RouteCost Cost);

    private sealed record RouteCandidate(
        RouteCost Cost,
        SegmentDirection Direction,
        ImmutableArray<ImmutableArray<PointD>> Legs);

    private readonly record struct RouteCost(double Total, int Bends, double Length) :
        IComparable<RouteCost>
    {
        internal static RouteCost Zero { get; } = new(0d, 0, 0d);

        internal static RouteCost Infinite { get; } =
            new(double.PositiveInfinity, int.MaxValue, double.PositiveInfinity);

        internal bool IsInfinite => double.IsPositiveInfinity(Total);

        internal RouteCost Add(double length, bool bend)
        {
            var bendCount = Bends + (bend ? 1 : 0);
            var totalLength = Length + length;
            return new RouteCost(
                totalLength + (bendCount * BpmnRoutingPolicy.BendPenalty),
                bendCount,
                totalLength);
        }

        internal RouteCost Combine(RouteCost other)
        {
            var bendCount = Bends + other.Bends;
            var totalLength = Length + other.Length;
            return new RouteCost(
                totalLength + (bendCount * BpmnRoutingPolicy.BendPenalty),
                bendCount,
                totalLength);
        }

        public int CompareTo(RouteCost other)
        {
            var comparison = Total.CompareTo(other.Total);
            comparison = comparison != 0 ? comparison : Bends.CompareTo(other.Bends);
            return comparison != 0 ? comparison : Length.CompareTo(other.Length);
        }
    }

    private readonly record struct QueueEntry(
        int StateIndex,
        PointD Point,
        SegmentDirection Direction,
        RouteCost Cost);

    private sealed class QueueEntryComparer : IComparer<QueueEntry>
    {
        internal static QueueEntryComparer Instance { get; } = new();

        public int Compare(QueueEntry left, QueueEntry right)
        {
            var comparison = left.Cost.CompareTo(right.Cost);
            comparison = comparison != 0 ? comparison : left.Point.Y.CompareTo(right.Point.Y);
            comparison = comparison != 0 ? comparison : left.Point.X.CompareTo(right.Point.X);
            comparison = comparison != 0
                ? comparison
                : left.Direction.CompareTo(right.Direction);
            return comparison != 0 ? comparison : left.StateIndex.CompareTo(right.StateIndex);
        }
    }
}
