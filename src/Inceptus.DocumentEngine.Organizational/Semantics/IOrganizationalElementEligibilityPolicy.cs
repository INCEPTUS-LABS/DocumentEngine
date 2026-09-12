using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Organizational.Semantics;

/// <summary>
/// Host-supplied notation policy for semantic types that may be assigned to a Pool.
/// Structural scope containment and attached-element exclusions remain enforced by the
/// Organizational plugin.
/// </summary>
public interface IOrganizationalElementEligibilityPolicy
{
    bool IsEligible(SemanticElementSnapshot element);
}

/// <summary>
/// Adapts a notation semantic-type predicate to the Organizational policy boundary.
/// </summary>
public sealed class OrganizationalElementEligibilityPolicy :
    IOrganizationalElementEligibilityPolicy
{
    private readonly Func<SemanticTypeId, bool> _isEligibleSemanticType;

    public OrganizationalElementEligibilityPolicy(
        Func<SemanticTypeId, bool> isEligibleSemanticType)
    {
        ArgumentNullException.ThrowIfNull(isEligibleSemanticType);
        _isEligibleSemanticType = isEligibleSemanticType;
    }

    public bool IsEligible(SemanticElementSnapshot element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return _isEligibleSemanticType(element.TypeId);
    }
}
