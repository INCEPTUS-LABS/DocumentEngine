using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Inceptus.DocumentEngine.Contracts.Properties;

#pragma warning disable CA1710 // PropertyMap is the approved immutable value-contract name.
public sealed class PropertyMap :
    IReadOnlyDictionary<string, PropertyValue>,
    IEquatable<PropertyMap>
{
    private readonly ImmutableSortedDictionary<string, PropertyValue> _entries;

    public PropertyMap(IEnumerable<KeyValuePair<string, PropertyValue>>? entries = null)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, PropertyValue>(
            StringComparer.Ordinal);

        if (entries is not null)
        {
            foreach (var entry in entries)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key, nameof(entries));
                ArgumentNullException.ThrowIfNull(entry.Value, nameof(entries));
                builder.Add(entry.Key, entry.Value);
            }
        }

        _entries = builder.ToImmutable();
    }

    public static PropertyMap Empty { get; } = new();

    public int Count => _entries.Count;

    public IEnumerable<string> Keys => _entries.Keys;

    public IEnumerable<PropertyValue> Values => _entries.Values;

    public PropertyValue this[string key] => _entries[key];

    public bool ContainsKey(string key) => _entries.ContainsKey(key);

    public bool TryGetValue(
        string key,
        [MaybeNullWhen(false)] out PropertyValue value) =>
        _entries.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<string, PropertyValue>> GetEnumerator() =>
        _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(PropertyMap? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
            Count == other.Count &&
            _entries.SequenceEqual(other._entries);
    }

    public override bool Equals(object? obj) => Equals(obj as PropertyMap);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var entry in _entries)
        {
            hash.Add(entry.Key, StringComparer.Ordinal);
            hash.Add(entry.Value);
        }

        return hash.ToHashCode();
    }
}
#pragma warning restore CA1710
