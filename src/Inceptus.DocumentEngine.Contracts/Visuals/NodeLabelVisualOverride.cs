using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

/// <summary>
/// Describes one node-owned label's persistent manual layout box in logical
/// document coordinates.
/// </summary>
public sealed class NodeLabelVisualOverride : IEquatable<NodeLabelVisualOverride>
{
    public const string OffsetXPropertyKey = "inceptus:node-label:offset-x";

    public const string OffsetYPropertyKey = "inceptus:node-label:offset-y";

    public const string WidthPropertyKey = "inceptus:node-label:width";

    public const string HeightPropertyKey = "inceptus:node-label:height";

    public const double MinimumWidth = 1d;

    public const double MinimumHeight = 1d;

    public NodeLabelVisualOverride(
        double offsetX,
        double offsetY,
        double width,
        double height)
    {
        OffsetX = RequireFinite(offsetX, nameof(offsetX));
        OffsetY = RequireFinite(offsetY, nameof(offsetY));
        Width = RequireMinimum(width, MinimumWidth, nameof(width));
        Height = RequireMinimum(height, MinimumHeight, nameof(height));
    }

    /// <summary>
    /// Gets the horizontal displacement from the owning node's bounds center to
    /// the label-box center.
    /// </summary>
    public double OffsetX { get; }

    /// <summary>
    /// Gets the vertical displacement from the owning node's bounds center to
    /// the label-box center.
    /// </summary>
    public double OffsetY { get; }

    /// <summary>
    /// Gets the logical label layout-box width.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the logical label layout-box height.
    /// </summary>
    public double Height { get; }

    /// <summary>
    /// Resolves the complete manual label box in logical Document coordinates.
    /// </summary>
    public RectD ResolveBounds(RectD ownerBounds)
    {
        var centerX = ownerBounds.Left + (ownerBounds.Width / 2d) + OffsetX;
        var centerY = ownerBounds.Top + (ownerBounds.Height / 2d) + OffsetY;
        return new RectD(
            centerX - (Width / 2d),
            centerY - (Height / 2d),
            Width,
            Height);
    }

    /// <summary>
    /// Creates the node-owned override that represents one Document-space label box.
    /// </summary>
    public static NodeLabelVisualOverride FromBounds(RectD ownerBounds, RectD labelBounds)
    {
        var ownerCenterX = ownerBounds.Left + (ownerBounds.Width / 2d);
        var ownerCenterY = ownerBounds.Top + (ownerBounds.Height / 2d);
        var labelCenterX = labelBounds.Left + (labelBounds.Width / 2d);
        var labelCenterY = labelBounds.Top + (labelBounds.Height / 2d);
        return new NodeLabelVisualOverride(
            labelCenterX - ownerCenterX,
            labelCenterY - ownerCenterY,
            labelBounds.Width,
            labelBounds.Height);
    }

    /// <summary>
    /// Reads one complete manual node-label override from a Visual State property map.
    /// Absence or malformed partial data returns <see langword="false"/>.
    /// </summary>
    public static bool TryRead(
        PropertyMap properties,
        out NodeLabelVisualOverride? visualOverride)
    {
        ArgumentNullException.ThrowIfNull(properties);
        visualOverride = null;
        if (!TryReadNumber(properties, OffsetXPropertyKey, out var offsetX) ||
            !TryReadNumber(properties, OffsetYPropertyKey, out var offsetY) ||
            !TryReadNumber(properties, WidthPropertyKey, out var width) ||
            !TryReadNumber(properties, HeightPropertyKey, out var height) ||
            width < MinimumWidth ||
            height < MinimumHeight)
        {
            return false;
        }

        visualOverride = new NodeLabelVisualOverride(
            offsetX,
            offsetY,
            width,
            height);
        return true;
    }

    /// <summary>
    /// Replaces the complete manual node-label override while preserving unrelated
    /// Visual State properties. A null value removes the override and restores
    /// notation-derived automatic placement.
    /// </summary>
    public static PropertyMap UpdateProperties(
        PropertyMap properties,
        NodeLabelVisualOverride? visualOverride)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var retained = properties.Where(entry =>
            !StringComparer.Ordinal.Equals(entry.Key, OffsetXPropertyKey) &&
            !StringComparer.Ordinal.Equals(entry.Key, OffsetYPropertyKey) &&
            !StringComparer.Ordinal.Equals(entry.Key, WidthPropertyKey) &&
            !StringComparer.Ordinal.Equals(entry.Key, HeightPropertyKey));
        return visualOverride is null
            ? new PropertyMap(retained)
            : new PropertyMap(retained.Concat(
            [
                new KeyValuePair<string, PropertyValue>(
                    OffsetXPropertyKey,
                    PropertyValue.FromNumber(visualOverride.OffsetX)),
                new KeyValuePair<string, PropertyValue>(
                    OffsetYPropertyKey,
                    PropertyValue.FromNumber(visualOverride.OffsetY)),
                new KeyValuePair<string, PropertyValue>(
                    WidthPropertyKey,
                    PropertyValue.FromNumber(visualOverride.Width)),
                new KeyValuePair<string, PropertyValue>(
                    HeightPropertyKey,
                    PropertyValue.FromNumber(visualOverride.Height)),
            ]));
    }

    public bool Equals(NodeLabelVisualOverride? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        OffsetX.Equals(other.OffsetX) &&
        OffsetY.Equals(other.OffsetY) &&
        Width.Equals(other.Width) &&
        Height.Equals(other.Height);

    public override bool Equals(object? obj) =>
        Equals(obj as NodeLabelVisualOverride);

    public override int GetHashCode() =>
        HashCode.Combine(OffsetX, OffsetY, Width, Height);

    public static bool operator ==(
        NodeLabelVisualOverride? left,
        NodeLabelVisualOverride? right) =>
        EqualityComparer<NodeLabelVisualOverride>.Default.Equals(left, right);

    public static bool operator !=(
        NodeLabelVisualOverride? left,
        NodeLabelVisualOverride? right) =>
        !(left == right);

    private static bool TryReadNumber(
        PropertyMap properties,
        string key,
        out double value)
    {
        if (properties.TryGetValue(key, out var property) &&
            property.Kind == PropertyValueKind.Number &&
            double.IsFinite(property.NumberValue))
        {
            value = property.NumberValue;
            return true;
        }

        value = default;
        return false;
    }

    private static double RequireFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The value must be finite.");
        }

        return value;
    }

    private static double RequireMinimum(
        double value,
        double minimum,
        string parameterName)
    {
        if (!double.IsFinite(value) || value < minimum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"The value must be finite and at least {minimum} logical document units.");
        }

        return value;
    }
}
