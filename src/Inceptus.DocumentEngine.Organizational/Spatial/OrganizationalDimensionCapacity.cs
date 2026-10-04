using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Organizational.Spatial;

/// <summary>One body-based capacity policy for saved geometry and frozen pointer constraints.</summary>
internal static class OrganizationalDimensionCapacity
{
    internal static readonly double CoordinateBudget = Math.Sqrt(double.MaxValue) / 16d;
    // Beyond this scale a double can no longer preserve the fixed 32-unit content
    // inset. Keeping that increment representable also preserves every 40-unit
    // gap and the minimum expanded row when a preceding height changes.
    private static readonly double RepresentedStackBudget = Math.BitDecrement(Math.ScaleB(32d, 52));

    internal static Canvas2DSpatialDimensionConstraints Width(IEnumerable<Canvas2DScopeGeometryNodeBounds> nodes)
    {
        var bodies = nodes.ToArray();
        var minimum = Math.Max(520d, 70d + bodies.Select(static node => node.BodyBounds.Right).DefaultIfEmpty(0d).Max());
        return Constraints(minimum, CoordinateBudget - 40d, bodies,
            node => node.BodyBounds.Right + 70d == minimum);
    }

    internal static Canvas2DSpatialDimensionConstraints Height(IEnumerable<Canvas2DScopeGeometryNodeBounds> nodes,
        double compactNameHeight, double otherStackExtent)
    {
        var bodies = nodes.ToArray();
        var minimum = Math.Max(Math.Max(144d, compactNameHeight),
            32d + bodies.Select(static node => node.BodyBounds.Bottom).DefaultIfEmpty(0d).Max());
        return Constraints(minimum, Math.Min(CoordinateBudget, RepresentedStackBudget) - otherStackExtent, bodies,
            node => node.BodyBounds.Bottom + 32d == minimum);
    }

    private static Canvas2DSpatialDimensionConstraints Constraints(double minimum, double maximum,
        Canvas2DScopeGeometryNodeBounds[] bodies, Func<Canvas2DScopeGeometryNodeBounds, bool> limiting)
    {
        var diagnostics = bodies.Where(static node => node.BodyBounds.Left < -32d || node.BodyBounds.Top < -32d)
            .Select(static node => new Diagnostic("ORGANIZATIONAL_DIMENSION_CHILD_ORIGIN", DiagnosticSeverity.Error,
                "The complete child body must fit at the region's fixed top and left boundary.", node.VisualStateId.Value)).ToList();
        if (minimum > maximum)
            diagnostics.Add(new Diagnostic("ORGANIZATIONAL_DIMENSION_COORDINATE_CAPACITY", DiagnosticSeverity.Error,
                "The required complete body extent exceeds the finite coordinate capacity of this stack."));
        return new Canvas2DSpatialDimensionConstraints(minimum, Math.Max(minimum, maximum),
            bodies.Where(limiting).Select(static node => node.VisualStateId), diagnostics);
    }
}
