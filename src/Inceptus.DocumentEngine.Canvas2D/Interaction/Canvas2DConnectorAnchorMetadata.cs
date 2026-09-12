namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

/// <summary>
/// Runtime-only metadata for connector-anchor interaction points.
/// </summary>
internal static class Canvas2DConnectorAnchorMetadata
{
    internal const string HandleKind = "inceptus.canvas2d:connector-anchor-handle";
    internal const string AnchorId = "inceptus.canvas2d:connector-anchor-id";
    internal const string Side = "inceptus.canvas2d:connector-anchor-side";
    internal const string Role = "inceptus.canvas2d:connector-anchor-role";
    internal const string RoleCapability =
        "inceptus.canvas2d:connector-anchor-role-capability";
    internal const string AnchorKind = "inceptus.canvas2d:connector-anchor-kind";
    internal const string TargetSceneObjectId =
        "inceptus.canvas2d:connector-anchor-target-scene-object-id";
    internal const string TargetVisualStateId =
        "inceptus.canvas2d:connector-anchor-target-visual-state-id";
    internal const string DeleteCapable =
        "inceptus.canvas2d:connector-anchor-delete-capable";
    internal const string ConnectionTargetCandidate =
        "inceptus.canvas2d:connector-anchor-connection-target-candidate";
    internal const double HandleExtent = 10d;
    internal const int HandleZIndex = 5050;

}
