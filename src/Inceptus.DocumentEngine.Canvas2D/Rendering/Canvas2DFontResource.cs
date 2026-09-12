using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.Canvas2D.Rendering;

/// <summary>
/// Explicitly associates a stable font identity and version with one browser font family.
/// </summary>
public sealed class Canvas2DFontResource : IEquatable<Canvas2DFontResource>
{
    public Canvas2DFontResource(
        string fontIdentity,
        string fontVersion,
        string fontFamily,
        string sourceUri,
        int fontWeight = 400,
        TextFontStyle fontStyle = TextFontStyle.Normal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUri);
        if (fontWeight is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fontWeight), fontWeight, "Font weight must be between 1 and 1000.");
        }

        if (!Enum.IsDefined(fontStyle))
        {
            throw new ArgumentOutOfRangeException(nameof(fontStyle), fontStyle, "Font style must be defined.");
        }
        FontIdentity = fontIdentity;
        FontVersion = fontVersion;
        FontFamily = fontFamily;
        SourceUri = sourceUri;
        FontWeight = fontWeight;
        FontStyle = fontStyle;
    }

    public string FontIdentity { get; }

    public string FontVersion { get; }

    public string FontFamily { get; }

    public string SourceUri { get; }

    public int FontWeight { get; }

    public TextFontStyle FontStyle { get; }

    public bool Equals(Canvas2DFontResource? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        StringComparer.Ordinal.Equals(FontIdentity, other.FontIdentity) &&
        StringComparer.Ordinal.Equals(FontVersion, other.FontVersion) &&
        StringComparer.Ordinal.Equals(FontFamily, other.FontFamily) &&
        StringComparer.Ordinal.Equals(SourceUri, other.SourceUri) &&
        FontWeight == other.FontWeight &&
        FontStyle == other.FontStyle;

    public override bool Equals(object? obj) => Equals(obj as Canvas2DFontResource);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.Ordinal.GetHashCode(FontIdentity),
        StringComparer.Ordinal.GetHashCode(FontVersion),
        StringComparer.Ordinal.GetHashCode(FontFamily),
        StringComparer.Ordinal.GetHashCode(SourceUri),
        FontWeight,
        FontStyle);
}
