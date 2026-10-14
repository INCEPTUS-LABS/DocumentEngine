using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Bpmn.Placement;

/// <summary>Grid constraints for explicit BPMN placement planning, in displayed scope coordinates.</summary>
internal static class BpmnGridPlacement
{
    // Use the shared midpoint rule; advance only when its nearest point violates a lower bound.
    internal static double AtOrAfter(double preferred, double minimum, GridSettings grid)
    {
        var snapped = GridGeometry.Snap(new(Math.Max(preferred, minimum), 0), grid).X;
        if (snapped < minimum) snapped = GridGeometry.Snap(new(snapped + grid.PointDistance, 0), grid).X;
        if (snapped < minimum || !double.IsFinite(snapped))
            throw new InvalidOperationException("The grid cannot represent a position within the placement constraints.");
        return snapped;
    }

    internal static RectD Snap(RectD local, Matrix2D localToScope, GridSettings grid, double minimumLeft)
    {
        var center = localToScope.TransformPoint(new(local.X + local.Width / 2, local.Y + local.Height / 2));
        var snapped = GridGeometry.Snap(center, grid);
        snapped = new(AtOrAfter(snapped.X, minimumLeft + local.Width / 2 + localToScope.OffsetX, grid), snapped.Y);
        if (!localToScope.TryInvert(out var inverse)) throw new InvalidOperationException("The placement frame is unavailable.");
        var point = inverse.TransformPoint(snapped);
        var bounds = new RectD(point.X - local.Width / 2, point.Y - local.Height / 2, local.Width, local.Height);
        Validate(bounds);
        return bounds;
    }

    internal static void Validate(RectD bounds)
    {
        // Extremely coarse grids can consume all precision needed to represent a node or its gaps.
        if (bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top)
            throw new InvalidOperationException("The grid is too coarse to represent the element geometry safely.");
    }

    internal static RectD RetainEquivalentCoordinates(RectD candidate, RectD original, Matrix2D frame)
    {
        // Pool frames include measured caption heights. Their reconstruction can differ by a
        // few ULPs after a commit. Keep the exact authored coordinate when the displayed
        // centers are indistinguishable at that frame's precision, avoiding empty History edits.
        // This is a floating-point comparison, not another rounding or snapping rule.
        var target = frame.TransformPoint(new(candidate.X + candidate.Width / 2, candidate.Y + candidate.Height / 2));
        var prior = frame.TransformPoint(new(original.X + original.Width / 2, original.Y + original.Height / 2));
        static bool Equivalent(double first, double second, double offset)
        {
            var magnitude = Math.Max(Math.Max(Math.Abs(first), Math.Abs(second)), Math.Abs(offset));
            var ulp = Math.BitIncrement(magnitude) - magnitude;
            return double.IsFinite(ulp) && Math.Abs(first - second) <= 4 * ulp;
        }
        return new(Equivalent(target.X, prior.X, frame.OffsetX) ? original.X : candidate.X,
            Equivalent(target.Y, prior.Y, frame.OffsetY) ? original.Y : candidate.Y, candidate.Width, candidate.Height);
    }
}
