using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Visuals;

public enum ConnectorEndpointDecoration
{
    None,
    FilledArrow,
    /// <summary>A closed triangular outline with a white interior.</summary>
    OpenArrow,
    OpenCircle,
}

/// <summary>Declarative connector appearance shared by projection and transient previews.</summary>
public sealed class ConnectorVisualDescriptor : IEquatable<ConnectorVisualDescriptor>
{
    public ConnectorVisualDescriptor(IEnumerable<double>? dashPattern = null,
        ConnectorEndpointDecoration sourceDecoration = ConnectorEndpointDecoration.None,
        ConnectorEndpointDecoration targetDecoration = ConnectorEndpointDecoration.FilledArrow)
    {
        if (!Enum.IsDefined(sourceDecoration)) throw new ArgumentOutOfRangeException(nameof(sourceDecoration));
        if (!Enum.IsDefined(targetDecoration)) throw new ArgumentOutOfRangeException(nameof(targetDecoration));
        DashPattern = dashPattern?.ToImmutableArray() ?? [];
        if (DashPattern.Any(static value => !double.IsFinite(value) || value < 0d))
            throw new ArgumentOutOfRangeException(nameof(dashPattern), "Dash lengths must be finite and non-negative.");
        SourceDecoration = sourceDecoration;
        TargetDecoration = targetDecoration;
    }

    public static ConnectorVisualDescriptor Default { get; } = new();
    public ImmutableArray<double> DashPattern { get; }
    public ConnectorEndpointDecoration SourceDecoration { get; }
    public ConnectorEndpointDecoration TargetDecoration { get; }
    public bool Equals(ConnectorVisualDescriptor? other) => ReferenceEquals(this, other) || other is not null &&
        SourceDecoration == other.SourceDecoration && TargetDecoration == other.TargetDecoration &&
        DashPattern.AsSpan().SequenceEqual(other.DashPattern.AsSpan());
    public override bool Equals(object? obj) => Equals(obj as ConnectorVisualDescriptor);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SourceDecoration);
        hash.Add(TargetDecoration);
        foreach (var dash in DashPattern) hash.Add(dash);
        return hash.ToHashCode();
    }
}

/// <summary>Encodes derived presentation data only; these keys never enter semantic or visual persistence.</summary>
public static class ConnectorVisualMetadata
{
    private const string SourceKey = "inceptus:connector-source-decoration";
    private const string TargetKey = "inceptus:connector-target-decoration";
    private const string DashCountKey = "inceptus:connector-dash-count";
    private const string DashPrefix = "inceptus:connector-dash:";

    public static ImmutableArray<KeyValuePair<string, PropertyValue>> Encode(ConnectorVisualDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return [new(SourceKey, PropertyValue.FromInteger((int)descriptor.SourceDecoration)),
            new(TargetKey, PropertyValue.FromInteger((int)descriptor.TargetDecoration)),
            new(DashCountKey, PropertyValue.FromInteger(descriptor.DashPattern.Length)),
            .. descriptor.DashPattern.Select((dash, index) => KeyValuePair.Create(
                DashPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture), PropertyValue.FromNumber(dash)))];
    }

    public static ConnectorVisualDescriptor Resolve(PropertyMap properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (!properties.ContainsKey(SourceKey) && !properties.ContainsKey(TargetKey) && !properties.ContainsKey(DashCountKey))
            return ConnectorVisualDescriptor.Default;
        if (!properties.TryGetValue(SourceKey, out var source) || source.Kind != PropertyValueKind.Integer ||
            !properties.TryGetValue(TargetKey, out var target) || target.Kind != PropertyValueKind.Integer ||
            !properties.TryGetValue(DashCountKey, out var count) || count.Kind != PropertyValueKind.Integer ||
            count.IntegerValue < 0 || count.IntegerValue > properties.Count || source.IntegerValue is < 0 or > 3 || target.IntegerValue is < 0 or > 3 ||
            !Enum.IsDefined((ConnectorEndpointDecoration)source.IntegerValue) ||
            !Enum.IsDefined((ConnectorEndpointDecoration)target.IntegerValue))
            throw new ArgumentException("The connector visual descriptor is invalid.", nameof(properties));
        var pattern = new double[(int)count.IntegerValue];
        for (var index = 0; index < pattern.Length; index++)
        {
            if (!properties.TryGetValue(DashPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture), out var dash) ||
                dash.Kind != PropertyValueKind.Number)
                throw new ArgumentException("The connector dash pattern is incomplete.", nameof(properties));
            pattern[index] = dash.NumberValue;
        }
        return new ConnectorVisualDescriptor(pattern, (ConnectorEndpointDecoration)source.IntegerValue,
            (ConnectorEndpointDecoration)target.IntegerValue);
    }
}
