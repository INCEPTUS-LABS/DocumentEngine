using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseB1SnapshotCompositionTests
{
    [Fact]
    public void CompleteSnapshotIsCoherentImmutableOrderedAndStructurallyComparable()
    {
        var documentId = new DocumentId("test:document");
        var revision = new DocumentRevision(12);
        var elementProperties = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:name", PropertyValue.FromText("Source")),
        };
        var elements = new List<SemanticElementSnapshot>
        {
            Element("test:target", "Target"),
            new(
                new SemanticElementId("test:source"),
                new SemanticTypeId("test:node-type"),
                elementProperties),
        };
        var relationships = new List<SemanticRelationshipSnapshot>
        {
            new(
                new SemanticElementId("test:connection"),
                new SemanticTypeId("test:connection-type"),
                new SemanticElementId("test:source"),
                new SemanticElementId("test:target")),
        };
        var route = new List<PointD> { new(110d, 40d), new(200d, 40d) };
        var visualStates = new List<VisualStateSnapshot>
        {
            Visual("test:visual-target", "test:target", new PointD(200d, 10d)),
            new(
                new VisualStateId("test:visual-connection"),
                new SemanticElementId("test:connection"),
                new PointD(0d, 0d),
                new SizeD(0d, 0d),
                VisualPlacementMode.Automatic,
                route),
            Visual("test:visual-source", "test:source", new PointD(10d, 10d)),
        };
        var systemMetadata = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:schema", PropertyValue.FromText("1")),
        };
        var extensionMetadata = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:setting", PropertyValue.FromBoolean(true)),
        };

        var document = new DocumentSnapshot(
            new SemanticModelSnapshot(documentId, revision, elements, relationships),
            new VisualModelSnapshot(documentId, revision, visualStates),
            new DocumentMetadataSnapshot(
                documentId,
                revision,
                systemMetadata,
                extensionMetadata));

        elements.Clear();
        relationships.Clear();
        visualStates.Clear();
        route.Clear();
        elementProperties.Clear();
        systemMetadata.Clear();
        extensionMetadata.Clear();

        IDocumentView view = document;
        Assert.Equal(documentId, view.DocumentId);
        Assert.Equal(revision, view.Revision);
        Assert.Equal(["test:source", "test:target"],
            view.SemanticModel.Elements.Select(element => element.Id.Value));
        Assert.Equal("test:source", view.SemanticModel.Relationships[0].SourceId.Value);
        Assert.Equal("test:target", view.SemanticModel.Relationships[0].TargetId.Value);
        Assert.Equal(["test:visual-connection", "test:visual-source", "test:visual-target"],
            view.VisualModel.VisualStates.Select(state => state.Id.Value));
        Assert.Equal(2, view.VisualModel.VisualStates[0].Route.Length);
        Assert.Equal("1", view.Metadata.SystemManagedProperties["test:schema"].TextValue);
        Assert.True(view.Metadata.ExtensionProperties["test:setting"].BooleanValue);

        var equivalent = CreateEquivalentSnapshot(reverseInput: true);
        Assert.Equal(document, equivalent);
        Assert.Equal(document.GetHashCode(), equivalent.GetHashCode());
    }

    private static DocumentSnapshot CreateEquivalentSnapshot(bool reverseInput)
    {
        var documentId = new DocumentId("test:document");
        var revision = new DocumentRevision(12);
        SemanticElementSnapshot[] elements =
        [
            Element("test:source", "Source"),
            Element("test:target", "Target"),
        ];
        SemanticRelationshipSnapshot[] relationships =
        [
            new(
                new SemanticElementId("test:connection"),
                new SemanticTypeId("test:connection-type"),
                new SemanticElementId("test:source"),
                new SemanticElementId("test:target")),
        ];
        VisualStateSnapshot[] visualStates =
        [
            Visual("test:visual-source", "test:source", new PointD(10d, 10d)),
            new(
                new VisualStateId("test:visual-connection"),
                new SemanticElementId("test:connection"),
                new PointD(0d, 0d),
                new SizeD(0d, 0d),
                VisualPlacementMode.Automatic,
                [new PointD(110d, 40d), new PointD(200d, 40d)]),
            Visual("test:visual-target", "test:target", new PointD(200d, 10d)),
        ];

        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                documentId,
                revision,
                reverseInput ? elements.Reverse() : elements,
                relationships),
            new VisualModelSnapshot(
                documentId,
                revision,
                reverseInput ? visualStates.Reverse() : visualStates),
            new DocumentMetadataSnapshot(
                documentId,
                revision,
                [new("test:schema", PropertyValue.FromText("1"))],
                [new("test:setting", PropertyValue.FromBoolean(true))]));
    }

    private static SemanticElementSnapshot Element(string id, string name) =>
        new(
            new SemanticElementId(id),
            new SemanticTypeId("test:node-type"),
            [new("test:name", PropertyValue.FromText(name))]);

    private static VisualStateSnapshot Visual(string id, string semanticId, PointD position) =>
        new(
            new VisualStateId(id),
            new SemanticElementId(semanticId),
            position,
            new SizeD(100d, 60d),
            VisualPlacementMode.Manual);
}
