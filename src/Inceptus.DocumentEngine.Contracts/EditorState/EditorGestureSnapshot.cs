using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.EditorState;

/// <summary>
/// Describes one active transient gesture in logical coordinates.
/// </summary>
public sealed class EditorGestureSnapshot : IEquatable<EditorGestureSnapshot>
{
    public EditorGestureSnapshot(
        string id,
        string kind,
        PointD origin,
        PointD current,
        IEnumerable<KeyValuePair<string, PropertyValue>>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        Id = id;
        Kind = kind;
        Origin = origin;
        Current = current;
        Properties = new PropertyMap(properties);
    }

    public string Id { get; }

    public string Kind { get; }

    public PointD Origin { get; }

    public PointD Current { get; }

    public PropertyMap Properties { get; }

    public bool Equals(EditorGestureSnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        StringComparer.Ordinal.Equals(Id, other.Id) &&
        StringComparer.Ordinal.Equals(Kind, other.Kind) &&
        Origin == other.Origin &&
        Current == other.Current &&
        Properties.Equals(other.Properties);

    public override bool Equals(object? obj) => Equals(obj as EditorGestureSnapshot);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.Ordinal.GetHashCode(Id),
        StringComparer.Ordinal.GetHashCode(Kind),
        Origin,
        Current,
        Properties);
}
