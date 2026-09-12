using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Runtime.Documents;

public sealed class DocumentInvariantBoundaryTests
{
    private static readonly DocumentId TestDocumentId = new("test:document");
    private static readonly DocumentRevision TestRevision = new(3);
    private static readonly SemanticTypeId TestTypeId = new("test:type");

    [Fact]
    public void DuplicateSemanticIdentitiesAreRejectedBeforeRuntimeDocumentConstruction()
    {
        Assert.Throws<ArgumentException>(() => new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [Element("test:duplicate"), Element("test:duplicate")]));
        Assert.Throws<ArgumentException>(() => new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [Element("test:shared")],
            [Relationship("test:shared", "test:source", "test:target")]));
    }

    [Fact]
    public void DuplicateVisualIdentitiesAreRejectedBeforeRuntimeDocumentConstruction()
    {
        Assert.Throws<ArgumentException>(() => new VisualModelSnapshot(
            TestDocumentId,
            TestRevision,
            [Visual("test:duplicate"), Visual("test:duplicate")]));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ComponentMismatchIsRejectedBeforeRuntimeDocumentConstruction(
        bool mismatchIdentity,
        bool mismatchRevision)
    {
        var semantic = new SemanticModelSnapshot(TestDocumentId, TestRevision);
        var visual = new VisualModelSnapshot(
            mismatchIdentity ? new DocumentId("test:other") : TestDocumentId,
            mismatchRevision ? TestRevision.Increment() : TestRevision);
        var metadata = new DocumentMetadataSnapshot(TestDocumentId, TestRevision);

        Assert.Throws<ArgumentException>(() => new DocumentSnapshot(semantic, visual, metadata));
    }

    [Fact]
    public void InvalidGeometryAndRoutePointsAreRejectedBeforeRuntimeDocumentConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointD(double.NaN, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeD(-1d, 10d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VisualStateSnapshot(
            new VisualStateId("test:visual"),
            new SemanticElementId("test:semantic"),
            new PointD(0d, 0d),
            new SizeD(10d, 10d),
            VisualPlacementMode.Manual,
            [new PointD(double.PositiveInfinity, 0d)]));
    }

    [Theory]
    [InlineData(-1d, 0d)]
    [InlineData(0d, -1d)]
    public void RuntimeReconstructionRejectsNegativePersistentNodePosition(
        double x,
        double y)
    {
        var semanticId = new SemanticElementId("test:semantic");
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                TestDocumentId,
                TestRevision,
                [new SemanticElementSnapshot(semanticId, TestTypeId)]),
            new VisualModelSnapshot(
                TestDocumentId,
                TestRevision,
                [
                    new VisualStateSnapshot(
                        new VisualStateId("test:visual"),
                        semanticId,
                        new PointD(x, y),
                        new SizeD(10d, 10d),
                        VisualPlacementMode.Pinned),
                ]),
            new DocumentMetadataSnapshot(TestDocumentId, TestRevision));

        var result = DocumentReconstructor.Reconstruct(snapshot);

        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.Equal(
            DocumentInvariantValidator.VisualGeometryInvalidCode,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void RuntimeReconstructionAcceptsPersistentGeometryOnTheBoundary()
    {
        var semanticId = new SemanticElementId("test:semantic");
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                TestDocumentId,
                TestRevision,
                [new SemanticElementSnapshot(semanticId, TestTypeId)]),
            new VisualModelSnapshot(
                TestDocumentId,
                TestRevision,
                [
                    new VisualStateSnapshot(
                        new VisualStateId("test:visual"),
                        semanticId,
                        new PointD(0d, 0d),
                        new SizeD(10d, 10d),
                        VisualPlacementMode.Pinned),
                ]),
            new DocumentMetadataSnapshot(TestDocumentId, TestRevision));

        var result = DocumentReconstructor.Reconstruct(snapshot);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(0d, 10d)]
    [InlineData(10d, 0d)]
    public void RuntimeReconstructionRejectsZeroExtentPersistentNodeGeometry(
        double width,
        double height)
    {
        var semanticId = new SemanticElementId("test:semantic");
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                TestDocumentId,
                TestRevision,
                [new SemanticElementSnapshot(semanticId, TestTypeId)]),
            new VisualModelSnapshot(
                TestDocumentId,
                TestRevision,
                [
                    new VisualStateSnapshot(
                        new VisualStateId("test:visual"),
                        semanticId,
                        new PointD(0d, 0d),
                        new SizeD(width, height),
                        VisualPlacementMode.Pinned),
                ]),
            new DocumentMetadataSnapshot(TestDocumentId, TestRevision));

        var result = DocumentReconstructor.Reconstruct(snapshot);

        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.Equal(
            DocumentInvariantValidator.VisualGeometryInvalidCode,
            Assert.Single(result.Diagnostics).Code);
    }

    private static SemanticElementSnapshot Element(string id) =>
        new(new SemanticElementId(id), TestTypeId);

    private static SemanticRelationshipSnapshot Relationship(
        string id,
        string sourceId,
        string targetId) =>
        new(
            new SemanticElementId(id),
            TestTypeId,
            new SemanticElementId(sourceId),
            new SemanticElementId(targetId));

    private static VisualStateSnapshot Visual(string id) =>
        new(
            new VisualStateId(id),
            new SemanticElementId("test:semantic"),
            new PointD(0d, 0d),
            new SizeD(10d, 10d),
            VisualPlacementMode.Automatic);
}
