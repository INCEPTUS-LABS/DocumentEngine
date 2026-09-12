namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Defines the deterministic phase in which a Scene contributor participates.
/// </summary>
public enum Canvas2DSceneContributionStage
{
    /// <summary>
    /// Contributes notation-specific appearance to one scope-local canonical Process Scene.
    /// </summary>
    Canonical,

    /// <summary>
    /// Composes read-only canonical scope Scenes into the final transient presentation.
    /// </summary>
    Presentation,
}
