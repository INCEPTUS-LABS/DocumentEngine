using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

internal static class Canvas2DRouteGestureMetadata
{
    internal const string Kind = "inceptus.canvas2d:route-bend";
    internal const string HandleKind = "inceptus.canvas2d:route-bend-handle";
    internal const string HandleRole = "inceptus.canvas2d:route-handle-role";
    internal const string BendRole = "bend";
    internal const string BendIndex = "inceptus.canvas2d:route-bend-index";
    internal const string TargetSceneObjectId =
        "inceptus.canvas2d:route-target-scene-object-id";
    internal const string TargetVisualStateId =
        "inceptus.canvas2d:route-target-visual-state-id";
    internal const string RouteEditable = "inceptus.canvas2d:route-editable";
    internal const double HandleExtent = 10d;
    internal const int HandleZIndex = 5100;

    internal static PropertyValue EditableValue { get; } = PropertyValue.FromBoolean(true);
}
