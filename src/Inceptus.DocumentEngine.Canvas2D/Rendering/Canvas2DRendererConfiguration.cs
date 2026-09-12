using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.Canvas2D.Rendering;

/// <summary>
/// Immutable browser-resource configuration for one Canvas2DRenderer.
/// </summary>
public sealed class Canvas2DRendererConfiguration : IEquatable<Canvas2DRendererConfiguration>
{
    public Canvas2DRendererConfiguration(
        IEnumerable<KeyValuePair<string, string>>? imageResources = null,
        IEnumerable<Canvas2DFontResource>? fontResources = null,
        string? defaultFontFamily = null,
        string textMeasurementConfigurationId = "canvas2d.browser",
        string textMeasurementConfigurationVersion = "1")
    {
        if (defaultFontFamily is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(defaultFontFamily);
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(textMeasurementConfigurationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(textMeasurementConfigurationVersion);
        var builder = ImmutableSortedDictionary.CreateBuilder<string, string>(
            StringComparer.Ordinal);
        if (imageResources is not null)
        {
            foreach (var entry in imageResources)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key, nameof(imageResources));
                ArgumentException.ThrowIfNullOrWhiteSpace(entry.Value, nameof(imageResources));
                builder.Add(entry.Key, entry.Value);
            }
        }

        ImageResources = builder.ToImmutable();
        FontResources = CopyAndOrderFonts(fontResources);
        if (defaultFontFamily is not null &&
            !FontResources.Any(font =>
                StringComparer.Ordinal.Equals(font.FontFamily, defaultFontFamily) &&
                font.FontWeight == 400 &&
                font.FontStyle == TextFontStyle.Normal))
        {
            throw new ArgumentException(
                "The default font family must identify an explicitly configured normal-weight 400 font resource.",
                nameof(defaultFontFamily));
        }

        DefaultFontFamily = defaultFontFamily;
        TextMeasurementConfigurationId = textMeasurementConfigurationId;
        TextMeasurementConfigurationVersion = textMeasurementConfigurationVersion;
    }

    public static Canvas2DRendererConfiguration Default { get; } = new();

    /// <summary>
    /// Gets the canonical immutable map from scene image references to browser-loadable URIs.
    /// </summary>
    public ImmutableSortedDictionary<string, string> ImageResources { get; }

    public ImmutableArray<Canvas2DFontResource> FontResources { get; }

    public string? DefaultFontFamily { get; }

    public string TextMeasurementConfigurationId { get; }

    public string TextMeasurementConfigurationVersion { get; }

    public bool Equals(Canvas2DRendererConfiguration? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        ImageResources.SequenceEqual(other.ImageResources) &&
        FontResources.AsSpan().SequenceEqual(other.FontResources.AsSpan()) &&
        StringComparer.Ordinal.Equals(DefaultFontFamily, other.DefaultFontFamily) &&
        StringComparer.Ordinal.Equals(
            TextMeasurementConfigurationId,
            other.TextMeasurementConfigurationId) &&
        StringComparer.Ordinal.Equals(
            TextMeasurementConfigurationVersion,
            other.TextMeasurementConfigurationVersion);

    public override bool Equals(object? obj) => Equals(obj as Canvas2DRendererConfiguration);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var entry in ImageResources)
        {
            hash.Add(entry.Key, StringComparer.Ordinal);
            hash.Add(entry.Value, StringComparer.Ordinal);
        }

        foreach (var font in FontResources)
        {
            hash.Add(font);
        }

        hash.Add(DefaultFontFamily, StringComparer.Ordinal);
        hash.Add(TextMeasurementConfigurationId, StringComparer.Ordinal);
        hash.Add(TextMeasurementConfigurationVersion, StringComparer.Ordinal);

        return hash.ToHashCode();
    }

    private static ImmutableArray<Canvas2DFontResource> CopyAndOrderFonts(
        IEnumerable<Canvas2DFontResource>? fontResources)
    {
        if (fontResources is null)
        {
            return [];
        }

        var copy = fontResources.ToArray();
        if (Array.Exists(copy, static font => font is null))
        {
            throw new ArgumentException("Font resources cannot contain null values.", nameof(fontResources));
        }

        Array.Sort(copy, static (left, right) =>
        {
            var comparison = StringComparer.Ordinal.Compare(left.FontIdentity, right.FontIdentity);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.Ordinal.Compare(left.FontVersion, right.FontVersion);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.FontWeight.CompareTo(right.FontWeight);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.FontStyle.CompareTo(right.FontStyle);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.Ordinal.Compare(left.FontFamily, right.FontFamily);
            return comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(left.SourceUri, right.SourceUri);
        });
        for (var index = 1; index < copy.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(copy[index - 1].FontIdentity, copy[index].FontIdentity) &&
                StringComparer.Ordinal.Equals(copy[index - 1].FontVersion, copy[index].FontVersion) &&
                copy[index - 1].FontWeight == copy[index].FontWeight &&
                copy[index - 1].FontStyle == copy[index].FontStyle)
            {
                throw new ArgumentException(
                    $"Font '{copy[index].FontIdentity}' version '{copy[index].FontVersion}' weight " +
                    $"'{copy[index].FontWeight}' style '{copy[index].FontStyle}' is duplicated.",
                    nameof(fontResources));
            }
        }

        var duplicateFace = copy
            .GroupBy(
                static font => (font.FontFamily, font.FontWeight, font.FontStyle),
                FontFaceKeyComparer.Instance)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateFace is not null)
        {
            throw new ArgumentException(
                $"Font family '{duplicateFace.Key.FontFamily}' weight '{duplicateFace.Key.FontWeight}' " +
                $"style '{duplicateFace.Key.FontStyle}' is configured more than once.",
                nameof(fontResources));
        }

        return [.. copy];
    }

    private sealed class FontFaceKeyComparer :
        IEqualityComparer<(string FontFamily, int FontWeight, TextFontStyle FontStyle)>
    {
        internal static FontFaceKeyComparer Instance { get; } = new();

        public bool Equals(
            (string FontFamily, int FontWeight, TextFontStyle FontStyle) left,
            (string FontFamily, int FontWeight, TextFontStyle FontStyle) right) =>
            StringComparer.Ordinal.Equals(left.FontFamily, right.FontFamily) &&
            left.FontWeight == right.FontWeight &&
            left.FontStyle == right.FontStyle;

        public int GetHashCode(
            (string FontFamily, int FontWeight, TextFontStyle FontStyle) value) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.FontFamily),
                value.FontWeight,
                value.FontStyle);
    }
}
