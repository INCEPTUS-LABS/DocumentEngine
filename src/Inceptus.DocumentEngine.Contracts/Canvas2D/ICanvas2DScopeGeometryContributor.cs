namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Optional pure geometry preparation capability on an existing Scene contributor registration.
/// Both stages run before routing, using immutable scoped inputs and no editor or viewport state.
/// </summary>
public interface ICanvas2DScopeGeometryContributor
{
    Canvas2DScopeGeometryBaseResult PrepareBase(Canvas2DScopeGeometryBaseContext context);

    Canvas2DScopeGeometryPresentationResult PreparePresentation(
        Canvas2DScopeGeometryPresentationContext context);
}
