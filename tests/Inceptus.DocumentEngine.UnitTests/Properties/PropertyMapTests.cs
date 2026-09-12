using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Properties;

public sealed class PropertyMapTests
{
    [Fact]
    public void EntriesAreDefensivelyCopiedAndEnumeratedOrdinally()
    {
        var source = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:zeta", PropertyValue.FromText("last")),
            new("test:alpha", PropertyValue.FromInteger(2)),
            new("test:Alpha", PropertyValue.FromInteger(1)),
        };

        var map = new PropertyMap(source);
        source.Clear();
        source.Add(new("test:added", PropertyValue.FromBoolean(true)));

        Assert.Equal(["test:Alpha", "test:alpha", "test:zeta"], map.Keys);
        Assert.Equal(3, map.Count);
        Assert.False(map.ContainsKey("test:added"));
        Assert.Equal("last", map["test:zeta"].TextValue);
    }

    [Fact]
    public void DuplicateAndInvalidEntriesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new PropertyMap(
        [
            new("test:key", PropertyValue.FromInteger(1)),
            new("test:key", PropertyValue.FromInteger(2)),
        ]));
        Assert.Throws<ArgumentException>(() => new PropertyMap(
            [new KeyValuePair<string, PropertyValue>(" ", PropertyValue.FromInteger(1))]));
        Assert.Throws<ArgumentNullException>(() => new PropertyMap(
            [new KeyValuePair<string, PropertyValue>("test:key", null!)]));
    }

    [Fact]
    public void IndependentMapsUseStructuralEqualityAndHashing()
    {
        var first = new PropertyMap(
        [
            new("test:b", PropertyValue.FromBoolean(true)),
            new("test:a", PropertyValue.FromText("value")),
        ]);
        var same = new PropertyMap(
        [
            new("test:a", PropertyValue.FromText("value")),
            new("test:b", PropertyValue.FromBoolean(true)),
        ]);
        var different = new PropertyMap(
            [new("test:a", PropertyValue.FromText("other"))]);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void PublicMapSurfaceCannotModifyTheCollection()
    {
        var map = new PropertyMap(
            [new("test:key", PropertyValue.FromText("value"))]);

        Assert.IsAssignableFrom<IReadOnlyDictionary<string, PropertyValue>>(map);
        Assert.IsNotAssignableFrom<IDictionary<string, PropertyValue>>(map);

        var extended = new PropertyMap(map.Append(
            new KeyValuePair<string, PropertyValue>("test:other", PropertyValue.FromInteger(2))));

        Assert.Single(map);
        Assert.Equal(2, extended.Count);
    }

    [Fact]
    public void NullInputCreatesAnImmutableEmptyMap()
    {
        var map = new PropertyMap();

        Assert.Empty(map);
        Assert.Same(PropertyMap.Empty, PropertyMap.Empty);
        Assert.Equal(PropertyMap.Empty, map);
    }
}
