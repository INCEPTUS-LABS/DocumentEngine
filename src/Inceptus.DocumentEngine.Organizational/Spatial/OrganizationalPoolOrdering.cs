using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Organizational.Spatial;

/// <summary>Authored Pool order, grouped into atomic Participant blocks. No membership is inferred from geometry.</summary>
internal static class OrganizationalPoolOrdering
{
    internal sealed record Block(SemanticElementId Id, bool IsParticipant, ImmutableArray<SemanticElementSnapshot> Pools);

    internal static ImmutableArray<Block> Resolve(DocumentSnapshot document, DocumentScopeId scopeId)
    {
        var pools = OrganizationalSemantics.GetOrderedPoolsInScope(document, scopeId);
        var regions = Regions(document, scopeId).Where(region => region.IsActive && region.ContainerSemanticElementId is not null)
            .ToDictionary(region => region.ContainerSemanticElementId!);
        var hasGeometry = pools.All(pool => regions.ContainsKey(pool.Id));
        var fallback = pools.Select((pool, index) => (pool.Id, index)).ToDictionary(entry => entry.Id, entry => entry.index);
        double Top(SemanticElementSnapshot pool) => hasGeometry ? regions[pool.Id].ContentBounds!.Value.Top : fallback[pool.Id];
        return [.. pools.GroupBy(pool => (Id: OrganizationalSemantics.GetParticipantId(pool) ?? pool.Id,
                IsParticipant: OrganizationalSemantics.GetParticipantId(pool) is not null))
            .Select(group => new Block(group.Key.Id, group.Key.IsParticipant,
                [.. group.OrderBy(Top).ThenBy(pool => pool.Id.Value, StringComparer.Ordinal)]))
            .OrderBy(block => block.Pools.Min(Top)).ThenBy(block => block.Id.Value, StringComparer.Ordinal)
            .ThenBy(block => block.IsParticipant)];
    }

    internal static bool CanMove(DocumentSnapshot document, DocumentScopeId scopeId, SemanticElementId poolId,
        OrganizationalPoolMoveDirection direction, bool laneOnly)
    {
        var blocks = Resolve(document, scopeId);
        var blockIndex = Array.FindIndex(blocks.ToArray(), block => block.Pools.Any(pool => pool.Id == poolId));
        if (blockIndex < 0) return false;
        var block = blocks[blockIndex];
        if (laneOnly && !block.IsParticipant) return false;
        var index = laneOnly ? Array.FindIndex(block.Pools.ToArray(), pool => pool.Id == poolId) : blockIndex;
        var count = laneOnly ? block.Pools.Length : blocks.Length;
        return direction == OrganizationalPoolMoveDirection.Up ? index > 0 : index < count - 1;
    }

    internal static DocumentSnapshot Move(DocumentSnapshot document, DocumentScopeId scopeId, SemanticElementId poolId,
        OrganizationalPoolMoveDirection direction, bool laneOnly)
    {
        var blocks = Resolve(document, scopeId).ToArray();
        var blockIndex = Array.FindIndex(blocks, block => block.Pools.Any(pool => pool.Id == poolId));
        var original = blocks.ToArray();
        var step = direction == OrganizationalPoolMoveDirection.Up ? -1 : 1;
        var start = blockIndex;
        var end = blockIndex;
        if (laneOnly)
        {
            var lanes = blocks[blockIndex].Pools.ToArray();
            var index = Array.FindIndex(lanes, pool => pool.Id == poolId);
            (lanes[index], lanes[index + step]) = (lanes[index + step], lanes[index]);
            blocks[blockIndex] = blocks[blockIndex] with { Pools = [.. lanes] };
        }
        else
        {
            (blocks[blockIndex], blocks[blockIndex + step]) = (blocks[blockIndex + step], blocks[blockIndex]);
            start = Math.Min(blockIndex, blockIndex + step);
            end = Math.Max(blockIndex, blockIndex + step);
        }

        var saved = Regions(document, scopeId);
        var byPool = saved.Where(region => region.IsActive && region.ContainerSemanticElementId is not null)
            .ToDictionary(region => region.ContainerSemanticElementId!);
        var replacements = new Dictionary<Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSpatialRegionId, SpatialRegionGeometrySnapshot>();
        if (blocks.SelectMany(block => block.Pools).All(pool => byPool.ContainsKey(pool.Id)))
        {
            double Top(Block block) => block.Pools.Min(pool => byPool[pool.Id].ContentBounds!.Value.Top);
            double Bottom(Block block) => block.Pools.Max(pool => byPool[pool.Id].ContentBounds!.Value.Bottom);
            var cursor = laneOnly || start == 0 ? Top(original[start])
                : Bottom(original[start - 1]) + OrganizationalPoolSceneContributor.PoolGap;
            for (var index = start; index < blocks.Length; index++)
            {
                if (laneOnly && index > end) break;
                var block = blocks[index];
                if (!laneOnly && index > end && Top(block) == cursor) break;
                foreach (var pool in block.Pools)
                {
                    var region = byPool[pool.Id];
                    replacements[region.Id] = AtTop(region, cursor);
                    cursor += region.ExpandedHeight;
                }
                cursor += OrganizationalPoolSceneContributor.PoolGap;
            }
            if (!laneOnly)
            {
                var bottom = byPool.Values.Max(region => replacements.GetValueOrDefault(region.Id, region).ContentBounds!.Value.Bottom);
                foreach (var unassigned in saved.Where(region => region.IsActive && region.ContainerSemanticElementId is null))
                    replacements[unassigned.Id] = AtTop(unassigned, bottom + OrganizationalPoolSceneContributor.PoolGap);
            }
        }
        var ordered = blocks.SelectMany(block => block.Pools).ToArray();
        var ids = ordered.Select(pool => pool.Id).ToHashSet();
        var presentations = document.VisualModel.ProfileElementPresentations.Where(presentation =>
                presentation.ProfileId != OrganizationalModelProfile.Id || !ids.Contains(presentation.SemanticElementId))
            .Concat(ordered.Select((pool, index) => new ModelProfileElementPresentationSnapshot(OrganizationalModelProfile.Id, pool.Id, index)));
        return Replace(document, presentations, replacements);
    }

    internal static SpatialRegionGeometrySnapshot AtTop(SpatialRegionGeometrySnapshot region, double top)
    {
        var bounds = region.ContentBounds!.Value;
        var transform = region.LocalToScopeTransform!.Value;
        return new(region.Id, region.ProfileId, region.ContainerSemanticElementId, region.ExpandedHeight,
            Matrix2D.CreateTranslation(transform.OffsetX, transform.OffsetY + top - bounds.Top),
            new RectD(bounds.X, top, bounds.Width, bounds.Height));
    }

    internal static IEnumerable<SpatialRegionGeometrySnapshot> Regions(DocumentSnapshot document, DocumentScopeId scopeId) =>
        document.VisualModel.RoutingScopes?.FirstOrDefault(scope => scope.ScopeId == scopeId)?.Geometry.Regions
            .Where(region => region.ProfileId == OrganizationalModelProfile.Id) ?? [];

    internal static DocumentSnapshot Replace(DocumentSnapshot document, IEnumerable<ModelProfileElementPresentationSnapshot> presentations,
        IReadOnlyDictionary<Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSpatialRegionId, SpatialRegionGeometrySnapshot> replacements)
    {
        var visuals = document.VisualModel;
        var scopes = visuals.RoutingScopes?.Select(scope =>
        {
            var geometry = scope.Geometry;
            return new ScopeRoutingSnapshot(scope.ScopeId, new ScopeGeometrySnapshot(geometry.PolicyId, geometry.PolicyVersion,
                geometry.LayoutAlgorithmId, geometry.Configuration, geometry.TextConfiguration, geometry.Contributors,
                geometry.Nodes, geometry.Regions.Select(region => replacements.GetValueOrDefault(region.Id, region)),
                geometry.Captions, geometry.TextMeasurements, geometry.SpatialWidths), scope.Connectors);
        });
        return new(document.SemanticModel, new VisualModelSnapshot(document.DocumentId, document.Revision,
            visuals.VisualStates, presentations, scopes, visuals.DocumentRoutingStates), document.Metadata, document.Publication);
    }
}
