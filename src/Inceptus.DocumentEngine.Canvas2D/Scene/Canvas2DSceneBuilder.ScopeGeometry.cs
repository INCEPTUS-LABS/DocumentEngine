using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    internal async ValueTask<ScopeGeometrySnapshot> PrepareScopeGeometryAsync(
        ScopeGeometryInputs inputs, ProjectedGraph graph, LayoutResult layout,
        VisualModelSnapshot visualModel, ScopeGeometrySnapshot? previous,
        IReadOnlyDictionary<Canvas2DSpatialRegionId, double> requestedHeights,
        IReadOnlyDictionary<ModelProfileId, double> requestedWidths,
        ITextMetricsService textMetrics,
        Func<string, Canvas2DSceneStyle, double, TextMeasurementRequest> requestFactory,
        CancellationToken cancellationToken)
    {
        var textConfiguration = requestFactory(string.Empty, NodeLabelStyle,
            NodeLabelStyle.FontSize * _nodeLabelLayout.LineHeightMultiplier);
        var labelService = new Canvas2DTextLayoutService(textMetrics, requestFactory);
        var labels = await CreateMeasuredNodeLabelLayoutsAsync(graph, layout, visualModel,
            EditorStateSnapshot.Empty, labelService, cancellationToken, previous).ConfigureAwait(false)
            ?? throw new ScopeGeometryPreparationException(labelService.Diagnostics);
        var captions = labels.Values.Select(label =>
        {
            NodeLabelVisualOverride? visualOverride = null;
            var owner = label.Label.Source.VisualStateId
                ?? throw new InvalidOperationException("Saved captions require a Visual State owner.");
            if (visualModel.TryGetVisualState(owner, out var visual))
                NodeLabelVisualOverride.TryRead(visual!.Properties, out visualOverride);
            return new ScopeNodeCaptionSnapshot(owner, label.Label.Id,
                label.Label.NodePlacement ?? NodeLabelPlacement.InsideCentered, visualOverride,
                label.Current.PlacementBounds, label.Current.ClipBounds, label.Current.OwnerTransform,
                label.Current.TextLayout.Lines.Select(line => new ScopeTextMeasurementSnapshot(
                    requestFactory(line.Text, NodeLabelStyle,
                        NodeLabelStyle.FontSize * _nodeLabelLayout.LineHeightMultiplier), line.Metrics)));
        }).ToArray();
        var registrations = _contributors.Registrations.Where(static registration =>
            registration.Descriptor.PlacementDependency != Canvas2DScenePlacementDependency.BoundedFeedbackOnly).ToArray();
        var textValues = new Dictionary<TextMeasurementRequest, TextMetrics>();
        foreach (var caption in captions)
            foreach (var line in caption.Lines)
                textValues.TryAdd(line.Request, line.Metrics);
        var regions = new List<SpatialRegionGeometrySnapshot>();
        var widths = new List<SpatialScopeWidthSnapshot>();
        Canvas2DSpatialPresentationPlan? spatialPlan = null;
        var bases = new List<(ICanvas2DScopeGeometryContributor Provider, Canvas2DScopeGeometryBaseContext Context)>();
        var measured = new Dictionary<SceneObjectId, TextMetrics>();
        var overrides = new Dictionary<SceneObjectId, Canvas2DCanonicalSceneItemVisualOverride>();
        var itemIds = new HashSet<SceneObjectId>();
        var canonicalNodeIds = graph.Nodes.Select(static node =>
            Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node")).ToHashSet();
        foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (registration.Contributor is not ICanvas2DScopeGeometryContributor provider)
                throw new InvalidOperationException($"Contributor '{registration.Descriptor.ContributorId}' does not support saved scope geometry.");
            var context = new Canvas2DScopeGeometryBaseContext(inputs, graph, layout,
                _configuration, registration.Descriptor, textConfiguration);
            var baseResult = provider.PrepareBase(context);
            if (!baseResult.Succeeded)
                throw new InvalidOperationException(string.Join(" ", baseResult.Diagnostics.Select(static item => item.Message)));
            bases.Add((provider, context));
            foreach (var item in baseResult.Contribution!.Items)
                if (!itemIds.Add(item.Id)) throw new InvalidOperationException("Scope contributors produced duplicate item identities.");
            foreach (var replacement in baseResult.Contribution.CanonicalItemVisualOverrides)
            {
                if (!canonicalNodeIds.Contains(replacement.TargetSceneObjectId))
                    throw new InvalidOperationException("Scope preparation can override only current canonical node appearance.");
                if (!overrides.TryAdd(replacement.TargetSceneObjectId, replacement))
                    throw new InvalidOperationException("Scope contributors produced competing node appearance overrides.");
            }
            foreach (var request in baseResult.TextRequests.OrderBy(static item => item.Key.Value, StringComparer.Ordinal))
            {
                if (!textValues.TryGetValue(request.Value, out var metrics))
                {
                    var result = await textMetrics.MeasureAsync(request.Value, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    metrics = result.Metrics ?? throw new ScopeGeometryPreparationException(result.Diagnostics);
                    textValues.Add(request.Value, metrics);
                }
                measured.Add(request.Key, metrics);
            }
        }
        var layouts = layout.Nodes.ToDictionary(static node => node.ProjectedObjectId);
        var captionsByVisual = captions.GroupBy(static caption => caption.OwnerVisualStateId)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        var nodeExtents = graph.Nodes.Select(node =>
        {
            var value = layouts[node.Id];
            var id = Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node");
            var shape = overrides.GetValueOrDefault(id)?.Geometry ?? Canvas2DSceneGeometry.Rectangle(
                new RectD(0, 0, value.Bounds.Width, value.Bounds.Height));
            var body = new Canvas2DSceneItem(id, Canvas2DSceneLayer.Content, 0, shape, CreateOrigin(node), value.Transform);
            // Canonical appearance replacement cannot introduce a second geometry authority.
            if (Math.Abs(body.Bounds.Left - value.Bounds.Left) > 1e-9 ||
                Math.Abs(body.Bounds.Top - value.Bounds.Top) > 1e-9 ||
                Math.Abs(body.Bounds.Right - value.Bounds.Right) > 1e-9 ||
                Math.Abs(body.Bounds.Bottom - value.Bounds.Bottom) > 1e-9)
                throw new InvalidOperationException("The node appearance's transformed bounds do not match the saved layout basis.");
            var visualId = node.Source.VisualStateId
                ?? throw new InvalidOperationException("Saved node geometry requires a Visual State owner.");
            RectD? captionBounds = null;
            foreach (var caption in captionsByVisual.GetValueOrDefault(visualId) ?? [])
                captionBounds = captionBounds is { } bounds
                    ? new RectD(Math.Min(bounds.Left, caption.PlacementBounds.Left), Math.Min(bounds.Top, caption.PlacementBounds.Top),
                        Math.Max(bounds.Right, caption.PlacementBounds.Right) - Math.Min(bounds.Left, caption.PlacementBounds.Left),
                        Math.Max(bounds.Bottom, caption.PlacementBounds.Bottom) - Math.Min(bounds.Top, caption.PlacementBounds.Top))
                    : caption.PlacementBounds;
            return new Canvas2DScopeGeometryNodeBounds(visualId, value.Bounds, captionBounds);
        }).ToArray();
        foreach (var (provider, context) in bases)
        {
            var presentation = provider.PreparePresentation(new Canvas2DScopeGeometryPresentationContext(
                context, nodeExtents, measured, previous?.Regions ?? [], requestedHeights,
                previous?.SpatialWidths ?? [], requestedWidths));
            if (!presentation.Succeeded)
                throw new InvalidOperationException(string.Join(" ", presentation.Diagnostics.Select(static item => item.Message)));
            regions.AddRange(presentation.Regions);
            widths.AddRange(presentation.SpatialWidths);
            if (presentation.Plan is not null)
            {
                if (spatialPlan is not null)
                    throw new InvalidOperationException("A scope cannot have competing spatial geometry providers.");
                spatialPlan = presentation.Plan;
            }
        }
        var placements = spatialPlan?.VisualPlacements.ToDictionary(static placement => placement.VisualStateId);
        if (placements is not null)
        {
            var expectedIds = graph.Nodes.Select(static node => node.Source.VisualStateId!).ToHashSet();
            var activeRegions = regions.Where(static region => region.LocalToScopeTransform is not null)
                .Select(static region => region.Id).ToHashSet();
            if (!expectedIds.SetEquals(placements.Keys) || placements.Values.Any(placement => !activeRegions.Contains(placement.RegionId)))
                throw new InvalidOperationException("The spatial provider must place every current node in exactly one active region.");
        }
        else if (regions.Any(static region => region.LocalToScopeTransform is not null))
            throw new InvalidOperationException("Active saved regions require a complete spatial presentation plan.");
        var nodes = graph.Nodes.Select(node =>
        {
            var current = layouts[node.Id];
            var visualId = node.Source.VisualStateId!;
            var region = placements?.GetValueOrDefault(visualId)?.RegionId;
            return new ScopeNodeGeometrySnapshot(visualId, current.Bounds, current.Transform, region);
        });
        if (requestedWidths.Any(request => !widths.Any(width =>
                width.ProfileId == request.Key && width.OuterWidth == request.Value)))
            throw new InvalidOperationException("A requested spatial width has no matching provider result.");
        if (widths.Any(width => !regions.Any(region => region.ProfileId == width.ProfileId)))
            throw new InvalidOperationException("A spatial width must belong to a contributed region profile.");
        return new ScopeGeometrySnapshot("inceptus:scope-geometry", "2", layout.AlgorithmId,
            _configuration, textConfiguration,
            registrations.Select(static registration => new ScopeGeometryContributorSnapshot(
                SavedGeometryDescriptor(registration.Descriptor), registration.Stage)),
            nodes, regions, captions, textValues.Select(static pair => new ScopeTextMeasurementSnapshot(pair.Key, pair.Value)), widths);
    }

    // Label/route-drag reuse is a runtime capability, not a change to saved geometry provenance
    // or the native v2 format. Existing saved contributor signatures remain exact.
    private static Canvas2DSceneContributorDescriptor SavedGeometryDescriptor(Canvas2DSceneContributorDescriptor value) =>
        new(value.ContributorId, value.Version, value.PanDependency, value.MoveGestureDependency,
            value.HoverDependency, value.VisualSelectionDependency, value.PlacementDependency,
            value.RegionResizeDependency);

    private static Dictionary<ProjectedObjectId, Canvas2DMeasuredNodeLabel> RestoreSavedNodeLabels(
        ProjectedGraph graph, ScopeGeometrySnapshot geometry)
    {
        var captions = geometry.Captions.ToDictionary(static caption => caption.LabelId);
        return graph.Labels.Where(label => captions.ContainsKey(label.Id)).ToDictionary(
            static label => label.Id, label =>
            {
                var saved = captions[label.Id];
                return new Canvas2DMeasuredNodeLabel(label,
                    new Canvas2DMeasuredLabelBox(saved.PlacementBounds, saved.ContentBounds, saved.Transform,
                        new Canvas2DTextLayout([.. saved.Lines.Select(static line =>
                            new Canvas2DTextLayoutLine(line.Request.Text, line.Metrics))])),
                    null, null, saved.VisualOverride is not null);
            });
    }
}
