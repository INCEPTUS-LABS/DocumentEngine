using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.Organizational.Scene;

public sealed partial class OrganizationalPoolSceneContributor
{
    private static Canvas2DSceneContributionResult ContributeSavedGeometry(
        Canvas2DSceneContributionContext context,
        ScopeGeometrySnapshot geometry,
        ImmutableArray<SemanticElementSnapshot> pools,
        bool decorationVisible)
    {
        var presentation = context.Presentation!;
        var poolsById = pools.ToDictionary(static pool => pool.Id);
        var savedRegions = geometry.Regions
            .Where(static region => region.ProfileId == OrganizationalModelProfile.Id && region.IsActive)
            .OrderBy(static region => region.ContentBounds!.Value.Top).ToArray();
        var unassignedId = UnassignedRegionId(presentation.ActiveScopeId);
        if (savedRegions.Length != pools.Length + 1 ||
            !savedRegions.Any(region => region.Id == unassignedId) ||
            pools.Any(pool => !savedRegions.Any(region => region.Id == PoolRegionId(pool.Id))))
            return SavedGeometryFailure("The accepted spatial geometry does not cover the current Pool partition.");

        var items = new List<Canvas2DSceneItem>();
        var regions = new List<Canvas2DSpatialRegion>();
        var bands = new List<Canvas2DSpatialCoordinateBand>();
        var hiddenRegions = new HashSet<Canvas2DSpatialRegionId>();
        double? previousLogicalBottom = null;
        double? previousDisplayedBottom = null;
        foreach (var saved in savedRegions)
        {
            var logicalBounds = saved.ContentBounds!.Value;
            var localToScope = saved.LocalToScopeTransform!.Value;
            var height = saved.ExpandedHeight;
            var collapsed = saved.ContainerSemanticElementId is { } id &&
                presentation.ModelProfileElementViewState.IsCollapsed(OrganizationalModelProfile.Id, id);
            ScopeTextMeasurementSnapshot? nameMeasurement = null;
            if (saved.ContainerSemanticElementId is { } poolId)
            {
                var request = PoolNameRequest(poolsById[poolId], geometry.TextConfiguration);
                nameMeasurement = geometry.TextMeasurements.FirstOrDefault(measured => measured.Request.Equals(request));
                if (nameMeasurement is null)
                    return SavedGeometryFailure("The accepted geometry has no compatible measured Pool-name typography.");
                if (collapsed)
                {
                    height = CompactHeight(nameMeasurement.Metrics);
                    if (height > saved.ExpandedHeight)
                        return SavedGeometryFailure("The expanded Pool height cannot contain its compact name row.");
                    hiddenRegions.Add(saved.Id);
                }
            }

            var displayedTop = previousDisplayedBottom is { } bottom
                ? bottom + (logicalBounds.Top - previousLogicalBottom!.Value) : logicalBounds.Top;
            var offsetY = displayedTop - logicalBounds.Top;
            var bounds = new RectD(logicalBounds.Left, displayedTop, logicalBounds.Width, height);
            bands.Add(new Canvas2DSpatialCoordinateBand(saved.Id, logicalBounds.Top, logicalBounds.Bottom,
                bounds.Top, bounds.Bottom));
            var displayed = new Canvas2DSpatialRegion(saved.Id, saved.ProfileId, saved.ContainerSemanticElementId,
                Matrix2D.CreateTranslation(localToScope.OffsetX, localToScope.OffsetY + offsetY), bounds);
            regions.Add(displayed);
            if (saved.ContainerSemanticElementId is { } poolSemanticId)
            {
                var pool = poolsById[poolSemanticId];
                var rowBounds = new RectD(bounds.Left - HeaderWidth, bounds.Top, bounds.Width + HeaderWidth, bounds.Height);
                items.Add(CreatePlacementExclusion(poolSemanticId, rowBounds, collapsed));
                if (decorationVisible)
                {
                    var selected = context.EditorState.SemanticSceneSelection == poolSemanticId;
                    items.AddRange(collapsed
                        ? CreateCompactPoolItems(pool, rowBounds, nameMeasurement!, selected)
                        : CreatePoolItems(pool, rowBounds, rowBounds, selected));
                }
            }
            else if (decorationVisible)
            {
                items.Add(CreateUnassignedRegionItem(presentation.ActiveScopeId, bounds));
            }
            previousLogicalBottom = logicalBounds.Bottom;
            previousDisplayedBottom = bounds.Bottom;
        }

        var regionIds = regions.Select(static region => region.Id).ToHashSet();
        var placements = geometry.Nodes.Where(node => node.RegionId is not null && regionIds.Contains(node.RegionId))
            .Select(node => new Canvas2DSpatialVisualPlacement(node.VisualStateId, node.RegionId!,
                isVisible: !hiddenRegions.Contains(node.RegionId!)));
        return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution(items,
            spatialPresentationPlan: new Canvas2DSpatialPresentationPlan(regions, placements, Matrix2D.Identity,
                new Canvas2DSpatialCoordinateMap(bands), unassignedId, decorationVisible
                    ? CreateResizeTargets(geometry, pools, regions, hiddenRegions, presentation.ActiveScopeId) : [])));
    }

    private static IEnumerable<Canvas2DSceneItem> CreateCompactPoolItems(
        SemanticElementSnapshot pool,
        RectD bounds,
        ScopeTextMeasurementSnapshot nameMeasurement,
        bool selected)
    {
        yield return Item(pool.Id, "pool-background", Canvas2DSceneLayer.Background, 0,
            Canvas2DSceneGeometry.Rectangle(bounds),
            new Canvas2DSceneStyle(fill: "#f8fafc", stroke: selected ? "#2563eb" : "#64748b",
                strokeWidth: selected ? 2.5d : 1.5d, opacity: 0.72d), Canvas2DHitTestPolicy.None);
        yield return Item(pool.Id, "pool-header", Canvas2DSceneLayer.Background, 20,
            Canvas2DSceneGeometry.Rectangle(bounds),
            new Canvas2DSceneStyle(fill: selected ? "#dbeafe" : "#eef2f7",
                stroke: selected ? "#2563eb" : "#64748b", strokeWidth: selected ? 2.5d : 1.5d),
            new Canvas2DHitTestPolicy(Canvas2DHitTestMode.Bounds),
            metadata: [Canvas2DSemanticSceneInteractionMetadata.Enabled]);
        // Match existing unwrapped text overflow. The measured name controls the one line's
        // height only; a long name neither widens the authored frame nor gains another line.
        var textBounds = new RectD(bounds.Left + PoolNameHorizontalInset, bounds.Top + PoolNameVerticalInset,
            nameMeasurement.Metrics.Width, nameMeasurement.Metrics.LineHeight);
        yield return Item(pool.Id, "pool-name", Canvas2DSceneLayer.Background, 40,
            Canvas2DSceneGeometry.Text(textBounds, nameMeasurement.Request.Text, textBounds.TopLeft,
                Canvas2DTextAlignment.Start, Canvas2DTextBaseline.Top),
            new Canvas2DSceneStyle(fill: "#0f172a", fontFamily: nameMeasurement.Request.FontFamily,
                fontSize: nameMeasurement.Request.FontSize), Canvas2DHitTestPolicy.None);
    }

    private static Canvas2DSceneContributionResult SavedGeometryFailure(string message) =>
        Canvas2DSceneContributionResult.Failure([new Diagnostic(
            "organizational.scene.incompatible-saved-geometry", DiagnosticSeverity.Error, message)]);
}
