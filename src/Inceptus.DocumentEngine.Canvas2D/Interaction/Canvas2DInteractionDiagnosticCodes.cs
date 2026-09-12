namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

public static class Canvas2DInteractionDiagnosticCodes
{
    public const string UnavailableSession = "CANVAS2D_INTERACTION_UNAVAILABLE_SESSION";
    public const string StaleScene = "CANVAS2D_INTERACTION_STALE_SCENE";
    public const string InvalidGestureTarget = "CANVAS2D_INTERACTION_INVALID_GESTURE_TARGET";
    public const string GestureAlreadyActive = "CANVAS2D_INTERACTION_GESTURE_ALREADY_ACTIVE";
    public const string AmbiguousConnectionCreation =
        "CANVAS2D_INTERACTION_AMBIGUOUS_CONNECTION_CREATION";
    public const string ConnectionCreationFactoryFailed =
        "CANVAS2D_INTERACTION_CONNECTION_CREATION_FACTORY_FAILED";
    public const string AmbiguousEndpointReconnection =
        "CANVAS2D_INTERACTION_AMBIGUOUS_ENDPOINT_RECONNECTION";
    public const string EndpointReconnectionFactoryFailed =
        "CANVAS2D_INTERACTION_ENDPOINT_RECONNECTION_FACTORY_FAILED";
    public const string StaleGesture = "CANVAS2D_INTERACTION_STALE_GESTURE";
    public const string InvalidGestureGeometry = "CANVAS2D_INTERACTION_INVALID_GESTURE_GEOMETRY";
    public const string CommandRejected = "CANVAS2D_INTERACTION_COMMAND_REJECTED";
    public const string CoordinateConversionFailed = "CANVAS2D_INTERACTION_COORDINATE_CONVERSION_FAILED";
    public const string HitTestFailed = "CANVAS2D_INTERACTION_HIT_TEST_FAILED";
    public const string EditorStateUpdateFailed = "CANVAS2D_INTERACTION_EDITOR_STATE_UPDATE_FAILED";
    public const string Cancelled = "CANVAS2D_INTERACTION_CANCELLED";
    public const string Disposed = "CANVAS2D_INTERACTION_DISPOSED";
}
