using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Contracts.Text;

/// <summary>
/// Immutable outcome of one text-measurement request. Failed and cancelled outcomes expose no metrics.
/// </summary>
public sealed class TextMeasurementResult : IEquatable<TextMeasurementResult>
{
    private TextMeasurementResult(
        TextMeasurementStatus status,
        TextMetrics? metrics,
        IEnumerable<Diagnostic>? diagnostics)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Text-measurement status must be defined.");
        }

        Diagnostics = TextMetricsDiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
        var hasError = Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (status == TextMeasurementStatus.Succeeded)
        {
            if (metrics is null ||
                hasError ||
                !TextMetricsDiagnosticCollection.SequenceEquals(Diagnostics, metrics.Diagnostics))
            {
                throw new ArgumentException(
                    "Successful text measurement requires metrics and matching non-error diagnostics.",
                    nameof(metrics));
            }
        }
        else if (metrics is not null)
        {
            throw new ArgumentException(
                "Failed or cancelled text measurement cannot expose partial metrics.",
                nameof(metrics));
        }

        if (status == TextMeasurementStatus.Failed && !hasError)
        {
            throw new ArgumentException(
                "Failed text measurement requires at least one error diagnostic.",
                nameof(diagnostics));
        }

        Status = status;
        Metrics = metrics;
    }

    public TextMeasurementStatus Status { get; }
    public TextMetrics? Metrics { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public bool IsSuccessful => Status == TextMeasurementStatus.Succeeded;

    public static TextMeasurementResult Success(TextMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        return new(TextMeasurementStatus.Succeeded, metrics, metrics.Diagnostics);
    }

    public static TextMeasurementResult Failure(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(TextMeasurementStatus.Failed, null, diagnostics);
    }

    public static TextMeasurementResult Cancelled(IEnumerable<Diagnostic>? diagnostics = null) =>
        new(
            TextMeasurementStatus.Cancelled,
            null,
            CreateCancellationDiagnostics(diagnostics));

    public bool Equals(TextMeasurementResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Status == other.Status &&
        Equals(Metrics, other.Metrics) &&
        TextMetricsDiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as TextMeasurementResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Status);
        hash.Add(Metrics);
        TextMetricsDiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }

    private static IEnumerable<Diagnostic> CreateCancellationDiagnostics(
        IEnumerable<Diagnostic>? diagnostics)
    {
        var copy = diagnostics?.ToArray() ?? [];
        if (copy.Any(static diagnostic =>
                diagnostic is not null &&
                StringComparer.Ordinal.Equals(diagnostic.Code, TextMetricsDiagnosticCodes.Cancelled)))
        {
            return copy;
        }

        return copy.Append(new Diagnostic(
            TextMetricsDiagnosticCodes.Cancelled,
            DiagnosticSeverity.Information,
            "Text measurement was cancelled."));
    }
}
