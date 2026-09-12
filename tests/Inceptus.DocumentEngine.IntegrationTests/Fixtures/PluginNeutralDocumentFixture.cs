using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests.Fixtures;

internal sealed class PluginNeutralDocumentFixture
{
    private readonly List<KeyValuePair<string, PropertyValue>> _alphaProperties =
    [
        new("test:label", PropertyValue.FromText("Alpha")),
    ];

    private readonly List<PointD> _persistentRoute =
    [
        new(120d, 40d),
        new(180d, 40d),
        new(180d, 120d),
    ];

    private readonly List<KeyValuePair<string, PropertyValue>> _systemMetadata =
    [
        new("test:format-version", PropertyValue.FromInteger(1)),
    ];

    private readonly List<KeyValuePair<string, PropertyValue>> _extensionMetadata =
    [
        new("test:extension-setting", PropertyValue.FromBoolean(true)),
    ];

    public PluginNeutralDocumentFixture()
    {
        AlphaId = new SemanticElementId("test:element-alpha");
        BetaId = new SemanticElementId("test:element-beta");
        GammaId = new SemanticElementId("test:element-gamma");
        AlphaToBetaId = new SemanticElementId("test:relationship-alpha-beta");
        BetaToGammaId = new SemanticElementId("test:relationship-beta-gamma");

        Elements =
        [
            new SemanticElementSnapshot(
                AlphaId,
                new SemanticTypeId("test:node"),
                _alphaProperties),
            new SemanticElementSnapshot(BetaId, new SemanticTypeId("test:node")),
            new SemanticElementSnapshot(GammaId, new SemanticTypeId("test:node")),
        ];

        Relationships =
        [
            new SemanticRelationshipSnapshot(
                AlphaToBetaId,
                new SemanticTypeId("test:edge"),
                AlphaId,
                BetaId),
            new SemanticRelationshipSnapshot(
                BetaToGammaId,
                new SemanticTypeId("test:edge"),
                BetaId,
                GammaId),
        ];

        VisualStates =
        [
            new VisualStateSnapshot(
                new VisualStateId("test:visual-alpha"),
                AlphaId,
                new PointD(20d, 20d),
                new SizeD(100d, 40d),
                VisualPlacementMode.Manual),
            new VisualStateSnapshot(
                new VisualStateId("test:visual-beta"),
                BetaId,
                new PointD(180d, 100d),
                new SizeD(120d, 60d),
                VisualPlacementMode.Pinned),
            new VisualStateSnapshot(
                new VisualStateId("test:visual-alpha-beta"),
                AlphaToBetaId,
                new PointD(0d, 0d),
                new SizeD(0d, 0d),
                VisualPlacementMode.Automatic,
                _persistentRoute),
        ];
    }

    public DocumentId DocumentId { get; } = new("test:phase-b2-document");

    public SemanticElementId AlphaId { get; }

    public SemanticElementId BetaId { get; }

    public SemanticElementId GammaId { get; }

    public SemanticElementId AlphaToBetaId { get; }

    public SemanticElementId BetaToGammaId { get; }

    public List<SemanticElementSnapshot> Elements { get; }

    public List<SemanticRelationshipSnapshot> Relationships { get; }

    public List<VisualStateSnapshot> VisualStates { get; }

    public DocumentSnapshot CreateSnapshot(DocumentRevision revision) =>
        new(
            new SemanticModelSnapshot(DocumentId, revision, Elements, Relationships),
            new VisualModelSnapshot(DocumentId, revision, VisualStates),
            new DocumentMetadataSnapshot(
                DocumentId,
                revision,
                _systemMetadata,
                _extensionMetadata));

    public void MutateCallerOwnedCollections()
    {
        Elements.Clear();
        Relationships.Clear();
        VisualStates.Clear();
        _alphaProperties.Clear();
        _persistentRoute.Clear();
        _systemMetadata.Clear();
        _extensionMetadata.Clear();
    }
}
