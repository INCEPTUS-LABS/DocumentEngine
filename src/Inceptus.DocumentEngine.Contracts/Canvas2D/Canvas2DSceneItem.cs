using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

public sealed class Canvas2DSceneItem : IEquatable<Canvas2DSceneItem>
{
    public Canvas2DSceneItem(
        SceneObjectId id,
        Canvas2DSceneLayer layer,
        int zIndex,
        Canvas2DSceneGeometry geometry,
        Canvas2DSceneOriginTrace origin,
        Matrix2D? transform = null,
        RectD? clip = null,
        Canvas2DSceneStyle? style = null,
        bool isVisible = true,
        Canvas2DHitTestPolicy? hitTestPolicy = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? persistentAppearance = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? metadata = null,
        RectD? bounds = null,
        Canvas2DSpatialRegion? spatialRegion = null,
        Canvas2DConnectorPresentationMapping? connectorPresentationMapping = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(origin);
        if (!Enum.IsDefined(layer))
        {
            throw new ArgumentOutOfRangeException(nameof(layer), layer, "The scene layer must be defined.");
        }

        Id = id;
        Layer = layer;
        ZIndex = zIndex;
        Geometry = geometry;
        Transform = transform ?? Matrix2D.Identity;
        Bounds = bounds ?? TransformBounds(geometry.Bounds, Transform);
        Clip = clip;
        Style = style ?? Canvas2DSceneStyle.Default;
        IsVisible = isVisible;
        HitTestPolicy = hitTestPolicy ?? Canvas2DHitTestPolicy.None;
        Origin = origin;
        PersistentAppearance = new PropertyMap(persistentAppearance);
        Metadata = new PropertyMap(metadata);
        SpatialRegion = spatialRegion;
        ConnectorPresentationMapping = connectorPresentationMapping;
    }

    public SceneObjectId Id { get; }
    public Canvas2DSceneLayer Layer { get; }
    public int ZIndex { get; }
    public Canvas2DSceneGeometry Geometry { get; }

    /// <summary>
    /// Gets the final document-coordinate bounds of the item after its item transform is applied.
    /// </summary>
    public RectD Bounds { get; }

    public Matrix2D Transform { get; }
    /// <summary>
    /// Gets the optional document-coordinate clipping rectangle for the final transformed item.
    /// </summary>
    public RectD? Clip { get; }
    public Canvas2DSceneStyle Style { get; }
    public bool IsVisible { get; }
    public Canvas2DHitTestPolicy HitTestPolicy { get; }
    public Canvas2DSceneOriginTrace Origin { get; }
    public PropertyMap PersistentAppearance { get; }
    public PropertyMap Metadata { get; }

    /// <summary>
    /// Gets the optional region that uniquely places this canonical Visual State in a
    /// single-scope spatial presentation.
    /// </summary>
    public Canvas2DSpatialRegion? SpatialRegion { get; }

    /// <summary>
    /// Gets the optional reversible mapping for one derived connector presentation.
    /// </summary>
    public Canvas2DConnectorPresentationMapping? ConnectorPresentationMapping { get; }

    public bool Equals(Canvas2DSceneItem? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Id == other.Id && Layer == other.Layer && ZIndex == other.ZIndex &&
        Geometry.Equals(other.Geometry) && Bounds == other.Bounds &&
        Transform == other.Transform && Clip == other.Clip &&
        Style.Equals(other.Style) && IsVisible == other.IsVisible &&
        HitTestPolicy.Equals(other.HitTestPolicy) && Origin.Equals(other.Origin) &&
        PersistentAppearance.Equals(other.PersistentAppearance) && Metadata.Equals(other.Metadata) &&
        Equals(SpatialRegion, other.SpatialRegion) &&
        Equals(ConnectorPresentationMapping, other.ConnectorPresentationMapping);

    public override bool Equals(object? obj) => Equals(obj as Canvas2DSceneItem);

    public override int GetHashCode() => HashCode.Combine(
        HashCode.Combine(Id, Layer, ZIndex, Geometry, Bounds, Transform, Clip),
        HashCode.Combine(
            Style,
            IsVisible,
            HitTestPolicy,
            Origin,
            PersistentAppearance,
            Metadata,
            SpatialRegion,
            ConnectorPresentationMapping));

    private static RectD TransformBounds(RectD bounds, Matrix2D transform)
    {
        if (!IsFinite(bounds) || !IsFinite(transform))
        {
            // Malformed instances can only arrive through an untrusted implementation boundary.
            // Preserve the invalid value so the framework Scene Builder can report a diagnostic.
            return bounds;
        }

        var topLeft = transform.TransformPoint(bounds.TopLeft);
        var topRight = transform.TransformPoint(new PointD(bounds.Right, bounds.Top));
        var bottomLeft = transform.TransformPoint(new PointD(bounds.Left, bounds.Bottom));
        var bottomRight = transform.TransformPoint(new PointD(bounds.Right, bounds.Bottom));
        var minimumX = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
        var minimumY = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
        var maximumX = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
        var maximumY = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));
        return new RectD(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY);
    }

    private static bool IsFinite(RectD bounds) =>
        double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) &&
        double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) &&
        bounds.Width >= 0d && bounds.Height >= 0d &&
        double.IsFinite(bounds.Right) && double.IsFinite(bounds.Bottom);

    private static bool IsFinite(Matrix2D transform) =>
        double.IsFinite(transform.M11) && double.IsFinite(transform.M12) &&
        double.IsFinite(transform.M21) && double.IsFinite(transform.M22) &&
        double.IsFinite(transform.OffsetX) && double.IsFinite(transform.OffsetY);
}
