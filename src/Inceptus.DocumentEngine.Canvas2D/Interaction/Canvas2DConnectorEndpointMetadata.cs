using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

internal static class Canvas2DConnectorEndpointMetadata
{
    internal const string HandleKind = "inceptus.canvas2d:connector-endpoint-handle";
    internal const string HandleRole = "inceptus.canvas2d:connector-endpoint-role";
    internal const string StartEndpointRole = "StartEndpoint";
    internal const string EndEndpointRole = "EndEndpoint";
    internal const string TargetSceneObjectId =
        "inceptus.canvas2d:connector-endpoint-target-scene-object-id";
    internal const string TargetVisualStateId =
        "inceptus.canvas2d:connector-endpoint-target-visual-state-id";
    internal const double HandleExtent = Canvas2DRouteGestureMetadata.HandleExtent;
    internal const int StartEndpointZIndex = 5200;
    internal const int EndEndpointZIndex = 5201;

    internal static PropertyValue RoleValue(bool isStart) => PropertyValue.FromText(
        isStart ? StartEndpointRole : EndEndpointRole);
}
