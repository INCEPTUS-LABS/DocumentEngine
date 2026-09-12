using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Metadata;

public sealed class DocumentMetadataSnapshotTests
{
    [Fact]
    public void SystemManagedAndExtensionMetadataRemainDistinctAndImmutable()
    {
        var system = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:schema", PropertyValue.FromText("1")),
        };
        var extensions = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:plugin-setting", PropertyValue.FromBoolean(true)),
        };
        var snapshot = new DocumentMetadataSnapshot(
            new DocumentId("test:document"),
            new DocumentRevision(4),
            system,
            extensions);
        system.Clear();
        extensions.Clear();

        Assert.Equal("1", snapshot.SystemManagedProperties["test:schema"].TextValue);
        Assert.True(snapshot.ExtensionProperties["test:plugin-setting"].BooleanValue);
        Assert.Single(snapshot.SystemManagedProperties);
        Assert.Single(snapshot.ExtensionProperties);
    }

    [Fact]
    public void MetadataOrderingIsDeterministicAndOrdinal()
    {
        var snapshot = new DocumentMetadataSnapshot(
            new DocumentId("test:document"),
            new DocumentRevision(4),
            [
                new("test:z", PropertyValue.FromInteger(3)),
                new("test:a", PropertyValue.FromInteger(2)),
                new("test:A", PropertyValue.FromInteger(1)),
            ]);

        Assert.Equal(["test:A", "test:a", "test:z"], snapshot.SystemManagedProperties.Keys);
    }

    [Fact]
    public void DuplicateMetadataKeysWithinAComponentAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new DocumentMetadataSnapshot(
            new DocumentId("test:document"),
            new DocumentRevision(4),
            [
                new("test:key", PropertyValue.FromInteger(1)),
                new("test:key", PropertyValue.FromInteger(2)),
            ]));
        Assert.Throws<ArgumentException>(() => new DocumentMetadataSnapshot(
            new DocumentId("test:document"),
            new DocumentRevision(4),
            extensionProperties:
            [
                new("test:key", PropertyValue.FromInteger(1)),
                new("test:key", PropertyValue.FromInteger(2)),
            ]));
    }

    [Fact]
    public void IndependentMetadataSnapshotsUseDeepStructuralEquality()
    {
        var first = Snapshot(reverse: false);
        var same = Snapshot(reverse: true);
        var different = new DocumentMetadataSnapshot(
            new DocumentId("test:document"),
            new DocumentRevision(4),
            [new("test:schema", PropertyValue.FromText("other"))]);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void EmptyMetadataRequiresNoEnvironmentDependentValues()
    {
        var snapshot = new DocumentMetadataSnapshot(
            new DocumentId("test:document"),
            DocumentRevision.Zero);

        Assert.Empty(snapshot.SystemManagedProperties);
        Assert.Empty(snapshot.ExtensionProperties);
    }

    private static DocumentMetadataSnapshot Snapshot(bool reverse)
    {
        KeyValuePair<string, PropertyValue>[] system =
        [
            new("test:schema", PropertyValue.FromText("1")),
            new("test:format", PropertyValue.FromText("test")),
        ];
        KeyValuePair<string, PropertyValue>[] extensions =
        [
            new("test:enabled", PropertyValue.FromBoolean(true)),
            new("test:level", PropertyValue.FromInteger(2)),
        ];

        return new DocumentMetadataSnapshot(
            new DocumentId("test:document"),
            new DocumentRevision(4),
            reverse ? system.Reverse() : system,
            reverse ? extensions.Reverse() : extensions);
    }
}
