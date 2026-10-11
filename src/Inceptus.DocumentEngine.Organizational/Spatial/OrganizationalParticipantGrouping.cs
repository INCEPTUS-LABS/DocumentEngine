using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Organizational.Spatial;

/// <summary>Local horizontal Lane grouping. It never assigns membership or arranges BPMN nodes.</summary>
public static class OrganizationalParticipantGrouping
{
    /// <summary>
    /// Plans the region part of an explicit assignment after semantic membership has
    /// changed. Existing Lanes anchor the group; only overlapping following blocks
    /// shift to make room. History replay supplies its own saved region deltas.
    /// </summary>
    public static DocumentSnapshot AppendAssignedPool(DocumentSnapshot document, SemanticElementId poolId) =>
        AppendAssignedPool(document, poolId, document);

    /// <summary>
    /// Also closes the source slot when assignment moves a Pool from above the destination.
    /// Previous semantic membership determines whether a top-level gap is removed with it.
    /// </summary>
    public static DocumentSnapshot AppendAssignedPool(DocumentSnapshot document, SemanticElementId poolId, DocumentSnapshot previousDocument)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(poolId);
        ArgumentNullException.ThrowIfNull(previousDocument);
        var scopeId = document.SemanticModel.GetScope(poolId).Id;
        var blocks = OrganizationalPoolOrdering.Resolve(document, scopeId);
        var group = blocks.FirstOrDefault(block => block.IsParticipant && block.Pools.Any(pool => pool.Id == poolId));
        if (group is null || group.Pools.Length < 2) return document;
        var regions = OrganizationalPoolOrdering.Regions(document, scopeId)
            .Where(region => region.IsActive && region.ContainerSemanticElementId is not null)
            .ToDictionary(region => region.ContainerSemanticElementId!);
        if (blocks.SelectMany(block => block.Pools).Any(pool => !regions.ContainsKey(pool.Id))) return document;
        var existingLanes = group.Pools.Where(pool => pool.Id != poolId).Select(pool => regions[pool.Id]).ToArray();
        var replacements = new Dictionary<Canvas2DSpatialRegionId, SpatialRegionGeometrySnapshot>();
        var source = regions[poolId].ContentBounds!.Value;
        if (source.Top < existingLanes.Min(region => region.ContentBounds!.Value.Top))
        {
            var originalGroup = OrganizationalPoolOrdering.Resolve(previousDocument, scopeId)
                .FirstOrDefault(block => block.Pools.Any(pool => pool.Id == poolId));
            if (originalGroup is not null && (originalGroup.Id != group.Id || !originalGroup.IsParticipant))
            {
                var removedGap = originalGroup.Pools.Length == 1 ? OrganizationalPoolSceneContributor.PoolGap : 0d;
                var destinationBottom = existingLanes.Max(region => region.ContentBounds!.Value.Bottom);
                // Close the source slot up to the destination. Beyond the destination,
                // its appended Lane replaces the removed height; only a removed block gap remains.
                foreach (var region in regions.Values.Where(region => region.ContainerSemanticElementId != poolId &&
                             region.ContentBounds!.Value.Top >= source.Bottom).ToArray())
                {
                    var top = region.ContentBounds!.Value.Top;
                    var offset = removedGap + (top < destinationBottom ? source.Height : 0d);
                    if (offset == 0d) continue;
                    var moved = OrganizationalPoolOrdering.AtTop(region, top - offset);
                    regions[region.ContainerSemanticElementId!] = moved;
                    replacements[moved.Id] = moved;
                }
                existingLanes = group.Pools.Where(pool => pool.Id != poolId).Select(pool => regions[pool.Id]).ToArray();
            }
        }
        var groupTop = existingLanes.Min(region => region.ContentBounds!.Value.Top);
        var appended = OrganizationalPoolOrdering.AtTop(regions[poolId], existingLanes.Max(region => region.ContentBounds!.Value.Bottom));
        replacements[appended.Id] = appended;
        var cursor = appended.ContentBounds!.Value.Bottom + OrganizationalPoolSceneContributor.PoolGap;
        foreach (var block in blocks.Where(block => block != group))
        {
            var members = block.Pools.Select(pool => regions[pool.Id]).ToArray();
            var top = members.Min(region => region.ContentBounds!.Value.Top);
            if (top < groupTop) continue;
            if (top >= cursor) break;
            var offset = cursor - top;
            foreach (var member in members)
                replacements[member.Id] = OrganizationalPoolOrdering.AtTop(member, member.ContentBounds!.Value.Top + offset);
            cursor = members.Max(region => region.ContentBounds!.Value.Bottom) + offset + OrganizationalPoolSceneContributor.PoolGap;
        }
        return OrganizationalPoolOrdering.Replace(document, document.VisualModel.ProfileElementPresentations, replacements);
    }

    /// <summary>Separates a newly standalone Pool from its former Participant without splitting the remaining Lanes.</summary>
    public static DocumentSnapshot SeparateUnassignedPool(DocumentSnapshot document, SemanticElementId poolId, DocumentSnapshot previousDocument)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(poolId);
        ArgumentNullException.ThrowIfNull(previousDocument);
        var scopeId = document.SemanticModel.GetScope(poolId).Id;
        var blocks = OrganizationalPoolOrdering.Resolve(previousDocument, scopeId);
        var group = blocks.FirstOrDefault(block => block.IsParticipant && block.Pools.Any(pool => pool.Id == poolId));
        if (group is null || group.Pools.Length < 2) return document;
        var regions = OrganizationalPoolOrdering.Regions(document, scopeId)
            .Where(region => region.IsActive && region.ContainerSemanticElementId is not null)
            .ToDictionary(region => region.ContainerSemanticElementId!);
        if (blocks.SelectMany(block => block.Pools).Any(pool => !regions.ContainsKey(pool.Id))) return document;
        var detached = regions[poolId];
        var first = group.Pools[0].Id == poolId;
        var cursor = first ? detached.ContentBounds!.Value.Bottom + OrganizationalPoolSceneContributor.PoolGap
            : regions[group.Pools[0].Id].ContentBounds!.Value.Top;
        var replacements = new Dictionary<Canvas2DSpatialRegionId, SpatialRegionGeometrySnapshot>();
        foreach (var pool in group.Pools.Where(pool => pool.Id != poolId))
        {
            var region = regions[pool.Id];
            replacements[region.Id] = OrganizationalPoolOrdering.AtTop(region, cursor);
            cursor += region.ExpandedHeight;
        }
        if (!first)
        {
            cursor += OrganizationalPoolSceneContributor.PoolGap;
            replacements[detached.Id] = OrganizationalPoolOrdering.AtTop(detached, cursor);
            cursor += detached.ExpandedHeight;
        }
        cursor += OrganizationalPoolSceneContributor.PoolGap;
        foreach (var block in blocks.SkipWhile(block => block != group).Skip(1))
        {
            var members = block.Pools.Select(pool => regions[pool.Id]).ToArray();
            var top = members.Min(region => region.ContentBounds!.Value.Top);
            if (top >= cursor) break;
            var offset = cursor - top;
            foreach (var member in members)
                replacements[member.Id] = OrganizationalPoolOrdering.AtTop(member, member.ContentBounds!.Value.Top + offset);
            cursor = members.Max(region => region.ContentBounds!.Value.Bottom) + offset + OrganizationalPoolSceneContributor.PoolGap;
        }
        return OrganizationalPoolOrdering.Replace(document, document.VisualModel.ProfileElementPresentations, replacements);
    }

    public static IReadOnlyList<SpatialRegionGeometrySnapshot> Normalize(
        IEnumerable<SemanticElementSnapshot> pools, IEnumerable<SpatialRegionGeometrySnapshot> regions,
        IReadOnlySet<SemanticElementId>? affectedParticipants = null)
    {
        ArgumentNullException.ThrowIfNull(pools);
        ArgumentNullException.ThrowIfNull(regions);
        var result = regions.ToArray();
        var assignment = pools.Where(OrganizationalSemantics.IsPool)
            .Where(pool => OrganizationalSemantics.GetParticipantId(pool) is not null)
            .ToDictionary(pool => pool.Id, pool => OrganizationalSemantics.GetParticipantId(pool)!);
        var groups = result.Where(region => region.IsActive && region.ContainerSemanticElementId is { } id && assignment.ContainsKey(id))
            .GroupBy(region => assignment[region.ContainerSemanticElementId!]);
        var replacements = new Dictionary<SemanticElementId, SpatialRegionGeometrySnapshot>();
        foreach (var group in groups.OrderBy(group => group.Min(region => region.ContentBounds!.Value.Top))
                     .ThenBy(group => group.Key.Value, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(region => region.ContentBounds!.Value.Top)
                .ThenBy(region => region.ContainerSemanticElementId!.Value, StringComparer.Ordinal).ToArray();
            var top = ordered[0].ContentBounds!.Value.Top;
            var left = ordered.Min(region => region.ContentBounds!.Value.Left);
            var right = ordered.Max(region => region.ContentBounds!.Value.Right);
            var height = ordered.Sum(region => region.ExpandedHeight);
            var own = ordered.Select(region => region.Id).ToHashSet();
            var obstacles = result.Where(region => region.IsActive && region.ContainerSemanticElementId is not null && !own.Contains(region.Id))
                .Select(region => region.ContainerSemanticElementId is { } id && replacements.TryGetValue(id, out var changed) ? changed : region)
                .Select(region => region.ContentBounds!.Value).OrderBy(bounds => bounds.Top).ToArray();
            if (affectedParticipants is not null && !affectedParticipants.Contains(group.Key) &&
                !obstacles.Any(obstacle => top < obstacle.Bottom && top + height > obstacle.Top)) continue;
            // Move only this group when a newly joined envelope would cover an unrelated Pool.
            // Already contiguous groups are stable; no global layout is performed.
            foreach (var obstacle in obstacles)
                if (top < obstacle.Bottom && top + height > obstacle.Top) top = obstacle.Bottom + OrganizationalPoolSceneContributor.PoolGap;
            foreach (var region in ordered)
            {
                var bounds = region.ContentBounds!.Value;
                var translation = region.LocalToScopeTransform!.Value;
                var next = new RectD(left, top, right - left, region.ExpandedHeight);
                replacements.Add(region.ContainerSemanticElementId!, new(region.Id, region.ProfileId,
                    region.ContainerSemanticElementId, region.ExpandedHeight,
                    Matrix2D.CreateTranslation(translation.OffsetX + next.Left - bounds.Left,
                        translation.OffsetY + next.Top - bounds.Top), next));
                top = next.Bottom;
            }
        }
        return result.Select(region => region.ContainerSemanticElementId is { } id && replacements.TryGetValue(id, out var replacement)
            ? replacement : region).ToArray();
    }
}
