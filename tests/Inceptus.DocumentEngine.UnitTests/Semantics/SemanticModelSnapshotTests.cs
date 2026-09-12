using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Semantics;

public sealed class SemanticModelSnapshotTests
{
    private static readonly DocumentId TestDocumentId = new("test:document");
    private static readonly DocumentRevision TestRevision = new(7);

    [Fact]
    public void EntriesAreDefensivelyCopiedAndOrderedByOrdinalIdentity()
    {
        var elements = new List<SemanticElementSnapshot>
        {
            Element("test:z"),
            Element("test:a"),
            Element("test:A"),
        };
        var relationships = new List<SemanticRelationshipSnapshot>
        {
            Relationship("test:relationship-z", "test:A", "test:z"),
            Relationship("test:relationship-a", "test:a", "test:z"),
        };

        var snapshot = new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            elements,
            relationships);
        elements.Clear();
        relationships.Clear();

        Assert.Equal(["test:A", "test:a", "test:z"],
            snapshot.Elements.Select(element => element.Id.Value));
        Assert.Equal(["test:relationship-a", "test:relationship-z"],
            snapshot.Relationships.Select(relationship => relationship.Id.Value));
        Assert.Equal(3, snapshot.ElementCount);
        Assert.Equal(2, snapshot.RelationshipCount);
    }

    [Fact]
    public void LookupIsStableAndCaseSensitive()
    {
        var expectedElement = Element("test:node");
        var expectedRelationship = Relationship("test:edge", "test:node", "test:target");
        var snapshot = new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [expectedElement],
            [expectedRelationship]);

        Assert.True(snapshot.TryGetElement(expectedElement.Id, out var element));
        Assert.Same(expectedElement, element);
        Assert.True(snapshot.TryGetRelationship(expectedRelationship.Id, out var relationship));
        Assert.Same(expectedRelationship, relationship);
        Assert.False(snapshot.TryGetElement(new SemanticElementId("TEST:NODE"), out element));
        Assert.Null(element);
        Assert.False(snapshot.TryGetRelationship(new SemanticElementId("test:missing"), out relationship));
        Assert.Null(relationship);
    }

    [Fact]
    public void DuplicateSemanticIdentitiesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [Element("test:duplicate"), Element("test:duplicate")],
            []));
        Assert.Throws<ArgumentException>(() => new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [],
            [
                Relationship("test:duplicate", "test:a", "test:b"),
                Relationship("test:duplicate", "test:b", "test:c"),
            ]));
        Assert.Throws<ArgumentException>(() => new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [Element("test:shared")],
            [Relationship("test:shared", "test:a", "test:b")]));
    }

    [Fact]
    public void RelationshipPreservesIdentityTypeEndpointsAndProperties()
    {
        var properties = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:condition", PropertyValue.FromText("approved")),
        };
        var relationship = new SemanticRelationshipSnapshot(
            new SemanticElementId("test:relationship"),
            new SemanticTypeId("test:connection"),
            new SemanticElementId("test:source"),
            new SemanticElementId("test:target"),
            properties);
        properties.Clear();

        Assert.Equal("test:relationship", relationship.Id.Value);
        Assert.Equal("test:connection", relationship.TypeId.Value);
        Assert.Equal("test:source", relationship.SourceId.Value);
        Assert.Equal("test:target", relationship.TargetId.Value);
        Assert.Equal("approved", relationship.Properties["test:condition"].TextValue);
    }

    [Fact]
    public void IndependentSnapshotsUseDeepStructuralEquality()
    {
        var first = new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [Element("test:b", "second"), Element("test:a", "first")],
            [Relationship("test:edge", "test:a", "test:b")]);
        var same = new SemanticModelSnapshot(
            new DocumentId("test:document"),
            new DocumentRevision(7),
            [Element("test:a", "first"), Element("test:b", "second")],
            [Relationship("test:edge", "test:a", "test:b")]);
        var different = new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [Element("test:a", "changed"), Element("test:b", "second")],
            [Relationship("test:edge", "test:a", "test:b")]);

        Assert.Equal(first, same);
        Assert.True(first == same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, different);
        Assert.True(first != different);
    }

    [Fact]
    public void ElementPropertiesAreDefensivelyCopied()
    {
        var properties = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:name", PropertyValue.FromText("original")),
        };
        var element = new SemanticElementSnapshot(
            new SemanticElementId("test:node"),
            new SemanticTypeId("test:node-type"),
            properties);
        properties[0] = new("test:name", PropertyValue.FromText("changed"));
        properties.Add(new("test:later", PropertyValue.FromBoolean(true)));

        Assert.Equal("original", element.Properties["test:name"].TextValue);
        Assert.False(element.Properties.ContainsKey("test:later"));
    }

    private static SemanticElementSnapshot Element(string id, string? name = null) =>
        new(
            new SemanticElementId(id),
            new SemanticTypeId("test:node-type"),
            name is null
                ? null
                : [new("test:name", PropertyValue.FromText(name))]);

    private static SemanticRelationshipSnapshot Relationship(
        string id,
        string sourceId,
        string targetId) =>
        new(
            new SemanticElementId(id),
            new SemanticTypeId("test:connection-type"),
            new SemanticElementId(sourceId),
            new SemanticElementId(targetId));
}
