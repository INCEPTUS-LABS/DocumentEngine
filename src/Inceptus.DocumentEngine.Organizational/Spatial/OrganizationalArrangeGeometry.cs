using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Scene;

namespace Inceptus.DocumentEngine.Organizational.Spatial;

/// <summary>Capacity planning using the same authored blocks, dimensions and frame translation as Pool ordering.</summary>
public static class OrganizationalArrangeGeometry
{
    public static (DocumentSnapshot Document, ImmutableArray<SpatialRegionHeightIntent> Heights,
        ImmutableArray<SpatialScopeWidthIntent> Widths) Expand(DocumentSnapshot document,
        DocumentScopeId scopeId, IReadOnlyDictionary<VisualStateId, RectD> localBounds) =>
        Expand(document, scopeId, localBounds, []);

    public static (DocumentSnapshot Document, ImmutableArray<SpatialRegionHeightIntent> Heights,
        ImmutableArray<SpatialScopeWidthIntent> Widths) Expand(DocumentSnapshot document,
        DocumentScopeId scopeId, IReadOnlyDictionary<VisualStateId, RectD> localBounds,
        IEnumerable<ScopeNodeGeometrySnapshot> addedNodes)
    {
        var geometry = document.VisualModel.RoutingScopes?.SingleOrDefault(scope => scope.ScopeId == scopeId)?.Geometry;
        if (geometry is null) return (document, [], []);
        var nodes = geometry.Nodes.AddRange(addedNodes ?? []);
        var regions = OrganizationalPoolOrdering.Regions(document, scopeId).Where(region => region.IsActive).ToArray();
        if (regions.Length == 0) return (document, [], []);
        var captions = geometry.Captions.GroupBy(caption => caption.OwnerVisualStateId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var extents = nodes.ToDictionary(node => node.VisualStateId, node =>
        {
            var body = localBounds.GetValueOrDefault(node.VisualStateId, node.LocalBounds);
            var offset = new VectorD(body.X - node.LocalBounds.X, body.Y - node.LocalBounds.Y);
            var content = captions.GetValueOrDefault(node.VisualStateId, []).Select(caption => caption.ContentBounds.Translate(offset));
            return (Right: content.Select(bounds => bounds.Right).Append(body.Right).Max(),
                Bottom: content.Select(bounds => bounds.Bottom).Append(body.Bottom).Max());
        });
        var heights = regions.ToDictionary(region => region.Id, region => Math.Max(region.ExpandedHeight,
            nodes.Where(node => node.RegionId == region.Id).Select(node =>
                extents[node.VisualStateId].Bottom + 2 * OrganizationalPoolSceneContributor.ContentPadding)
                .DefaultIfEmpty(region.ExpandedHeight).Max()));
        var heightIntents = regions.Where(region => heights[region.Id] != region.ExpandedHeight)
            .Select(region => new SpatialRegionHeightIntent(scopeId, region.Id, heights[region.Id])).ToImmutableArray();
        var oldWidth = geometry.SpatialWidths.Single(width => width.ProfileId == OrganizationalModelProfile.Id).OuterWidth;
        var minimumWidth = OrganizationalDimensionCapacity.Width(nodes.Select(node =>
            new Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DScopeGeometryNodeBounds(node.VisualStateId,
                localBounds.GetValueOrDefault(node.VisualStateId, node.LocalBounds)))).Minimum;
        var paddedWidth = extents.Values.Select(extent => extent.Right + OrganizationalPoolSceneContributor.HeaderWidth +
            2 * OrganizationalPoolSceneContributor.ContentPadding).DefaultIfEmpty(minimumWidth).Max();
        var width = Math.Max(oldWidth, Math.Max(minimumWidth, paddedWidth));
        ImmutableArray<SpatialScopeWidthIntent> widthIntents = width == oldWidth ? [] :
            [new(scopeId, OrganizationalModelProfile.Id, width)];
        if (heightIntents.IsEmpty && widthIntents.IsEmpty) return (document, [], []);
        var byPool = regions.Where(region => region.ContainerSemanticElementId is not null)
            .ToDictionary(region => region.ContainerSemanticElementId!);
        var replacements = new Dictionary<Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSpatialRegionId, SpatialRegionGeometrySnapshot>();
        double? bottom = null;
        foreach (var block in OrganizationalPoolOrdering.Resolve(document, scopeId))
        {
            var top = byPool[block.Pools[0].Id].ContentBounds!.Value.Top;
            if (bottom is { } prior) top = Math.Max(top, prior + OrganizationalPoolSceneContributor.PoolGap);
            foreach (var pool in block.Pools)
            {
                var region = byPool[pool.Id];
                var moved = OrganizationalPoolOrdering.AtTop(region, top);
                var bounds = moved.ContentBounds!.Value;
                replacements[region.Id] = new(region.Id, region.ProfileId, pool.Id, heights[region.Id],
                    moved.LocalToScopeTransform, new RectD(bounds.X, bounds.Y,
                        bounds.Width + width - oldWidth, heights[region.Id]));
                top += heights[region.Id];
            }
            bottom = top;
        }
        foreach (var region in regions.Where(region => region.ContainerSemanticElementId is null))
        {
            var moved = OrganizationalPoolOrdering.AtTop(region,
                bottom is { } prior ? prior + OrganizationalPoolSceneContributor.PoolGap : region.ContentBounds!.Value.Top);
            var bounds = moved.ContentBounds!.Value;
            replacements[region.Id] = new(region.Id, region.ProfileId, null, heights[region.Id], moved.LocalToScopeTransform,
                new RectD(bounds.X, bounds.Y, bounds.Width + width - oldWidth, heights[region.Id]));
        }
        return (OrganizationalPoolOrdering.Replace(document, document.VisualModel.ProfileElementPresentations, replacements),
            heightIntents, widthIntents);
    }
}
