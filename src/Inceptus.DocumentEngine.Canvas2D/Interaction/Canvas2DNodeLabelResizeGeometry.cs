using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

internal static class Canvas2DNodeLabelResizeGeometry
{
    // InteractionBounds limits corner extent to half the shortest label side.
    // Two full corner extents retain the existing hit geometry and a usable body.
    // This is an interaction constraint, not the persisted override's validity floor.
    internal const double MinimumWidth = 2d * Canvas2DResizeGestureMetadata.HandleExtent;
    internal const double MinimumHeight = 2d * Canvas2DResizeGestureMetadata.HandleExtent;

    internal static RectD CalculateBounds(RectD original, VectorD delta, Canvas2DResizeDirection direction)
    {
        var bounds = Canvas2DResizeGeometry.CalculateBounds(original, delta, direction, MinimumWidth, MinimumHeight);
        // Previously saved small boxes remain valid. Their first resize also lifts
        // an untouched undersized dimension, retaining its top/left anchor.
        return new RectD(bounds.X, bounds.Y, Math.Max(bounds.Width, MinimumWidth), Math.Max(bounds.Height, MinimumHeight));
    }
}
