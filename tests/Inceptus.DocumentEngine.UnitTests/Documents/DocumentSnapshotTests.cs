using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Documents;

public sealed class DocumentSnapshotTests
{
    private static readonly DocumentId TestDocumentId = new("test:document");
    private static readonly DocumentRevision TestRevision = new(9);

    [Fact]
    public void SnapshotIncludesExactlyTheThreeAuthoritativeComponentsAtOneRevision()
    {
        var semantic = Semantic(TestDocumentId, TestRevision);
        var visual = Visual(TestDocumentId, TestRevision);
        var metadata = Metadata(TestDocumentId, TestRevision);

        var snapshot = new DocumentSnapshot(semantic, visual, metadata);
        IDocumentView view = snapshot;

        Assert.Equal(TestDocumentId, snapshot.DocumentId);
        Assert.Equal(TestRevision, snapshot.Revision);
        Assert.Same(semantic, snapshot.SemanticModel);
        Assert.Same(visual, snapshot.VisualModel);
        Assert.Same(metadata, snapshot.Metadata);
        Assert.Same(semantic, view.SemanticModel);
        Assert.Same(visual, view.VisualModel);
        Assert.Same(metadata, view.Metadata);
    }

    [Theory]
    [InlineData("semantic")]
    [InlineData("visual")]
    [InlineData("metadata")]
    public void ComponentDocumentIdentityMismatchIsRejected(string changedComponent)
    {
        var differentId = new DocumentId("test:other-document");
        var semantic = Semantic(
            changedComponent == "semantic" ? differentId : TestDocumentId,
            TestRevision);
        var visual = Visual(
            changedComponent == "visual" ? differentId : TestDocumentId,
            TestRevision);
        var metadata = Metadata(
            changedComponent == "metadata" ? differentId : TestDocumentId,
            TestRevision);

        if (changedComponent == "semantic")
        {
            Assert.Throws<ArgumentException>(() => new DocumentSnapshot(
                semantic,
                Visual(TestDocumentId, TestRevision),
                Metadata(TestDocumentId, TestRevision)));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => new DocumentSnapshot(semantic, visual, metadata));
        }
    }

    [Theory]
    [InlineData("semantic")]
    [InlineData("visual")]
    [InlineData("metadata")]
    public void ComponentRevisionMismatchIsRejected(string changedComponent)
    {
        var otherRevision = TestRevision.Increment();
        var semantic = Semantic(
            TestDocumentId,
            changedComponent == "semantic" ? otherRevision : TestRevision);
        var visual = Visual(
            TestDocumentId,
            changedComponent == "visual" ? otherRevision : TestRevision);
        var metadata = Metadata(
            TestDocumentId,
            changedComponent == "metadata" ? otherRevision : TestRevision);

        if (changedComponent == "semantic")
        {
            Assert.Throws<ArgumentException>(() => new DocumentSnapshot(
                semantic,
                Visual(TestDocumentId, TestRevision),
                Metadata(TestDocumentId, TestRevision)));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => new DocumentSnapshot(semantic, visual, metadata));
        }
    }

    [Fact]
    public void AuthoritativeComponentsAreRequired()
    {
        var semantic = Semantic(TestDocumentId, TestRevision);
        var visual = Visual(TestDocumentId, TestRevision);
        var metadata = Metadata(TestDocumentId, TestRevision);

        Assert.Throws<ArgumentNullException>(() => new DocumentSnapshot(null!, visual, metadata));
        Assert.Throws<ArgumentNullException>(() => new DocumentSnapshot(semantic, null!, metadata));
        Assert.Throws<ArgumentNullException>(() => new DocumentSnapshot(semantic, visual, null!));
    }

    [Fact]
    public void IndependentDocumentsUseDeepStructuralEquality()
    {
        var first = Snapshot(reverseProperties: false);
        var same = Snapshot(reverseProperties: true);
        var different = new DocumentSnapshot(
            Semantic(TestDocumentId, TestRevision, "changed"),
            Visual(TestDocumentId, TestRevision),
            Metadata(TestDocumentId, TestRevision));

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, different);
    }

    private static DocumentSnapshot Snapshot(bool reverseProperties)
    {
        KeyValuePair<string, PropertyValue>[] properties =
        [
            new("test:name", PropertyValue.FromText("node")),
            new("test:enabled", PropertyValue.FromBoolean(true)),
        ];
        var orderedProperties = reverseProperties ? properties.Reverse() : properties;
        var semantic = new SemanticModelSnapshot(
            new DocumentId("test:document"),
            new DocumentRevision(9),
            [
                new SemanticElementSnapshot(
                    new SemanticElementId("test:node"),
                    new SemanticTypeId("test:node-type"),
                    orderedProperties),
            ]);

        return new DocumentSnapshot(
            semantic,
            Visual(new DocumentId("test:document"), new DocumentRevision(9)),
            Metadata(new DocumentId("test:document"), new DocumentRevision(9)));
    }

    private static SemanticModelSnapshot Semantic(
        DocumentId documentId,
        DocumentRevision revision,
        string name = "node") =>
        new(
            documentId,
            revision,
            [
                new SemanticElementSnapshot(
                    new SemanticElementId("test:node"),
                    new SemanticTypeId("test:node-type"),
                    [new("test:name", PropertyValue.FromText(name))]),
            ]);

    private static VisualModelSnapshot Visual(
        DocumentId documentId,
        DocumentRevision revision) =>
        new(
            documentId,
            revision,
            [
                new VisualStateSnapshot(
                    new VisualStateId("test:visual"),
                    new SemanticElementId("test:node"),
                    new PointD(10d, 20d),
                    new SizeD(100d, 60d),
                    VisualPlacementMode.Manual),
            ]);

    private static DocumentMetadataSnapshot Metadata(
        DocumentId documentId,
        DocumentRevision revision) =>
        new(
            documentId,
            revision,
            [new("test:schema", PropertyValue.FromText("1"))]);
}
