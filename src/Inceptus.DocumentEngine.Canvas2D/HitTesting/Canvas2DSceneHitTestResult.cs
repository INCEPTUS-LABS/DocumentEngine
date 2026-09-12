using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Canvas2D.HitTesting;

/// <summary>
/// Immutable identity and traceability returned by one document-space scene hit test.
/// </summary>
public sealed class Canvas2DSceneHitTestResult
{
    internal Canvas2DSceneHitTestResult(Canvas2DSceneItem item, PointD documentPoint)
    {
        SceneObjectId = item.Id;
        Origin = item.Origin;
        DocumentPoint = documentPoint;
        SpatialRegion = item.SpatialRegion;
        PresentedScopePoint = item.SpatialRegion?.MapSceneToLocal(documentPoint) ??
            documentPoint;
    }

    public SceneObjectId SceneObjectId { get; }

    public Canvas2DSceneOriginTrace Origin { get; }

    public PointD DocumentPoint { get; }

    /// <summary>
    /// Gets the optional presentation instance hit by the pointer.
    /// </summary>
    public Canvas2DSpatialRegion? SpatialRegion { get; }

    /// <summary>
    /// Gets the pointer position mapped into the presented scope's Process-local document
    /// coordinate system. For canonical items this equals <see cref="DocumentPoint"/>.
    /// </summary>
    public PointD PresentedScopePoint { get; }
}
