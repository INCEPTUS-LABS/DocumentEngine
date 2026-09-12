using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Contracts.Projection;

public sealed class ProjectionRuleResult : IEquatable<ProjectionRuleResult>
{
    private ProjectionRuleResult(
        bool succeeded,
        ProjectionRuleContribution? contribution,
        IEnumerable<Diagnostic>? diagnostics)
    {
        Diagnostics = ProjectionDiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
        var hasError = Diagnostics.Any(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        if (succeeded && (contribution is null || hasError))
        {
            throw new ArgumentException(
                "A successful Projection rule result requires a contribution and no error diagnostics.",
                nameof(contribution));
        }

        if (!succeeded && !hasError)
        {
            throw new ArgumentException(
                "A failed Projection rule result requires at least one error diagnostic.",
                nameof(diagnostics));
        }

        Succeeded = succeeded;
        Contribution = contribution;
    }

    public bool Succeeded { get; }

    public ProjectionRuleContribution? Contribution { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public static ProjectionRuleResult Success(
        ProjectionRuleContribution contribution,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        return new(true, contribution, diagnostics);
    }

    public static ProjectionRuleResult Failure(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(false, null, diagnostics);
    }

    public bool Equals(ProjectionRuleResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Succeeded == other.Succeeded &&
        Equals(Contribution, other.Contribution) &&
        ProjectionDiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as ProjectionRuleResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Succeeded);
        hash.Add(Contribution);
        ProjectionDiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }
}
