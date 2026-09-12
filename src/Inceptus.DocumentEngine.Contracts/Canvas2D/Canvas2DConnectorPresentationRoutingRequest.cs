using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Immutable canonical and final-presentation inputs for one pure connector presentation route.
/// </summary>
public sealed class Canvas2DConnectorPresentationRoutingRequest
{
    public Canvas2DConnectorPresentationRoutingRequest(
        ProjectedEdge edge,
        IEnumerable<PointD> canonicalLogicalPath,
        IEnumerable<PointD> canonicalEditablePath,
        PointD displayedSourceAnchor,
        PointD displayedTargetAnchor,
        Canvas2DSpatialRegion? sourceRegion,
        Canvas2DSpatialRegion? targetRegion,
        Matrix2D canonicalGuidanceToSceneTransform,
        IEnumerable<RectD>? presentedObstacles = null,
        bool isNoRouteFallback = false)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(canonicalLogicalPath);
        ArgumentNullException.ThrowIfNull(canonicalEditablePath);
        var logicalPath = canonicalLogicalPath.ToImmutableArray();
        var editablePath = canonicalEditablePath.ToImmutableArray();
        if (logicalPath.Length < 2 || editablePath.Length < 2)
        {
            throw new ArgumentException(
                "Connector presentation routing requires canonical logical and editable paths.",
                nameof(canonicalLogicalPath));
        }

        if (!IsTranslation(canonicalGuidanceToSceneTransform) ||
            !canonicalGuidanceToSceneTransform.TryInvert(out _))
        {
            throw new ArgumentException(
                "Connector presentation guidance requires an invertible translation-only transform.",
                nameof(canonicalGuidanceToSceneTransform));
        }

        Edge = edge;
        CanonicalLogicalPath = logicalPath;
        CanonicalEditablePath = editablePath;
        DisplayedSourceAnchor = displayedSourceAnchor;
        DisplayedTargetAnchor = displayedTargetAnchor;
        SourceRegion = sourceRegion;
        TargetRegion = targetRegion;
        CanonicalGuidanceToSceneTransform = canonicalGuidanceToSceneTransform;
        PresentedObstacles = presentedObstacles?.ToImmutableArray() ?? [];
        IsNoRouteFallback = isNoRouteFallback;
    }

    public ProjectedEdge Edge { get; }

    public ImmutableArray<PointD> CanonicalLogicalPath { get; }

    public ImmutableArray<PointD> CanonicalEditablePath { get; }

    public PointD DisplayedSourceAnchor { get; }

    public PointD DisplayedTargetAnchor { get; }

    public Canvas2DSpatialRegion? SourceRegion { get; }

    public Canvas2DSpatialRegion? TargetRegion { get; }

    public Matrix2D CanonicalGuidanceToSceneTransform { get; }

    public ImmutableArray<RectD> PresentedObstacles { get; }

    public bool IsNoRouteFallback { get; }

    private static bool IsTranslation(Matrix2D transform) =>
        transform.M11 == 1d &&
        transform.M12 == 0d &&
        transform.M21 == 0d &&
        transform.M22 == 1d;
}
