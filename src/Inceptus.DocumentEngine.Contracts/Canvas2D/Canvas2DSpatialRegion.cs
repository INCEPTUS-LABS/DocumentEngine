using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Describes one transient region that translates canonical Process coordinates into the final
/// single-scope Scene. The optional container identity is provenance only and never owns Process
/// content or geometry.
/// </summary>
public sealed class Canvas2DSpatialRegion : IEquatable<Canvas2DSpatialRegion>
{
    private readonly Matrix2D _sceneToLocalTransform;

    public Canvas2DSpatialRegion(
        Canvas2DSpatialRegionId id,
        ModelProfileId modelProfileId,
        SemanticElementId? containerSemanticElementId,
        Matrix2D localToSceneTransform,
        RectD bounds)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(modelProfileId);
        if (!IsTranslation(localToSceneTransform) ||
            !localToSceneTransform.TryInvert(out _sceneToLocalTransform))
        {
            throw new ArgumentException(
                "A spatial region requires an invertible translation-only transform.",
                nameof(localToSceneTransform));
        }

        Id = id;
        ModelProfileId = modelProfileId;
        ContainerSemanticElementId = containerSemanticElementId;
        LocalToSceneTransform = localToSceneTransform;
        Bounds = bounds;
    }

    public Canvas2DSpatialRegionId Id { get; }

    public ModelProfileId ModelProfileId { get; }

    public SemanticElementId? ContainerSemanticElementId { get; }

    public Matrix2D LocalToSceneTransform { get; }

    /// <summary>
    /// Gets the final Scene-coordinate bounds of this presentation region.
    /// </summary>
    public RectD Bounds { get; }

    public PointD MapLocalToScene(PointD point) => LocalToSceneTransform.TransformPoint(point);

    public PointD MapSceneToLocal(PointD point) => _sceneToLocalTransform.TransformPoint(point);

    public RectD MapLocalToScene(RectD bounds) => bounds.Translate(new VectorD(
        LocalToSceneTransform.OffsetX,
        LocalToSceneTransform.OffsetY));

    public RectD MapSceneToLocal(RectD bounds) => bounds.Translate(new VectorD(
        _sceneToLocalTransform.OffsetX,
        _sceneToLocalTransform.OffsetY));

    public bool Equals(Canvas2DSpatialRegion? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id &&
        ModelProfileId == other.ModelProfileId &&
        ContainerSemanticElementId == other.ContainerSemanticElementId &&
        LocalToSceneTransform == other.LocalToSceneTransform &&
        Bounds == other.Bounds;

    public override bool Equals(object? obj) => Equals(obj as Canvas2DSpatialRegion);

    public override int GetHashCode() => HashCode.Combine(
        Id,
        ModelProfileId,
        ContainerSemanticElementId,
        LocalToSceneTransform,
        Bounds);

    private static bool IsTranslation(Matrix2D transform) =>
        transform.M11 == 1d &&
        transform.M12 == 0d &&
        transform.M21 == 0d &&
        transform.M22 == 1d;
}
