using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>Authored region capacity and its resolved expanded frame, absent while dormant.</summary>
public sealed record SpatialRegionGeometrySnapshot
{
    public SpatialRegionGeometrySnapshot(
        Canvas2DSpatialRegionId id,
        ModelProfileId profileId,
        SemanticElementId? containerSemanticElementId,
        double expandedHeight,
        Matrix2D? localToScopeTransform,
        RectD? contentBounds)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(profileId);
        if (!double.IsFinite(expandedHeight) || expandedHeight <= 0d)
            throw new ArgumentOutOfRangeException(nameof(expandedHeight));
        if (localToScopeTransform.HasValue != contentBounds.HasValue)
            throw new ArgumentException("An active region requires both transform and bounds; dormant regions have neither.");
        if (localToScopeTransform is { } transform &&
            (transform.M11 != 1d || transform.M22 != 1d || transform.M12 != 0d || transform.M21 != 0d))
            throw new ArgumentException("Spatial region transforms must be translations.", nameof(localToScopeTransform));
        if (contentBounds is { } bounds && (bounds.Height != expandedHeight || bounds.Width <= 0d))
            throw new ArgumentException("Expanded bounds must have the authored height and positive width.", nameof(contentBounds));
        Id = id;
        ProfileId = profileId;
        ContainerSemanticElementId = containerSemanticElementId;
        ExpandedHeight = expandedHeight;
        LocalToScopeTransform = localToScopeTransform;
        ContentBounds = contentBounds;
    }

    public Canvas2DSpatialRegionId Id { get; }
    public ModelProfileId ProfileId { get; }
    public SemanticElementId? ContainerSemanticElementId { get; }
    public double ExpandedHeight { get; }
    public Matrix2D? LocalToScopeTransform { get; }
    public RectD? ContentBounds { get; }
    public bool IsActive => LocalToScopeTransform.HasValue;
}
