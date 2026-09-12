using System.Collections.Immutable;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

public sealed class Canvas2DSceneStyle : IEquatable<Canvas2DSceneStyle>
{
    public Canvas2DSceneStyle(
        string? fill = null,
        string? stroke = null,
        double strokeWidth = 1d,
        IEnumerable<double>? dashPattern = null,
        double opacity = 1d,
        string? fontFamily = null,
        double fontSize = 12d)
    {
        Fill = ValidateOptionalValue(fill, nameof(fill));
        Stroke = ValidateOptionalValue(stroke, nameof(stroke));
        StrokeWidth = RequireFiniteNonNegative(strokeWidth, nameof(strokeWidth));
        DashPattern = CopyDashPattern(dashPattern);
        if (!double.IsFinite(opacity) || opacity < 0d || opacity > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opacity), opacity, "Opacity must be finite and between zero and one.");
        }

        Opacity = opacity;
        FontFamily = ValidateOptionalValue(fontFamily, nameof(fontFamily));
        if (!double.IsFinite(fontSize) || fontSize <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fontSize), fontSize, "Font size must be finite and greater than zero.");
        }

        FontSize = fontSize;
    }

    public static Canvas2DSceneStyle Default { get; } = new();

    public string? Fill { get; }

    public string? Stroke { get; }

    public double StrokeWidth { get; }

    public ImmutableArray<double> DashPattern { get; }

    public double Opacity { get; }

    public string? FontFamily { get; }

    public double FontSize { get; }

    public bool Equals(Canvas2DSceneStyle? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        StringComparer.Ordinal.Equals(Fill, other.Fill) &&
        StringComparer.Ordinal.Equals(Stroke, other.Stroke) &&
        StrokeWidth.Equals(other.StrokeWidth) &&
        DashPattern.AsSpan().SequenceEqual(other.DashPattern.AsSpan()) &&
        Opacity.Equals(other.Opacity) &&
        StringComparer.Ordinal.Equals(FontFamily, other.FontFamily) &&
        FontSize.Equals(other.FontSize);

    public override bool Equals(object? obj) => Equals(obj as Canvas2DSceneStyle);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fill, StringComparer.Ordinal);
        hash.Add(Stroke, StringComparer.Ordinal);
        hash.Add(StrokeWidth);
        foreach (var dash in DashPattern)
        {
            hash.Add(dash);
        }

        hash.Add(Opacity);
        hash.Add(FontFamily, StringComparer.Ordinal);
        hash.Add(FontSize);
        return hash.ToHashCode();
    }

    private static ImmutableArray<double> CopyDashPattern(IEnumerable<double>? values)
    {
        if (values is null)
        {
            return [];
        }

        var copy = values.ToArray();
        for (var index = 0; index < copy.Length; index++)
        {
            copy[index] = RequireFiniteNonNegative(copy[index], nameof(values));
        }

        return [.. copy];
    }

    private static double RequireFiniteNonNegative(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, value, "The value must be finite and non-negative.");
        }

        return value;
    }

    private static string? ValidateOptionalValue(string? value, string parameterName)
    {
        if (value is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        }

        return value;
    }
}
