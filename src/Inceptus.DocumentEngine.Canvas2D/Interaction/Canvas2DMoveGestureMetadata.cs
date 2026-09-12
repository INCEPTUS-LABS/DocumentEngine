namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

using Inceptus.DocumentEngine.Contracts.Properties;

internal static class Canvas2DMoveGestureMetadata
{
    internal const string Kind = "inceptus.canvas2d:move";
    internal const string TargetSceneObjectId = "inceptus.canvas2d:move-target-scene-object-id";
    internal const string TargetVisualStateId = "inceptus.canvas2d:move-target-visual-state-id";
    internal const string TargetCount = "inceptus.canvas2d:move-target-count";
    internal const string TargetSceneObjectIdPrefix =
        "inceptus.canvas2d:move-target-scene-object-id:";
    internal const string TargetVisualStateIdPrefix =
        "inceptus.canvas2d:move-target-visual-state-id:";
    internal const string MoveCapable = "inceptus.canvas2d:move-capable";

    internal static string IndexedSceneObjectId(int index) =>
        $"{TargetSceneObjectIdPrefix}{index}";

    internal static string IndexedVisualStateId(int index) =>
        $"{TargetVisualStateIdPrefix}{index}";

    internal static PropertyMap MoveCapability { get; } = new(
    [
        new KeyValuePair<string, PropertyValue>(
            MoveCapable,
            PropertyValue.FromBoolean(true)),
    ]);
}
