namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Transient automatic source-relative placement requested by a notation's projection.
/// Absence of this intent retains the generic midpoint policy. Manual Visual State
/// placement always takes precedence. Values use logical document units.
/// </summary>
public sealed record ConnectorLabelPlacementIntent
{
    public ConnectorLabelPlacementIntent(double distanceFromSource, double perpendicularGap)
    {
        if (!double.IsFinite(distanceFromSource) || distanceFromSource < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceFromSource));
        }

        if (!double.IsFinite(perpendicularGap) || perpendicularGap < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(perpendicularGap));
        }

        DistanceFromSource = distanceFromSource;
        PerpendicularGap = perpendicularGap;
    }

    public double DistanceFromSource { get; }

    public double PerpendicularGap { get; }
}
