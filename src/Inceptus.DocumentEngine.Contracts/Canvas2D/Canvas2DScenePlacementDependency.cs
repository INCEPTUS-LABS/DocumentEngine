namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Conservative dependency declaration for activation, replacement and retirement of transient
/// Toolbox placement feedback. All other Editor State and pipeline inputs remain identical.
/// </summary>
public enum Canvas2DScenePlacementDependency
{
    /// <summary>No placement dependency proof is provided. Complete composition is required.</summary>
    Unknown,

    /// <summary>
    /// The entire contribution, including diagnostics, metadata, canonical overrides and spatial
    /// presentation, is unchanged by placement feedback geometry, eligibility and lifecycle changes.
    /// </summary>
    Invariant,

    /// <summary>The contribution depends on placement feedback. Complete composition is required.</summary>
    Dependent,

    /// <summary>
    /// The entire contribution is the bounded placement feedback family. It contains only
    /// non-interactive EditorState Overlay items, has no persistent semantic or visual ownership,
    /// and contributes no metadata, canonical overrides or spatial presentation plan. Its output
    /// depends only on placement feedback and otherwise fixed configuration and provenance.
    /// Its context contains only typed placement feedback in Editor State and no canonical
    /// presentation items. Other Editor State dimensions use their default values. Without
    /// placement feedback the contribution is empty, so retirement cannot retain a ghost.
    /// The Scene builder validates this restricted output before bounded reuse.
    /// </summary>
    BoundedFeedbackOnly,
}
