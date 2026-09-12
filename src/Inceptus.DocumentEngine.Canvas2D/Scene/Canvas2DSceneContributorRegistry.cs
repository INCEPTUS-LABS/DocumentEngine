using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

internal sealed class Canvas2DSceneContributorRegistry
{
    private readonly ImmutableArray<Canvas2DSceneContributorRegistration> _registrations;

    internal Canvas2DSceneContributorRegistry(
        IEnumerable<Canvas2DSceneContributorRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var copy = registrations.ToArray();
        if (Array.Exists(copy, static registration => registration is null))
        {
            throw new ArgumentException(
                "Canvas2D scene contributor registrations cannot contain null values.",
                nameof(registrations));
        }

        Array.Sort(copy, Compare);
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].Descriptor.ContributorId ==
                copy[index].Descriptor.ContributorId)
            {
                throw new ArgumentException(
                    $"{Canvas2DSceneDiagnosticCodes.DuplicateContributorRegistration}: " +
                    $"Canvas2D scene contributor ID '{copy[index].Descriptor.ContributorId}' is already registered.",
                    nameof(registrations));
            }
        }

        _registrations = [.. copy];
        Descriptors = _registrations
            .Select(static registration => registration.Descriptor)
            .ToImmutableArray();
    }

    internal ImmutableArray<Canvas2DSceneContributorDescriptor> Descriptors { get; }

    internal ImmutableArray<Canvas2DSceneContributorRegistration> Registrations =>
        _registrations;

    private static int Compare(
        Canvas2DSceneContributorRegistration left,
        Canvas2DSceneContributorRegistration right)
    {
        var comparison = StringComparer.Ordinal.Compare(
            left.Descriptor.ContributorId.Value,
            right.Descriptor.ContributorId.Value);
        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(
                left.Descriptor.Version,
                right.Descriptor.Version);
    }
}
