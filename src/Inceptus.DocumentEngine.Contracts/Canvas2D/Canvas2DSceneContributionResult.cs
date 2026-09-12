using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Immutable outcome returned by a deterministic scene contributor.
/// </summary>
public sealed class Canvas2DSceneContributionResult :
    IEquatable<Canvas2DSceneContributionResult>
{
    private Canvas2DSceneContributionResult(
        bool succeeded,
        Canvas2DSceneContribution? contribution,
        IEnumerable<Diagnostic>? diagnostics)
    {
        Diagnostics = Canvas2DSceneDiagnosticCollection.CopyAndOrder(
            diagnostics,
            nameof(diagnostics));
        var hasError = Diagnostics.Any(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);

        if (succeeded && (contribution is null || hasError))
        {
            throw new ArgumentException(
                "A successful scene contribution requires immutable data and no error diagnostics.",
                nameof(contribution));
        }

        if (!succeeded && !hasError)
        {
            throw new ArgumentException(
                "A failed scene contribution requires at least one error diagnostic.",
                nameof(diagnostics));
        }

        Succeeded = succeeded;
        Contribution = contribution;
    }

    public bool Succeeded { get; }

    public Canvas2DSceneContribution? Contribution { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public static Canvas2DSceneContributionResult Success(
        Canvas2DSceneContribution contribution,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        return new(true, contribution, diagnostics);
    }

    public static Canvas2DSceneContributionResult Failure(
        IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(false, null, diagnostics);
    }

    public bool Equals(Canvas2DSceneContributionResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Succeeded == other.Succeeded &&
        Equals(Contribution, other.Contribution) &&
        Canvas2DSceneDiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) =>
        Equals(obj as Canvas2DSceneContributionResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Succeeded);
        hash.Add(Contribution);
        Canvas2DSceneDiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }
}
