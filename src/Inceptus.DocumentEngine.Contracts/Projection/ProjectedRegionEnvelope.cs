using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>Transient endpoint geometry derived from authoritative spatial regions; never saved as node bounds.</summary>
public sealed class ProjectedRegionEnvelope : IEquatable<ProjectedRegionEnvelope>
{
    public ProjectedRegionEnvelope(IEnumerable<Canvas2DSpatialRegionId> regionIds, double leftExpansion, double? headerWidth = null)
    {
        ArgumentNullException.ThrowIfNull(regionIds);
        RegionIds = [.. regionIds.Distinct().OrderBy(static id => id.Value, StringComparer.Ordinal)];
        if (RegionIds.IsEmpty || RegionIds.Any(static id => id is null)) throw new ArgumentException("An envelope requires regions.", nameof(regionIds));
        if (!double.IsFinite(leftExpansion) || leftExpansion < 0) throw new ArgumentOutOfRangeException(nameof(leftExpansion));
        if (headerWidth is { } width && (!double.IsFinite(width) || width <= 0)) throw new ArgumentOutOfRangeException(nameof(headerWidth));
        LeftExpansion = leftExpansion;
        HeaderWidth = headerWidth;
    }
    public ImmutableArray<Canvas2DSpatialRegionId> RegionIds { get; }
    public double LeftExpansion { get; }
    public double? HeaderWidth { get; }
    public RectD Resolve(IEnumerable<SpatialRegionGeometrySnapshot> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var byId = regions.ToDictionary(static region => region.Id);
        var bounds = RegionIds.Select(id => byId.TryGetValue(id, out var region) && region.ContentBounds is { } value
            ? value : throw new InvalidOperationException("The endpoint envelope requires current expanded region geometry.")).ToArray();
        var left = bounds.Min(static item => item.Left) - LeftExpansion;
        var top = bounds.Min(static item => item.Top);
        return new(left, top, HeaderWidth ?? bounds.Max(static item => item.Right) - left, bounds.Max(static item => item.Bottom) - top);
    }
    public bool Equals(ProjectedRegionEnvelope? other) => ReferenceEquals(this, other) || other is not null &&
        LeftExpansion == other.LeftExpansion && HeaderWidth == other.HeaderWidth && RegionIds.SequenceEqual(other.RegionIds);
    public override bool Equals(object? obj) => Equals(obj as ProjectedRegionEnvelope);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(LeftExpansion); hash.Add(HeaderWidth);
        foreach (var id in RegionIds) hash.Add(id);
        return hash.ToHashCode();
    }
}
