using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Layout;

/// <summary>
/// Immutable configuration that participates in layout determinism.
/// </summary>
public sealed class LayoutContext : IEquatable<LayoutContext>
{
    public LayoutContext(
        IEnumerable<KeyValuePair<string, PropertyValue>>? options = null) =>
        Options = new PropertyMap(options);

    public static LayoutContext Empty { get; } = new();

    public PropertyMap Options { get; }

    public bool Equals(LayoutContext? other) =>
        ReferenceEquals(this, other) ||
        other is not null && Options.Equals(other.Options);

    public override bool Equals(object? obj) => Equals(obj as LayoutContext);

    public override int GetHashCode() => Options.GetHashCode();
}
