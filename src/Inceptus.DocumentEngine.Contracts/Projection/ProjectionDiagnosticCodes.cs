namespace Inceptus.DocumentEngine.Contracts.Projection;

public static class ProjectionDiagnosticCodes
{
    public const string UnsupportedSemanticType = "PROJECTION_UNSUPPORTED_SEMANTIC_TYPE";
    public const string InvalidInput = "PROJECTION_INVALID_INPUT";
    public const string RuleFailure = "PROJECTION_RULE_FAILURE";
    public const string InvalidRuleResult = "PROJECTION_INVALID_RULE_RESULT";
    public const string DuplicateProjectedObjectId = "PROJECTION_DUPLICATE_PROJECTED_OBJECT_ID";
    public const string InvalidSourceTraceability = "PROJECTION_INVALID_SOURCE_TRACEABILITY";
    public const string InvalidGraph = "PROJECTION_INVALID_GRAPH";
    public const string Cancelled = "PROJECTION_CANCELLED";
    public const string DuplicateRuleRegistration = "PROJECTION_DUPLICATE_RULE_REGISTRATION";
}
