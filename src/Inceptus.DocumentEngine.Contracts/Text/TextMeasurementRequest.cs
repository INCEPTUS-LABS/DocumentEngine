namespace Inceptus.DocumentEngine.Contracts.Text;

/// <summary>
/// Immutable declaration of every input that may determine one text measurement.
/// Numeric values use logical document-coordinate units.
/// </summary>
public sealed class TextMeasurementRequest : IEquatable<TextMeasurementRequest>
{
    public TextMeasurementRequest(
        string text,
        string fontFamily,
        string fontIdentity,
        string fontVersion,
        double fontSize,
        double lineHeight,
        int fontWeight,
        TextFontStyle fontStyle,
        string locale,
        TextDirection direction,
        TextWritingMode writingMode,
        double scale,
        string configurationId,
        string configurationVersion)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationVersion);

        if (!double.IsFinite(fontSize) || fontSize <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fontSize), fontSize, "Font size must be finite and greater than zero.");
        }

        if (!double.IsFinite(lineHeight) || lineHeight <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lineHeight), lineHeight, "Line height must be finite and greater than zero.");
        }

        if (fontWeight is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fontWeight), fontWeight, "Font weight must be between 1 and 1000.");
        }

        if (!Enum.IsDefined(fontStyle))
        {
            throw new ArgumentOutOfRangeException(nameof(fontStyle), fontStyle, "Font style must be defined.");
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Text direction must be defined.");
        }

        if (!Enum.IsDefined(writingMode))
        {
            throw new ArgumentOutOfRangeException(nameof(writingMode), writingMode, "Writing mode must be defined.");
        }

        if (!double.IsFinite(scale) || scale <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale), scale, "Measurement scale must be finite and greater than zero.");
        }

        Text = text;
        FontFamily = fontFamily;
        FontIdentity = fontIdentity;
        FontVersion = fontVersion;
        FontSize = fontSize;
        LineHeight = lineHeight;
        FontWeight = fontWeight;
        FontStyle = fontStyle;
        Locale = locale;
        Direction = direction;
        WritingMode = writingMode;
        Scale = scale;
        ConfigurationId = configurationId;
        ConfigurationVersion = configurationVersion;
    }

    public string Text { get; }
    public string FontFamily { get; }
    public string FontIdentity { get; }
    public string FontVersion { get; }
    public double FontSize { get; }
    public double LineHeight { get; }
    public int FontWeight { get; }
    public TextFontStyle FontStyle { get; }
    public string Locale { get; }
    public TextDirection Direction { get; }
    public TextWritingMode WritingMode { get; }
    public double Scale { get; }
    public string ConfigurationId { get; }
    public string ConfigurationVersion { get; }

    public bool Equals(TextMeasurementRequest? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        StringComparer.Ordinal.Equals(Text, other.Text) &&
        StringComparer.Ordinal.Equals(FontFamily, other.FontFamily) &&
        StringComparer.Ordinal.Equals(FontIdentity, other.FontIdentity) &&
        StringComparer.Ordinal.Equals(FontVersion, other.FontVersion) &&
        FontSize.Equals(other.FontSize) &&
        LineHeight.Equals(other.LineHeight) &&
        FontWeight == other.FontWeight &&
        FontStyle == other.FontStyle &&
        StringComparer.Ordinal.Equals(Locale, other.Locale) &&
        Direction == other.Direction &&
        WritingMode == other.WritingMode &&
        Scale.Equals(other.Scale) &&
        StringComparer.Ordinal.Equals(ConfigurationId, other.ConfigurationId) &&
        StringComparer.Ordinal.Equals(ConfigurationVersion, other.ConfigurationVersion);

    public override bool Equals(object? obj) => Equals(obj as TextMeasurementRequest);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Text, StringComparer.Ordinal);
        hash.Add(FontFamily, StringComparer.Ordinal);
        hash.Add(FontIdentity, StringComparer.Ordinal);
        hash.Add(FontVersion, StringComparer.Ordinal);
        hash.Add(FontSize);
        hash.Add(LineHeight);
        hash.Add(FontWeight);
        hash.Add(FontStyle);
        hash.Add(Locale, StringComparer.Ordinal);
        hash.Add(Direction);
        hash.Add(WritingMode);
        hash.Add(Scale);
        hash.Add(ConfigurationId, StringComparer.Ordinal);
        hash.Add(ConfigurationVersion, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
