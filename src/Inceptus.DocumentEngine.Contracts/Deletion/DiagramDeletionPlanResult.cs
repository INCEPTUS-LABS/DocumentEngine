using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Contracts.Deletion;

/// <summary>
/// Immutable success or failure returned by a notation-owned deletion factory.
/// </summary>
public sealed class DiagramDeletionPlanResult
{
    private DiagramDeletionPlanResult(
        DiagramDeletionPlan? plan,
        IEnumerable<Diagnostic>? diagnostics)
    {
        var copy = diagnostics?.ToArray() ?? [];
        if (Array.Exists(copy, static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "Deletion diagnostics cannot contain null values.",
                nameof(diagnostics));
        }

        Array.Sort(copy, static (left, right) =>
        {
            var comparison = StringComparer.Ordinal.Compare(left.Code, right.Code);
            comparison = comparison != 0
                ? comparison
                : left.Severity.CompareTo(right.Severity);
            comparison = comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(left.Message, right.Message);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(left.SourceIdentity, right.SourceIdentity);
        });
        Diagnostics = [.. copy];

        var hasError = Diagnostics.Any(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        if (plan is not null && hasError)
        {
            throw new ArgumentException(
                "A successful deletion plan cannot contain error diagnostics.",
                nameof(diagnostics));
        }

        if (plan is null && !hasError)
        {
            throw new ArgumentException(
                "A failed deletion plan requires at least one error diagnostic.",
                nameof(diagnostics));
        }

        Plan = plan;
    }

    public bool Succeeded => Plan is not null;

    public DiagramDeletionPlan? Plan { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public static DiagramDeletionPlanResult Success(
        DiagramDeletionPlan plan,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new(plan, diagnostics);
    }

    public static DiagramDeletionPlanResult Failure(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(null, diagnostics);
    }
}
