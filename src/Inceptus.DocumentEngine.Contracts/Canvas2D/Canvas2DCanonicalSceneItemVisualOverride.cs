using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Immutable visual-only replacement for one framework-composed canonical Scene item.
/// </summary>
/// <remarks>
/// The Scene Builder retains the target item's canonical identity, origin trace, transform,
/// bounds, ordering, visibility, persistent appearance, and framework-owned interaction
/// metadata and hit-test policy. This contract can replace only geometry and style and therefore
/// cannot create another logical item or change interaction ownership.
/// </remarks>
public sealed class Canvas2DCanonicalSceneItemVisualOverride :
    IEquatable<Canvas2DCanonicalSceneItemVisualOverride>
{
    public Canvas2DCanonicalSceneItemVisualOverride(
        SceneObjectId targetSceneObjectId,
        Canvas2DSceneGeometry geometry,
        Canvas2DSceneStyle style)
    {
        ArgumentNullException.ThrowIfNull(targetSceneObjectId);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(style);

        TargetSceneObjectId = targetSceneObjectId;
        Geometry = geometry;
        Style = style;
    }

    public SceneObjectId TargetSceneObjectId { get; }

    public Canvas2DSceneGeometry Geometry { get; }

    public Canvas2DSceneStyle Style { get; }

    public bool Equals(Canvas2DCanonicalSceneItemVisualOverride? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        TargetSceneObjectId == other.TargetSceneObjectId &&
        Geometry.Equals(other.Geometry) &&
        Style.Equals(other.Style);

    public override bool Equals(object? obj) =>
        Equals(obj as Canvas2DCanonicalSceneItemVisualOverride);

    public override int GetHashCode() => HashCode.Combine(
        TargetSceneObjectId,
        Geometry,
        Style);
}
