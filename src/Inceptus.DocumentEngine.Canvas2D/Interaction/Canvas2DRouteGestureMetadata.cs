using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

internal static class Canvas2DRouteGestureMetadata
{
    internal const string Kind = "inceptus.canvas2d:route-bend";
    internal const string HandleKind = "inceptus.canvas2d:route-bend-handle";
    internal const string HandleRole = "inceptus.canvas2d:route-handle-role";
    internal const string BendRole = "bend";
    internal const string BendIndex = "inceptus.canvas2d:route-bend-index";
    internal const string ControlKey = "inceptus.canvas2d:route-bend-control";
    internal const string SnapXTarget = "inceptus.canvas2d:route-bend-snap-x";
    internal const string SnapYTarget = "inceptus.canvas2d:route-bend-snap-y";

    internal static bool IsCandidateProperty(string key) =>
        key is ControlKey or SnapXTarget or SnapYTarget;

    internal static Canvas2DRouteBendSnapState SnapState(PropertyMap properties, int bend)
    {
        return new(Target(SnapXTarget), Target(SnapYTarget));
        // Only the two immediate neighbours can ever be supplied to presentation.
        // The values live solely on the transient EditorGestureSnapshot.
        int? Target(string key) => properties.TryGetValue(key, out var value) &&
            value.Kind == PropertyValueKind.Integer &&
            (value.IntegerValue == bend - 1 || value.IntegerValue == bend + 1)
                ? (int)value.IntegerValue : null;
    }

    internal static bool IsControlPressed(PropertyMap properties) =>
        properties.TryGetValue(ControlKey, out var value) &&
        value.Kind == PropertyValueKind.Boolean && value.BooleanValue;
    internal const string TargetSceneObjectId =
        "inceptus.canvas2d:route-target-scene-object-id";
    internal const string TargetVisualStateId =
        "inceptus.canvas2d:route-target-visual-state-id";
    internal const string RouteEditable = "inceptus.canvas2d:route-editable";
    internal const double HandleExtent = 10d;
    internal const int HandleZIndex = 5100;

    internal static PropertyValue EditableValue { get; } = PropertyValue.FromBoolean(true);
}
