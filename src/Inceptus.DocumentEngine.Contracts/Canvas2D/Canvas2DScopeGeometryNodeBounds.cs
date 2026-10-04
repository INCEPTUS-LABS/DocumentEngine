using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Measured transient node extents in the node's region-local coordinates. Body bounds are the
/// complete placement/capacity authority; caption bounds inform only initial frame defaults.
/// </summary>
public sealed record Canvas2DScopeGeometryNodeBounds
{
    public Canvas2DScopeGeometryNodeBounds(
        VisualStateId visualStateId,
        RectD bodyBounds,
        RectD? captionBounds = null)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        VisualStateId = visualStateId;
        BodyBounds = bodyBounds;
        CaptionBounds = captionBounds;
    }

    public VisualStateId VisualStateId { get; }
    public RectD BodyBounds { get; }
    public RectD? CaptionBounds { get; }
}
