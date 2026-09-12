using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Properties;

public sealed class PropertyValueTests
{
    [Fact]
    public void TypedValuesRetainTheirKindAndValue()
    {
        var text = PropertyValue.FromText("test:value");
        var boolean = PropertyValue.FromBoolean(true);
        var integer = PropertyValue.FromInteger(42);
        var number = PropertyValue.FromNumber(12.5d);

        Assert.Equal(PropertyValueKind.Text, text.Kind);
        Assert.Equal("test:value", text.TextValue);
        Assert.Equal(PropertyValueKind.Boolean, boolean.Kind);
        Assert.True(boolean.BooleanValue);
        Assert.Equal(PropertyValueKind.Integer, integer.Kind);
        Assert.Equal(42L, integer.IntegerValue);
        Assert.Equal(PropertyValueKind.Number, number.Kind);
        Assert.Equal(12.5d, number.NumberValue);
    }

    [Fact]
    public void ReadingAValueAsTheWrongKindIsRejected()
    {
        var value = PropertyValue.FromText("test:value");

        Assert.Throws<InvalidOperationException>(() => value.BooleanValue);
        Assert.Throws<InvalidOperationException>(() => value.IntegerValue);
        Assert.Throws<InvalidOperationException>(() => value.NumberValue);
    }

    [Fact]
    public void NumberValuesMustBeFinite()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PropertyValue.FromNumber(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PropertyValue.FromNumber(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PropertyValue.FromNumber(double.NegativeInfinity));
    }

    [Fact]
    public void TextValuesRejectNullButPermitEmptyText()
    {
        Assert.Throws<ArgumentNullException>(() => PropertyValue.FromText(null!));
        Assert.Equal(string.Empty, PropertyValue.FromText(string.Empty).TextValue);
    }

    [Fact]
    public void ValuesUseStructuralKindSpecificEquality()
    {
        var first = PropertyValue.FromInteger(1);
        var same = PropertyValue.FromInteger(1);
        var different = PropertyValue.FromInteger(2);
        var differentKind = PropertyValue.FromNumber(1d);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, different);
        Assert.NotEqual(first, differentKind);
    }
}
