using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

/// <summary>Reads the unique current presentation of a canonical visual; stores no selection.</summary>
public static class Canvas2DSpatialLookup
{
    public static Canvas2DSpatialRegion? GetVisualRegion(this Canvas2DScene scene, VisualStateId? visualId)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return visualId is null ? null : scene.Items.FirstOrDefault(item =>
            item.Origin.VisualStateId == visualId && item.IsVisible &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0)?.SpatialRegion;
    }
}
