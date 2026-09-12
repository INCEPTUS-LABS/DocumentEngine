using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

/// <summary>
/// Derives a display-only connector path with sampled line jumps at strict orthogonal crossings.
/// Horizontal segments own the jump; routed centerlines remain unchanged.
/// </summary>
internal static class Canvas2DConnectorLineJumpGeometry
{
    internal const double HalfWidth = 5d;
    internal const double Height = 4d;

    private const int SampleIntervalCount = 8;
    private const double StrictInteriorTolerance = 1e-9;

    internal static ImmutableArray<PointD> Create(
        IReadOnlyList<PointD> logicalPath,
        IEnumerable<IReadOnlyList<PointD>> crossingPaths) =>
        CreatePresentation(logicalPath, crossingPaths).DisplayPath;

    internal static Canvas2DConnectorLineJumpPresentation CreatePresentation(
        IReadOnlyList<PointD> logicalPath,
        IEnumerable<IReadOnlyList<PointD>> crossingPaths)
    {
        ArgumentNullException.ThrowIfNull(logicalPath);
        ArgumentNullException.ThrowIfNull(crossingPaths);

        var otherPaths = crossingPaths.ToArray();
        if (Array.Exists(otherPaths, static path => path is null))
        {
            throw new ArgumentException(
                "Crossing paths cannot contain null values.",
                nameof(crossingPaths));
        }

        if (logicalPath.Count < 2)
        {
            return new Canvas2DConnectorLineJumpPresentation(
                logicalPath.ToImmutableArray(),
                []);
        }

        var displayPath = ImmutableArray.CreateBuilder<PointD>();
        var jumpPaths = ImmutableArray.CreateBuilder<ImmutableArray<PointD>>();
        AddDistinct(displayPath, logicalPath[0]);
        for (var segmentIndex = 0; segmentIndex < logicalPath.Count - 1; segmentIndex++)
        {
            var start = logicalPath[segmentIndex];
            var end = logicalPath[segmentIndex + 1];
            if (IsHorizontal(start, end))
            {
                foreach (var jump in ResolveFittingCrossings(start, end, otherPaths))
                {
                    var jumpPath = CreateJump(
                        start.Y,
                        jump.CrossingX,
                        jump.HalfWidth,
                        end.X > start.X ? 1d : -1d);
                    foreach (var point in jumpPath)
                    {
                        AddDistinct(displayPath, point);
                    }

                    jumpPaths.Add(jumpPath);
                }
            }

            AddDistinct(displayPath, end);
        }

        return new Canvas2DConnectorLineJumpPresentation(
            displayPath.ToImmutable(),
            jumpPaths.ToImmutable());
    }

    private static List<LineJump> ResolveFittingCrossings(
        PointD horizontalStart,
        PointD horizontalEnd,
        IReadOnlyList<PointD>[] crossingPaths)
    {
        var minimumX = Math.Min(horizontalStart.X, horizontalEnd.X);
        var maximumX = Math.Max(horizontalStart.X, horizontalEnd.X);
        var candidates = new SortedSet<double>();
        foreach (var crossingPath in crossingPaths)
        {
            for (var segmentIndex = 0; segmentIndex < crossingPath.Count - 1; segmentIndex++)
            {
                var verticalStart = crossingPath[segmentIndex];
                var verticalEnd = crossingPath[segmentIndex + 1];
                if (!IsVertical(verticalStart, verticalEnd) ||
                    !IsStrictlyBetween(verticalStart.X, minimumX, maximumX) ||
                    !IsStrictlyBetween(
                        horizontalStart.Y,
                        verticalStart.Y,
                        verticalEnd.Y))
                {
                    continue;
                }

                candidates.Add(verticalStart.X);
            }
        }

        var ordered = candidates.ToArray();
        var fitted = new List<LineJump>(ordered.Length);
        for (var index = 0; index < ordered.Length; index++)
        {
            var crossingX = ordered[index];
            var leftBoundary = index == 0
                ? minimumX
                : Midpoint(ordered[index - 1], crossingX);
            var rightBoundary = index == ordered.Length - 1
                ? maximumX
                : Midpoint(crossingX, ordered[index + 1]);
            var availableHalfWidth = Math.Min(
                crossingX - leftBoundary,
                rightBoundary - crossingX);
            if (availableHalfWidth <= 0d)
            {
                continue;
            }

            var fittedHalfWidth = availableHalfWidth > HalfWidth
                ? HalfWidth
                : Math.BitDecrement(availableHalfWidth);
            if (fittedHalfWidth <= 0d)
            {
                fittedHalfWidth = availableHalfWidth / 2d;
            }

            fitted.Add(new LineJump(crossingX, fittedHalfWidth));
        }

        if (horizontalEnd.X < horizontalStart.X)
        {
            fitted.Reverse();
        }

        return fitted;
    }

    private static ImmutableArray<PointD> CreateJump(
        double baselineY,
        double crossingX,
        double halfWidth,
        double traversalDirection)
    {
        var path = ImmutableArray.CreateBuilder<PointD>(SampleIntervalCount + 1);
        for (var sampleIndex = 0; sampleIndex <= SampleIntervalCount; sampleIndex++)
        {
            var progress = sampleIndex / (double)SampleIntervalCount;
            var offset = -halfWidth + (2d * halfWidth * progress);
            var verticalOffset = sampleIndex is 0 or SampleIntervalCount
                ? 0d
                : Height * Math.Sin(Math.PI * progress);
            AddDistinct(path, new PointD(
                crossingX + (traversalDirection * offset),
                baselineY + verticalOffset));
        }

        return path.MoveToImmutable();
    }

    private static void AddDistinct(
        ImmutableArray<PointD>.Builder path,
        PointD point)
    {
        if (path.Count == 0 || path[^1] != point)
        {
            path.Add(point);
        }
    }

    private static bool IsHorizontal(PointD start, PointD end) =>
        start.Y.Equals(end.Y) && !start.X.Equals(end.X);

    private static bool IsVertical(PointD start, PointD end) =>
        start.X.Equals(end.X) && !start.Y.Equals(end.Y);

    private static bool IsStrictlyBetween(double value, double first, double second) =>
        value > Math.Min(first, second) + StrictInteriorTolerance &&
        value < Math.Max(first, second) - StrictInteriorTolerance;

    private static double Midpoint(double first, double second) =>
        (first / 2d) + (second / 2d);

    private readonly record struct LineJump(double CrossingX, double HalfWidth);
}

internal readonly record struct Canvas2DConnectorLineJumpPresentation(
    ImmutableArray<PointD> DisplayPath,
    ImmutableArray<ImmutableArray<PointD>> JumpPaths);

internal static class Canvas2DConnectorLineJumpMetadata
{
    private const string HitTargetSceneObjectId =
        "inceptus.canvas2d:connector-line-jump-hit-target";

    internal static PropertyMap Create(SceneObjectId hitTargetId) =>
        new(
        [
            new KeyValuePair<string, PropertyValue>(
                HitTargetSceneObjectId,
                PropertyValue.FromText(hitTargetId.Value)),
        ]);

    internal static bool HasHitTarget(Canvas2DSceneItem item) =>
        TryResolveHitTarget(item, out _);

    internal static bool TryResolveHitTarget(
        Canvas2DSceneItem item,
        out SceneObjectId hitTargetId)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Metadata.TryGetValue(HitTargetSceneObjectId, out var value) &&
            value.Kind == PropertyValueKind.Text &&
            value.TextValue is { Length: > 0 } text)
        {
            hitTargetId = new SceneObjectId(text);
            return true;
        }

        hitTargetId = default!;
        return false;
    }
}
