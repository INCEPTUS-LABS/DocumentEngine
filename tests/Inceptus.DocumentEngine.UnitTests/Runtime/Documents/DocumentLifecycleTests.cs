using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Runtime.Documents;

public sealed class DocumentLifecycleTests
{
    private static readonly DocumentId TestDocumentId = new("test:document");
    private static readonly SemanticTypeId NodeTypeId = new("test:node-type");
    private static readonly SemanticTypeId RelationshipTypeId = new("test:relationship-type");

    [Fact]
    public void CreateEmptyUsesExplicitIdentityAndRevisionZero()
    {
        var result = DocumentFactory.CreateEmpty(TestDocumentId);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);

        var document = Assert.IsType<Document>(result.Document);
        Assert.Equal(TestDocumentId, document.DocumentId);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
        Assert.Equal(0, document.SemanticModel.ElementCount);
        Assert.Equal(0, document.SemanticModel.RelationshipCount);
        Assert.Equal(0, document.VisualModel.Count);
        Assert.Empty(document.Metadata.SystemManagedProperties);
        Assert.Empty(document.Metadata.ExtensionProperties);
    }

    [Fact]
    public void CreateAcceptsSuppliedCoherentRevisionZeroState()
    {
        var initialState = CreateValidSnapshot(DocumentRevision.Zero);

        var result = DocumentFactory.Create(initialState);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);

        var document = Assert.IsType<Document>(result.Document);
        var captured = document.CaptureSnapshot();
        Assert.Equal(initialState, captured);
        Assert.NotSame(initialState, captured);
        Assert.Equal(2, captured.SemanticModel.ElementCount);
        Assert.Single(captured.SemanticModel.Relationships);
        Assert.Equal(3, captured.VisualModel.Count);
    }

    [Fact]
    public void CreateRejectsNonzeroInitialRevisionWithoutProducingDocument()
    {
        var initialState = CreateValidSnapshot(new DocumentRevision(1));

        var result = DocumentFactory.Create(initialState);

        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("DOC_CREATION_REVISION_NOT_ZERO", diagnostic.Code);
        Assert.Equal(TestDocumentId.Value, diagnostic.SourceIdentity);
        Assert.Equal("1", diagnostic.Context["ActualRevision"]);
    }

    [Fact]
    public void EmptyCreationCopiesMetadataIndependentlyOfCallerCollections()
    {
        var systemEntries = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:schema", PropertyValue.FromText("1")),
        };
        var extensionEntries = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:feature", PropertyValue.FromBoolean(true)),
        };
        var systemMetadata = new PropertyMap(systemEntries);
        var extensionMetadata = new PropertyMap(extensionEntries);

        var result = DocumentFactory.CreateEmpty(
            TestDocumentId,
            systemMetadata,
            extensionMetadata);
        systemEntries[0] = new("test:schema", PropertyValue.FromText("changed"));
        extensionEntries.Clear();

        var document = Assert.IsType<Document>(result.Document);
        var metadata = document.CaptureSnapshot().Metadata;
        Assert.Equal("1", metadata.SystemManagedProperties["test:schema"].TextValue);
        Assert.True(metadata.ExtensionProperties["test:feature"].BooleanValue);
        Assert.NotSame(systemMetadata, metadata.SystemManagedProperties);
        Assert.NotSame(extensionMetadata, metadata.ExtensionProperties);
    }

    [Fact]
    public void CaptureSnapshotExposesOneCoherentReadOnlyState()
    {
        var document = Assert.IsType<Document>(
            DocumentFactory.Create(CreateValidSnapshot(DocumentRevision.Zero)).Document);

        var captured = document.CaptureSnapshot();

        Assert.Equal(document.DocumentId, captured.DocumentId);
        Assert.Equal(document.Revision, captured.Revision);
        Assert.Equal(captured.DocumentId, captured.SemanticModel.DocumentId);
        Assert.Equal(captured.DocumentId, captured.VisualModel.DocumentId);
        Assert.Equal(captured.DocumentId, captured.Metadata.DocumentId);
        Assert.Equal(captured.Revision, captured.SemanticModel.Revision);
        Assert.Equal(captured.Revision, captured.VisualModel.Revision);
        Assert.Equal(captured.Revision, captured.Metadata.Revision);

        Assert.Empty(typeof(Document).GetConstructors());
        Assert.All(
            typeof(Document).GetProperties(),
            property => Assert.Null(property.SetMethod));
        Assert.DoesNotContain(
            typeof(Document).GetMethods().Where(method => method.DeclaringType == typeof(Document)),
            method => method.Name.StartsWith("Add", StringComparison.Ordinal) ||
                method.Name.StartsWith("Remove", StringComparison.Ordinal) ||
                method.Name.StartsWith("Update", StringComparison.Ordinal) ||
                method.Name.StartsWith("Set", StringComparison.Ordinal) ||
                method.Name.StartsWith("Replace", StringComparison.Ordinal));
    }

    [Fact]
    public void ReconstructionPreservesIdentityRevisionAndStructuralState()
    {
        var source = CreateValidSnapshot(new DocumentRevision(37));

        var result = DocumentReconstructor.Reconstruct(source);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        var document = Assert.IsType<Document>(result.Document);
        Assert.Equal(TestDocumentId, document.DocumentId);
        Assert.Equal(new DocumentRevision(37), document.Revision);
        Assert.Equal(source, document.CaptureSnapshot());
        Assert.NotSame(source, document.CaptureSnapshot());
    }

    [Fact]
    public void IndependentReconstructionsProduceSeparateEquivalentDocuments()
    {
        var source = CreateValidSnapshot(new DocumentRevision(ulong.MaxValue));

        var first = Assert.IsType<Document>(DocumentReconstructor.Reconstruct(source).Document);
        var second = Assert.IsType<Document>(DocumentReconstructor.Reconstruct(source).Document);

        Assert.NotSame(first, second);
        Assert.NotSame(first.CaptureSnapshot(), second.CaptureSnapshot());
        Assert.Equal(first.CaptureSnapshot(), second.CaptureSnapshot());
        Assert.Equal(new DocumentRevision(ulong.MaxValue), first.Revision);
    }

    [Fact]
    public void CrossReferenceFailuresAreDeterministicAndProduceNoPartialDocument()
    {
        var revision = new DocumentRevision(8);
        var semantic = new SemanticModelSnapshot(
            TestDocumentId,
            revision,
            [Element("test:existing")],
            [
                Relationship("test:z-edge", "test:missing-z-source", "test:missing-z-target"),
                Relationship("test:a-edge", "test:missing-a-source", "test:missing-a-target"),
            ]);
        var visual = new VisualModelSnapshot(
            TestDocumentId,
            revision,
            [
                Visual("test:z-visual", "test:missing-z-visual"),
                Visual("test:a-visual", "test:missing-a-visual"),
            ]);
        var snapshot = Snapshot(revision, semantic, visual);

        var first = DocumentReconstructor.Reconstruct(snapshot);
        var second = DocumentReconstructor.Reconstruct(snapshot);

        Assert.False(first.Succeeded);
        Assert.Null(first.Document);
        Assert.False(second.Succeeded);
        Assert.Null(second.Document);

        var expectedCodes = new[]
        {
            "DOC_RELATIONSHIP_SOURCE_MISSING",
            "DOC_RELATIONSHIP_TARGET_MISSING",
            "DOC_RELATIONSHIP_SOURCE_MISSING",
            "DOC_RELATIONSHIP_TARGET_MISSING",
            "DOC_VISUAL_SEMANTIC_REFERENCE_MISSING",
            "DOC_VISUAL_SEMANTIC_REFERENCE_MISSING",
        };
        var expectedSources = new[]
        {
            "test:a-edge",
            "test:a-edge",
            "test:z-edge",
            "test:z-edge",
            "test:a-visual",
            "test:z-visual",
        };

        Assert.Equal(expectedCodes, first.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.Equal(expectedSources, first.Diagnostics.Select(diagnostic => diagnostic.SourceIdentity));
        Assert.Equal(
            first.Diagnostics.Select(DiagnosticSignature),
            second.Diagnostics.Select(DiagnosticSignature));
    }

    [Fact]
    public void CreationAlsoRejectsInvalidCrossReferencesAtomically()
    {
        var revision = DocumentRevision.Zero;
        var semantic = new SemanticModelSnapshot(
            TestDocumentId,
            revision,
            [Element("test:node")]);
        var visual = new VisualModelSnapshot(
            TestDocumentId,
            revision,
            [Visual("test:visual", "test:missing")]);

        var result = DocumentFactory.Create(Snapshot(revision, semantic, visual));

        Assert.False(result.Succeeded);
        Assert.Null(result.Document);
        Assert.Equal(
            "DOC_VISUAL_SEMANTIC_REFERENCE_MISSING",
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void FailureDiagnosticsAreImmutableErrors()
    {
        var revision = DocumentRevision.Zero;
        var semantic = new SemanticModelSnapshot(
            TestDocumentId,
            revision,
            [Element("test:node")]);
        var visual = new VisualModelSnapshot(
            TestDocumentId,
            revision,
            [Visual("test:visual", "test:missing")]);

        var result = DocumentFactory.Create(Snapshot(revision, semantic, visual));
        var mutableView = (IList<Diagnostic>)result.Diagnostics;

        Assert.False(result.Succeeded);
        Assert.True(mutableView.IsReadOnly);
        Assert.All(result.Diagnostics, diagnostic =>
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
    }

    [Fact]
    public void VisualStateMayReferenceSemanticRelationshipIdentity()
    {
        var revision = new DocumentRevision(4);
        var semantic = new SemanticModelSnapshot(
            TestDocumentId,
            revision,
            [Element("test:source"), Element("test:target")],
            [Relationship("test:edge", "test:source", "test:target")]);
        var visual = new VisualModelSnapshot(
            TestDocumentId,
            revision,
            [Visual("test:edge-visual", "test:edge")]);

        var result = DocumentReconstructor.Reconstruct(Snapshot(revision, semantic, visual));

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Document);
    }

    [Fact]
    public void CompleteNumericConnectorLabelPlacementIsAcceptedOnlyForRelationshipVisual()
    {
        var revision = new DocumentRevision(4);
        var semantic = new SemanticModelSnapshot(
            TestDocumentId,
            revision,
            [Element("test:source"), Element("test:target")],
            [Relationship("test:edge", "test:source", "test:target")]);
        var placement = ConnectorLabelPlacement.UpdateProperties(
            PropertyMap.Empty,
            new ConnectorLabelPlacement(0.75d, new VectorD(-3d, 8d)));
        var visual = new VisualModelSnapshot(
            TestDocumentId,
            revision,
            [
                new VisualStateSnapshot(
                    new VisualStateId("test:edge-visual"),
                    new SemanticElementId("test:edge"),
                    default,
                    default,
                    VisualPlacementMode.Manual,
                    properties: placement),
            ]);

        var result = DocumentReconstructor.Reconstruct(Snapshot(revision, semantic, visual));

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Document);
    }

    [Fact]
    public void MalformedOrElementOwnedConnectorLabelPlacementIsRejectedAtomically()
    {
        var revision = new DocumentRevision(4);
        var semantic = new SemanticModelSnapshot(
            TestDocumentId,
            revision,
            [Element("test:source"), Element("test:target")],
            [Relationship("test:edge", "test:source", "test:target")]);
        var cases = new[]
        {
            new InvalidPlacementCase(
                "test:partial",
                "test:edge",
                new PropertyMap(
                [
                    new(
                        ConnectorLabelPlacement.PathPositionPropertyKey,
                        PropertyValue.FromNumber(0.5d)),
                ])),
            new InvalidPlacementCase(
                "test:wrong-kind",
                "test:edge",
                PlacementProperties(PropertyValue.FromText("0.5"))),
            new InvalidPlacementCase(
                "test:below-range",
                "test:edge",
                PlacementProperties(PropertyValue.FromNumber(-0.01d))),
            new InvalidPlacementCase(
                "test:above-range",
                "test:edge",
                PlacementProperties(PropertyValue.FromNumber(1.01d))),
            new InvalidPlacementCase(
                "test:element-owned",
                "test:source",
                PlacementProperties(PropertyValue.FromNumber(0.5d))),
        };

        foreach (var invalid in cases)
        {
            var visual = new VisualModelSnapshot(
                TestDocumentId,
                revision,
                [
                    new VisualStateSnapshot(
                        new VisualStateId(invalid.VisualId),
                        new SemanticElementId(invalid.SemanticId),
                        default,
                        invalid.SemanticId == "test:source"
                            ? new SizeD(10d, 10d)
                            : default,
                        VisualPlacementMode.Manual,
                        properties: invalid.Properties),
                ]);

            var result = DocumentReconstructor.Reconstruct(
                Snapshot(revision, semantic, visual));

            Assert.False(result.Succeeded);
            Assert.Null(result.Document);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(
                DocumentInvariantValidator.VisualLabelPlacementInvalidCode,
                diagnostic.Code);
            Assert.Equal(invalid.VisualId, diagnostic.SourceIdentity);
        }
    }

    [Fact]
    public void MultipleVisualStatesMayReferenceOneSemanticIdentity()
    {
        var revision = new DocumentRevision(5);
        var semantic = new SemanticModelSnapshot(
            TestDocumentId,
            revision,
            [Element("test:node")]);
        var visual = new VisualModelSnapshot(
            TestDocumentId,
            revision,
            [Visual("test:first", "test:node"), Visual("test:second", "test:node")]);

        var result = DocumentReconstructor.Reconstruct(Snapshot(revision, semantic, visual));

        Assert.True(result.Succeeded);
        Assert.Equal(2, Assert.IsType<Document>(result.Document).VisualModel.Count);
    }

    [Fact]
    public void ReconstructionPreservesAutomaticManualAndPinnedAppearanceAndRoutes()
    {
        var revision = new DocumentRevision(6);
        var semantic = new SemanticModelSnapshot(
            TestDocumentId,
            revision,
            [Element("test:node")]);
        var expectedVisuals = new[]
        {
            Visual("test:auto", "test:node", VisualPlacementMode.Automatic),
            Visual(
                "test:manual",
                "test:node",
                VisualPlacementMode.Manual,
                [new PointD(0d, 0d), new PointD(25d, 10d)]),
            Visual(
                "test:pinned",
                "test:node",
                VisualPlacementMode.Pinned,
                [new PointD(10d, 10d), new PointD(50d, 40d)]),
        };
        var visual = new VisualModelSnapshot(TestDocumentId, revision, expectedVisuals);

        var result = DocumentReconstructor.Reconstruct(Snapshot(revision, semantic, visual));

        var captured = Assert.IsType<Document>(result.Document).CaptureSnapshot();
        Assert.Equal(expectedVisuals, captured.VisualModel.VisualStates);
        Assert.Equal(
            [VisualPlacementMode.Automatic, VisualPlacementMode.Manual, VisualPlacementMode.Pinned],
            captured.VisualModel.VisualStates.Select(state => state.PlacementMode));
        Assert.Equal(
            new[] { new PointD(10d, 10d), new PointD(50d, 40d) },
            captured.VisualModel.VisualStates.Single(state => state.Id.Value == "test:pinned").Route);
    }

    private static DocumentSnapshot CreateValidSnapshot(DocumentRevision revision)
    {
        var semantic = new SemanticModelSnapshot(
            TestDocumentId,
            revision,
            [Element("test:source"), Element("test:target")],
            [Relationship("test:edge", "test:source", "test:target")]);
        var visual = new VisualModelSnapshot(
            TestDocumentId,
            revision,
            [
                Visual("test:source-visual", "test:source", VisualPlacementMode.Manual),
                Visual("test:target-visual", "test:target", VisualPlacementMode.Pinned),
                Visual(
                    "test:edge-visual",
                    "test:edge",
                    route: [new PointD(10d, 20d), new PointD(110d, 20d)]),
            ]);

        return Snapshot(revision, semantic, visual);
    }

    private static DocumentSnapshot Snapshot(
        DocumentRevision revision,
        SemanticModelSnapshot semantic,
        VisualModelSnapshot visual) =>
        new(
            semantic,
            visual,
            new DocumentMetadataSnapshot(
                TestDocumentId,
                revision,
                [new("test:schema", PropertyValue.FromText("1"))],
                [new("test:extension", PropertyValue.FromText("value"))]));

    private static SemanticElementSnapshot Element(string id) =>
        new(new SemanticElementId(id), NodeTypeId);

    private static SemanticRelationshipSnapshot Relationship(
        string id,
        string sourceId,
        string targetId) =>
        new(
            new SemanticElementId(id),
            RelationshipTypeId,
            new SemanticElementId(sourceId),
            new SemanticElementId(targetId));

    private static VisualStateSnapshot Visual(
        string id,
        string semanticId,
        VisualPlacementMode placementMode = VisualPlacementMode.Automatic,
        IEnumerable<PointD>? route = null) =>
        new(
            new VisualStateId(id),
            new SemanticElementId(semanticId),
            new PointD(10d, 20d),
            new SizeD(100d, 60d),
            placementMode,
            route,
            [new("test:style", PropertyValue.FromText("default"))]);

    private static PropertyMap PlacementProperties(PropertyValue pathPosition) =>
        new(
        [
            new(ConnectorLabelPlacement.PathPositionPropertyKey, pathPosition),
            new(
                ConnectorLabelPlacement.OffsetXPropertyKey,
                PropertyValue.FromNumber(0d)),
            new(
                ConnectorLabelPlacement.OffsetYPropertyKey,
                PropertyValue.FromNumber(-12d)),
        ]);

    private sealed record InvalidPlacementCase(
        string VisualId,
        string SemanticId,
        PropertyMap Properties);

    private static string DiagnosticSignature(Diagnostic diagnostic) =>
        string.Join(
            "|",
            diagnostic.Code,
            diagnostic.SourceIdentity,
            string.Join(",", diagnostic.Context.Select(pair => $"{pair.Key}={pair.Value}")));
}
