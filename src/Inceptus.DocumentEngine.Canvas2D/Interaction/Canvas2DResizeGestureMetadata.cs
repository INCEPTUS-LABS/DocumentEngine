using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

internal static class Canvas2DResizeGestureMetadata
{
    internal const string Kind = "inceptus.canvas2d:resize";
    internal const string HandleKind = "inceptus.canvas2d:resize-handle";
    internal const string EdgeHitZoneKind = "inceptus.canvas2d:resize-edge-hit-zone";
    internal const string HandleRole = "inceptus.canvas2d:resize-handle-role";
    internal const string NorthWestRole = "northwest";
    internal const string NorthRole = "north";
    internal const string NorthEastRole = "northeast";
    internal const string EastRole = "east";
    internal const string SouthEastRole = "southeast";
    internal const string SouthRole = "south";
    internal const string SouthWestRole = "southwest";
    internal const string WestRole = "west";
    internal const string TargetSceneObjectId =
        "inceptus.canvas2d:resize-target-scene-object-id";
    internal const string TargetVisualStateId =
        "inceptus.canvas2d:resize-target-visual-state-id";
    internal const string ResizeCapable = "inceptus.canvas2d:resize-capable";
    internal const double HandleExtent = 10d;
    internal const double EdgeHitExtent = 8d;
    internal const double MinimumExtent = Canvas2DInteractionController.MinimumVisualExtent;

    internal static PropertyMap MoveAndResizeCapability { get; } = new(
    [
        new KeyValuePair<string, PropertyValue>(
            Canvas2DMoveGestureMetadata.MoveCapable,
            PropertyValue.FromBoolean(true)),
        new KeyValuePair<string, PropertyValue>(
            ResizeCapable,
            PropertyValue.FromBoolean(true)),
    ]);
}
