using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Immutable configuration that participates in Projection determinism.
/// </summary>
public sealed class ProjectionContext : IEquatable<ProjectionContext>
{
    public ProjectionContext(
        IEnumerable<KeyValuePair<string, PropertyValue>>? configuration = null) =>
        Configuration = new PropertyMap(configuration);

    public static ProjectionContext Empty { get; } = new();

    public PropertyMap Configuration { get; }

    public bool Equals(ProjectionContext? other) =>
        ReferenceEquals(this, other) ||
        other is not null && Configuration.Equals(other.Configuration);

    public override bool Equals(object? obj) => Equals(obj as ProjectionContext);

    public override int GetHashCode() => Configuration.GetHashCode();
}
