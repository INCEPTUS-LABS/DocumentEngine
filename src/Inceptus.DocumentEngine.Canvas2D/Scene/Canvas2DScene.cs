using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

/// <summary>
/// Immutable, transient Canvas2D rendering plan constructed from one compatible pipeline input set.
/// </summary>
public sealed class Canvas2DScene : IEquatable<Canvas2DScene>, IDisposable
{
    internal Canvas2DScene(
        DocumentId documentId,
        DocumentRevision sourceRevision,
        AlgorithmId layoutAlgorithmId,
        AlgorithmId routingAlgorithmId,
        Canvas2DSceneConfiguration configuration,
        IEnumerable<Canvas2DSceneContributorDescriptor> contributors,
        ViewportSnapshot viewport,
        Matrix2D viewportTransform,
        string? activeToolId,
        string? focusTargetId,
        PropertyMap toolState,
        PropertyMap contributorMetadata,
        IEnumerable<Canvas2DSceneItem> items,
        IEnumerable<Diagnostic>? diagnostics = null,
        Canvas2DSpatialPresentationPlan? spatialPresentationPlan = null,
        Canvas2DScenePanReuseSource? panReuseSource = null,
        Canvas2DBoundaryGuidePresentation? boundaryGuides = null,
        Canvas2DBoundedPresentation? movePreview = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(layoutAlgorithmId);
        ArgumentNullException.ThrowIfNull(routingAlgorithmId);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(contributors);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(toolState);
        ArgumentNullException.ThrowIfNull(contributorMetadata);
        ArgumentNullException.ThrowIfNull(items);

        DocumentId = documentId;
        SourceRevision = sourceRevision;
        LayoutAlgorithmId = layoutAlgorithmId;
        RoutingAlgorithmId = routingAlgorithmId;
        Configuration = configuration;
        Contributors = contributors.ToImmutableArray();
        Viewport = viewport;
        ViewportTransform = viewportTransform;
        ActiveToolId = activeToolId;
        FocusTargetId = focusTargetId;
        ToolState = toolState;
        ContributorMetadata = contributorMetadata;
        Items = items.ToImmutableArray();
        SpatialPresentationPlan = spatialPresentationPlan;
        PanReuseSource = panReuseSource;
        BoundaryGuides = boundaryGuides ?? Canvas2DBoundaryGuidePresentation.Empty;
        BoundedPresentation = movePreview;
        Diagnostics = Canvas2DSceneDiagnosticCollection.CopyAndOrder(
            diagnostics,
            nameof(diagnostics));
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision SourceRevision { get; }

    public AlgorithmId LayoutAlgorithmId { get; }

    public AlgorithmId RoutingAlgorithmId { get; }

    public Canvas2DSceneConfiguration Configuration { get; }

    public ImmutableArray<Canvas2DSceneContributorDescriptor> Contributors { get; }

    public ViewportSnapshot Viewport { get; }

    public Matrix2D ViewportTransform { get; }

    public string? ActiveToolId { get; }

    public string? FocusTargetId { get; }

    public PropertyMap ToolState { get; }

    public PropertyMap ContributorMetadata { get; }

    public ImmutableArray<Canvas2DSceneItem> Items { get; }

    /// <summary>
    /// Gets the optional transient plan that spatially presents the one canonical active-scope
    /// Process Scene. The plan never duplicates Process or Visual State identities.
    /// </summary>
    public Canvas2DSpatialPresentationPlan? SpatialPresentationPlan { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public int ItemCount => Items.Length;

    internal Canvas2DScenePanReuseSource? PanReuseSource { get; }

    internal Canvas2DBoundaryGuidePresentation BoundaryGuides { get; }

    internal Canvas2DBoundedPresentation? BoundedPresentation { get; }

    internal ImmutableArray<Canvas2DSceneItem> RenderContent => BoundedPresentation?.Source.Items ?? Items;

    internal Canvas2DScene WithBoundedPresentation(
        Canvas2DBoundedPresentation presentation,
        ImmutableArray<Canvas2DSceneItem> items,
        Canvas2DScenePanReuseSource? panSource) =>
        new(this, presentation, items, panSource);

    private Canvas2DScene(Canvas2DScene previous, Canvas2DBoundedPresentation presentation,
        ImmutableArray<Canvas2DSceneItem> items, Canvas2DScenePanReuseSource? panSource)
    {
        DocumentId = previous.DocumentId;
        SourceRevision = previous.SourceRevision;
        LayoutAlgorithmId = previous.LayoutAlgorithmId;
        RoutingAlgorithmId = previous.RoutingAlgorithmId;
        Configuration = previous.Configuration;
        Contributors = previous.Contributors;
        Viewport = presentation.EditorState.Viewport;
        ViewportTransform = previous.ViewportTransform;
        ActiveToolId = previous.ActiveToolId;
        FocusTargetId = previous.FocusTargetId;
        ToolState = previous.ToolState;
        ContributorMetadata = previous.ContributorMetadata;
        Items = items;
        BoundaryGuides = previous.BoundaryGuides;
        SpatialPresentationPlan = previous.SpatialPresentationPlan;
        Diagnostics = previous.Diagnostics;
        PanReuseSource = panSource;
        BoundedPresentation = presentation;
    }

    internal Canvas2DScene WithPan(
        Canvas2DScenePanReuseSource source,
        Canvas2DBoundaryGuidePresentation boundaryGuides) => new(this, source, boundaryGuides);

    private Canvas2DScene(
        Canvas2DScene previous,
        Canvas2DScenePanReuseSource source,
        Canvas2DBoundaryGuidePresentation boundaryGuides)
    {
        DocumentId = previous.DocumentId;
        SourceRevision = previous.SourceRevision;
        LayoutAlgorithmId = previous.LayoutAlgorithmId;
        RoutingAlgorithmId = previous.RoutingAlgorithmId;
        Configuration = previous.Configuration;
        Contributors = previous.Contributors;
        Viewport = source.EditorState.Viewport;
        ViewportTransform = Canvas2DSceneBuilder.CreateViewportTransform(Viewport);
        ActiveToolId = previous.ActiveToolId;
        FocusTargetId = previous.FocusTargetId;
        ToolState = previous.ToolState;
        ContributorMetadata = previous.ContributorMetadata;
        Items = previous.Items;
        BoundaryGuides = boundaryGuides;
        SpatialPresentationPlan = previous.SpatialPresentationPlan;
        Diagnostics = previous.Diagnostics;
        PanReuseSource = source;
        BoundedPresentation = previous.BoundedPresentation is { } move
            ? move with { EditorState = source.EditorState }
            : null;
    }

    internal Canvas2DScene RebindToCommittedRevision(
        DocumentId documentId,
        DocumentRevision previousRevision,
        DocumentRevision committedRevision)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        if (DocumentId != documentId || SourceRevision != previousRevision)
        {
            throw new ArgumentException(
                "Only a Scene from the previous Document revision can be rebound.",
                nameof(previousRevision));
        }

        if (committedRevision != previousRevision.Increment())
        {
            throw new ArgumentException(
                "The rebound Scene revision must be exactly one revision after the previous revision.",
                nameof(committedRevision));
        }

        return new Canvas2DScene(
            documentId,
            committedRevision,
            LayoutAlgorithmId,
            RoutingAlgorithmId,
            Configuration,
            Contributors,
            Viewport,
            ViewportTransform,
            ActiveToolId,
            FocusTargetId,
            ToolState,
            ContributorMetadata,
            Items,
            Diagnostics,
            SpatialPresentationPlan,
            boundaryGuides: BoundaryGuides);
    }

    public void Dispose()
    {
        // The immutable plan currently owns no live resources. Disposal is intentionally idempotent.
    }

    public bool Equals(Canvas2DScene? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        DocumentId == other.DocumentId &&
        SourceRevision == other.SourceRevision &&
        LayoutAlgorithmId == other.LayoutAlgorithmId &&
        RoutingAlgorithmId == other.RoutingAlgorithmId &&
        Configuration.Equals(other.Configuration) &&
        Contributors.AsSpan().SequenceEqual(other.Contributors.AsSpan()) &&
        Viewport.Equals(other.Viewport) &&
        ViewportTransform == other.ViewportTransform &&
        StringComparer.Ordinal.Equals(ActiveToolId, other.ActiveToolId) &&
        StringComparer.Ordinal.Equals(FocusTargetId, other.FocusTargetId) &&
        ToolState.Equals(other.ToolState) &&
        ContributorMetadata.Equals(other.ContributorMetadata) &&
        Items.AsSpan().SequenceEqual(other.Items.AsSpan()) &&
        BoundaryGuides.HasSamePresentation(other.BoundaryGuides) &&
        Equals(SpatialPresentationPlan, other.SpatialPresentationPlan) &&
        Canvas2DSceneDiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as Canvas2DScene);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId);
        hash.Add(SourceRevision);
        hash.Add(LayoutAlgorithmId);
        hash.Add(RoutingAlgorithmId);
        hash.Add(Configuration);
        foreach (var contributor in Contributors)
        {
            hash.Add(contributor);
        }

        hash.Add(Viewport);
        hash.Add(ViewportTransform);
        hash.Add(ActiveToolId, StringComparer.Ordinal);
        hash.Add(FocusTargetId, StringComparer.Ordinal);
        hash.Add(ToolState);
        hash.Add(ContributorMetadata);
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        hash.Add(SpatialPresentationPlan);
        hash.Add(BoundaryGuides.HorizontalContentIndex);
        hash.Add(BoundaryGuides.VerticalContentIndex);
        foreach (var guide in BoundaryGuides.Items)
        {
            hash.Add(guide);
        }

        Canvas2DSceneDiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }

}
