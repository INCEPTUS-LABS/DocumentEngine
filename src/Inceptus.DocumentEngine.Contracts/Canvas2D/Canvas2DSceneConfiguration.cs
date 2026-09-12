using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

public sealed class Canvas2DSceneConfiguration : IEquatable<Canvas2DSceneConfiguration>
{
    public Canvas2DSceneConfiguration(
        string configurationId,
        string version,
        IEnumerable<KeyValuePair<string, PropertyValue>>? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ConfigurationId = configurationId;
        Version = version;
        Options = new PropertyMap(options);
    }

    public static Canvas2DSceneConfiguration Default { get; } = new("canvas2d.default", "1");

    public string ConfigurationId { get; }
    public string Version { get; }
    public PropertyMap Options { get; }

    public bool Equals(Canvas2DSceneConfiguration? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        StringComparer.Ordinal.Equals(ConfigurationId, other.ConfigurationId) &&
        StringComparer.Ordinal.Equals(Version, other.Version) &&
        Options.Equals(other.Options);

    public override bool Equals(object? obj) => Equals(obj as Canvas2DSceneConfiguration);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.Ordinal.GetHashCode(ConfigurationId),
        StringComparer.Ordinal.GetHashCode(Version),
        Options);
}
