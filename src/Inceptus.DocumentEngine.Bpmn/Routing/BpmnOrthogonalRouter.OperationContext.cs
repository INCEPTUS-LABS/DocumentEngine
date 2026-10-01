using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Routing;

internal static partial class BpmnOrthogonalRouter
{
    /// <summary>
    /// Owned by one complete Route call. No routes or search states survive an edge search.
    /// </summary>
    internal sealed class OperationContext(IReadOnlyList<BpmnRoutingObstacle> actualObstacles)
    {
        private ObstacleContext? _preferred;

        internal IReadOnlyList<BpmnRoutingObstacle> ActualObstacles { get; } = actualObstacles;

        internal ObstacleContext GetAttempt(
            ProjectedObjectId sourceOwnerNodeId,
            ProjectedObjectId targetOwnerNodeId,
            double clearance,
            double leadDistance,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Only equal inflation is independent of endpoint ownership. Keep relaxed,
            // edge-dependent attempts isolated, including their coordinates and intervals.
            var share = clearance == BpmnRoutingPolicy.PreferredObstacleClearance &&
                leadDistance == clearance;
            if (share && _preferred is not null)
            {
                return _preferred;
            }

            var context = new ObstacleContext(CreateAttemptObstacles(
                ActualObstacles,
                sourceOwnerNodeId,
                targetOwnerNodeId,
                clearance,
                leadDistance,
                cancellationToken));
            if (share)
            {
                _preferred = context;
            }

            return context;
        }
    }

    internal sealed class ObstacleContext(BpmnRoutingObstacle[] obstacles)
    {
        private double[]? _xCore;
        private double[]? _yCore;
        private readonly Dictionary<double, ImmutableArray<BlockedInterval>> _rowBlocks = [];
        private readonly Dictionary<double, ImmutableArray<BlockedInterval>> _columnBlocks = [];
        private double[]? _lastX;
        private double[]? _lastY;
        private SearchGraph? _lastGraph;

        internal IReadOnlyList<BpmnRoutingObstacle> Obstacles { get; } = obstacles;

        internal SearchGraph GetGraph(
            PointD start,
            PointD end,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCoordinates(cancellationToken);
            var xCoordinates = InsertEndpoints(_xCore!, start.X, end.X, cancellationToken);
            var yCoordinates = InsertEndpoints(_yCore!, start.Y, end.Y, cancellationToken);
            if (_lastGraph is not null &&
                xCoordinates.AsSpan().SequenceEqual(_lastX) &&
                yCoordinates.AsSpan().SequenceEqual(_lastY))
            {
                return _lastGraph;
            }

            var candidates = BuildCandidateGraph(
                xCoordinates,
                yCoordinates,
                this,
                cancellationToken);
            var graph = new SearchGraph(candidates, BuildNeighbors(candidates, cancellationToken));
            // Bound retained topology to one graph; it is read-only after construction.
            // Start/end indexes and all directional costs/predecessors remain search-local.
            _lastX = xCoordinates;
            _lastY = yCoordinates;
            _lastGraph = graph;
            return graph;
        }

        internal ImmutableArray<BlockedInterval> GetBlockedIntervals(
            double coordinate,
            bool alongHorizontalLine,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cache = alongHorizontalLine ? _rowBlocks : _columnBlocks;
            if (cache.TryGetValue(coordinate, out var intervals))
            {
                return intervals;
            }

            intervals = BuildBlockedIntervals(
                coordinate,
                alongHorizontalLine,
                Obstacles,
                cancellationToken);
            // Retain only obstacle-core lines. Arbitrarily many authored waypoints cannot
            // grow this cache; endpoint-only lines live in the single candidate graph.
            var core = alongHorizontalLine ? _yCore! : _xCore!;
            if (Array.BinarySearch(core, coordinate) >= 0)
            {
                cache.Add(coordinate, intervals);
            }

            return intervals;
        }

        private void EnsureCoordinates(CancellationToken cancellationToken)
        {
            if (_xCore is not null)
            {
                return;
            }

            var xCoordinates = new SortedSet<double> { 0d };
            var yCoordinates = new SortedSet<double> { 0d };
            foreach (var obstacle in Obstacles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddNonnegative(xCoordinates, obstacle.Bounds.Left);
                AddNonnegative(xCoordinates, obstacle.Bounds.Right);
                AddNonnegative(yCoordinates, obstacle.Bounds.Top);
                AddNonnegative(yCoordinates, obstacle.Bounds.Bottom);
            }

            _xCore = [.. xCoordinates];
            _yCore = [.. yCoordinates];
        }

        private static void AddNonnegative(SortedSet<double> coordinates, double value)
        {
            if (value >= 0d)
            {
                coordinates.Add(value);
            }
        }

        private static double[] InsertEndpoints(
            double[] core,
            double start,
            double end,
            CancellationToken cancellationToken)
        {
            var coordinates = new SortedSet<double>();
            foreach (var value in core)
            {
                cancellationToken.ThrowIfCancellationRequested();
                coordinates.Add(value);
            }

            coordinates.Add(start);
            coordinates.Add(end);
            return [.. coordinates];
        }
    }

    internal sealed record SearchGraph(CandidateGraph Candidates, int[][] Neighbors);
}
