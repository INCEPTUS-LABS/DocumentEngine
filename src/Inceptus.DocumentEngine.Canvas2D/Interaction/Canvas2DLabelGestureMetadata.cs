using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

internal static class Canvas2DLabelGestureMetadata
{
    internal const string Kind = "inceptus.canvas2d:connector-label-move";
    internal const string LabelMoveCapable =
        "inceptus.canvas2d:connector-label-move-capable";
    internal const string TargetConnectorSceneObjectId =
        "inceptus.canvas2d:connector-label-target-scene-object-id";
    internal const string TargetVisualStateId =
        "inceptus.canvas2d:connector-label-target-visual-state-id";
    internal const string TargetLabelProjectedObjectId =
        "inceptus.canvas2d:connector-label-target-projected-label-id";

    internal static PropertyValue MoveCapableValue { get; } = PropertyValue.FromBoolean(true);
}
