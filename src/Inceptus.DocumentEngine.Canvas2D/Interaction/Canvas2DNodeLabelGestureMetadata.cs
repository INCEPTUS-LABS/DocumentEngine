using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

/// <summary>
/// Runtime-only metadata for an editable node-owned label subtarget.
/// </summary>
internal static class Canvas2DNodeLabelGestureMetadata
{
    internal const string Kind = "inceptus.canvas2d:node-label-visual-override";
    internal const string ResizeZoneKind = "inceptus.canvas2d:node-label-resize-zone";
    internal const string InteractionCapable =
        "inceptus.canvas2d:node-label-interaction-capable";
    internal const string TargetNodeSceneObjectId =
        "inceptus.canvas2d:node-label-target-node-scene-object-id";
    internal const string TargetLabelSceneObjectId =
        "inceptus.canvas2d:node-label-target-label-scene-object-id";
    internal const string TargetVisualStateId =
        "inceptus.canvas2d:node-label-target-visual-state-id";
    internal const string TargetLabelProjectedObjectId =
        "inceptus.canvas2d:node-label-target-projected-label-id";
    internal const string Operation = "inceptus.canvas2d:node-label-operation";
    internal const string MoveOperation = "move";
    internal const string ResizeOperation = "resize";
    internal const string ResizeDirection =
        "inceptus.canvas2d:node-label-resize-direction";
    internal const int EdgeZoneZIndexBase = 5020;
    internal const int CornerZoneZIndexBase = 5030;

    internal static PropertyValue InteractionCapableValue { get; } =
        PropertyValue.FromBoolean(true);

    internal static int ResizeZoneZIndex(Canvas2DResizeDirection direction) => direction switch
    {
        Canvas2DResizeDirection.North => EdgeZoneZIndexBase,
        Canvas2DResizeDirection.East => EdgeZoneZIndexBase + 1,
        Canvas2DResizeDirection.South => EdgeZoneZIndexBase + 2,
        Canvas2DResizeDirection.West => EdgeZoneZIndexBase + 3,
        Canvas2DResizeDirection.NorthWest => CornerZoneZIndexBase,
        Canvas2DResizeDirection.NorthEast => CornerZoneZIndexBase + 1,
        Canvas2DResizeDirection.SouthWest => CornerZoneZIndexBase + 2,
        Canvas2DResizeDirection.SouthEast => CornerZoneZIndexBase + 3,
        _ => throw new ArgumentOutOfRangeException(
            nameof(direction),
            direction,
            "The node-label resize direction must be defined."),
    };
}

internal enum Canvas2DNodeLabelGestureOperation
{
    Move,
    Resize,
}
