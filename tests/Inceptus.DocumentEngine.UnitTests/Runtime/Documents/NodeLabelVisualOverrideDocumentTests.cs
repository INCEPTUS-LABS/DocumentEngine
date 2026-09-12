using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Runtime.Documents;

public sealed class NodeLabelVisualOverrideDocumentTests
{
    private static readonly DocumentId DocumentId = new("test:node-label-document");
    private static readonly SemanticElementId SourceId = new("test:source");
    private static readonly SemanticElementId TargetId = new("test:target");
    private static readonly SemanticElementId RelationshipId = new("test:relationship");
    private static readonly DocumentRevision Revision = new(7);

    [Fact]
    public void CompleteElementOwnedOverrideReconstructsWithoutLosingPersistentValues()
    {
        var expected = new NodeLabelVisualOverride(40d, 18d, 145d, 44d);
        var properties = NodeLabelVisualOverride.UpdateProperties(
            new PropertyMap(
            [
                new("test:style", PropertyValue.FromText("preserved")),
            ]),
            expected);
        var snapshot = Snapshot(SourceId, properties);

        var result = DocumentReconstructor.Reconstruct(snapshot);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        var captured = Assert.IsType<Document>(result.Document).CaptureSnapshot();
        Assert.Equal(Revision, captured.Revision);
        var visual = Assert.Single(captured.VisualModel.VisualStates);
        Assert.True(NodeLabelVisualOverride.TryRead(
            visual.Properties,
            out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal("preserved", visual.Properties["test:style"].TextValue);
    }

    [Fact]
    public void VisualStateWithoutOverrideRemainsBackwardCompatible()
    {
        var result = DocumentReconstructor.Reconstruct(
            Snapshot(SourceId, PropertyMap.Empty));

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        var visual = Assert.Single(
            Assert.IsType<Document>(result.Document).VisualModel.VisualStates);
        Assert.False(NodeLabelVisualOverride.TryRead(visual.Properties, out _));
    }

    [Fact]
    public void PartialWrongKindSubminimumOrRelationshipOwnedOverrideIsRejectedAtomically()
    {
        var cases = new[]
        {
            new InvalidOverrideCase(
                "test:partial",
                SourceId,
                new PropertyMap(
                [
                    new(
                        NodeLabelVisualOverride.OffsetXPropertyKey,
                        PropertyValue.FromNumber(2d)),
                ])),
            new InvalidOverrideCase(
                "test:wrong-kind",
                SourceId,
                CompleteProperties(
                    PropertyValue.FromText("2"),
                    PropertyValue.FromNumber(3d),
                    PropertyValue.FromNumber(80d),
                    PropertyValue.FromNumber(20d))),
            new InvalidOverrideCase(
                "test:width-below-minimum",
                SourceId,
                CompleteProperties(
                    PropertyValue.FromNumber(2d),
                    PropertyValue.FromNumber(3d),
                    PropertyValue.FromNumber(0.5d),
                    PropertyValue.FromNumber(20d))),
            new InvalidOverrideCase(
                "test:height-below-minimum",
                SourceId,
                CompleteProperties(
                    PropertyValue.FromNumber(2d),
                    PropertyValue.FromNumber(3d),
                    PropertyValue.FromNumber(80d),
                    PropertyValue.FromNumber(0.5d))),
            new InvalidOverrideCase(
                "test:outside-document",
                SourceId,
                CompleteProperties(
                    PropertyValue.FromNumber(-70d),
                    PropertyValue.FromNumber(-40d),
                    PropertyValue.FromNumber(80d),
                    PropertyValue.FromNumber(20d))),
            new InvalidOverrideCase(
                "test:relationship-owned",
                RelationshipId,
                CompleteProperties(
                    PropertyValue.FromNumber(2d),
                    PropertyValue.FromNumber(3d),
                    PropertyValue.FromNumber(80d),
                    PropertyValue.FromNumber(20d))),
        };

        foreach (var invalid in cases)
        {
            var result = DocumentReconstructor.Reconstruct(
                Snapshot(invalid.SemanticId, invalid.Properties, invalid.VisualId));

            Assert.False(result.Succeeded);
            Assert.Null(result.Document);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(
                DocumentInvariantValidator.VisualNodeLabelOverrideInvalidCode,
                diagnostic.Code);
            Assert.Equal(invalid.VisualId, diagnostic.SourceIdentity);
        }
    }

    private static DocumentSnapshot Snapshot(
        SemanticElementId semanticId,
        PropertyMap properties,
        string visualId = "test:visual") =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                Revision,
                [
                    new SemanticElementSnapshot(
                        SourceId,
                        new SemanticTypeId("test:node")),
                    new SemanticElementSnapshot(
                        TargetId,
                        new SemanticTypeId("test:node")),
                ],
                [
                    new SemanticRelationshipSnapshot(
                        RelationshipId,
                        new SemanticTypeId("test:relationship"),
                        SourceId,
                        TargetId),
                ]),
            new VisualModelSnapshot(
                DocumentId,
                Revision,
                [
                    new VisualStateSnapshot(
                        new VisualStateId(visualId),
                        semanticId,
                        new PointD(10d, 20d),
                        new SizeD(100d, 60d),
                        VisualPlacementMode.Pinned,
                        properties: properties),
                ]),
            new DocumentMetadataSnapshot(DocumentId, Revision));

    private static PropertyMap CompleteProperties(
        PropertyValue offsetX,
        PropertyValue offsetY,
        PropertyValue width,
        PropertyValue height) =>
        new(
        [
            new(NodeLabelVisualOverride.OffsetXPropertyKey, offsetX),
            new(NodeLabelVisualOverride.OffsetYPropertyKey, offsetY),
            new(NodeLabelVisualOverride.WidthPropertyKey, width),
            new(NodeLabelVisualOverride.HeightPropertyKey, height),
        ]);

    private sealed record InvalidOverrideCase(
        string VisualId,
        SemanticElementId SemanticId,
        PropertyMap Properties);
}
