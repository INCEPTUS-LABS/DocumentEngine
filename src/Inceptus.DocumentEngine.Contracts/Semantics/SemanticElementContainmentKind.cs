namespace Inceptus.DocumentEngine.Contracts.Semantics;

/// <summary>
/// Distinguishes semantic-scope containment from Document-level semantic containment.
/// </summary>
public enum SemanticElementContainmentKind
{
    /// <summary>
    /// The element belongs to an explicit scope membership or, when absent, the
    /// canonical implicit root scope.
    /// </summary>
    Scope = 0,

    /// <summary>
    /// The element belongs to the Document itself and has no semantic-scope membership.
    /// </summary>
    Document = 1,
}
