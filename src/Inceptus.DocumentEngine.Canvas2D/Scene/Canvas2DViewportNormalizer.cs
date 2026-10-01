using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

/// <summary>Normalizes an observed editor viewport against the Document origin only.</summary>
internal static class Canvas2DViewportNormalizer
{
    internal static ViewportSnapshot Normalize(
        ViewportSnapshot candidate,
        Canvas2DSurfaceSize surface)
    {
        var region = Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(candidate, surface);
        var correctX = region.Left < DocumentGeometryBoundary.MinimumX;
        var correctY = region.Top < DocumentGeometryBoundary.MinimumY;
        var pan = candidate.Pan;
        if (correctX || correctY)
        {
            var transform = Canvas2DSceneBuilder.CreateViewportTransform(candidate);
            var correction = transform.TransformVector(new VectorD(
                correctX ? DocumentGeometryBoundary.MinimumX - region.Left : 0d,
                correctY ? DocumentGeometryBoundary.MinimumY - region.Top : 0d));
            pan -= correction;

            // Inverse/forward scale multiplication can leave a few ulps after subtraction.
            // Align only the corrected origin axes to CSS zero using the same transform.
            // This exact cancellation needs no visible epsilon or guessed Pan sign.
            var corrected = new ViewportSnapshot(candidate.Zoom, pan);
            var origin = Canvas2DSceneBuilder.CreateViewportTransform(corrected).TransformPoint(
                new PointD(DocumentGeometryBoundary.MinimumX, DocumentGeometryBoundary.MinimumY));
            pan = new VectorD(
                correctX ? pan.X - origin.X : pan.X,
                correctY ? pan.Y - origin.Y : pan.Y);
            region = Canvas2DSceneBuilder.CalculateVisibleDocumentRegion(
                new ViewportSnapshot(candidate.Zoom, pan), surface);
        }

        return candidate.Pan == pan && candidate.VisibleDocumentRegion == region
            ? candidate
            : new ViewportSnapshot(candidate.Zoom, pan, region);
    }
}
