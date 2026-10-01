namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>Conservative dependency declaration for ordinary single-node move presentation.</summary>
public enum Canvas2DSceneMoveGestureDependency
{
    /// <summary>No invariance proof is provided. Complete composition is required.</summary>
    Unknown,

    /// <summary>
    /// Contribution, overrides, diagnostics and spatial presentation are unchanged by activating,
    /// updating or retiring an ordinary single-node move gesture, including selecting that node
    /// on activation. Only ActiveGesture and the zero-or-one Visual State selection may differ;
    /// semantic selection, hover, feedback, tool, viewport, profiles and all pipeline inputs remain
    /// identical. This declaration does not imply invariance for other gestures or editor changes.
    /// </summary>
    Invariant,

    /// <summary>The contribution depends on this transition. Complete composition is required.</summary>
    Dependent,
}
