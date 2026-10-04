using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Spatial;

namespace Inceptus.DocumentEngine.Organizational.Scene;

public sealed partial class OrganizationalPoolSceneContributor
{
    internal static Canvas2DSpatialDimensionConstraints? SavedDimensionConstraints(ScopeGeometrySnapshot geometry,
        IEnumerable<SemanticElementSnapshot> elements, Canvas2DSpatialResizeEdge edge, Canvas2DSpatialRegionId? authorityRegionId)
    {
        var regions = geometry.Regions.Where(static region => region.ProfileId == OrganizationalModelProfile.Id).ToArray();
        if (!regions.Any(static region => region.IsActive)) return null;
        if (edge == Canvas2DSpatialResizeEdge.Right && authorityRegionId is null)
            return OrganizationalDimensionCapacity.Width(geometry.Nodes.Select(static node =>
                new Canvas2DScopeGeometryNodeBounds(node.VisualStateId, node.LocalBounds, null)));
        var region = regions.FirstOrDefault(region => region.Id == authorityRegionId && region.IsActive);
        if (edge != Canvas2DSpatialResizeEdge.Bottom || region is null) return null;
        var compactHeight = 0d;
        if (region.ContainerSemanticElementId is { } poolId)
        {
            var pool = elements.FirstOrDefault(element => element.Id == poolId);
            if (pool is null) return null;
            var nameRequest = PoolNameRequest(pool, geometry.TextConfiguration);
            var metrics = geometry.TextMeasurements.FirstOrDefault(measurement => measurement.Request.Equals(nameRequest));
            if (metrics is null) return null;
            compactHeight = CompactHeight(metrics.Metrics);
        }
        return OrganizationalDimensionCapacity.Height(geometry.Nodes.Where(node => node.RegionId == region.Id)
                .Select(static node => new Canvas2DScopeGeometryNodeBounds(node.VisualStateId, node.LocalBounds, null)),
            compactHeight, StackTop + (PoolGap * regions.Length) + regions.Where(other => other.Id != region.Id).Sum(static other => other.ExpandedHeight));
    }

    private static IEnumerable<Canvas2DSpatialResizeTarget> CreateResizeTargets(ScopeGeometrySnapshot geometry,
        IEnumerable<SemanticElementSnapshot> pools, IReadOnlyList<Canvas2DSpatialRegion> displayedRegions,
        HashSet<Canvas2DSpatialRegionId> collapsedRegions, DocumentScopeId scopeId)
    {
        var width = geometry.SpatialWidths.Single(value => value.ProfileId == OrganizationalModelProfile.Id).OuterWidth;
        var allIds = displayedRegions.Select(static region => region.Id).ToArray();
        var span = new RectD(StackLeft, displayedRegions.Min(static region => region.Bounds.Top), width,
            displayedRegions.Max(static region => region.Bounds.Bottom) - displayedRegions.Min(static region => region.Bounds.Top));
        var widthConstraints = SavedDimensionConstraints(geometry, pools, Canvas2DSpatialResizeEdge.Right, null)!;
        foreach (var region in displayedRegions)
        {
            var poolId = region.ContainerSemanticElementId;
            var bounds = poolId is null ? region.Bounds : new RectD(region.Bounds.Left - HeaderWidth,
                region.Bounds.Top, region.Bounds.Width + HeaderWidth, region.Bounds.Height);
            var borderId = Canvas2DSceneObjectIdentity.ForExtension(Descriptor.ContributorId,
                poolId is null ? $"{scopeId.Value}:unassigned-region" : $"{poolId.Value}:pool-background");
            yield return new Canvas2DSpatialResizeTarget(OrganizationalModelProfile.Id, region.Id, null,
                Canvas2DSpatialResizeEdge.Right, borderId, bounds, width, widthConstraints, allIds, span);
            if (!collapsedRegions.Contains(region.Id))
                yield return new Canvas2DSpatialResizeTarget(OrganizationalModelProfile.Id, region.Id, region.Id,
                    Canvas2DSpatialResizeEdge.Bottom, borderId, bounds,
                    geometry.Regions.Single(saved => saved.Id == region.Id).ExpandedHeight,
                    SavedDimensionConstraints(geometry, pools, Canvas2DSpatialResizeEdge.Bottom, region.Id)!, [region.Id], bounds);
        }
    }
}
