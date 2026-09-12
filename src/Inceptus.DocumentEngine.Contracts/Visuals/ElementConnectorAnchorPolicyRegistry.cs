using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

public sealed class ElementConnectorAnchorPolicyRegistration
{
    public ElementConnectorAnchorPolicyRegistration(
        SemanticTypeId elementTypeId,
        ElementConnectorAnchorPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(elementTypeId);
        ArgumentNullException.ThrowIfNull(policy);
        ElementTypeId = elementTypeId;
        Policy = policy;
    }

    public SemanticTypeId ElementTypeId { get; }

    public ElementConnectorAnchorPolicy Policy { get; }
}

public interface IElementConnectorAnchorPolicyProvider
{
    ElementConnectorAnchorPolicy Resolve(SemanticTypeId elementTypeId);
}

/// <summary>
/// Provides immutable, deterministic element connector-anchor capability lookup.
/// </summary>
public sealed class ElementConnectorAnchorPolicyRegistry :
    IElementConnectorAnchorPolicyProvider
{
    private readonly ElementConnectorAnchorPolicy _fallbackPolicy;
    private readonly ImmutableArray<ElementConnectorAnchorPolicyRegistration> _registrations;

    public ElementConnectorAnchorPolicyRegistry(
        IEnumerable<ElementConnectorAnchorPolicyRegistration>? registrations = null,
        ElementConnectorAnchorPolicy? fallbackPolicy = null)
    {
        var copy = registrations?.ToArray() ?? [];
        if (Array.Exists(copy, static registration => registration is null))
        {
            throw new ArgumentException(
                "Element connector-anchor policy registrations cannot contain null values.",
                nameof(registrations));
        }

        Array.Sort(
            copy,
            static (left, right) => StringComparer.Ordinal.Compare(
                left.ElementTypeId.Value,
                right.ElementTypeId.Value));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].ElementTypeId == copy[index].ElementTypeId)
            {
                throw new ArgumentException(
                    $"Element type '{copy[index].ElementTypeId}' has more than one connector-anchor policy registration.",
                    nameof(registrations));
            }
        }

        _registrations = [.. copy];
        _fallbackPolicy = fallbackPolicy ?? ElementConnectorAnchorPolicy.DynamicUnlimitedAllEdges;
    }

    public static ElementConnectorAnchorPolicyRegistry Default { get; } = new();

    public ElementConnectorAnchorPolicy Resolve(SemanticTypeId elementTypeId)
    {
        ArgumentNullException.ThrowIfNull(elementTypeId);
        var lower = 0;
        var upper = _registrations.Length - 1;
        while (lower <= upper)
        {
            var middle = lower + ((upper - lower) / 2);
            var registration = _registrations[middle];
            var comparison = StringComparer.Ordinal.Compare(
                registration.ElementTypeId.Value,
                elementTypeId.Value);
            if (comparison == 0)
            {
                return registration.Policy;
            }

            if (comparison < 0)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle - 1;
            }
        }

        return _fallbackPolicy;
    }
}
