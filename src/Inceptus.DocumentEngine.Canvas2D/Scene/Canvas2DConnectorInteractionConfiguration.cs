namespace Inceptus.DocumentEngine.Canvas2D.Scene;

/// <summary>
/// Immutable document-space interaction policy for Canvas2D connector paths.
/// </summary>
internal sealed class Canvas2DConnectorInteractionConfiguration :
    IEquatable<Canvas2DConnectorInteractionConfiguration>
{
    internal Canvas2DConnectorInteractionConfiguration(
        double pathHitTolerance = 5d,
        double routePointProximityTolerance = 5d)
    {
        if (!double.IsFinite(pathHitTolerance) || pathHitTolerance < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pathHitTolerance),
                pathHitTolerance,
                "The connector path hit tolerance must be finite and non-negative.");
        }

        if (!double.IsFinite(routePointProximityTolerance) || routePointProximityTolerance < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(routePointProximityTolerance),
                routePointProximityTolerance,
                "The route-point proximity tolerance must be finite and non-negative.");
        }

        PathHitTolerance = pathHitTolerance;
        RoutePointProximityTolerance = routePointProximityTolerance;
    }

    internal static Canvas2DConnectorInteractionConfiguration Default { get; } = new();

    internal double PathHitTolerance { get; }

    internal double RoutePointProximityTolerance { get; }

    public bool Equals(Canvas2DConnectorInteractionConfiguration? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        PathHitTolerance.Equals(other.PathHitTolerance) &&
        RoutePointProximityTolerance.Equals(other.RoutePointProximityTolerance);

    public override bool Equals(object? obj) =>
        Equals(obj as Canvas2DConnectorInteractionConfiguration);

    public override int GetHashCode() => HashCode.Combine(
        PathHitTolerance,
        RoutePointProximityTolerance);
}
