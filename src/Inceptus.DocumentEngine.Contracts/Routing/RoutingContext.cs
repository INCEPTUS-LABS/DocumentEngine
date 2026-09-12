using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>
/// Immutable configuration that participates in routing determinism.
/// </summary>
public sealed class RoutingContext : IEquatable<RoutingContext>
{
    public RoutingContext(
        IEnumerable<KeyValuePair<string, PropertyValue>>? options = null) =>
        Options = new PropertyMap(options);

    public static RoutingContext Empty { get; } = new();

    public PropertyMap Options { get; }

    public bool Equals(RoutingContext? other) =>
        ReferenceEquals(this, other) ||
        other is not null && Options.Equals(other.Options);

    public override bool Equals(object? obj) => Equals(obj as RoutingContext);

    public override int GetHashCode() => Options.GetHashCode();
}
