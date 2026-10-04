using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

internal static class Canvas2DRouteBendGeometry
{
    internal const double SnapEnterCss = 6d;
    internal const double SnapReleaseCss = 9d;

    internal static (PointD Point, Canvas2DRouteBendSnapState State) Snap(
        ImmutableArray<PointD> original, int bend, PointD raw, double zoom,
        Canvas2DRouteBendSnapState state, Canvas2DConnectorPresentationMapping? mapping = null,
        bool controlKey = false)
    {
        // Ctrl orthogonal movement is an alternative to magnetic alignment.
        // Discard retained targets so releasing Ctrl starts with the entry radius.
        if (controlKey) return (raw, default);
        var displayed = Map(raw);
        var previous = Map(original[bend - 1]);
        var next = Map(original[bend + 1]);
        var x = Axis(displayed.X, previous.X, next.X, state.XTarget);
        var y = Axis(displayed.Y, previous.Y, next.Y, state.YTarget);
        var targets = new Canvas2DRouteBendSnapState(x, y);
        return (ApplySnap(original, raw, targets), targets);

        PointD Map(PointD point) => mapping?.MapLogicalToScene(point) ?? point;

        int? Axis(double value, double before, double after, int? retained)
        {
            // Original route indices are local gesture identities. Equal coordinates
            // choose the previous point on entry; retention never switches targets.
            if (retained is { } index && (index == bend - 1 || index == bend + 1) &&
                Math.Abs(value - (index == bend - 1 ? before : after)) * zoom <= SnapReleaseCss)
                return index;
            var beforeDistance = Math.Abs(value - before) * zoom;
            var afterDistance = Math.Abs(value - after) * zoom;
            if (Math.Min(beforeDistance, afterDistance) > SnapEnterCss) return null;
            return beforeDistance <= afterDistance ? bend - 1 : bend + 1;
        }
    }

    private static PointD ApplySnap(ImmutableArray<PointD> original, PointD raw, Canvas2DRouteBendSnapState state) =>
        new(state.XTarget is { } x ? original[x].X : raw.X,
            state.YTarget is { } y ? original[y].Y : raw.Y);

    internal static ImmutableArray<PointD> Candidate(
        ImmutableArray<PointD> original, int bend, VectorD delta, bool controlKey) =>
        CandidateAt(original, bend, original[bend] + delta, controlKey);

    internal static ImmutableArray<PointD> CandidateAt(
        ImmutableArray<PointD> original, int bend, PointD active, bool controlKey)
    {
        if (bend <= 0 || bend >= original.Length - 1)
            throw new ArgumentOutOfRangeException(nameof(bend));
        var points = original.ToArray();
        var delta = active - original[bend];
        points[bend] = active;
        if (controlKey)
        {
            MoveNeighbour(bend - 1);
            MoveNeighbour(bend + 1);
        }
        return [.. points];

        void MoveNeighbour(int neighbour)
        {
            // Only direct editable neighbours participate; endpoints are immutable.
            if (neighbour <= 0 || neighbour >= original.Length - 1) return;
            points[neighbour] += Canvas2DSegmentGeometry.Orientation(original[bend], original[neighbour]) switch
            {
                Canvas2DSegmentOrientation.Horizontal => new VectorD(0, delta.Y),
                Canvas2DSegmentOrientation.Vertical => new VectorD(delta.X, 0),
                _ => default,
            };
        }
    }

    internal static ImmutableArray<PointD> DisplayedCandidate(
        Canvas2DSceneItem target, int bend, VectorD displayedDelta, bool controlKey,
        Canvas2DRouteBendSnapState snap = default)
    {
        if (controlKey) snap = default;
        if (target.ConnectorPresentationMapping is not { } mapping)
        {
            var path = Canvas2DConnectorPathMetadata.ResolveEditable(target);
            return CandidateAt(path, bend, ApplySnap(path, path[bend] + displayedDelta, snap), controlKey);
        }
        var original = mapping.CanonicalEditablePath;
        var raw = mapping.MapSceneToLogical(mapping.DisplayedEditablePath[bend] + displayedDelta);
        var candidate = CandidateAt(original, bend, ApplySnap(original, raw, snap), controlKey).ToArray();
        for (var index = 1; index < candidate.Length - 1; index++)
            candidate[index] = mapping.MapLogicalToScene(candidate[index]);
        candidate[0] = mapping.DisplayedEditablePath[0];
        candidate[^1] = mapping.DisplayedEditablePath[^1];
        return [.. candidate];
    }
}

internal readonly record struct Canvas2DRouteBendSnapState(int? XTarget, int? YTarget);
