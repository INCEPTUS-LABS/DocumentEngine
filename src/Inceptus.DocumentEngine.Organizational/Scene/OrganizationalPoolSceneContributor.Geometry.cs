using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Organizational.Spatial;

namespace Inceptus.DocumentEngine.Organizational.Scene;

public sealed partial class OrganizationalPoolSceneContributor
{
    private const double PoolNameFontSize = 13d;
    private const double PoolNameHorizontalInset = 8d;
    private const double PoolNameVerticalInset = 5d;

    public Canvas2DScopeGeometryBaseResult PrepareBase(Canvas2DScopeGeometryBaseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var textRequests = OrderedPools(context.Inputs).Select(pool =>
            KeyValuePair.Create(PoolNameIdentity(pool.Id), PoolNameRequest(pool, context.TextConfiguration)));
        return Canvas2DScopeGeometryBaseResult.Success(new Canvas2DSceneContribution(), textRequests);
    }

    public Canvas2DScopeGeometryPresentationResult PreparePresentation(
        Canvas2DScopeGeometryPresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var inputs = context.BaseContext.Inputs;
        var pools = OrderedPools(inputs);
        var previousInputs = context.BaseContext.PreviousInputs;
        var previousPools = previousInputs is null ? pools : OrderedPools(previousInputs);
        var hasGroups = pools.Concat(previousPools).Any(pool => OrganizationalSemantics.GetParticipantId(pool) is not null);
        var poolIds = pools.Select(static pool => pool.Id).ToHashSet();
        var unassignedId = UnassignedRegionId(inputs.ScopeId);
        var existing = context.ExistingRegions
            .Where(static region => region.ProfileId == OrganizationalModelProfile.Id)
            .ToDictionary(static region => region.Id);
        var liveRegionIds = pools.Select(pool => PoolRegionId(pool.Id)).Append(unassignedId).ToHashSet();
        if (context.RequestedRegionHeights.Keys.Any(id => !liveRegionIds.Contains(id)))
            return GeometryFailure("organizational.geometry.unknown-region", "The requested height has no live Pool or Unassigned region.");

        var assignmentByVisual = context.BaseContext.ProjectedGraph.Nodes
            .Where(static node => node.Source.VisualStateId is not null && node.RegionEnvelope is null)
            .ToDictionary(static node => node.Source.VisualStateId!,
                node => ResolvePreparationPoolId(inputs, node, poolIds));
        var nodeBounds = context.Nodes.ToDictionary(static node => node.VisualStateId);
        var contentByPool = pools.ToDictionary(static pool => pool.Id,
            pool => ResolvePreparationContentBounds(assignmentByVisual, nodeBounds, pool.Id));
        var unassignedContent = ResolvePreparationContentBounds(assignmentByVisual, nodeBounds, null);
        var isActive = inputs.ModelProfiles.IsAvailable(OrganizationalModelProfile.Id) && !pools.IsEmpty;
        if (pools.IsEmpty && !existing.ContainsKey(unassignedId))
            return Canvas2DScopeGeometryPresentationResult.Success([]);
        var previousWidth = context.ExistingWidths.FirstOrDefault(static width => width.ProfileId == OrganizationalModelProfile.Id);
        var commonWidth = context.RequestedWidths.GetValueOrDefault(OrganizationalModelProfile.Id,
            previousWidth?.OuterWidth ?? Math.Max(MinimumPoolWidth,
                contentByPool.Values.Append(unassignedContent).Max(DestinationContentRight) + (ContentPadding * 2d) + HeaderWidth));
        var widthConstraints = OrganizationalDimensionCapacity.Width(context.Nodes);
        if (isActive || context.RequestedWidths.ContainsKey(OrganizationalModelProfile.Id) || previousWidth is null)
        {
            var errors = widthConstraints.Validate(commonWidth);
            if (errors.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                return Canvas2DScopeGeometryPresentationResult.Failure(errors);
        }
        var widths = new[] { new SpatialScopeWidthSnapshot(OrganizationalModelProfile.Id, commonWidth) };
        var regions = new List<SpatialRegionGeometrySnapshot>();
        var active = new List<Canvas2DSpatialRegion>();
        var placements = new List<Canvas2DSpatialVisualPlacement>();
        var cursorY = StackTop;
        for (var poolIndex = 0; poolIndex <= pools.Length; poolIndex++)
        {
            var pool = poolIndex < pools.Length ? pools[poolIndex] : null;
            var poolId = pool?.Id;
            var id = poolId is null ? unassignedId : PoolRegionId(poolId);
            var content = poolId is null ? unassignedContent : contentByPool[poolId];
            var members = assignmentByVisual.Where(entry => entry.Value == poolId)
                .Select(entry => entry.Key).ToArray();
            var bodies = members.Where(nodeBounds.ContainsKey).Select(id => nodeBounds[id]).ToArray();
            if (members.Any(id => !nodeBounds.ContainsKey(id)))
                return GeometryFailure("organizational.geometry.missing-node-body", "An assigned node has no measured body geometry.");
            var compactHeight = 0d;
            if (poolId is not null)
            {
                if (!context.Measurements.TryGetValue(PoolNameIdentity(poolId), out var measuredName))
                    return GeometryFailure("organizational.geometry.missing-name-measurement", "Pool preparation requires its declared single-line name measurement.");
                compactHeight = CompactHeight(measuredName);
            }
            var constraints = OrganizationalDimensionCapacity.Height(bodies, compactHeight,
                StackTop + (PoolGap * (pools.Length + 1)) + existing.Values.Where(region => region.Id != id)
                    .Sum(region => context.RequestedRegionHeights.GetValueOrDefault(region.Id, region.ExpandedHeight)));
            var height = context.RequestedRegionHeights.TryGetValue(id, out var requestedHeight)
                ? requestedHeight
                : existing.TryGetValue(id, out var previous)
                    ? previous.ExpandedHeight
                    : Math.Max(constraints.Minimum, DestinationContentBottom(content) + (ContentPadding * 2d));
            if (isActive || context.RequestedRegionHeights.ContainsKey(id) || !existing.ContainsKey(id))
            {
                var errors = constraints.Validate(height);
                if (errors.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                    return Canvas2DScopeGeometryPresentationResult.Failure(errors);
            }
            if (!isActive)
            {
                regions.Add(new SpatialRegionGeometrySnapshot(id, OrganizationalModelProfile.Id, poolId, height, null, null));
                continue;
            }
            var top = existing.TryGetValue(id, out var existingRegion) && existingRegion.ContentBounds is { } priorBounds
                ? priorBounds.Top : cursorY;
            // Preserve existing Process-only stacking for explicit ordering/capacity edits.
            // Group edits retain unrelated region frames and normalize only their members.
            if (!hasGroups && (context.RequestedRegionHeights.Count > 0 ||
                    pools.Length != previousPools.Length)) top = cursorY;
            var bounds = new RectD(StackLeft + HeaderWidth, top, commonWidth - HeaderWidth, height);
            if (!double.IsFinite(bounds.Bottom) || !double.IsFinite(bounds.Right) ||
                bounds.Bottom <= bounds.Top || bounds.Right <= bounds.Left ||
                bounds.Bottom > OrganizationalDimensionCapacity.CoordinateBudget ||
                bounds.Right > OrganizationalDimensionCapacity.CoordinateBudget)
                return GeometryFailure("organizational.geometry.coordinate-range", "The requested dimensions exceed the supported spatial coordinate range.");
            var translation = Matrix2D.CreateTranslation(bounds.Left + ContentPadding, bounds.Top + ContentPadding);
            var region = new SpatialRegionGeometrySnapshot(id, OrganizationalModelProfile.Id, poolId,
                height, translation, bounds);
            regions.Add(region);
            active.Add(new Canvas2DSpatialRegion(id, OrganizationalModelProfile.Id, poolId, translation, bounds));
            placements.AddRange(members.Select(visual => new Canvas2DSpatialVisualPlacement(visual, id)));
            // New geometry must not reserve a Lane gap that grouping would leave behind
            // between this Participant and the next block. Authored region tops stay authoritative.
            var participantId = pool is null ? null : OrganizationalSemantics.GetParticipantId(pool);
            var nextPool = poolIndex + 1 < pools.Length ? pools[poolIndex + 1] : null;
            var sameParticipant = participantId is not null && nextPool is not null &&
                participantId == OrganizationalSemantics.GetParticipantId(nextPool);
            cursorY = Math.Max(cursorY, bounds.Bottom + (sameParticipant ? 0d : PoolGap));
        }
        HashSet<SemanticElementId>? affectedParticipants = null;
        if (previousInputs is not null && existing.Count > 0)
        {
            affectedParticipants = [];
            var oldPools = previousPools.ToDictionary(pool => pool.Id);
            var newPools = pools.ToDictionary(pool => pool.Id);
            foreach (var id in oldPools.Keys.Union(newPools.Keys))
            {
                oldPools.TryGetValue(id, out var oldPool);
                newPools.TryGetValue(id, out var newPool);
                var oldOwner = oldPool is null ? null : OrganizationalSemantics.GetParticipantId(oldPool);
                var newOwner = newPool is null ? null : OrganizationalSemantics.GetParticipantId(newPool);
                var regionId = PoolRegionId(id);
                var next = regions.FirstOrDefault(region => region.Id == regionId);
                var geometryChanged = !existing.TryGetValue(regionId, out var prior) || next != prior;
                if (oldOwner != newOwner || geometryChanged)
                {
                    if (oldOwner is not null) affectedParticipants.Add(oldOwner);
                    if (newOwner is not null) affectedParticipants.Add(newOwner);
                }
            }
        }
        regions = OrganizationalParticipantGrouping.Normalize(pools, regions, affectedParticipants).ToList();
        if (isActive && hasGroups)
        {
            // The existing Unassigned workspace is the trailing movement boundary.
            // Its derived translation follows the Pool partition; node-local geometry stays authoritative.
            var index = regions.FindIndex(region => region.Id == unassignedId);
            var unassigned = regions[index];
            var bounds = unassigned.ContentBounds!.Value;
            var top = regions.Where(region => region.ContainerSemanticElementId is not null)
                .Max(region => region.ContentBounds!.Value.Bottom) + PoolGap;
            regions[index] = new(unassigned.Id, unassigned.ProfileId, null, unassigned.ExpandedHeight,
                Matrix2D.CreateTranslation(bounds.Left + ContentPadding, top + ContentPadding),
                new RectD(bounds.Left, top, bounds.Width, bounds.Height));
        }
        active = regions.Where(region => region.IsActive).Select(region => new Canvas2DSpatialRegion(
            region.Id, region.ProfileId, region.ContainerSemanticElementId, region.LocalToScopeTransform!.Value,
            region.ContentBounds!.Value)).ToList();
        return Canvas2DScopeGeometryPresentationResult.Success(regions, widths, isActive
            ? new Canvas2DSpatialPresentationPlan(active, placements, Matrix2D.Identity,
                Canvas2DSpatialCoordinateMap.Identity, unassignedId) : null);
    }

    private SemanticElementId? ResolvePreparationPoolId(
        ScopeGeometryInputs inputs,
        ProjectedNode node,
        HashSet<SemanticElementId> poolIds)
    {
        var ownerId = node.PlacementHint?.BoundaryAttachment?.AttachedToElementId ?? node.Source.SemanticElementId;
        var owner = inputs.Elements.FirstOrDefault(element => element.Id == ownerId);
        if (owner is null || (!OrganizationalSemantics.IsDirectlyAssignable(owner, _eligibilityPolicy) &&
                node.PlacementHint?.BoundaryAttachment is null))
            return null;
        var assignment = inputs.ProfileAssignments.FirstOrDefault(assignment =>
            assignment.ProfileId == OrganizationalModelProfile.Id && assignment.SemanticElementId == ownerId);
        return assignment is not null && poolIds.Contains(assignment.ContainerSemanticElementId)
            ? assignment.ContainerSemanticElementId : null;
    }

    private static ImmutableArray<SemanticElementSnapshot> OrderedPools(ScopeGeometryInputs inputs)
    {
        var pools = inputs.Elements.Where(OrganizationalSemantics.IsPool)
            .OrderBy(static pool => pool.Id.Value, StringComparer.Ordinal).ToImmutableArray();
        var order = inputs.ProfileElementPresentations
            .Where(static presentation => presentation.ProfileId == OrganizationalModelProfile.Id)
            .ToDictionary(static presentation => presentation.SemanticElementId, static presentation => presentation.Order);
        return pools.All(pool => order.ContainsKey(pool.Id))
            ? pools.OrderBy(pool => order[pool.Id]).ThenBy(static pool => pool.Id.Value, StringComparer.Ordinal).ToImmutableArray()
            : pools;
    }

    private static RectD ResolvePreparationContentBounds(
        Dictionary<VisualStateId, SemanticElementId?> assignments,
        Dictionary<VisualStateId, Canvas2DScopeGeometryNodeBounds> nodes,
        SemanticElementId? poolId)
    {
        var bounds = assignments.Where(entry => entry.Value == poolId && nodes.ContainsKey(entry.Key))
            .SelectMany(entry => nodes[entry.Key].CaptionBounds is { } caption
                ? new[] { nodes[entry.Key].BodyBounds, caption }
                : [nodes[entry.Key].BodyBounds]).ToArray();
        return bounds.Length == 0
            ? new RectD(DefaultContentX, DefaultContentY, DefaultContentWidth, DefaultContentHeight)
            : Union(bounds);
    }

    private static SceneObjectId PoolNameIdentity(SemanticElementId poolId) =>
        Canvas2DSceneObjectIdentity.ForExtension(Descriptor.ContributorId, $"{poolId.Value}:pool-name");

    private static TextMeasurementRequest PoolNameRequest(SemanticElementSnapshot pool, TextMeasurementRequest template) =>
        new(SingleLinePoolName(pool), template.FontFamily, template.FontIdentity, template.FontVersion,
            PoolNameFontSize, PoolNameFontSize * template.LineHeight / template.FontSize,
            400, TextFontStyle.Normal, template.Locale, template.Direction, TextWritingMode.HorizontalTopToBottom,
            template.Scale, template.ConfigurationId, template.ConfigurationVersion);

    private static string SingleLinePoolName(SemanticElementSnapshot pool) =>
        (TryReadName(pool) ?? "Pool").Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ').Replace('\n', ' ').Replace('\u2028', ' ').Replace('\u2029', ' ');

    private static double CompactHeight(TextMetrics metrics) => metrics.LineHeight + (PoolNameVerticalInset * 2d);

    private static Canvas2DScopeGeometryPresentationResult GeometryFailure(string code, string message) =>
        Canvas2DScopeGeometryPresentationResult.Failure([new Diagnostic(code, DiagnosticSeverity.Error, message)]);
}
