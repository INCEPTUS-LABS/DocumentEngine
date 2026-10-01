using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>
/// Immutable configuration that participates in routing determinism.
/// </summary>
public sealed class RoutingContext : IEquatable<RoutingContext>
{
    public RoutingContext(
        IEnumerable<KeyValuePair<string, PropertyValue>>? options = null,
        PreparedRoutingInput? preparedInput = null)
    {
        Options = new PropertyMap(options);
        PreparedInput = preparedInput;
    }

    public static RoutingContext Empty { get; } = new();

    public PropertyMap Options { get; }

    public PreparedRoutingInput? PreparedInput { get; }

    public bool Equals(RoutingContext? other) =>
        ReferenceEquals(this, other) ||
        other is not null && Options.Equals(other.Options) &&
        Equals(PreparedInput, other.PreparedInput);

    public override bool Equals(object? obj) => Equals(obj as RoutingContext);

    public override int GetHashCode() => HashCode.Combine(Options, PreparedInput);
}
