using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.Organizational.Scene;

/// <summary>
/// Derives one single-scope Pool partition and its connector presentation policy. Process and
/// Visual State identities are retained exactly once; Pool geometry is transient.
/// </summary>
public sealed class OrganizationalPoolSceneContributor :
    ICanvas2DSceneContributor,
    ICanvas2DConnectorPresentationRouter
{
    internal const double ContentPadding = 32d;
    internal const double HeaderWidth = 38d;
    internal const double PoolGap = 40d;
    internal const double StackLeft = 40d;
    internal const double StackTop = 40d;
    internal const double MinimumPoolWidth = 520d;
    internal const double MinimumPoolHeight = 144d;
    internal const double CollapsedPoolHeight = 72d;

    private const double ConnectorObstacleClearance = 16d;
    private const double DefaultContentX = 80d;
    private const double DefaultContentY = 80d;
    private const double DefaultContentWidth = 420d;
    private const double DefaultContentHeight = 80d;

    private static readonly Canvas2DSceneContributorDescriptor Descriptor = new(
        new Canvas2DSceneContributorId("inceptus:organizational/scene/pools"),
        "1");

    private readonly IOrganizationalElementEligibilityPolicy _eligibilityPolicy;

    public OrganizationalPoolSceneContributor(
        IOrganizationalElementEligibilityPolicy eligibilityPolicy)
    {
        ArgumentNullException.ThrowIfNull(eligibilityPolicy);
        _eligibilityPolicy = eligibilityPolicy;
    }

    public static Canvas2DSceneContributorRegistration CreateRegistration(
        IOrganizationalElementEligibilityPolicy eligibilityPolicy) =>
        new(
            Descriptor,
            new OrganizationalPoolSceneContributor(eligibilityPolicy),
            Canvas2DSceneContributionStage.Presentation);

    public Canvas2DSceneContributionResult Contribute(
        Canvas2DSceneContributionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var presentation = context.Presentation;
        if (presentation is null ||
            !presentation.Document.SemanticModel.ModelProfiles.IsAvailable(
                OrganizationalModelProfile.Id))
        {
            return Empty();
        }

        // Availability enables the organizational spatial model. View visibility controls
        // its decoration only; hiding it must not change coordinates or edit destinations.
        var decorationVisible = presentation.ModelProfileViewState.IsEffectivelyVisible(
            OrganizationalModelProfile.Id,
            presentation.Document.SemanticModel.ModelProfiles);
        var pools = OrganizationalSemantics.GetOrderedPoolsInScope(
            presentation.Document,
            presentation.ActiveScopeId);
        if (pools.IsEmpty)
        {
            return Empty();
        }

        var poolIds = pools.Select(static pool => pool.Id).ToHashSet();
        var nodes = context.ProjectedGraph.Nodes
            .Where(static node => node.Source.VisualStateId is not null)
            .OrderBy(static node => node.Id.Value, StringComparer.Ordinal)
            .ToArray();
        var assignmentByVisual = new Dictionary<VisualStateId, SemanticElementId?>();
        foreach (var node in nodes)
        {
            assignmentByVisual.Add(
                node.Source.VisualStateId!,
                ResolvePresentationPoolId(
                    presentation.Document,
                    presentation.ActiveScopeId,
                    node,
                    poolIds));
        }

        var canonicalBoundsByVisual = ResolveCanonicalBoundsByVisual(
            nodes,
            presentation.BaseSceneItems);
        var canonicalConnectorBounds = ResolveCanonicalConnectorBounds(
            context.ProjectedGraph,
            assignmentByVisual,
            presentation.BaseSceneItems);
        var contentByPool = pools.ToDictionary(
            static pool => pool.Id,
            pool => ResolveContentBounds(
                assignmentByVisual,
                canonicalBoundsByVisual,
                canonicalConnectorBounds,
                pool.Id));
        var unassignedBounds = ResolveContentBounds(
            assignmentByVisual,
            canonicalBoundsByVisual,
            canonicalConnectorBounds,
            poolId: null);
        var commonPoolWidth = pools.Max(pool => Math.Max(
            MinimumPoolWidth,
            DestinationContentRight(contentByPool[pool.Id]) + (ContentPadding * 2d) + HeaderWidth));
        var commonLeftOverflow = Math.Min(0d, pools.Min(pool => contentByPool[pool.Id].Left));

        var items = new List<Canvas2DSceneItem>();
        var regions = new List<Canvas2DSpatialRegion>();
        var placements = new List<Canvas2DSpatialVisualPlacement>();
        var cursorY = StackTop;
        foreach (var pool in pools)
        {
            var contentBounds = contentByPool[pool.Id];
            var isCollapsed = presentation.ModelProfileElementViewState.IsCollapsed(
                OrganizationalModelProfile.Id,
                pool.Id);
            var poolHeight = isCollapsed
                ? CollapsedPoolHeight
                : Math.Max(
                    MinimumPoolHeight,
                    DestinationContentBottom(contentBounds) + (ContentPadding * 2d));
            var poolBounds = new RectD(
                StackLeft,
                cursorY,
                commonPoolWidth,
                poolHeight);
            var contentRegionBounds = new RectD(
                poolBounds.Left + HeaderWidth,
                poolBounds.Top,
                poolBounds.Width - HeaderWidth,
                poolBounds.Height);
            // The canonical origin is fixed within the stack row. Content minima must not
            // rebase it: moving a leftmost/topmost node would otherwise cancel its displayed
            // move and translate every sibling after the correct canonical command commits.
            var translation = Matrix2D.CreateTranslation(
                contentRegionBounds.Left + ContentPadding,
                contentRegionBounds.Top + ContentPadding);
            var region = new Canvas2DSpatialRegion(
                PoolRegionId(pool.Id),
                OrganizationalModelProfile.Id,
                pool.Id,
                translation,
                contentRegionBounds);
            regions.Add(region);
            foreach (var visual in assignmentByVisual.Where(entry => entry.Value == pool.Id))
            {
                placements.Add(new Canvas2DSpatialVisualPlacement(
                    visual.Key,
                    region.Id,
                    isVisible: !isCollapsed));
            }

            items.Add(CreatePlacementExclusion(pool.Id, poolBounds, isCollapsed));
            if (decorationVisible)
            {
                var selected = context.EditorState.SemanticSceneSelection == pool.Id;
                items.AddRange(CreatePoolItems(
                    pool,
                    poolBounds,
                    ExpandDecorationBounds(poolBounds, commonLeftOverflow,
                        isCollapsed ? 0d : Math.Min(0d, contentBounds.Top)),
                    selected));
            }
            cursorY = poolBounds.Bottom + PoolGap;
        }

        // The explicit unassigned region remains available even while empty so a normal move can
        // remove a Pool assignment without inventing a selected-Pool fallback.
        {
            var unassignedWidth = Math.Max(
                commonPoolWidth - HeaderWidth,
                DestinationContentRight(unassignedBounds) + (ContentPadding * 2d));
            var unassignedRegionBounds = new RectD(
                StackLeft + HeaderWidth,
                cursorY,
                unassignedWidth,
                Math.Max(
                    MinimumPoolHeight,
                    DestinationContentBottom(unassignedBounds) + (ContentPadding * 2d)));
            var unassignedRegion = new Canvas2DSpatialRegion(
                UnassignedRegionId(presentation.ActiveScopeId),
                OrganizationalModelProfile.Id,
                containerSemanticElementId: null,
                Matrix2D.CreateTranslation(
                    unassignedRegionBounds.Left + ContentPadding,
                    unassignedRegionBounds.Top + ContentPadding),
                unassignedRegionBounds);
            regions.Add(unassignedRegion);
            foreach (var visual in assignmentByVisual.Where(static entry => entry.Value is null))
            {
                placements.Add(new Canvas2DSpatialVisualPlacement(
                    visual.Key,
                    unassignedRegion.Id));
            }

            if (decorationVisible)
            {
                items.Add(CreateUnassignedRegionItem(
                    presentation.ActiveScopeId,
                    ExpandDecorationBounds(unassignedRegionBounds,
                        Math.Min(0d, unassignedBounds.Left), Math.Min(0d, unassignedBounds.Top))));
            }
        }

        return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(
            items,
            spatialPresentationPlan: new Canvas2DSpatialPresentationPlan(
                regions,
                placements,
                Matrix2D.Identity)));
    }

    public Canvas2DConnectorPresentationRoute Route(
        Canvas2DConnectorPresentationRoutingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourceRegionId = request.SourceRegion?.Id;
        var targetRegionId = request.TargetRegion?.Id;
        var sameRegion = request.SourceRegion?.Id == request.TargetRegion?.Id;
        var displayedEditable = request.CanonicalEditablePath
            .Select((point, index) => index switch
            {
                0 => request.DisplayedSourceAnchor,
                _ when index == request.CanonicalEditablePath.Length - 1 =>
                    request.DisplayedTargetAnchor,
                _ => request.CanonicalGuidanceToSceneTransform.TransformPoint(point),
            })
            .ToImmutableArray();
        ImmutableArray<PointD> displayedLogical;
        if (sameRegion)
        {
            var transform = request.SourceRegion?.LocalToSceneTransform ?? Matrix2D.Identity;
            displayedLogical = request.CanonicalLogicalPath
                .Select(transform.TransformPoint)
                .ToImmutableArray();
        }
        else
        {
            // Automatic canonical routing bends are valid only in the unpartitioned Process
            // presentation. Across translated regions, retain only authored editable guidance;
            // the presentation router derives fresh orthogonal segments between those exact,
            // reversibly mapped waypoints and the final displayed endpoints.
            var internalPoints = displayedEditable
                .Skip(1)
                .SkipLast(1)
                .ToArray();
            displayedLogical = CreateCrossRegionPath(
                request.CanonicalLogicalPath,
                request.DisplayedSourceAnchor,
                request.DisplayedTargetAnchor,
                internalPoints,
                request.PresentedObstacles);
        }

        return new Canvas2DConnectorPresentationRoute(
            displayedLogical,
            new Canvas2DConnectorPresentationMapping(
                request.CanonicalEditablePath,
                displayedEditable,
                request.CanonicalGuidanceToSceneTransform,
                sourceRegionId,
                targetRegionId));
    }

    private SemanticElementId? ResolvePresentationPoolId(
        Contracts.Documents.DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        ProjectedNode node,
        HashSet<SemanticElementId> poolIds)
    {
        var semanticElementId = node.PlacementHint?.BoundaryAttachment?.AttachedToElementId ??
            node.Source.SemanticElementId;
        if (!document.SemanticModel.TryGetElement(semanticElementId, out var element) ||
            element is null ||
            document.SemanticModel.GetScope(element.Id).Id != activeScopeId ||
            (!OrganizationalSemantics.IsDirectlyAssignable(element, _eligibilityPolicy) &&
             node.PlacementHint?.BoundaryAttachment is null) ||
            !OrganizationalSemantics.TryGetAssignedPoolId(
                document.SemanticModel,
                semanticElementId,
                out var poolId) ||
            poolId is null ||
            !poolIds.Contains(poolId))
        {
            return null;
        }

        return poolId;
    }

    private static Dictionary<VisualStateId, RectD> ResolveCanonicalBoundsByVisual(
        IEnumerable<ProjectedNode> nodes,
        IEnumerable<Canvas2DSceneItem> baseSceneItems)
    {
        var result = new Dictionary<VisualStateId, RectD>();
        foreach (var node in nodes)
        {
            var visualStateId = node.Source.VisualStateId!;
            var bounds = baseSceneItems
                .Where(item =>
                    item.IsVisible &&
                    item.Origin.VisualStateId == visualStateId &&
                    item.Layer != Canvas2DSceneLayer.Connector &&
                    (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0)
                .Select(static item => item.Bounds)
                .ToArray();
            if (bounds.Length > 0)
            {
                result.Add(visualStateId, Union(bounds));
            }
        }

        return result;
    }

    private static ImmutableArray<RegionConnectorBounds> ResolveCanonicalConnectorBounds(
        ProjectedGraph graph,
        Dictionary<VisualStateId, SemanticElementId?> assignments,
        IEnumerable<Canvas2DSceneItem> baseSceneItems)
    {
        var nodesById = graph.Nodes
            .Where(static node => node.Source.VisualStateId is not null)
            .ToDictionary(static node => node.Id);
        var canonicalItems = baseSceneItems
            .Where(static item =>
                item.IsVisible &&
                (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0)
            .ToArray();
        var result = ImmutableArray.CreateBuilder<RegionConnectorBounds>();
        foreach (var edge in graph.Edges)
        {
            if (!nodesById.TryGetValue(edge.SourceNodeId, out var source) ||
                !nodesById.TryGetValue(edge.TargetNodeId, out var target) ||
                !assignments.TryGetValue(source.Source.VisualStateId!, out var sourcePoolId) ||
                !assignments.TryGetValue(target.Source.VisualStateId!, out var targetPoolId) ||
                sourcePoolId != targetPoolId)
            {
                continue;
            }

            var connectorId = Canvas2DSceneObjectIdentity.ForProjected(
                edge.Id,
                "connector");
            foreach (var item in canonicalItems.Where(item =>
                item.Layer == Canvas2DSceneLayer.Connector &&
                item.Id == connectorId))
            {
                result.Add(new RegionConnectorBounds(sourcePoolId, item.Bounds));
            }

            var labelIds = graph.Labels
                .Where(label => label.OwnerId == edge.Id)
                .Select(static label => label.Id)
                .ToHashSet();
            foreach (var item in canonicalItems.Where(item =>
                item.Layer == Canvas2DSceneLayer.Label &&
                item.Origin.ProjectedObjectId is not null &&
                labelIds.Contains(item.Origin.ProjectedObjectId)))
            {
                result.Add(new RegionConnectorBounds(sourcePoolId, item.Bounds));
            }
        }

        return result.ToImmutable();
    }

    private static RectD ResolveContentBounds(
        IReadOnlyDictionary<VisualStateId, SemanticElementId?> assignments,
        Dictionary<VisualStateId, RectD> canonicalBounds,
        ImmutableArray<RegionConnectorBounds> canonicalConnectorBounds,
        SemanticElementId? poolId)
    {
        var bounds = assignments
            .Where(entry => entry.Value == poolId && canonicalBounds.ContainsKey(entry.Key))
            .Select(entry => canonicalBounds[entry.Key])
            .Concat(canonicalConnectorBounds
                .Where(entry => entry.PoolId == poolId)
                .Select(static entry => entry.Bounds))
            .ToArray();
        return bounds.Length == 0
            ? new RectD(
                DefaultContentX,
                DefaultContentY,
                DefaultContentWidth,
                DefaultContentHeight)
            : Union(bounds);
    }

    // Empty and populated rows share the same minimum usable destination extent. Replacing
    // the empty fallback with a small first node must not remove free placement/drop space.
    // Actual content still controls growth and signed decorative overflow, never the origin.
    private static double DestinationContentRight(RectD contentBounds) =>
        Math.Max(DefaultContentX + DefaultContentWidth, contentBounds.Right);

    private static double DestinationContentBottom(RectD contentBounds) =>
        Math.Max(DefaultContentY + DefaultContentHeight, contentBounds.Bottom);

    private static RectD Union(IReadOnlyCollection<RectD> bounds) =>
        new(
            bounds.Min(static item => item.Left),
            bounds.Min(static item => item.Top),
            bounds.Max(static item => item.Right) - bounds.Min(static item => item.Left),
            bounds.Max(static item => item.Bottom) - bounds.Min(static item => item.Top));

    // Signed labels/manual routes may overflow the legal Process quadrant. Enclose them in
    // non-hittable decoration only; neither the canonical origin nor the disjoint destination
    // bands/header hit targets follow that overflow. Rendering does not clip content to a band.
    private static RectD ExpandDecorationBounds(RectD bounds, double leftOverflow, double topOverflow) =>
        new(bounds.Left + leftOverflow, bounds.Top + topOverflow,
            bounds.Width - leftOverflow, bounds.Height - topOverflow);

    private static IEnumerable<Canvas2DSceneItem> CreatePoolItems(
        Contracts.Semantics.SemanticElementSnapshot pool,
        RectD poolBounds,
        RectD decorationBounds,
        bool selected)
    {
        var headerBounds = new RectD(
            poolBounds.Left,
            poolBounds.Top,
            HeaderWidth,
            poolBounds.Height);
        yield return Item(
            pool.Id,
            "pool-background",
            Canvas2DSceneLayer.Background,
            0,
            Canvas2DSceneGeometry.Rectangle(decorationBounds),
            new Canvas2DSceneStyle(
                fill: "#f8fafc",
                stroke: selected ? "#2563eb" : "#64748b",
                strokeWidth: selected ? 2.5d : 1.5d,
                opacity: 0.72d),
            Canvas2DHitTestPolicy.None);
        yield return Item(
            pool.Id,
            "pool-header",
            Canvas2DSceneLayer.Background,
            20,
            Canvas2DSceneGeometry.Rectangle(headerBounds),
            new Canvas2DSceneStyle(
                fill: selected ? "#dbeafe" : "#eef2f7",
                stroke: selected ? "#2563eb" : "#64748b",
                strokeWidth: selected ? 2.5d : 1.5d),
            new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Bounds),
            metadata: [Canvas2DSemanticSceneInteractionMetadata.Enabled]);
        yield return Item(
            pool.Id,
            "header-separator",
            Canvas2DSceneLayer.Background,
            30,
            Canvas2DSceneGeometry.Path(
            [
                new PointD(headerBounds.Right, headerBounds.Top),
                new PointD(headerBounds.Right, headerBounds.Bottom),
            ]),
            new Canvas2DSceneStyle(
                stroke: selected ? "#2563eb" : "#64748b",
                strokeWidth: selected ? 2.5d : 1.5d),
            Canvas2DHitTestPolicy.None);

        var name = TryReadName(pool) ?? "Pool";
        var localWidth = Math.Max(1d, headerBounds.Height - 16d);
        var localHeight = Math.Max(1d, HeaderWidth - 10d);
        var center = new PointD(
            headerBounds.Left + (headerBounds.Width / 2d),
            headerBounds.Top + (headerBounds.Height / 2d));
        yield return Item(
            pool.Id,
            "pool-name",
            Canvas2DSceneLayer.Background,
            40,
            Canvas2DSceneGeometry.Text(
                new RectD(0d, 0d, localWidth, localHeight),
                name,
                new PointD(localWidth / 2d, localHeight / 2d),
                Canvas2DTextAlignment.Center,
                Canvas2DTextBaseline.Middle),
            new Canvas2DSceneStyle(fill: "#0f172a", fontSize: 13d),
            Canvas2DHitTestPolicy.None,
            new Matrix2D(
                0d,
                -1d,
                1d,
                0d,
                center.X - (localHeight / 2d),
                center.Y + (localWidth / 2d)));
    }

    private static Canvas2DSceneItem CreatePlacementExclusion(
        SemanticElementId poolId,
        RectD poolBounds,
        bool collapsed)
    {
        // Structural placement restrictions remain active with graphics hidden. This item
        // paints nothing and has no semantic identity or hit target: it is not a hidden header.
        var stableKey = $"{poolId.Value}:placement-exclusion";
        var bounds = collapsed
            ? poolBounds
            : new RectD(poolBounds.Left, poolBounds.Top, HeaderWidth, poolBounds.Height);
        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForExtension(Descriptor.ContributorId, stableKey),
            Canvas2DSceneLayer.Background,
            0,
            Canvas2DSceneGeometry.Rectangle(bounds),
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.RegisteredExtension,
                stableSourceKey: stableKey),
            hitTestPolicy: Canvas2DHitTestPolicy.None,
            metadata: [Canvas2DSemanticSceneInteractionMetadata.PlacementBlockedEntry]);
    }

    private static Canvas2DSceneItem CreateUnassignedRegionItem(
        DocumentScopeId scopeId,
        RectD bounds)
    {
        var stableKey = $"{scopeId.Value}:unassigned-region";
        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForExtension(Descriptor.ContributorId, stableKey),
            Canvas2DSceneLayer.Background,
            -10,
            Canvas2DSceneGeometry.Rectangle(bounds),
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.RegisteredExtension,
                stableSourceKey: stableKey),
            style: new Canvas2DSceneStyle(
                fill: "#ffffff",
                stroke: "#94a3b8",
                dashPattern: [6d, 4d],
                opacity: 0.55d),
            hitTestPolicy: Canvas2DHitTestPolicy.None);
    }

    private static Canvas2DSceneItem Item(
        SemanticElementId poolId,
        string localKey,
        Canvas2DSceneLayer layer,
        int zIndex,
        Canvas2DSceneGeometry geometry,
        Canvas2DSceneStyle style,
        Canvas2DHitTestPolicy hitTestPolicy,
        Matrix2D? transform = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? metadata = null)
    {
        var stableKey = $"{poolId.Value}:{localKey}";
        return new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForExtension(Descriptor.ContributorId, stableKey),
            layer,
            zIndex,
            geometry,
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.SemanticElement |
                Canvas2DSceneOriginCategory.RegisteredExtension,
                semanticElementId: poolId,
                stableSourceKey: stableKey),
            transform,
            style: style,
            hitTestPolicy: hitTestPolicy,
            metadata: metadata);
    }

    private static ImmutableArray<PointD> CreateCrossRegionPath(
        IReadOnlyList<PointD> canonicalLogicalPath,
        PointD source,
        PointD target,
        PointD[] internalPoints,
        IReadOnlyList<RectD> obstacles)
    {
        var result = new List<PointD> { source };
        var sourceStub = CreateEndpointStub(
            source,
            ResolveEndpointOutwardTangent(canonicalLogicalPath, sourceEndpoint: true),
            obstacles);
        var targetStub = CreateEndpointStub(
            target,
            ResolveEndpointOutwardTangent(canonicalLogicalPath, sourceEndpoint: false),
            obstacles);
        if (sourceStub != source)
        {
            result.Add(sourceStub);
        }

        foreach (var point in internalPoints)
        {
            AppendBestOrthogonalConnection(result, point, obstacles);
        }

        AppendBestOrthogonalConnection(result, targetStub, obstacles);
        if (result[^1] != target)
        {
            result.Add(target);
        }

        // Authored editable guidance must remain explicit vertices, including a waypoint that
        // happens to be collinear with adjacent presentation segments. Removing it would leave
        // its route handle detached from the final displayed path.
        return [.. RemoveConsecutiveDuplicates(result)];
    }

    private static void AppendBestOrthogonalConnection(
        List<PointD> points,
        PointD target,
        IReadOnlyList<RectD> obstacles)
    {
        var source = points[^1];
        if (source == target)
        {
            return;
        }

        var candidates = new List<IReadOnlyList<PointD>>();
        if (source.X == target.X || source.Y == target.Y)
        {
            candidates.Add([source, target]);
        }
        else
        {
            candidates.Add(RemoveConsecutiveDuplicates(
            [
                source,
                new PointD(target.X, source.Y),
                target,
            ]));
            candidates.Add(RemoveConsecutiveDuplicates(
            [
                source,
                new PointD(source.X, target.Y),
                target,
            ]));
        }

        foreach (var corridorX in obstacles
                     .SelectMany(static obstacle => new[]
                     {
                         obstacle.Left - ConnectorObstacleClearance,
                         obstacle.Right + ConnectorObstacleClearance,
                     })
                     .Distinct()
                     .Order())
        {
            candidates.Add(RemoveConsecutiveDuplicates(
            [
                source,
                new PointD(corridorX, source.Y),
                new PointD(corridorX, target.Y),
                target,
            ]));
        }

        foreach (var corridorY in obstacles
                     .SelectMany(static obstacle => new[]
                     {
                         obstacle.Top - ConnectorObstacleClearance,
                         obstacle.Bottom + ConnectorObstacleClearance,
                     })
                     .Distinct()
                     .Order())
        {
            candidates.Add(RemoveConsecutiveDuplicates(
            [
                source,
                new PointD(source.X, corridorY),
                new PointD(target.X, corridorY),
                target,
            ]));
        }

        var best = candidates[0];
        var bestIntersections = IntersectionScore(best, obstacles);
        var bestLength = ManhattanLength(best);
        foreach (var candidate in candidates.Skip(1))
        {
            var intersections = IntersectionScore(candidate, obstacles);
            var length = ManhattanLength(candidate);
            if (intersections < bestIntersections ||
                (intersections == bestIntersections && length < bestLength))
            {
                best = candidate;
                bestIntersections = intersections;
                bestLength = length;
            }
        }

        points.AddRange(best.Skip(1));
    }

    private static PointD CreateEndpointStub(
        PointD anchor,
        VectorD canonicalOutwardTangent,
        IReadOnlyList<RectD> obstacles)
    {
        var directions = new List<VectorD>();
        foreach (var obstacle in obstacles)
        {
            if (IsBetween(anchor.Y, obstacle.Top, obstacle.Bottom))
            {
                if (NearlyEqual(anchor.X, obstacle.Left))
                {
                    AddDirection(directions, new VectorD(-1d, 0d));
                }

                if (NearlyEqual(anchor.X, obstacle.Right))
                {
                    AddDirection(directions, new VectorD(1d, 0d));
                }
            }

            if (IsBetween(anchor.X, obstacle.Left, obstacle.Right))
            {
                if (NearlyEqual(anchor.Y, obstacle.Top))
                {
                    AddDirection(directions, new VectorD(0d, -1d));
                }

                if (NearlyEqual(anchor.Y, obstacle.Bottom))
                {
                    AddDirection(directions, new VectorD(0d, 1d));
                }
            }
        }

        if (directions.Count == 0)
        {
            return anchor;
        }

        var direction = directions[0];
        var bestAlignment = Dot(direction, canonicalOutwardTangent);
        foreach (var candidate in directions.Skip(1))
        {
            var alignment = Dot(candidate, canonicalOutwardTangent);
            if (alignment > bestAlignment)
            {
                direction = candidate;
                bestAlignment = alignment;
            }
        }

        return anchor + (direction * ConnectorObstacleClearance);
    }

    private static VectorD ResolveEndpointOutwardTangent(
        IReadOnlyList<PointD> path,
        bool sourceEndpoint)
    {
        var endpoint = sourceEndpoint ? path[0] : path[^1];
        for (var offset = 1; offset < path.Count; offset++)
        {
            var candidate = sourceEndpoint ? path[offset] : path[^(offset + 1)];
            var tangent = candidate - endpoint;
            if (tangent.X != 0d || tangent.Y != 0d)
            {
                return tangent;
            }
        }

        return default;
    }

    private static int IntersectionScore(
        IReadOnlyList<PointD> path,
        IReadOnlyList<RectD> obstacles) =>
        obstacles.Count(obstacle =>
            path.Zip(path.Skip(1)).Any(segment =>
                SegmentIntersectsInterior(segment.First, segment.Second, obstacle)));

    private static double ManhattanLength(IReadOnlyList<PointD> path) =>
        path.Zip(path.Skip(1)).Sum(segment =>
            Math.Abs(segment.Second.X - segment.First.X) +
            Math.Abs(segment.Second.Y - segment.First.Y));

    private static void AddDirection(List<VectorD> directions, VectorD direction)
    {
        if (!directions.Contains(direction))
        {
            directions.Add(direction);
        }
    }

    private static bool IsBetween(double value, double minimum, double maximum) =>
        value >= minimum - 0.000001d && value <= maximum + 0.000001d;

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= 0.000001d;

    private static double Dot(VectorD left, VectorD right) =>
        (left.X * right.X) + (left.Y * right.Y);

    private static bool SegmentIntersectsInterior(PointD start, PointD end, RectD obstacle)
    {
        if (start.X == end.X)
        {
            return start.X > obstacle.Left && start.X < obstacle.Right &&
                Math.Max(start.Y, end.Y) > obstacle.Top &&
                Math.Min(start.Y, end.Y) < obstacle.Bottom;
        }

        return start.Y > obstacle.Top && start.Y < obstacle.Bottom &&
            Math.Max(start.X, end.X) > obstacle.Left &&
            Math.Min(start.X, end.X) < obstacle.Right;
    }

    private static List<PointD> RemoveConsecutiveDuplicates(IReadOnlyList<PointD> points)
    {
        var result = new List<PointD>();
        foreach (var point in points)
        {
            if (result.Count == 0 || result[^1] != point)
            {
                result.Add(point);
            }
        }

        return result;
    }

    private static Canvas2DSpatialRegionId PoolRegionId(SemanticElementId poolId) =>
        new($"inceptus:organizational/pool-region:{poolId.Value}");

    private static Canvas2DSpatialRegionId UnassignedRegionId(DocumentScopeId scopeId) =>
        new($"inceptus:organizational/unassigned-region:{scopeId.Value}");

    private static string? TryReadName(Contracts.Semantics.SemanticElementSnapshot pool) =>
        pool.Properties.TryGetValue(OrganizationalSemanticProperties.Name, out var name) &&
        name.Kind == PropertyValueKind.Text &&
        !string.IsNullOrWhiteSpace(name.TextValue)
            ? name.TextValue
            : null;

    private readonly record struct RegionConnectorBounds(
        SemanticElementId? PoolId,
        RectD Bounds);

    private static Canvas2DSceneContributionResult Empty() =>
        Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution());
}
