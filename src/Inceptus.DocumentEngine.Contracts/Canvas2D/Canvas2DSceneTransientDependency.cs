namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>Dependency on one explicitly identified transient editor-state dimension.</summary>
public enum Canvas2DSceneTransientDependency
{
    /// <summary>No proof is declared; a change in this dimension requires complete composition.</summary>
    Unknown,

    /// <summary>
    /// Contribution items, overrides, diagnostics, metadata and spatial presentation are unchanged
    /// when only the declared dimension changes. All other inputs remain identical.
    /// </summary>
    Invariant,

    /// <summary>The contribution depends on this dimension and requires complete composition.</summary>
    Dependent,
}
