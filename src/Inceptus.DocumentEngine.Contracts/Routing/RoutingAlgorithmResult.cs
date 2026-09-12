using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>
/// Immutable computation outcome returned by a Routing Algorithm to the Routing Engine.
/// </summary>
public sealed class RoutingAlgorithmResult : IEquatable<RoutingAlgorithmResult>
{
    private RoutingAlgorithmResult(
        bool succeeded,
        RoutingComputation? computation,
        IEnumerable<Diagnostic>? diagnostics)
    {
        Diagnostics = RoutingDiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
        var hasError = Diagnostics.Any(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        if (succeeded && (computation is null || hasError))
        {
            throw new ArgumentException(
                "A successful Routing Algorithm result requires computation data and no error diagnostics.",
                nameof(computation));
        }

        if (!succeeded && !hasError)
        {
            throw new ArgumentException(
                "A failed Routing Algorithm result requires at least one error diagnostic.",
                nameof(diagnostics));
        }

        Succeeded = succeeded;
        Computation = computation;
    }

    public bool Succeeded { get; }

    public RoutingComputation? Computation { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public static RoutingAlgorithmResult Success(
        RoutingComputation computation,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(computation);
        return new(true, computation, diagnostics);
    }

    public static RoutingAlgorithmResult Failure(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(false, null, diagnostics);
    }

    public bool Equals(RoutingAlgorithmResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Succeeded == other.Succeeded &&
        Equals(Computation, other.Computation) &&
        RoutingDiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as RoutingAlgorithmResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Succeeded);
        hash.Add(Computation);
        RoutingDiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }
}
