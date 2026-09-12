namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Pure notation-neutral capability that adapts one canonical connector to final single-scope
/// spatial presentation geometry. Implementations must not mutate model, View, or History state.
/// </summary>
public interface ICanvas2DConnectorPresentationRouter
{
    Canvas2DConnectorPresentationRoute Route(
        Canvas2DConnectorPresentationRoutingRequest request);
}
