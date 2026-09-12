namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Supplies deterministic immutable Canvas2D-oriented data to the framework-owned Scene Builder.
/// </summary>
public interface ICanvas2DSceneContributor
{
    Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context);
}
