namespace Inceptus.DocumentEngine.Contracts.Properties;

public sealed class PropertyValue : IEquatable<PropertyValue>
{
    private readonly string? _textValue;
    private readonly bool _booleanValue;
    private readonly long _integerValue;
    private readonly double _numberValue;

    private PropertyValue(
        PropertyValueKind kind,
        string? textValue = null,
        bool booleanValue = default,
        long integerValue = default,
        double numberValue = default)
    {
        Kind = kind;
        _textValue = textValue;
        _booleanValue = booleanValue;
        _integerValue = integerValue;
        _numberValue = numberValue;
    }

    public PropertyValueKind Kind { get; }

    public string TextValue => Kind == PropertyValueKind.Text
        ? _textValue!
        : throw CreateKindMismatch(PropertyValueKind.Text);

    public bool BooleanValue => Kind == PropertyValueKind.Boolean
        ? _booleanValue
        : throw CreateKindMismatch(PropertyValueKind.Boolean);

    public long IntegerValue => Kind == PropertyValueKind.Integer
        ? _integerValue
        : throw CreateKindMismatch(PropertyValueKind.Integer);

    public double NumberValue => Kind == PropertyValueKind.Number
        ? _numberValue
        : throw CreateKindMismatch(PropertyValueKind.Number);

    public static PropertyValue FromText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new PropertyValue(PropertyValueKind.Text, textValue: value);
    }

    public static PropertyValue FromBoolean(bool value) =>
        new(PropertyValueKind.Boolean, booleanValue: value);

    public static PropertyValue FromInteger(long value) =>
        new(PropertyValueKind.Integer, integerValue: value);

    public static PropertyValue FromNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be finite.");
        }

        return new PropertyValue(PropertyValueKind.Number, numberValue: value);
    }

    public bool Equals(PropertyValue? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            PropertyValueKind.Text => string.Equals(_textValue, other._textValue, StringComparison.Ordinal),
            PropertyValueKind.Boolean => _booleanValue == other._booleanValue,
            PropertyValueKind.Integer => _integerValue == other._integerValue,
            PropertyValueKind.Number => _numberValue.Equals(other._numberValue),
            _ => false,
        };
    }

    public override bool Equals(object? obj) => Equals(obj as PropertyValue);

    public override int GetHashCode() => Kind switch
    {
        PropertyValueKind.Text => HashCode.Combine(
            Kind,
            StringComparer.Ordinal.GetHashCode(_textValue!)),
        PropertyValueKind.Boolean => HashCode.Combine(Kind, _booleanValue),
        PropertyValueKind.Integer => HashCode.Combine(Kind, _integerValue),
        PropertyValueKind.Number => HashCode.Combine(Kind, _numberValue),
        _ => Kind.GetHashCode(),
    };

    private InvalidOperationException CreateKindMismatch(PropertyValueKind expectedKind) =>
        new($"A {Kind} property value cannot be read as {expectedKind}.");
}
