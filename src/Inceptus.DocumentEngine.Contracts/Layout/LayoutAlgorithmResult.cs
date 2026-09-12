using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Contracts.Layout;

/// <summary>
/// Immutable computation outcome returned by a Layout Algorithm to the Layout Engine.
/// </summary>
public sealed class LayoutAlgorithmResult : IEquatable<LayoutAlgorithmResult>
{
    private LayoutAlgorithmResult(
        bool succeeded,
        LayoutComputation? computation,
        IEnumerable<Diagnostic>? diagnostics)
    {
        Diagnostics = LayoutDiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
        var hasError = Diagnostics.Any(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        if (succeeded && (computation is null || hasError))
        {
            throw new ArgumentException(
                "A successful Layout Algorithm result requires computation data and no error diagnostics.",
                nameof(computation));
        }

        if (!succeeded && !hasError)
        {
            throw new ArgumentException(
                "A failed Layout Algorithm result requires at least one error diagnostic.",
                nameof(diagnostics));
        }

        Succeeded = succeeded;
        Computation = computation;
    }

    public bool Succeeded { get; }

    public LayoutComputation? Computation { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public static LayoutAlgorithmResult Success(
        LayoutComputation computation,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(computation);
        return new(true, computation, diagnostics);
    }

    public static LayoutAlgorithmResult Failure(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(false, null, diagnostics);
    }

    public bool Equals(LayoutAlgorithmResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Succeeded == other.Succeeded &&
        Equals(Computation, other.Computation) &&
        LayoutDiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as LayoutAlgorithmResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Succeeded);
        hash.Add(Computation);
        LayoutDiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }
}
