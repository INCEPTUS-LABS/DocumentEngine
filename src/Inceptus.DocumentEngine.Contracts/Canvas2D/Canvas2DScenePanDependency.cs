namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Declares whether a contributor's complete output, including any presentation routing,
/// can change when only viewport Pan and its visible-region translation change.
/// All other immutable inputs, zoom and surface observations must remain unchanged.
/// </summary>
public enum Canvas2DScenePanDependency
{
    /// <summary>No independence guarantee is available; rebuild the complete Scene.</summary>
    Unknown,

    /// <summary>The complete contribution is invariant under pure Pan.</summary>
    Invariant,

    /// <summary>The contribution depends on Pan; rebuild the complete Scene.</summary>
    Dependent,
}
