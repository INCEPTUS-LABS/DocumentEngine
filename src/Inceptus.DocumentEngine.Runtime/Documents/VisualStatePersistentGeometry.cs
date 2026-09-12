using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Documents;

/// <summary>
/// Identifies when a Visual State's persistent position and size are the authoritative
/// node rectangle rather than an input hint for the selected Layout Algorithm.
/// </summary>
internal static class VisualStatePersistentGeometry
{
    internal static bool TryResolveAuthoritativeNodeBounds(
        VisualStateSnapshot visualState,
        out RectD bounds)
    {
        ArgumentNullException.ThrowIfNull(visualState);
        bounds = new RectD(
            visualState.Position.X,
            visualState.Position.Y,
            visualState.Size.Width,
            visualState.Size.Height);
        return visualState.PlacementMode == VisualPlacementMode.Pinned;
    }
}
