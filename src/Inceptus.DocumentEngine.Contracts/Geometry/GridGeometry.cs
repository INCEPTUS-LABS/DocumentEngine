using Inceptus.DocumentEngine.Contracts.EditorState;

namespace Inceptus.DocumentEngine.Contracts.Geometry;

/// <summary>Diagram-space snapping, with exact midpoint ties rounded away from zero.</summary>
public static class GridGeometry
{
    public static PointD Snap(PointD center, GridSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.SnapToGrid
            ? new(SnapCoordinate(center.X, settings.PointDistance), SnapCoordinate(center.Y, settings.PointDistance))
            : center;
    }

    private static double SnapCoordinate(double coordinate, double distance)
    {
        var quotient = coordinate / distance;
        // At sub-ULP spacings every representable coordinate is already indistinguishable
        // from a grid point. Avoid overflowing for a valid, extremely small spacing.
        if (!double.IsFinite(quotient) || Math.Abs(quotient) >= 4503599627370496d) return coordinate;
        var snapped = distance * Math.Round(quotient, MidpointRounding.AwayFromZero);
        return double.IsFinite(snapped) ? snapped : coordinate;
    }
}
