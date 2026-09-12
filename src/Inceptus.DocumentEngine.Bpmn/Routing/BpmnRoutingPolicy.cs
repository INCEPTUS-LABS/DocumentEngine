namespace Inceptus.DocumentEngine.Bpmn.Routing;

/// <summary>
/// Named logical-unit policy values for the supported BPMN orthogonal router.
/// </summary>
internal static class BpmnRoutingPolicy
{
    internal const double PreferredObstacleClearance = 10d;

    internal const double PreferredEndpointLeadDistance = 10d;

    internal const double BendPenalty = 20d;

    internal const double GeometryTolerance = 1e-9;

    internal const double MinimumEndpointLeadDistance = GeometryTolerance;

    internal static ReadOnlySpan<double> ObstacleClearanceAttempts =>
        [PreferredObstacleClearance, 7.5d, 5d, 2.5d, 0d];

    internal static ReadOnlySpan<double> EndpointLeadDistanceAttempts =>
        [PreferredEndpointLeadDistance, 7.5d, 5d, 2.5d, MinimumEndpointLeadDistance];
}
