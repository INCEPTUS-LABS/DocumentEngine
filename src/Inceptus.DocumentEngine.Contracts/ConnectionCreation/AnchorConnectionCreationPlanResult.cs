using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Contracts.ConnectionCreation;

/// <summary>
/// Immutable success or failure returned by a connection-creation factory.
/// </summary>
public sealed class AnchorConnectionCreationPlanResult
{
    private AnchorConnectionCreationPlanResult(
        AnchorConnectionCreationPlan? plan,
        IEnumerable<Diagnostic>? diagnostics)
    {
        Diagnostics = CopyAndOrderDiagnostics(diagnostics);
        var hasError = Diagnostics.Any(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);

        if (plan is not null && hasError)
        {
            throw new ArgumentException(
                "A successful connection-creation plan cannot contain error diagnostics.",
                nameof(diagnostics));
        }

        if (plan is null && !hasError)
        {
            throw new ArgumentException(
                "A failed connection-creation plan requires at least one error diagnostic.",
                nameof(diagnostics));
        }

        Plan = plan;
    }

    public bool Succeeded => Plan is not null;

    public AnchorConnectionCreationPlan? Plan { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public static AnchorConnectionCreationPlanResult Success(
        AnchorConnectionCreationPlan plan,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new(plan, diagnostics);
    }

    public static AnchorConnectionCreationPlanResult Failure(
        IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(null, diagnostics);
    }

    private static ImmutableArray<Diagnostic> CopyAndOrderDiagnostics(
        IEnumerable<Diagnostic>? diagnostics)
    {
        if (diagnostics is null)
        {
            return [];
        }

        var copy = diagnostics.ToArray();
        if (Array.Exists(copy, static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "Connection-creation diagnostics cannot contain null values.",
                nameof(diagnostics));
        }

        Array.Sort(copy, CompareDiagnostics);
        return [.. copy];
    }

    private static int CompareDiagnostics(Diagnostic? left, Diagnostic? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var comparison = StringComparer.Ordinal.Compare(left.Code, right.Code);
        comparison = comparison != 0
            ? comparison
            : left.Severity.CompareTo(right.Severity);
        comparison = comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(left.Message, right.Message);
        comparison = comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(left.SourceIdentity, right.SourceIdentity);
        if (comparison != 0)
        {
            return comparison;
        }

        using var leftContext = left.Context.GetEnumerator();
        using var rightContext = right.Context.GetEnumerator();
        while (leftContext.MoveNext())
        {
            if (!rightContext.MoveNext())
            {
                return 1;
            }

            comparison = StringComparer.Ordinal.Compare(
                leftContext.Current.Key,
                rightContext.Current.Key);
            comparison = comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(
                    leftContext.Current.Value,
                    rightContext.Current.Value);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return rightContext.MoveNext() ? -1 : 0;
    }
}
