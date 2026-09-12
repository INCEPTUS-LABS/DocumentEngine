namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public static class EditingSessionDiagnosticCodes
{
    public const string InvalidAttachment = "EDITING_SESSION_INVALID_ATTACHMENT";
    public const string PipelineFailed = "EDITING_SESSION_PIPELINE_FAILED";
    public const string PipelineCancelled = "EDITING_SESSION_PIPELINE_CANCELLED";
    public const string NodeGeometryPreservationUnavailable =
        "EDITING_SESSION_NODE_GEOMETRY_PRESERVATION_UNAVAILABLE";
    public const string SceneBuildFailed = "EDITING_SESSION_SCENE_BUILD_FAILED";
    public const string StalePipelineResult = "EDITING_SESSION_STALE_PIPELINE_RESULT";
    public const string InvalidSceneProvenance = "EDITING_SESSION_INVALID_SCENE_PROVENANCE";
    public const string IncompatibleSceneArtifacts = "EDITING_SESSION_INCOMPATIBLE_SCENE_ARTIFACTS";
    public const string InvalidOperation = "EDITING_SESSION_INVALID_OPERATION";
    public const string InvalidScope = "EDITING_SESSION_INVALID_SCOPE";
    public const string SelectionChanged = "EDITING_SESSION_SELECTION_CHANGED";
    public const string RuntimeFaulted = "EDITING_SESSION_RUNTIME_FAULTED";
    public const string Closed = "EDITING_SESSION_CLOSED";
    public const string NotificationFailure = "EDITING_SESSION_NOTIFICATION_FAILURE";
    public const string ResourceDisposalFailed = "EDITING_SESSION_RESOURCE_DISPOSAL_FAILED";
}
