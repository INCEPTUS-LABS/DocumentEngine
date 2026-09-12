namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Stable diagnostic codes produced during Canvas2D scene contribution and construction.
/// </summary>
public static class Canvas2DSceneDiagnosticCodes
{
    public const string InvalidInput = "SCENE_INVALID_INPUT";
    public const string IncompatibleProjectedGraph = "SCENE_INCOMPATIBLE_PROJECTED_GRAPH";
    public const string IncompatibleLayoutResult = "SCENE_INCOMPATIBLE_LAYOUT_RESULT";
    public const string IncompatibleRoutingResult = "SCENE_INCOMPATIBLE_ROUTING_RESULT";
    public const string IncompatibleVisualModel = "SCENE_INCOMPATIBLE_VISUAL_MODEL";
    public const string DuplicateContributorRegistration = "SCENE_DUPLICATE_CONTRIBUTOR_REGISTRATION";
    public const string ContributorFailure = "SCENE_CONTRIBUTOR_FAILURE";
    public const string InvalidContribution = "SCENE_INVALID_CONTRIBUTION";
    public const string InvalidCanonicalItemVisualOverride =
        "SCENE_INVALID_CANONICAL_ITEM_VISUAL_OVERRIDE";
    public const string ConflictingCanonicalItemVisualOverride =
        "SCENE_CONFLICTING_CANONICAL_ITEM_VISUAL_OVERRIDE";
    public const string UnsupportedSceneContribution = "SCENE_UNSUPPORTED_CONTRIBUTION";
    public const string DuplicateSceneObjectId = "SCENE_DUPLICATE_OBJECT_ID";
    public const string MissingProjectedObject = "SCENE_MISSING_PROJECTED_OBJECT";
    public const string InvalidSourceTrace = "SCENE_INVALID_SOURCE_TRACE";
    public const string InvalidGeometry = "SCENE_INVALID_GEOMETRY";
    public const string InvalidClipping = "SCENE_INVALID_CLIPPING";
    public const string StaleEditorStateReference = "SCENE_STALE_EDITOR_STATE_REFERENCE";
    public const string ConstructionFailure = "SCENE_CONSTRUCTION_FAILURE";
}
