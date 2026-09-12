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
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(visualPlacements);
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
    }

    public ImmutableArray<Canvas2DSpatialRegion> Regions { get; }

    public ImmutableArray<Canvas2DSpatialVisualPlacement> VisualPlacements { get; }

    /// <summary>
    /// Gets the one reversible translation used for relationship-owned manual guidance.
    /// Endpoint geometry is mapped through its owning visual region instead.
    /// </summary>
    public Matrix2D CanonicalGuidanceToSceneTransform { get; }

    public PointD MapCanonicalGuidanceToScene(PointD point) =>
        CanonicalGuidanceToSceneTransform.TransformPoint(point);

    public PointD MapSceneGuidanceToCanonical(PointD point) =>
        _sceneToCanonicalGuidanceTransform.TransformPoint(point);

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
        Regions.AsSpan().SequenceEqual(other.Regions.AsSpan()) &&
        VisualPlacements.AsSpan().SequenceEqual(other.VisualPlacements.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as Canvas2DSpatialPresentationPlan);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CanonicalGuidanceToSceneTransform);
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
