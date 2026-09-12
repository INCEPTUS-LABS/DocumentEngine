using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Runtime.Layout;

/// <summary>
/// Provides immutable deterministic lookup for registered Layout Algorithms.
/// </summary>
internal sealed class LayoutAlgorithmRegistry
{
    private readonly ImmutableArray<LayoutAlgorithmRegistration> _registrations;

    internal LayoutAlgorithmRegistry(IEnumerable<LayoutAlgorithmRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var copy = registrations.ToArray();
        if (Array.Exists(copy, static registration => registration is null))
        {
            throw new ArgumentException(
                "Layout Algorithm registrations cannot contain null values.",
                nameof(registrations));
        }

        Array.Sort(copy, static (left, right) =>
            StringComparer.Ordinal.Compare(left.AlgorithmId.Value, right.AlgorithmId.Value));
        RejectDuplicateAlgorithmIds(copy, nameof(registrations));
        _registrations = [.. copy];
    }

    internal bool TryGet(
        AlgorithmId algorithmId,
        out LayoutAlgorithmRegistration? registration)
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
        LayoutAlgorithmRegistration[] registrations,
        string parameterName)
    {
        for (var index = 1; index < registrations.Length; index++)
        {
            if (registrations[index - 1].AlgorithmId != registrations[index].AlgorithmId)
            {
                continue;
            }

            throw new ArgumentException(
                $"{LayoutDiagnosticCodes.DuplicateAlgorithmRegistration}: " +
                $"Layout Algorithm ID '{registrations[index].AlgorithmId}' is registered more than once.",
                parameterName);
        }
    }
}
