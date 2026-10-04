using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Immutable transient plan that partitions one canonical Process Scene into uniquely placed
/// regions without copying Process or Visual State identities.
/// </summary>
public sealed class Canvas2DSpatialPresentationPlan : IEquatable<Canvas2DSpatialPresentationPlan>
{
    private readonly Matrix2D _sceneToCanonicalGuidanceTransform;
    private readonly Dictionary<Canvas2DSpatialRegionId, Canvas2DSpatialRegion> _regionsById;
    private readonly Dictionary<VisualStateId, Canvas2DSpatialVisualPlacement>
        _placementsByVisualStateId;

    public Canvas2DSpatialPresentationPlan(
        IEnumerable<Canvas2DSpatialRegion> regions,
        IEnumerable<Canvas2DSpatialVisualPlacement> visualPlacements,
        Matrix2D canonicalGuidanceToSceneTransform)
        : this(regions, visualPlacements, canonicalGuidanceToSceneTransform,
            Canvas2DSpatialCoordinateMap.Identity, movementBottomBoundaryRegionId: null)
    {
    }

    public Canvas2DSpatialPresentationPlan(
        IEnumerable<Canvas2DSpatialRegion> regions,
        IEnumerable<Canvas2DSpatialVisualPlacement> visualPlacements,
        Matrix2D canonicalGuidanceToSceneTransform,
        Canvas2DSpatialCoordinateMap coordinateMap,
        Canvas2DSpatialRegionId? movementBottomBoundaryRegionId)
        : this(regions, visualPlacements, canonicalGuidanceToSceneTransform, coordinateMap,
            movementBottomBoundaryRegionId, [])
    {
    }

    public Canvas2DSpatialPresentationPlan(
        IEnumerable<Canvas2DSpatialRegion> regions,
        IEnumerable<Canvas2DSpatialVisualPlacement> visualPlacements,
        Matrix2D canonicalGuidanceToSceneTransform,
        Canvas2DSpatialCoordinateMap coordinateMap,
        Canvas2DSpatialRegionId? movementBottomBoundaryRegionId,
        IEnumerable<Canvas2DSpatialResizeTarget> resizeTargets)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(visualPlacements);
        ArgumentNullException.ThrowIfNull(coordinateMap);
        if (!IsTranslation(canonicalGuidanceToSceneTransform) ||
            !canonicalGuidanceToSceneTransform.TryInvert(
                out _sceneToCanonicalGuidanceTransform))
        {
            throw new ArgumentException(
                "Canonical guidance requires an invertible translation-only transform.",
                nameof(canonicalGuidanceToSceneTransform));
        }

        var regionCopy = regions.ToArray();
        if (Array.Exists(regionCopy, static region => region is null))
        {
            throw new ArgumentException(
                "Spatial presentation regions cannot contain null values.",
                nameof(regions));
        }

        Array.Sort(regionCopy, static (left, right) => StringComparer.Ordinal.Compare(
            left.Id.Value,
            right.Id.Value));
        for (var index = 1; index < regionCopy.Length; index++)
        {
            if (regionCopy[index - 1].Id == regionCopy[index].Id)
            {
                throw new ArgumentException(
                    $"Spatial region '{regionCopy[index].Id}' occurs more than once.",
                    nameof(regions));
            }
        }

        var placementCopy = visualPlacements.ToArray();
        if (Array.Exists(placementCopy, static placement => placement is null))
        {
            throw new ArgumentException(
                "Spatial visual placements cannot contain null values.",
                nameof(visualPlacements));
        }

        Array.Sort(placementCopy, static (left, right) => StringComparer.Ordinal.Compare(
            left.VisualStateId.Value,
            right.VisualStateId.Value));
        var regionIds = regionCopy.Select(static region => region.Id).ToHashSet();
        for (var index = 0; index < placementCopy.Length; index++)
        {
            if (!regionIds.Contains(placementCopy[index].RegionId))
            {
                throw new ArgumentException(
                    $"Visual placement '{placementCopy[index].VisualStateId}' targets missing " +
                    $"spatial region '{placementCopy[index].RegionId}'.",
                    nameof(visualPlacements));
            }

            if (index > 0 &&
                placementCopy[index - 1].VisualStateId == placementCopy[index].VisualStateId)
            {
                throw new ArgumentException(
                    $"Visual State '{placementCopy[index].VisualStateId}' has more than one " +
                    "spatial placement.",
                    nameof(visualPlacements));
            }
        }

        Regions = [.. regionCopy];
        VisualPlacements = [.. placementCopy];
        CanonicalGuidanceToSceneTransform = canonicalGuidanceToSceneTransform;
        _regionsById = Regions.ToDictionary(static region => region.Id);
        _placementsByVisualStateId = VisualPlacements.ToDictionary(
            static placement => placement.VisualStateId);
        if (movementBottomBoundaryRegionId is not null &&
            (!_regionsById.TryGetValue(movementBottomBoundaryRegionId, out var boundary) ||
             boundary.ContainerSemanticElementId is not null))
        {
            throw new ArgumentException(
                "The movement bottom boundary must identify a declared region without a semantic container.",
                nameof(movementBottomBoundaryRegionId));
        }
        if (!coordinateMap.Bands.IsEmpty &&
            (coordinateMap.Bands.Length != Regions.Length ||
             coordinateMap.Bands.Any(band =>
                 !_regionsById.TryGetValue(band.RegionId, out var region) ||
                 band.DisplayedTop != region.Bounds.Top || band.DisplayedBottom != region.Bounds.Bottom)))
        {
            throw new ArgumentException(
                "Coordinate bands must cover the exact current displayed regions.", nameof(coordinateMap));
        }
        CoordinateMap = coordinateMap;
        MovementBottomBoundaryRegionId = movementBottomBoundaryRegionId;
        ArgumentNullException.ThrowIfNull(resizeTargets);
        var copiedResizeTargets = resizeTargets.ToImmutableArray();
        if (copiedResizeTargets.Any(static target => target is null))
            throw new ArgumentException("Resize targets cannot contain null entries.", nameof(resizeTargets));
        ResizeTargets = copiedResizeTargets.OrderBy(static target => target.AcquiredRegionId.Value, StringComparer.Ordinal)
            .ThenBy(static target => target.Edge).ToImmutableArray();
        if (ResizeTargets.Any(target => !_regionsById.TryGetValue(target.AcquiredRegionId, out var region) ||
                region.ModelProfileId != target.ProfileId || target.AffectedRegionIds.Any(id =>
                    !_regionsById.TryGetValue(id, out var affected) || affected.ModelProfileId != target.ProfileId)) ||
            ResizeTargets.Select(static target => (target.AcquiredRegionId, target.Edge)).Distinct().Count() != ResizeTargets.Length)
            throw new ArgumentException("Resize targets require unique current region edges of the owning profile.", nameof(resizeTargets));
    }

    public ImmutableArray<Canvas2DSpatialRegion> Regions { get; }

    public ImmutableArray<Canvas2DSpatialVisualPlacement> VisualPlacements { get; }

    public Canvas2DSpatialCoordinateMap CoordinateMap { get; }

    public Canvas2DSpatialRegionId? MovementBottomBoundaryRegionId { get; }
    public ImmutableArray<Canvas2DSpatialResizeTarget> ResizeTargets { get; }

    /// <summary>
    /// Gets the one reversible translation used for relationship-owned manual guidance.
    /// Endpoint geometry is mapped through its owning visual region instead.
    /// </summary>
    public Matrix2D CanonicalGuidanceToSceneTransform { get; }

    public PointD MapCanonicalGuidanceToScene(PointD point) =>
        CanonicalGuidanceToSceneTransform.TransformPoint(point);

    public PointD MapSceneGuidanceToCanonical(PointD point) =>
        _sceneToCanonicalGuidanceTransform.TransformPoint(point);

    public PointD MapLogicalToScene(PointD point) => CoordinateMap.MapLogicalToScene(point);

    public PointD MapSceneToLogical(PointD point) => CoordinateMap.MapSceneToLogical(point);

    public bool TryGetRegion(
        Canvas2DSpatialRegionId regionId,
        [NotNullWhen(true)] out Canvas2DSpatialRegion? region)
    {
        ArgumentNullException.ThrowIfNull(regionId);
        return _regionsById.TryGetValue(regionId, out region);
    }

    public bool TryGetPlacement(
        VisualStateId visualStateId,
        [NotNullWhen(true)] out Canvas2DSpatialVisualPlacement? placement)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        return _placementsByVisualStateId.TryGetValue(visualStateId, out placement);
    }

    public bool TryGetRegionForVisual(
        VisualStateId visualStateId,
        [NotNullWhen(true)] out Canvas2DSpatialRegion? region)
    {
        region = null;
        return TryGetPlacement(visualStateId, out var placement) &&
            TryGetRegion(placement.RegionId, out region);
    }

    public bool Equals(Canvas2DSpatialPresentationPlan? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        CanonicalGuidanceToSceneTransform == other.CanonicalGuidanceToSceneTransform &&
        CoordinateMap.Equals(other.CoordinateMap) &&
        MovementBottomBoundaryRegionId == other.MovementBottomBoundaryRegionId &&
        Regions.AsSpan().SequenceEqual(other.Regions.AsSpan()) &&
        ResizeTargets.AsSpan().SequenceEqual(other.ResizeTargets.AsSpan()) &&
        VisualPlacements.AsSpan().SequenceEqual(other.VisualPlacements.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as Canvas2DSpatialPresentationPlan);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CanonicalGuidanceToSceneTransform);
        hash.Add(CoordinateMap);
        hash.Add(MovementBottomBoundaryRegionId);
        foreach (var target in ResizeTargets) hash.Add(target);
        foreach (var region in Regions)
        {
            hash.Add(region);
        }

        foreach (var placement in VisualPlacements)
        {
            hash.Add(placement);
        }

        return hash.ToHashCode();
    }

    private static bool IsTranslation(Matrix2D transform) =>
        transform.M11 == 1d &&
        transform.M12 == 0d &&
        transform.M21 == 0d &&
        transform.M22 == 1d;
}
