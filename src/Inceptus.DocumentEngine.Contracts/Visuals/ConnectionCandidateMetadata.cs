using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

/// <summary>Transient, revision-bound candidate visibility captured by the managed interaction policy.</summary>
public static class ConnectionCandidateMetadata
{
    private const string CountKey = "inceptus:connection-candidate-count";
    private const string Prefix = "inceptus:connection-candidate:";
    private const string SourceCountKey = "inceptus:connection-source-count";
    private const string SourcePrefix = "inceptus:connection-source:";

    public static ImmutableArray<KeyValuePair<string, PropertyValue>> EncodeAllowedVisuals(IEnumerable<VisualStateId> ids)
        => Encode(ids, CountKey, Prefix);

    public static ImmutableArray<KeyValuePair<string, PropertyValue>> EncodeAllowedSources(IEnumerable<VisualStateId> ids)
        => Encode(ids, SourceCountKey, SourcePrefix);

    private static ImmutableArray<KeyValuePair<string, PropertyValue>> Encode(IEnumerable<VisualStateId> ids,
        string countKey, string prefix)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var sorted = ids.Distinct().OrderBy(static id => id.Value, StringComparer.Ordinal).ToArray();
        return [new(countKey, PropertyValue.FromInteger(sorted.Length)),
            .. sorted.Select((id, index) => KeyValuePair.Create(prefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PropertyValue.FromText(id.Value)))];
    }

    public static bool AllowsVisual(PropertyMap properties, VisualStateId visualId)
        => Allows(properties, visualId, CountKey, Prefix);

    public static bool HasCapturedCandidates(PropertyMap properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return properties.ContainsKey(CountKey);
    }

    public static bool AllowsSource(PropertyMap properties, VisualStateId visualId)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(visualId);
        return properties.ContainsKey(SourceCountKey) && Allows(properties, visualId, SourceCountKey, SourcePrefix);
    }

    private static bool Allows(PropertyMap properties, VisualStateId visualId, string countKey, string prefix)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(visualId);
        if (!properties.TryGetValue(countKey, out var count)) return true;
        if (count.Kind != PropertyValueKind.Integer || count.IntegerValue <= 0) return false;
        // Read the bounded stored set rather than trusting a metadata count for iteration.
        return properties.Any(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal) &&
            pair.Value.Kind == PropertyValueKind.Text && pair.Value.TextValue == visualId.Value);
    }

    public static IEnumerable<KeyValuePair<string, PropertyValue>> Capture(PropertyMap properties) =>
        properties.Where(pair => pair.Key == CountKey || pair.Key.StartsWith(Prefix, StringComparison.Ordinal));

    public static IEnumerable<KeyValuePair<string, PropertyValue>> WithoutSources(PropertyMap properties) =>
        properties.Where(pair => pair.Key != SourceCountKey && !pair.Key.StartsWith(SourcePrefix, StringComparison.Ordinal));
}
