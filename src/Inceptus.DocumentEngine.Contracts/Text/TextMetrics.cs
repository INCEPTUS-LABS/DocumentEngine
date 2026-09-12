using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Contracts.Text;

/// <summary>
/// Immutable normalized text geometry expressed in logical document-coordinate units.
/// </summary>
public sealed class TextMetrics : IEquatable<TextMetrics>
{
    public TextMetrics(
        double width,
        double ascent,
        double descent,
        double lineHeight,
        RectD boundingBox,
        string resolvedFontIdentity,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        Width = RequireFiniteNonNegative(width, nameof(width));
        Ascent = RequireFiniteNonNegative(ascent, nameof(ascent));
        Descent = RequireFiniteNonNegative(descent, nameof(descent));
        if (!double.IsFinite(lineHeight) || lineHeight <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lineHeight), lineHeight, "Line height must be finite and greater than zero.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedFontIdentity);
        var orderedDiagnostics = TextMetricsDiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
        if (orderedDiagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            throw new ArgumentException("Successful text metrics cannot contain error diagnostics.", nameof(diagnostics));
        }

        LineHeight = lineHeight;
        BoundingBox = boundingBox;
        ResolvedFontIdentity = resolvedFontIdentity;
        Diagnostics = orderedDiagnostics;
    }

    public double Width { get; }
    public double Ascent { get; }
    public double Descent { get; }
    public double LineHeight { get; }
    public RectD BoundingBox { get; }
    public double BoundingWidth => BoundingBox.Width;
    public double BoundingHeight => BoundingBox.Height;
    public string ResolvedFontIdentity { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool Equals(TextMetrics? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Width.Equals(other.Width) &&
        Ascent.Equals(other.Ascent) &&
        Descent.Equals(other.Descent) &&
        LineHeight.Equals(other.LineHeight) &&
        BoundingBox == other.BoundingBox &&
        StringComparer.Ordinal.Equals(ResolvedFontIdentity, other.ResolvedFontIdentity) &&
        TextMetricsDiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as TextMetrics);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Width);
        hash.Add(Ascent);
        hash.Add(Descent);
        hash.Add(LineHeight);
        hash.Add(BoundingBox);
        hash.Add(ResolvedFontIdentity, StringComparer.Ordinal);
        TextMetricsDiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }

    private static double RequireFiniteNonNegative(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, value, "Text metric values must be finite and non-negative.");
        }

        return value;
    }
}
