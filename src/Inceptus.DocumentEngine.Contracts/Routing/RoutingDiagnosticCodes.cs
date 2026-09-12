namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>
/// Stable diagnostic codes produced by Routing Algorithm and Routing Engine validation.
/// </summary>
public static class RoutingDiagnosticCodes
{
    public const string DuplicateAlgorithmRegistration = "ROUTING_ALGORITHM_REGISTRATION_DUPLICATE";
    public const string MissingAlgorithm = "ROUTING_ALGORITHM_NOT_FOUND";
    public const string InvalidInput = "ROUTING_INPUT_INVALID";
    public const string IncompatibleLayoutResult = "ROUTING_LAYOUT_INCOMPATIBLE";
    public const string AlgorithmFailure = "ROUTING_ALGORITHM_FAILED";
    public const string InvalidAlgorithmResult = "ROUTING_ALGORITHM_RESULT_INVALID";
    public const string MissingRoute = "ROUTING_ROUTE_MISSING";
    public const string UnexpectedRoute = "ROUTING_ROUTE_UNEXPECTED";
    public const string DuplicateRoute = "ROUTING_ROUTE_DUPLICATE";
    public const string ConflictingRouteOutcome = "ROUTING_ROUTE_OUTCOME_CONFLICT";
    public const string InvalidProjectedIdentity = "ROUTING_PROJECTED_IDENTITY_INVALID";
    public const string InvalidPortReference = "ROUTING_PORT_REFERENCE_INVALID";
    public const string InvalidAttachmentPoint = "ROUTING_ATTACHMENT_POINT_INVALID";
    public const string NonFiniteGeometry = "ROUTING_GEOMETRY_NON_FINITE";
    public const string InvalidPath = "ROUTING_PATH_INVALID";
    public const string RoutingConstraintViolation = "ROUTING_CONSTRAINT_VIOLATION";
    public const string Cancelled = "ROUTING_CANCELLED";
}
