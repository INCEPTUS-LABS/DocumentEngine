using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

/// <summary>
/// Describes one connector label's persistent route-relative placement in logical
/// document coordinates.
/// </summary>
public sealed class ConnectorLabelPlacement : IEquatable<ConnectorLabelPlacement>
{
    public const string PathPositionPropertyKey =
        "inceptus:connector-label:path-position";

    public const string OffsetXPropertyKey =
        "inceptus:connector-label:offset-x";

    public const string OffsetYPropertyKey =
        "inceptus:connector-label:offset-y";

    private const double DefaultOffsetY = -12d;

    public ConnectorLabelPlacement(double pathPosition, VectorD offset)
    {
        if (!double.IsFinite(pathPosition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pathPosition),
                pathPosition,
                "The path position must be finite.");
        }

        PathPosition = Math.Clamp(pathPosition, 0d, 1d);
        Offset = offset;
    }

    public static ConnectorLabelPlacement Default { get; } =
        new(0.5d, new VectorD(0d, DefaultOffsetY));

    public double PathPosition { get; }

    public VectorD Offset { get; }

    /// <summary>
    /// Resolves an explicit persistent placement or the centralized automatic default.
    /// </summary>
    public static ConnectorLabelPlacement Resolve(PropertyMap properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return TryRead(properties, out var placement) ? placement! : Default;
    }

    /// <summary>
    /// Reads a complete explicit placement from one Visual State property map.
    /// </summary>
    public static bool TryRead(
        PropertyMap properties,
        out ConnectorLabelPlacement? placement)
    {
        ArgumentNullException.ThrowIfNull(properties);
        placement = null;
        if (!properties.TryGetValue(PathPositionPropertyKey, out var pathPosition) ||
            pathPosition.Kind != PropertyValueKind.Number ||
            !properties.TryGetValue(OffsetXPropertyKey, out var offsetX) ||
            offsetX.Kind != PropertyValueKind.Number ||
            !properties.TryGetValue(OffsetYPropertyKey, out var offsetY) ||
            offsetY.Kind != PropertyValueKind.Number)
        {
            return false;
        }

        placement = new ConnectorLabelPlacement(
            pathPosition.NumberValue,
            new VectorD(offsetX.NumberValue, offsetY.NumberValue));
        return true;
    }

    /// <summary>
    /// Replaces the three placement properties while preserving unrelated visual data.
    /// A null placement removes explicit placement and restores automatic defaults.
    /// </summary>
    public static PropertyMap UpdateProperties(
        PropertyMap properties,
        ConnectorLabelPlacement? placement)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var retained = properties.Where(entry =>
            !StringComparer.Ordinal.Equals(entry.Key, PathPositionPropertyKey) &&
            !StringComparer.Ordinal.Equals(entry.Key, OffsetXPropertyKey) &&
            !StringComparer.Ordinal.Equals(entry.Key, OffsetYPropertyKey));
        return placement is null
            ? new PropertyMap(retained)
            : new PropertyMap(retained.Concat(
            [
                new KeyValuePair<string, PropertyValue>(
                    PathPositionPropertyKey,
                    PropertyValue.FromNumber(placement.PathPosition)),
                new KeyValuePair<string, PropertyValue>(
                    OffsetXPropertyKey,
                    PropertyValue.FromNumber(placement.Offset.X)),
                new KeyValuePair<string, PropertyValue>(
                    OffsetYPropertyKey,
                    PropertyValue.FromNumber(placement.Offset.Y)),
            ]));
    }

    public bool Equals(ConnectorLabelPlacement? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        PathPosition.Equals(other.PathPosition) &&
        Offset == other.Offset;

    public override bool Equals(object? obj) => Equals(obj as ConnectorLabelPlacement);

    public override int GetHashCode() => HashCode.Combine(PathPosition, Offset);

    public static bool operator ==(
        ConnectorLabelPlacement? left,
        ConnectorLabelPlacement? right) =>
        EqualityComparer<ConnectorLabelPlacement>.Default.Equals(left, right);

    public static bool operator !=(
        ConnectorLabelPlacement? left,
        ConnectorLabelPlacement? right) =>
        !(left == right);
}
