namespace Inceptus.DocumentEngine.Contracts.Layout;

/// <summary>
/// Stable diagnostic codes produced by Layout Algorithm and Layout Engine validation.
/// </summary>
public static class LayoutDiagnosticCodes
{
    public const string DuplicateAlgorithmRegistration = "LAYOUT_ALGORITHM_REGISTRATION_DUPLICATE";
    public const string MissingAlgorithm = "LAYOUT_ALGORITHM_NOT_FOUND";
    public const string InvalidInput = "LAYOUT_INPUT_INVALID";
    public const string AlgorithmFailure = "LAYOUT_ALGORITHM_FAILED";
    public const string InvalidAlgorithmResult = "LAYOUT_ALGORITHM_RESULT_INVALID";
    public const string InvalidGeometry = "LAYOUT_GEOMETRY_INVALID";
    public const string DuplicateGeometry = "LAYOUT_GEOMETRY_DUPLICATE";
    public const string MissingGeometry = "LAYOUT_GEOMETRY_MISSING";
    public const string UnexpectedGeometry = "LAYOUT_GEOMETRY_UNEXPECTED";
    public const string PinnedPlacementViolation = "LAYOUT_PINNED_PLACEMENT_VIOLATION";
    public const string Cancelled = "LAYOUT_CANCELLED";
}
