using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Runtime.Routing;

/// <summary>
/// Provides immutable deterministic lookup for registered Routing Algorithms.
/// </summary>
internal sealed class RoutingAlgorithmRegistry
{
    private readonly ImmutableArray<RoutingAlgorithmRegistration> _registrations;

    internal RoutingAlgorithmRegistry(IEnumerable<RoutingAlgorithmRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var copy = registrations.ToArray();
        if (Array.Exists(copy, static registration => registration is null))
        {
            throw new ArgumentException(
                "Routing Algorithm registrations cannot contain null values.",
                nameof(registrations));
        }

        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.AlgorithmId.Value, right.AlgorithmId.Value));
        RejectDuplicateAlgorithmIds(copy, nameof(registrations));
        _registrations = [.. copy];
    }

    internal bool TryGet(
        AlgorithmId algorithmId,
        out RoutingAlgorithmRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(algorithmId);

        foreach (var candidate in _registrations)
        {
            var comparison = StringComparer.Ordinal.Compare(
                candidate.AlgorithmId.Value,
                algorithmId.Value);
            if (comparison < 0)
            {
                continue;
            }

            if (comparison == 0)
            {
                registration = candidate;
                return true;
            }

            break;
        }

        registration = null;
        return false;
    }

    private static void RejectDuplicateAlgorithmIds(
        RoutingAlgorithmRegistration[] registrations,
        string parameterName)
    {
        for (var index = 1; index < registrations.Length; index++)
        {
            if (registrations[index - 1].AlgorithmId != registrations[index].AlgorithmId)
            {
                continue;
            }

            throw new ArgumentException(
                $"{RoutingDiagnosticCodes.DuplicateAlgorithmRegistration}: " +
                $"Routing Algorithm ID '{registrations[index].AlgorithmId}' is registered more than once.",
                parameterName);
        }
    }
}
