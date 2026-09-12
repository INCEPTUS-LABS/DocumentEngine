using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.Runtime.Projection;

/// <summary>
/// Provides an immutable, deterministic lookup of plugin-provided Projection rules.
/// </summary>
internal sealed class ProjectionRuleRegistry
{
    private readonly ImmutableArray<ProjectionRuleRegistration> _registrations;

    internal ProjectionRuleRegistry(IEnumerable<ProjectionRuleRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var copy = registrations.ToArray();
        if (Array.Exists(copy, static registration => registration is null))
        {
            throw new ArgumentException(
                "Projection rule registrations cannot contain null values.",
                nameof(registrations));
        }

        RejectDuplicateRuleIds(copy, nameof(registrations));
        Array.Sort(copy, CompareRegistrations);
        _registrations = [.. copy];
    }

    internal ImmutableArray<ProjectionRuleRegistration> Find(
        ProjectionSourceKind sourceKind,
        SemanticTypeId semanticTypeId)
    {
        ArgumentNullException.ThrowIfNull(semanticTypeId);

        var builder = ImmutableArray.CreateBuilder<ProjectionRuleRegistration>();

        foreach (var registration in _registrations)
        {
            var sourceKindComparison = registration.SourceKind.CompareTo(sourceKind);
            if (sourceKindComparison < 0)
            {
                continue;
            }

            if (sourceKindComparison > 0)
            {
                break;
            }

            var typeComparison = StringComparer.Ordinal.Compare(
                registration.SemanticTypeId.Value,
                semanticTypeId.Value);
            if (typeComparison < 0)
            {
                continue;
            }

            if (typeComparison > 0)
            {
                break;
            }

            builder.Add(registration);
        }

        return builder.ToImmutable();
    }

    private static int CompareRegistrations(
        ProjectionRuleRegistration left,
        ProjectionRuleRegistration right)
    {
        var comparison = left.SourceKind.CompareTo(right.SourceKind);
        comparison = comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(
                left.SemanticTypeId.Value,
                right.SemanticTypeId.Value);

        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(left.RuleId.Value, right.RuleId.Value);
    }

    private static void RejectDuplicateRuleIds(
        ProjectionRuleRegistration[] registrations,
        string parameterName)
    {
        var orderedByRuleId = registrations
            .OrderBy(static registration => registration.RuleId.Value, StringComparer.Ordinal)
            .ToArray();

        for (var index = 1; index < orderedByRuleId.Length; index++)
        {
            if (orderedByRuleId[index - 1].RuleId != orderedByRuleId[index].RuleId)
            {
                continue;
            }

            throw new ArgumentException(
                $"{ProjectionDiagnosticCodes.DuplicateRuleRegistration}: " +
                $"Projection rule ID '{orderedByRuleId[index].RuleId}' is registered more than once.",
                parameterName);
        }
    }
}
